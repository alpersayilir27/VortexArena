using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Combat;

namespace VortexArena.Core.Arena
{
    /// <summary>The ONLY place deciding whether <see cref="BaseZone"/>s are visible/enabled: the
    /// red/blue strips only mean something in a <b>team</b> mode.</summary>
    /// <remarks>
    /// Team-less (FFA): hidden — reviving there means standing still, so a colored strip would teach
    /// a rule that does not exist.
    /// <para>
    /// The gate is <see cref="ModeSelection"/> (§5.3 <c>selection_state</c>), i.e. the SELECTED
    /// mode, not the running one: staging an arena moves everyone while the lobby rules still run
    /// (§10.7). Falls back to <see cref="ModeRuntime"/> when the server reports no selection.
    /// </para>
    /// <para>
    /// X-ray: while the local player is DEAD, a second material slot (<c>M_BaseZoneXRay</c>,
    /// <c>ZTest Greater</c>) is added to their OWN team's strip so the revive point is visible
    /// through decor. Never for a living player, never for the enemy base, never for
    /// <see cref="Team.Neutral"/>. Team color is read from the strip's own material — no second
    /// color definition.
    /// </para>
    /// <para>
    /// ⚠️ Unrelated to the weapon source: this used to ride on <c>WeaponGranter</c>'s
    /// <c>weaponSource</c> gate, which silently hid the bases once the lobby weapon became random.
    /// </para>
    /// <para>
    /// ⚠️ Only restores what it disabled ITSELF (x-ray likewise) — <c>AdminSpectator</c> disables
    /// the same components, and an unconditional restore would undo its decision.
    /// </para>
    /// <para>
    /// Self-bootstrapping singleton (<c>WeaponGranter</c> pattern): a scene component would add a
    /// manual setup step to every new arena.
    /// </para>
    /// </remarks>
    public class BaseZoneVisibility : MonoBehaviour
    {
        /// <summary><c>Resources</c> path of the x-ray material. ⚠️ Must stay under
        /// <c>Resources/</c>: no scene references it, so otherwise the shader is stripped from the
        /// build and the strip draws pink on Quest.</summary>
        private const string XRayMaterialResource = "M_BaseZoneXRay";

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private static BaseZoneVisibility _instance;

        /// <summary>Zones disabled BY THIS component — kept apart so another owner's decision is not
        /// undone. Refs die with the scene and the list is rebuilt.</summary>
        private readonly List<BaseZone> _disabledZones = new List<BaseZone>();

        /// <summary>Strip visual objects hidden BY THIS component.</summary>
        private readonly List<GameObject> _hiddenObjects = new List<GameObject>();

        /// <summary>Renderers this component added an x-ray slot to, and the material instances it
        /// gave them — the record of who put what, used when removing.</summary>
        private readonly List<Renderer> _xrayRenderers = new List<Renderer>();

        private readonly List<Material> _xrayMaterials = new List<Material>();

        /// <summary>Scratch buffer for material array read/write — avoids per-frame garbage.</summary>
        private readonly List<Material> _materialScratch = new List<Material>();

        private Material _xrayShared;
        private bool _xrayLoadFailed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("[BaseZoneVisibility]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<BaseZoneVisibility>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // Persistent singleton: Awake/OnDestroy rather than OnEnable/OnDisable so events are not
            // missed if the object is deactivated (PlayerCombatState pattern).
            SceneManager.sceneLoaded += HandleSceneLoaded;
            ModeSelection.Changed += Apply;
            ModeRuntime.Changed += Apply;
            PlayerCombatState.LocalTeamChanged += HandleLocalTeamChanged;
            PlayerCombatState.LocalAliveChanged += HandleLocalAliveChanged;
            Apply();
        }

        private void OnDestroy()
        {
            if (_instance != this)
            {
                return;
            }

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            ModeSelection.Changed -= Apply;
            ModeRuntime.Changed -= Apply;
            PlayerCombatState.LocalTeamChanged -= HandleLocalTeamChanged;
            PlayerCombatState.LocalAliveChanged -= HandleLocalAliveChanged;

            ClearXRay();

            _instance = null;
        }

