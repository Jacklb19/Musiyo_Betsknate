using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace MusiyoBetsknate.Museum
{
    public sealed class MuseumHud : MonoBehaviour
    {
        [SerializeField] private TourLoader loader;
        [SerializeField] private MuseumInteraction interaction;
        [SerializeField] private DesktopPointInput pointInput;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text focusText;
        [SerializeField] private TMP_Text panelText;
        [SerializeField] private GameObject panel;
        [SerializeField] private Transform elementList;
        [SerializeField] private Button elementTemplate;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button pauseButton;
        [SerializeField] private Button detailButton;
        [SerializeField] private Slider sensitivitySlider;
        [SerializeField] private DesktopVisitorController visitor;
        private UnityWebRequest detailRequest;
        private Coroutine reading;
        private string requestedSlug;
        private string detail;

        private void OnEnable()
        {
            loader.Changed += Refresh;
            interaction.Changed += Refresh;
            retryButton.onClick.AddListener(loader.Reload);
            closeButton.onClick.AddListener(interaction.Back);
            pauseButton.onClick.AddListener(TogglePause);
            detailButton.onClick.AddListener(OpenDetail);
            if (visitor != null && sensitivitySlider != null)
            {
                sensitivitySlider.value = visitor.LookSensitivity;
                sensitivitySlider.onValueChanged.AddListener(visitor.SetSensitivity);
            }
            Refresh();
        }

        private void OnDisable()
        {
            loader.Changed -= Refresh;
            interaction.Changed -= Refresh;
            retryButton.onClick.RemoveListener(loader.Reload);
            closeButton.onClick.RemoveListener(interaction.Back);
            pauseButton.onClick.RemoveListener(TogglePause);
            detailButton.onClick.RemoveListener(OpenDetail);
            if (visitor != null && sensitivitySlider != null) sensitivitySlider.onValueChanged.RemoveListener(visitor.SetSensitivity);
            CancelDetail();
        }

        private void TogglePause() => interaction.SetPaused(interaction.State != InteractionState.Paused);
        private void OpenDetail() => interaction.OpenDetail();

        private void Update()
        {
            var focus = pointInput.FocusedPoint;
            focusText.text = focus != null && focus.Content != null ? focus.Content.name + " · Enter para activar"
                : pointInput.GazeProgress > 0 ? "Mirada: " + Mathf.RoundToInt(pointInput.GazeProgress * 100) + "%" : ".";
        }

        private void Refresh()
        {
            statusText.text = loader.Status;
            retryButton.gameObject.SetActive(loader.State == TourLoadState.Unavailable);
            bool paused = interaction.State == InteractionState.Paused;
            pauseButton.GetComponentInChildren<TMP_Text>().text = paused ? "Reanudar" : "Pausa";
            if (sensitivitySlider != null) sensitivitySlider.gameObject.SetActive(paused);
            panel.SetActive(interaction.State != InteractionState.Exploration);
            if (interaction.State == InteractionState.Exploration || paused)
            {
                CancelDetail();
                requestedSlug = null;
                detail = null;
            }
            foreach (Transform child in elementList)
                if (child.gameObject != elementTemplate.gameObject)
                { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            elementTemplate.gameObject.SetActive(false);
            bool choosing = interaction.State == InteractionState.PointFocus;
            elementList.gameObject.SetActive(choosing);
            detailButton.gameObject.SetActive(interaction.State == InteractionState.ElementSelected);
            if (paused) { panelText.text = "Visita en pausa. Ajusta la sensibilidad del ratón o reanuda para continuar."; return; }
            var point = interaction.ActivePoint;
            if (point == null) { panelText.text = ""; return; }
            if (choosing)
            {
                panelText.text = point.Content.name + "\nElige un elemento:";
                for (int index = 0; index < point.Content.elements.Length; index++)
                {
                    int selectedIndex = index;
                    var button = Instantiate(elementTemplate, elementList);
                    button.name = "ElementChoice";
                    button.gameObject.SetActive(true);
                    button.GetComponentInChildren<TMP_Text>().text = point.Content.elements[index].title;
                    button.onClick.AddListener(() => interaction.SelectElement(selectedIndex));
                    if (index == 0 && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(button.gameObject);
                }
                return;
            }
            var element = interaction.SelectedElement;
            if (element == null) return;
            if (interaction.State != InteractionState.Reading)
            {
                CancelDetail();
                requestedSlug = null;
                detail = null;
                panelText.text = point.Content.name + "\n" + element.title + "\nF: leer ficha · Retroceso: cerrar";
                return;
            }
            panelText.text = detail ?? "Cargando ficha…";
#if UNITY_WEBGL && !UNITY_EDITOR
            panelText.text = "La ficha seleccionada está disponible debajo del recorrido. Pulsa Volver para continuar la visita.";
            return;
#else
            if (requestedSlug == element.slug) return;
            CancelDetail();
            requestedSlug = element.slug;
            reading = StartCoroutine(LoadDetail(element.slug));
#endif
        }

        private IEnumerator LoadDetail(string slug)
        {
            detailRequest = UnityWebRequest.Get(loader.ApiBase + "/elements/" + System.Uri.EscapeDataString(slug));
            detailRequest.timeout = 15;
            yield return detailRequest.SendWebRequest();
            bool success = detailRequest.result == UnityWebRequest.Result.Success;
            var json = success ? detailRequest.downloadHandler.text : null;
            detailRequest.Dispose();
            detailRequest = null;
            reading = null;
            if (!success || !ElementText.TryFormat(slug, json, out var formatted))
            { detail = "La ficha no está disponible. Puedes cerrar y seguir recorriendo el museo."; Refresh(); yield break; }
            detail = formatted;
            Refresh();
        }

        private void CancelDetail()
        {
            if (reading != null) StopCoroutine(reading);
            reading = null;
            if (detailRequest == null) return;
            detailRequest.Abort();
            detailRequest.Dispose();
            detailRequest = null;
        }
    }
}
