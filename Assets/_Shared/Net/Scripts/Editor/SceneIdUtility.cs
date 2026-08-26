using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Protocol;

namespace VortexArena.Net.Editor
{
    /// <summary>
    /// sceneId bake helpers: collecting <see cref="NetIdentity"/> in a scene, finding a free id, writing
    /// it and repairing 0/collisions. <c>NetworkParentMenu</c> and <c>SceneIdGuard</c> share this logic
    /// (single source of truth).
    /// <para>
    /// The field being <c>private</c>, writing ALWAYS goes through <see cref="SerializedObject"/>: no
    /// editor-only setter on the runtime side (the Net layer stays clean, and prefab overrides and Undo
    /// recording work by themselves).
    /// </para>
    /// </summary>
    internal static class SceneIdUtility
    {
        /// <summary>The name of the serialized field — must match the one in <see cref="NetIdentity"/> exactly.</summary>
        private const string SCENE_ID_PROPERTY = "sceneId";

        /// <summary>Baked scene ids stay in the LOWER half of the id space; the upper half is RESERVED
        /// for the server's dynamically spawned objects. On overflow a scene object and a dynamic
        /// object share an id and the server cannot tell which one a hit damaged.</summary>
        private const uint MIN_ID = ArenaProtocol.NET_ID_SCENE_MIN;

        /// <inheritdoc cref="MIN_ID"/>
        private const uint MAX_ID = ArenaProtocol.NET_ID_SCENE_MAX;

        /// <summary>Whether an id is addressable at all (0 = never assigned, above the range = would
        /// collide with a server-allocated id).</summary>
        internal static bool IsInRange(uint id)
        {
            return id >= MIN_ID && id <= MAX_ID;
        }

        /// <summary>
        /// Collects every NetIdentity in the scene (INACTIVE included). Deterministic order: root object
        /// order → <c>GetComponentsInChildren</c> hierarchy order — the repair being reproducible across
        /// runs rests on it.
        /// </summary>
        internal static List<NetIdentity> CollectInScene(Scene scene)
        {
            var result = new List<NetIdentity>();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return result;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                result.AddRange(roots[i].GetComponentsInChildren<NetIdentity>(true));
            }

            return result;
        }

        /// <summary>The scene's first free sceneId, always inside
        /// <see cref="MIN_ID"/>..<see cref="MAX_ID"/>; <c>0</c> = the range is full.</summary>
        internal static uint NextFreeId(Scene scene)
        {
            return NextFreeId(CollectInScene(scene));
        }

        /// <summary>
        /// max + 1 over an already collected list, clamped into the scene range. A list-taking overload
        /// so a loop does not rescan the scene over and over.
        /// </summary>
        /// <returns><c>0</c> when every id in the range is taken — the caller must treat that as "no
        /// room", NOT as a usable id.</returns>
        internal static uint NextFreeId(IReadOnlyList<NetIdentity> identities)
        {
            var used = new HashSet<uint>();
            uint max = 0u;

            if (identities != null)
            {
                for (int i = 0; i < identities.Count; i++)
                {
                    NetIdentity identity = identities[i];
                    if (identity == null || !IsInRange(identity.SceneId))
                    {
                        continue;
                    }

                    used.Add(identity.SceneId);
                    if (identity.SceneId > max)
                    {
                        max = identity.SceneId;
                    }
                }
            }

            return ResolveFree(used, max + 1u);
        }

        /// <summary>First unused id at or after <paramref name="from"/>; once the top is reached the
        /// scan wraps to reuse holes left by deleted objects. <c>0</c> = full range is taken.</summary>
        private static uint ResolveFree(HashSet<uint> used, uint from)
        {
            if (from < MIN_ID)
            {
                from = MIN_ID;
            }

            for (uint id = from; id <= MAX_ID; id++)
            {
                if (!used.Contains(id))
                {
                    return id;
                }
            }

            for (uint id = MIN_ID; id <= MAX_ID && id < from; id++)
            {
                if (!used.Contains(id))
                {
                    return id;
                }
            }

            return 0u;
        }

        /// <summary>
        /// Writes sceneId through a SerializedObject and marks the object dirty. Does nothing when the
        /// value is already the requested one (so as not to produce a needless scene diff). Returns
        /// true only when a write actually happened.
        /// </summary>
        internal static bool AssignId(NetIdentity identity, uint value)
        {
            if (identity == null || identity.SceneId == value)
            {
                return false;
            }

            var serialized = new SerializedObject(identity);
            SerializedProperty property = serialized.FindProperty(SCENE_ID_PROPERTY);
            if (property == null)
            {
                Debug.LogError(
                    $"[VortexArena] NetIdentity.{SCENE_ID_PROPERTY} alanı bulunamadı — alan yeniden mi adlandırıldı?",
                    identity);
                return false;
            }

            property.uintValue = value;
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(identity);
            return true;
        }

        /// <summary>
        /// Repairs sceneIds that are OUT OF RANGE (0 or above <see cref="MAX_ID"/>) or COLLIDE in the
        /// scene: the FIRST CLAIMANT of an id keeps it, the later ones get the next free id. Since the
        /// scan follows hierarchy order the result is deterministic. Returns true when a repair
        /// happened; <paramref name="fixedCount"/> is the number of changed components.
        /// </summary>
        internal static bool RepairScene(Scene scene, out int fixedCount)
        {
            fixedCount = 0;

            List<NetIdentity> identities = CollectInScene(scene);
            if (identities.Count == 0)
            {
                return false;
            }

            var used = new HashSet<uint>();
            uint next = NextFreeId(identities);

            for (int i = 0; i < identities.Count; i++)
            {
                NetIdentity identity = identities[i];
                if (identity == null)
                {
                    continue;
                }

                // Out of range = never assigned (0) or reaching into the server's reserved half;
                // used.Add false = an earlier object already claimed this id.
                if (IsInRange(identity.SceneId) && used.Add(identity.SceneId))
                {
                    continue;
                }

                next = ResolveFree(used, next);
                if (next == 0u)
                {
                    Debug.LogError(
                        $"[VortexArena] '{scene.name}': sahne kimliği aralığı ({MIN_ID}..{MAX_ID}) doldu — " +
                        $"'{identity.name}' kimliksiz kaldı, bu obje ağda görünmez.",
                        identity);
                    continue;
                }

                if (AssignId(identity, next))
                {
                    fixedCount++;
                }

                used.Add(next);
                next++;
            }

            return fixedCount > 0;
        }
    }
}
