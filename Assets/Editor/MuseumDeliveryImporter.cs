using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MusiyoBetsknate.Editor
{
    /// <summary>
    /// Copies the files an external museum delivery lists for import, after checking each one against its hash.
    /// Nothing outside that list is read into the project, and the delivery folder is never written to.
    /// </summary>
    public static class MuseumDeliveryImporter
    {
        private const string PathArgument = "-museumDelivery";
        /// <summary>The three models carry the same geometry; the lightest profile supplies the one that is imported.</summary>
        private const string ModelProfile = "web";
        private static readonly string[] ProfileNames = { ModelProfile, "quest", "windows" };

        /// <summary>Scene anchor each source point stands for. A new source point needs a decision here before it imports.</summary>
        private static readonly Dictionary<string, string> PointKeys = new Dictionary<string, string>
        {
            { "PI_Bienvenida_01_Mito_Betiyeguagua", "punto.bienvenida.mito" },
            { "PI_Bienvenida_02_Nombres_y_Significados", "punto.bienvenida.significados" },
            { "PI_Personajes_01_Matachin", "punto.personajes.pedestal_1" },
            { "PI_Personajes_02_Gobernador_Cabildo", "punto.personajes.pedestal_2" },
            { "PI_Personajes_03_Bandereros", "punto.personajes.castillo" },
            { "PI_Personajes_04_Sanjuanes", "punto.personajes.pedestal_4" },
            { "PI_Personajes_05_Saraguayes", "punto.personajes.pedestal_5" },
            { "PI_Instrumentos_01_Bombo", "punto.instrumentos.pedestal_1" },
            { "PI_Instrumentos_02_Cacho", "punto.instrumentos.pedestal_2" },
            { "PI_Instrumentos_03_Flauta", "punto.instrumentos.pedestal_3" },
            { "PI_Instrumentos_04_Rondador", "punto.instrumentos.pedestal_4" },
            { "PI_Instrumentos_05_Dulzaina", "punto.instrumentos.pedestal_5" },
            { "PI_Instrumentos_06_Danza_del_Ocho", "punto.instrumentos.danza_ocho" },
            { "PI_Fogon_01_Personaje_Guia", "guia.principal" },
            { "PI_Mirador_01_Creditos_Portadores", "punto.mirador.creditos" },
        };

        /// <summary>Surface designs made for this museum; every other texture is a tiling material photograph.</summary>
        private static readonly string[] AuthoredDesigns =
        {
            "cenefa_piso_rombos", "estera_fibra", "piso_bienvenida_labores", "piso_patio_rombo_sol",
            "tsombiach_labores_interpretacion",
        };

        [MenuItem("Musiyo/Museum Delivery/Import Package...")]
        public static void ImportFromDialog()
        {
            string package = EditorUtility.OpenFolderPanel("Museum delivery folder", "", "");
            if (string.IsNullOrEmpty(package)) return;
            Import(package);
            MuseumDeliveryAssets.Build();
        }

        /// <summary>Batch entry: -executeMethod ... -museumDelivery &lt;folder&gt;.</summary>
        public static void ImportFromCommandLine()
        {
            var arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, PathArgument);
            if (index < 0 || index + 1 >= arguments.Length)
                throw new InvalidOperationException("Pass " + PathArgument + " <delivery folder>.");
            Import(arguments[index + 1]);
            MuseumDeliveryAssets.Build();
        }

        public static MuseumDeliveryManifest Import(string package)
        {
            package = Path.GetFullPath(package);
            var list = JObject.Parse(File.ReadAllText(Path.Combine(package, "import_list.json")));
            var manifest = new MuseumDeliveryManifest
            {
                Package = new DirectoryInfo(package).Name,
                ProvenanceNote = "Provenance is read from file names and the delivery notes; the delivery carries no licence records. "
                    + "material_photograph: tiling surface sets named after Poly Haven texture assets, which that library publishes "
                    + "under CC0; colour maps ending in _musiyo were recoloured for the museum. "
                    + "authored_design: surface designs made for this museum.",
            };
            var stored = new Dictionary<string, MuseumDeliveryManifest.TextureEntry>();
            Directory.CreateDirectory(MuseumDeliveryManifest.TextureFolder);
            Directory.CreateDirectory(Path.GetDirectoryName(MuseumDeliveryManifest.ModelPath));

            foreach (string profileName in ProfileNames)
            {
                var files = list["profiles"][profileName]["import_in_unity"];
                string model = Verify(package, files["model_fbx"]);
                string guide = Verify(package, files["materials_json"]);
                var source = new MuseumDeliveryManifest.SourceEntry
                {
                    Profile = profileName,
                    Source = (string)files["model_fbx"]["file"],
                    Sha256 = (string)files["model_fbx"]["sha256"],
                    Bytes = (long)files["model_fbx"]["bytes"],
                };
                if (profileName == ModelProfile)
                {
                    CopyIfChanged(model, MuseumDeliveryManifest.ModelPath, source.Sha256);
                    manifest.Model = new MuseumDeliveryManifest.ModelEntry { Source = source.Source, Sha256 = source.Sha256, Bytes = source.Bytes };
                }
                else manifest.Model.EquivalentSources.Add(source);

                var delivered = new Dictionary<string, string>();
                foreach (var texture in files["textures"])
                {
                    string path = Verify(package, texture);
                    string sha = (string)texture["sha256"];
                    if (!stored.TryGetValue(sha, out var entry))
                    {
                        int width = (int)texture["size_px"][0], height = (int)texture["size_px"][1];
                        string stem = Path.GetFileNameWithoutExtension(path);
                        // Some delivered files carry an extension that does not match their content.
                        string name = stem + "_" + Math.Max(width, height) + ContentExtension(path);
                        if (stored.Values.Any(other => other.File == name))
                            throw new InvalidOperationException("Two different textures would be stored as " + name + ".");
                        CopyIfChanged(path, MuseumDeliveryManifest.TextureFolder + "/" + name, sha);
                        entry = new MuseumDeliveryManifest.TextureEntry
                        {
                            File = name, Sha256 = sha, Bytes = (long)texture["bytes"], Role = (string)texture["role"],
                            Width = width, Height = height,
                            Provenance = AuthoredDesigns.Contains(stem) ? "authored_design" : "material_photograph",
                        };
                        stored.Add(sha, entry);
                        manifest.Textures.Add(entry);
                    }
                    else if (entry.Role != (string)texture["role"])
                        throw new InvalidOperationException(entry.File + " is delivered with two different roles.");
                    entry.Sources.Add((string)texture["file"]);
                    delivered[Path.GetFileName(path)] = entry.File;
                }

                var guideData = JObject.Parse(File.ReadAllText(guide));
                manifest.Variant = (string)guideData["variant"];
                var profile = new MuseumDeliveryManifest.ProfileEntry
                {
                    Name = profileName,
                    Shader = ((string)guideData["profile_settings"]["shader_hint"]).StartsWith("URP/Simple Lit", StringComparison.Ordinal)
                        ? "Universal Render Pipeline/Simple Lit" : "Universal Render Pipeline/Lit",
                    GuideSource = (string)files["materials_json"]["file"],
                    GuideSha256 = (string)files["materials_json"]["sha256"],
                };
                foreach (var material in guideData["materials"])
                {
                    if ((float)material["alpha"] < 1)
                        throw new InvalidOperationException("Transparent material needs an import rule: " + material["name"]);
                    profile.Materials.Add(new MuseumDeliveryManifest.MaterialEntry
                    {
                        Name = (string)material["name"],
                        DoubleSided = (bool)material["double_sided"],
                        BaseColor = material["base_color"].Select(value => (float)value).ToArray(),
                        Metallic = (float)material["metallic"],
                        Roughness = (float)material["roughness"],
                        BaseColorTexture = Delivered(delivered, material["base_color_texture"]),
                        NormalTexture = Delivered(delivered, material["normal_texture"]),
                        NormalStrength = material["normal_strength"] != null ? (float)material["normal_strength"] : 1,
                        RoughnessTexture = Delivered(delivered, material["roughness_texture"]),
                        EmissionColor = material["emission_color"]?.Select(value => (float)value).ToArray(),
                        EmissionStrength = material["emission_strength"] != null ? (float)material["emission_strength"] : 0,
                    });
                }
                manifest.Profiles.Add(profile);
            }

            var packageHashes = ReadPackageHashes(package);
            foreach (var row in ReadTable(VerifyListed(package, "maps/object_roles.csv", packageHashes)))
            {
                if (row["exported"] != "yes") continue;
                var channels = row["uv_channels"].Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                bool lightmapChannel = channels.Length > 1;
                manifest.Objects.Add(new MuseumDeliveryManifest.ObjectEntry
                {
                    Name = row["object"], Mesh = row["type"] == "MESH", Room = row["room"], Role = row["role"],
                    Collision = row["collision_use"], Triangles = Number(row["triangles"]), UvChannels = channels.Length,
                    Gi = row["type"] != "MESH" ? "" : lightmapChannel && row["gi_hint"] == "lightmap candidate" ? "lightmap" : "probes",
                    ScaleInLightmap = row["scale_in_lightmap_min"].Length > 0 ? Math.Max(1, Real(row["scale_in_lightmap_min"])) : 1,
                    LightmapCleanPx = Number(row["lightmap_min_clean_px"]),
                    // The source is Z-up; the scene frame is Y-up.
                    Size = row["type"] == "MESH" ? new[] { Real(row["size_x_m"]), Real(row["size_z_m"]), Real(row["size_y_m"]) } : null,
                    Lowest = row["type"] == "MESH" ? Real(row["lowest_z_m"]) : 0,
                });
            }
            manifest.Model.Objects = manifest.Objects.Count;
            manifest.Model.Meshes = manifest.Objects.Count(entry => entry.Mesh);
            manifest.Model.Triangles = manifest.Objects.Sum(entry => entry.Triangles);

            foreach (var row in ReadTable(VerifyListed(package, "maps/anchors.csv", packageHashes)))
            {
                string key = null;
                if (row["kind"] == "point_of_interest" && !PointKeys.TryGetValue(row["object"], out key))
                    throw new InvalidOperationException("Source point without a scene key: " + row["object"]);
                manifest.Anchors.Add(new MuseumDeliveryManifest.AnchorEntry
                {
                    Object = row["object"], Kind = row["kind"], Key = key,
                    Position = new[] { Real(row["unity_plan_x"]), Real(row["unity_plan_y"]), Real(row["unity_plan_z"]) },
                    ActivationDiameter = row["activation_zone_diameter_m"].Length > 0 ? Real(row["activation_zone_diameter_m"]) : 0,
                });
            }

            manifest.Save();
            AssetDatabase.Refresh();
            Debug.Log("Museum delivery " + manifest.Package + " copied: 1 model, " + manifest.Textures.Count + " textures, "
                + manifest.Profiles.Count + " surface profiles, " + manifest.Objects.Count + " objects.");
            return manifest;
        }

        private static string Delivered(Dictionary<string, string> delivered, JToken name)
        {
            if (name == null || name.Type == JTokenType.Null) return null;
            if (!delivered.TryGetValue((string)name, out string file))
                throw new InvalidOperationException("A material names a texture outside the import list: " + name);
            return file;
        }

        /// <summary>Full path of a listed file whose size and SHA-256 match the list.</summary>
        private static string Verify(string package, JToken listed)
        {
            string path = Resolve(package, (string)listed["file"]);
            if (new FileInfo(path).Length != (long)listed["bytes"] || Sha256(path) != (string)listed["sha256"])
                throw new InvalidOperationException("Delivered file does not match the import list: " + listed["file"]);
            return path;
        }

        private static string VerifyListed(string package, string relative, Dictionary<string, string> packageHashes)
        {
            string path = Resolve(package, relative);
            if (!packageHashes.TryGetValue(relative, out string expected) || Sha256(path) != expected)
                throw new InvalidOperationException("Delivered file does not match the package manifest: " + relative);
            return path;
        }

        private static string Resolve(string package, string relative)
        {
            string path = Path.GetFullPath(Path.Combine(package, relative));
            if (!path.StartsWith(package + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                throw new InvalidOperationException("Listed file is missing from the delivery: " + relative);
            return path;
        }

        /// <summary>Hashes of the package manifest, keyed by path relative to the delivery folder.</summary>
        private static Dictionary<string, string> ReadPackageHashes(string package)
        {
            var hashes = new Dictionary<string, string>();
            string marker = "/" + new DirectoryInfo(package).Name + "/";
            var files = (JObject)JObject.Parse(File.ReadAllText(Path.Combine(package, "manifest_delivery.json")))["files"];
            foreach (var file in files)
            {
                int start = file.Key.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (start >= 0) hashes[file.Key.Substring(start + marker.Length)] = (string)file.Value["sha256"];
            }
            return hashes;
        }

        private static void CopyIfChanged(string source, string destination, string sha)
        {
            if (File.Exists(destination) && Sha256(destination) == sha) return;
            File.Copy(source, destination, true);
        }

        public static string Sha256(string path)
        {
            using (var algorithm = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var text = new StringBuilder();
                foreach (byte value in algorithm.ComputeHash(stream)) text.Append(value.ToString("x2"));
                return text.ToString();
            }
        }

        private static string ContentExtension(string path)
        {
            var head = new byte[4];
            using (var stream = File.OpenRead(path)) stream.Read(head, 0, head.Length);
            if (head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47) return ".png";
            if (head[0] == 0xFF && head[1] == 0xD8) return ".jpg";
            throw new InvalidOperationException("Unsupported texture content: " + Path.GetFileName(path));
        }

        private static int Number(string text) => text.Length > 0 ? int.Parse(text, CultureInfo.InvariantCulture) : 0;
        private static float Real(string text) => float.Parse(text, CultureInfo.InvariantCulture);

        /// <summary>Rows of a comma-separated table with a header line and optional quoted fields.</summary>
        private static List<Dictionary<string, string>> ReadTable(string path)
        {
            var rows = new List<Dictionary<string, string>>();
            string[] header = null;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (line.Length == 0) continue;
                var fields = new List<string>();
                var field = new StringBuilder();
                bool quoted = false;
                for (int index = 0; index < line.Length; index++)
                {
                    char character = line[index];
                    if (quoted && character == '"' && index + 1 < line.Length && line[index + 1] == '"') { field.Append('"'); index++; }
                    else if (character == '"') quoted = !quoted;
                    else if (character == ',' && !quoted) { fields.Add(field.ToString()); field.Clear(); }
                    else field.Append(character);
                }
                fields.Add(field.ToString());
                if (header == null) { header = fields.Select(name => name.Trim('﻿')).ToArray(); continue; }
                var row = new Dictionary<string, string>();
                for (int index = 0; index < header.Length; index++) row[header[index]] = index < fields.Count ? fields[index] : "";
                rows.Add(row);
            }
            return rows;
        }
    }
}
