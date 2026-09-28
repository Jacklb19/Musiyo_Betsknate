using MusiyoBetsknate.Museo;
using NUnit.Framework;

namespace MusiyoBetsknate.Tests
{
    public class ContratoRecorridoTests
    {
        [Test]
        public void UnElementoPuedeAparecerEnDosPuntos()
        {
            const string json = "{\"schemaVersion\":1,\"recorridoId\":\"r\",\"salas\":[{\"id\":\"s\",\"orden\":0,\"puntos\":[{\"anclajeId\":\"p1\",\"elementoIds\":[\"e1\"]},{\"anclajeId\":\"p2\",\"elementoIds\":[\"e1\"]}]}]}";
            Assert.IsTrue(ContratoRecorridoV1.IntentarLeer(json, out var contrato, out var error), error);
            Assert.AreEqual(2, contrato.salas[0].puntos.Length);
        }

        [Test]
        public void AnclajeRepetidoBloqueaContrato()
        {
            const string json = "{\"schemaVersion\":1,\"recorridoId\":\"r\",\"salas\":[{\"id\":\"s\",\"orden\":0,\"puntos\":[{\"anclajeId\":\"p1\",\"elementoIds\":[]},{\"anclajeId\":\"p1\",\"elementoIds\":[]}]}]}";
            Assert.IsFalse(ContratoRecorridoV1.IntentarLeer(json, out _, out _));
        }

        [Test]
        public void VersionDesconocidaBloqueaContrato()
        {
            const string json = "{\"schemaVersion\":2,\"recorridoId\":\"r\",\"salas\":[{\"id\":\"s\",\"orden\":0,\"puntos\":[]}]}";
            Assert.IsFalse(ContratoRecorridoV1.IntentarLeer(json, out _, out _));
        }
    }
}
