using System;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using System.Reflection;

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
        public void RejectedTransportSettlesUnavailableInsteadOfLeavingEntryLoading()
        {
            var root = new GameObject("TransportTest");
            try
            {
                var loader = root.AddComponent<TourLoader>();
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(TourLoader).GetField("sendRequest", flags).SetValue(loader,
                    new Func<UnityWebRequest, UnityWebRequestAsyncOperation>(_ => throw new InvalidOperationException("Transport denied")));
                var states = new System.Collections.Generic.List<TourLoadState>();
                loader.Changed += () => states.Add(loader.State);
                var load = (System.Collections.IEnumerator)typeof(TourLoader).GetMethod("Load", flags).Invoke(loader, null);
                Assert.That(load.MoveNext(), Is.False);
                Assert.That(states, Is.EqualTo(new[] { TourLoadState.Loading, TourLoadState.Unavailable }));
                Assert.That(loader.State, Is.EqualTo(TourLoadState.Unavailable));
                Assert.That(loader.Status, Is.EqualTo(MuseumInterfaceText.Get("tour_unavailable")));
                Assert.That(loader.RetryScheduled, Is.True, "An unreachable service is tried again without the visitor's help.");
                Assert.That(loader.RetryInSeconds, Is.EqualTo(MuseumExperienceConfiguration.Current.ServiceRetrySeconds[0]).Within(.5f));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void RetriesWaitLongerEachTimeUpToTheLastConfiguredWait()
        {
            var waits = new[] { 5f, 10f, 30f };
            Assert.That(TourLoader.RetryDelay(waits, 1), Is.EqualTo(5));
            Assert.That(TourLoader.RetryDelay(waits, 2), Is.EqualTo(10));
            Assert.That(TourLoader.RetryDelay(waits, 3), Is.EqualTo(30));
            Assert.That(TourLoader.RetryDelay(waits, 12), Is.EqualTo(30));
        }

        [Test]
        public void NativeConfigurationOverridesPageAndRejectsUnsupportedSchemes()
        {
            Assert.That(TourLoader.ResolveApiBase("https://api.example/api/v1/", ""), Is.EqualTo("https://api.example/api/v1"));
            Assert.Throws<ArgumentException>(() => TourLoader.ResolveApiBase("file:///private", ""));
        }

        [Test]
        public void BuildsFindTheDeployedApiInTheirOwnOrder()
        {
            const string deployed = "https://museo.example.org/api/v1";
            // A browser page always serves its own API, whatever a build or file says.
            Assert.That(MuseumServiceAddress.Resolve(null, "https://visita.example/museo", true, deployed),
                Is.EqualTo("https://visita.example/api/v1"));
            // A headset or desktop build uses the configured address, then the local development API.
            Assert.That(MuseumServiceAddress.Resolve(null, "", false, deployed), Is.EqualTo(deployed));
            Assert.That(MuseumServiceAddress.Resolve(null, "", false, null), Is.EqualTo(MuseumServiceAddress.LocalDevelopment));
            // A development scene or a test sets its own address above everything.
            Assert.That(MuseumServiceAddress.Resolve("http://127.0.0.1:9000/api/v1", "https://visita.example/", true, deployed),
                Is.EqualTo("http://127.0.0.1:9000/api/v1"));
        }

        [Test]
        public void TheServiceFileHoldsOneAddressAndNothingElse()
        {
            Assert.That(MuseumServiceAddress.TryParse("{\"schema_version\": 1, \"api_base\": \"https://museo.example.org\"}", out var address), Is.True);
            Assert.That(address, Is.EqualTo("https://museo.example.org/api/v1"), "An origin alone gets the API path.");
            Assert.That(MuseumServiceAddress.TryParse("{\"schema_version\": 1, \"api_base\": \"\"}", out address), Is.True);
            Assert.That(address, Is.Null, "An empty address means not set.");
            Assert.That(MuseumServiceAddress.TryParse("{\"schema_version\": 2, \"api_base\": \"https://museo.example.org\"}", out _), Is.False);
            Assert.That(MuseumServiceAddress.TryParse("{\"schema_version\": 1, \"api_base\": \"ftp://museo.example.org\"}", out _), Is.False);
            Assert.That(MuseumServiceAddress.TryParse("{\"schema_version\": 1, \"api_base\": \"https://user:key@museo.example.org\"}", out _), Is.False,
                "Credentials never travel in the address.");
            Assert.That(MuseumServiceAddress.TryParse("not json", out _), Is.False);
            var built = Resources.Load<TextAsset>(MuseumServiceAddress.ResourceName);
            Assert.That(built, Is.Not.Null);
            Assert.That(MuseumServiceAddress.TryParse(built.text, out address), Is.True);
            Assert.That(address, Is.Null, "The repository keeps no deployed address; builds bake it in with -musiyoApiBase.");
        }
    }
}
