using System.Linq;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    public sealed class GuideFigureTests
    {
        [Test]
        public void BothGuidesAreBallsOfYarnThatNeedNothingFromTheMuseumModel()
        {
            var settings = GuideFigureSettings.Current;
            foreach (var role in new[] { GuideFigureSettings.Companion, GuideFigureSettings.Interviewer })
            {
                Assert.That(settings.Figures.ContainsKey(role), Is.True, role);
                var definition = settings.For(role);
                Assert.That(definition.Kind, Is.EqualTo(GuideFigureDefinition.Yarn), role + ": no human figure (owner, 22/10/2026).");
                var holder = new GameObject("Holder");
                try
                {
                    var figure = GuideFigure.Build(holder.transform, role);
                    Assert.That(figure.Floats && !figure.FromModel && !figure.Seated, Is.True);
                    var bounds = figure.WorldBounds();
                    // Turns of thread crossing the ball widen its box a little.
                    Assert.That(bounds.size.y, Is.InRange(definition.Diameter * .95f, definition.Diameter * 1.4f), role + " is a ball of its diameter.");
                    Assert.That(bounds.center.y, Is.EqualTo(definition.FloatHeight).Within(.05f), role + " floats at its height.");
                    Assert.That(figure.Height, Is.EqualTo(definition.FloatHeight + definition.Diameter * .5f).Within(.01f));
                    Assert.That(figure.GetComponentsInChildren<Collider>(), Is.Empty);
                    Assert.That(figure.Renderers.SelectMany(renderer => renderer.sharedMaterials).All(material => material != null
                        && material.shader.name == "Universal Render Pipeline/Lit" && !material.IsKeywordEnabled("_EMISSION")), Is.True,
                        "Opaque wool from the shipped URP material, never glowing.");
                    Assert.That(figure.Head, Is.Not.Null);
                }
                finally { Object.DestroyImmediate(holder); }
            }
        }

        [Test]
        public void TheWovenFigureStaysAvailableThroughTheSettings()
        {
            var holder = new GameObject("Holder");
            try
            {
                var figure = GuideFigure.Build(holder.transform, new GuideFigureDefinition { Kind = GuideFigureDefinition.Woven });
                Assert.That(figure.Floats, Is.False);
                Assert.That(figure.WorldBounds().size.y, Is.EqualTo(1.6f).Within(.05f));
                Assert.That(figure.Renderers.Select(renderer => renderer.GetComponent<MeshFilter>().sharedMesh.name).Count(name => name.Contains("Body")), Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(holder); }
        }

        /// <summary>Every face of the woven figure is drawn from outside: Unity culls the inside of clockwise triangles.</summary>
        [Test]
        public void TheWovenFigureFacesOutward()
        {
            foreach (var (name, mesh, axis) in new[]
            {
                ("body", GuideFigure.BodyMesh(), Vector3.zero), ("seated body", GuideFigure.SeatedBodyMesh(), Vector3.zero),
                ("head", GuideFigure.HeadMesh(), Vector3.zero), ("leg", GuideFigure.LegMesh(), Vector3.zero),
                ("ball", GuideFigure.BallMesh(), Vector3.zero)
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
            Assert.That(() => GuideFigureSettings.Parse("{\"schema_version\":1,\"figures\":{\"companion\":{\"kind\":\"ghost\"}}}"),
                Throws.InvalidOperationException, "Only the known forms.");
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
                var figure = GuideFigure.Build(holder.transform, new GuideFigureDefinition { Model = "Guides/DoesNotExist", Kind = GuideFigureDefinition.Woven });
                Assert.That(figure.FromModel, Is.False);
                Assert.That(figure.Renderers, Is.Not.Empty);
            }
            finally { Object.DestroyImmediate(holder); }
        }
    }
}
