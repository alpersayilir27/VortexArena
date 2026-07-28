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
        /// <summary>Reload ilerlemesinde şarjörün kaybolduğu faz sonu.</summary>
        private const float MagOutPhaseEnd = 0.35f;

        /// <summary>Reload ilerlemesinde şarjörün geri gelmeye başladığı faz başı.</summary>
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

            // t=0'da çalınır: tek-klip modunda (MagInClip yok) bu klip tüm reload sesini taşır.
            if (weaponAudio != null)
            {
                weaponAudio.PlayMagOut();
            }
        }

        private void TickReload()
        {
            if (!_reloading)
            {
                return;
            }

            float f = Mathf.Clamp01((Time.time - _reloadStartTime) / _reloadDuration);

            if (f < MagOutPhaseEnd)
            {
                // Şarjör aşağı + öne eğilerek kayar (doğrusal).
                ApplyMagOffset(f / MagOutPhaseEnd);
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

            if (f < MagInPhaseStart)
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
            float t = Mathf.SmoothStep(0f, 1f, (f - MagInPhaseStart) / (1f - MagInPhaseStart));
            ApplyMagOffset(1f - t);
        }

        private void HandleReloadCompleted()
        {
            _reloading = false;
            _magPulled = false;
            _magInserted = false;

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
