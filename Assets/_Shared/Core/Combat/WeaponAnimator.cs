using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Code-driven weapon part animation: bolt kick on fire, magazine out/in on reload. An Animator
    /// is DELIBERATELY not used — clip paths differ per model, durations must be normalized to the
    /// weapon's reload time, and this keeps the Animator cost on Quest at zero; driving two
    /// transforms with f(t) is enough.
    /// <para>Parts are found by name ("_Mag", "_Bolt"); when absent the component keeps running
    /// silently, so models outside the pack do not break. Update runs even when the weapon is
    /// released — Weapon completes the reload, here only f(t) is drawn.</para>
    /// </summary>
    public class WeaponAnimator : MonoBehaviour
    {
        /// <summary>End of the phase in which the magazine disappears, in ANIMATION progress
        /// (on a single-clip weapon).</summary>
        private const float MagOutPhaseEnd = 0.35f;

        /// <summary>Start of the phase in which the magazine comes back, in ANIMATION progress
        /// (single clip).</summary>
        private const float MagInPhaseStart = 0.7f;

        [SerializeField] private Weapon weapon;
        [SerializeField] private WeaponAudio weaponAudio;
        [Tooltip("Parçaların arandığı model kökü; boşsa weapon.ModelPivot kullanılır.")]
        [SerializeField] private Transform modelRoot;
        [Tooltip("Şarjörün reload'da aşağı kayma mesafesi (metre).")]
        [SerializeField] private float magDropMeters = 0.12f;
        [Tooltip("Şarjörün çıkarken öne eğilme açısı (derece).")]
        [SerializeField] private float magTiltDegrees = 25f;
        [Tooltip("Boltun atışta geriye kayma mesafesi (metre).")]
        [SerializeField] private float boltBackMeters = 0.03f;
        [Min(0f)]
        [Tooltip("Şarjör animasyonunun süresi (saniye). 0 = OTOMATİK: süre şarjör seslerinden " +
                 "türetilir (magOutClip + magInClip). >0 = elle verilen bu süre kullanılır. " +
                 "İki durumda da silahın reload süresini AŞAMAZ; kalan sürede şarjör dinlenme " +
                 "pozunda bekler.")]
        [SerializeField] private float manualReloadDuration;

        private Transform _mag;
        private Vector3 _magRestPosition;
        private Quaternion _magRestRotation;

        private Transform _bolt;
        private Vector3 _boltRestPosition;
        private Quaternion _boltRestRotation;

        /// <summary>Pulled to 1 on fire, decays to 0 over a duration tied to the fire rate.</summary>
        private float _boltKick;

        private bool _reloading;
        private float _reloadStartTime;
        private float _reloadDuration;
        private bool _magPulled;
        private bool _magInserted;

        /// <summary>Number of loading sounds to play in this reload (1 = single clip, the capacity
        /// on a shell-loaded weapon).</summary>
        private int _shellTotal;

        /// <summary>Number of loading sounds played so far in this reload.</summary>
        private int _shellsPlayed;

        /// <summary>Seconds between loading sounds; 0 = no repetition.</summary>
        private float _shellInterval;

        /// <summary>Duration of the magazine animation (seconds) — may be SHORTER than the reload
        /// duration.</summary>
        private float _animDuration;

        /// <summary>Moment, from the animation start, at which the magazine is fully down
        /// (seconds).</summary>
        private float _magOutEnd;

        /// <summary>Moment, from the animation start, at which the magazine starts coming back
        /// (seconds).</summary>
        private float _magInStart;

        private void Start()
        {
            // Deferred to Start so weapon.ModelPivot is guaranteed set up on the Weapon side.
            // OnEnable subscriptions are unaffected; the handlers are null-safe without parts.
            Transform root = modelRoot;
            if (root == null && weapon != null)
            {
                root = weapon.ModelPivot;
            }

            _mag = FindPart(root, "_Mag");
            if (_mag != null)
            {
                _magRestPosition = _mag.localPosition;
                _magRestRotation = _mag.localRotation;
            }

            _bolt = FindPart(root, "_Bolt");
            if (_bolt != null)
            {
                _boltRestPosition = _bolt.localPosition;
                _boltRestRotation = _bolt.localRotation;
            }
        }

        private void OnEnable()
        {
            if (weapon == null)
            {
                return;
            }

            weapon.Fired += HandleFired;
            weapon.ReloadStarted += HandleReloadStarted;
            weapon.ReloadCompleted += HandleReloadCompleted;
        }

        private void OnDisable()
        {
            if (weapon == null)
            {
                return;
            }

            weapon.Fired -= HandleFired;
            weapon.ReloadStarted -= HandleReloadStarted;
            weapon.ReloadCompleted -= HandleReloadCompleted;
        }

        private void Update()
        {
            TickBolt();
            TickReload();
        }

        // ------------------------------------------------------------ bolt kick

        private void HandleFired()
        {
            _boltKick = 1f;
        }

        private void TickBolt()
        {
            if (_bolt == null || _boltKick <= 0f)
            {
                return;
            }

            // The return duration is tied to the fire rate: on a fast weapon the bolt returns fast too.
            float duration = Mathf.Max(0.03f, weapon != null && weapon.Definition != null
                ? weapon.Definition.SecondsPerShot * 0.5f
                : 0.06f);

            _boltKick = Mathf.Max(0f, _boltKick - Time.deltaTime / duration);

            // The bolt slides along the model's own -Z axis; the offset is carried into parent
            // space by the rest rotation (Vector3.back is enough).
            float offset = boltBackMeters * Mathf.SmoothStep(0f, 1f, _boltKick);
            _bolt.localPosition = _boltRestPosition + _boltRestRotation * (Vector3.back * offset);
        }

        // --------------------------------------------------------- magazine movement

        private void HandleReloadStarted(float duration)
        {
            _reloading = true;
            _reloadStartTime = Time.time;
            _reloadDuration = Mathf.Max(0.05f, duration);
            _magPulled = false;
            _magInserted = false;

            // If a previous reload was interrupted the magazine may have stayed hidden — the pull
            // phase always starts with a visible magazine.
            if (_mag != null)
            {
                _mag.gameObject.SetActive(true);
            }

            // Shell-by-shell weapons (WeaponDefinition.PerShellReloadAudio) play the same clip
            // capacity-many times across the reload; the interval is DERIVED from the reload
            // duration, not the clip length, so the audio adapts when that duration changes.
            WeaponDefinition definition = weapon != null ? weapon.Definition : null;
            ResolveReloadTimeline(definition);
            _shellTotal = 1;
            _shellInterval = 0f;
            if (definition != null && definition.PerShellReloadAudio &&
                definition.MagOutClip != null && definition.MagazineSize > 1)
            {
                _shellTotal = definition.MagazineSize;
                _shellInterval = _reloadDuration / _shellTotal;
            }

            // Played at t=0: in single-clip mode (no MagInClip) this clip carries the entire reload
            // sound.
            if (weaponAudio != null)
            {
                weaponAudio.PlayMagOut();
            }

            _shellsPlayed = 1;
        }

        private void TickReload()
        {
            if (!_reloading)
            {
                return;
            }

            float elapsed = Time.time - _reloadStartTime;
            TickShellAudio(elapsed);

            // The timeline is in SECONDS: the animation may be shorter than the reload and once it
            // ends the magazine waits in its rest pose (the last line below is pinned to 1).
            if (elapsed < _magOutEnd)
            {
                // The magazine slides down while tilting forward (linear).
                ApplyMagOffset(elapsed / Mathf.Max(0.0001f, _magOutEnd));
                return;
            }

            if (!_magPulled)
            {
                _magPulled = true;
                if (_mag != null)
                {
                    _mag.gameObject.SetActive(false);
                }
            }

            if (elapsed < _magInStart)
            {
                return;
            }

            if (!_magInserted)
            {
                _magInserted = true;
                if (_mag != null)
                {
                    _mag.gameObject.SetActive(true);
                }

                if (weaponAudio != null && weapon != null && weapon.Definition != null &&
                    weapon.Definition.MagInClip != null)
                {
                    weaponAudio.PlayMagIn();
                }
            }

            // Reverse interpolation from the down pose back to the rest pose (SmoothStep).
            float t = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((elapsed - _magInStart) / Mathf.Max(0.0001f, _animDuration - _magInStart)));
            ApplyMagOffset(1f - t);
        }

        /// <summary>
        /// Magazine animation duration and phase boundaries (seconds). Order:
        /// <see cref="manualReloadDuration"/> → magazine clips → reload duration. Always clamped to
        /// the reload duration (the animation cannot outlast the game rule); ending shorter is
        /// expected — the magazine then waits in its rest pose.
        /// </summary>
        private void ResolveReloadTimeline(WeaponDefinition definition)
        {
            float outLength = definition != null && definition.MagOutClip != null
                ? definition.MagOutClip.length
                : 0f;
            float inLength = definition != null && definition.MagInClip != null
                ? definition.MagInClip.length
                : 0f;

            if (manualReloadDuration > 0f)
            {
                SetTimelineFromDefaults(Mathf.Min(manualReloadDuration, _reloadDuration));
                return;
            }

            // On a shell-by-shell loading weapon the clip is the sound of a SINGLE shell (see
            // PerShellReloadAudio) — the animation's measure is not that clip but the whole reload.
            if (outLength <= 0f || (definition != null && definition.PerShellReloadAudio))
            {
                SetTimelineFromDefaults(_reloadDuration);
                return;
            }

            if (inLength > 0f)
            {
                // Two-clip weapon: the magazine goes down over the magOut clip and returns over the
                // magIn clip.
                _animDuration = Mathf.Max(0.05f, Mathf.Min(outLength + inLength, _reloadDuration));
                float scale = _animDuration / (outLength + inLength);
                _magOutEnd = outLength * scale;
                _magInStart = _magOutEnd;
                return;
            }

            // Single-clip weapon: the clip carries the entire reload sound and the animation is
            // fitted to it.
            SetTimelineFromDefaults(Mathf.Min(outLength, _reloadDuration));
        }

        /// <summary>Spreads the given duration over the timeline with the default phase ratios
        /// (0.35 / 0.7).</summary>
        private void SetTimelineFromDefaults(float duration)
        {
            _animDuration = Mathf.Max(0.05f, duration);
            _magOutEnd = _animDuration * MagOutPhaseEnd;
            _magInStart = _animDuration * MagInPhaseStart;
        }

        /// <summary>
        /// Shell-by-shell loading sound at equal intervals. The count survives skipped frames (the
        /// while loop catches up); the first sound played at reload start.
        /// </summary>
        private void TickShellAudio(float elapsed)
        {
            if (_shellInterval <= 0f || weaponAudio == null)
            {
                return;
            }

            while (_shellsPlayed < _shellTotal && elapsed >= _shellsPlayed * _shellInterval)
            {
                weaponAudio.PlayMagOut();
                _shellsPlayed++;
            }
        }

        private void HandleReloadCompleted()
        {
            _reloading = false;
            _magPulled = false;
            _magInserted = false;
            _shellsPlayed = _shellTotal;

            if (_mag == null)
            {
                return;
            }

            // Snap to the rest pose immediately (independent of where Update left off).
            _mag.gameObject.SetActive(true);
            _mag.localPosition = _magRestPosition;
            _mag.localRotation = _magRestRotation;
        }

        /// <summary>Moves the magazine from the rest pose to the ejected pose: 0=rest, 1=fully
        /// out.</summary>
        private void ApplyMagOffset(float amount)
        {
            if (_mag == null)
            {
                return;
            }

            _mag.localPosition = _magRestPosition + _magRestRotation * (Vector3.down * (magDropMeters * amount));
            _mag.localRotation = _magRestRotation * Quaternion.Euler(magTiltDegrees * amount, 0f, 0f);
        }

        /// <summary>
        /// Searches for a part by name across ALL descendants of the root: first the first
        /// transform ENDING with the token, otherwise the first one CONTAINING it (e.g.
        /// "AR_A_Mag_2", "AR_D_Bolt_Part").
        /// </summary>
        private static Transform FindPart(Transform root, string token)
        {
            if (root == null)
            {
                return null;
            }

            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            Transform containsMatch = null;

            for (int i = 0; i < all.Length; i++)
            {
                Transform candidate = all[i];
                if (candidate == null || candidate == root)
                {
                    continue;
                }

                string partName = candidate.name;
                if (partName.EndsWith(token, StringComparison.Ordinal))
                {
                    return candidate;
                }

                if (containsMatch == null && partName.IndexOf(token, StringComparison.Ordinal) >= 0)
                {
                    containsMatch = candidate;
                }
            }

            return containsMatch;
        }
    }
}
