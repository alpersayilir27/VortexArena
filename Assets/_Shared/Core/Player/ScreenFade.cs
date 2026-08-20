using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>The <b>arbiter</b> of the HMD blackout quad: several sources report their own blackout,
    /// the highest alpha wins and a <b>single</b> draw happens.</summary>
    /// <remarks>
    /// <b>Why it exists:</b> the quad is single but several systems want it (approaching the arena
    /// boundary · obstacle violation). Writing to the <c>MaterialPropertyBlock</c> directly, they would
    /// overwrite each other per frame — the symptom would be "the screen flickers when I enter an
    /// obstacle near the boundary", with its cause spread over two components.
    /// <para>⚠️ <b>This class DRAWS nothing.</b> The renderer is owned by
    /// <see cref="VortexArena.Core.Arena.ArenaBoundary"/> (the quad is its serialized field, bound in the
    /// rig prefab) and it applies the winner. Adding drawing here would create a second renderer
    /// owner.</para>
    /// <para><b>Heartbeat contract:</b> a source reports <b>every frame</b>; one that stops reporting
    /// drops out by itself after <see cref="EntryTimeoutSeconds"/>. Forgetting to say "off" is impossible
    /// — such a source would leave the screen permanently black. (The same pattern is used in
    /// <c>PlayerCombatState.RequestBaseTracking</c>.)</para>
    /// </remarks>
    public static class ScreenFade
    {
        /// <summary>If a report gets older than this, the source counts as gone silent (s).</summary>
        private const float EntryTimeoutSeconds = 0.25f;

        private struct Entry
        {
            public float Alpha;
            public Color Color;
            public float Time;
        }

        // The number of sources is single-digit; the dictionary is for setup convenience (the sources do
        // not know about each other).
        private static readonly Dictionary<string, Entry> Sources = new Dictionary<string, Entry>();

        /// <summary>A source's blackout request for that frame. If <paramref name="alpha"/> is <c>0</c>
        /// the source does not want a blackout (same outcome as not reporting, but explicit).</summary>
        /// <param name="sourceId">The source's fixed id (e.g. "boundary", "obstacle").</param>
        /// <param name="alpha">Blackout intensity, 0..1.</param>
        /// <param name="color">The blackout's RGB — the alpha channel is IGNORED.</param>
        public static void Report(string sourceId, float alpha, Color color)
        {
            if (string.IsNullOrEmpty(sourceId))
            {
                return;
            }

            Sources[sourceId] = new Entry
            {
                Alpha = Mathf.Clamp01(alpha),
                Color = color,
                // ⚠️ unscaledTime: the blackout is a PRESENTATION layer and must keep its freshness even
                // when the match is paused (if Time.timeScale is played with).
                Time = UnityEngine.Time.unscaledTime
            };
        }

        /// <summary>Returns the winning blackout: the source asking for the <b>highest alpha</b>.
        /// <c>false</c> means no fresh source wants a blackout (the drawing side then hides the
        /// quad).</summary>
        /// <remarks>Mixing (adding/multiplying alpha) is deliberately ABSENT: two overlapping
        /// semi-transparent layers give a result darker than either, and the answer to "why did it go this
        /// dark" would be found in no single source. Taking the highest is always a value some source
        /// asked for.</remarks>
        public static bool Resolve(out float alpha, out Color color)
        {
            alpha = 0f;
            color = Color.black;

            float now = UnityEngine.Time.unscaledTime;
            bool found = false;

            foreach (KeyValuePair<string, Entry> kv in Sources)
            {
                Entry entry = kv.Value;
                if (now - entry.Time > EntryTimeoutSeconds)
                {
                    continue; // a source that went silent
                }

                if (entry.Alpha <= alpha)
                {
                    continue;
                }

                alpha = entry.Alpha;
                color = entry.Color;
                found = true;
            }

            return found && alpha > 0.001f;
        }
    }
}
