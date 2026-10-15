using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using MusiyoBetsknate.Museo;
using UnityEngine;
using UnityEngine.Networking;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Lets the companion wear a mask that the content service publishes and allows to be reused. Nothing is embedded:
    /// the model is requested like any published model (platform variant, temporary access, hash and size checks) and
    /// dropped with the scene. A record that restricts reuse, or has no variant for this device, is never worn. The mask
    /// keeps its proportions (uniform scale only) and is put away while the same record is on display at the visitor's
    /// point, so the guide is never mistaken for the exhibit.
    /// </summary>
    [RequireComponent(typeof(MuseumCompanion))]
    public sealed class MuseumCompanionMask : MonoBehaviour
    {
        /// <summary>Largest side of the worn mask, in metres, before the figure's own scale.</summary>
        public const float WornSize = .3f;
        private MuseumCompanion companion;
        private MuseumInteraction interaction;
        private CancellationTokenSource loading;
        private GltfImport import;
        private Transform worn;
        private string element;

        /// <summary>
        /// Where the mask stands: empty before a request, then loading, worn, or why it is not worn (no_element, restricted,
        /// no_model, no_access, invalid_model, unavailable, incompatible).
        /// </summary>
        public string Status { get; private set; } = "";
        public bool Worn => worn != null;
        public event Action Changed;

        private void Start()
        {
            companion = GetComponent<MuseumCompanion>();
            interaction = FindAnyObjectByType<MuseumInteraction>();
            element = MuseumCompanionSettings.Current.MaskElement;
            if (string.IsNullOrWhiteSpace(element)) { Finish("no_element"); return; }
            var loader = interaction != null ? interaction.GetComponent<TourLoader>() : null;
            string api = loader != null ? loader.ApiBase : TourLoader.ResolveApiBase(null, Application.absoluteURL);
            loading = new CancellationTokenSource();
            Load(api, element, Application.platform == RuntimePlatform.Android ? "quest" : "web", loading.Token);
        }

        private void Update()
        {
            if (worn == null) return;
            // Two of the same piece side by side would read as one being lent out; the companion takes its off meanwhile.
            bool shown = interaction != null && interaction.ActivePoint != null && interaction.ActivePoint.HasContent
                && interaction.ActivePoint.Content.elements.Any(item => item.slug == element);
            worn.gameObject.SetActive(!shown);
        }

        /// <summary>A record is worn only if it carries no restriction on reuse.</summary>
        public static bool MayWear(ElementContractV1 record)
            => record != null && (record.restrictions == null || record.restrictions.All(restriction => restriction.kind != "no_reuse"));

        private async void Load(string api, string slug, string profile, CancellationToken cancel)
        {
            Status = "loading";
            try
            {
                ElementContractV1 record;
                using (var request = UnityWebRequest.Get(api + "/elements/" + Uri.EscapeDataString(slug)))
                {
                    await Send(request, 15, cancel);
                    if (!ElementContractV1.TryParse(request.downloadHandler.text, out record)) { Finish("incompatible"); return; }
                }
                if (!MayWear(record)) { Finish("restricted"); return; }
                if (!ModelResourcePolicy.TrySelect(record, profile, out _, out var id)) { Finish("no_model"); return; }
                ResourceAccessContractV1 access;
                Uri url;
                using (var request = new UnityWebRequest(api + "/resources/" + Uri.EscapeDataString(id) + "/access?schema_version=1", "POST"))
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                    await Send(request, 15, cancel);
                    if (!ModelResourcePolicy.TryAccess(api, request.downloadHandler.text, profile, DateTimeOffset.UtcNow, out access, out url))
                    { Finish("no_access"); return; }
                }
                byte[] data;
                using (var request = UnityWebRequest.Get(url))
                {
                    await Send(request, 60, cancel);
                    data = request.downloadHandler.data;
                }
                if (!ModelResourcePolicy.ValidateGlb(data, profile, access.sha256)) { Finish("invalid_model"); return; }
                import = new GltfImport();
                if (!await import.Load(data, url, new ImportSettings { GenerateMipMaps = true }, cancel) || cancel.IsCancellationRequested
                    || companion == null || companion.MaskMount == null)
                { Finish("invalid_model"); return; }
                var holder = new GameObject("WornMask").transform;
                holder.SetParent(companion.MaskMount, false);
                // Published models face -Z after import; the figure faces +Z.
                holder.localRotation = Quaternion.Euler(0, 180, 0);
                var model = new GameObject("Model").transform;
                model.SetParent(holder, false);
                var instantiator = new GameObjectInstantiator(import, model, settings: new InstantiationSettings { Mask = ComponentType.Mesh });
                if (!await import.InstantiateMainSceneAsync(instantiator, cancel) || !ModelExaminationPose.Normalize(model, WornSize * MuseumCompanionSettings.Current.FigureScale))
                { Destroy(holder.gameObject); Finish("invalid_model"); return; }
                worn = holder;
                Finish("worn");
            }
            catch (OperationCanceledException) { }
            catch (Exception) { Finish("unavailable"); }
        }

        private void Finish(string status)
        {
            Status = status;
            Changed?.Invoke();
        }

        private static async Task Send(UnityWebRequest request, int timeout, CancellationToken cancel)
        {
            request.timeout = timeout;
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (cancel.IsCancellationRequested) { request.Abort(); throw new OperationCanceledException(); }
                await Task.Yield();
            }
            if (request.result != UnityWebRequest.Result.Success) throw new InvalidOperationException(request.error);
        }

        private void OnDestroy()
        {
            loading?.Cancel();
            loading?.Dispose();
            loading = null;
            import?.Dispose();
            import = null;
        }
    }
}
