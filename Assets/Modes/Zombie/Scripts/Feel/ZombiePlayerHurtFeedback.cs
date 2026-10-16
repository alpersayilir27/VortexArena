using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Core.Player;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Modes.Zombie
{
    /// <summary>What a zombie's blow feels like on the player: vibration in both hands, a pain sound and
    /// the damage direction arc pointing at the attacker.</summary>
    /// <remarks>Source: <c>health_update.srcNetId</c> (protocol 27) — object damage keeps
    /// <c>attackerId = 0</c>, so the shared directional indicator stays silent and this component aims it
    /// from the zombie's chest instead.
    /// <para>⚠️ No vignette here: <c>DamageVignette</c> already samples HP and a second flash would
    /// double it.</para></remarks>
    internal sealed class ZombiePlayerHurtFeedback : MonoBehaviour
    {
        private float lastHp;
        private float nextSoundTime;

        private void OnEnable()
        {
            // Reseeded here, not in Awake: a stale value across a disable reads as a phantom hit.
            lastHp = ArenaCombat.LocalHp;
            NetEvents.OnHealthUpdate += HandleHealthUpdate;
        }

        private void OnDisable()
        {
            NetEvents.OnHealthUpdate -= HandleHealthUpdate;
        }

        private void HandleHealthUpdate(HealthUpdateMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            int localId = ArenaCombat.LocalPlayerId;
            if (localId == 0 || msg.playerId != localId)
            {
                return;
            }

            float previous = lastHp;
            lastHp = msg.hp;

            // srcNetId 0 = not an object (revive, environment); hp rising = healing.
            if (msg.srcNetId == 0 || msg.hp >= previous)
            {
                return;
            }

            ZombieFeel feel = ZombieFeel.Live;
            float damage = previous - msg.hp;

            ZombieHaptics.Play(ZombieHapticEvent.PlayerHurt, Oculus.Haptics.Controller.Both);

            // Cooldown: a crowd lands blows faster than one pain sound lasts, and stacked copies read
            // as a glitch rather than as pressure.
            if (Time.unscaledTime >= nextSoundTime)
            {
                nextSoundTime = Time.unscaledTime + feel.PlayerHurtCooldown;
                ZombieFeelRunner.Audio?.Play(feel.PlayerHurt, feel.PlayerHurtVolume, feel.HitPitchJitter);
            }

            // No arc without a live zombie: the object may be gone already (despawn race).
            if (ZombieFeelEvents.TryGet(msg.srcNetId, out IZombieFeelTarget target) && target != null)
            {
                DamageDirectionIndicator.ShowFromWorldPoint(target.ChestPosition, damage);
            }
        }
    }
}
