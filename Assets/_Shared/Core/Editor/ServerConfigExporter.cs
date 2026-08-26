using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Net;

namespace VortexArena.Core.Editor
{
    /// <summary>Generates <c>Server/config/maps.json</c> from the project's
    /// <see cref="MapDefinition"/> ScriptableObjects.</summary>
    /// <remarks>
    /// <b>There is NO weapon export</b> (Docs/ArenaNet-Protokol.md §10.3): the server keeps no
    /// weapon table, the client computes damage and reports it via <c>hit_report.damage</c>.
    /// <see cref="WeaponDefinition"/> assets live on the client only, so adding or tuning a weapon
    /// needs no export. Besides the map catalog the file carries only the network object table
    /// (§10.10): per map <c>objects[]</c> and, at the root, <c>kinds[]</c>.
    /// <para><b>Determinism (keep the git diff clean):</b> maps and mode lists are sorted Ordinal,
    /// line ending LF, UTF-8 without BOM, single trailing <c>\n</c>. Same content → same
    /// bytes.</para>
    /// <para><b>Safety brake:</b> when no asset is found the file is NOT written (so an existing
    /// server config is not overwritten with an empty table); a warning is returned instead.</para>
    /// </remarks>
    public static class ServerConfigExporter
    {
        private const string MapsFileName = "maps.json";

        /// <summary>Config folder the server reads (<c>Server/config</c> under the repo root).</summary>
        private static string ConfigDirectory =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Server", "config"));

        /// <summary>Menu entry: manual export with a dialog.</summary>
        [MenuItem("Tools/VortexArena/Server/Export Server Config", false, 60)]
        private static void ExportMenu()
        {
            Export(true);
        }

        /// <summary>Writes maps.json and returns the result.</summary>
        /// <remarks>With <paramref name="showDialog"/> <c>false</c> no dialog is opened at all, so
        /// MCP / batch automation can call it headless; the summary always goes to the console.</remarks>
        /// <param name="showDialog">Show the summary dialog when done.</param>
        /// <returns>Written path, row count and validation warnings.</returns>
        public static ServerConfigExportResult Export(bool showDialog)
        {
            var result = new ServerConfigExportResult();

            string configDir = ConfigDirectory;
            result.MapsPath = Path.Combine(configDir, MapsFileName);

            List<MapDefinition> maps = CollectMaps(result);
            List<NetKindRow> kinds = CollectKinds(result);

            result.MapCount = maps.Count;

            Directory.CreateDirectory(configDir);

            if (maps.Count > 0)
            {
                WriteFile(result.MapsPath, BuildMapsJson(maps, kinds, result));
            }
            else
            {
                result.Warnings.Add($"Hiç MapDefinition bulunamadı — {MapsFileName} YAZILMADI (mevcut sunucu tablosu korundu).");
            }

            result.Summary =
                $"Export Server Config: {result.MapCount} harita, {kinds.Count} ağ nesnesi türü → " +
                $"{configDir} ({result.Warnings.Count} uyarı)";

            for (int i = 0; i < result.Warnings.Count; i++)
            {
                Debug.LogWarning($"[ExportServerConfig] {result.Warnings[i]}");
            }

            Debug.Log($"[ExportServerConfig] {result.Summary}");

            if (showDialog)
            {
                EditorUtility.DisplayDialog("VortexArena — Export Server Config", BuildDialogText(result), "Tamam");
            }

            return result;
        }

        // -------------------------------------------------------------- check

