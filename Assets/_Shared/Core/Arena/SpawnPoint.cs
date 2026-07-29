using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Sahne marker'ı: arenanın <b>tek</b> başlangıç noktası — maç öncesi operatörün oyuncuyu
    /// yönlendirdiği fiziksel yer. Takımı ve slotu YOKTUR (arena başına bir tane).
    /// <para>
    /// ⚠️ FREE-ROAM: oyuncu fiziksel olarak yürür, IŞINLANMAZ. Bu marker yalnız GÖSTERGEDİR;
    /// hiçbir kod rig'i, kamerayı ya da oyuncuyu buraya taşımaz — ne maç başında, ne canlanmada,
    /// ne de harita değişiminde. <b>Harita değişimi kalibrasyonu da sıfırlamaz</b>: yeni sahnenin
    /// <see cref="ArenaCalibrator"/>'ı kayıtlı spatial anchor'dan hizalamayı geri yükler
    /// (Docs/ArenaNet-Protokol.md §10.4).
    /// </para>
    /// <para>
    /// Ölünce dönülecek yer bu DEĞİLDİR — o <see cref="BaseZone"/>'dur (taban bölgesi).
    /// </para>
    /// <para>
    /// ⚠️ <b>Bu marker aynı zamanda arena uzayının SIFIRIDIR</b> (<see cref="ArenaSpace"/> origin'i):
    /// ağa giden/gelen tüm pozlar bu transforma göre çevrilir. Yerini (ya da dönüşünü) değiştirmek
    /// arenadaki TÜM oyuncuların koordinatını kaydırır — bir kez yerleştirilir ve bırakılır.
    /// Origin bilinçli olarak <see cref="ArenaBoundary"/>'de DEĞİLDİR: muhafaza duvarını
    /// büyütmek/kaydırmak ağ uzayını bozmasın diye ikisi ayrıldı.
    /// </para>
    /// <para>
    /// Sahneye <c>GameObject &gt; VortexArena &gt; Spawn Point</c> ile eklenir ve ELLE
    /// yerleştirilir. Kayıt <see cref="OnEnable"/>/<see cref="OnDisable"/> ile yapılır; sahne
    /// değişiminde statik liste kendiliğinden boşalır (sızıntı yok).
    /// </para>
    /// </summary>
    public class SpawnPoint : MonoBehaviour
    {
        private static readonly List<SpawnPoint> Registry = new List<SpawnPoint>();

        /// <summary>Sahnedeki başlangıç noktası; hiç yoksa <c>null</c>. Birden çok varsa ilk
        /// kaydolan döner (fazlalık <see cref="OnEnable"/>'da uyarı basar).
        /// <para>⚠️ Kayıt <c>OnEnable</c>'da dolar, yani <b>yalnız Play kipinde</b> geçerlidir —
        /// editör aracı yazarken sahneyi <c>FindObjectsByType</c> ile tara.</para></summary>
        public static SpawnPoint Current => Registry.Count > 0 ? Registry[0] : null;

        /// <summary>Play kipinde aktif olan tüm noktalar (salt okunur). Normalde 0 ya da 1 öğe.</summary>
        public static IReadOnlyList<SpawnPoint> All => Registry;

        private void OnEnable()
        {
            if (Registry.Contains(this))
            {
                return;
            }

            Registry.Add(this);

            // Sessizce ikinci nokta kabul edilmez: "hangisi geçerli" sorusu sahnede görünür olsun.
            if (Registry.Count > 1)
            {
                Debug.LogWarning(
                    $"[SpawnPoint] Sahnede {Registry.Count} başlangıç noktası var — arena başına " +
                    "TEK nokta beklenir. Fazlalıkları sil.", this);
            }

            // Origin yalnız GEÇERLİ nokta için kaydedilir: fazlalık bir marker ağ uzayını
            // sessizce kendine çekmesin (hangisi olduğu Current ile aynı cevap olsun).
            SyncOrigin();
        }

        private void OnDisable()
        {
            Registry.Remove(this);
            ArenaSpace.ClearOrigin(transform);

            // Geçerli nokta gittiyse sıradaki devralır; hiç kalmadıysa origin boş kalır.
            SyncOrigin();
        }

        /// <summary>Arena uzayı origin'ini <see cref="Current"/> ile hizalar (yoksa dokunmaz).</summary>
        private static void SyncOrigin()
        {
            SpawnPoint current = Current;
            if (current != null)
            {
                ArenaSpace.SetOrigin(current.transform);
            }
        }

        // ------------------------------------------------------------------ gizmo

        /// <summary>Editörde yerleştirmeyi kolaylaştırır: küre + bakış oku.</summary>
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.25f, 0.85f, 0.35f, 0.9f);

            Vector3 origin = transform.position + Vector3.up * 0.05f;
            Gizmos.DrawSphere(origin, 0.12f);

            Vector3 forward = transform.forward;
            Vector3 tip = origin + forward * 0.6f;
            Gizmos.DrawLine(origin, tip);
            Gizmos.DrawLine(tip, tip + Quaternion.AngleAxis(150f, Vector3.up) * forward * 0.18f);
            Gizmos.DrawLine(tip, tip + Quaternion.AngleAxis(-150f, Vector3.up) * forward * 0.18f);
        }
    }
}
