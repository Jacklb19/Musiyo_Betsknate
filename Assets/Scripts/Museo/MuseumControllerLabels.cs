using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>One small label by one control of a Quest controller, read from <c>Resources/MuseumControllerLabels.json</c>.</summary>
    [Preserve]
    public sealed class ControllerLabelSettings
    {
        [JsonProperty("id")] public string Id;
        /// <summary><c>left</c> or <c>right</c>.</summary>
        [JsonProperty("hand")] public string Hand;
        /// <summary>The input action the control triggers, as Map/Action; a test checks that it is really bound to <see cref="Control"/>.</summary>
        [JsonProperty("action")] public string Action;
        [JsonProperty("control")] public string Control;
        [JsonProperty("text_key")] public string TextKey;
        /// <summary>Where the control is on the controller, in metres in the controller's pointing frame (x right, y up, z forward).</summary>
        [JsonProperty("button")] public float[] Button = new float[3];
        /// <summary>Where its label stands, in the same frame: beside the controller, on its outer side.</summary>
        [JsonProperty("label")] public float[] Label = new float[3];

        public bool Right => Hand == "right";
        public Vector3 ButtonOffset => new Vector3(Button[0], Button[1], Button[2]);
        public Vector3 LabelOffset => new Vector3(Label[0], Label[1], Label[2]);
    }

    [Preserve]
    public sealed class MuseumControllerLabelSettings
    {
        public const string ResourceName = "MuseumControllerLabels";
        [JsonProperty("schema_version")] public int SchemaVersion;
        /// <summary>Looking within this angle of a controller, close enough, shows its labels; beyond <see cref="HideAngle"/> they go.</summary>
        [JsonProperty("look_angle_degrees")] public float LookAngle = 26;
        [JsonProperty("hide_angle_degrees")] public float HideAngle = 36;
        [JsonProperty("look_distance_m")] public float LookDistance = .75f;
        [JsonProperty("fade_seconds")] public float FadeSeconds = .2f;
        /// <summary>Text height in millimetres.</summary>
        [JsonProperty("text_size_mm")] public float TextSize = 11;
        [JsonProperty("labels")] public List<ControllerLabelSettings> Labels = new();

        private static MuseumControllerLabelSettings current;
        public static MuseumControllerLabelSettings Current
        {
            get
            {
                if (current != null) return current;
                var resource = Resources.Load<TextAsset>(ResourceName);
                if (resource == null) throw new InvalidOperationException("Missing controller label settings.");
                return current = Parse(resource.text);
            }
        }

        public static MuseumControllerLabelSettings Parse(string json)
        {
            var result = JsonConvert.DeserializeObject<MuseumControllerLabelSettings>(json);
            if (result == null || result.SchemaVersion != 1) throw new InvalidOperationException("Invalid controller label settings.");
            foreach (var value in new[] { result.LookAngle, result.HideAngle, result.LookDistance, result.FadeSeconds, result.TextSize })
                if (!MuseumExperienceConfiguration.Finite(value) || value <= 0) throw new InvalidOperationException("Invalid controller label settings.");
            result.Labels ??= new();
            if (result.HideAngle < result.LookAngle || result.Labels.Count == 0
                || result.Labels.Any(label => label == null || string.IsNullOrWhiteSpace(label.Id) || label.Hand != "left" && label.Hand != "right"
                    || string.IsNullOrWhiteSpace(label.Action) || string.IsNullOrWhiteSpace(label.Control) || string.IsNullOrWhiteSpace(label.TextKey)
                    || label.Button == null || label.Button.Length != 3 || label.Label == null || label.Label.Length != 3
                    || label.Button.Concat(label.Label).Any(value => !MuseumExperienceConfiguration.Finite(value) || Mathf.Abs(value) > .3f))
                || result.Labels.Select(label => label.Hand + label.Id).Distinct().Count() != result.Labels.Count)
                throw new InvalidOperationException("Invalid controller labels.");
            return result;
        }
    }

    /// <summary>
    /// Small labels beside the controls of the visitor's Quest controllers (owner, 28/10/2026): «Menú», «Hablar con el
    /// colibrí», «Tomar» and the like, each with a thin line to its control. They show during the first-visit tutorial and
    /// whenever the visitor looks at a controller in their hand, and fade otherwise. The visitor can turn them off in the
    /// settings. Only a headset shows them; a hand without its controller has none. They take no input.
    /// </summary>
    public sealed class MuseumControllerLabels : MonoBehaviour
    {
        /// <summary>A controller pose in the space of <see cref="Space"/>: tracked or not, where it is and how it points.</summary>
        public delegate (bool tracked, Vector3 position, Quaternion rotation) PoseSource(bool right);

        private sealed class Hand
        {
            public RectTransform Canvas;
            public CanvasGroup Group;
            public bool Looking;
            public readonly List<(ControllerLabelSettings settings, RectTransform pill, TMP_Text text, RectTransform leader, RectTransform dot)> Labels = new();
        }

        private MuseumControllerLabelSettings settings;
        private MuseumMenuHud menu;
        private MuseumXrHost host;
        private readonly Hand[] hands = new Hand[2];

        /// <summary>Where the poses come from; the headset's controllers unless a test gives others.</summary>
        public PoseSource Poses { get; set; }
        /// <summary>The space the poses are given in; the headset's tracking space unless a test gives another (null is the world).</summary>
        public Transform Space { get; set; }
        /// <summary>The eye the labels turn to; the headset's camera unless a test gives another.</summary>
        public Camera Head { get; set; }
        /// <summary>Shows the labels whatever the visitor looks at, as during the tutorial; for the review pictures.</summary>
        public bool ForceShow { get; set; }
        /// <summary>The labels of a hand are showing.</summary>
        public bool Showing(bool right) => hands[right ? 1 : 0] != null && hands[right ? 1 : 0].Group.alpha > .5f;
        public IEnumerable<string> Texts(bool right) => hands[right ? 1 : 0]?.Labels.Select(label => label.text.text) ?? Enumerable.Empty<string>();

        public static MuseumControllerLabels Ensure(MuseumMenuHud menu, MuseumInteraction interaction)
        {
            if (interaction == null) return null;
            var labels = interaction.GetComponent<MuseumControllerLabels>();
            if (labels == null) labels = interaction.gameObject.AddComponent<MuseumControllerLabels>();
            labels.menu = menu;
            return labels;
        }

        private void Awake() => settings = MuseumControllerLabelSettings.Current;

        private void LateUpdate()
        {
            bool spatial = Poses != null || MuseumInput.Current.Spatial;
            if (Poses == null)
            {
                if (host == null) host = FindAnyObjectByType<MuseumXrHost>();
                if (!spatial || host == null || !host.Ready) { Hide(); return; }
            }
            var eye = Head != null ? Head : host != null && host.Origin != null ? host.Origin.Camera : null;
            if (eye == null) { Hide(); return; }
            bool allowed = menu == null || menu.Preferences == null || menu.Preferences.ControllerLabels;
            for (int side = 0; side < 2; side++)
            {
                bool right = side == 1;
                var (tracked, position, rotation) = Poses != null ? Poses(right) : HeadsetPose(right);
                var space = Poses != null ? Space : host.TrackingOrigin;
                if (space != null) { position = space.TransformPoint(position); rotation = space.rotation * rotation; }
                var hand = hands[side] ??= Build(right);
                if (hand == null) continue;
                var toController = position - eye.transform.position;
                float angle = Vector3.Angle(eye.transform.forward, toController);
                bool near = toController.magnitude < settings.LookDistance;
                hand.Looking = near && angle < (hand.Looking ? settings.HideAngle : settings.LookAngle);
                bool shown = allowed && tracked && (ForceShow || MuseumFirstVisitTutorial.Active || hand.Looking);
                hand.Group.alpha = Mathf.MoveTowards(hand.Group.alpha, shown ? 1 : 0, Time.unscaledDeltaTime / settings.FadeSeconds);
                bool visible = hand.Group.alpha > 0;
                if (hand.Canvas.gameObject.activeSelf != visible) hand.Canvas.gameObject.SetActive(visible);
                if (visible) Place(hand, position, rotation, eye.transform.position);
            }
        }

        private void Hide()
        {
            foreach (var hand in hands)
                if (hand != null) { hand.Group.alpha = 0; hand.Canvas.gameObject.SetActive(false); }
        }

        private static (bool, Vector3, Quaternion) HeadsetPose(bool right)
        {
            var input = MuseumInput.Current;
            return right
                ? (input.RightTracked.IsPressed(), input.RightPosition.ReadValue<Vector3>(), input.RightRotation.ReadValue<Quaternion>())
                : (input.LeftTracked.IsPressed(), input.LeftPosition.ReadValue<Vector3>(), input.LeftRotation.ReadValue<Quaternion>());
        }

        /// <summary>
        /// The labels of one hand on a small canvas of their own that faces the eye: a pill of text for each control and a
        /// thin line from it to a dot on the control.
        /// </summary>
        private Hand Build(bool right)
        {
            var wanted = settings.Labels.Where(label => label.Right == right).ToList();
            if (wanted.Count == 0) return null;
            var holder = new GameObject(right ? "RightControllerLabels" : "LeftControllerLabels", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            holder.transform.SetParent(transform, false);
            var canvas = holder.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 5;
            var rect = (RectTransform)holder.transform;
            rect.sizeDelta = new Vector2(400, 400);
            rect.localScale = Vector3.one * MuseumWorldUi.Millimetre;
            var hand = new Hand { Canvas = rect, Group = holder.GetComponent<CanvasGroup>() };
            hand.Group.alpha = 0;
            hand.Group.interactable = hand.Group.blocksRaycasts = false;
            var panel = MuseumWorldUi.Panel;
            panel.a = .9f;
            var accent = MuseumWorldUi.Accent;
            foreach (var label in wanted)
            {
                var leader = MuseumWorldUi.Surface("Leader_" + label.Id, rect, new Color(accent.r, accent.g, accent.b, .8f)).rectTransform;
                leader.pivot = new Vector2(0, .5f);
                var dot = MuseumWorldUi.Surface("Dot_" + label.Id, rect, accent);
                dot.sprite = MuseumUiSprites.Dot(32);
                dot.rectTransform.sizeDelta = new Vector2(5, 5);
                var pill = MuseumWorldUi.Surface("Label_" + label.Id, rect, panel);
                pill.sprite = MuseumUiSprites.Rounded(12);
                pill.type = Image.Type.Sliced;
                pill.rectTransform.pivot = new Vector2(right ? 0 : 1, .5f);
                var text = MuseumWorldUi.Label("Text", pill.transform, MuseumInterfaceText.Get(label.TextKey), settings.TextSize, MuseumWorldUi.Text);
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.alignment = TextAlignmentOptions.Center;
                MuseumWorldUi.Fill(text.rectTransform);
                pill.rectTransform.sizeDelta = new Vector2(text.preferredWidth + 12, settings.TextSize + 7);
                hand.Labels.Add((label, pill.rectTransform, text, leader, dot.rectTransform));
            }
            holder.SetActive(false);
            return hand;
        }

        /// <summary>
        /// Puts the hand's canvas at the controller, facing the eye, and each label where its offset falls on that canvas,
        /// with its line drawn from the control to the near edge of the label.
        /// </summary>
        private static void Place(Hand hand, Vector3 position, Quaternion rotation, Vector3 eye)
        {
            hand.Canvas.SetPositionAndRotation(position, Quaternion.LookRotation(position - eye, Vector3.up));
            foreach (var (settings, pill, _, leader, dot) in hand.Labels)
            {
                Vector2 button = hand.Canvas.InverseTransformPoint(position + rotation * settings.ButtonOffset);
                Vector2 at = hand.Canvas.InverseTransformPoint(position + rotation * settings.LabelOffset);
                pill.anchoredPosition = at;
                dot.anchoredPosition = button;
                var line = at - button;
                leader.anchoredPosition = button;
                leader.sizeDelta = new Vector2(line.magnitude, 1.2f);
                leader.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(line.y, line.x) * Mathf.Rad2Deg);
            }
        }
    }
}
