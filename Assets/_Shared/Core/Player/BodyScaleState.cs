using System;
using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Yerel oyuncunun <b>gövde ölçüsünü</b> alır ve sunucuya bildirir (§10.8).
    /// <para>
    /// Ölçek, oyuncunun göz yüksekliğinin karakterin <b>o anki</b> göz yüksekliğine oranıdır; ikisi
    /// de arena uzayında, <b>aynı karede</b> okunur. Karakter zaten body tracking'den sürüldüğü için
    /// oyuncuyla aynı pozdadır — yani duruş farkı orandan kendiliğinden düşer ve sabit bir "model
    /// göz yüksekliği" sayısı tutmak gerekmez (o sayı model değiştiğinde sessizce bayatlardı).
    /// </para>
    /// <para>
    /// ⚠️ <b>Ölçümü ZAMAN tetiklemez, operatör tetikler</b> (<c>measure_body_scale</c>). Ölçünün
    /// doğru anını — oyuncunun ayakta ve dik durduğu anı — makine bilemez; kalibrasyondan otomatik
    /// tetiklenen bir ölçüm, oyuncu kumandayı zemine değdirmek için <b>eğilmişken</b> ölçmek olurdu.
    /// </para>
    /// <para>
    /// ⚠️ <b>Ölçek YEREL karaktere uygulanmaz</b> (yalnız uzak avatarlara, §10.8). Ölçümün
    /// tekrarlanabilirliği buna bağlıdır: yerel karakter de ölçeklenseydi ikinci ölçüm zaten
    /// ölçeklenmiş bir referansı okur ve çarpanı <c>1</c>'e yaklaştırırdı.
    /// </para>
    /// <para>
    /// Sahnede DURMAZ: kalıcı tekil olarak kendini önyükler (<see cref="CalibrationState"/> deseni)
    /// — her arenaya elle bir kurulum adımı eklememek için. Rig yoksa (admin gözlemci) hiçbir şey
    /// yapmaz.
    /// </para>
    /// </summary>
    public class BodyScaleState : MonoBehaviour
    {
        /// <summary>Ölçeğin cihazda saklandığı anahtar — yeniden bağlanan oyuncu yeniden ölçülmesin.</summary>
        private const string ScalePrefsKey = "VortexArena.BodyScale";

        /// <summary>Örnekleme penceresi (sn). Tek kare, SDK'nın çözüm gürültüsünü ölçüye yazardı.</summary>
        private const float SampleSeconds = 0.5f;

        /// <summary>
        /// Örnekler arasındaki kabul edilen en büyük yayılım (medyanın oranı olarak). Aşılırsa
        /// ölçüm <b>reddedilir</b>: oyuncu o sırada hareket ediyor ya da eğiliyor demektir.
        /// <para>Oran ölçüldüğü için (pay ve payda birlikte oynar) bu eşik gerçek duruş
        /// değişimlerini yakalar, başın normal salınımını değil.</para>
        /// </summary>
        private const float MaxSpreadRatio = 0.05f;

        /// <summary>Altında ölçümün anlamsız sayıldığı göz yüksekliği (m) — oyuncu ya da karakter
        /// yerde/çömelmiş demektir.</summary>
        private const float MinEyeHeightMeters = 0.8f;

        /// <summary>Ölçek değişimini "yeni bir değer" saymak için gereken en küçük fark.</summary>
        private const float ScaleEpsilon = 0.0005f;

        public static BodyScaleState Instance { get; private set; }

        /// <summary>Sunucunun bildiği ölçek (<c>lobby_state</c>); <c>0</c> = ölçülmemiş.</summary>
        public static float ServerScale { get; private set; }

        /// <summary>Durum değiştiğinde (ana thread).</summary>
        public static event Action Changed;

        // Her bildirimde yeni DTO ayırmamak için tek örnek (CalibrationState deseni).
        private readonly SetBodyScaleMsg _reportMsg = new SetBodyScaleMsg();

        /// <summary>Bu cihazda ölçülmüş ölçek; 0 = yok. Yeniden bağlanınca bu değer bildirilir.</summary>
        private float _localScale;

        private OVRCameraRig _rig;
        private float _rigSearchTime = float.NegativeInfinity;
        private const float RigSearchIntervalSeconds = 0.5f;

        // ── Ölçüm penceresi ─────────────────────────────────────────────────────────────
        private bool _sampling;
        private float _sampleDeadline;

        // Örnekler alanda tutulur: ölçüm insan hızında olsa da kare başına tahsis etmemek için.
        private readonly float[] _samples = new float[128];
        private int _sampleCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[BodyScaleState]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<BodyScaleState>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _localScale = PlayerPrefs.GetFloat(ScalePrefsKey, 0f);

            // Kalıcı tekiliz: obje devre dışı bırakılsa bile olaylar kaçmasın diye
            // OnEnable/OnDisable yerine Awake/OnDestroy'da abone oluruz.
            NetEvents.OnMeasureBodyScale += HandleMeasureRequest;
            NetEvents.OnConnected += HandleConnected;
            NetEvents.OnLobbyState += HandleLobbyState;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            NetEvents.OnMeasureBodyScale -= HandleMeasureRequest;
            NetEvents.OnConnected -= HandleConnected;
            NetEvents.OnLobbyState -= HandleLobbyState;

            Instance = null;
        }

        // ------------------------------------------------------------------ ölçüm

        /// <summary>Operatör ölçüm istedi → örnekleme penceresini açar.</summary>
        private void HandleMeasureRequest()
        {
            _sampling = true;
            _sampleCount = 0;
            _sampleDeadline = Time.unscaledTime + SampleSeconds;
        }

        private void Update()
        {
            if (!_sampling)
            {
                return;
            }

            if (TrySampleRatio(out float ratio) && _sampleCount < _samples.Length)
            {
                _samples[_sampleCount++] = ratio;
            }

            if (Time.unscaledTime < _sampleDeadline)
            {
                return;
            }

            _sampling = false;
            FinishMeasurement();
        }

        /// <summary>
        /// Bu karenin oranı: oyuncunun gözü / karakterin gözü, ikisi de arena uzayında (zemin
        /// y=0). Ölçülemiyorsa <c>false</c> — o kare sessizce atlanır, pencere sonunda toplam
        /// örnek sayısına bakılır.
        /// </summary>
        private bool TrySampleRatio(out float ratio)
        {
            ratio = 0f;

            OVRCameraRig rig = ResolveRig();
            LocalBodyAvatar body = LocalBodyAvatar.Instance;
            if (rig == null || rig.centerEyeAnchor == null || body == null)
            {
                return false;
            }

            Transform avatarEye = body.EyeAnchor;
            if (avatarEye == null || !body.IsBodyPoseValid)
            {
                return false;
            }

            float playerEyeY = ArenaSpace.WorldToArena(rig.centerEyeAnchor.position).y;
            float avatarEyeY = ArenaSpace.WorldToArena(avatarEye.position).y;
            if (playerEyeY < MinEyeHeightMeters || avatarEyeY < MinEyeHeightMeters)
            {
                return false;
            }

            ratio = playerEyeY / avatarEyeY;
            return true;
        }

        /// <summary>
        /// Pencere doldu: medyanı al, yayılımı denetle, kırp ve bildir.
        /// <para>⚠️ Başarısız ölçümde <b>ölçek gönderilmez ama GEREKÇE gider</b> (§10.8): eski ölçek
        /// durur, sebep hem konsola yazılır hem <c>set_body_scale.error</c> ile operatöre gider.
        /// Yanlış bir boyu yazmak hiç yazmamaktan kötüdür — sonucu yalnız başkaları görür; ama
        /// sessiz kalmak da kötüdür, ölçümü isteyen operatör düğmenin işlemediğini sanır.</para>
        /// </summary>
        private void FinishMeasurement()
        {
            if (_sampleCount == 0)
            {
                Debug.LogWarning(
                    "[BodyScaleState] Gövde ölçülemedi: gövde pozu yok ya da göz işaretçisi bağlı " +
                    "değil. Resources/LocalBodyAvatar.prefab içindeki 'Eye Anchor' alanı kafa " +
                    "kemiğinin altındaki işaretçiyi göstermeli.", this);
                ReportError("gövde pozu yok");
                return;
            }

            Array.Sort(_samples, 0, _sampleCount);
            float median = _samples[_sampleCount / 2];
            float spread = _samples[_sampleCount - 1] - _samples[0];

            if (median <= 0f || spread / median > MaxSpreadRatio)
            {
                Debug.LogWarning(
                    $"[BodyScaleState] Ölçüm reddedildi: {_sampleCount} örnekte yayılım " +
                    $"%{spread / Mathf.Max(median, 0.0001f) * 100f:F1} (tavan " +
                    $"%{MaxSpreadRatio * 100f:F0}). Oyuncu ölçüm anında hareketli ya da eğilmiş — " +
                    "dik dururken tekrar ölçün.", this);
                ReportError("oyuncu hareketli/eğilmiş");
                return;
            }

            float scale = Mathf.Clamp(median, ArenaProtocol.BODY_SCALE_MIN, ArenaProtocol.BODY_SCALE_MAX);
            if (!Mathf.Approximately(scale, median))
            {
                Debug.LogWarning(
                    $"[BodyScaleState] Ölçülen {median:F3} protokol aralığının " +
                    $"({ArenaProtocol.BODY_SCALE_MIN:F2}–{ArenaProtocol.BODY_SCALE_MAX:F2}) dışında, " +
                    $"{scale:F3} olarak kırpıldı — avatar oyuncunun boyuna tam yetişmeyecek.", this);
            }

            SetLocalScale(scale);
            Report(scale);
            Debug.Log($"[BodyScaleState] Gövde ölçeği {scale:F3} ({_sampleCount} örnek).", this);
        }

        // ------------------------------------------------------------- başlık → sunucu

        private void Report(float scale)
        {
            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
            {
                return; // sunucusuz oturum: bildirilecek kimse yok
            }

            _reportMsg.scale = scale;
            // ⚠️ DTO tek örnektir: bayat bir hata alanı bir sonraki BAŞARILI ölçümü kirletirdi
            // (sunucu doluysa ölçeği yok sayıyor, §10.8).
            _reportMsg.error = "";
            client.Send(_reportMsg);
        }

        /// <summary>
        /// Ölçüm başarısız: ölçek yerine GEREKÇE gider (§10.8). <c>scale = 0</c> ve dolu
        /// <c>error</c> sunucuya "kayıtlı ölçeği değiştirme, gerekçeyi operatöre göster" der.
        /// <para>Kırpma dalı buraya GİRMEZ — kırpılmış ölçüm bir başarıdır, değeri yazılır.</para>
        /// </summary>
        private void ReportError(string reason)
        {
            ArenaClient client = ArenaClient.Instance;
            if (client == null || !client.IsConnected)
            {
                return; // sunucusuz oturum: bildirilecek kimse yok
            }

            _reportMsg.scale = 0f;
            _reportMsg.error = reason;
            client.Send(_reportMsg);
        }

        /// <summary>Yerel kaydı yazar (cihazda kalıcı). <c>0</c> = ölçüm yok.</summary>
        private void SetLocalScale(float scale)
        {
            _localScale = scale;
            if (scale > 0f)
            {
                PlayerPrefs.SetFloat(ScalePrefsKey, scale);
            }
            else
            {
                PlayerPrefs.DeleteKey(ScalePrefsKey);
            }

            PlayerPrefs.Save();
        }

        // ------------------------------------------------------------- sunucu → başlık

        /// <summary>
        /// Sunucu her <c>hello</c>'da ölçeği sıfırlar (§10.8, kalibrasyonla aynı gerekçe) — cihazda
        /// bir ölçüm duruyorsa hemen yeniden bildirilir, yani operatör yeniden ölçmek zorunda kalmaz.
        /// </summary>
        private void HandleConnected(WelcomeMsg msg)
        {
            ServerScale = 0f;
            if (_localScale > 0f)
            {
                Report(_localScale);
            }

            Raise();
        }

        /// <summary>
        /// Roster'daki kendi satırımız ölçeğin tek doğruluk kaynağıdır (§5.3).
        /// <para>⚠️ Sunucu <c>0</c> yayınladıysa <b>yerel kayıt da silinir</b>: operatör
        /// kalibrasyonu sıfırladığında ölçek de düşer ve silinmeseydi oyuncu bir sonraki
        /// bağlanışında onu kendiliğinden geri getirirdi — sıfırlama sessizce geri alınmış olurdu.</para>
        /// </summary>
        private void HandleLobbyState(LobbyStateMsg msg)
        {
            int selfId = PlayerCombatState.Instance != null ? PlayerCombatState.Instance.PlayerId : 0;
            if (msg == null || msg.players == null || selfId == 0)
            {
                return;
            }

            for (int i = 0; i < msg.players.Length; i++)
            {
                PlayerInfo info = msg.players[i];
                if (info == null || info.playerId != selfId)
                {
                    continue;
                }

                ApplyServerState(info.bodyScale);
                return;
            }
        }

        private void ApplyServerState(float scale)
        {
            if (scale <= 0f && _localScale > 0f)
            {
                SetLocalScale(0f);
                Debug.Log("[BodyScaleState] Sunucu gövde ölçeğini sıfırladı — yeniden ölçüm gerekiyor.");
            }

            if (Mathf.Abs(ServerScale - scale) < ScaleEpsilon)
            {
                return;
            }

            ServerScale = scale;
            Raise();
        }

        private static void Raise()
        {
            Changed?.Invoke();
        }

        /// <summary>Etkin rig'i bulur; arama kısılır (admin gözlemcide rig hiç gelmez —
        /// <see cref="LocalBodyAvatar.ResolveRig"/> ile aynı gerekçe).</summary>
        private OVRCameraRig ResolveRig()
        {
            if (_rig != null && _rig.isActiveAndEnabled)
            {
                return _rig;
            }

            if (Time.unscaledTime - _rigSearchTime < RigSearchIntervalSeconds)
            {
                return null;
            }

            _rigSearchTime = Time.unscaledTime;
            _rig = FindFirstObjectByType<OVRCameraRig>();
            return _rig;
        }
    }
}
