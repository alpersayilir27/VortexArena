using UnityEngine;
using UnityEngine.Events;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// <b>Taban bölgesi</b>: arenanın bir kenarındaki şerit (kırmızı/mavi). Ölen oyuncu
    /// buraya fiziken yürüyünce canlanır — <see cref="ModeReviveAnchor.OwnBase"/> kipinde
    /// <c>PlayerCombatState</c> bu bölgeye girişi <c>revive_request</c>'in şartı olarak okur
    /// (Docs/ArenaNet-Protokol.md §10.4).
    /// <para>
    /// HMD konumunu bölgenin YEREL uzayında izler (fizik yok, <see cref="ArenaBoundary"/> ile
    /// aynı desen) ve oyuncu girip çıkınca olay yayınlar; oyunun büyümesiyle silah tazeleme /
    /// iyileşme gibi davranışlar bu olaylara takılabilir.
    /// </para>
    /// <para>
    /// <see cref="team"/> = bu bölgeyi kimin kullanabildiği. <see cref="Team.Neutral"/> işaretli
    /// bir bölgeyi HERKES kullanır; takımsız modda (§10.5) oyuncu tüm bölgeleri kullanır. Aynı
    /// takıma ait birden çok bölge konabilir — herhangi birine girmek yeter.
    /// </para>
    /// <para>
    /// ⚠️ Maç başlangıç noktası bu DEĞİLDİR — o arena başına tek olan <see cref="SpawnPoint"/>'tir.
    /// İkisi de bir konum değişimi TETİKLEMEZ: oyuncu fiziksel olarak yürür, ışınlanmaz.
    /// </para>
    /// </summary>
    public class BaseZone : MonoBehaviour
    {
        [Tooltip("Bu taban bölgesini kimler kullanabilir. Neutral = herkes.")]
        [SerializeField] private Team team = Team.Red;

        [Header("References")]
        [Tooltip("HMD transform (CenterEyeAnchor). Falls back to Camera.main.")]
        [SerializeField] private Transform head;

        [Header("Zone size (meters)")]
        [Tooltip("Half depth of the strip, along local X.")]
        [SerializeField] private float halfExtentX = 0.5f;
        [Tooltip("Half width of the strip, along local Z.")]
        [SerializeField] private float halfExtentZ = 5f;

        public UnityEvent onPlayerEntered;
        public UnityEvent onPlayerExited;

        /// <summary>Bu bölgeyi kimin kullanabildiği; <see cref="Team.Neutral"/> = herkes.</summary>
        public Team Team => team;

        /// <summary>Yerel oyuncunun HMD'si bölgenin içinde mi (bileşen kapalıyken DONAR).</summary>
        public bool IsPlayerInside { get; private set; }

        private void Awake()
        {
            if (head == null && Camera.main != null)
                head = Camera.main.transform;
        }

        private void Update()
        {
            if (head == null)
                return;

            Vector3 local = transform.InverseTransformPoint(head.position);
            bool inside = Mathf.Abs(local.x) <= halfExtentX && Mathf.Abs(local.z) <= halfExtentZ;
            if (inside == IsPlayerInside)
                return;

            IsPlayerInside = inside;
            Debug.Log($"BaseZone: player {(inside ? "entered" : "left")} {team} base.");
            if (inside)
                onPlayerEntered?.Invoke();
            else
                onPlayerExited?.Invoke();
        }
    }
}
