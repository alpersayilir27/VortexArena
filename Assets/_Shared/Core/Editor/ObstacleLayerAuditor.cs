using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.Arena;

namespace VortexArena.Core.Editor
{
    /// <summary>Audits the <c>Obstacle</c> layer in the open scenes: the one setup rule the obstacle
    /// violation system needs is a CONVEX collider, and that cannot be checked by eye.</summary>
    /// <remarks>
    /// A non-convex <c>MeshCollider</c> on this layer makes <c>Collider.ClosestPoint</c> return the
    /// input point unchanged → the point-inside test always says "inside" → everyone in that scene
    /// starts dying instantly. At runtime <c>ObstacleVolumes</c> discards such a collider and logs
    /// an error, but that line is only seen by someone entering Play and reading the console; this
    /// tool asks the same question before the scene is saved.
    /// <para>⚠️ The tool FIXES nothing, it only reports. Auto-marking a collider convex or changing
    /// its layer would silently override a deliberate artist choice.</para>
    /// </remarks>
    public static class ObstacleLayerAuditor
    {
        private const string MenuPath = "Tools/VortexArena/Arena/Engel Hacimlerini Denetle";

        [MenuItem(MenuPath)]
        public static void Audit()
        {
            int layer = LayerMask.NameToLayer(ArenaLayers.ObstacleName);
            if (layer < 0)
            {
                EditorUtility.DisplayDialog(
                    "Engel hacimleri",
                    $"'{ArenaLayers.ObstacleName}' layer'ı projede tanımlı değil.\n\n" +
                    "Project Settings > Tags and Layers altında bu adla bir user layer açılmadan " +
                    "engel ihlali tespiti hiç çalışmaz.",
                    "Tamam");
                return;
            }

            var nonConvex = new List<GameObject>();
            var triggers = new List<GameObject>();
            var colliderless = new List<GameObject>();
            var swollen = new List<GameObject>();
            int healthy = 0;

            foreach (GameObject root in EditorSceneRoots())
            {
                Transform[] all = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    GameObject go = all[i].gameObject;
                    if (go.layer != layer)
                    {
                        continue;
                    }

                    var collider = go.GetComponent<Collider>();
                    if (collider == null)
                    {
                        // Layer stamped but no collider: usually the layer applied to children
                        // (visual meshes) — harmless, reported only to surface the intent.
                        colliderless.Add(go);
                        continue;
                    }

                    if (collider.isTrigger)
                    {
                        triggers.Add(go);
                        continue;
                    }

                    if (collider is MeshCollider mesh && !mesh.convex)
                    {
                        nonConvex.Add(go);
                        continue;
                    }

                    if (IsSwollen(go, collider))
                    {
                        swollen.Add(go);
                        continue;
                    }

                    healthy++;
                }
            }

            Report(nonConvex, triggers, colliderless, swollen, healthy);
        }

        // ---------------------------------------------------------------- swelling

        /// <summary>Max sampled triangles in the swelling test (evenly spaced on large meshes).</summary>
        private const int MaxSwellSamples = 200;

        /// <summary>Probe offset to each side of the surface (m). If the collider coincides with the
        /// surface one side is inside and the other outside; both inside means it swells there.</summary>
        private const float SwellProbeMeters = 0.02f;

        /// <summary>Minimum sample ratio and count to call it swollen, so isolated numeric noise
        /// does not produce a report.</summary>
        private const float SwellRatioThreshold = 0.02f;
        private const int SwellMinHits = 3;

        /// <summary>Whether the collider swells beyond the surface of its own source mesh.</summary>
        /// <remarks>
        /// The remaining geometric trap: on a concave mesh marked convex the hull fills the cavity,
        /// the collider grows past the visible surface and the player is punished in mid-air —
        /// invisible to the eye, because the drawn mesh is correct.
        /// <para>The criterion is the same as the runtime test (point-inside via
        /// <c>ClosestPoint</c>, <c>ObstacleVolumes</c>): triangle centroids are pushed out along the
        /// normal and asked "still inside?". Separate math such as a volume/hull comparison would be
        /// a second source of truth.</para>
        /// <para>⚠️ Only <see cref="MeshCollider"/> is checked, and against the collider's own mesh,
        /// not a MeshFilter: only a MeshCollider claims "I am this surface". Box/Capsule is a
        /// deliberate coarsening (the recommended fix), so reporting it as swollen would make the
        /// tool flag its own advice.</para>
        /// <para>Unreadable mesh (Read/Write off) or a disabled collider/object skips the test and
        /// returns <c>false</c>: an object that could not be checked is not reported as broken.
        /// ⚠️ On a disabled collider <c>ClosestPoint</c> returns the input point unchanged, so
        /// without this gate every disabled object would read as swollen.</para>
        /// </remarks>
        private static bool IsSwollen(GameObject go, Collider collider)
        {
            if (collider is not MeshCollider meshCollider || !collider.enabled ||
                !go.activeInHierarchy)
            {
                return false;
            }

            Mesh mesh = meshCollider.sharedMesh;
            if (mesh == null || !mesh.isReadable)
            {
                return false;
            }

            int[] triangles = mesh.triangles;
            Vector3[] vertices = mesh.vertices;
            int triangleCount = triangles.Length / 3;
            if (triangleCount == 0)
            {
                return false;
            }

            Transform tr = go.transform;
            int step = Mathf.Max(1, triangleCount / MaxSwellSamples);
            int sampled = 0;
            int inside = 0;

            for (int t = 0; t < triangleCount; t += step)
            {
                int i = t * 3;
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];

                Vector3 normal = Vector3.Cross(b - a, c - a);
                if (normal.sqrMagnitude <= 1e-12f)
                {
                    continue; // degenerate triangle
                }

                Vector3 worldCentroid = tr.TransformPoint((a + b + c) / 3f);
                Vector3 offset = tr.TransformDirection(normal).normalized * SwellProbeMeters;

                // ⚠️ BOTH sides are probed and the criterion is "both inside". Testing one side
                // would trust triangle winding: read the wrong way every point falls inside the
                // body and every object would be reported swollen. A surface inside on both sides
                // is buried IN the collider regardless of winding — exactly the case sought (hull
                // filled the cavity).
                sampled++;
                if (IsInside(collider, worldCentroid + offset) &&
                    IsInside(collider, worldCentroid - offset))
                {
                    inside++;
                }
            }

