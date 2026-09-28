using UnityEngine;

namespace MusiyoBetsknate.Museo
{
    public sealed class ValidadorContratoEscena : MonoBehaviour
    {
        [SerializeField] private TextAsset contratoJson;
        [SerializeField] private RecorridoVirtual recorrido;

        private void Start()
        {
            if (contratoJson == null)
            {
                Debug.LogError("No se asignó contrato de recorrido.", this);
                return;
            }
            if (!ContratoRecorridoV1.IntentarLeer(contratoJson.text, out var contrato, out var error)
                || !contrato.CoincideCon(recorrido, out error))
                Debug.LogError("Contrato de recorrido incompatible: " + error, this);
        }
    }
}
