using System.Linq;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    /// <summary>Owner, Web test of 23/10: a starry night instead of black behind the fire of the credits and the title.</summary>
    public sealed class MuseumNightSkyTests
    {
        [Test]
        public void TheNightOverTheFireIsTheMuseumsSkyShaderWithStars()
        {
            var shared = Resources.Load<Material>(MuseumEntryBackdrop.NightSkyMaterial);
            Assert.That(shared, Is.Not.Null, "The night travels in Resources, with its shader.");
            Assert.That(shared.shader.name, Is.EqualTo("Musiyo/Sky Panorama"), "The sky shader the visit already draws on the Web and the headset.");
            var sky = MuseumEntryBackdrop.NightSky();
            var stars = sky.GetTexture("_MainTex");
            Assert.That(stars, Is.Not.Null);
            Assert.That((stars.width, stars.height), Is.EqualTo((MuseumEntryBackdrop.NightSkyWidth, MuseumEntryBackdrop.NightSkyHeight)));
            Assert.That(shared.GetTexture("_MainTex"), Is.Null, "The stars are drawn into a copy, never into the project's material.");
        }

        [Test]
        public void StarsSpreadOverTheUpperHalfOnly()
        {
            var pixels = MuseumEntryBackdrop.StarPixels();
            int width = MuseumEntryBackdrop.NightSkyWidth, height = MuseumEntryBackdrop.NightSkyHeight;
            Assert.That(pixels.Length, Is.EqualTo(width * height));
            var below = pixels.Take(width * height / 2).ToArray();
            var above = pixels.Skip(width * height / 2).ToArray();
            Assert.That(below.Max(pixel => pixel.g), Is.LessThan(12), "Nothing shines below the horizon.");
            int shining = above.Count(pixel => pixel.g > 60);
            Assert.That(shining, Is.InRange(1500, 8000), "Some thousands of stars.");
            Assert.That(above.Average(pixel => (float)pixel.g), Is.LessThan(12), "A night: dark between the stars.");
            Assert.That(above.Count(pixel => pixel.g > 200), Is.InRange(20, 1500), "A few bright ones.");
        }
    }
}
