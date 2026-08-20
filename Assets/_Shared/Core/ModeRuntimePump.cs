using UnityEngine;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core
{
    /// <summary>
    /// The single feed point of <see cref="ModeRuntime"/>: it listens to <c>load_match</c> /
    /// <c>welcome</c> and snaps back to the defaults on return-to-lobby and on disconnect.
    /// <para>
    /// It does NOT live in the scene: <c>load_match</c> arrives BEFORE the scene is loaded, so it
    /// bootstraps itself with the <c>PlayerCombatState</c>/<c>ArenaClient</c> pattern
    /// (<c>AfterSceneLoad</c> + <c>DontDestroyOnLoad</c>).
    /// </para>
    /// <para>
    /// The components that read the rules are born from the same hook and the order of the three
    /// <c>AfterSceneLoad</c> calls is UNDEFINED — but this is not a race: a rule message can only
    /// arrive after a network connection has been established, and that happens at the earliest in
    /// the scene's <c>Start()</c> calls.
    /// </para>
    /// <para>There is no role split: admin receives the same rules too (team mode feeds the UI's
    /// single/double column decision).</para>
    /// </summary>
    public class ModeRuntimePump : MonoBehaviour
    {
        private static ModeRuntimePump _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("[ModeRuntimePump]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ModeRuntimePump>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // We are a persistent singleton: subscription happens in Awake/OnDestroy instead of
            // OnEnable/OnDisable so that no rule message is missed even if the object is disabled.
            NetEvents.OnLoadMatch += HandleLoadMatch;
            NetEvents.OnConnected += HandleConnected;
            NetEvents.OnReturnToLobby += HandleReturnToLobby;
            NetEvents.OnDisconnected += HandleDisconnected;
            NetEvents.OnSelectionState += HandleSelectionState;
            NetEvents.OnRulesUpdate += HandleRulesUpdate;

            // With domain reload disabled, statics can linger from the previous session.
            ModeRuntime.Reset();
            ModeSelection.Reset();
        }

        private void OnDestroy()
        {
            if (_instance != this)
            {
                return;
            }

            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnReturnToLobby -= HandleReturnToLobby;
            NetEvents.OnDisconnected -= HandleDisconnected;
            NetEvents.OnSelectionState -= HandleSelectionState;
            NetEvents.OnRulesUpdate -= HandleRulesUpdate;

            _instance = null;
        }

        /// <summary>A match is being set up: the rules come from this message. If <c>rules</c> is
        /// empty (a server that does not carry rules) the catalog takes over —
        /// <see cref="ModeRuntime.ApplyFromCatalog"/>.</summary>
        private static void HandleLoadMatch(LoadMatchMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            ModeRuntime.Apply(msg.modeId, msg.rules);
        }

        /// <summary>Late join: the running match's rules come from <c>welcome.match</c>.</summary>
        private static void HandleConnected(WelcomeMsg msg)
        {
            if (msg?.match == null)
            {
                return;
            }

            // On a server idling in the lobby the mode is empty; in that case the default is already
            // the correct answer.
            ModeRuntime.Apply(msg.match.modeId, msg.match.rules);
        }

        /// <summary>Return to lobby: the rules are NOT reset, the lobby profile is applied (§10.7).
        /// <para>The lobby carries content too — its weapon loadout is resolved from the catalog via
        /// <c>ModeRuntime.ModeId</c>. If the message carries an empty <c>modeId</c> (lobby not
        /// configured, or an old server) <see cref="ModeRuntime.Apply"/> already falls back to the
        /// default, i.e. it becomes identical to the old behaviour.</para></summary>
        private static void HandleReturnToLobby(ReturnToLobbyMsg msg)
        {
            if (msg == null)
            {
                ModeRuntime.Reset();
                return;
            }

            ModeRuntime.Apply(msg.modeId, msg.rules);
        }

        /// <summary>
        /// Selection notification (§5.3 <c>selection_state</c>) — <b>presentation, NOT a rule</b>:
        /// it is deliberately written to <see cref="ModeSelection"/> and not to <see cref="ModeRuntime"/>.
        /// <para>
        /// ⚠️ <c>ModeRuntime.Apply</c> is NOT called from here: the selection does not change the
        /// running match, and if it were called the HUD and loadout of a player waiting in the lobby
        /// would jump to the selected mode before the match starts (the client-side counterpart of the
        /// decision where the server deliberately keeps <c>modeId</c> as <c>"lobby"</c> during
        /// staging, §10.7).
        /// </para>
        /// </summary>
        private static void HandleSelectionState(SelectionStateMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            ModeSelection.Apply(msg.modeId, msg.teamMode);
        }

        /// <summary>
        /// The rule shape changed MID-match (§5.3 <c>rules_update</c>) — today its only cause is the
        /// operator's friendly-fire switch (§5.2).
        /// <para>
        /// ⚠️ Unlike <c>HandleSelectionState</c> this one <b>is applied</b>: a selection describes a
        /// future match, whereas this is the rule of the running match. Authority is still on the
        /// server — the client only refreshes its mirror; the damage decision is already the server's
        /// (§10.3).
        /// </para>
        /// </summary>
        private static void HandleRulesUpdate(RulesUpdateMsg msg)
        {
            if (msg?.rules == null)
            {
                return;
            }

            ModeRuntime.Apply(msg.modeId, msg.rules);
        }

        private static void HandleDisconnected()
        {
            ModeRuntime.Reset();
            ModeSelection.Reset();
        }
    }
}
