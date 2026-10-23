using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    /// <summary>Which walkable surfaces of the delivered museum the automatic tour treats as paths.</summary>
    public sealed class AutoTourPathTests
    {
        [TestCase("Sendero_Jajan", MuseumWayfinding.PathArea)]
        [TestCase("Umbral_Personajes_Sur", MuseumWayfinding.PathArea)]
        [TestCase("Pasaje_Norte_Piso", MuseumWayfinding.PathArea)]
        [TestCase("Personajes_Piso_Patio", MuseumWayfinding.PathArea)]
        [TestCase("Bienvenida_Piso", MuseumWayfinding.PathArea)]
        [TestCase("Mirador_Piso_Madera_Piso", MuseumWayfinding.PathArea)]
        [TestCase("Terreno_Valle_Provisional", MuseumWayfinding.OpenGroundArea)]
        [TestCase("ValleyGround", MuseumWayfinding.OpenGroundArea)]
        [TestCase("Jajan_Maiz", MuseumWayfinding.OpenGroundArea)]
        [TestCase("Muro_Norte", 0)]
        public void SurfacesAreSortedIntoPathsAndOpenGround(string name, int area)
            => Assert.That(MuseumWayfinding.WalkArea(name), Is.EqualTo(area));

        [Test]
        public void ASurfaceTakesTheAreaOfTheDeliveredObjectItBelongsTo()
        {
            var ground = new GameObject("Terreno_Valle_Provisional");
            try
            {
                var collider = new GameObject("Collider");
                collider.transform.SetParent(ground.transform);
                Assert.That(MuseumWayfinding.WalkArea(collider.transform), Is.EqualTo(MuseumWayfinding.OpenGroundArea));
                Assert.That(MuseumWayfinding.OpenGroundCost, Is.GreaterThan(3), "Open ground costs several times a path.");
            }
            finally { Object.DestroyImmediate(ground); }
        }
    }
}
