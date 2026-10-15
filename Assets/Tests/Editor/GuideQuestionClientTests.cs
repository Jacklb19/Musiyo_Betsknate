using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using MusiyoBetsknate.Museum;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.TestTools;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// Exercises the guide client over real HTTP against a local synthetic service that replays the canonical examples.
    /// </summary>
    public sealed class GuideQuestionClientTests
    {
        private const string Question = "¿Qué dice la ficha de prueba?";
        private SyntheticGuideService service;
        private GuideQuestionClient client;
        private double now;

        private static string Example(string name)
            => File.ReadAllText(Path.Combine(Application.dataPath, "Contracts", "examples", name));

        [SetUp]
        public void StartService() => service = new SyntheticGuideService();

        [TearDown]
        public void StopService()
        {
            client?.Dispose();
            client = null;
            service.Dispose();
        }

        private GuideQuestionClient Client(string scenario, float timeout = GuideQuestionClient.DefaultTimeoutSeconds,
            bool realClock = false)
        {
            client?.Dispose();
            client = new GuideQuestionClient(() => service.Base(scenario),
                realClock ? () => Time.realtimeSinceStartupAsDouble : () => now, timeout);
            return client;
        }

        private static IEnumerator Settle(GuideQuestionClient guide, float limit = 10)
        {
            var end = Time.realtimeSinceStartupAsDouble + limit;
            while (guide.Busy && Time.realtimeSinceStartupAsDouble < end)
            {
                guide.Poll();
                yield return null;
            }
            Assert.IsFalse(guide.Busy, "The question did not complete.");
        }

        [UnityTest]
        public IEnumerator TheThreeAnswersArriveAsValidatedResponses()
        {
            foreach (var (scenario, state, citations) in new[]
            {
                ("answered", GuideQuestionState.Answered, 1), ("abstained", GuideQuestionState.Abstained, 0),
                ("unavailable", GuideQuestionState.ProviderUnavailable, 0)
            })
            {
                var guide = Client(scenario);
                Assert.IsTrue(guide.Ask("  " + Question + "  "));
                Assert.AreEqual(GuideQuestionState.Sending, guide.State);
                yield return Settle(guide);
                Assert.AreEqual(state, guide.State, scenario);
                Assert.IsNotNull(guide.Response);
                Assert.AreEqual(citations, guide.Response.citations.Length);
                Assert.AreEqual("  " + Question + "  ", guide.Question, "The visitor's text is kept as written.");
            }
            Assert.IsTrue(service.Requests.TryDequeue(out var request));
            Assert.AreEqual("POST", request.Method);
            StringAssert.StartsWith("application/json", request.ContentType);
            Assert.IsNull(request.Authorization, "Questions are anonymous.");
            Assert.IsNull(request.Cookie, "Questions are anonymous.");
            Assert.IsTrue(MusiyoBetsknate.Museo.GuideQuestionContractV1.TryParse(request.Body, out var body),
                "The request follows the shared schema.");
            Assert.AreEqual(1, body.schema_version);
            Assert.AreEqual(Question, body.question);
            Assert.IsNull(body.element_slug);
        }

        [UnityTest]
        public IEnumerator TheElementSlugOnlyTravelsAsAFilter()
        {
            var guide = Client("answered");
            Assert.IsTrue(guide.Ask(Question, "synthetic-item"));
            yield return Settle(guide);
            Assert.IsTrue(service.Requests.TryDequeue(out var request));
            Assert.IsTrue(MusiyoBetsknate.Museo.GuideQuestionContractV1.TryParse(request.Body, out var body));
            Assert.AreEqual("synthetic-item", body.element_slug);
            Assert.IsNull(request.Authorization);
        }

        [Test]
        public void AQuestionTheContractRejectsIsNeverSent()
        {
            var guide = Client("answered");
            Assert.IsFalse(guide.Ask("ab"));
            Assert.AreEqual(GuideQuestionState.Invalid, guide.State);
            Assert.AreEqual("ab", guide.Question);
            Assert.IsFalse(guide.CanRetry);
            Assert.IsFalse(guide.Ask(new string('a', 501)));
            Assert.IsFalse(guide.Ask(Question, "two words"));
            Assert.AreEqual(0, service.Requests.Count);
        }

        [UnityTest]
        public IEnumerator FailuresAreToldApartAndKeepTheQuestion()
        {
            foreach (var (scenario, state) in new[]
            {
                ("invalid", GuideQuestionState.Rejected), ("server-error", GuideQuestionState.ServiceError),
                ("malformed", GuideQuestionState.Incompatible), ("missing", GuideQuestionState.ServiceError)
            })
            {
                var guide = Client(scenario);
                Assert.IsTrue(guide.Ask(Question));
                yield return Settle(guide);
                Assert.AreEqual(state, guide.State, scenario);
                Assert.IsNull(guide.Response, scenario);
                Assert.AreEqual(Question, guide.Question, scenario);
                Assert.IsTrue(guide.CanRetry, scenario);
            }
        }

        [UnityTest]
        public IEnumerator RateLimitWaitsForTheAnnouncedDelayAndNeverRetriesByItself()
        {
            foreach (var (scenario, seconds) in new[] { ("limited", 60), ("limited-bare", 60), ("limited-garbled", 60), ("limited-short", 2) })
            {
                now = 100;
                var guide = Client(scenario);
                Assert.IsTrue(guide.Ask(Question));
                yield return Settle(guide);
                Assert.AreEqual(GuideQuestionState.RateLimited, guide.State, scenario);
                Assert.AreEqual(seconds, guide.RetryAfterSeconds, scenario);
                Assert.AreEqual(seconds, guide.RetryWaitRemaining, scenario);
                Assert.IsFalse(guide.CanRetry, scenario);
                Assert.IsFalse(guide.Retry(), scenario);
                now += seconds;
                Assert.IsTrue(guide.CanRetry, scenario);
                Assert.AreEqual(Question, guide.Question);
            }
            var before = service.Count;
            yield return null;
            client.Poll();
            Assert.AreEqual(before, service.Count, "Nothing is sent again without the visitor.");
        }

        [UnityTest]
        public IEnumerator ASlowServiceTimesOutWithoutLosingTheQuestion()
        {
            var guide = Client("slow", .5f, realClock: true);
            Assert.IsTrue(guide.Ask(Question));
            yield return Settle(guide);
            Assert.AreEqual(GuideQuestionState.TimedOut, guide.State);
            Assert.AreEqual(Question, guide.Question);
            Assert.IsTrue(guide.CanRetry);
        }

        [UnityTest]
        public IEnumerator ACancelledQuestionNeverReachesTheVisitor()
        {
            var guide = Client("slow-answer", realClock: true);
            var changes = 0;
            guide.Changed += () => changes++;
            Assert.IsTrue(guide.Ask(Question));
            guide.Cancel();
            Assert.AreEqual(GuideQuestionState.Idle, guide.State);
            Assert.AreEqual(Question, guide.Question);
            Assert.IsTrue(guide.CanRetry);
            // The service still answers after a while; the client has already let the request go.
            int seen = changes;
            var end = Time.realtimeSinceStartupAsDouble + 2.5;
            while (Time.realtimeSinceStartupAsDouble < end) { guide.Poll(); yield return null; }
            Assert.AreEqual(seen, changes, "The abandoned question changed nothing.");
            Assert.AreEqual(GuideQuestionState.Idle, guide.State);
            Assert.IsNull(guide.Response);
        }

        [UnityTest]
        public IEnumerator ANewQuestionAbandonsTheOneInFlight()
        {
            // The base is read for each question, so the second one goes to a fast answer.
            var scenario = "slow-answer";
            client = new GuideQuestionClient(() => service.Base(scenario), () => Time.realtimeSinceStartupAsDouble);
            var guide = client;
            Assert.IsTrue(guide.Ask("¿Primera pregunta?"));
            scenario = "abstained";
            Assert.IsTrue(guide.Ask("¿Segunda pregunta?"));
            yield return Settle(guide);
            Assert.AreEqual(GuideQuestionState.Abstained, guide.State);
            Assert.AreEqual("¿Segunda pregunta?", guide.Question);
            var end = Time.realtimeSinceStartupAsDouble + 2.5;
            while (Time.realtimeSinceStartupAsDouble < end) { guide.Poll(); yield return null; }
            Assert.AreEqual(GuideQuestionState.Abstained, guide.State, "The first answer arrived late and was discarded.");
        }

        [UnityTest]
        public IEnumerator AnUnreachableServiceIsAFailureOfTransport()
        {
            client = new GuideQuestionClient(() => "http://127.0.0.1:" + SyntheticGuideService.ClosedPort() + "/api/v1",
                () => Time.realtimeSinceStartupAsDouble);
            Assert.IsTrue(client.Ask(Question));
            yield return Settle(client);
            Assert.AreEqual(GuideQuestionState.Unreachable, client.State);
            Assert.AreEqual(Question, client.Question);
        }

        [TestCase(null, 60)]
        [TestCase("", 60)]
        [TestCase("60", 60)]
        [TestCase(" 5 ", 5)]
        [TestCase("-3", 60)]
        [TestCase("soon", 60)]
        [TestCase("Thu, 15 Oct 2026 12:00:30 GMT", 30)]
        [TestCase("Thu, 15 Oct 2026 11:00:00 GMT", 0)]
        public void RetryAfterFallsBackToTheMinute(string header, int expected)
            => Assert.AreEqual(expected, GuideQuestionClient.ParseRetryAfter(header,
                new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero)));

        [Test]
        public void TheBodyMustFollowTheSharedSchemaForItsStatus()
        {
            Assert.AreEqual(GuideQuestionState.Answered, GuideQuestionClient.Classify(UnityWebRequest.Result.Success, 200, null,
                Example("guide_answered.json"), out var answered));
            Assert.AreEqual("synthetic-item", answered.citations[0].element_slug);
            Assert.AreEqual(GuideQuestionState.Incompatible, GuideQuestionClient.Classify(UnityWebRequest.Result.Success, 200, null,
                Example("guide_abstained.json").Replace("\"answer\": null", "\"answer\": \"Texto sin respaldo.\""), out var rejected));
            Assert.IsNull(rejected);
            Assert.AreEqual(GuideQuestionState.ServiceError, GuideQuestionClient.Classify(UnityWebRequest.Result.Success, 201, null,
                Example("guide_answered.json"), out _), "Only HTTP 200 carries the three answers.");
            Assert.AreEqual(GuideQuestionState.TimedOut, GuideQuestionClient.Classify(UnityWebRequest.Result.ConnectionError, 0,
                "Request timeout", null, out _));
            Assert.AreEqual(GuideQuestionState.Unreachable, GuideQuestionClient.Classify(UnityWebRequest.Result.ConnectionError, 0,
                "Cannot connect to destination host", null, out _));
        }

        private sealed class RecordedRequest
        {
            public string Method, ContentType, Authorization, Cookie, Body;
        }

        /// <summary>Local stand-in for the guide endpoint, answering by scenario with the canonical examples.</summary>
        private sealed class SyntheticGuideService : IDisposable
        {
            private readonly HttpListener listener = new HttpListener();
            private readonly string answered = Example("guide_answered.json");
            private readonly string abstained = Example("guide_abstained.json");
            private readonly string unavailable = Example("guide_unavailable.json");
            private readonly int port;
            private int count;
            public ConcurrentQueue<RecordedRequest> Requests { get; } = new ConcurrentQueue<RecordedRequest>();
            public int Count => count;

            public SyntheticGuideService()
            {
                port = ClosedPort();
                listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                listener.Start();
                listener.BeginGetContext(Receive, null);
            }

            public string Base(string scenario) => "http://127.0.0.1:" + port + "/" + scenario + "/api/v1";

            public static int ClosedPort()
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                var free = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
                return free;
            }

            private void Receive(IAsyncResult pending)
            {
                HttpListenerContext context;
                try { context = listener.EndGetContext(pending); }
                catch (Exception) { return; }
                try { listener.BeginGetContext(Receive, null); } catch (Exception) { }
                ThreadPool.QueueUserWorkItem(_ => Respond(context));
            }

            private void Respond(HttpListenerContext context)
            {
                var request = context.Request;
                string body;
                using (var reader = new StreamReader(request.InputStream, Encoding.UTF8)) body = reader.ReadToEnd();
                var scenario = request.Url.AbsolutePath.Split('/')[1];
                bool known = request.Url.AbsolutePath.EndsWith("/api/v1/guide/questions");
                Interlocked.Increment(ref count);
                Requests.Enqueue(new RecordedRequest
                {
                    Method = request.HttpMethod, ContentType = request.ContentType,
                    Authorization = request.Headers["Authorization"], Cookie = request.Headers["Cookie"], Body = body
                });
                var response = context.Response;
                try
                {
                    if (!known || scenario == "missing") { Send(response, 404, "{\"detail\":\"Not Found\"}"); return; }
                    switch (scenario)
                    {
                        case "answered": Send(response, 200, answered); break;
                        case "abstained": Send(response, 200, abstained); break;
                        case "unavailable": Send(response, 200, unavailable); break;
                        case "invalid": Send(response, 422, "{\"type\":\"about:blank\",\"title\":\"Unprocessable\",\"status\":422}"); break;
                        case "server-error": Send(response, 500, "{\"title\":\"Error\"}"); break;
                        case "malformed": Send(response, 200, answered.Replace("\"reason\": null", "\"reason\": \"insufficient_evidence\"")); break;
                        case "limited": response.AddHeader("Retry-After", "60"); Send(response, 429, Problem()); break;
                        case "limited-bare": Send(response, 429, Problem()); break;
                        case "limited-garbled": response.AddHeader("Retry-After", "later"); Send(response, 429, Problem()); break;
                        case "limited-short": response.AddHeader("Retry-After", "2"); Send(response, 429, Problem()); break;
                        case "slow": Thread.Sleep(3000); Send(response, 200, answered); break;
                        case "slow-answer": Thread.Sleep(1500); Send(response, 200, answered); break;
                        default: Send(response, 404, "{}"); break;
                    }
                }
                catch (Exception) { /* The client may have abandoned the request. */ }
            }

            private static string Problem() => "{\"type\":\"about:blank\",\"title\":\"Too Many Requests\",\"status\":429}";

            private static void Send(HttpListenerResponse response, int status, string json)
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                response.StatusCode = status;
                response.ContentType = status == 200 ? "application/json" : "application/problem+json";
                response.ContentLength64 = bytes.Length;
                response.OutputStream.Write(bytes, 0, bytes.Length);
                response.OutputStream.Close();
            }

            public void Dispose()
            {
                try { listener.Stop(); listener.Close(); } catch (Exception) { }
            }
        }
    }
}
