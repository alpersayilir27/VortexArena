using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VortexArena.Core.Arena
{
    /// <summary>Floor levels derived from the <see cref="FloorPortal"/> instances in the loaded scenes (floor 0 = world y 0).</summary>
    /// <remarks>
    /// <b>No level is hand-entered anywhere</b> — the portal IS the drawn thing, exactly as
    /// <see cref="BaseZone"/> derives its area from the strip. A numeric level list would silently
    /// drift from the meshes: the player would be lifted to a height with no floor under it and
    /// nothing would report the error.
    /// <para>Rebuilt lazily on the first query after <see cref="MarkDirty"/>, so a scene full of
    /// portals is walked once per change, not per consumer.</para>
    /// </remarks>
    public static class ArenaFloors
    {
        /// <summary>How far an object's Y may sit from a floor level and still count as ON it (m).</summary>
        public const float LevelToleranceMeters = 0.25f;

        /// <summary>Two portals claiming the same floor pair must agree within this (m); tighter than
        /// <see cref="LevelToleranceMeters"/> because this compares two MEASUREMENTS, not an object's
        /// placement.</summary>
        private const float UpperMismatchMeters = 0.05f;

        // Floor 0 is always present: an arena with no portal is a single-floor arena.
        private static readonly List<float> Levels = new List<float> { 0f };
        private static readonly StringBuilder Summary = new StringBuilder();
        private static bool _dirty = true;

        static ArenaFloors()
        {
            SceneManager.sceneLoaded += (scene, mode) => MarkDirty();
            SceneManager.sceneUnloaded += scene => MarkDirty();
        }

        /// <summary>Floor count; 1 when the arena has no portal.</summary>
        public static int Count
        {
            get
            {
                Rebuild();
                return Levels.Count;
            }
        }

        /// <summary>Bumped on every rebuild; consumers cache their derived floor against it.</summary>
        public static int Version { get; private set; }

        /// <summary>World height of a floor (m); the index is clamped into range.</summary>
        public static float HeightOf(int floor)
        {
            Rebuild();
            return Levels[Mathf.Clamp(floor, 0, Levels.Count - 1)];
        }

        /// <summary>Floor an object at this world height sits on (0 when below every level).</summary>
        public static int FloorAt(float worldY)
        {
            Rebuild();
            return IndexAt(worldY);
        }

        /// <summary>Forces a rebuild on the next query; called by every portal on enable/disable and
        /// on scene load/unload.</summary>
        public static void MarkDirty()
        {
            _dirty = true;
        }

        private static int IndexAt(float worldY)
        {
            int index = 0;
            for (int i = 0; i < Levels.Count; i++)
            {
                if (Levels[i] <= worldY + LevelToleranceMeters)
                {
                    index = i;
                }
            }

            return index;
        }

        private static void Rebuild()
        {
            if (!_dirty)
            {
                return;
            }

            // Cleared first: a portal disabling itself below calls MarkDirty again, and that must
            // schedule the NEXT rebuild rather than re-enter this one.
            _dirty = false;

            Levels.Clear();
            Levels.Add(0f);

            FloorPortal[] portals = UnityEngine.Object.FindObjectsByType<FloorPortal>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            // Ascending Y: a portal can only add the floor ABOVE the one it stands on, so the lower
            // levels must exist before the higher portals are read.
            System.Array.Sort(portals, CompareByHeight);

            for (int i = 0; i < portals.Length; i++)
            {
                FloorPortal portal = portals[i];
                // A portal that switched itself off (root on no level) must not define a level either.
                if (portal == null || !portal.isActiveAndEnabled)
                {
                    continue;
                }

                float y = portal.transform.position.y;
                int lower = IndexAt(y);
                if (Mathf.Abs(y - Levels[lower]) > LevelToleranceMeters)
                {
                    Debug.LogWarning(
                        $"[ArenaFloors] '{portal.name}': kök yüksekliği {y:F2} m hiçbir kat zeminine " +
                        "oturmuyor — kat listesine katılmadı.", portal);
                    continue;
                }

                float upper = Levels[lower] + portal.UpperHeight;
                if (lower + 1 < Levels.Count)
                {
                    if (Mathf.Abs(Levels[lower + 1] - upper) > UpperMismatchMeters)
                    {
                        Debug.LogWarning(
                            $"[ArenaFloors] '{portal.name}': üst kat yüksekliği {upper:F2} m, aynı kat " +
                            $"için daha önce {Levels[lower + 1]:F2} m ölçüldü — ilk değer geçerli.", portal);
                    }

                    continue;
                }

                Levels.Add(upper);
            }

            WarnOverlappingPortals(portals);

            Version++;

            if (Levels.Count > 1)
            {
                LogSummary();
            }
        }

        /// <summary>Warns about portals whose discs share one X/Z spot on a touching floor pair: the
        /// player arriving from one lands inside the other's dwell and is bounced on. O(n²) on
        /// purpose — a scene holds a handful of portals.</summary>
        private static void WarnOverlappingPortals(FloorPortal[] portals)
        {
            float minSeparation = 2f * FloorPortal.RadiusMeters;

            for (int i = 0; i < portals.Length; i++)
            {
                FloorPortal a = portals[i];
                if (a == null || !a.isActiveAndEnabled)
                {
                    continue;
                }

                int lowerA = IndexAt(a.transform.position.y);

                for (int j = i + 1; j < portals.Length; j++)
                {
                    FloorPortal b = portals[j];
                    if (b == null || !b.isActiveAndEnabled)
                    {
                        continue;
                    }

                    Vector3 pa = a.transform.position;
                    Vector3 pb = b.transform.position;
                    float dx = pa.x - pb.x;
                    float dz = pa.z - pb.z;
                    if (dx * dx + dz * dz >= minSeparation * minSeparation)
                    {
                        continue;
                    }

                    int lowerB = IndexAt(pb.y);
                    bool touching = lowerA == lowerB || lowerA + 1 == lowerB || lowerB + 1 == lowerA;
                    if (!touching)
                    {
                        continue;
                    }

                    Debug.LogWarning(
                        $"[ArenaFloors] '{a.name}' ile '{b.name}' diskleri aynı noktada — bir portaldan " +
                        "gelen oyuncu doğrudan diğerinin dolumuna girer; portalları X/Z'de ayır.", a);
                }
            }
        }

        private static int CompareByHeight(FloorPortal a, FloorPortal b)
        {
            float ya = a != null ? a.transform.position.y : float.MaxValue;
            float yb = b != null ? b.transform.position.y : float.MaxValue;
            return ya.CompareTo(yb);
        }

        private static void LogSummary()
        {
            Summary.Clear();
            for (int i = 0; i < Levels.Count; i++)
            {
                if (i > 0)
                {
                    Summary.Append(" / ");
                }

                Summary.Append(Levels[i].ToString("F2")).Append(" m");
            }

            Debug.Log($"[ArenaFloors] {Levels.Count} kat: {Summary}");
        }
    }
}
