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
            if (!TourContractV1.TryParse(contratoJson.text, out var contract, out var error)
                || !contract.MatchesScene(recorrido, out error))
                Debug.LogError("Contrato de recorrido incompatible: " + error, this);
        }
    }
}
