using System.Collections.Generic;
using UnityEngine;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Who may be chased: the ids the last <c>lobby_state</c> listed as CONNECTED and
    /// CALIBRATED (§10.6).</summary>
    /// <remarks>⚠️ An uncalibrated player cannot take damage, so a zombie that picks them as its target
    /// stands in front of them for the rest of the wave — the roster is the only place that flag exists.
    /// Admin records always carry <c>calibrated:false</c>, which keeps the operator out of the set for
    /// free.
    /// <para>ONE static subscription for the whole mode on purpose: a dozen zombies each rebuilding the
    /// same set from every roster broadcast is the same answer computed twelve times.</para></remarks>
    internal static class ZombieRoster
    {
        private static readonly HashSet<int> Chaseable = new HashSet<int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            // Static handler on a static event: nothing to unsubscribe, and the set survives scene loads
            // exactly like the roster it mirrors.
            NetEvents.OnLobbyState += Apply;
            NetEvents.OnDisconnected += Chaseable.Clear;
        }

        private static void Apply(LobbyStateMsg msg)
        {
            Chaseable.Clear();
            if (msg?.players == null)
            {
                return;
            }

            for (int i = 0; i < msg.players.Length; i++)
            {
                PlayerInfo info = msg.players[i];
                if (info != null && info.calibrated &&
                    info.connection == ArenaProtocol.CONNECTION_CONNECTED)
                {
                    Chaseable.Add(info.playerId);
                }
            }
        }

        /// <summary>⚠️ Empty set → <c>false</c> for everyone: before the first roster nobody is chased,
        /// which is the safe direction (a zombie waiting one second beats a zombie beating an
        /// uncalibrated player).</summary>
        public static bool CanBeChased(int playerId) => Chaseable.Contains(playerId);
    }
}
