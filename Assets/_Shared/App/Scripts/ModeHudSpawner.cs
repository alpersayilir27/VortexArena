using UnityEngine;
using VortexArena.Core;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// Instantiates the active mode's HUD prefab (GameCatalog → ModeDefinition.HudPrefab) in the
    /// arena scene. ⚠️ App does NOT reference the mode assemblies — the prefab is carried as a plain
    /// GameObject. Player role only.
    /// <para>
    /// ⚠️ <b>The HUD is bound to the modId, not to the scene lifetime:</b> a mode can change without
    /// a scene change (new match on the same map, or a match starting in an already staged arena).
    /// Keeping the old mode's HUD would leave the new mode's components unspawned and lock the
    /// match flow silently (Docs/Sistem-Ozeti.md §7 "a mode change is not a scene change").
    /// </para>
    /// </summary>
    public class ModeHudSpawner : MonoBehaviour
    {
        [Header("Katalog")]
        [SerializeField] private GameCatalog catalog;

        [Tooltip("HUD'ın altına örnekleneceği kök; boşsa Camera.main transformu kullanılır.")]
        [SerializeField] private Transform hudParent;

        private GameObject _hudInstance;

        /// <summary>modId of the spawned HUD — catches a mode change without a scene change.</summary>
        private string _spawnedModeId = "";

        private void OnEnable()
        {
            NetEvents.OnLoadMatch += HandleLoadMatch;
            NetEvents.OnConnected += HandleConnected;
        }

        private void OnDisable()
        {
            NetEvents.OnLoadMatch -= HandleLoadMatch;
            NetEvents.OnConnected -= HandleConnected;
        }

        private void Start()
        {
            // load_match normally arrives BEFORE the scene loads; SceneRouter keeps the last modId.
            string modeId = SceneRouter.Instance != null ? SceneRouter.Instance.LastModeId : "";
            SpawnHud(modeId);
        }

        // ------------------------------------------------------------ event handlers

        private void HandleLoadMatch(LoadMatchMsg msg)
        {
            if (msg != null)
            {
                SpawnHud(msg.modeId);
            }
        }

        /// <summary>Late join: take the mode from the match info inside welcome.</summary>
        private void HandleConnected(WelcomeMsg msg)
        {
            if (msg != null && msg.match != null)
            {
                SpawnHud(msg.match.modeId);
            }
        }

        // ------------------------------------------------------------------- setup

        private void SpawnHud(string modeId)
        {
            if (AppSession.Role != AppSession.RolePlayer)
            {
                return; // admin spectator: the mode HUD belongs to the player, AdminHud is drawn separately
            }

            if (catalog == null)
            {
                Debug.LogWarning("[ModeHudSpawner] GameCatalog atanmadı; mod HUD'ı örneklenemiyor.");
                return;
            }

            ModeDefinition mode = ResolveMode(modeId);
            if (mode == null)
            {
                return;
            }

            if (_hudInstance != null)
            {
                if (mode.ModeId == _spawnedModeId)
                {
                    return; // same mode — the HUD is already correct
                }

                // Mode changed without a scene change (see class doc): drop the old HUD.
                Destroy(_hudInstance);
                _hudInstance = null;
                _spawnedModeId = "";
            }

            GameObject prefab = mode.HudPrefab;
            if (prefab == null)
            {
                Debug.LogWarning($"[ModeHudSpawner] '{mode.ModeId}' modunun HudPrefab'ı yok; HUD örneklenmedi.");
                return;
            }

            Transform parent = ResolveParent();
            _hudInstance = Instantiate(prefab, parent, false);
            _hudInstance.name = prefab.name;
            _spawnedModeId = mode.ModeId;

            // Local pose comes from the prefab (a fixed spot under the camera in VR).
            _hudInstance.transform.localPosition = prefab.transform.localPosition;
            _hudInstance.transform.localRotation = prefab.transform.localRotation;
        }

        /// <summary>
        /// Resolves the modId from the catalog; <c>null</c> means no HUD is spawned.
        /// <para>
        /// ⚠️ In a connected session an empty modId means "no match" (§10.7) and no HUD is correct.
        /// The fallback to the catalog's FIRST mode belongs only to the SERVER-LESS editor sandbox:
        /// while connected, the wrong mode's HUD looks error-free but behaves wrongly.
        /// </para>
        /// </summary>
        private ModeDefinition ResolveMode(string modeId)
        {
            if (!string.IsNullOrEmpty(modeId))
            {
                ModeDefinition found = catalog.FindMode(modeId);
                if (found != null)
                {
                    return found;
                }

                Debug.LogWarning($"[ModeHudSpawner] '{modeId}' modu katalogda yok; HUD örneklenmedi.");
                return null;
            }

            ArenaClient client = ArenaClient.Instance;
            bool sandbox = Application.isEditor && (client == null || !client.IsConnected);
            if (!sandbox)
            {
                return null; // no match — no HUD is correct (rationale above)
            }

            ModeDefinition[] modes = catalog.Modes;
            if (modes == null)
            {
                return null;
            }

            for (int i = 0; i < modes.Length; i++)
            {
                if (modes[i] != null)
                {
                    return modes[i];
                }
            }

            return null;
        }

        private Transform ResolveParent()
        {
            if (hudParent != null)
            {
                return hudParent;
            }

            Camera cam = Camera.main;
            return cam != null ? cam.transform : transform;
        }
    }
}