        private void HandleLocalTeamChanged(Team team)
        {
            // Only the x-ray cares, but Apply is idempotent — a narrow path would be a second
            // application point.
            Apply();
        }

        private void HandleLocalAliveChanged(bool alive)
        {
            // Same rationale as HandleLocalTeamChanged: routed through Apply(), no narrow path.
            Apply();
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // New scene = new zones; the old ones died with the scene, so the restore lists are
            // cleared instead of holding dead refs.
            _disabledZones.Clear();
            _hiddenObjects.Clear();
            Apply();
        }

        /// <summary>Single application point. Zones are searched in the scene: this component can be
        /// born before a scene loads and <c>ModeSelection</c> changes independently of it.</summary>
        private void Apply()
        {
            // Always removed first: mode or team may have changed and slots must not pile up.
            ClearXRay();

            if (ShouldShow())
            {
                Restore();
                ApplyXRay();
                return;
            }

            Hide();
        }

        /// <summary>Selected mode if any, otherwise the running rules. "Unknown" and "team-less" are
        /// different states — the rules take over so an old server keeps today's behaviour.</summary>
        private static bool ShouldShow()
        {
            return ModeSelection.HasValue ? !ModeSelection.IsTeamless : !ModeRuntime.IsTeamless;
        }

        private void Hide()
        {
            BaseZone[] zones = FindObjectsByType<BaseZone>(FindObjectsSortMode.None);
            for (int i = 0; i < zones.Length; i++)
            {
                BaseZone zone = zones[i];
                if (zone == null)
                {
                    continue;
                }

                // ⚠️ The component is disabled, NOT the GameObject: PlayerCombatState reads a
                // disabled component as "no open base", while deactivating the object would take
                // EVERYTHING under it down and blur what Restore should bring back (the strip is
                // HideStrip's job).
                if (zone.enabled)
                {
                    zone.enabled = false;
                    _disabledZones.Add(zone);
                }

                HideStrip(zone);
            }
        }

        /// <summary>The zone's strip visual: direct children carrying a Renderer.</summary>
        private void HideStrip(BaseZone zone)
        {
            Transform root = zone.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (!IsStripChild(child) || !child.gameObject.activeSelf)
                {
                    continue;
                }

                child.gameObject.SetActive(false);
                _hiddenObjects.Add(child.gameObject);
            }
        }

        /// <summary>Is this direct child a "strip visual". <see cref="HideStrip"/> and the x-ray must
        /// look at the same set — two selection rules would silently drift.</summary>
        private static bool IsStripChild(Transform child)
        {
            return child.GetComponentInChildren<Renderer>(true) != null;
        }

        /// <summary>Re-enables only what this component disabled; dead refs are skipped.</summary>
        private void Restore()
        {
            for (int i = 0; i < _hiddenObjects.Count; i++)
            {
                if (_hiddenObjects[i] != null)
                {
                    _hiddenObjects[i].SetActive(true);
                }
            }

            for (int i = 0; i < _disabledZones.Count; i++)
            {
                if (_disabledZones[i] != null)
                {
                    _disabledZones[i].enabled = true;
                }
            }

            _hiddenObjects.Clear();
            _disabledZones.Clear();
        }

        // ------------------------------------------------------------------------- x-ray

        /// <summary>Adds the through-wall draw slot to the local player's own team strips; only
        /// meaningful while they are DEAD.</summary>
        private void ApplyXRay()
        {
            // ⚠️ Instance may be null — bootstrap order can put this before PlayerCombatState. Treat
            // as alive (its own initial value); LocalAliveChanged re-runs Apply() once it exists.
            if (PlayerCombatState.Instance == null || PlayerCombatState.Instance.IsAlive)
            {
                return;
            }

            Team local = ArenaCombat.LocalTeam;
            if (local == Team.Neutral)
            {
                // No team yet (unassigned / admin spectator): whose strip it is cannot be told.
                return;
            }

            Material shared = ResolveXRayMaterial();
            if (shared == null)
            {
                return;
            }

            BaseZone[] zones = FindObjectsByType<BaseZone>(FindObjectsSortMode.None);
            for (int i = 0; i < zones.Length; i++)
            {
                BaseZone zone = zones[i];
                if (zone == null || zone.Team != local)
                {
                    continue;
                }

                Transform root = zone.transform;
                for (int c = 0; c < root.childCount; c++)
                {
                    Transform child = root.GetChild(c);
                    if (!IsStripChild(child))
                    {
                        continue;
                    }

                    Renderer[] renderers = child.GetComponentsInChildren<Renderer>(true);
                    for (int r = 0; r < renderers.Length; r++)
                    {
                        AddXRaySlot(renderers[r], shared);
                    }
                }
            }
        }

