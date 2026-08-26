using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VortexArena.Net.Editor
{
    /// <summary>
    /// Writes the scene's <see cref="NetObject"/> list to <c>&lt;scene folder&gt;/Data/&lt;Scene&gt;_objects.json</c>
    /// on every scene save; <c>ServerConfigExporter</c> folds it into <c>maps.json → maps[].objects</c>
    /// (Docs/ArenaNet-Protokol.md §10.10, §11).
    /// <para>
    /// It exists so the server config export never has to OPEN a scene: baked ids only live inside the
    /// scene file, and the export runs over assets. Whoever saves the scene also publishes its object
    /// list, in the same step.
    /// </para>
    /// <para><b>Determinism (keep the git diff clean):</b> ascending sceneId order, LF, UTF-8 without
    /// BOM, single trailing <c>\n</c>. Same scene → same bytes.</para>
    /// </summary>
    internal static class SceneObjectExporter
    {
        /// <summary>Subfolder next to the scene file, shared with <c>Data/&lt;Scene&gt;.asset</c>
        /// (MapDefinition) — the export resolves the object list from there.</summary>
        private const string DATA_FOLDER = "Data";

        private const string FILE_SUFFIX = "_objects.json";

        /// <summary>Writes (or deletes) the object list of a scene being saved.</summary>
        /// <remarks>⚠️ Runs UNCONDITIONALLY, even when the id repair changed nothing: a NetObject's
        /// KIND may have changed without touching any id.</remarks>
        internal static void WriteForScene(Scene scene, string scenePath)
        {
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scenePath))
            {
                return;
            }

            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            string sceneDirectory = Path.GetDirectoryName(scenePath);
            if (string.IsNullOrEmpty(sceneName) || string.IsNullOrEmpty(sceneDirectory))
            {
                return;
            }

            // Asset-relative path throughout: the editor's working directory is the project root, and
            // AssetDatabase needs this form for the delete branch.
            string dataDirectory = sceneDirectory.Replace('\\', '/') + "/" + DATA_FOLDER;
            string filePath = dataDirectory + "/" + sceneName + FILE_SUFFIX;

            List<NetObject> objects = Collect(scene);
            List<string> entries = BuildEntries(objects);

            if (entries.Count == 0)
            {
                // A stale list must not outlive the objects: the server would keep breakables the scene
                // no longer has.
                DeleteIfExists(filePath);
                return;
            }

            Directory.CreateDirectory(dataDirectory);
            File.WriteAllText(filePath, BuildJson(entries), new UTF8Encoding(false));
        }

        /// <summary>Every NetObject in the scene, INACTIVE included; deterministic order (root order →
        /// hierarchy order), same pattern as <see cref="SceneIdUtility.CollectInScene"/>.</summary>
        private static List<NetObject> Collect(Scene scene)
        {
            var result = new List<NetObject>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                result.AddRange(roots[i].GetComponentsInChildren<NetObject>(true));
            }

            return result;
        }

        /// <summary>Validates and sorts the rows; a rejected object gets one console line — an object
        /// silently missing from the file is diagnosed in the field as "it does not break".</summary>
        private static List<string> BuildEntries(List<NetObject> objects)
        {
            var rows = new List<KeyValuePair<uint, string>>(objects.Count);

            for (int i = 0; i < objects.Count; i++)
            {
                NetObject netObject = objects[i];
                if (netObject == null)
                {
                    continue;
                }

                if (netObject.Kind == null || string.IsNullOrWhiteSpace(netObject.Kind.Kind))
                {
                    Debug.LogWarning(
                        $"[VortexArena] '{netObject.name}' için NetObjectKind/kind boş — obje listeye yazılmadı.",
                        netObject);
                    continue;
                }

                var identity = netObject.GetComponent<NetIdentity>();
                uint sceneId = identity != null ? identity.SceneId : 0u;
                if (!SceneIdUtility.IsInRange(sceneId))
                {
                    Debug.LogWarning(
                        $"[VortexArena] '{netObject.name}' sahne kimliği geçersiz ({sceneId}) — obje listeye yazılmadı.",
                        netObject);
                    continue;
                }

                // Warn but still export: the object is valid on the wire, it just cannot be hit. The
                // field symptom of a missing collider is only "it does not break".
                if (netObject.Kind.MaxHp > 0f && !HasHitCollider(netObject))
                {
                    Debug.LogWarning(
                        $"[VortexArena] '{netObject.name}' hasar alabilir ('{netObject.Kind.Kind}') ama " +
                        "raycast'e takılan collider'ı yok — vurulamaz. Objeye bir collider ekleyin.",
                        netObject);
                }

                rows.Add(new KeyValuePair<uint, string>(
                    sceneId,
                    $"    {{ \"sceneId\": {sceneId}, \"kind\": \"{EscapeJson(netObject.Kind.Kind)}\" }}"));
            }

            rows.Sort((a, b) => a.Key.CompareTo(b.Key));

            var entries = new List<string>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                entries.Add(rows[i].Value);
            }

            return entries;
        }

        /// <summary>Any enabled collider in the subtree — a disabled one is invisible to the raycast.</summary>
        private static bool HasHitCollider(NetObject netObject)
        {
            Collider[] colliders = netObject.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null && colliders[i].enabled)
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildJson(List<string> entries)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"objects\": [\n");

            for (int i = 0; i < entries.Count; i++)
            {
                sb.Append(entries[i]).Append(i < entries.Count - 1 ? ",\n" : "\n");
            }

            return sb.Append("  ]\n}\n").ToString();
        }

        /// <summary>Deletes through AssetDatabase so the <c>.meta</c> goes with it; plain File.Delete
        /// would leave an orphan meta behind.</summary>
        private static void DeleteIfExists(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            if (!AssetDatabase.DeleteAsset(filePath))
            {
                File.Delete(filePath);
            }
        }

        /// <summary>Minimal JSON string escaping (kind ids are ASCII, but stay safe).</summary>
        private static string EscapeJson(string value)
        {
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }
}
