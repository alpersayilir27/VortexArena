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
    /// <b>Algılama alanı ÇİZİLEN ŞERİTTİR</b>: bölgenin altındaki Renderer'ların kapladığı
    /// dikdörtgen, bölgenin YEREL uzayında ölçülür ve sınır odur. Elle girilen bir ölçü alanı
    /// YOKTUR ve eklenmez — bölgeyi büyütmek/döndürmek/taşımak = şeridi büyütmek/döndürmek/
    /// taşımak. Sayı alanı olsaydı görselle sessizce sapardı: oyuncu kırmızıya basmış görünürken
    /// canlanamaz (ya da şeridin yanında dururken canlanır) olurdu ve hata hiçbir yerde görünmezdi.
    /// </para>
    /// <para>
    /// ⚠️ <b>Yükseklik yok sayılır</b> (yalnız XZ): oyuncunun HMD'si şeridin metrelerce üstündedir,
    /// şeridin kendi kalınlığıyla karşılaştırılamaz. Ölçü <b>bir kez</b>, <see cref="Awake"/>'te
    /// alınır — şerit sahnede statiktir; kapalı (gizlenmiş) Renderer'lar da sayılır, çünkü
    /// <see cref="BaseZoneVisibility"/> takımsız modda şeridi kapatır.
    /// </para>
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

        public UnityEvent onPlayerEntered;
        public UnityEvent onPlayerExited;

        /// <summary>Bu bölgeyi kimin kullanabildiği; <see cref="Team.Neutral"/> = herkes.</summary>
        public Team Team => team;

        /// <summary>Yerel oyuncunun HMD'si bölgenin içinde mi (bileşen kapalıyken DONAR).</summary>
        public bool IsPlayerInside { get; private set; }

        /// <summary>Şeritten ölçülen dikdörtgenin köşeleri, bölgenin YEREL uzayında (x, z).
        /// Şerit bölgenin pivotuna göre kaymış olabilir — bu yüzden merkez varsayılmaz.</summary>
        private Vector2 _areaMin;
        private Vector2 _areaMax;

        private void Awake()
        {
            if (TryMeasureStrip(out _areaMin, out _areaMax))
            {
                return;
            }

            // Açık başarısızlık: ölçüsüz bölge sessizce "hiç girilemeyen" bir bölge olurdu ve
            // oyuncu şeridin üstünde dururken canlanamazdı. Bileşen kapatılınca PlayerCombatState
            // bunu "açık taban yok" diye okur ve kendi fail-open'ı devreye girer (§10.4).
            Debug.LogError(
                $"BaseZone ({name}): altında Renderer yok — algılama alanı ÇİZİLEN ŞERİTTEN " +
                "türer, ölçü alınamadı. Bölge kapatıldı; şerit mesh'ini bölgenin altına koy.", this);
            enabled = false;
        }

        private void Update()
        {
            if (!ResolveHead())
                return;

            Vector3 local = transform.InverseTransformPoint(head.position);
            bool inside = local.x >= _areaMin.x && local.x <= _areaMax.x &&
                          local.z >= _areaMin.y && local.z <= _areaMax.y;
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
        /// Şeridin kapladığı dikdörtgeni bölgenin yerel XZ'sinde ölçer.
        /// <para>
        /// ⚠️ <c>Renderer.bounds</c> (dünya eksenli AABB) KULLANILMAZ: döndürülmüş bir şeritte
        /// kutu şişer ve bölge şeridin dışına taşar. Ölçü, her Renderer'ın <b>kendi</b> yerel
        /// kutusunun sekiz köşesini dünyaya, oradan bölgenin yerel uzayına taşıyarak alınır —
        /// döndürülmüş şerit de, bölgeye göre eğik duran şerit de doğru çıkar.
        /// </para>
        /// </summary>
        private bool TryMeasureStrip(out Vector2 min, out Vector2 max)
        {
            min = Vector2.zero;
            max = Vector2.zero;

            // Kapalı olanlar dahil: takımsız modda şeridi BaseZoneVisibility kapatıyor ve bu
            // bileşen sahne yüklenirken ondan sonra da uyanabilir.
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            bool measured = false;

            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                {
                    continue;
                }

                Bounds local = renderer.localBounds;
                Vector3 center = local.center;
                Vector3 extents = local.extents;
                Transform space = renderer.transform;

                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = new Vector3(
                        (corner & 1) == 0 ? -extents.x : extents.x,
                        (corner & 2) == 0 ? -extents.y : extents.y,
                        (corner & 4) == 0 ? -extents.z : extents.z);

                    Vector3 point = transform.InverseTransformPoint(space.TransformPoint(center + offset));

                    if (!measured)
                    {
                        min = new Vector2(point.x, point.z);
                        max = min;
                        measured = true;
                        continue;
                    }

                    min.x = Mathf.Min(min.x, point.x);
                    min.y = Mathf.Min(min.y, point.z);
                    max.x = Mathf.Max(max.x, point.x);
                    max.y = Mathf.Max(max.y, point.z);
                }
            }

            return measured;
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

#if UNITY_EDITOR
        /// <summary>Seçiliyken algılama dikdörtgenini çizer — ölçü artık görselden geldiği için
        /// tek denetim yolu gözle bakmaktır. Play beklemeden tazedir: her çizimde yeniden ölçülür.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            if (!TryMeasureStrip(out Vector2 min, out Vector2 max))
            {
                return;
            }

            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = team == Team.Blue ? Color.cyan : team == Team.Red ? Color.red : Color.white;

            var a = new Vector3(min.x, 0f, min.y);
            var b = new Vector3(max.x, 0f, min.y);
            var c = new Vector3(max.x, 0f, max.y);
            var d = new Vector3(min.x, 0f, max.y);

            Gizmos.DrawLine(a, b);
            Gizmos.DrawLine(b, c);
            Gizmos.DrawLine(c, d);
            Gizmos.DrawLine(d, a);
        }
#endif
    }
}
