using System.Collections.Generic;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Uzak oyuncuların atış FX/SFX'i: sunucunun relay ettiği <c>shot_fired</c>
    /// mesajını dinler, namlu pozunda flaş + mekânsal ses oynatır. Sunucu mesajı
    /// ATANI HARİÇ herkese relay eder — kendi atışımız buradan ASLA gelmez, yerel
    /// atış FX'i Weapon/WeaponAudio'da kalır. Pozlar/yönler ARENA UZAYINDADIR
    /// (Docs/ArenaNet-Protokol.md §3), oynatmadan önce dünyaya çevrilir.
    /// <para>
    /// Sahnede DURMAZ: PlayerCombatState deseniyle kendini önyükler ve
    /// DontDestroyOnLoad olur. FX düğümleri 8'lik round-robin havuzda tembel üretilir:
    /// <c>WeaponCatalog.RemoteShotFxPrefab</c> varsa o, yoksa sade AudioSource fallback'i.
    /// </para>
    /// </summary>
    public class RemoteShotFx : MonoBehaviour
    {
        /// <summary>Havuzdaki FX düğümü sayısı (aynı anda canlı kalabilecek atış efekti).</summary>
        private const int PoolSize = 8;

        /// <summary>Bu mesafeden (metre) uzak atışlarda ses çalınmaz, yalnız flaş kalır.</summary>
        private const float MaxAudibleDistanceMeters = 40f;

        /// <summary>Fallback AudioSource'un sönümlenme mesafesi.</summary>
        private const float FallbackMaxDistanceMeters = 60f;

        /// <summary>Atış başına yayılan parçacık sayısı.</summary>
        private const int ParticlesPerShot = 14;

        public static RemoteShotFx Instance { get; private set; }

        /// <summary>Havuz düğümü; bileşenler üretim anında önbelleklenir (atış başına GetComponent yok).</summary>
        private sealed class FxNode
        {
            public Transform Root;
            public AudioSource Source;
            public ParticleSystem Particles;
        }

        private readonly FxNode[] _pool = new FxNode[PoolSize];
        private int _nextNode;

        // weaponId başına tek "katalogda yok" uyarısı (log taşmasın).
        private readonly HashSet<string> _warnedWeaponIds = new HashSet<string>();
        private bool _warnedNoPrefab;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[RemoteShotFx]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<RemoteShotFx>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // İkinci kopya (sahneye elle konmuş olabilir) kendini yok eder.
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Kalıcı tekiliz: OnEnable/OnDisable yerine Awake/OnDestroy'da abone oluruz,
            // böylece obje devre dışı bırakılsa bile sunucu olayları kaçmaz.
            NetEvents.OnShotFired += HandleShotFired;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            NetEvents.OnShotFired -= HandleShotFired;
            Instance = null;
        }

        private void HandleShotFired(ShotFiredMsg msg)
        {
            if (msg == null ||
                msg.muzzlePos == null || msg.muzzlePos.Length < 3 ||
                msg.muzzleDir == null || msg.muzzleDir.Length < 3)
            {
                return;
            }

            var arenaPos = new Vector3(msg.muzzlePos[0], msg.muzzlePos[1], msg.muzzlePos[2]);
            var arenaDir = new Vector3(msg.muzzleDir[0], msg.muzzleDir[1], msg.muzzleDir[2]);

            // Yön bir NOKTA değildir: ArenaToWorld nokta çevirir, yön iki dünya
            // noktasının farkından çıkarılır (origin dönük/ötelenmişse de doğru kalır).
            Vector3 worldPos = ArenaSpace.ArenaToWorld(arenaPos);
            Vector3 worldDir = ArenaSpace.ArenaToWorld(arenaPos + arenaDir) - worldPos;
            worldDir = worldDir.sqrMagnitude > 1e-6f ? worldDir.normalized : Vector3.forward;

            WeaponCatalog catalog = WeaponCatalog.Load();
            WeaponDefinition def = catalog != null ? catalog.FindByWeaponId(msg.weaponId) : null;
            if (def == null)
            {
                WarnUnknownWeapon(msg.weaponId);
            }

            FxNode node = TakeNode(catalog);
            if (node == null || node.Root == null)
            {
                return;
            }

            node.Root.SetPositionAndRotation(worldPos, Quaternion.LookRotation(worldDir));

            if (node.Particles != null)
            {
                node.Particles.Emit(ParticlesPerShot);
            }

            PlayShotSound(node, def, worldPos);
        }

        /// <summary>def yoksa (katalog dışı weaponId) veya dinleyici yok/uzaksa yalnız flaş kalır.</summary>
        private static void PlayShotSound(FxNode node, WeaponDefinition def, Vector3 worldPos)
        {
            if (def == null || node.Source == null || def.FireClips == null || def.FireClips.Length == 0)
            {
                return;
            }

            Camera listener = Camera.main;
            if (listener == null)
            {
                return;
            }

            if ((listener.transform.position - worldPos).sqrMagnitude >
                MaxAudibleDistanceMeters * MaxAudibleDistanceMeters)
            {
                return;
            }

            AudioClip clip = def.FireClips[Random.Range(0, def.FireClips.Length)];
            if (clip == null)
            {
                return;
            }

            node.Source.pitch = def.FirePitchBase + Random.Range(-def.FirePitchJitter, def.FirePitchJitter);
            node.Source.PlayOneShot(clip, def.FireVolume);
        }

        /// <summary>Round-robin: sıradaki (en eski) düğümü döndürür; henüz yoksa tembel üretir.</summary>
        private FxNode TakeNode(WeaponCatalog catalog)
        {
            FxNode node = _pool[_nextNode];
            if (node == null || node.Root == null)
            {
                node = CreateNode(catalog);
                _pool[_nextNode] = node;
            }

            _nextNode = (_nextNode + 1) % PoolSize;
            return node;
        }

        private FxNode CreateNode(WeaponCatalog catalog)
        {
            GameObject prefab = catalog != null ? catalog.RemoteShotFxPrefab : null;
            GameObject go;

            if (prefab != null)
            {
                // DDOL kökümüzün altında yaşar — sahne geçişinde havuz yok olmaz.
                go = Instantiate(prefab, transform);
            }
            else
            {
                if (!_warnedNoPrefab)
                {
                    _warnedNoPrefab = true;
                    Debug.LogWarning(
                        "[RemoteShotFx] WeaponCatalog.RemoteShotFxPrefab atanmadı — parçacıksız sade ses düğümü kullanılacak.");
                }

                go = new GameObject("[RemoteShotFxNode]");
                go.transform.SetParent(transform, false);

                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                source.maxDistance = FallbackMaxDistanceMeters;
            }

            return new FxNode
            {
                Root = go.transform,
                Source = go.GetComponentInChildren<AudioSource>(true),
                Particles = go.GetComponentInChildren<ParticleSystem>(true),
            };
        }

        private void WarnUnknownWeapon(string weaponId)
        {
            string key = weaponId ?? "";
            if (!_warnedWeaponIds.Add(key))
            {
                return;
            }

            Debug.LogWarning($"[RemoteShotFx] weaponId '{key}' WeaponCatalog'da yok — atış yalnız flaş olarak oynatılır.");
        }
    }
}
