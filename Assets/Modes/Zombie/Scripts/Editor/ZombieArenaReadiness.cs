using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using VortexArena.Core;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Core.Editor;
using VortexArena.Net;
using Object = UnityEngine.Object;

namespace VortexArena.Modes.Zombie.Editor
{
    /// <summary>Build window readiness row for the OPEN scene: is it a playable zombie arena (NavMesh,
    /// a spawn point on every floor, a weapon board)?</summary>
    /// <remarks>Lives here and not in <c>BuildReadiness</c> because Core cannot reference a mode; the
    /// row is pushed in through <see cref="BuildReadiness.RegisterCheck"/>.
    /// <para>⚠️ Reads only — all three are human steps the tool cannot perform (bake, placement).</para>
    /// </remarks>
    [InitializeOnLoad]
    internal static class ZombieArenaReadiness
    {
        private const string RowTitle = "Zombi arenası hazır";

        /// <summary>Catalog mode id. ⚠️ <c>ZombieKinds</c> is internal to the runtime assembly, so the
        /// literal is repeated here; it must match the catalog's <c>modeId</c>.</summary>
        private const string ModeId = "zombie";

        /// <summary>Wire kind of the spawn point object (<c>ZombieKinds.SpawnPoint</c>).</summary>
        private const string SpawnKind = "zombie_spawn";

        private const string Tooltip =
            "AKTİF sahne bir zombi arenasıysa: NavMesh bake'li mi, HER katta en az bir zombie_spawn " +
            "var mı, sahnede silah tahtası (WeaponFrame) var mı. NE ZAMAN: zombi arenası kurarken ya " +
            "da kat/doğum noktası/silah tahtası düzenleyince. ✗ insan adımıdır, araç düzeltemez: " +
            "NavMesh'siz sahnede zombi doğduğu yerde kalır, noktası olmayan kat zombisiz kalır " +
            "(zombi kat portalı kullanamaz) ve silah tahtası olmayan arenada oyuncu dalga ilerledikçe " +
            "yeni silah alamaz. Zombi arenası olmayan sahnede satır temizdir.";

        static ZombieArenaReadiness()
        {
            BuildReadiness.RegisterCheck(RowTitle, IsZombieArenaReady, Tooltip);
        }

        private static bool IsZombieArenaReady(out string detail)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !SupportsZombie(scene.name))
            {
                detail = "aktif sahne zombi arenası değil";
                return true;
            }

            var missing = new List<string>();

            if (NavMesh.CalculateTriangulation().vertices.Length == 0)
            {
                missing.Add("NavMesh bake'li değil");
            }

            // Edit mode gets no sceneLoaded callback, so the cached floor list can predate the last
            // portal edit. MarkDirty only invalidates it — nothing is written.
            ArenaFloors.MarkDirty();
            int floors = Mathf.Max(1, ArenaFloors.Count);
            int[] spawnsPerFloor = new int[floors];
            int spawns = 0;

            NetObject[] netObjects =
                Object.FindObjectsByType<NetObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < netObjects.Length; i++)
            {
                NetObject netObject = netObjects[i];
                if (netObject == null || netObject.Kind == null || netObject.Kind.Kind != SpawnKind)
                {
                    continue;
                }

                int floor = Mathf.Clamp(ArenaFloors.FloorAt(netObject.transform.position.y), 0, floors - 1);
                spawnsPerFloor[floor]++;
                spawns++;
            }

            for (int floor = 0; floor < floors; floor++)
            {
                if (spawnsPerFloor[floor] == 0)
                {
                    missing.Add($"kat {floor} doğum noktasız");
                }
            }

            int frames = Object
                .FindObjectsByType<WeaponFrame>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Length;
            if (frames == 0)
            {
                missing.Add("silah tahtası yok");
            }

            if (missing.Count == 0)
            {
                detail = $"NavMesh var, {floors} katın hepsinde doğum noktası var " +
                         $"(toplam {spawns}), silah tahtası var ({frames} yuva).";
                return true;
            }

            detail = "eksik: " + string.Join(" · ", missing) + ".";
            return false;
        }

        /// <summary>Does the scene's map list <c>zombie</c> among its supported modes?</summary>
        private static bool SupportsZombie(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                return false;
            }

            // Resources path is the catalog's documented home (GameCatalog summary).
            var catalog = Resources.Load<GameCatalog>("GameCatalog");
            MapDefinition map = catalog != null ? catalog.FindMap(sceneName) : null;
            string[] modeIds = map != null ? map.SupportedModeIds : null;
            if (modeIds == null)
            {
                return false;
            }

            for (int i = 0; i < modeIds.Length; i++)
            {
                if (modeIds[i] == ModeId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
