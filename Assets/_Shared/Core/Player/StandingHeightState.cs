using UnityEngine;
using VortexArena.Core.Combat;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Oyuncunun <b>ayakta göz yüksekliği</b> (m, zeminden) — gövdeye göre ölçülen jestlerin
    /// paydası. Canlı göz yüksekliğinden farkı budur: bu sayı oyuncu eğilince, çömelince ya da
    /// aşağı bakınca <b>değişmez</b>.
    /// <para>
    /// ⚠️ <b>Neden ayrı bir ölçü:</b> canlı göz yüksekliğini payda yapan bir eşik, ona ulaşmak için
    /// yapılan hareketin kendisiyle birlikte iner. Oyuncu eğildiğinde kafa 40–55 cm düşer, eşik de
    /// onunla düşer ve sarkan kol eşiğin altında kalır — jest oyuncu hiçbir şey yapmadan tetiklenir.
    /// Payda sabitlenince aynı jest her duruşta aynı hareketi ister.
    /// </para>
    /// <para>
    /// ⚠️ <b>Ölçü ÖĞRENİLİR, sorulmaz:</b> tavan anında yükselir, aşağı <see cref="DecayMetersPerSecond"/>
    /// hızıyla çok yavaş sızar. Asimetri bilinçlidir — fazla yüksek bir tahmin jesti yalnız
    /// zorlaştırır (oyuncu elini biraz daha indirir), fazla düşük bir tahmin ise onu kendiliğinden
    /// tetikler. Sızıntı gözlüğü daha kısa biri devraldığında ölçünün yakınsaması içindir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Cihazda SAKLANMAZ</b> (<see cref="BodyScaleState"/>'in aksine): işletme gözlüğü elden
    /// ele geçer, saklanan bir boy bir sonraki oyuncuya yanlış paydayla başlar. Aynı sebeple ölçü
    /// her <c>hello</c>'da sıfırlanır; oyuncu gözlüğü takıp doğrulduğu ilk karede yeniden oturur.
    /// </para>
    /// <para>
    /// Sahnede DURMAZ: kalıcı tekil olarak kendini önyükler (<see cref="BodyScaleState"/> deseni) —
    /// her arenaya elle bir kurulum adımı eklememek için. Rig yoksa (admin gözlemci) hiçbir şey
    /// yapmaz ve <see cref="TryGet"/> <c>false</c> döner: eli/kafası olmayan oturumda "bu oyuncunun
    /// boyu ne" sorusunun doğru cevabı yoktur, uydurmak yerine susar.
    /// </para>
    /// </summary>
    public class StandingHeightState : MonoBehaviour
    {
        /// <summary>Altındaki örnek ölçüye HİÇ girmez: oyuncu yerde/çömelmiş ya da rig henüz
        /// oturmamıştır (rig'in ilk kareleri sıfıra yakın gelir).</summary>
        private const float MinSampleMeters = 0.8f;

        /// <summary>Tahminin aşağı sızma hızı (m/sn) — 3 cm/dk. Yalnız gözlüğü devralan daha kısa
        /// oyuncuya yakınsamak için vardır; bir oyuncunun çömelip kalktığı sürede kayda değer bir
        /// şey kaybettirmeyecek kadar yavaştır.</summary>
        private const float DecayMetersPerSecond = 0.0005f;

        public static StandingHeightState Instance { get; private set; }

        /// <summary>Öğrenilmiş ayakta göz yüksekliği (m); <c>0</c> = henüz ölçülemedi.</summary>
        private float _estimate;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[StandingHeightState]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<StandingHeightState>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Kalıcı tekiliz: obje devre dışı bırakılsa bile olay kaçmasın diye
            // OnEnable/OnDisable yerine Awake/OnDestroy'da abone oluruz (BodyScaleState deseni).
            NetEvents.OnConnected += HandleConnected;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            NetEvents.OnConnected -= HandleConnected;
            Instance = null;
        }

        /// <summary>Yeni oturum = muhtemelen yeni oyuncu: ölçü sıfırlanır, ilk dik karede yeniden
        /// öğrenilir (kalibrasyon ve gövde ölçeğiyle aynı gerekçe).</summary>
        private void HandleConnected(WelcomeMsg msg)
        {
            _estimate = 0f;
        }

        private void Update()
        {
            if (!WeaponGranter.TryResolveEyeAndFloor(out float eyeY, out float floorY))
            {
                return;
            }

            float sample = eyeY - floorY;
            if (sample < MinSampleMeters)
            {
                return;
            }

            // Tek satırda iki yön: örnek tahminden yüksekse tavan ANINDA yükselir, değilse
            // tahmin örneğe doğru yavaşça sızar ama onun altına inmez.
            _estimate = Mathf.Max(sample, _estimate - DecayMetersPerSecond * Time.deltaTime);
        }

        /// <summary>
        /// Ayakta göz yüksekliği (m, zeminden). Henüz hiç geçerli örnek alınmadıysa (rig yok,
        /// oyuncu yerde) <c>false</c> — çağıran jest o karede HİÇ tanınmaz.
        /// </summary>
        public static bool TryGet(out float standingEyeHeight)
        {
            standingEyeHeight = Instance != null ? Instance._estimate : 0f;
            return standingEyeHeight >= MinSampleMeters;
        }
    }
}
