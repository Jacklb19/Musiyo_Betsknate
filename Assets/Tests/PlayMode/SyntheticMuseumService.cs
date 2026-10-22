using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace MusiyoBetsknate.Tests
{
    /// <summary>
    /// A local stand-in for the museum API serving synthetic element records with 3D models, through the same resource
    /// access contract as the real service. It counts model downloads and can be told to fail one.
    /// </summary>
    public sealed class SyntheticMuseumService : IDisposable
    {
        private readonly HttpListener listener = new HttpListener();
        private readonly ConcurrentDictionary<string, byte[]> models = new ConcurrentDictionary<string, byte[]>();
        private readonly ConcurrentDictionary<string, string> titles = new ConcurrentDictionary<string, string>();
        private readonly ConcurrentDictionary<string, int> downloads = new ConcurrentDictionary<string, int>();
        private readonly ConcurrentDictionary<string, string> records = new ConcurrentDictionary<string, string>();
        public string Base { get; }
        /// <summary>Slugs whose model download answers 404.</summary>
        public ConcurrentDictionary<string, bool> Failing { get; } = new ConcurrentDictionary<string, bool>();
        /// <summary>Delay added to every model download, to let a test change its mind mid-flight.</summary>
        public int ModelDelayMilliseconds { get; set; }
        /// <summary>Lifetime of each granted access, in seconds; the real service grants five minutes.</summary>
        public int AccessSeconds { get; set; } = 600;
        /// <summary>Slugs whose access is no longer granted (HTTP 404), as after a revocation.</summary>
        public ConcurrentDictionary<string, bool> Revoked { get; } = new ConcurrentDictionary<string, bool>();
        private readonly ConcurrentDictionary<string, int> grants = new ConcurrentDictionary<string, int>();
        /// <summary>Body of GET /credits; without one the credits are not published (HTTP 404).</summary>
        public string Credits { get; set; }
        public int CreditRequests { get; private set; }
        /// <summary>The canonical synthetic credits of the shared contract.</summary>
        public static string ExampleCredits => Example("museum_credits.json");

        public SyntheticMuseumService()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            listener.Start();
            listener.BeginGetContext(Receive, null);
            Base = "http://127.0.0.1:" + port + "/api/v1";
        }

        public void AddModel(string slug, string title, byte[] glb)
        {
            models[slug] = glb;
            titles[slug] = title;
        }

        /// <summary>Serves a record as given, for instance one with text only and no resources.</summary>
        public void AddRecord(string slug, string json) => records[slug] = json;

        /// <summary>The canonical synthetic record under another slug and title, with its text, source and credit and no resources.</summary>
        public static string TextRecord(string slug, string title)
            => Example("element.json").Replace("\"slug\": \"synthetic-item\"", "\"slug\": \"" + slug + "\"")
                .Replace("\"title\": \"Elemento sintético\"", "\"title\": \"" + title + "\"");

        public int Downloads(string slug) => downloads.TryGetValue(slug, out var count) ? count : 0;

        /// <summary>Accesses granted for a slug's model, first grants and renewals alike.</summary>
        public int Grants(string slug) => grants.TryGetValue(slug, out var count) ? count : 0;

        private static string Example(string name)
            => File.ReadAllText(Path.Combine(Application.dataPath, "Contracts", "examples", name));

        private string Element(string slug)
        {
            var resources = "\"resources\": [{\"id\": \"" + slug + "-model\", \"kind\": \"model_3d\", \"mime\": \"model/gltf-binary\", \"byte_count\": "
                + models[slug].Length + ", \"alternative_text\": \"Modelo sintético de prueba.\", \"variants\": [{\"id\": \"" + slug
                + "-web\", \"profile\": \"web\"}, {\"id\": \"" + slug + "-quest\", \"profile\": \"quest\"}]}]";
            return Example("element.json").Replace("\"slug\": \"synthetic-item\"", "\"slug\": \"" + slug + "\"")
                .Replace("\"title\": \"Elemento sintético\"", "\"title\": \"" + titles[slug] + "\"")
                .Replace("\"resources\": []", resources);
        }

        private string Access(string slug) => "{\"schema_version\": 1, \"url\": \"/files/" + slug + ".glb\", \"expires_at\": \""
            + DateTimeOffset.UtcNow.AddSeconds(AccessSeconds).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
            + "\", \"mime\": \"model/gltf-binary\", \"byte_count\": " + models[slug].Length + ", \"sha256\": null}";

        private void Receive(IAsyncResult pending)
        {
            HttpListenerContext context;
            try { context = listener.EndGetContext(pending); listener.BeginGetContext(Receive, null); }
            catch (Exception) { return; }
            ThreadPool.QueueUserWorkItem(_ => Respond(context));
        }

        private void Respond(HttpListenerContext context)
        {
            try
            {
                var path = context.Request.Url.AbsolutePath;
                byte[] body = null;
                string type = "application/json";
                foreach (var record in records)
                    if (path == "/api/v1/elements/" + record.Key) body = Encoding.UTF8.GetBytes(record.Value);
                if (path == "/api/v1/credits")
                {
                    CreditRequests++;
                    if (Credits != null) body = Encoding.UTF8.GetBytes(Credits);
                }
                foreach (var slug in models.Keys)
                {
                    if (path == "/api/v1/elements/" + slug) body = Encoding.UTF8.GetBytes(Element(slug));
                    else if ((path == "/api/v1/resources/" + slug + "-web/access" || path == "/api/v1/resources/" + slug + "-quest/access")
                        && !Revoked.ContainsKey(slug))
                    {
                        grants.AddOrUpdate(slug, 1, (_, count) => count + 1);
                        body = Encoding.UTF8.GetBytes(Access(slug));
                    }
                    else if (path == "/files/" + slug + ".glb")
                    {
                        downloads.AddOrUpdate(slug, 1, (_, count) => count + 1);
                        if (ModelDelayMilliseconds > 0) Thread.Sleep(ModelDelayMilliseconds);
                        if (!Failing.ContainsKey(slug)) { body = models[slug]; type = "model/gltf-binary"; }
                    }
                }
                int status = body != null ? 200 : 404;
                body ??= Encoding.UTF8.GetBytes("{\"detail\":\"Not Found\"}");
                context.Response.StatusCode = status;
                context.Response.ContentType = type;
                context.Response.ContentLength64 = body.Length;
                context.Response.OutputStream.Write(body, 0, body.Length);
                context.Response.OutputStream.Close();
            }
            catch (Exception) { }
        }

        public void Dispose()
        {
            try { listener.Stop(); listener.Close(); } catch (Exception) { }
        }
    }
}