        /// <summary>Whether the <c>maps.json</c> on disk matches the project — <b>WRITES
        /// NOTHING</b> (read by the build readiness panel).</summary>
        /// <remarks>⚠️ The comparison is against the content that would be generated, not a parse:
        /// the export is deterministic (sorted, LF, no BOM), so "same bytes?" is exactly "would the
        /// export change this file?". A hand written JSON reader would be a second, driftable
        /// interpretation of the format.
        /// <para>Warnings are readiness problems too (e.g. a scene missing from Build Settings): a
        /// file that is up to date but has warnings does not count as clean.</para></remarks>
        internal static bool IsMapsJsonUpToDate(out string detail)
        {
            string path = Path.Combine(ConfigDirectory, MapsFileName);
            if (!File.Exists(path))
            {
                detail = $"'{MapsFileName}' YOK — sunucu hiçbir haritayı tanımaz, start_match reddedilir.";
                return false;
            }

            var probe = new ServerConfigExportResult();
            List<MapDefinition> maps = CollectMaps(probe);
            List<NetKindRow> kinds = CollectKinds(probe);

            if (maps.Count == 0)
            {
                detail = "Projede export edilebilir harita yok — export dosyaya dokunmaz.";
                return false;
            }

            if (!string.Equals(BuildMapsJson(maps, kinds, probe), File.ReadAllText(path), StringComparison.Ordinal))
            {
                detail = $"'{MapsFileName}' projeden ayrışmış ({maps.Count} harita bekleniyor) — export ezecek.";
                return false;
            }

            if (probe.Warnings.Count > 0)
            {
                detail = $"{maps.Count} harita yazılı ama {probe.Warnings.Count} uyarı var " +
                         "(uyarıları görmek için export'u çalıştır).";
                return false;
            }

            detail = $"{maps.Count} harita güncel.";
            return true;
        }

        // -------------------------------------------------------------- collect

        /// <summary>Collects, validates and sorts (by sceneName) every map definition in the
        /// project.</summary>
        private static List<MapDefinition> CollectMaps(ServerConfigExportResult result)
        {
            var loaded = new List<MapDefinition>();
            var paths = new Dictionary<MapDefinition, string>();

            string[] guids = AssetDatabase.FindAssets("t:MapDefinition");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<MapDefinition>(path);
                if (asset == null)
                {
                    continue;
                }

                loaded.Add(asset);
                paths[asset] = path;
            }

            loaded.Sort((a, b) =>
            {
                int byName = string.CompareOrdinal(a.SceneName ?? string.Empty, b.SceneName ?? string.Empty);
                return byName != 0 ? byName : string.CompareOrdinal(paths[a], paths[b]);
            });

            Dictionary<string, bool> buildScenes = CollectBuildSettingsScenes();

            var accepted = new List<MapDefinition>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < loaded.Count; i++)
            {
                MapDefinition map = loaded[i];
                string path = paths[map];
                string sceneName = map.SceneName;

                if (string.IsNullOrWhiteSpace(sceneName))
                {
                    result.Warnings.Add($"Boş sceneName: '{path}' — atlandı (katalog anahtarı zorunlu).");
                    continue;
                }

                // ⚠️ Filtering comes BEFORE the duplicate check: a filtered map must not claim its
                // name in `seen`, otherwise a REAL map sharing a template's scene name would be
                // dropped as "duplicate" (sorting puts the template first).

                // Template = unplayable content: skipped silently (a warning would be noise).
                if (path.StartsWith(TemplateRoot, StringComparison.Ordinal))
                {
                    continue;
                }

                // A map outside a venue is NOT exported: the server asks the operator for a venue at
                // startup and that list comes from this file, so a venueless map would create a row
                // for a business that does not exist. The venue is the folder, not a field.
                if (!path.StartsWith(VenuesRoot, StringComparison.Ordinal))
                {
                    result.Warnings.Add(
                        $"'{sceneName}' bir mekan klasöründe değil ('{path}') — ATLANDI. " +
                        $"Oynanacak arenanın MapDefinition'ı {VenuesRoot}<İşletme>/Scenes/<Sahne>/Data/<Sahne>.asset " +
                        $"olmalı; şablonların yeri {TemplateRoot}.");
                    continue;
                }

                if (!seen.Add(sceneName))
                {
                    result.Warnings.Add($"Yinelenen sceneName '{sceneName}': '{path}' — atlandı (ilk eşleşme yazıldı).");
                    continue;
                }

                if (!buildScenes.TryGetValue(sceneName, out bool enabled))
                {
                    result.Warnings.Add($"sceneName '{sceneName}' Build Settings'te YOK — istemciler bu sahneyi yükleyemez ('{path}').");
                }
                else if (!enabled)
                {
                    result.Warnings.Add($"sceneName '{sceneName}' Build Settings'te var ama KAPALI (enabled=false) — '{path}'.");
                }

                accepted.Add(map);
            }

