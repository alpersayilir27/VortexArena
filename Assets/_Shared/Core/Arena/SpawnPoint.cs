using System.Collections.Generic;
using UnityEngine;

// Takım enum'u için takma ad: sınıfta aynı adlı bir ÖZELLİK (Team) olduğu için
// enum üyelerine bu alias üzerinden erişilir (isim belirsizliği kalmasın).
using CoreTeam = VortexArena.Core.Team;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Sahne marker'ı: bir takımın <c>slot</c> numaralı başlangıç/canlanma noktası.
    /// <para>
    /// ⚠️ FREE-ROAM: oyuncu fiziksel olarak yürür, IŞINLANMAZ. Bu marker yalnız
    /// GÖSTERGE amaçlıdır ("tabanının şu slotuna dön") — hiçbir kod rig'i buraya taşımaz.
    /// Sunucu <c>load_match.spawnSlot</c> / <c>respawn.spawnSlot</c> gönderir, slot çözümü
    /// istemcide bu marker'lardan yapılır (Docs/ArenaNet-Protokol.md §10.4).
    /// </para>
    /// Kayıt <see cref="OnEnable"/>/<see cref="OnDisable"/> ile yapılır; sahne değişiminde
    /// statik liste kendiliğinden boşalır (sızıntı yok).
    /// </summary>
    public class SpawnPoint : MonoBehaviour
    {
        [SerializeField] private Team team = CoreTeam.Red;
        [Tooltip("Takım içi 0 tabanlı slot numarası (load_match.spawnSlot ile eşleşir).")]
        [SerializeField] private int slot;

        private static readonly List<SpawnPoint> Registry = new List<SpawnPoint>();

        /// <summary>Bu noktanın takımı.</summary>
        public Team Team => team;

        /// <summary>Takım içi 0 tabanlı slot numarası.</summary>
        public int Slot => slot;

        /// <summary>Sahnede aktif olan tüm spawn noktaları (salt okunur).</summary>
        public static IReadOnlyList<SpawnPoint> All => Registry;

        private void OnEnable()
        {
            if (!Registry.Contains(this))
            {
                Registry.Add(this);
            }
        }

        private void OnDisable()
        {
            Registry.Remove(this);
        }

        /// <summary>
        /// Takım + slot ile nokta bulur. Tam eşleşme yoksa aynı takımın EN KÜÇÜK slot'u,
        /// o da yoksa null döner (sahnede o takımın noktası hiç yoksa).
        /// </summary>
        public static SpawnPoint Find(Team team, int slot)
        {
            SpawnPoint fallback = null;

            for (int i = 0; i < Registry.Count; i++)
            {
                SpawnPoint point = Registry[i];
                if (point == null || point.team != team)
                {
                    continue;
                }

                if (point.slot == slot)
                {
                    return point;
                }

                if (fallback == null || point.slot < fallback.slot)
                {
                    fallback = point;
                }
            }

            return fallback;
        }

        // ------------------------------------------------------------------ gizmo

        /// <summary>Editörde yerleştirmeyi kolaylaştırır: takım renginde küre + bakış oku.</summary>
        private void OnDrawGizmos()
        {
            Gizmos.color = team == CoreTeam.Red
                ? new Color(0.85f, 0.20f, 0.20f, 0.9f)
                : new Color(0.20f, 0.40f, 0.90f, 0.9f);

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
