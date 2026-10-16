using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The zombie mode's weapon weight: per-weapon spring recoil on the model pivot plus the
    /// fire haptic and the close-fire audio layer.</summary>
    /// <remarks>Installed over <see cref="Weapon.FeelOverride"/>, which REPLACES the weapon's own
    /// linear kick and fire pulse — everything the shot writes is written here.
    /// <para>⚠️ The camera never moves (plan: "ekran sarsıntısı yok"): weight is the model, the sound
    /// and the vibration. Do not add a head/eye offset here.</para>
    /// <para>⚠️ <c>OnShot</c> is also the mode's "the LOCAL player fired" signal: a remote player's
    /// shot never runs <c>Weapon.Fire</c> (it arrives as a shot event and is drawn by
    /// <c>RemoteShotFx</c>), so no holder filtering is needed.</para></remarks>
    internal sealed class ZombieWeaponFeel : IWeaponFeel
    {
        private const float MaxStep = 0.05f;

        // Below this the channel is parked: a spring never reaches exactly zero and a model that keeps
        // being written cannot be moved by anything else.
        private const float RestEpsilon = 0.0005f;

        private static readonly Dictionary<string, ZombieWeaponClass> Classes =
            new Dictionary<string, ZombieWeaponClass>(16)
            {
                { "glock", ZombieWeaponClass.Pistol },
                { "magnum", ZombieWeaponClass.Magnum },
                { "ump45", ZombieWeaponClass.Smg },
                { "p90", ZombieWeaponClass.Smg },
                { "mp9", ZombieWeaponClass.Smg },
                { "g36c", ZombieWeaponClass.Rifle },
                { "scarl", ZombieWeaponClass.Rifle },
                { "famas", ZombieWeaponClass.Rifle },
                { "m4a4", ZombieWeaponClass.Rifle },
                { "m4a1", ZombieWeaponClass.Rifle },
                { "aug", ZombieWeaponClass.Rifle },
                { "galilar", ZombieWeaponClass.Rifle },
                { "ak47", ZombieWeaponClass.Rifle },
                { "nova", ZombieWeaponClass.Shotgun },
                { "xm1014", ZombieWeaponClass.Shotgun },
            };

        private readonly Dictionary<Weapon, Channels> states = new Dictionary<Weapon, Channels>(8);

        // Released states are kept for the next grab: a weapon swap must not allocate mid-match.
        private readonly Stack<Channels> pool = new Stack<Channels>(8);

        /// <summary>The installed hook, for the feedback components that need the firing hand.</summary>
        public static ZombieWeaponFeel Installed { get; set; }

        /// <summary>Trigger hand of the last local shot; <c>None</c> = nothing fired yet or the hand was
        /// unresolved. Kill and dismember cues ride on it — those arrive after the shot, when the grip
        /// may already be gone.</summary>
        public OVRInput.Controller LastFiringHand { get; private set; }

        private sealed class Channels
        {
            public float Pitch;
            public float PitchVelocity;
            public float Yaw;
            public float YawVelocity;
            public float Roll;
            public float RollVelocity;
            public float Back;
            public float BackVelocity;

            /// <summary>Ceiling of the accumulated up-kick (3× one shot's peak by default).</summary>
            public float PitchLimit;

            /// <summary>Already written at rest: stop writing so the weapon owns the pivot again.</summary>
            public bool Parked;

            public void Reset()
            {
                Pitch = PitchVelocity = Yaw = YawVelocity = Roll = RollVelocity = Back = BackVelocity = 0f;
                PitchLimit = 0f;
                Parked = false;
            }
        }

        public void OnShot(in WeaponShotContext shot)
        {
            Weapon weapon = shot.Weapon;
            if (weapon == null)
            {
                return;
            }

            ZombieFeel feel = ZombieFeel.Live;
            string weaponId = shot.Definition != null ? shot.Definition.WeaponId : null;
            ZombieWeaponClass weaponClass = ResolveClass(weaponId);
            ZombieRecoilProfile profile = feel.GetRecoil(weaponId, weaponClass);

            // ⚠️ shot.RecoilScale is IGNORED on purpose: it carries the PvP grip tuning (prefab
            // two-hand multiplier + the definition's one-hand penalty). The mode's own two numbers
            // keep the zombie feel independent of PvP balance passes.
            // ⚠️ The one-hand penalty is only charged to a weapon DESIGNED for two hands and held with
            // one. A pistol is a one-handed weapon: its profile already IS its one-handed feel, and
            // multiplying it again would make the sidearm kick harder than the rifle it replaces.
            float gripScale = shot.TwoHanded
                ? feel.TwoHandScale
                : (shot.Definition != null && shot.Definition.IsTwoHanded ? feel.OneHandScale : 1f);

            if (!states.TryGetValue(weapon, out Channels state))
            {
                state = pool.Count > 0 ? pool.Pop() : new Channels();
                state.Reset();
                states[weapon] = state;
            }

            float omega = Mathf.Sqrt(feel.Stiffness);
            float gain = ImpulseGain(feel.DampingRatio);

            float pitchPeak = profile.pitchDegrees * gripScale;
            state.PitchLimit = pitchPeak * feel.MaxPitchKicks;
            state.PitchVelocity += pitchPeak * omega * gain;
            state.YawVelocity += Random.Range(-1f, 1f) * profile.yawDegrees * gripScale * omega * gain;
            state.RollVelocity += Random.Range(-1f, 1f) * profile.rollDegrees * gripScale * omega * gain;
            state.BackVelocity += profile.backMeters * gripScale * omega * gain;
            state.Parked = false;

            LastFiringHand = shot.MainHand;

            PlayFireHaptics(feel, weaponClass, shot.MainHand, shot.SupportHand);
            ZombieFeelRunner.PlayFireClose(weaponClass);
        }

        public bool TryGetPivotOffset(Weapon weapon, float deltaTime, out Vector3 position,
            out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (weapon == null || !states.TryGetValue(weapon, out Channels state) || state.Parked)
            {
                return false;
            }

            ZombieFeel feel = ZombieFeel.Live;
            float step = Mathf.Min(deltaTime, MaxStep);
            float stiffness = feel.Stiffness;
            float damping = 2f * feel.DampingRatio * Mathf.Sqrt(stiffness);

            Integrate(ref state.Pitch, ref state.PitchVelocity, stiffness, damping, step, state.PitchLimit);
            Integrate(ref state.Yaw, ref state.YawVelocity, stiffness, damping, step, 0f);
            Integrate(ref state.Roll, ref state.RollVelocity, stiffness, damping, step, 0f);
            Integrate(ref state.Back, ref state.BackVelocity, stiffness, damping, step, 0f);

            if (Mathf.Abs(state.Pitch) < RestEpsilon && Mathf.Abs(state.Yaw) < RestEpsilon &&
                Mathf.Abs(state.Roll) < RestEpsilon && Mathf.Abs(state.Back) < RestEpsilon * 0.1f)
            {
                // One last write parks the model exactly on its authored pose, then we go quiet.
                state.Reset();
                state.Parked = true;
                return true;
            }

            // Negative pitch = muzzle UP, mirroring the built-in kick's Euler(-kick, 0, 0).
            rotation = Quaternion.Euler(-state.Pitch, state.Yaw, state.Roll);
            position = Vector3.back * state.Back;
            return true;
        }

        public void OnReleased(Weapon weapon)
        {
            if (weapon == null || !states.TryGetValue(weapon, out Channels state))
            {
                return;
            }

            states.Remove(weapon);
            state.Reset();
            pool.Push(state);
        }

        /// <summary>Feel class of a <c>weaponId</c>; unknown ids read as a rifle (the middle profile,
        /// the least wrong guess for a weapon added without a feel row).</summary>
        public static ZombieWeaponClass ResolveClass(string weaponId)
        {
            if (!string.IsNullOrEmpty(weaponId) && Classes.TryGetValue(weaponId, out ZombieWeaponClass found))
            {
                return found;
            }

            return ZombieWeaponClass.Rifle;
        }

        private static void PlayFireHaptics(ZombieFeel feel, ZombieWeaponClass weaponClass,
            OVRInput.Controller mainHand, OVRInput.Controller supportHand)
        {
            ZombieHapticEvent fire = ZombieFeel.FireEvent(weaponClass);

            if (ZombieHaptics.TryMapHand(mainHand, out Oculus.Haptics.Controller main))
            {
                ZombieHaptics.Play(fire, main, feel.FireHapticGain);
            }

            if (supportHand != OVRInput.Controller.None && supportHand != mainHand &&
                ZombieHaptics.TryMapHand(supportHand, out Oculus.Haptics.Controller support))
            {
                ZombieHaptics.Play(fire, support, feel.SupportHandFireAmplitude * feel.FireHapticGain);
            }
        }

        /// <summary>Semi-implicit (symplectic) Euler on a damped spring toward zero: velocity first,
        /// then position. ⚠️ Explicit Euler blows up at this stiffness on a dropped frame.</summary>
        /// <param name="limit">Ceiling of |value|; <c>0</c> or less = unlimited.</param>
        private static void Integrate(ref float value, ref float velocity, float stiffness, float damping,
            float step, float limit)
        {
            velocity += ((-stiffness * value) - (damping * velocity)) * step;
            value += velocity * step;

            if (limit <= 0f || Mathf.Abs(value) <= limit)
            {
                return;
            }

            // Clamped: the velocity is dropped too, otherwise the next frame pushes against the wall.
            value = Mathf.Sign(value) * limit;
            velocity = 0f;
        }

        /// <summary>Velocity impulse that makes a damped spring peak at exactly 1: the profiles are
        /// authored as PEAK angles, so retuning stiffness/damping must not rescale the weapons.</summary>
        /// <remarks>x(t) = (v0/ωd)·e^(-ζωt)·sin(ωd t) peaks at ωd·t = atan(√(1-ζ²)/ζ), where
        /// x_peak = (v0/ω)·e^(-ζ·φ/√(1-ζ²)). So v0 = peak·ω·e^(ζ·φ/√(1-ζ²)); this returns that
        /// exponential (ζ = 0.6 → ≈2.005).</remarks>
        private static float ImpulseGain(float dampingRatio)
        {
            float rest = Mathf.Sqrt(1f - (dampingRatio * dampingRatio));
            if (rest <= 0.0001f)
            {
                return 1f;
            }

            float phi = Mathf.Atan2(rest, dampingRatio);
            return Mathf.Exp(dampingRatio * phi / rest);
        }
    }
}
