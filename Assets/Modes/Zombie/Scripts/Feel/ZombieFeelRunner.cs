using UnityEngine;

namespace VortexArena.Modes.Zombie
{
    /// <summary>Host of the zombie hit feel while the mode runs: the 2D audio pool, the haptic delay
    /// queue and the feedback components' event subscriptions.</summary>
    /// <remarks>One <c>DontDestroyOnLoad</c> object owned by <see cref="ZombieFeelInstaller"/>: the
    /// feel outlives scene loads (staging → arena) and must not be searched for at runtime.</remarks>
    internal sealed class ZombieFeelRunner : MonoBehaviour
    {
        private static ZombieFeelRunner instance;

        private ZombieFeelAudio audioPool;

        /// <summary>The live pool; <c>null</c> while the mode is not running.</summary>
        public static ZombieFeelAudio Audio => instance != null ? instance.audioPool : null;

        /// <summary>Creates the runner with its feedback components, or returns the live one.</summary>
        public static ZombieFeelRunner Ensure()
        {
            if (instance != null)
            {
                return instance;
            }

            // ⚠️ Built INACTIVE, activated last: the feedback components read the audio pool from their
            // OnEnable, and on activation Awake/OnEnable run in component order — so the runner's Awake
            // (which creates the pool) is guaranteed to come first.
            GameObject host = new GameObject("ZombieFeelRunner");
            host.SetActive(false);
            Object.DontDestroyOnLoad(host);

            host.AddComponent<ZombieFeelRunner>();
            host.AddComponent<ZombieHitFeedback>();
            host.AddComponent<ZombiePlayerHurtFeedback>();
            host.SetActive(true);

            return instance;
        }

        /// <summary>Tears the runner down; the haptic players are released with it.</summary>
        public static void Release()
        {
            ZombieHaptics.Shutdown();

            if (instance == null)
            {
                return;
            }

            GameObject host = instance.gameObject;
            instance = null;
            Object.Destroy(host);
        }

        /// <summary>The close "punch" layer over the weapon's own 3D fire sound; no-op outside the mode.</summary>
        public static void PlayFireClose(ZombieWeaponClass weaponClass)
        {
            ZombieFeelAudio pool = Audio;
            if (pool == null)
            {
                return;
            }

            ZombieFeel feel = ZombieFeel.Live;
            pool.Play(feel.GetFireClose(weaponClass), feel.FireCloseVolume, feel.FireClosePitchJitter);
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }

            instance = this;
            audioPool = new ZombieFeelAudio(gameObject, ZombieFeel.Live.AudioPoolSize);
        }

        private void Update()
        {
            ZombieHaptics.Tick();
        }

        private void OnDestroy()
        {
            if (instance != this)
            {
                return;
            }

            audioPool?.StopAll();
            instance = null;
        }
    }
}
