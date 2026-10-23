using System;
using System.Collections.Generic;
using System.Linq;
using MusiyoBetsknate.Museum;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Sets right what sat wrong by the museum's hearth (review of 22/10/2026, inventory and pictures in the U2 report),
    /// when the scene enters Play mode or a build, before its static pieces are merged: the maize cobs, which hung 0.29 m
    /// below the rack over the fire with nothing holding them, now hang from it; and a stool standing inside the clay
    /// pots around the fire moves round the fire to free floor, or is left out when there is none. Nothing is added and
    /// the scene file is not changed. Where the Editor merged the static pieces before this step (some Play mode loads),
    /// nothing is moved. Setting MUSIYO_HEARTH_TIDY=off skips it, for pictures of the hearth as delivered.
    /// </summary>
    public static class MuseumHearthTidy
    {
        public const string Rack = "Fogon_Bichasha";
        public const string Cobs = "Fogon_Mazorcas";
        public const string Pots = "Fogon_Ollas_Barro";
        public const string Fire = "Fogon_Piedras_Shinyac";
        public const string StoolPrefix = "Hearth_Banco_";
        /// <summary>The blankets on the hearth's benches; the benches under them have no collider, so their footprint counts.</summary>
        public const string SeatPrefix = "Fogon_Manta_";
        private static List<Bounds> seats = new List<Bounds>();

        public static bool Skipped => string.Equals(Environment.GetEnvironmentVariable("MUSIYO_HEARTH_TIDY"), "off", StringComparison.OrdinalIgnoreCase);

        /// <summary>Applies the corrections; returns one line per piece set right.</summary>
        public static List<string> Apply(Scene scene)
        {
            var changes = new List<string>();
            var renderers = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>(true)).ToList();
            Renderer Piece(string name) => renderers.FirstOrDefault(item => item.name == name);
            // Play mode in the Editor may have merged the static pieces already; moving them then shows nothing, so the
            // hearth is left as it is there. Builds process the scene before merging it.
            if (renderers.Any(item => item.TryGetComponent<MeshFilter>(out var filter) && filter.sharedMesh != null
                && filter.sharedMesh.name.StartsWith("Combined Mesh"))) return changes;
            HangCobs(Piece(Rack), Piece(Cobs), changes);
            var fire = Piece(Fire);
            var pots = Piece(Pots);
            seats = renderers.Where(item => item.name.StartsWith(SeatPrefix)).Select(item => item.bounds).ToList();
            if (fire != null && pots != null) FreeStools(fire.bounds.center, PotPoints(pots), renderers.Where(item => item.name.StartsWith(StoolPrefix)).ToList(),
                scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PointAnchor>(true)).ToList(), changes);
            foreach (var change in changes) Debug.Log("[MuseumHearthTidy] " + change);
            return changes;
        }

        /// <summary>Raises the cobs until their top meets the underside of the rack they hang under.</summary>
        private static void HangCobs(Renderer rack, Renderer cobs, List<string> changes)
        {
            if (rack == null || cobs == null) return;
            var under = rack.bounds;
            var hanging = cobs.bounds;
            bool beneath = hanging.center.x > under.min.x && hanging.center.x < under.max.x && hanging.center.z > under.min.z && hanging.center.z < under.max.z;
            float gap = under.min.y - hanging.max.y;
            if (!beneath || gap < .03f) return;
            cobs.transform.position += Vector3.up * (gap - .01f);
            changes.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} raised {1:F2} m to hang from {2}", Cobs, gap - .01f, Rack));
        }

        /// <summary>Points of the pots' surface, in world space.</summary>
        public static List<Vector3> PotPoints(Renderer pots)
        {
            var filter = pots.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return new List<Vector3>();
            var matrix = pots.transform.localToWorldMatrix;
            return filter.sharedMesh.vertices.Select(vertex => matrix.MultiplyPoint3x4(vertex)).ToList();
        }

        /// <summary>True when any pot point lies within the stool's footprint, a little enlarged.</summary>
        public static bool Crowded(Bounds stool, IEnumerable<Vector3> pots)
        {
            stool.Expand(new Vector3(.08f, 0, .08f));
            return pots.Any(point => point.x > stool.min.x && point.x < stool.max.x && point.z > stool.min.z && point.z < stool.max.z
                && point.y < stool.max.y + .1f);
        }

        /// <summary>True when a bench by the fire or something solid other than the floor stands within the stool's footprint.</summary>
        public static bool Blocked(Bounds stool, Transform own)
        {
            var footprint = stool;
            footprint.Expand(new Vector3(.06f, 0, .06f));
            if (seats.Any(seat => footprint.min.x < seat.max.x && footprint.max.x > seat.min.x && footprint.min.z < seat.max.z && footprint.max.z > seat.min.z))
                return true;
            var size = new Vector3(Mathf.Max(.01f, stool.size.x - .06f), Mathf.Max(.01f, stool.size.y - .06f), Mathf.Max(.01f, stool.size.z - .06f));
            return Physics.OverlapBox(stool.center + Vector3.up * .03f, size * .5f, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .Any(collider => !collider.transform.IsChildOf(own) && collider.bounds.max.y > stool.min.y + .05f && collider.bounds.size.y < 3);
        }

        private static void FreeStools(Vector3 fire, List<Vector3> pots, List<Renderer> stools, List<PointAnchor> anchors, List<string> changes)
        {
            foreach (var stool in stools)
            {
                var root = stool.transform;
                if (!Crowded(stool.bounds, pots) && !Blocked(stool.bounds, root)) continue;
                var start = root.position;
                bool moved = false;
                // Round the fire at the same distance, a little at a time, alternating sides.
                for (int step = 1; step <= 9 && !moved; step++)
                    foreach (float sign in new[] { 1f, -1f })
                    {
                        root.position = fire + Quaternion.Euler(0, sign * step * 8, 0) * (start - fire);
                        root.position = new Vector3(root.position.x, start.y, root.position.z);
                        Physics.SyncTransforms();
                        var bounds = stool.bounds;
                        bool free = !Crowded(bounds, pots) && !Blocked(bounds, root)
                            && !stools.Any(other => other != stool && other.enabled && other.bounds.Intersects(bounds))
                            && !anchors.Any(anchor => Vector2.Distance(new Vector2(anchor.transform.position.x, anchor.transform.position.z),
                                new Vector2(bounds.center.x, bounds.center.z)) < (anchor.Key == "guia.principal" ? 1.4f : 1.1f));
                        if (!free) continue;
                        changes.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} moved {1:F2} m round the fire, clear of the pots",
                            root.name, Vector3.Distance(start, root.position)));
                        moved = true;
                        break;
                    }
                if (moved) continue;
                root.position = start;
                root.gameObject.SetActive(false);
                changes.Add(root.name + " left out: no free floor beside the pots");
            }
        }
    }

    /// <summary>Runs <see cref="MuseumHearthTidy"/> on the museum scene for Play mode and builds, after its world is built.</summary>
    public sealed class MuseumHearthTidyStep : IProcessSceneWithReport
    {
        public int callbackOrder => 5;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (scene.path != MuseumSurfaceAudit.MuseumScene || MuseumHearthTidy.Skipped) return;
            MuseumHearthTidy.Apply(scene);
        }
    }
}
