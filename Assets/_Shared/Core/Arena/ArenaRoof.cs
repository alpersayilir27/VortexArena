using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VortexArena.Core.Arena
{
    /// <summary>
    /// Arena çatısı/tavanı işaretçisi: admin gözlemci <b>kuş bakışına</b> geçtiğinde altındaki
    /// geometri gizlenir, POV/serbest kipte geri gelir. Oyuncu tarafında hiçbir etkisi YOKTUR —
    /// bileşen kendiliğinden bir şey yapmaz, yalnız <see cref="ApplyAll"/> çağrıldığında tepki
    /// verir ve onu yalnız admin gözlemci çağırır.
    ///
    /// <para><b>Kurulum (geliştirici için tek adım):</b> çatı hiyerarşisinin KÖKÜNE bu bileşeni
    /// ekle (<c>GameObject &gt; VortexArena &gt; Arena Roof</c>). Altındaki <b>tüm</b> Renderer'lar
    /// çatı sayılır — ayrı ayrı liste tutmak gerekmez. Bileşen eklendiğinde (veya sağ tık →
    /// <i>Çatı katmanını uygula</i>) hepsine <see cref="LayerName"/> katmanı damgalanır: sahne
    /// görünümündeki Layers süzgeciyle "hangisi çatı" tek bakışta görülür. Katman yalnız
    /// GÖRÜNÜRLÜK/AYIKLAMA içindir — davranış Renderer listesinden gelir, damga unutulsa da
    /// gizleme çalışır.</para>
    ///
    /// <para><b>Gizleme yöntemi:</b> <c>MaterialPropertyBlock</c> ile <c>_BaseColor</c> alfası —
    /// paylaşılan malzeme değiştirilmez, örnek başına bir blok yazılır. Tam gizlemede Renderer
    /// KAPATILMAZ, <see cref="ShadowCastingMode.ShadowsOnly"/>'ye alınır: çatı çizilmez ama
    /// gölgesini atmaya devam eder. Kapatılsaydı iç mekân aydınlanır, kuş bakışı düz ve
    /// okunmaz bir aydınlık levhaya dönerdi.</para>
    ///
    /// <para><b>Sahne değişimine dayanıklıdır:</b> son uygulanan alfa statik tutulur ve yeni
    /// yüklenen sahnedeki çatı <c>OnEnable</c>'da onu kendine uygular — admin kuş bakışındayken
    /// yeni arena açıldığında çatı bir kare bile görünmez.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("VortexArena/Arena Roof")]
    public class ArenaRoof : MonoBehaviour
    {
        /// <summary>Çatı geometrisine damgalanan katman adı (ProjectSettings/TagManager.asset).</summary>
        public const string LayerName = "ArenaRoof";

        /// <summary>Bu alfanın altı "tümüyle gizli" sayılır (gölge korunur, çizim durur).</summary>
        private const float HiddenEpsilon = 0.004f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private static readonly List<ArenaRoof> ActiveRoofs = new List<ArenaRoof>();

        /// <summary>Son uygulanan alfa; yeni sahnedeki çatı bunu OnEnable'da devralır.</summary>
        private static float lastAlpha = 1f;

        [Tooltip("Boş bırakılırsa altındaki TÜM Renderer'lar (kapalı olanlar dâhil) çatı sayılır. " +
                 "Yalnız bir alt kümeyi gizlemek istiyorsan burayı doldur.")]
        [SerializeField] private Renderer[] roofRenderers;

        private Renderer[] resolved;
        private ShadowCastingMode[] originalShadows;
        private MaterialPropertyBlock propertyBlock;

        /// <summary>Sahnedeki etkin çatılar (admin arayüzü "çatı var mı" diye buna bakabilir).</summary>
        public static IReadOnlyList<ArenaRoof> Active => ActiveRoofs;

        /// <summary>
        /// Sahnedeki tüm çatılara alfa uygular ve değeri sonraki sahneler için hatırlar.
        /// 1 = normal, 0 = çizilmez (gölge kalır). Aradaki değerler yarı saydam çatı verir —
        /// ⚠️ ama yalnız malzeme <b>Transparent</b> surface type'taysa: Opaque malzemede alfa
        /// yazımının görsel karşılığı yoktur, ara değer 1 gibi davranır (0 yine tam gizler, çünkü
        /// o yol alfadan değil <see cref="ShadowCastingMode.ShadowsOnly"/>'den geçer).
        /// </summary>
        public static void ApplyAll(float alpha)
        {
            lastAlpha = Mathf.Clamp01(alpha);
            for (int i = 0; i < ActiveRoofs.Count; i++)
            {
                ActiveRoofs[i].SetAlpha(lastAlpha);
            }
        }

        private void Awake()
        {
            Resolve();
        }

        private void OnEnable()
        {
            if (!ActiveRoofs.Contains(this))
            {
                ActiveRoofs.Add(this);
            }

            // Sahne kuş bakışındayken yüklendiyse çatı hiç görünmeden gizli başlar.
            SetAlpha(lastAlpha);
        }

        private void OnDisable()
        {
            ActiveRoofs.Remove(this);
        }

        /// <summary>1 = normal, 0 = çizilmez (gölge korunur).</summary>
        public void SetAlpha(float alpha)
        {
            if (resolved == null)
            {
                Resolve();
            }

            alpha = Mathf.Clamp01(alpha);
            bool hidden = alpha < HiddenEpsilon;
            propertyBlock ??= new MaterialPropertyBlock();

            for (int i = 0; i < resolved.Length; i++)
            {
                Renderer target = resolved[i];
                if (target == null)
                {
                    continue;
                }

                // Gizli: çizme ama gölgeyi bırak. Görünür: özgün gölge kipine dön.
                target.shadowCastingMode = hidden ? ShadowCastingMode.ShadowsOnly : originalShadows[i];

                if (hidden)
                {
                    continue; // ShadowsOnly'de renk yazmanın anlamı yok
                }

                target.GetPropertyBlock(propertyBlock);
                Color color = target.sharedMaterial != null && target.sharedMaterial.HasProperty(BaseColorId)
                    ? target.sharedMaterial.GetColor(BaseColorId)
                    : Color.white;
                color.a = alpha;
                propertyBlock.SetColor(BaseColorId, color);
                target.SetPropertyBlock(propertyBlock);
            }
        }

        /// <summary>Renderer listesini ve özgün gölge kiplerini çözer (elle liste > çocuklar).</summary>
        private void Resolve()
        {
            resolved = roofRenderers != null && roofRenderers.Length > 0
                ? roofRenderers
                : GetComponentsInChildren<Renderer>(true);

            originalShadows = new ShadowCastingMode[resolved.Length];
            for (int i = 0; i < resolved.Length; i++)
            {
                originalShadows[i] = resolved[i] != null ? resolved[i].shadowCastingMode : ShadowCastingMode.On;
            }
        }

#if UNITY_EDITOR
        /// <summary>Bileşen ilk eklendiğinde katmanı damgala — geliştiriciye ek adım kalmasın.</summary>
        private void Reset()
        {
            StampLayer();
        }

        /// <summary>
        /// Çatı geometrisine <see cref="LayerName"/> katmanını damgalar. Sonradan yeni mesh
        /// eklendiğinde bileşene sağ tıklayıp tekrar çalıştır. Katman yalnız ayıklama/süzme
        /// içindir; gizleme davranışı buna bağlı DEĞİLDİR.
        /// </summary>
        [ContextMenu("Çatı katmanını uygula")]
        public void StampLayer()
        {
            int layer = LayerMask.NameToLayer(LayerName);
            if (layer < 0)
            {
                Debug.LogWarning($"[ArenaRoof] '{LayerName}' katmanı yok — " +
                                 "ProjectSettings > Tags and Layers'a ekleyin. Gizleme yine çalışır.", this);
                return;
            }

            Resolve();
            UnityEditor.Undo.RecordObject(gameObject, "Çatı katmanı");
            gameObject.layer = layer;
            for (int i = 0; i < resolved.Length; i++)
            {
                if (resolved[i] == null || resolved[i].gameObject.layer == layer)
                {
                    continue;
                }

                UnityEditor.Undo.RecordObject(resolved[i].gameObject, "Çatı katmanı");
                resolved[i].gameObject.layer = layer;
            }
        }
#endif
    }
}
