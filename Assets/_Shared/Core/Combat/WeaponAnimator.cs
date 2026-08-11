using System;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Kod-güdümlü silah parça animasyonu: atışta bolt geri tepmesi, reload'da
    /// şarjör çıkar/tak hareketi. Animator BİLİNÇLİ olarak kullanılmaz — animasyon
    /// klip yolları silah modeline göre değişir, sürelerin silahın reload süresine
    /// normalize edilmesi gerekir ve Quest'te Animator bileşen maliyeti böylece
    /// sıfır kalır; iki transformu f(t) ile sürmek yeterlidir.
    /// <para>
    /// Parçalar ada göre bulunur (şarjör "_Mag", bolt "_Bolt"); bulunamazsa bileşen
    /// sessizce çalışmaya devam eder — pack dışı modellerde kırılmaz.
    /// Silah bırakılsa da Update çalışır: reload'u Weapon kendisi tamamlar, burada
    /// yalnız f(t) çizilir.
    /// </para>
    /// </summary>
    public class WeaponAnimator : MonoBehaviour
    {
        /// <summary>ANİMASYON ilerlemesinde şarjörün kaybolduğu faz sonu (tek klipli silahta).</summary>
        private const float MagOutPhaseEnd = 0.35f;

        /// <summary>ANİMASYON ilerlemesinde şarjörün geri gelmeye başladığı faz başı (tek klipli).</summary>
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

        /// <summary>Atışta 1'e çekilir, atış temposuna bağlı sürede 0'a iner.</summary>
        private float _boltKick;

        private bool _reloading;
        private float _reloadStartTime;
        private float _reloadDuration;
        private bool _magPulled;
        private bool _magInserted;

        /// <summary>Bu reload'da çalınacak dolum sesi adedi (1 = tek klip, pompalıda kapasite).</summary>
        private int _shellTotal;

        /// <summary>Bu reload'da şimdiye kadar çalınan dolum sesi adedi.</summary>
        private int _shellsPlayed;

        /// <summary>Dolum sesleri arası saniye; 0 = tekrar yok.</summary>
        private float _shellInterval;

        /// <summary>Şarjör animasyonunun süresi (saniye) — reload süresinden KISA olabilir.</summary>
        private float _animDuration;

        /// <summary>Animasyon başından itibaren şarjörün tamamen indiği an (saniye).</summary>
        private float _magOutEnd;

        /// <summary>Animasyon başından itibaren şarjörün geri gelmeye başladığı an (saniye).</summary>
        private float _magInStart;

        private void Start()
        {
            // Parça araması Start'a bırakıldı: weapon.ModelPivot'un Weapon tarafında
            // kurulmuş olması garanti olsun (OnEnable abonelikleri bundan etkilenmez,
            // işleyiciler parça yokken de null-güvenli).
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

        // ------------------------------------------------------------ bolt tepmesi

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

            // Geri dönüş süresi atış temposuna bağlanır: hızlı silahta bolt da hızlı döner.
            float duration = Mathf.Max(0.03f, weapon != null && weapon.Definition != null
                ? weapon.Definition.SecondsPerShot * 0.5f
                : 0.06f);

            _boltKick = Mathf.Max(0f, _boltKick - Time.deltaTime / duration);

            // Bolt modelin kendi -Z ekseninde kayar; ofset dinlenme rotasyonuyla
            // parent uzayına taşınır (Vector3.back yeterli).
            float offset = boltBackMeters * Mathf.SmoothStep(0f, 1f, _boltKick);
            _bolt.localPosition = _boltRestPosition + _boltRestRotation * (Vector3.back * offset);
        }

        // --------------------------------------------------------- şarjör hareketi

        private void HandleReloadStarted(float duration)
        {
            _reloading = true;
            _reloadStartTime = Time.time;
            _reloadDuration = Mathf.Max(0.05f, duration);
            _magPulled = false;
            _magInserted = false;

            // Önceki reload yarıda kalmışsa şarjör gizli kalmış olabilir — çekiş
            // fazı her zaman görünür şarjörle başlar.
            if (_mag != null)
            {
                _mag.gameObject.SetActive(true);
            }

            // Fişek fişek dolan silahta (WeaponDefinition.PerShellReloadAudio) aynı klip reload
            // süresine yayılarak kapasite kadar kez çalar; aralık süreden TÜRETİLİR, klip
            // uzunluğundan değil — reload süresi değişince ses kendiliğinden uyar.
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

            // t=0'da çalınır: tek-klip modunda (MagInClip yok) bu klip tüm reload sesini taşır.
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

            // Zaman çizgisi SANİYE cinsindendir: animasyon reload'dan kısa olabilir ve bitince
            // şarjör dinlenme pozunda bekler (aşağıdaki son satır 1'e sabitlenir).
            if (elapsed < _magOutEnd)
            {
                // Şarjör aşağı + öne eğilerek kayar (doğrusal).
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

            // Aşağıdaki pozdan dinlenme pozuna ters interpolasyon (SmoothStep).
            float t = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01((elapsed - _magInStart) / Mathf.Max(0.0001f, _animDuration - _magInStart)));
            ApplyMagOffset(1f - t);
        }

        /// <summary>
        /// Şarjör animasyonunun süresini ve faz sınırlarını (saniye) belirler.
        /// Sıra: <see cref="manualReloadDuration"/> → şarjör klipleri → reload süresi. Sonuç her
        /// hâlde reload süresine kırpılır (animasyon oyun kuralından uzun süremez); kısa kalması
        /// beklenen durumdur, kalan sürede şarjör dinlenme pozunda bekler.
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

            // Fişek fişek dolan silahta klip TEK fişeğin sesidir (bkz. PerShellReloadAudio) —
            // animasyonun ölçüsü o klip değil, reload'un tamamıdır.
            if (outLength <= 0f || (definition != null && definition.PerShellReloadAudio))
            {
                SetTimelineFromDefaults(_reloadDuration);
                return;
            }

            if (inLength > 0f)
            {
                // İki klipli silah: şarjör magOut klibi boyunca iner, magIn klibi boyunca döner.
                _animDuration = Mathf.Max(0.05f, Mathf.Min(outLength + inLength, _reloadDuration));
                float scale = _animDuration / (outLength + inLength);
                _magOutEnd = outLength * scale;
                _magInStart = _magOutEnd;
                return;
            }

            // Tek klipli silah: klip tüm reload sesini taşır, animasyon ona oturur.
            SetTimelineFromDefaults(Mathf.Min(outLength, _reloadDuration));
        }

        /// <summary>Verilen süreyi varsayılan faz oranlarıyla (0.35 / 0.7) zaman çizgisine yayar.</summary>
        private void SetTimelineFromDefaults(float duration)
        {
            _animDuration = Mathf.Max(0.05f, duration);
            _magOutEnd = _animDuration * MagOutPhaseEnd;
            _magInStart = _animDuration * MagInPhaseStart;
        }

        /// <summary>
        /// Fişek fişek dolum sesi: kalan sesleri eşit aralıkla çalar. Kare atlansa da adet
        /// korunur (while ile birikmiş aralıklar kapatılır); ilk ses reload başında çalındı.
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

            // Dinlenme pozuna anında oturt (Update'in kaldığı yerden bağımsız).
            _mag.gameObject.SetActive(true);
            _mag.localPosition = _magRestPosition;
            _mag.localRotation = _magRestRotation;
        }

        /// <summary>Şarjörü dinlenme pozundan çıkış pozuna taşır: 0=dinlenme, 1=tam çıkmış.</summary>
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
        /// Kökün TÜM torunlarında ada göre parça arar: önce token ile BİTEN ilk
        /// transform, yoksa token İÇEREN ilk (ör. "AR_A_Mag_2", "AR_D_Bolt_Part").
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