        /// <summary>Adds a second material to the renderer: the same mesh again with inverted depth
        /// test. ⚠️ The <c>renderer.materials</c> getter is NOT used — it would clone the existing
        /// team material and cut its link to the shared one.</summary>
        private void AddXRaySlot(Renderer renderer, Material shared)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.GetSharedMaterials(_materialScratch);

            // Skip if a hand-placed or leftover slot is already there (idempotent).
            for (int i = 0; i < _materialScratch.Count; i++)
            {
                Material existing = _materialScratch[i];
                if (existing != null && existing.shader == shared.shader)
                {
                    return;
                }
            }

            var ghost = new Material(shared) { name = shared.name + " (runtime)" };
            CopyTeamColor(_materialScratch.Count > 0 ? _materialScratch[0] : null, ghost);

            _materialScratch.Add(ghost);
            renderer.sharedMaterials = _materialScratch.ToArray();

            _xrayRenderers.Add(renderer);
            _xrayMaterials.Add(ghost);
        }

        /// <summary>Team color is read from the strip's own material
        /// (<c>M_TeamRed</c>/<c>M_TeamBlue</c>) to keep one source; the x-ray material carries
        /// none.</summary>
        private static void CopyTeamColor(Material source, Material ghost)
        {
            if (source == null)
            {
                return;
            }

            if (source.HasProperty(BaseColorId))
            {
                ghost.SetColor(BaseColorId, source.GetColor(BaseColorId));
            }
            else if (source.HasProperty(ColorId))
            {
                ghost.SetColor(BaseColorId, source.GetColor(ColorId));
            }
        }

        /// <summary>Removes only the slots this component added and destroys the materials it made.
        /// Dead renderers (scene changed) are skipped; materials are still cleaned up.</summary>
        private void ClearXRay()
        {
            for (int i = 0; i < _xrayRenderers.Count; i++)
            {
                Renderer renderer = _xrayRenderers[i];
                if (renderer == null)
                {
                    continue;
                }

                renderer.GetSharedMaterials(_materialScratch);
                int removed = _materialScratch.RemoveAll(IsOwnXRayMaterial);
                if (removed > 0)
                {
                    renderer.sharedMaterials = _materialScratch.ToArray();
                }
            }

            for (int i = 0; i < _xrayMaterials.Count; i++)
            {
                if (_xrayMaterials[i] != null)
                {
                    Destroy(_xrayMaterials[i]);
                }
            }

            _xrayRenderers.Clear();
            _xrayMaterials.Clear();
        }

        private bool IsOwnXRayMaterial(Material material)
        {
            return material != null && _xrayMaterials.Contains(material);
        }

        /// <summary>The shared x-ray material; if missing, logs once and never retries (otherwise the
        /// same error repeats on every scene load).</summary>
        private Material ResolveXRayMaterial()
        {
            if (_xrayShared != null)
            {
                return _xrayShared;
            }

            if (_xrayLoadFailed)
            {
                return null;
            }

            _xrayShared = Resources.Load<Material>(XRayMaterialResource);
            if (_xrayShared == null)
            {
                _xrayLoadFailed = true;
                Debug.LogError($"BaseZoneVisibility: '{XRayMaterialResource}' Resources altında " +
                               "bulunamadı — taban şeridi duvar arkasından görünmeyecek.");
            }

            return _xrayShared;
        }
    }
}
