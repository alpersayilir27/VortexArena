using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;

namespace VortexArena.App.Admin
{
    /// <summary>
    /// Brightens the spectator view: a runtime global <see cref="Volume"/> (exposure up, vignette
    /// off) plus per-scene fog/ambient relaxation.
    ///
    /// <para><b>Why:</b> arena lighting is authored for the VR player standing inside it — linear
    /// fog ends a few metres out and ambient is near black, so the top-down camera (≈20 m up) sees
    /// mostly fog colour. The scene assets are NOT re-authored: that would change what the players
    /// see too.</para>
    ///
    /// <para>⚠️ Admin only: <see cref="AdminSpectator"/> adds this component after the role check,
    /// and the component destroys itself if the role is not <c>admin</c>. Scene lighting is global
    /// state, so touching it in the player build would dim/brighten the real game.</para>
    ///
    /// <para>Scene lighting (<c>RenderSettings</c>) belongs to the ACTIVE scene and arenas are
    /// loaded/unloaded at runtime, so the originals are remembered per scene and every pass is
    /// computed FROM the originals — re-adoption of the same scene never compounds.</para>
    ///
    /// <para>⚠️ The amount is PER MAP (<see cref="MapDefinition"/>: admin exposure/fog/ambient).
    /// A map that leaves them neutral is shown exactly as authored — there is no global default,
    /// otherwise an already bright scene (lobby) would wash out.</para>
    /// </summary>
    public class AdminViewTuning : MonoBehaviour
    {
        /// <summary>Above every profile authored in the project, so the override always wins.</summary>
        private const float VolumePriority = 1000f;

        private Volume _volume;
        private VolumeProfile _profile;
        private ColorAdjustments _exposure;
        private Vignette _vignette;

        /// <summary>Untouched lighting per loaded scene, keyed by <c>Scene.handle</c>.</summary>
        private readonly Dictionary<int, SceneLighting> _originals = new Dictionary<int, SceneLighting>();

        private readonly List<int> _staleKeys = new List<int>();

        private struct SceneLighting
        {
            public bool Fog;
            public FogMode Mode;
            public float Start;
            public float End;
            public float Density;
            public float AmbientIntensity;
        }

        private void Awake()
        {
            // Defensive twin of the spectator's own gate: this writes global lighting state.
            if (!AppSession.RoleResolved || AppSession.Role != AppSession.RoleAdmin)
            {
                Destroy(this);
                return;
            }

            CreateVolume();
            SceneManager.sceneLoaded += HandleSceneLoaded;
            SceneManager.activeSceneChanged += HandleActiveSceneChanged;
            ApplyToActiveScene();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;

            RestoreActiveScene();

            if (_volume != null)
            {
                Destroy(_volume);
                _volume = null;
            }

            // Runtime-created asset: it is not owned by the project, nothing else will free it.
            if (_profile != null)
            {
                Destroy(_profile);
                _profile = null;
                _exposure = null;
                _vignette = null;
            }
        }

        // ------------------------------------------------------------------ volume

        /// <summary>
        /// Builds the global volume on the spectator root. ⚠️ The GameObject stays on the Default
        /// layer: the camera's <c>volumeLayerMask</c> only samples layers it is set to.
        /// </summary>
        private void CreateVolume()
        {
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "AdminViewTuning";
            _profile.hideFlags = HideFlags.HideAndDontSave;

            _exposure = _profile.Add<ColorAdjustments>(true);
            _exposure.postExposure.overrideState = false;
            _exposure.postExposure.value = 0f;

            // The default profile darkens the frame edges; on a 2D top-down map that hides players.
            _vignette = _profile.Add<Vignette>(true);
            _vignette.intensity.overrideState = false;
            _vignette.intensity.value = 0f;

            _volume = gameObject.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = VolumePriority;
            _volume.weight = 0f;
            _volume.sharedProfile = _profile;
        }

        // ------------------------------------------------------------------ scenes

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplyToActiveScene();
        }

        private void HandleActiveSceneChanged(Scene previous, Scene next)
        {
            ApplyToActiveScene();
        }

        /// <summary>Applies the relaxed lighting to the active scene, always from its originals.</summary>
        private void ApplyToActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                return;
            }

            PruneUnloaded();

            if (!_originals.TryGetValue(scene.handle, out SceneLighting original))
            {
                original = Capture();
                _originals[scene.handle] = original;
            }

            MapDefinition map = AdminContent.Catalog != null
                ? AdminContent.Catalog.FindMap(scene.name)
                : null;

            float exposure = map != null ? map.AdminPostExposure : 0f;
            float fogScale = map != null ? map.AdminFogScale : 1f;
            float ambientScale = map != null ? map.AdminAmbientScale : 1f;
            bool tuned = !Mathf.Approximately(exposure, 0f) ||
                         !Mathf.Approximately(fogScale, 1f) ||
                         !Mathf.Approximately(ambientScale, 1f);

            // Untuned map = shown as authored; the volume must not darken/brighten anything.
            _volume.weight = tuned ? 1f : 0f;
            _exposure.postExposure.overrideState = !Mathf.Approximately(exposure, 0f);
            _exposure.postExposure.value = exposure;
            _vignette.intensity.overrideState = tuned;

            float factor = fogScale > 0f ? fogScale : 1f;

            if (original.Fog)
            {
                if (original.Mode == FogMode.Linear)
                {
                    RenderSettings.fogStartDistance = original.Start * factor;
                    RenderSettings.fogEndDistance = original.End * factor;
                }
                else
                {
                    RenderSettings.fogDensity = original.Density / factor;
                }
            }

            RenderSettings.ambientIntensity = original.AmbientIntensity * Mathf.Max(0f, ambientScale);
        }

        private void RestoreActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !_originals.TryGetValue(scene.handle, out SceneLighting original))
            {
                return;
            }

            // Only the ACTIVE scene can be written back; an unloaded one took our values with it.
            RenderSettings.fogStartDistance = original.Start;
            RenderSettings.fogEndDistance = original.End;
            RenderSettings.fogDensity = original.Density;
            RenderSettings.ambientIntensity = original.AmbientIntensity;
            _originals.Remove(scene.handle);
        }

        private static SceneLighting Capture()
        {
            return new SceneLighting
            {
                Fog = RenderSettings.fog,
                Mode = RenderSettings.fogMode,
                Start = RenderSettings.fogStartDistance,
                End = RenderSettings.fogEndDistance,
                Density = RenderSettings.fogDensity,
                AmbientIntensity = RenderSettings.ambientIntensity,
            };
        }

        /// <summary>Drops entries of unloaded scenes; their handles can be reused by a new load.</summary>
        private void PruneUnloaded()
        {
            _staleKeys.Clear();
            foreach (KeyValuePair<int, SceneLighting> entry in _originals)
            {
                bool alive = false;
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    if (SceneManager.GetSceneAt(i).handle == entry.Key)
                    {
                        alive = true;
                        break;
                    }
                }

                if (!alive)
                {
                    _staleKeys.Add(entry.Key);
                }
            }

            for (int i = 0; i < _staleKeys.Count; i++)
            {
                _originals.Remove(_staleKeys[i]);
            }
        }
    }
}
