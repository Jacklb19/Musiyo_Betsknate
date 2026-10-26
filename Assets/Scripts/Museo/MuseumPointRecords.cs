using System.Collections.Generic;
using System.Linq;
using MusiyoBetsknate.Museo;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// How the records of one point are presented (owner, 25/10/2026: "una sola ficha por punto y los datos de la máscara
    /// en una etiqueta pequeña"). The rule follows the published records, not their names:
    /// <list type="bullet">
    /// <item>the card shows the point's records without a 3D model (the character, the meanings of a name), and when they
    /// are several, as tabs named after their communities (Kamëntsá, Inga) or their titles;</item>
    /// <item>the records with a 3D model of such a point are its pieces: they stand on the pedestal one at a time and are
    /// named by a small label beside the piece, never by the card;</item>
    /// <item>a point whose records all have a model keeps them all on the card, stepping between them with arrows.</item>
    /// </list>
    /// Nothing is hidden or changed in the published data.
    /// </summary>
    public static class MuseumPointRecords
    {
        /// <summary>Indices of the records the card shows, in their published order.</summary>
        public static List<int> CardRecords(PointContractV1 point)
        {
            var elements = point?.elements ?? System.Array.Empty<ElementSummaryContractV1>();
            var plain = Enumerable.Range(0, elements.Length).Where(index => !elements[index].has_3d_model).ToList();
            return plain.Count > 0 ? plain : Enumerable.Range(0, elements.Length).ToList();
        }

        /// <summary>Records shown as pieces with a label instead of on the card; empty where the card shows them all.</summary>
        public static List<ElementSummaryContractV1> Pieces(PointContractV1 point)
        {
            var elements = point?.elements ?? System.Array.Empty<ElementSummaryContractV1>();
            if (!elements.Any(element => !element.has_3d_model)) return new List<ElementSummaryContractV1>();
            return elements.Where(element => element.has_3d_model).ToList();
        }

        public static bool IsPiece(PointContractV1 point, ElementSummaryContractV1 element)
            => element != null && Pieces(point).Contains(element);

        /// <summary>Several records without a model share the card and are chosen with tabs.</summary>
        public static bool HasTabs(PointContractV1 point) => point?.elements != null && point.elements.Count(element => !element.has_3d_model) >= 2;

        /// <summary>The arrows beside the pedestal step between pieces (the card stays) when the point has several.</summary>
        public static bool StepsPieces(PointContractV1 point) => Pieces(point).Count >= 2;

        /// <summary>The arrows step between the card's records when they all have a model and are several.</summary>
        public static bool StepsRecords(PointContractV1 point) => !HasTabs(point) && Pieces(point).Count == 0 && CardRecords(point).Count >= 2;

        /// <summary>
        /// Names of the card's tabs: the records' communities when they tell the records apart (Kamëntsá, Inga), and
        /// otherwise their titles.
        /// </summary>
        public static List<string> TabNames(PointContractV1 point)
        {
            var records = CardRecords(point).Select(index => point.elements[index]).ToList();
            var communities = records.Select(record => record.community?.Trim()).ToList();
            bool distinct = communities.All(name => !string.IsNullOrEmpty(name)) && communities.Distinct().Count() == communities.Count;
            return distinct ? communities : records.Select(record => record.title).ToList();
        }
    }
}
