using UnityEngine;

namespace MusiyoBetsknate.Museo
{
    public sealed class HudRecorridoPrueba : MonoBehaviour
    {
        [SerializeField] private NavegacionTecladoPuntosInteres navegacion;
        private GUIStyle estilo;

        private void OnGUI()
        {
            if (estilo == null)
            {
                estilo = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 16, wordWrap = true };
                estilo.normal.textColor = Color.white;
            }
            var punto = navegacion != null ? navegacion.VitrinaSeleccionada : null;
            string estado = punto == null ? "Sin punto seleccionado" : punto.NombreComponente + ": contenido no disponible para divulgación pública";
            GUI.Box(new Rect(16, 16, Mathf.Min(Screen.width - 32, 610), 112),
                "PROTOTIPO GEOMÉTRICO — SIN CONTENIDO CULTURAL\n" +
                "WASD: caminar | Clic: mirar | Esc: liberar ratón\n" +
                "Tab / Mayús+Tab: punto | Intro: examinar | Re Pág / Av Pág: elemento\n" + estado,
                estilo);
        }
    }
}
