using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Scripting;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// A remark the companion may make in one area of the museum. Remarks are content: only the ones marked approved are
    /// ever shown, and approved ones arrive from the content service; this repository ships none.
    /// </summary>
    [Preserve]
    public sealed class CompanionRemark
    {
        [JsonProperty("id")] public string Id;
        /// <summary>Stable room key of the scene (for example room.arrival) where the remark belongs.</summary>
        [JsonProperty("zone")] public string Zone;
        [JsonProperty("text")] public string Text;
        [JsonProperty("approved")] public bool Approved;
        /// <summary>Where the statement comes from; shown nowhere, kept for review.</summary>
        [JsonProperty("source")] public string Source;
    }

    /// <summary>Operational interface copy, kept separate from approved cultural remarks.</summary>
    [Preserve]
    public sealed class CompanionInterfaceRemark
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("zone")] public string Zone;
        [JsonProperty("text_key")] public string TextKey;
    }

    /// <summary>Behaviour of the companion, read from <c>Resources/MuseumCompanion.json</c>.</summary>
    [Preserve]
    public sealed class MuseumCompanionSettings
    {
        public const string ResourceName = "MuseumCompanion";

        [JsonProperty("schema_version")] public int SchemaVersion;
        [JsonProperty("enabled")] public bool Enabled = true;
        /// <summary>Walking distance the companion keeps behind the visitor, along the way the visitor went.</summary>
        [JsonProperty("follow_distance_m")] public float FollowDistance;
        /// <summary>Closer than this, the companion steps back out of the way.</summary>
        [JsonProperty("personal_space_m")] public float PersonalSpace;
        /// <summary>Closer than this to the visitor's eyes the figure is not drawn, so it never fills the view.</summary>
        [JsonProperty("hide_within_m")] public float HideWithin;
        [JsonProperty("side_offset_m")] public float SideOffset;
        [JsonProperty("walk_speed")] public float WalkSpeed;
        [JsonProperty("catch_up_speed")] public float CatchUpSpeed;
        [JsonProperty("catch_up_beyond_m")] public float CatchUpBeyond;
        /// <summary>Farther than this along the way (after a jump of the visitor), the companion rejoins out of sight.</summary>
        [JsonProperty("rejoin_beyond_m")] public float RejoinBeyond;
        [JsonProperty("turn_degrees_per_second")] public float TurnSpeed;
        [JsonProperty("figure_scale")] public float FigureScale;
        /// <summary>Room whose guide anchor is the companion's own place; there it waits by the anchor.</summary>
        [JsonProperty("home_room")] public string HomeRoom;
        /// <summary>
        /// Room where the companion stays behind while the visitor is in it; empty for none. With <see cref="WaitBoxPieces"/>
        /// only the box round those pieces of the room counts, and the companion waits at its edge, inside the room.
        /// </summary>
        [JsonProperty("wait_outside_room")] public string WaitOutside = "";
        /// <summary>
        /// Museum pieces, by the start of their names, whose box (with <see cref="WaitBoxMargin"/>) the companion does not
        /// enter: the fire and the seats round it (owner, 23/10/2026: «una caja exactamente donde está la fogata»).
        /// </summary>
        [JsonProperty("wait_box_pieces")] public List<string> WaitBoxPieces = new();
        [JsonProperty("wait_box_margin_m")] public float WaitBoxMargin = .5f;
        /// <summary>
        /// Where the companion stays in sight (owner, 28/10/2026: «que el colibrí siempre intente ponerse delante del usuario
        /// para poder verlo siempre», in front and a little aside, without chasing every turn of the head).
        /// </summary>
        [JsonProperty("in_view")] public CompanionInViewSettings InView = new();
        [JsonProperty("welcome_text")] public string WelcomeText;
        [JsonProperty("welcome_delay_seconds")] public float WelcomeDelay;
        [JsonProperty("remark_seconds")] public float RemarkSeconds;
        /// <summary>Time in an area before a remark about it, so passing through does not trigger it.</summary>
        [JsonProperty("dwell_seconds")] public float DwellSeconds;
        [JsonProperty("gap_seconds")] public float GapSeconds;
        /// <summary>Record whose model the companion may wear, only where the record allows reuse; empty for none.</summary>
        [JsonProperty("mask_element")] public string MaskElement = "";
        [JsonProperty("remarks")] public List<CompanionRemark> Remarks = new List<CompanionRemark>();
        [JsonProperty("interface_remarks")] public List<CompanionInterfaceRemark> InterfaceRemarks = new();

        private static MuseumCompanionSettings current;
        public static MuseumCompanionSettings Current
        {
            get
            {
                if (current != null) return current;
                var resource = Resources.Load<TextAsset>(ResourceName);
                if (resource == null) throw new InvalidOperationException("Missing museum companion settings.");
                return current = Parse(resource.text);
            }
        }

        public static MuseumCompanionSettings Parse(string json)
        {
            var result = JsonConvert.DeserializeObject<MuseumCompanionSettings>(json);
            if (result == null || result.SchemaVersion != 1) throw new InvalidOperationException("Invalid museum companion settings.");
            foreach (var value in new[] { result.FollowDistance, result.PersonalSpace, result.HideWithin, result.WalkSpeed, result.CatchUpSpeed,
                result.CatchUpBeyond, result.RejoinBeyond, result.TurnSpeed, result.FigureScale, result.RemarkSeconds, result.DwellSeconds })
                if (!MuseumExperienceConfiguration.Finite(value) || value <= 0) throw new InvalidOperationException("Invalid museum companion settings.");
            if (!MuseumExperienceConfiguration.Finite(result.SideOffset) || result.SideOffset < 0 || !MuseumExperienceConfiguration.Finite(result.WelcomeDelay)
                || result.WelcomeDelay < 0 || !MuseumExperienceConfiguration.Finite(result.GapSeconds) || result.GapSeconds < 0
                || result.PersonalSpace >= result.FollowDistance || result.CatchUpBeyond <= result.FollowDistance
                || result.RejoinBeyond <= result.CatchUpBeyond || result.CatchUpSpeed < result.WalkSpeed)
                throw new InvalidOperationException("Invalid museum companion distances.");
            result.Remarks ??= new List<CompanionRemark>();
            if (result.Remarks.Any(remark => remark == null || string.IsNullOrWhiteSpace(remark.Id) || string.IsNullOrWhiteSpace(remark.Zone)
                || string.IsNullOrWhiteSpace(remark.Text)) || result.Remarks.Select(remark => remark.Id).Distinct().Count() != result.Remarks.Count)
                throw new InvalidOperationException("Invalid companion remark.");
            result.MaskElement ??= "";
            result.WaitOutside ??= "";
            result.WaitBoxPieces ??= new();
            if (result.WaitBoxPieces.Any(string.IsNullOrWhiteSpace) || !MuseumExperienceConfiguration.Finite(result.WaitBoxMargin)
                || result.WaitBoxMargin < 0 || result.WaitBoxMargin > 3)
                throw new InvalidOperationException("Invalid companion waiting box.");
            result.InView ??= new();
            if (!result.InView.Valid(result.PersonalSpace)) throw new InvalidOperationException("Invalid companion in-view settings.");
            result.InterfaceRemarks ??= new();
            if (result.InterfaceRemarks.Any(remark => remark == null || string.IsNullOrWhiteSpace(remark.Id)
                || string.IsNullOrWhiteSpace(remark.Zone) || string.IsNullOrWhiteSpace(remark.TextKey)
                || MuseumInterfaceText.Current.Find(remark.TextKey) == null)
                || result.InterfaceRemarks.Select(remark => remark.Id).Concat(result.Remarks.Select(remark => remark.Id))
                    .Distinct().Count() != result.InterfaceRemarks.Count + result.Remarks.Count)
                throw new InvalidOperationException("Invalid companion interface remark.");
            return result;
        }
    }

    /// <summary>
    /// How the companion keeps itself in the visitor's sight: standing, it flies to a free spot in front and to one side of
    /// the gaze once it has been out of sight a moment; walking, it goes a little ahead and aside, never on the way; after
    /// a jump it reappears in sight. Its height follows the visitor's eyes within limits.
    /// </summary>
    [Preserve]
    public sealed class CompanionInViewSettings
    {
        [JsonProperty("enabled")] public bool Enabled = true;
        /// <summary>Distance from the visitor of the spot in sight.</summary>
        [JsonProperty("distance_m")] public float Distance = 1.8f;
        /// <summary>Angle of the spot from the gaze, to either side.</summary>
        [JsonProperty("angle_degrees")] public float Angle = 35;
        /// <summary>Time out of sight before the companion moves back into it, so it never chases a glance.</summary>
        [JsonProperty("out_of_sight_seconds")] public float OutOfSightSeconds = 1.5f;
        /// <summary>Degrees beyond half the field of view that still count as in sight: once seen, a small turn keeps it there.</summary>
        [JsonProperty("sight_margin_degrees")] public float SightMargin = 8;
        /// <summary>Speed of the move back into sight, gentler than walking.</summary>
        [JsonProperty("move_speed")] public float MoveSpeed = 1.3f;
        /// <summary>The bird flies this far below the visitor's eyes, within the lift limits from its own height.</summary>
        [JsonProperty("eye_drop_m")] public float EyeDrop = .15f;
        [JsonProperty("lift_min_m")] public float LiftMin = -.45f;
        [JsonProperty("lift_max_m")] public float LiftMax = .35f;
        /// <summary>Vertical speed of the height change, in metres per second.</summary>
        [JsonProperty("lift_speed")] public float LiftSpeed = .4f;
        /// <summary>Walking, the companion keeps this far ahead along the visitor's way and this far to its side.</summary>
        [JsonProperty("lead_ahead_m")] public float LeadAhead = 1.5f;
        [JsonProperty("lead_aside_m")] public float LeadAside = 1;
        /// <summary>The visitor walks above this speed and stands below <see cref="StandSpeed"/> for <see cref="StandSeconds"/>.</summary>
        [JsonProperty("walk_speed_threshold")] public float WalkThreshold = .45f;
        [JsonProperty("stand_speed_threshold")] public float StandSpeed = .2f;
        [JsonProperty("stand_seconds")] public float StandSeconds = .6f;
        /// <summary>The companion never passes nearer than this to the visitor on its way round them.</summary>
        [JsonProperty("pass_clearance_m")] public float PassClearance = .7f;
        /// <summary>Out of sight and with no clear way back this long, it reappears in sight, as after a jump.</summary>
        [JsonProperty("reappear_after_seconds")] public float ReappearAfter = 5;

        public bool Valid(float personalSpace)
        {
            foreach (var value in new[] { Distance, Angle, OutOfSightSeconds, MoveSpeed, LiftSpeed, LeadAhead, LeadAside, WalkThreshold,
                StandSpeed, StandSeconds, PassClearance, ReappearAfter })
                if (!MuseumExperienceConfiguration.Finite(value) || value <= 0) return false;
            return MuseumExperienceConfiguration.Finite(SightMargin) && SightMargin >= 0 && SightMargin < 45
                && MuseumExperienceConfiguration.Finite(EyeDrop) && MuseumExperienceConfiguration.Finite(LiftMin)
                && MuseumExperienceConfiguration.Finite(LiftMax) && LiftMin <= 0 && LiftMax >= 0 && LiftMax - LiftMin < 1.5f
                && Angle >= 10 && Angle <= 60 && Distance > personalSpace && Distance < 4 && StandSpeed < WalkThreshold
                && ReappearAfter > OutOfSightSeconds;
        }
    }

    /// <summary>What the visit lets the companion do at a moment.</summary>
    public readonly struct CompanionMoment
    {
        public readonly double Time;
        /// <summary>Room key the visitor stands in, or null outside every room.</summary>
        public readonly string Zone;
        /// <summary>The visitor is walking the museum (not in a menu, the entry or a pause).</summary>
        public readonly bool Exploring;
        /// <summary>A narration plays, a point's screen is open or the visitor talks with the guide.</summary>
        public readonly bool Busy;
        /// <summary>The visitor asked the companion to be quiet.</summary>
        public readonly bool Muted;

        public CompanionMoment(double time, string zone, bool exploring, bool busy, bool muted)
        { Time = time; Zone = zone; Exploring = exploring; Busy = busy; Muted = muted; }
    }

    /// <summary>
    /// Decides when the companion speaks: a neutral welcome once the visit starts, then at most one approved remark per
    /// area, each once per visit, only after the visitor has stayed a while and never over a narration, an open screen,
    /// a menu or a pause. Crossing in and out of an area never repeats a remark.
    /// </summary>
    public sealed class MuseumCompanionRemarks
    {
        public const string WelcomeId = "welcome";
        private readonly MuseumCompanionSettings settings;
        private readonly List<CompanionRemark> remarks;
        private readonly HashSet<string> said = new HashSet<string>();
        private double exploringSince = double.NaN;
        private string zone;
        private double zoneSince;
        private double lastEnded = double.NegativeInfinity;
        private double shownUntil;

        public CompanionRemark Current { get; private set; }
        public IReadOnlyCollection<string> Said => said;

        public MuseumCompanionRemarks(MuseumCompanionSettings settings, IEnumerable<CompanionRemark> extra = null)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            remarks = settings.Remarks.Concat(extra ?? Enumerable.Empty<CompanionRemark>()).Where(remark => remark.Approved).ToList();
            Add(settings.InterfaceRemarks.Select(remark => new CompanionRemark
            {
                Id = remark.Id, Zone = remark.Zone, Text = MuseumInterfaceText.Get(remark.TextKey),
                Approved = true, Source = "Neutral visitor interface copy"
            }));
        }

        /// <summary>Adds approved remarks that arrived later (for example from the content service); others are ignored.</summary>
        public void Add(IEnumerable<CompanionRemark> more)
        {
            foreach (var remark in more ?? Enumerable.Empty<CompanionRemark>())
                if (remark != null && remark.Approved && !string.IsNullOrWhiteSpace(remark.Text) && remarks.All(known => known.Id != remark.Id))
                    remarks.Add(remark);
        }

        /// <summary>Advances to the moment and returns the remark to show, or null for none.</summary>
        public CompanionRemark Tick(CompanionMoment moment)
        {
            if (!moment.Exploring) { exploringSince = double.NaN; Hide(moment.Time); return null; }
            if (double.IsNaN(exploringSince)) exploringSince = moment.Time;
            if (moment.Zone != zone) { zone = moment.Zone; zoneSince = moment.Time; }
            if (moment.Muted || moment.Busy) { Hide(moment.Time); return null; }
            if (Current != null)
            {
                if (moment.Time < shownUntil) return Current;
                Hide(moment.Time);
            }
            if (moment.Time - lastEnded < settings.GapSeconds) return null;
            if (!said.Contains(WelcomeId) && !string.IsNullOrWhiteSpace(settings.WelcomeText))
            {
                if (moment.Time - exploringSince < settings.WelcomeDelay) return null;
                return Show(new CompanionRemark { Id = WelcomeId, Zone = "", Text = settings.WelcomeText, Approved = true }, moment.Time);
            }
            if (zone == null || moment.Time - zoneSince < settings.DwellSeconds) return null;
            var next = remarks.FirstOrDefault(remark => remark.Zone == zone && !said.Contains(remark.Id));
            return next != null ? Show(next, moment.Time) : null;
        }

        private CompanionRemark Show(CompanionRemark remark, double time)
        {
            said.Add(remark.Id);
            Current = remark;
            shownUntil = time + Math.Max(settings.RemarkSeconds, remark.Text.Length / 14.0);
            return remark;
        }

        /// <summary>An interrupted remark is not repeated; it counts as said.</summary>
        private void Hide(double time)
        {
            if (Current == null) return;
            Current = null;
            lastEnded = time;
        }
    }
}
