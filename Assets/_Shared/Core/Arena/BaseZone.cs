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
    /// ⚠️ Bölge bir konum değişimi TETİKLEMEZ: oyuncu fiziksel olarak yürür, ışınlanmaz.
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

        private void Update()
        {
            if (!ResolveHead())
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

        /// <summary>
        /// HMD transformu; bulunana kadar HER KAREDE yeniden denenir.
        /// <para>
        /// ⚠️ <b>Tek seferlik çözme (eski hâli: <c>Awake</c>) yetmez ve sessizce ölür:</b>
        /// <see cref="Camera.main"/> yalnız <b>etkin</b> ve <c>MainCamera</c> etiketli bir kamera
        /// kayda girdikten sonra dolu döner; rig'in <c>CenterEyeAnchor</c> kamerası bu bileşenin
        /// <c>Awake</c>'inden SONRA kaydolursa alan kalıcı olarak <c>null</c> kalır. O hâlde
        /// <see cref="IsPlayerInside"/> ömür boyu <c>false</c>'ta donar ama
        /// <c>PlayerCombatState.HasOpenBaseZone</c> <b>true</b> kalır (bileşen açık) — yani
        /// "bölge yok" fail-open'ı da devreye girmez: oyuncu şeridin tam üstünde dururken hem
        /// canlanamaz hem tur toplanmasında hazır sayılmaz.
        /// </para>
        /// <para>Aynı desen <c>PlayerCombatState.ResolveHead</c>'de de kullanılıyor. Alan Inspector'da
        /// doluysa hiç aranmaz — <c>ArenaBoundary</c>'de olduğu gibi elle bağlanabilir.</para>
        /// </summary>
        private bool ResolveHead()
        {
            if (head != null)
                return true;

            Camera cam = Camera.main;
            if (cam == null)
                return false;

            head = cam.transform;
            return true;
        }
    }
}
