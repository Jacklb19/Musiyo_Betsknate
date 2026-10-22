using System.Linq;
using MusiyoBetsknate.Museo;
using MusiyoBetsknate.Museum;
using NUnit.Framework;

namespace MusiyoBetsknate.Tests
{
    public sealed class ElementTextTests
    {
        [Test]
        public void TheReadingStaysCleanAndTheReferencesCloseTheRecord()
        {
            const string json = "{\"slug\":\"test\",\"title\":\"Test\",\"description\":\"Synthetic\","
                + "\"blocks\":[{\"id\":\"b\",\"kind\":\"testimony\",\"text\":\"Testimony\",\"attribution\":\"Test speaker\",\"context\":\"Entrevista, Sibundoy.\"}],"
                + "\"sources\":[{\"id\":\"s\",\"kind\":\"other\",\"reference\":\"Test source\"}],"
                + "\"credits\":[{\"role\":\"Texto\",\"name\":\"Autor\"},{\"role\":null,\"name\":\"Cabildo indígena\"}],"
                + "\"resources\":[{\"id\":\"r\",\"kind\":\"narration\",\"mime\":\"audio/mpeg\",\"transcription\":\"TEST TRANSCRIPT\"}]}";
            Assert.That(ElementText.TryFormat("test", json, out var text), Is.True);
            int reading = text.IndexOf("Testimony"), references = text.IndexOf("Referencias");
            Assert.That(text, Does.Contain("Testimonio").And.Contain("TEST TRANSCRIPT"));
            Assert.That(reading, Is.GreaterThan(0));
            Assert.That(text.Substring(reading, references - reading), Does.Not.Contain("Test speaker").And.Not.Contain("Sibundoy"),
                "Nothing about who said it interrupts the reading.");
            Assert.That(text, Does.Contain("Testimony</noparse><sup>1</sup>"), "The block ends with its small reference number.");
            Assert.That(text, Does.Contain("1. <noparse>Testimonio: Test speaker. Entrevista, Sibundoy.</noparse>"));
            Assert.That(text, Does.Contain("<noparse>Texto: Autor · Cabildo indígena</noparse>"));
            Assert.That(text, Does.Not.Contain("Fuente:").And.Not.Contain("Test source"), "A source no block cites is left to the final credits.");
            Assert.That(text.IndexOf("Referencias"), Is.GreaterThan(text.IndexOf("TEST TRANSCRIPT")), "The references come last.");
            Assert.That(ElementText.TryFormat("another", json, out var rejected), Is.False);
            Assert.That(rejected, Is.Null);
        }

        [Test]
        public void ServiceTextIsNeverReadAsMarkup()
        {
            const string json = "{\"slug\":\"test\",\"title\":\"<b>Título</b>\",\"description\":\"a </noparse><color=red>b\","
                + "\"blocks\":[{\"id\":\"b\",\"kind\":\"interpretation\",\"text\":\"<size=200>Lectura\"}]}";
            Assert.That(ElementText.TryFormat("test", json, out var text), Is.True);
            Assert.That(text, Does.StartWith("<noparse><b>Título</b></noparse>"));
            Assert.That(text, Does.Contain("<noparse><size=200>Lectura</noparse>"));
            Assert.That(text, Does.Not.Contain("a </noparse><color"), "Text cannot close the literal span itself.");
            Assert.That(text, Does.Not.Contain("Referencias"), "A record without notes or credits ends with its reading.");
        }

        [TestCase("Castaño, A. (2021). Algunas imágenes del sincretismo.", "Castaño (2021)")]
        [TestCase("Gómez Montañez, P. F. (2020). El carnaval del perdón.", "Gómez Montañez (2020)")]
        [TestCase("Jaramillo Guerrero, J., & Dávila Zambrano, L. (2013). Estéticas convergentes.", "Jaramillo Guerrero y Dávila Zambrano (2013)")]
        [TestCase("Ministerio de Cultura de Colombia. (s. f.). Plan Especial de Salvaguardia.", "MinCultura (s. f.)")]
        [TestCase("Ramírez, A. (2025, 15 de agosto). Cuando lo ancestral.", "Ramírez (2025)")]
        [TestCase("Banco de Memoria del Bëtscnaté. Documentos de archivo comunitario.", "Banco de Memoria del Bëtscnaté")]
        [TestCase("Bëtsknaté: Historia, Significado y Simbología. Documento facilitado.", "Bëtsknaté: Historia, Significado y Simbología")]
        public void CitationsAreShortenedAsOnTheWeb(string reference, string expected)
            => Assert.That(RecordReferences.ShortCitation(reference), Is.EqualTo(expected));

        [Test]
        public void EachBlockWithASourceAttributionOrContextGetsOneNumberedNote()
        {
            var element = new ElementContractV1
            {
                sources = new[] { new SourceContractV1 { id = "g", kind = "publication", reference = "Gómez Montañez, P. F. (2020). Título." } },
                blocks = new[]
                {
                    new TextBlockContractV1 { id = "a", kind = "documented_fact", text = "Hecho.", source_id = "g" },
                    new TextBlockContractV1 { id = "b", kind = "testimony", text = "Relato.", source_id = "g",
                        attribution = "Colaborador de la comunidad citado por Gómez Montañez (2020)", context = "Entrevista etnográfica, Sibundoy." },
                    new TextBlockContractV1 { id = "c", kind = "interpretation", text = "Lectura." },
                },
                credits = new[] { new CreditContractV1 { role = "Texto", name = "Autor" }, new CreditContractV1 { role = "Pertenencia colectiva", name = "Cabildo indígena" } },
            };
            var notes = RecordReferences.BlockNotes(element);
            Assert.That(notes.Select(note => (note.BlockId, note.Number, note.Text)), Is.EqualTo(new[]
            {
                ("a", 1, "Gómez Montañez (2020)."),
                ("b", 2, "Testimonio: Colaborador de la comunidad citado por Gómez Montañez (2020). Entrevista etnográfica, Sibundoy."),
            }));
            Assert.That(RecordReferences.CreditLine(element), Is.EqualTo("Texto: Autor · Pertenencia colectiva: Cabildo indígena"));
        }

        [Test]
        public void BlocksCitingTheSameWorkShareOneNumber()
        {
            var element = new ElementContractV1
            {
                sources = new[] { new SourceContractV1 { id = "g", kind = "publication", reference = "Gómez Montañez, P. F. (2020). Título." } },
                blocks = new[]
                {
                    new TextBlockContractV1 { id = "a", kind = "documented_fact", text = "A.", source_id = "g" },
                    new TextBlockContractV1 { id = "b", kind = "interpretation", text = "B.", source_id = "g" },
                    new TextBlockContractV1 { id = "c", kind = "interpretation", text = "C.", source_id = "g", context = "Lectura etnográfica." },
                },
            };
            Assert.That(RecordReferences.BlockNotes(element).Select(note => note.Number), Is.EqualTo(new[] { 1, 1, 2 }));
        }
    }
}
