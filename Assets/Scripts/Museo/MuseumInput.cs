using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace MusiyoBetsknate.Museum
{
    /// <summary>What the visitor is doing decides which action maps listen.</summary>
    public enum MuseumInputContext
    {
        /// <summary>Gameplay and interface actions are silent while a new key is captured; headset tracking continues.</summary>
        Suspended,
        /// <summary>Walking freely, including reading a screen that floats inside the museum.</summary>
        Visit,
        /// <summary>A panel on the screen holds the visitor and takes interface navigation.</summary>
        Panel,
        /// <summary>A model is being examined.</summary>
        Examine,
        /// <summary>Entry and pause menus.</summary>
        Menu,
        /// <summary>The visitor writes text: keys type, so walking and every shortcut are silent. The pointer still chooses.</summary>
        Typing
    }

    /// <summary>
    /// Sole owner of the runtime copy of the museum actions. Locomotion and interface navigation never listen together,
    /// so one key press cannot both walk and move the focus.
    /// </summary>
    public sealed class MuseumInput : IDisposable
    {
        public const string ResourceName = "MuseumInput";
        public const string StorageKey = "musiyo.input-bindings.v1";
        private static readonly Dictionary<MuseumInputContext, string[]> ContextMaps = new()
        {
            { MuseumInputContext.Suspended, new string[0] },
            { MuseumInputContext.Visit, new[] { "Pointer", "Common", "Locomotion", "Visit" } },
            { MuseumInputContext.Panel, new[] { "Pointer", "Common", "Visit", "Navigation" } },
            { MuseumInputContext.Examine, new[] { "Pointer", "Common", "Visit", "Examine" } },
            { MuseumInputContext.Menu, new[] { "Pointer", "Common", "Navigation" } },
            { MuseumInputContext.Typing, new[] { "Pointer" } }
        };
        private static MuseumInput current;
        private readonly Dictionary<InputAction, InputActionReference> references = new();
        private readonly Action<string> store;
        private InputSystemUIInputModule uiModule;
        private MuseumInputContext requested;
        private int captureEndFrame = -1;
        private bool disposed;

        public InputActionAsset Asset { get; }
        /// <summary>False once released, or once Unity destroyed the runtime copy.</summary>
        public bool Alive => !disposed && Asset != null;
        public MuseumInputBindings Bindings { get; }
        public MuseumInputContext Context { get; private set; }
        /// <summary>True while a new key is being captured; interactive maps are silent meanwhile.</summary>
        public bool Capturing { get; private set; }
        /// <summary>True on the frame a capture ended, so the captured key cannot also act on the interface.</summary>
        public bool CaptureEndedThisFrame => captureEndFrame == Time.frameCount;
        public event Action BindingsChanged;

        public InputAction Point { get; }
        public InputAction Click { get; }
        public InputAction RightClick { get; }
        public InputAction MiddleClick { get; }
        public InputAction ScrollWheel { get; }
        public InputAction Confirm { get; }
        public InputAction Back { get; }
        public InputAction Pause { get; }
        public InputAction Next { get; }
        public InputAction Previous { get; }
        public InputAction Move { get; }
        public InputAction Look { get; }
        public InputAction KeyboardLook { get; }
        public InputAction Sprint { get; }
        public InputAction Interact { get; }
        public InputAction Detail { get; }
        public InputAction Examine { get; }
        public InputAction Guide { get; }
        public InputAction Wayfinding { get; }
        public InputAction NarrationPause { get; }
        public InputAction NarrationRepeat { get; }
        public InputAction NarrationMute { get; }
        public InputAction ScrollPanel { get; }
        public InputAction ChooseElement { get; }
        public InputAction Rotate { get; }
        public InputAction Zoom { get; }
        public InputAction ResetPose { get; }
        public InputAction Drag { get; }
        public InputAction Navigate { get; }
        public InputAction HeadPosition { get; }
        public InputAction HeadRotation { get; }
        public InputAction HeadState { get; }
        public InputAction RightPosition { get; }
        public InputAction RightRotation { get; }
        public InputAction RightTracked { get; }
        public InputAction LeftPosition { get; }
        public InputAction LeftRotation { get; }
        public InputAction LeftTracked { get; }
        /// <summary>Right stick of a headset: turns the visitor, continuously or in steps.</summary>
        public InputAction Turn { get; }
        /// <summary>Held on a headset: an arc shows where the visitor would stand; releasing it jumps there.</summary>
        public InputAction Teleport { get; }
        /// <summary>
        /// Takes the published piece that the pointer or hand reaches: the right grip on a headset, the right mouse button on
        /// a desktop.
        /// </summary>
        public InputAction Grab { get; }
        /// <summary>The left grip of a headset; either hand takes, hands over and turns the piece.</summary>
        public InputAction GrabLeft { get; }
        public InputAction AutoTour { get; }
        /// <summary>Asks the companion a spoken question anywhere: X on the left controller, a key on a computer.</summary>
        public InputAction AskCompanion { get; }
        /// <summary>Sits on the seat offered nearby, or stands up: the right stick's press on a headset, a key on a computer.</summary>
        public InputAction Sit { get; }
        public bool Spatial { get; private set; }

        /// <summary>The instance every museum component shares, created on first use with the visitor's saved keys.</summary>
        public static MuseumInput Current
        {
            get
            {
                if (current != null && !current.Alive) current = null;
                return current ??= new MuseumInput(LoadSource(), PlayerPrefs.GetString(StorageKey, ""), Save);
            }
        }

        /// <summary>Hands the shared role to another instance, or to nobody, and releases the previous one.</summary>
        public static void Replace(MuseumInput value)
        {
            if (current == value) return;
            current?.Dispose();
            current = value;
        }

        /// <summary>Shipped actions with their default keys; nothing is read from or written to this device.</summary>
        public static MuseumInput CreateDefault() => new MuseumInput(LoadSource(), null, null);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ReleaseShared() => Replace(null);

        private static InputActionAsset LoadSource()
        {
            var source = Resources.Load<InputActionAsset>(ResourceName);
            if (source == null) throw new InvalidOperationException("Missing museum input actions.");
            return source;
        }

        private static void Save(string serialized)
        {
            PlayerPrefs.SetString(StorageKey, serialized);
            PlayerPrefs.Save();
        }

        public MuseumInput(InputActionAsset source, string saved, Action<string> save)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            Asset = UnityEngine.Object.Instantiate(source);
            Asset.name = source.name;
            store = save;
            Point = Find("Pointer/Point"); Click = Find("Pointer/Click"); RightClick = Find("Pointer/RightClick");
            MiddleClick = Find("Pointer/MiddleClick"); ScrollWheel = Find("Pointer/ScrollWheel");
            Confirm = Find("Common/Confirm"); Back = Find("Common/Back"); Pause = Find("Common/Pause");
            Next = Find("Common/Next"); Previous = Find("Common/Previous");
            Move = Find("Locomotion/Move"); Look = Find("Locomotion/Look");
            KeyboardLook = Find("Locomotion/KeyboardLook"); Sprint = Find("Locomotion/Sprint");
            Interact = Find("Visit/Interact"); Detail = Find("Visit/Detail"); Examine = Find("Visit/Examine");
            Guide = Find("Visit/Guide"); Wayfinding = Find("Visit/Wayfinding");
            NarrationPause = Find("Visit/NarrationPause"); NarrationRepeat = Find("Visit/NarrationRepeat");
            NarrationMute = Find("Visit/NarrationMute"); ScrollPanel = Find("Visit/ScrollPanel");
            ChooseElement = Find("Visit/ChooseElement");
            Rotate = Find("Examine/Rotate"); Zoom = Find("Examine/Zoom"); ResetPose = Find("Examine/Reset"); Drag = Find("Examine/Drag");
            Navigate = Find("Navigation/Navigate");
            HeadPosition = Find("Tracking/HeadPosition"); HeadRotation = Find("Tracking/HeadRotation");
            HeadState = Find("Tracking/HeadState"); RightPosition = Find("Tracking/RightPosition");
            RightRotation = Find("Tracking/RightRotation"); RightTracked = Find("Tracking/RightTracked");
            LeftPosition = Find("Tracking/LeftPosition"); LeftRotation = Find("Tracking/LeftRotation");
            LeftTracked = Find("Tracking/LeftTracked");
            Turn = Find("Locomotion/Turn"); Teleport = Find("Locomotion/Teleport");
            Grab = Find("Visit/Grab"); GrabLeft = Find("Visit/GrabLeft"); AutoTour = Find("Visit/AutoTour");
            AskCompanion = Find("Visit/AskCompanion");
            Sit = Find("Visit/Sit");
            foreach (var maps in ContextMaps.Values)
                foreach (var map in maps) Asset.FindActionMap(map, true);
            Bindings = new MuseumInputBindings(this);
            Bindings.Load(saved);
        }

        private InputAction Find(string path) => Asset.FindAction(path, true);

        /// <summary>Maps that can listen at the same time in some context; keys must be unique among them.</summary>
        public static bool ShareContext(string first, string second)
        {
            foreach (var maps in ContextMaps.Values)
                if (Array.IndexOf(maps, first) >= 0 && Array.IndexOf(maps, second) >= 0) return true;
            return false;
        }

        /// <summary>Enables exactly the maps of the context. Calling it again repairs anything another component disabled.</summary>
        public void SetContext(MuseumInputContext context)
        {
            requested = context;
            if (!Alive) return;
            Context = Capturing ? MuseumInputContext.Suspended : context;
            var enabled = ContextMaps[Context];
            // Disable first so two contexts never overlap, not even for one action.
            foreach (var map in Asset.actionMaps)
                if (Array.IndexOf(enabled, map.name) < 0 && !(Spatial && map.name == "Tracking")) map.Disable();
            foreach (var map in Asset.actionMaps)
                if (Array.IndexOf(enabled, map.name) >= 0 || Spatial && map.name == "Tracking") map.Enable();
            // Menus move their own focus; only panels inside the visit let the interface module follow the arrows.
            if (uiModule != null) uiModule.move = Context == MuseumInputContext.Panel ? Reference(Navigate) : null;
        }

        /// <summary>Silences interactive actions while the visitor chooses a new key.</summary>
        public void BeginCapture()
        {
            Capturing = true;
            SetContext(requested);
        }

        public void EndCapture()
        {
            if (!Capturing) return;
            Capturing = false;
            captureEndFrame = Time.frameCount;
            SetContext(requested);
        }

        /// <summary>Points the interface module at these actions, so pointer and confirmation share the one runtime copy.</summary>
        public void BindUi(InputSystemUIInputModule module)
        {
            if (module == null || !Alive || module == uiModule && module.actionsAsset == Asset) return;
            uiModule = module;
            module.actionsAsset = Asset;
            module.point = Reference(Point);
            module.leftClick = Reference(Click);
            module.rightClick = Reference(RightClick);
            module.middleClick = Reference(MiddleClick);
            module.scrollWheel = Reference(ScrollWheel);
            module.submit = Reference(Confirm);
            // Pause and Back are handled by the visit and the menus, never as a generic interface cancel.
            module.cancel = null;
            module.trackedDevicePosition = Spatial ? Reference(RightPosition) : null;
            module.trackedDeviceOrientation = Spatial ? Reference(RightRotation) : null;
            // The module enables what it is given; put the context back in charge.
            SetContext(requested);
        }

        /// <summary>Keeps pose tracking alive even when gameplay is suspended. The visitor owns the tracking origin.</summary>
        public void SetSpatial(bool value, Transform trackingOrigin = null)
        {
            Spatial = value;
            if (uiModule != null)
            {
                uiModule.trackedDevicePosition = value ? Reference(RightPosition) : null;
                uiModule.trackedDeviceOrientation = value ? Reference(RightRotation) : null;
                uiModule.xrTrackingOrigin = trackingOrigin;
            }
            SetContext(requested);
        }

        /// <summary>Shared references belong to the sole runtime asset and are released with it.</summary>
        public InputActionReference Reference(InputAction action)
        {
            if (action == null || action.actionMap?.asset != Asset) throw new ArgumentException("Action must belong to the museum runtime asset.", nameof(action));
            if (!references.TryGetValue(action, out var reference) || reference == null)
                references[action] = reference = InputActionReference.Create(action);
            return reference;
        }

        /// <summary>Index of the element whose number key was pressed, or -1.</summary>
        public int ChosenElement()
        {
            var control = ChooseElement.activeControl;
            return control == null ? -1 : ChooseElement.GetBindingIndexForControl(control);
        }

        internal void NotifyBindingsChanged()
        {
            store?.Invoke(Bindings.Serialize());
            BindingsChanged?.Invoke();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (current == this) current = null;
            if (Asset == null) return;
            if (uiModule != null && uiModule.actionsAsset == Asset) uiModule.UnassignActions();
            uiModule = null;
            Asset.Disable();
            foreach (var reference in references.Values) Release(reference);
            references.Clear();
            Release(Asset);
        }

        private static void Release(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
