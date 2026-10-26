using MusiyoBetsknate.Museo;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using TMPro;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    /// <summary>One card per point (owner, 25/10/2026): the point's own records on the card, its pieces as labels.</summary>
    public sealed class PointRecordsTests
    {
        private static ElementSummaryContractV1 Record(string slug, bool model, string community = null)
            => new ElementSummaryContractV1 { slug = slug, title = "Título " + slug, has_3d_model = model, community = community };

        private static PointContractV1 Point(params ElementSummaryContractV1[] elements)
            => new PointContractV1 { key = "point", name = "Punto", order = 0, activation = new[] { "proximity" }, elements = elements };

        [Test]
        public void ACharacterAndItsMasksShowOneCardAndPieces()
        {
            var sanjuanes = Point(Record("sanjuanes", false, "Kamëntsá"), Record("animal", true, "Kamëntsá"), Record("silbido", true, "Kamëntsá"),
                Record("sin-arrugas", true, "Kamëntsá"));
            Assert.That(MuseumPointRecords.CardRecords(sanjuanes), Is.EqualTo(new[] { 0 }), "The card shows the character only.");
            Assert.That(MuseumPointRecords.Pieces(sanjuanes).ConvertAll(piece => piece.slug), Is.EqualTo(new[] { "animal", "silbido", "sin-arrugas" }));
            Assert.That(MuseumPointRecords.HasTabs(sanjuanes), Is.False);
            Assert.That(MuseumPointRecords.StepsPieces(sanjuanes), Is.True, "Its arrows change the mask.");
            Assert.That(MuseumPointRecords.StepsRecords(sanjuanes), Is.False);
            var matachin = Point(Record("matachin", false), Record("mask", true));
            Assert.That(MuseumPointRecords.StepsPieces(matachin), Is.False, "A single mask needs no arrows.");
            Assert.That(MuseumPointRecords.IsPiece(matachin, matachin.elements[1]), Is.True);
            Assert.That(MuseumPointRecords.IsPiece(matachin, matachin.elements[0]), Is.False);
        }

        [Test]
        public void TwoRecordsWithoutModelsAreTabsNamedAfterTheirCommunities()
        {
            var meanings = Point(Record("nombres", false, "Kamëntsá"), Record("atun-puncha", false, "Inga"));
            Assert.That(MuseumPointRecords.HasTabs(meanings), Is.True);
            Assert.That(MuseumPointRecords.TabNames(meanings), Is.EqualTo(new[] { "Kamëntsá", "Inga" }));
            Assert.That(MuseumPointRecords.StepsRecords(meanings), Is.False, "No arrows and no «1 de 2».");
            var same = Point(Record("a", false, "Kamëntsá"), Record("b", false, "Kamëntsá"));
            Assert.That(MuseumPointRecords.TabNames(same), Is.EqualTo(new[] { "Título a", "Título b" }), "Titles when the communities do not tell them apart.");
            var pieces = Point(Record("a", true), Record("b", true));
            Assert.That(MuseumPointRecords.HasTabs(pieces), Is.False);
            Assert.That(MuseumPointRecords.StepsRecords(pieces), Is.True, "Records that all have a model keep their arrows.");
            Assert.That(MuseumPointRecords.CardRecords(pieces), Is.EqualTo(new[] { 0, 1 }));
        }

        [Test]
        public void APiecesNoteIsTheRestOfItsDescription()
        {
            Assert.That(MuseumModelPresenter.PieceNote("Máscara de Sanjuán tallada en madera, con gesto de silbido. Su autoría, su historia y el significado particular de sus rasgos aún están por documentar."),
                Is.EqualTo("Su autoría, su historia y el significado particular de sus rasgos aún están por documentar."));
            Assert.That(MuseumModelPresenter.PieceNote("Una sola frase."), Is.Null);
            Assert.That(MuseumModelPresenter.PieceNote(""), Is.Null);
        }

        [Test]
        public void TheTabsMarkTheChosenRecordAndChooseOnPress()
        {
            var root = new GameObject("TabsTest", typeof(RectTransform), typeof(Canvas));
            try
            {
                var style = new GameObject("Style", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
                style.transform.SetParent(root.transform, false);
                int chosen = -1;
                var tabs = root.AddComponent<MuseumRecordTabs>();
                tabs.Configure((RectTransform)root.transform, new Vector2(16, -16), style, record => chosen = record);
                tabs.Show("point", new[] { 0, 2 }, new[] { "Kamëntsá", "Inga" }, 2);
                Assert.That(tabs.Row.gameObject.activeSelf, Is.True);
                Assert.That(tabs.Tabs.Count, Is.EqualTo(2));
                Assert.That(tabs.Tabs[1].caption.text, Is.EqualTo("Inga"));
                Assert.That(tabs.Tabs[1].caption.fontStyle & FontStyles.Bold, Is.EqualTo(FontStyles.Bold), "The chosen tab is marked.");
                Assert.That(tabs.Tabs[0].caption.fontStyle & FontStyles.Bold, Is.EqualTo(FontStyles.Normal));
                tabs.Tabs[0].button.onClick.Invoke();
                Assert.That(chosen, Is.EqualTo(0));
                tabs.Show("point", new[] { 0 }, new[] { "Kamëntsá" }, 0);
                Assert.That(tabs.Row.gameObject.activeSelf, Is.False, "A single record needs no tabs.");
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
