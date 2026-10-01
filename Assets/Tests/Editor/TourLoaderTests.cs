using System;
using MusiyoBetsknate.Museum;
using NUnit.Framework;

namespace MusiyoBetsknate.Tests
{
    public sealed class TourLoaderTests
    {
        [TestCase("https://museum.example/recorrido?point=test", "https://museum.example/api/v1")]
        [TestCase("http://localhost:5173/unity/index.html", "http://localhost:5173/api/v1")]
        [TestCase("", "http://127.0.0.1:8000/api/v1")]
        public void UsesPageOriginOrLocalEditorDefault(string page, string expected)
        {
            Assert.That(TourLoader.ResolveApiBase("", page), Is.EqualTo(expected));
        }

        [Test]
        public void NativeConfigurationOverridesPageAndRejectsUnsupportedSchemes()
        {
            Assert.That(TourLoader.ResolveApiBase("https://api.example/api/v1/", ""), Is.EqualTo("https://api.example/api/v1"));
            Assert.Throws<ArgumentException>(() => TourLoader.ResolveApiBase("file:///private", ""));
        }
    }
}
