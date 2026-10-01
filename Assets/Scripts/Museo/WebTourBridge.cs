using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Networking;

namespace MusiyoBetsknate.Museo
{
    public sealed class WebTourBridge : MonoBehaviour
    {
        private RecorridoVirtual tour;
        private NavegacionTecladoPuntosInteres navigation;
        private TourContractV1 contract;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void MusiyoNotifyPoint(string tourId, string pointKey);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var activeTour = FindAnyObjectByType<RecorridoVirtual>();
            if (activeTour != null && activeTour.GetComponent<WebTourBridge>() == null)
                activeTour.gameObject.AddComponent<WebTourBridge>();
        }
#endif

        private IEnumerator Start()
        {
            tour = GetComponent<RecorridoVirtual>();
            navigation = FindAnyObjectByType<NavegacionTecladoPuntosInteres>();
            if (tour == null || navigation == null)
            {
                Debug.LogError("Missing tour or navigation for the Web bridge.", this);
                yield break;
            }

            navigation.PuntoSeleccionado += NotifyPoint;
            yield return null;

            if (!Uri.TryCreate(Application.absoluteURL, UriKind.Absolute, out var page))
                yield break;

            var requestedTour = QueryParameter(page, "tour");
            if (!string.IsNullOrEmpty(requestedTour) && requestedTour != tour.IdComponente)
            {
                Debug.LogWarning("The requested tour does not match the scene.", this);
                yield break;
            }

            var url = new Uri(page, "/api/v1/tours/" + Uri.EscapeDataString(tour.IdComponente)
                + "?schema_version=1");
            using (var request = UnityWebRequest.Get(url.AbsoluteUri))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success
                    || !TourContractV1.TryParse(request.downloadHandler.text, out var received, out var error)
                    || !received.MatchesScene(tour, out error))
                {
                    Debug.LogError("Could not validate the tour with the API.", this);
                    yield break;
                }
                contract = received;
            }

            var pointKey = QueryParameter(page, "point");
            var elementId = QueryParameter(page, "element");
            if (string.IsNullOrEmpty(pointKey)) yield break;
            if (!TryResolve(contract, pointKey, elementId, out var roomKey))
            {
                Debug.LogWarning("The requested point or element is unavailable.", this);
                yield break;
            }

            foreach (var room in tour.Salas)
            {
                if (room.IdComponente != roomKey) continue;
                tour.IrASala(room);
                foreach (var child in room.Hijos)
                    if (child is PuntoInteres point && point.IdComponente == pointKey)
                    {
                        navigation.Seleccionar(point);
                        yield break;
                    }
            }
        }

        private void OnDestroy()
        {
            if (navigation != null) navigation.PuntoSeleccionado -= NotifyPoint;
        }

        private void NotifyPoint(PuntoInteres point)
        {
            if (contract == null || point == null
                || !TryResolve(contract, point.IdComponente, null, out _)) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            MusiyoNotifyPoint(tour.IdComponente, point.IdComponente);
#endif
        }

        public static bool TryResolve(TourContractV1 source, string pointKey,
            string elementId, out string roomKey)
        {
            roomKey = null;
            if (source == null || string.IsNullOrWhiteSpace(pointKey) || source.rooms == null)
                return false;
            foreach (var room in source.rooms)
                foreach (var point in room.points)
                {
                    if (point.key != pointKey) continue;
                    if (!string.IsNullOrEmpty(elementId)
                        && !Array.Exists(point.elements, element => element.slug == elementId)) return false;
                    roomKey = room.key;
                    return true;
                }
            return false;
        }

        private static string QueryParameter(Uri page, string name)
        {
            foreach (var part in page.Query.TrimStart('?').Split('&'))
            {
                var pair = part.Split(new[] { '=' }, 2);
                if (pair.Length == 2 && pair[0] == name)
                    return Uri.UnescapeDataString(pair[1].Replace('+', ' '));
            }
            return null;
        }
    }
}
