using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;

namespace MusiyoBetsknate.Museum
{
    /// <summary>The things the first-visit tutorial asks the visitor to try, each learnt by doing it.</summary>
    public enum TutorialStep { Turn, Move, Menu, Approach, Grab, Companion }

    /// <summary>One step of the tutorial as the settings list it: what it is and the catalog text that proposes it.</summary>
    [Preserve]
    public sealed class TutorialStepSettings
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("text_key")] public string TextKey;
        /// <summary>The step needs the controllers: with the hands alone the visitor is told to take them first.</summary>
        [JsonProperty("needs_controllers")] public bool NeedsControllers;
    }

    /// <summary>The first-visit tutorial, read from <c>Resources/MuseumTutorial.json</c>.</summary>
    [Preserve]
    public sealed class MuseumTutorialSettings
    {
        public const string ResourceName = "MuseumTutorial";
        public static readonly Dictionary<string, TutorialStep> Ids = new()
        {
            { "turn", TutorialStep.Turn }, { "move", TutorialStep.Move }, { "menu", TutorialStep.Menu },
            { "approach", TutorialStep.Approach }, { "grab", TutorialStep.Grab }, { "companion", TutorialStep.Companion },
        };

        [JsonProperty("schema_version")] public int SchemaVersion;
        [JsonProperty("enabled")] public bool Enabled = true;
        /// <summary>Room where the tutorial begins on a first visit.</summary>
        [JsonProperty("start_room")] public string StartRoom = "room.arrival";
        /// <summary>After the companion's welcome has been read, this long before the first step.</summary>
        [JsonProperty("start_after_welcome_seconds")] public float StartAfterWelcome = 1;
        /// <summary>Into the arrival, the tutorial begins at the latest after this long, welcome or not.</summary>
        [JsonProperty("start_at_latest_seconds")] public float StartAtLatest = 14;
        /// <summary>Degrees the visitor's gaze must turn, in all, to have turned.</summary>
        [JsonProperty("turn_degrees")] public float TurnDegrees = 60;
        /// <summary>Metres the visitor must move over the floor, walking or with a jump.</summary>
        [JsonProperty("move_metres")] public float MoveMetres = 1.5f;
        /// <summary>A step's text shows this long, then rests; it comes back every <see cref="RemindSeconds"/> while undone.</summary>
        [JsonProperty("show_seconds")] public float ShowSeconds = 30;
        [JsonProperty("remind_seconds")] public float RemindSeconds = 45;
        /// <summary>A done step is acknowledged this long before the next one.</summary>
        [JsonProperty("done_seconds")] public float DoneSeconds = 1.4f;
        [JsonProperty("end_seconds")] public float EndSeconds = 7;
        /// <summary>Holding the back control this long skips the tutorial.</summary>
        [JsonProperty("skip_hold_seconds")] public float SkipHoldSeconds = .8f;
        [JsonProperty("steps")] public List<TutorialStepSettings> Steps = new();

        private static MuseumTutorialSettings current;
        public static MuseumTutorialSettings Current
        {
            get
            {
                if (current != null) return current;
                var resource = Resources.Load<TextAsset>(ResourceName);
                if (resource == null) throw new InvalidOperationException("Missing museum tutorial settings.");
                return current = Parse(resource.text);
            }
        }

        public static MuseumTutorialSettings Parse(string json)
        {
            var result = JsonConvert.DeserializeObject<MuseumTutorialSettings>(json);
            if (result == null || result.SchemaVersion != 1) throw new InvalidOperationException("Invalid museum tutorial settings.");
            foreach (var value in new[] { result.StartAtLatest, result.TurnDegrees, result.MoveMetres, result.ShowSeconds, result.RemindSeconds,
                result.DoneSeconds, result.EndSeconds, result.SkipHoldSeconds })
                if (!MuseumExperienceConfiguration.Finite(value) || value <= 0) throw new InvalidOperationException("Invalid museum tutorial settings.");
            if (!MuseumExperienceConfiguration.Finite(result.StartAfterWelcome) || result.StartAfterWelcome < 0
                || string.IsNullOrWhiteSpace(result.StartRoom) || result.RemindSeconds <= result.ShowSeconds * .5f)
                throw new InvalidOperationException("Invalid museum tutorial settings.");
            result.Steps ??= new();
            if (result.Steps.Count == 0 || result.Steps.Any(step => step == null || step.Id == null || !Ids.ContainsKey(step.Id)
                    || string.IsNullOrWhiteSpace(step.TextKey))
                || result.Steps.Select(step => step.Id).Distinct().Count() != result.Steps.Count)
                throw new InvalidOperationException("Invalid museum tutorial steps.");
            return result;
        }
    }

    /// <summary>What the visit shows at a moment, for the tutorial to tell which steps the visitor has done.</summary>
    public readonly struct TutorialSignals
    {
        /// <summary>Degrees the gaze turned since the last moment, over the floor.</summary>
        public readonly float Turned;
        /// <summary>Metres the visitor stands from where the tutorial began.</summary>
        public readonly float Moved;
        public readonly bool MenuOpen;
        /// <summary>A point's screen is open, the visitor having come up to its stand.</summary>
        public readonly bool PointOpen;
        public readonly bool Holding;
        /// <summary>The visitor pressed the companion's control or talks with it.</summary>
        public readonly bool CalledCompanion;

        public TutorialSignals(float turned, float moved, bool menuOpen, bool pointOpen, bool holding, bool calledCompanion)
        { Turned = turned; Moved = moved; MenuOpen = menuOpen; PointOpen = pointOpen; Holding = holding; CalledCompanion = calledCompanion; }
    }

    /// <summary>How a tutorial came to an end.</summary>
    public enum TutorialEnding
    {
        /// <summary>Every step was done.</summary>
        Finished,
        /// <summary>The visitor skipped it, holding the back control or from the pause menu.</summary>
        Skipped,
        /// <summary>The visit left the museum, or the app closed, before it ended.</summary>
        LeftHalfway
    }

    /// <summary>
    /// Which steps of the tutorial the visitor has done, in any order: whatever they do counts at once, and the step
    /// proposed is always the first one still undone.
    /// </summary>
    public sealed class MuseumTutorialProgress
    {
        private readonly MuseumTutorialSettings settings;
        private readonly HashSet<TutorialStep> done = new();
        private float turned;

        public MuseumTutorialProgress(MuseumTutorialSettings settings) =>
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));

        public IReadOnlyList<TutorialStepSettings> Steps => settings.Steps;
        public IReadOnlyCollection<TutorialStep> Done => done;
        public bool Finished => settings.Steps.All(step => done.Contains(MuseumTutorialSettings.Ids[step.Id]));
        /// <summary>The first step still undone, or null when every step is done.</summary>
        public TutorialStepSettings Current => settings.Steps.FirstOrDefault(step => !done.Contains(MuseumTutorialSettings.Ids[step.Id]));
        public int CurrentNumber => Current == null ? settings.Steps.Count : settings.Steps.IndexOf(Current) + 1;

        public void MarkDone(TutorialStep step) => done.Add(step);

        /// <summary>
        /// A finished or skipped tutorial is remembered and never starts again by itself; one left halfway starts again the
        /// next time the visitor comes into the arrival (owner, 30/10/2026).
        /// </summary>
        public static bool Remembered(TutorialEnding ending) => ending != TutorialEnding.LeftHalfway;

        /// <summary>Takes in a moment of the visit; returns the steps that it completed.</summary>
        public List<TutorialStep> Observe(TutorialSignals signals)
        {
            var completed = new List<TutorialStep>();
            turned += Mathf.Abs(signals.Turned);
            void Check(TutorialStep step, bool now) { if (now && done.Add(step)) completed.Add(step); }
            Check(TutorialStep.Turn, turned >= settings.TurnDegrees);
            Check(TutorialStep.Move, signals.Moved >= settings.MoveMetres);
            Check(TutorialStep.Menu, signals.MenuOpen);
            Check(TutorialStep.Approach, signals.PointOpen);
            Check(TutorialStep.Grab, signals.Holding);
            Check(TutorialStep.Companion, signals.CalledCompanion);
            return completed;
        }
    }
}
