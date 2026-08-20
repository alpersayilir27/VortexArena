using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Editor
{
    /// <summary>Generates <c>Server/config/maps.json</c> from the project's
    /// <see cref="MapDefinition"/> ScriptableObjects.</summary>
    /// <remarks>
    /// <b>There is NO weapon export</b> (Docs/ArenaNet-Protokol.md §10.3): the server keeps no
    /// weapon table, the client computes damage and reports it via <c>hit_report.damage</c>.
    /// <see cref="WeaponDefinition"/> assets live on the client only, so adding or tuning a weapon
    /// needs no export. This tool is the map catalog alone — the server reads nothing else, it only
    /// validates in <c>start_match</c> that <c>sceneName</c> exists and supports the mode.
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

            result.MapCount = maps.Count;

            Directory.CreateDirectory(configDir);

            if (maps.Count > 0)
            {
                WriteFile(result.MapsPath, BuildMapsJson(maps));
            }
            else
            {
                result.Warnings.Add($"Hiç MapDefinition bulunamadı — {MapsFileName} YAZILMADI (mevcut sunucu tablosu korundu).");
            }

            result.Summary =
                $"Export Server Config: {result.MapCount} harita → {configDir} ({result.Warnings.Count} uyarı)";

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

            if (maps.Count == 0)
            {
                detail = "Projede export edilebilir harita yok — export dosyaya dokunmaz.";
                return false;
            }

            if (!string.Equals(BuildMapsJson(maps), File.ReadAllText(path), StringComparison.Ordinal))
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

        /// <summary><c>{ "maps": [ { "sceneName": "&lt;Scene&gt;", "venue": "&lt;Venue&gt;",
        /// "modes": ["&lt;modId&gt;"] } ] }</c> — for the server's <c>start_match</c> validation and
        /// venue selection.</summary>
        /// <remarks>Arena DIMENSIONS are not written: the server knows no metres (poses are
        /// client authoritative and arrive in arena space), and every venue's floor differs and is
        /// rarely even rectangular, so one pair of numbers would not describe it.</remarks>
        private static string BuildMapsJson(List<MapDefinition> maps)
        {
            var sb = new StringBuilder();
            sb.Append("{\n");

            if (maps.Count == 0)
            {
                sb.Append("  \"maps\": []\n");
            }
            else
            {
                sb.Append("  \"maps\": [\n");
                for (int i = 0; i < maps.Count; i++)
                {
                    MapDefinition map = maps[i];
                    sb.Append("    { \"sceneName\": \"").Append(EscapeJson(map.SceneName))
                        .Append("\", \"venue\": \"").Append(EscapeJson(VenueOf(map)))
                        .Append("\", \"modes\": ").Append(BuildModesArray(map.SupportedModeIds))
                        .Append(" }")
                        .Append(i < maps.Count - 1 ? ",\n" : "\n");
                }

                sb.Append("  ]\n");
            }

            sb.Append("}\n");
            return sb.ToString();
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