            return sampled > 0 && inside >= SwellMinHits && inside >= sampled * SwellRatioThreshold;
        }

        /// <summary>On a convex collider <c>ClosestPoint</c> of an inside point is the point ITSELF
        /// (same test as runtime <c>ObstacleVolumes</c>).</summary>
        private static bool IsInside(Collider collider, Vector3 point) =>
            (collider.ClosestPoint(point) - point).sqrMagnitude <= 1e-8f;

        private static void Report(List<GameObject> nonConvex, List<GameObject> triggers,
            List<GameObject> colliderless, List<GameObject> swollen, int healthy)
        {
            var text = new StringBuilder();
            text.AppendLine($"Sağlam engel hacmi: {healthy}");

            if (nonConvex.Count > 0)
            {
                text.AppendLine();
                text.AppendLine($"⛔ KONVEKS DEĞİL ({nonConvex.Count}) — bu objeler engel ihlali");
                text.AppendLine("hesabından ÇIKARILIR (çalışma anında da elenirler).");
                text.AppendLine("MeshCollider > Convex işaretle ya da kaba bir Box/Capsule kullan:");
                AppendNames(text, nonConvex);
            }

            if (triggers.Count > 0)
            {
                text.AppendLine();
                text.AppendLine($"⚠️ TRIGGER ({triggers.Count}) — engel hacmi katı olmalı; trigger");
                text.AppendLine("collider hem mermiyi durdurur hem tespitte yok sayılır:");
                AppendNames(text, triggers);
            }

            if (swollen.Count > 0)
            {
                text.AppendLine();
                text.AppendLine($"⚠️ ŞİŞKİN ({swollen.Count}) — collider görünen yüzeyin DIŞINA taşıyor.");
                text.AppendLine("Sebebi genelde içbükey bir mesh'in convex işaretlenmesidir (hull");
                text.AppendLine("çukuru doldurur). Oyuncu bu objelerde BOŞLUKTA ceza alır — şekli");
                text.AppendLine("konveks parçalara böl ya da kaba bir Box/Capsule kullan:");
                AppendNames(text, swollen);
            }

            if (colliderless.Count > 0)
            {
                text.AppendLine();
                text.AppendLine($"ℹ️ Collider'sız ({colliderless.Count}) — layer damgalı ama collider yok;");
                text.AppendLine("bu objeler tespitte hiç görünmez:");
                AppendNames(text, colliderless);
            }

            if (nonConvex.Count == 0 && triggers.Count == 0 && colliderless.Count == 0 &&
                swollen.Count == 0)
            {
                text.AppendLine();
                text.AppendLine("Sorun bulunmadı.");
            }

            // Also logged: the list must survive the dialog and stay clickable to the object.
            for (int i = 0; i < nonConvex.Count; i++)
            {
                Debug.LogError($"[Engel denetimi] '{Path(nonConvex[i])}' konveks değil — " +
                               "engel ihlali hesabından çıkarıldı.", nonConvex[i]);
            }

            for (int i = 0; i < triggers.Count; i++)
            {
                Debug.LogWarning($"[Engel denetimi] '{Path(triggers[i])}' trigger — engel hacmi " +
                                 "katı olmalı.", triggers[i]);
            }

            for (int i = 0; i < swollen.Count; i++)
            {
                Debug.LogWarning($"[Engel denetimi] '{Path(swollen[i])}' collider'ı görünen yüzeyden " +
                                 "şişkin — oyuncu bu objede boşlukta ceza alır.", swollen[i]);
            }

            EditorUtility.DisplayDialog("Engel hacimleri", text.ToString(), "Tamam");
        }

        private static void AppendNames(StringBuilder text, List<GameObject> objects)
        {
            const int MaxListed = 12;
            for (int i = 0; i < objects.Count && i < MaxListed; i++)
            {
                text.AppendLine($"  • {Path(objects[i])}");
            }

            if (objects.Count > MaxListed)
            {
                text.AppendLine($"  … ve {objects.Count - MaxListed} tane daha (tamamı konsolda)");
            }
        }

        private static string Path(GameObject go)
        {
            string path = go.name;
            Transform parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        /// <summary>Roots of ALL open scenes (additively loaded ones included).</summary>
        private static IEnumerable<GameObject> EditorSceneRoots()
        {
            int count = UnityEngine.SceneManagement.SceneManager.sceneCount;
            for (int i = 0; i < count; i++)
            {
                UnityEngine.SceneManagement.Scene scene =
                    UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                GameObject[] roots = scene.GetRootGameObjects();
                for (int r = 0; r < roots.Length; r++)
                {
                    yield return roots[r];
                }
            }
        }
    }
}
