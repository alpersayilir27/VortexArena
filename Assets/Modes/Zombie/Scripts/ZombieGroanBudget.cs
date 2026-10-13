using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Which zombies are allowed to groan right now: only the N nearest to the local
    /// listener.</summary>
    /// <remarks>⚠️ A budget and not a per-zombie level decision: a late wave puts dozens of zombies on
    /// the map and all of them groaning at once turns into one flat drone — and the headset's voice
    /// limit then steals virtual sources from the NEAR ones.
    /// <para>Ranked at most every <see cref="RankIntervalSeconds"/> into reused lists; a zombie asks
    /// only when its own groan timer fires, so this never runs per frame per zombie.</para></remarks>
    internal static class ZombieGroanBudget
    {
        /// <summary>Re-rank interval (s). A zombie walks about 1 m/s, so half a second cannot move one
        /// far enough for the pick to be heard as wrong.</summary>
        private const float RankIntervalSeconds = 0.5f;

        private static readonly List<ZombieView> Members = new List<ZombieView>();

        private static readonly List<ZombieView> Nearest = new List<ZombieView>();
        private static readonly List<float> NearestSqr = new List<float>();

        private static float _nextRank;

        public static void Register(ZombieView view)
        {
            if (view != null && !Members.Contains(view))
            {
                Members.Add(view);

                // Rank again on the next ask: a zombie that just appeared next to the player must not
                // wait out the interval in silence.
                _nextRank = 0f;
            }
        }

        public static void Unregister(ZombieView view)
        {
            if (!Members.Remove(view))
            {
                return;
            }

            int at = Nearest.IndexOf(view);
            if (at >= 0)
            {
                Nearest.RemoveAt(at);
                NearestSqr.RemoveAt(at);
            }
        }

        /// <summary>May <paramref name="view"/> groan now?</summary>
        public static bool MayGroan(ZombieView view, int budget)
        {
            Rank(Mathf.Max(1, budget));
            return Nearest.Contains(view);
        }

        private static void Rank(int budget)
        {
            if (Time.unscaledTime < _nextRank)
            {
                return;
            }

            _nextRank = Time.unscaledTime + RankIntervalSeconds;

            Nearest.Clear();
            NearestSqr.Clear();

            // Camera.main is the listener's position both on a headset and on the admin's spectator
            // camera — the same reference the shot FX use.
            Camera listener = Camera.main;
            if (listener == null)
            {
                return;
            }

            Vector3 ear = listener.transform.position;

            for (int i = Members.Count - 1; i >= 0; i--)
            {
                ZombieView view = Members[i];
                if (view == null)
                {
                    Members.RemoveAt(i);
                    continue;
                }

                Insert(view, (view.transform.position - ear).sqrMagnitude, budget);
            }
        }

        /// <summary>Sorted insert capped at <paramref name="budget"/>; the lists are reused, so after
        /// the first wave the ranking allocates nothing.</summary>
        private static void Insert(ZombieView view, float sqrDistance, int budget)
        {
            int at = NearestSqr.Count;
            for (int i = 0; i < NearestSqr.Count; i++)
            {
                if (sqrDistance < NearestSqr[i])
                {
                    at = i;
                    break;
                }
            }

            if (at >= budget)
            {
                return;
            }

            Nearest.Insert(at, view);
            NearestSqr.Insert(at, sqrDistance);

            if (Nearest.Count > budget)
            {
                Nearest.RemoveAt(Nearest.Count - 1);
                NearestSqr.RemoveAt(NearestSqr.Count - 1);
            }
        }
    }
}
