using System.Linq;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public sealed class GuideFigureTests
    {
        [Test]
        public void BothGuidesHaveAFigureThatNeedsNothingFromTheMuseumModel()
        {
            var settings = GuideFigureSettings.Current;
            foreach (var role in new[] { GuideFigureSettings.Companion, GuideFigureSettings.Interviewer })
            {
                Assert.That(settings.Figures.ContainsKey(role), Is.True, role);
                var holder = new GameObject("Holder");
                try
                {
                    var figure = GuideFigure.Build(holder.transform, role);
                    var bounds = figure.WorldBounds();
                    Assert.That(bounds.size.y, Is.EqualTo(figure.Height).Within(.05f), role + " is as tall as it says.");
                    Assert.That(figure.Height, Is.InRange(role == GuideFigureSettings.Interviewer ? 1.1f : 1.5f, role == GuideFigureSettings.Interviewer ? 1.35f : 1.7f));
                    Assert.That(Mathf.Max(bounds.size.x, bounds.size.z), Is.InRange(.35f, 1.4f), role + " is as wide as a person.");
                    Assert.That(bounds.min.y, Is.EqualTo(0).Within(.02f), role + " stands on its origin.");
                    Assert.That(figure.FromModel, Is.False, "The woven figure is built, not a replacement model.");
                    Assert.That(figure.Seated, Is.EqualTo(role == GuideFigureSettings.Interviewer), "The hearth guide sits; the companion walks.");
                    Assert.That(figure.Renderers.Select(renderer => renderer.GetComponent<MeshFilter>().sharedMesh.name).Count(name => name.Contains("Body")),
                        Is.EqualTo(1), "Tunic, belt and sayo are one mesh.");
                    Assert.That(figure.GetComponentsInChildren<Collider>(), Is.Empty);
                    Assert.That(figure.Renderers.SelectMany(renderer => renderer.sharedMaterials).All(material => material != null
                        && material.shader.name == "Universal Render Pipeline/Lit"), Is.True, "Tones come from the shipped URP material.");
                    Assert.That(figure.Head, Is.Not.Null);
                }
                finally { Object.DestroyImmediate(holder); }
            }
        }

        /// <summary>Every face of the woven figure is drawn from outside: Unity culls the inside of clockwise triangles.</summary>
        [Test]
        public void TheWovenFigureFacesOutward()
        {
            foreach (var (name, mesh, axis) in new[]
            {
                ("body", GuideFigure.BodyMesh(), Vector3.zero), ("seated body", GuideFigure.SeatedBodyMesh(), Vector3.zero),
                ("head", GuideFigure.HeadMesh(), Vector3.zero), ("leg", GuideFigure.LegMesh(), Vector3.zero)
            })
            {
                var copy = Object.Instantiate(mesh);
                try
                {
                    Assert.That(copy.bounds.size.magnitude, Is.GreaterThan(.1f), name);
                    int outward = 0, total = 0;
                    var vertices = new System.Collections.Generic.List<Vector3>();
                    copy.GetVertices(vertices);
                    for (int sub = 0; sub < copy.subMeshCount; sub++)
                    {
                        var triangles = copy.GetTriangles(sub);
                        for (int index = 0; index < triangles.Length; index += 3)
                        {
                            Vector3 a = vertices[triangles[index]], b = vertices[triangles[index + 1]], c = vertices[triangles[index + 2]];
                            var normal = Vector3.Cross(b - a, c - a);
                            if (normal.sqrMagnitude < 1e-12f) continue;
                            var centre = (a + b + c) / 3;
                            var away = new Vector3(centre.x, 0, centre.z);
                            // Caps and nearly flat rings face up or down; the sides face away from the axis.
                            if (away.sqrMagnitude < 1e-6f) continue;
                            total++;
                            if (Vector3.Dot(normal, away) > 0 || Mathf.Abs(normal.normalized.y) > .7f) outward++;
                        }
                    }
                    Assert.That(outward, Is.GreaterThanOrEqualTo(total * .97f), name + ": " + outward + " of " + total + " faces drawn outward.");
                }
                finally { Object.DestroyImmediate(copy); }
            }
        }

        [Test]
        public void FigureSettingsRejectImpossibleHeightsAndColours()
        {
            Assert.That(() => GuideFigureSettings.Parse("{\"schema_version\":1,\"figures\":{\"companion\":{\"height_m\":12}}}"),
                Throws.InvalidOperationException);
            Assert.That(() => GuideFigureSettings.Parse("{\"schema_version\":1,\"figures\":{\"companion\":{\"sayo_color\":\"not a colour\"}}}"),
                Throws.InvalidOperationException);
            Assert.That(() => GuideFigureSettings.Parse("{\"schema_version\":2,\"figures\":{}}"), Throws.InvalidOperationException);
            var parsed = GuideFigureSettings.Parse("{\"schema_version\":1,\"figures\":{\"interviewer\":{\"model\":\"Guides/Other\",\"height_m\":1.7}}}");
            Assert.That(parsed.For("interviewer").Model, Is.EqualTo("Guides/Other"));
            Assert.That(parsed.For("companion").Height, Is.EqualTo(1.6f), "A role without an entry gets the woven figure.");
        }

        [Test]
        public void AMissingReplacementModelFallsBackToTheWovenFigure()
        {
            var holder = new GameObject("Holder");
            try
            {
                UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("woven figure"));
                var figure = GuideFigure.Build(holder.transform, new GuideFigureDefinition { Model = "Guides/DoesNotExist" });
                Assert.That(figure.FromModel, Is.False);
                Assert.That(figure.Renderers, Is.Not.Empty);
            }
            finally { Object.DestroyImmediate(holder); }
        }
    }
}
