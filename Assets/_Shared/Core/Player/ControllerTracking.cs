using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// "Şu an bu elin anchor pozuna güvenilir mi" sorusunun <b>tek</b> cevabı + operatöre
    /// bildirilecek kumanda durumu (<c>ArenaProtocol.CONTROLLER_*</c>).
    /// <para>
    /// ⚠️ Varlık sebebi <c>OVRCameraRig.UpdateAnchors</c>'ın el anchor'larını <b>koşulsuz</b>
    /// yazmasıdır: göz anchor'larının aksine geçerlilik sorulmaz, doğrudan
    /// <c>OVRInput.GetLocalControllerPosition(aktif kumanda)</c> yazılır. Kumandanın pili bitince
    /// aktif kumanda <see cref="OVRInput.Controller.None"/> olur, okuma <c>(0,0,0)</c> döner ve el
    /// anchor'ı rig orijinine — oyuncunun ayağının dibine — sıçrar. O sıfır iki kanalı birden
    /// zehirler: hem <c>0x01 PoseUpdate</c> anchor'ı doğrudan okur, hem body tracking çözümü o eli
    /// hedef alıp iskeleti çökertir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Geçerlilik ölçütü rig'in yaptığının AYNASIDIR</b> ve başka bir ölçüte (pil, bağlı
    /// kumanda listesi, poz sıçraması) dayandırılmaz: anchor'ı hangi kaynak yazıyorsa geçerliliği
    /// de o kaynak söyler. Ayrı bir ölçüt ikinci bir doğruluk kaynağı olur ve iki taraf saptığında
    /// el ya boşuna dondurulur ya sıfır poz sızar. Aynı sebeple <b>el izleme (LHand/RHand) meşru
    /// bir kaynaktır</b> — kumanda yok diye geçersiz sayılmaz; rig hangisini seçiyorsa o.
    /// </para>
    /// </summary>
    public static class ControllerTracking
    {
        /// <summary>
        /// <see cref="GetState"/>'in bir değişimi bildirmeden önce beklediği kararlılık süresi.
        /// Durum operatör göstergesine gidiyor: izleme saniyede birkaç kez kırpışabildiği için
        /// filtresiz bir alan roster yayınını sürekli tetiklerdi (kesikli alan = yayın tetikleyici).
        /// </summary>
        private const float StateSettleSeconds = 1f;

        /// <summary>Sol el = 0, sağ el = 1 (dizinleme <c>right ? 1 : 0</c>).</summary>
        private static readonly HandTrack[] Hands = { new HandTrack(), new HandTrack() };

        private static int _lastTickFrame = -1;

        /// <summary>
        /// Kare başına bir kez örnekleme yapar; <b>idempotenttir</b> — aynı karede kaç kez
        /// çağrılırsa çağrılsın tek örnek alınır.
        /// <para>Her getter da başında bunu çağırır: çağıran <see cref="Tick"/>'i unutsa bile
        /// değerler bayat kalmaz — çağrılmadığı için sessizce yanlış cevap veren bir emniyet,
        /// emniyet değildir.</para>
        /// </summary>
        public static void Tick()
        {
            if (_lastTickFrame == Time.frameCount)
            {
                return;
            }

            _lastTickFrame = Time.frameCount;

            Sample(Hands[0], OVRInput.Handedness.LeftHanded);
            Sample(Hands[1], OVRInput.Handedness.RightHanded);
        }

        /// <summary>
        /// Bu elin anchor pozu <b>BU KARE</b> ölçüm mü (yani okunabilir mi).
        /// <para>⚠️ <b>Debounce YOKTUR ve eklenmez:</b> anchor sıfırlanmasıyla tutmanın devreye
        /// girmesi AYNI karede olmak zorunda — bir karelik gecikme bile ağa (ve body tracking'e)
        /// oyuncunun ayağının dibinde bir el sızdırır. Yumuşatma yalnız
        /// <see cref="GetState"/>'in işidir; o bir gösterge, bu bir kapıdır.</para>
        /// </summary>
        public static bool IsValid(bool right)
        {
            Tick();
            return Hands[right ? 1 : 0].Valid;
        }

        /// <summary>
        /// Operatöre bildirilecek durum: <see cref="ArenaProtocol.CONTROLLER_UNKNOWN"/> /
        /// <c>_OK</c> / <c>_UNTRACKED</c> / <c>_LOST</c>. Değişimler
        /// <see cref="StateSettleSeconds"/> kadar kararlı kaldıktan sonra rapor edilir.
        /// </summary>
        public static int GetState(bool right)
        {
            Tick();
            return Hands[right ? 1 : 0].Reported;
        }

        /// <summary>
        /// ⚠️ Statik durum domain reload kapalıyken Play oturumları arasında YAŞAR: sıfırlanmazsa
        /// bir önceki oturumun "hiç geçerli olmadı" damgası yeni oturuma taşınır.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _lastTickFrame = -1;
            Hands[0].Reset();
            Hands[1].Reset();
        }

        private static void Sample(HandTrack hand, OVRInput.Handedness handedness)
        {
            // Rig yoksa (admin gözlemci, gözlüksüz editör) ölçüm de yoktur: OVRInput'un aktif
            // kumanda tablosunu OVRManager sürüyor, o olmadan her el "kayıp" görünürdü.
            // ⚠️ Bilinmeyen durumda el GEÇERLİ sayılır — tutma bir tahmin üretir ve ölçüm
            // yapılmayan yerde tahmini akışa sokmanın karşılığı yoktur.
            if (OVRManager.instance == null)
            {
                hand.Valid = true;
                hand.Raw = ArenaProtocol.CONTROLLER_UNKNOWN;
                hand.Reported = ArenaProtocol.CONTROLLER_UNKNOWN;
                hand.RawSince = Time.unscaledTime;
                return;
            }

            // Rig'in anchor'ı hangi kaynaktan yazdığının aynası (bkz. sınıf notu).
            OVRInput.Controller active = OVRInput.GetActiveControllerForHand(handedness);

            int raw;
            if (active == OVRInput.Controller.None)
            {
                raw = ArenaProtocol.CONTROLLER_LOST;
            }
            else if (!OVRInput.GetControllerPositionValid(active))
            {
                raw = ArenaProtocol.CONTROLLER_UNTRACKED;
            }
            else
            {
                raw = ArenaProtocol.CONTROLLER_OK;
            }

            hand.Valid = raw == ArenaProtocol.CONTROLLER_OK;

            if (raw != hand.Raw)
            {
                hand.Raw = raw;
                hand.RawSince = Time.unscaledTime;
            }

            if (hand.Reported != raw && Time.unscaledTime - hand.RawSince >= StateSettleSeconds)
            {
                hand.Reported = raw;
            }
        }

        /// <summary>Tek elin örnekleme durumu. Sınıf (struct değil) çünkü dizide yerinde güncellenir.</summary>
        private sealed class HandTrack
        {
            /// <summary>Bu karenin ham geçerliliği — <see cref="IsValid"/> bunu döner.</summary>
            public bool Valid = true;

            /// <summary>Bu karenin ham durumu (debounce öncesi).</summary>
            public int Raw = ArenaProtocol.CONTROLLER_UNKNOWN;

            /// <summary><see cref="Raw"/> hangi andan beri değişmedi.</summary>
            public float RawSince;

            /// <summary>Debounce'tan geçmiş, dışarıya bildirilen durum.</summary>
            public int Reported = ArenaProtocol.CONTROLLER_UNKNOWN;

            public void Reset()
            {
                Valid = true;
                Raw = ArenaProtocol.CONTROLLER_UNKNOWN;
                RawSince = 0f;
                Reported = ArenaProtocol.CONTROLLER_UNKNOWN;
            }
        }
    }
}