            return accepted;
        }

        // ------------------------------------------------- network object kinds

        /// <summary>One <c>kinds[]</c> row read off a <c>NetObjectKind</c> asset.</summary>
        internal readonly struct NetKindRow
        {
            internal NetKindRow(
                string kind, float maxHp, string grab, List<NetKindEventRow> events, string assetPath)
            {
                Kind = kind;
                MaxHp = maxHp;
                Grab = grab;
                Events = events;
                AssetPath = assetPath;
            }

            internal string Kind { get; }

            internal float MaxHp { get; }

            /// <summary>Wire grab value (§10.10), resolved by the asset.</summary>
            internal string Grab { get; }

            /// <summary>Accepted events; empty = the kind accepts none.</summary>
            internal List<NetKindEventRow> Events { get; }

            /// <summary>Only for warning text (which asset is at fault).</summary>
            internal string AssetPath { get; }
        }

        /// <summary>One <c>kinds[].events[]</c> row; the wire strings are resolved here so the JSON
        /// writer never re-maps the enums itself.</summary>
        internal readonly struct NetKindEventRow
        {
            internal NetKindEventRow(string name, string policy, string phaseGate)
            {
                Name = name;
                Policy = policy;
                PhaseGate = phaseGate;
            }

            internal string Name { get; }

            internal string Policy { get; }

            internal string PhaseGate { get; }
        }

        /// <summary>Every <c>NetObjectKind</c> in the project, Ordinal sorted by kind id (§10.10).</summary>
        /// <remarks>Sorted before the duplicate check so "which one is skipped" does not depend on the
        /// asset database's scan order.</remarks>
        internal static List<NetKindRow> CollectKinds(ServerConfigExportResult result)
        {
            var loaded = new List<NetKindRow>();

            string[] guids = AssetDatabase.FindAssets("t:NetObjectKind");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var asset = AssetDatabase.LoadAssetAtPath<NetObjectKind>(path);
                if (asset == null)
                {
                    continue;
                }

                loaded.Add(new NetKindRow(
                    asset.Kind ?? string.Empty, asset.MaxHp, asset.WireGrab, CollectKindEvents(asset), path));
            }

            loaded.Sort((a, b) =>
            {
                int byKind = string.CompareOrdinal(a.Kind, b.Kind);
                return byKind != 0 ? byKind : string.CompareOrdinal(a.AssetPath, b.AssetPath);
            });

            var accepted = new List<NetKindRow>(loaded.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < loaded.Count; i++)
            {
                NetKindRow row = loaded[i];

                if (string.IsNullOrWhiteSpace(row.Kind))
                {
                    result.Warnings.Add(
                        $"Boş kind: '{row.AssetPath}' — atlandı (tür kimliği telde taşınır, zorunlu).");
                    continue;
                }

                if (!seen.Add(row.Kind))
                {
                    result.Warnings.Add(
                        $"Yinelenen kind '{row.Kind}': '{row.AssetPath}' — atlandı (ilk eşleşme yazıldı).");
                    continue;
                }

                accepted.Add(row);
            }

            return accepted;
        }

        /// <summary>A kind's event rules in AUTHORING order.</summary>
        /// <remarks>⚠️ Deliberately NOT sorted: the server matches an event by name, so the order
        /// carries no meaning — sorting it would only reshuffle the file whenever the list is
        /// reordered in the inspector.</remarks>
        private static List<NetKindEventRow> CollectKindEvents(NetObjectKind asset)
        {
            IReadOnlyList<NetObjectEventRule> rules = asset.Events;
            var rows = new List<NetKindEventRow>(rules.Count);

            for (int i = 0; i < rules.Count; i++)
            {
                NetObjectEventRule rule = rules[i];
                if (rule == null)
                {
                    continue;
                }

                rows.Add(new NetKindEventRow(rule.Name ?? string.Empty, rule.WirePolicy, rule.WirePhaseGate));
            }

            return rows;
        }

        /// <summary>A scene's baked network object list, as written at scene save time.</summary>
        /// <remarks>⚠️ Parsed with <see cref="JsonUtility"/>, never by hand: a second, driftable
        /// reading of the format is exactly how the export and the writer stop agreeing.</remarks>
        [Serializable]
        internal sealed class SceneObjectFile
        {
            public SceneObjectRow[] objects;
        }

        /// <inheritdoc cref="SceneObjectFile"/>
        [Serializable]
        internal sealed class SceneObjectRow
        {
            public int sceneId;
            public string kind;
        }

        /// <summary>Suffix of the per scene object list, next to the MapDefinition asset
        /// (<c>Data/&lt;Scene&gt;_objects.json</c>) — written by <c>SceneIdGuard</c>.</summary>
        internal const string ObjectsFileSuffix = "_objects.json";

        /// <summary>Parses one object list file; <c>null</c> = missing or unreadable.</summary>
        /// <remarks>The single parse site — the readiness check reads through it too, so the format is
        /// never interpreted twice.</remarks>
        internal static SceneObjectRow[] ReadObjectRows(string path, out string error)
        {
            error = null;

            if (!File.Exists(path))
            {
                return null;
            }

            SceneObjectFile parsed;
            try
            {
                parsed = JsonUtility.FromJson<SceneObjectFile>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                error = e.Message;
                return null;
            }

            if (parsed?.objects == null)
            {
                error = "objects listesi yok";
                return null;
            }

            return parsed.objects;
        }

        /// <summary>A map's object rows, sceneId sorted; empty when the scene has no network
        /// object.</summary>
        /// <remarks>The export NEVER opens a scene (baked ids only exist inside the scene file), so
        /// the file written at scene save is the only source. A missing file is normal, not a
        /// warning.</remarks>
        private static List<SceneObjectRow> ReadSceneObjects(
            MapDefinition map, HashSet<string> knownKinds, ServerConfigExportResult result)
        {
            var rows = new List<SceneObjectRow>();

            string assetPath = AssetDatabase.GetAssetPath(map);
            string directory = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(directory))
            {
                return rows;
            }

            string path = directory.Replace('\\', '/') + "/" +
                          Path.GetFileNameWithoutExtension(assetPath) + ObjectsFileSuffix;

            SceneObjectRow[] parsed = ReadObjectRows(path, out string error);
            if (parsed == null)
            {
                if (error != null)
                {
                    result.Warnings.Add($"'{path}' okunamadı ({error}) — bu sahnenin objeleri YAZILMADI.");
                }

                return rows;
            }

            for (int i = 0; i < parsed.Length; i++)
            {
                SceneObjectRow row = parsed[i];
                if (row == null || string.IsNullOrWhiteSpace(row.kind))
                {
                    result.Warnings.Add($"'{path}' içinde kind'ı boş bir satır var — atlandı.");
                    continue;
                }

                if (!knownKinds.Contains(row.kind))
                {
                    result.Warnings.Add(
                        $"'{map.SceneName}' sahnesindeki '{row.kind}' türü NetObjectKind olarak YOK — sunucu " +
                        $"bu objeyi (sceneId {row.sceneId}) tabloya almaz; sahada 'obje kırılmıyor' diye görünür.");
                }

                rows.Add(row);
            }

            rows.Sort((a, b) => a.sceneId.CompareTo(b.sceneId));
            return rows;
        }

        /// <summary>Root of playable arenas; the venue name is the next path segment:
        /// <c>Assets/Arenas/Venues/&lt;Venue&gt;/Scenes/&lt;Scene&gt;/Data/&lt;Scene&gt;.asset</c>.</summary>
        public const string VenuesRoot = "Assets/Arenas/Venues/";

        /// <summary>Root of reference templates
        /// (<c>Assets/Arenas/Template/Scenes/&lt;Scene&gt;/…</c>) — never exported.</summary>
        public const string TemplateRoot = "Assets/Arenas/Template/";

        /// <summary>The map's VENUE, derived from the asset path; there is no separate field.</summary>
        /// <remarks><c>Assets/Arenas/Venues/&lt;Venue&gt;/…</c> → <c>&lt;Venue&gt;</c>. The folder
        /// layout already states the venue; a second field could be forgotten, whereas the only way
        /// to file a map under the wrong venue is to put it in the wrong folder, which is visible.
        /// <para>Maps outside a venue never reach here — <see cref="CollectMaps"/> filters
        /// them.</para></remarks>
        private static string VenueOf(MapDefinition map)
        {
            string path = AssetDatabase.GetAssetPath(map);
            string rest = path.Substring(VenuesRoot.Length);
            int slash = rest.IndexOf('/');
            return slash > 0 ? rest.Substring(0, slash) : rest;
        }

        /// <summary>Build Settings scene names → enabled flag (on a name clash, enabled wins).</summary>
        private static Dictionary<string, bool> CollectBuildSettingsScenes()
        {
            var scenes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            EditorBuildSettingsScene[] all = EditorBuildSettings.scenes;

            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null || string.IsNullOrEmpty(all[i].path))
                {
                    continue;
                }

                string name = Path.GetFileNameWithoutExtension(all[i].path);
                if (scenes.TryGetValue(name, out bool existing))
                {
                    scenes[name] = existing || all[i].enabled;
                }
                else
                {
                    scenes[name] = all[i].enabled;
                }
            }

            return scenes;
        }

        // ----------------------------------------------------------------- json

        /// <summary><c>{ "maps": [ { "sceneName", "venue", "gameType", "modes": ["&lt;modId&gt;"],
        /// "objects": [ { "sceneId", "kind" } ] } ], "kinds": [ { "kind", "maxHp", "grab",
        /// "events": [ { "name", "policy", "phaseGate" } ] } ] }</c> — for the server's
        /// <c>start_match</c> validation, venue selection and object table (§10.10).</summary>
        /// <remarks>⚠️ <b>Objects and kinds are separate on purpose</b> — not repetition but ownership:
        /// the IDENTITY list belongs to the scene, the KIND rule belongs to the content. One kind runs
        /// in ten arenas and its health must be editable in ONE place.
        /// <para>Arena DIMENSIONS are not written: the server knows no metres (poses are client
        /// authoritative and arrive in arena space), and every venue's floor differs and is rarely even
        /// rectangular, so one pair of numbers would not describe it.</para></remarks>
        private static string BuildMapsJson(
            List<MapDefinition> maps, List<NetKindRow> kinds, ServerConfigExportResult result)
        {
            var knownKinds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < kinds.Count; i++)
            {
                knownKinds.Add(kinds[i].Kind);
            }

            var sb = new StringBuilder();
            sb.Append("{\n");

            if (maps.Count == 0)
            {
                sb.Append("  \"maps\": [],\n");
            }
            else
            {
                sb.Append("  \"maps\": [\n");
                for (int i = 0; i < maps.Count; i++)
                {
                    MapDefinition map = maps[i];
                    sb.Append("    {\n")
                        .Append("      \"sceneName\": \"").Append(EscapeJson(map.SceneName)).Append("\",\n")
                        .Append("      \"venue\": \"").Append(EscapeJson(VenueOf(map))).Append("\",\n")
                        .Append("      \"gameType\": \"").Append(GameTypeIds.ToWire(map.GameType)).Append("\",\n")
                        .Append("      \"modes\": ").Append(BuildModesArray(map.SupportedModeIds)).Append(",\n")
                        .Append("      \"objects\": ")
                        .Append(BuildObjectsArray(ReadSceneObjects(map, knownKinds, result))).Append('\n')
                        .Append(i < maps.Count - 1 ? "    },\n" : "    }\n");
                }

                sb.Append("  ],\n");
            }

            sb.Append(BuildKindsSection(kinds));
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>A map's object rows, one line each (empty = <c>[]</c>).</summary>
        private static string BuildObjectsArray(List<SceneObjectRow> rows)
        {
            if (rows.Count == 0)
            {
                return "[]";
            }

            var sb = new StringBuilder("[\n");
            for (int i = 0; i < rows.Count; i++)
            {
                sb.Append("        { \"sceneId\": ").Append(rows[i].sceneId.ToString(CultureInfo.InvariantCulture))
                    .Append(", \"kind\": \"").Append(EscapeJson(rows[i].kind)).Append("\" }")
                    .Append(i < rows.Count - 1 ? ",\n" : "\n");
            }

            return sb.Append("      ]").ToString();
        }

        /// <summary>Root <c>kinds[]</c> block (<c>kind</c>, <c>maxHp</c>, <c>grab</c>,
        /// <c>events[]</c>), already Ordinal sorted by the collector.</summary>
        /// <remarks>⚠️ The number goes through InvariantCulture: on a Turkish machine the decimal
        /// separator would be a comma and the server's JSON parser would reject the file.
        /// <para>⚠️ <c>grab</c> and <c>events[]</c> are always written even when they are empty: the
        /// server normalizes a missing field to <c>"none"</c>/<c>[]</c>, but the export is the single
        /// source of truth and a silently omitted field hides a mis-authored asset.</para></remarks>
        private static string BuildKindsSection(List<NetKindRow> kinds)
        {
            if (kinds.Count == 0)
            {
                return "  \"kinds\": []\n";
            }

            var sb = new StringBuilder("  \"kinds\": [\n");
            for (int i = 0; i < kinds.Count; i++)
            {
                NetKindRow row = kinds[i];
                sb.Append("    { \"kind\": \"").Append(EscapeJson(row.Kind))
                    .Append("\", \"maxHp\": ").Append(row.MaxHp.ToString(CultureInfo.InvariantCulture))
                    .Append(", \"grab\": \"").Append(EscapeJson(row.Grab))
                    .Append("\", \"events\": ").Append(BuildEventsArray(row.Events))
                    .Append(" }")
                    .Append(i < kinds.Count - 1 ? ",\n" : "\n");
            }

            return sb.Append("  ]\n").ToString();
        }

        /// <summary>A kind's event rows, one line each (empty = <c>[]</c>).</summary>
        private static string BuildEventsArray(List<NetKindEventRow> rows)
        {
            if (rows == null || rows.Count == 0)
            {
                return "[]";
            }

            var sb = new StringBuilder("[\n");
            for (int i = 0; i < rows.Count; i++)
            {
                sb.Append("        { \"name\": \"").Append(EscapeJson(rows[i].Name))
                    .Append("\", \"policy\": \"").Append(EscapeJson(rows[i].Policy))
                    .Append("\", \"phaseGate\": \"").Append(EscapeJson(rows[i].PhaseGate))
                    .Append("\" }")
                    .Append(i < rows.Count - 1 ? ",\n" : "\n");
            }

            return sb.Append("      ]").ToString();
        }

        /// <summary>modId array → Ordinal sorted JSON array (empty = <c>[]</c>).</summary>
        private static string BuildModesArray(string[] modeIds)
        {
            if (modeIds == null || modeIds.Length == 0)
            {
                return "[]";
            }

            var ids = new List<string>(modeIds.Length);
            for (int i = 0; i < modeIds.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(modeIds[i]))
                {
                    ids.Add(modeIds[i]);
                }
            }

            if (ids.Count == 0)
            {
                return "[]";
            }

            ids.Sort(StringComparer.Ordinal);

            var sb = new StringBuilder("[");
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append('"').Append(EscapeJson(ids[i])).Append('"');
            }

            return sb.Append(']').ToString();
        }

        /// <summary>Minimal JSON string escaping (protocol keys are ASCII, but stay safe).</summary>
        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            return sb.ToString();
        }

        // ----------------------------------------------------------------- I/O

        /// <summary>UTF-8 without BOM, LF line endings — the content is already built with
        /// <c>\n</c>.</summary>
        private static void WriteFile(string path, string content)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        /// <summary>Dialog text: summary + warning list when present.</summary>
        private static string BuildDialogText(ServerConfigExportResult result)
        {
            var sb = new StringBuilder();
            sb.Append(result.Summary);

            if (result.Warnings.Count > 0)
            {
                sb.Append("\n\nUyarılar:");
                for (int i = 0; i < result.Warnings.Count; i++)
                {
                    sb.Append("\n• ").Append(result.Warnings[i]);
                }
            }

            return sb.ToString();
        }
    }
}
