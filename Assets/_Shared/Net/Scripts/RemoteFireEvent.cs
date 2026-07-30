using UnityEngine;

namespace VortexArena.Net
{
    /// <summary>
    /// Sunucudan relay edilmiş tek bir uzak atış/atma olayı (0x04 EventBatch girdisinin
    /// Unity tarafındaki karşılığı, §6.5). UdpStateChannel ağ thread'inde açar,
    /// NetEvents.OnRemoteFireEvent ile ANA thread'de yayınlanır; sunum tarafı (Core'daki
    /// uzak atış efekti) dinler.
    /// <para>⚠️ Kendi olaylarımız buraya HİÇ gelmez — kanal <c>playerId</c> ile süzer
    /// (atan kendi olayını geri alır ve yok sayar, snapshot'ta kendi pozunu yok saymasıyla
    /// aynı desen, §6.5).</para>
    /// </summary>
    public struct RemoteFireEvent
    {
        /// <summary>Olayı üreten uzak oyuncu.</summary>
        public int playerId;

        /// <summary><c>FireEventEntry.KIND_SHOT</c> (hitscan) veya <c>KIND_THROW</c> (fırlatma).</summary>
        public byte kind;

        /// <summary>Olayın hangi elden çıktığı: true = sağ.</summary>
        public bool rightHand;

        /// <summary>
        /// Olay anındaki eşyanın <c>netItemId</c>'si (§6.6); <b>0 = çözülemedi</b> (el boş
        /// bildirilmiş ya da bayt kaybolmuş). Sunum profilini bu bayt çözer — durum baytları
        /// (snapshot <c>itemL</c>/<c>itemR</c>) kaybolsa da olay kendi kendine yeter.
        /// </summary>
        public byte itemId;

        /// <summary>
        /// Birim nişan yönü, <b>ARENA uzayında</b>. Dünyaya çevirmek çizen tarafın işidir
        /// (Net katmanı arena↔dünya dönüşümünü bilmez).
        /// </summary>
        public Vector3 arenaDirection;

        /// <summary>
        /// ⚠️ <b>Anlamı <see cref="kind"/>'a göre değişir:</b> <c>KIND_SHOT</c>'ta vuruş
        /// <b>mesafesi</b> (metre — tracer'ın nerede biteceği), <c>KIND_THROW</c>'da başlangıç
        /// <b>hızı</b> (m/sn — fırlatılan cismin ilk ivmesi). Telde ikisi de aynı u16 alanda
        /// (cm / cm-sn) taşındığı için tek isimli tek alan; tüketici türe bakmadan
        /// kullanamaz.
        /// </summary>
        public float magnitude;

        /// <summary>
        /// Olayın üretildiği sunucu tik'i.
        /// <para><b>Neden taşınıyor:</b> olay kendi tik'inde, alıcının <b>interpolasyon
        /// saatiyle</b> oynatılmalı — el pozu <c>INTERP_DELAY_MS</c> kadar geriden çizildiği için
        /// tracer'ın "şimdi"de değil o tik'te başlaması gerekir. Bu yüzden 20 Hz batch'leme
        /// algılanan gecikmeye EKLENMEZ: ≤50 ms batch beklemesi 100 ms'lik interp tamponunun
        /// içinde erir (§6.5).</para>
        /// </summary>
        public uint serverTick;
    }
}
