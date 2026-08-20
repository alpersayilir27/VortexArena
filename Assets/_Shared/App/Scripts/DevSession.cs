#if UNITY_EDITOR
using System.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.App.Admin;
using VortexArena.Core;
using VortexArena.Core.Combat;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// EDITOR ONLY — the runtime side of the developer selection: applies the role/address that the
    /// `Tools &gt; VortexArena &gt; Development &gt; Dev` window wrote to `EditorPrefs` when Play starts.
    ///
    /// <para><b>The "selection" layer of the two-layer config.</b> The named target list is
    /// committed (`dev-targets.json` → `DevTargets`), but WHICH target is selected is personal and
    /// lives in `EditorPrefs`: a committed IP choice means the team overwrites each other's settings
    /// and `git status` stays dirty. ⚠️ For the same reason these settings are NOT [SerializeField]s
    /// on `AppBoot` — that would dirty Boot.unity on every role change.</para>
    ///
    /// <para><b>Two jobs:</b></para>
    /// <list type="number">
    /// <item>`BeforeSceneLoad`: writes role + server address into <see cref="AppSession"/>
    ///   (`RoleResolved = true`, so <see cref="AppBoot"/> and the shell controllers keep their
    ///   hands off).</item>
    /// <item>`AfterSceneLoad`: when Play started directly from an ARENA scene, **connects** to the
    ///   server and nothing more — match data (mode, team, phase, time) comes only from the server.
    ///   <b>Sandbox mode does not connect</b> and applies rules locally instead.</item>
    /// </list>
    ///
    /// <para><b>Why connecting happens here:</b> `Connect(...)` normally comes from the shell
    /// scene's controller (`LobbyController`), which arena scenes do not have — so playing straight
    /// from an arena would leave nobody connecting: no health/score/phase, and `CanFire` never opens
    /// because the server never says `Live`.</para>
    ///
    /// <para><b>Team/mode/phase come only from the server.</b> On connect, the `welcome.match`
    /// late-join sync runs (`SceneRouter.HandleConnected`) and the server assigns the team. With no
    /// match running the client receives no match data — expected; an admin must start one. ⚠️ This
    /// class never fabricates server messages.</para>
    ///
    /// <para><b>Sandbox mode (server-less):</b> skips server + admin + calibration for testing LOCAL
    /// things (weapon pose, muzzle, audio). Nothing connects — the calibration gate is open while
    /// never connected (`CalibrationState.IsCalibrated`) and `ArenaCombat` is a silent no-op without
    /// a channel. One <see cref="ModeRuntime.Apply"/> call opens the two remaining gates:
    /// `fireWhilePaused` (trigger without phase `playing`) and `modeId` (where the loadout is read
    /// from — without it no weapon appears).</para>
    ///
    /// <para>⚠️ Sandbox is <b>NOT a match-rule test</b>: team/score/respawn fields stay at
    /// <see cref="ModeRulesInfo"/> defaults (TDM) — a mode's real rules come from the server and the
    /// server wins on divergence (§10.5). Only `modeId` matters here.</para>
    ///
    /// <para><b>Weapons are granted SEQUENTIALLY</b> (<c>WeaponGranter.SequentialGrant</c>): the
    /// next loadout entry per grip press, because the test is "go through all of them". ⚠️ The
    /// production random grant is unchanged.</para>
    ///
    /// <para><b>Turning it off:</b> with the window's "Dev enjeksiyonu" toggle off this class does
    /// nothing and the production path runs verbatim (for trying beacon discovery). An empty address
    /// field also writes no address and hands over to the discovery chain.</para>
    ///
    /// The whole file is inside <c>#if UNITY_EDITOR</c> → it never enters a build.
    /// </summary>
    public class DevSession : MonoBehaviour
    {
        // ------------------------------------------------------------ EditorPrefs keys
        // Kept in one place; the dev window (VortexArena.App.Editor) uses the same constants so the
        // key names do not diverge across the two sides.

        private const string Prefix = "VortexArena.Dev.";

        public const string KeyEnabled = Prefix + "Enabled";
        public const string KeyRole = Prefix + "Role";
        public const string KeyTargetName = Prefix + "TargetName";
        public const string KeyIp = Prefix + "Ip";
        public const string KeyPort = Prefix + "Port";
        public const string KeyStartFromBoot = Prefix + "StartFromBoot";
        public const string KeySandbox = Prefix + "Sandbox";
        public const string KeySandboxModeId = Prefix + "SandboxModeId";

        /// <summary>
        /// Sandbox's weapon source — the <c>ModeRulesInfo.weaponSource</c> wire value.
        /// <para>
        /// Fixed and not selectable: ⚠️ <b>the frame path is not used in sandbox</b>. Frames mean
        /// picking a weapon off a stand from a distance, while the point here is to get a weapon in
        /// hand immediately and walk the whole loadout (<c>WeaponGranter.SequentialGrant</c>).
        /// </para>
        /// </summary>
        private const string WeaponSourceGrant = "random";

        // --------------------------------------------------------------- selection

        /// <summary>Is dev injection on? While off, the production path runs verbatim.</summary>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(KeyEnabled, true);
            set => EditorPrefs.SetBool(KeyEnabled, value);
        }

        /// <summary>"player" | "admin".</summary>
        public static string Role
        {
            get => EditorPrefs.GetString(KeyRole, AppSession.RoleAdmin) == AppSession.RolePlayer
                ? AppSession.RolePlayer
                : AppSession.RoleAdmin;
            set => EditorPrefs.SetString(KeyRole, value);
        }

        /// <summary>Name of the target selected in the dev window (UI state only).</summary>
        public static string TargetName
        {
            get => EditorPrefs.GetString(KeyTargetName, "");
            set => EditorPrefs.SetString(KeyTargetName, value);
        }

        /// <summary>Server IP. <b>Empty = write no address</b>, let the discovery chain take over.</summary>
        public static string Ip
        {
            get => EditorPrefs.GetString(KeyIp, "127.0.0.1");
            set => EditorPrefs.SetString(KeyIp, value ?? "");
        }

        public static int Port
        {
            get => EditorPrefs.GetInt(KeyPort, ArenaProtocol.CONTROL_PORT);
            set => EditorPrefs.SetInt(KeyPort, value);
        }

        /// <summary>true = Play always runs from Boot; false = from the open scene.</summary>
        public static bool StartFromBoot
        {
            get => EditorPrefs.GetBool(KeyStartFromBoot, true);
            set => EditorPrefs.SetBool(KeyStartFromBoot, value);
        }

        /// <summary>
        /// Server-less sandbox mode: no connection, rules applied locally. ⚠️ Only meaningful when
        /// playing <b>from the open scene</b> — from Boot the shell scene drives the flow and
        /// <c>LobbyController</c> tries to connect.
        /// </summary>
        public static bool Sandbox
        {
            get => EditorPrefs.GetBool(KeySandbox, false);
            set => EditorPrefs.SetBool(KeySandbox, value);
        }

        /// <summary>modId applied in sandbox — <b>the loadout is found through it</b>
        /// (<c>GameCatalog.FindMode</c>); empty means no weapons.</summary>
        public static string SandboxModeId
        {
            get => EditorPrefs.GetString(KeySandboxModeId, "");
            set => EditorPrefs.SetString(KeySandboxModeId, value ?? "");
        }

        /// <summary>One-line summary of the selection (window header + console line).</summary>
        public static string Summary
        {
            get
            {
                string start = StartFromBoot ? "Boot'tan" : "açık sahneden";
                if (Sandbox)
                {
                    string mode = string.IsNullOrEmpty(SandboxModeId) ? "(mod seçilmedi)" : SandboxModeId;
                    return $"{Role} · SANDBOX (sunucusuz) · {mode} · silah: sırayla · {start}";
                }

                string address = HasAddress ? $"{Ip}:{Port}" : "keşif (adres yok)";
                return $"{Role} · {address} · {start}";
            }
        }

        /// <summary>Was an address given? An empty IP means "use the discovery chain".</summary>
        public static bool HasAddress => !string.IsNullOrWhiteSpace(Ip) && Port > 0;

        // ------------------------------------------------------- 1) role + address

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ApplySelection()
        {
            if (!Enabled)
            {
                return;
            }

            AppSession.Role = Role;
            AppSession.RoleResolved = true;

#if VORTEX_MPPM
            // Multiplayer Play Mode: EditorPrefs is MACHINE-wide on Windows, so the virtual player
            // process reads the main editor's role and both windows end up in the same role. Hence
            // the "player"/"admin" MPPM TAG overrides the selection; an untagged process (the main
            // editor) keeps the EditorPrefs choice.
            string tagRole = RoleFromMppmTags();
            if (tagRole != null && tagRole != AppSession.Role)
            {
                AppSession.Role = tagRole;
                Debug.Log($"[DevSession] MPPM tag'i rolü ezdi → '{tagRole}' " +
                          $"(EditorPrefs seçimi '{Role}' ana editörde geçerli kalır).");
            }
#endif

            // Admin role: release XR so the HMD stays with the player process on the same PC.
            AdminXrRelease.Apply();

            // Sandbox = server-less. ⚠️ The address is CLEARED so no shell controller or discovery
            // chain connects by accident: one successful connection latches `_hasEverConnected` and
            // closes the calibration gate (CalibrationState.IsCalibrated), silently losing
            // sandbox's "fire without calibration".
            if (Sandbox)
            {
                AppSession.ServerIp = "";
                AppSession.ServerPort = 0;
                Debug.Log($"[DevSession] Dev seçimi uygulandı → {Summary}. " +
                          "Değiştirmek için: Tools > VortexArena > Development > Dev (rol: Ctrl+Alt+R).");
                return;
            }

            if (HasAddress)
            {
                AppSession.ServerIp = Ip.Trim();
                AppSession.ServerPort = Port;
            }
            else
            {
                AppSession.ServerIp = "";
                AppSession.ServerPort = 0;
            }

            Debug.Log($"[DevSession] Dev seçimi uygulandı → {Summary}. " +
                      "Değiştirmek için: Tools > VortexArena > Development > Dev (rol: Ctrl+Alt+R).");
        }

        // ----------------------------- 2) Play from an arena scene: connect to the server

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ScheduleArenaSceneSetup()
        {
            if (!Enabled)
            {
                return;
            }

            if (StartFromBoot)
            {
                // From Boot, the flow is driven by Boot + the shell controllers.
                if (Sandbox)
                {
                    Debug.LogWarning(
                        "[DevSession] Sandbox kipi AÇIK ama başlangıç \"Boot'tan\" — sandbox " +
                        "uygulanmadı. Dev penceresinde başlangıcı \"Açık sahneden\" yapın.");
                }

                return;
            }

            if (IsShellScene(SceneManager.GetActiveScene().name))
            {
                // Lobby connects itself (in every role) and Boot routes. Both are meaningless in
                // sandbox, which is about playing from a playable scene (a venue lobby is an arena
                // box and does not hit this check).
                if (Sandbox)
                {
                    Debug.LogWarning(
                        "[DevSession] Sandbox kipi kabuk sahnesinde (Boot/Lobby) uygulanmaz — " +
                        "test edeceğiniz arena ya da mekan lobisi sahnesini açıp Play'e basın.");
                }

                return;
            }

            // ⚠️ Waiting one frame is MANDATORY: `ArenaClient` and `SceneRouter` also spawn in
            // AfterSceneLoad and the order of the three hooks is UNDEFINED. A MonoBehaviour also
            // lets the scene's Start()s finish.
            var go = new GameObject("[DevArenaSceneSetup]");
            DontDestroyOnLoad(go);
            go.AddComponent<DevSession>();
        }

        /// <summary>Is this a shell scene (one with its own connect/routing flow)?</summary>
        private static bool IsShellScene(string sceneName)
        {
            return sceneName == AppSession.SceneBoot ||
                   sceneName == AppSession.SceneLobby;
        }

        private IEnumerator Start()
        {
            yield return null; // let all singletons and scene subscribers (OnEnable/Start) settle

            if (Sandbox)
            {
                ApplySandboxRules();
            }
            else
            {
                ConnectFromArenaScene();
            }

            Destroy(gameObject);
        }

        /// <summary>
        /// Server-less sandbox: writes the rule state LOCALLY and connects to nothing.
        /// <para>
        /// ⚠️ The one-frame wait matters here too: <c>WeaponGranter</c> bootstraps in
        /// <c>AfterSceneLoad</c> while <c>modeId</c> is still empty (no loadout). A frame later
        /// <see cref="ModeRuntime.Apply"/> fires <c>Changed</c> and the rules are applied a second
        /// time, now with the mode known.
        /// </para>
        /// </summary>
        private void ApplySandboxRules()
        {
            string modeId = SandboxModeId;
            if (string.IsNullOrEmpty(modeId))
            {
                Debug.LogWarning(
                    "[DevSession] Sandbox kipinde mod seçilmemiş — silah loadout'u moddan " +
                    "okunduğu için elde silah belirmez. Dev penceresinden " +
                    "bir mod seçin.");
                return;
            }

            // Only two fields; the rest stay at ModeRulesInfo defaults (TDM) — sandbox is not a
            // match-rule test (see the class doc).
            var rules = new ModeRulesInfo
            {
                weaponSource = WeaponSourceGrant,

                // Without a server the phase stays 'paused'; this is the only gate that opens the
                // trigger (§10.5). There is still no damage — hit_report always requires 'playing'
                // (§10.3).
                fireWhilePaused = true
            };

            // Sequential instead of random: seeing every weapon one by one is the test itself. The
            // flag exists only in the editor and is written only here — production stays random.
            WeaponGranter.SequentialGrant = true;

            ModeRuntime.Apply(modeId, rules);

            Debug.Log(
                $"[DevSession] SANDBOX (sunucusuz): mod '{modeId}', silahlar loadout'tan SIRAYLA " +
                "(grip'e her basışta bir sonraki), serbest atış açık. Sunucuya bağlanılmadı — " +
                "hasar/skor/faz yoktur, kalibrasyon istenmez.");
        }

        /// <summary>
        /// Takes over connecting in an arena scene (no shell controller here — see the class doc).
        /// Without an address it does not connect but logs why: an arena scene has no UI for
        /// entering one.
        /// </summary>
        private void ConnectFromArenaScene()
        {
            if (ArenaClient.Instance == null)
            {
                Debug.LogWarning("[DevSession] ArenaClient tekili yok; bağlanılamadı.");
                return;
            }

            if (ArenaClient.Instance.State != ArenaConnectionState.Disconnected)
            {
                return; // a scene change during Play may have connected already
            }

            if (!HasAddress)
            {
                Debug.LogWarning(
                    "[DevSession] Adres yok (hedef 'keşif' kipinde) — arena sahnesinden " +
                    "bağlanılamaz, bu sahnede adres girecek arayüz yok. Dev penceresinde somut " +
                    "bir hedef seçin ya da Boot'tan başlatın.");
                return;
            }

            // AppSession.Role, not the EditorPrefs Role: an MPPM tag may have overridden it.
            string role = AppSession.Role;
            Debug.Log($"[DevSession] Arena sahnesinden bağlanılıyor: {Ip}:{Port} ({role}).");
            ArenaClient.Instance.Connect(Ip.Trim(), Port, role);
        }

#if VORTEX_MPPM
        /// <summary>
        /// Role from the MPPM tags: a process tagged "player" or "admin" takes that role
        /// (case-insensitive). null without a tag — the EditorPrefs choice stays.
        /// </summary>
        private static string RoleFromMppmTags()
        {
            foreach (string tag in Unity.Multiplayer.PlayMode.CurrentPlayer.ReadOnlyTags())
            {
                string normalized = tag != null ? tag.Trim().ToLowerInvariant() : null;
                if (normalized == AppSession.RolePlayer || normalized == AppSession.RoleAdmin)
                {
                    return normalized;
                }
            }

            return null;
        }
#endif
    }
}
#endif
