using System;
using System.Linq;
using MusiyoBetsknate.Museo;
using Newtonsoft.Json;
using UnityEngine.Networking;

namespace MusiyoBetsknate.Museo
{
    public sealed partial class MuseumCreditsContractV1
    {
        /// <summary>The museum's closing credits as published; sections and lines keep the order they arrive in.</summary>
        public static bool TryParse(string json, out MuseumCreditsContractV1 contract)
        {
            contract = null;
            try
            {
                contract = ContractJsonV1.Parse<MuseumCreditsContractV1>(json, ContractSchemasV1.MuseumCredits);
                return true;
            }
            catch (JsonException) { return false; }
        }

        /// <summary>True when there is something to roll: a title and at least one line with text.</summary>
        public bool HasLines => !string.IsNullOrWhiteSpace(title)
            && (sections ?? Array.Empty<CreditSectionContractV1>()).Any(section => (section.lines ?? Array.Empty<CreditLineContractV1>())
                .Any(line => !string.IsNullOrWhiteSpace(line.text)));
    }
}

namespace MusiyoBetsknate.Museum
{
    public enum MuseumCreditsState
    {
        Idle,
        Loading,
        /// <summary>Credits arrived and have lines to roll.</summary>
        Ready,
        /// <summary>No answer, an error status or a timeout.</summary>
        Failed,
        /// <summary>HTTP 200 whose body does not follow the shared contract, or holds nothing to roll.</summary>
        Incompatible
    }

    /// <summary>
    /// The closing credits of the museum: GET {api}/credits?schema_version=1. Everything shown at the end comes from this
    /// answer; nothing is invented or reordered. One request at a time; its owner calls <see cref="Poll"/> every frame.
    /// </summary>
    public sealed class MuseumCreditsClient : IDisposable
    {
        public const float DefaultTimeoutSeconds = 15;
        private readonly Func<string> apiBase;
        private readonly Func<double> clock;
        private readonly float timeoutSeconds;
        private UnityWebRequest active;
        private UnityWebRequestAsyncOperation operation;
        private double deadline;

        public MuseumCreditsState State { get; private set; }
        public MuseumCreditsContractV1 Credits { get; private set; }
        public bool Ready => State == MuseumCreditsState.Ready && Credits != null;
        public event Action Changed;

        public MuseumCreditsClient(Func<string> apiBase, Func<double> clock, float timeoutSeconds = DefaultTimeoutSeconds)
        {
            this.apiBase = apiBase ?? throw new ArgumentNullException(nameof(apiBase));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            if (timeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            this.timeoutSeconds = timeoutSeconds;
        }

        /// <summary>Asks for the credits unless they are already here or on their way.</summary>
        public void Load()
        {
            if (State == MuseumCreditsState.Ready || State == MuseumCreditsState.Loading) return;
            string url;
            try { url = apiBase() + "/credits?schema_version=1"; }
            catch (ArgumentException) { Set(MuseumCreditsState.Failed); return; }
            active = UnityWebRequest.Get(url);
            active.SetRequestHeader("Accept", "application/json");
            active.timeout = (int)Math.Ceiling(timeoutSeconds) + 5;
            deadline = clock() + timeoutSeconds;
            try { operation = active.SendWebRequest(); }
            catch (InvalidOperationException) { operation = null; }
            if (operation == null) { Abandon(); Set(MuseumCreditsState.Failed); return; }
            Set(MuseumCreditsState.Loading);
        }

        public void Poll()
        {
            if (active == null) return;
            if (!operation.isDone)
            {
                if (clock() < deadline) return;
                Abandon();
                Set(MuseumCreditsState.Failed);
                return;
            }
            var result = active.result;
            var code = active.responseCode;
            var text = active.downloadHandler?.text;
            Abandon();
            var state = Classify(result, code, text, out var credits);
            if (credits != null) Credits = credits;
            Set(state);
        }

        public static MuseumCreditsState Classify(UnityWebRequest.Result result, long code, string body, out MuseumCreditsContractV1 credits)
        {
            credits = null;
            if (code != 200 || result != UnityWebRequest.Result.Success) return MuseumCreditsState.Failed;
            if (!MuseumCreditsContractV1.TryParse(body, out var parsed) || !parsed.HasLines) return MuseumCreditsState.Incompatible;
            credits = parsed;
            return MuseumCreditsState.Ready;
        }

        private void Abandon()
        {
            if (active == null) return;
            if (operation != null && !operation.isDone) active.Abort();
            active.Dispose();
            active = null;
            operation = null;
        }

        private void Set(MuseumCreditsState state)
        {
            State = state;
            Changed?.Invoke();
        }

        public void Dispose() => Abandon();
    }
}
