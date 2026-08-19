using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Silah ele geldiğinde <b>çözülerek belirme</b> geçişi oynatır: silahın modeli geçici olarak
    /// çözülme materyaline çevrilir, <c>_Dissolve</c> 1→0 sürülür, sonra özgün materyaller geri
    /// konur. Bırakışta efekt yoktur — silah eskisi gibi anında gider.
    /// <para>
    /// <b>Kapı <see cref="Weapon.HeldChanged"/>'dir</b>, çağrı noktaları değil: silahı ele alan üç
    /// ayrı yol var (<see cref="WeaponGranter"/>'ın rastgele verdiği silah, çerçeveden çağrılan
    /// kalıcı klon, ISDK ile doğrudan kavrama) ve her birine ayrı ayrı efekt eklemek, yeni bir yol
    /// açıldığında sessizce unutulacak bir adım demekti. <see cref="WeaponFrame"/> aynı olayı aynı
    /// sebeple dinliyor.
    /// </para>
    /// <para>
    /// ⚠️ <b>Silahın kendi görünümü korunur:</b> geçiş boyunca özgün materyalin albedo dokusu ve
    /// rengi <see cref="MaterialPropertyBlock"/> ile çözülme materyaline taşınır. Taşınmasaydı
    /// silah çözülürken düz renkli bir siluete dönerdi — çözülme materyali TEK bir asset ve hangi
    /// silaha takıldığını bilmiyor.
    /// </para>
    /// <para>
    /// ⚠️ <b>Efektin görünümü materyalde ayarlanır, burada değil</b> (kenar rengi/kalınlığı, desen
    /// sıklığı, çözülme ekseni…). Bileşen yalnız <c>_Dissolve</c>'u sürer ve albedoyu taşır;
    /// materyalin geri kalanına dokunmaz — aynı ayarın iki yerde durması sapma üretirdi.
    /// </para>
    /// <para>
    /// ⚠️ <b>Silahın üstündeki dünya-uzayı panelleri (Canvas) çözülMEZ, SÖNÜMLENİR.</b> UI'ı
    /// <c>CanvasRenderer</c> çizer: <see cref="Renderer"/>'dan türemez (yani aşağıdaki tarama onu
    /// zaten hiç görmez), <see cref="MaterialPropertyBlock"/> kabul etmez ve çözülme materyali URP
    /// Lit hedeflidir — TMP'nin SDF mesh'ine takılırsa yazı bozulur. Panel bu yüzden ayrı bir
    /// kanaldan, <see cref="CanvasGroup"/> alfasıyla sürülür; zamanlama gövdeyle ORTAKtır.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Weapon))]
    public class SimpleWeaponDissolve : MonoBehaviour
    {
        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

        // Albedo iki isimden okunur: URP/Lit `_BaseMap` yazar, eski Standard/mobil shader'lar
        // `_MainTex`. Silah paketinin materyali ikisini de taşıyor; hangisi doluysa o kullanılır.
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        // Panelin belirmeye BAŞLADIĞI ilerleme oranı: geçişin ilk %35'inde görünmez, kalanında
        // 0→1 sönümlenir. Gövde daha delik deşikken üstünde okunur bir HUD paneli durmasın diye.
        // ⚠️ Serialize alan DEĞİL: `appearSeconds` gibi ikinci bir damgalama noktası açardı
        // (WeaponKitBuilder her koşuda geri yazıyor) — panelin ayarı gövdenin süresine bağlıdır.
        private const float UiFadeStart01 = 0.35f;

        // Materyal atanmamışsa uyarı OTURUM başına bir kez: eksikse her silahta eksiktir
        // (hepsi aynı prefab kitinden geliyor), örnek başına loglamak aynı satırı çoğaltırdı.
        private static bool _warnedNoMaterial;

        [Header("Materyal")]
        [Tooltip("Geçiş boyunca modele takılan çözülme materyali — Assets/_Shared/Materials/ " +
                 "altında DissolveEffect.mat (yumuşak lekeler) ya da VoronoiDissolveEffect.mat " +
                 "(hücresel). Efektin GÖRÜNÜMÜ bu materyalde ayarlanır.")]
        [SerializeField] private Material dissolveMaterial;

        [Header("Zamanlama")]
        [Tooltip("Silahın tamamen belirme süresi (sn). ⚠️ Kalıcı değer WeaponKitBuilder'dadır — " +
                 "araç her koşuda buraya geri yazar; burada yapılan değişiklik yalnız denemeliktir.")]
        [SerializeField] private float appearSeconds = 1.2f;

        private Weapon _weapon;
        private Coroutine _routine;
        private bool _swapped;

        private readonly List<Target> _targets = new List<Target>();

        /// <summary>Silahın üstündeki dünya-uzayı panellerinin alfa kolu. Hedef olarak
        /// <see cref="CanvasGroup"/> seçilmesinin sebebi HİYERARŞİK olması: TMP çalışma anında
        /// alt-mesh doğurabiliyor (yedek font/sprite) ve grafik başına toplanan bir liste o
        /// düğümleri kaçırıp tam opak bırakırdı.</summary>
        private readonly List<CanvasGroup> _canvasGroups = new List<CanvasGroup>();

        /// <summary>Efektin dokunduğu tek bir Renderer ve onu eski hâline döndürmek için gereken
        /// her şey. Property block Renderer BAŞINA tutulur: albedo dokusu silahtan silaha değil,
        /// aynı silahın parçaları arasında bile değişiyor (gövde ile dürbün camı ayrı materyal).</summary>
        private sealed class Target
        {
            public Renderer Renderer;
            public Material[] OriginalMaterials;
            public Material[] DissolveMaterials;
            public MaterialPropertyBlock Block;
        }

        private void Awake()
        {
            _weapon = GetComponent<Weapon>();
            CollectTargets();
        }

        private void OnEnable()
        {
            _weapon.HeldChanged += HandleHeldChanged;
        }

        private void OnDisable()
        {
            _weapon.HeldChanged -= HandleHeldChanged;
            _routine = null;

            // ⚠️ Obje kapanınca coroutine ÖLÜR (çerçeve klonu bırakılınca gizleniyor). Materyali
            // geri koymazsak silah bir dahaki çağrılışında YARI ÇÖZÜLMÜŞ hâlde belirir; ayrıca
            // property block'lu renderer SRP Batcher'a giremediği için maliyeti de sürerdi.
            Restore();
        }

        /// <summary>
        /// Efektin uygulanacağı Renderer'ları bir kez toplar.
        /// <para>
        /// Yalnız <b>katı gövde</b> alınır: namlu alevi/duman (<see cref="ParticleSystemRenderer"/>)
        /// ve nişan ışını (<see cref="LineRenderer"/>) kendi materyalleriyle çizilir — çözülme
        /// materyaline çevrilirlerse efekt sırasında kaybolur ya da bozuk çizilirler.
        /// </para>
        /// <para>
        /// <see cref="WeaponFrame"/>'in alt ağacı da atlanır: çerçeve sahnede duran KAYNAK silaha
        /// aittir ve silah tutulduğunda zaten kapanıyor (klonda ise hiç yok).
        /// </para>
        /// </summary>
        private void CollectTargets()
        {
            var frame = GetComponentInChildren<WeaponFrame>(true);
            Renderer[] all = GetComponentsInChildren<Renderer>(true);

            for (int i = 0; i < all.Length; i++)
            {
                Renderer renderer = all[i];
                if (renderer == null || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer))
                {
                    continue;
                }

                if (frame != null && renderer.transform.IsChildOf(frame.transform))
                {
                    continue;
                }

                _targets.Add(new Target
                {
                    Renderer = renderer,
                    // sharedMaterials her çağrıda YENİ dizi döndürür — sakladığımız kopya güvenli.
                    OriginalMaterials = renderer.sharedMaterials,
                    Block = new MaterialPropertyBlock(),
                });
            }

            CollectCanvasGroups(frame);
        }

        /// <summary>
        /// Silahın üstündeki panellerin (<c>AmmoCanvas</c>) alfa kolunu toplar; eksikse
        /// <see cref="CanvasGroup"/>'u çalışma anında EKLER.
        /// <para>
        /// ⚠️ Bileşen prefaba elle konmaz ve <c>WeaponKitBuilder</c>'da bir adım açılmaz: yeni bir
        /// silah eklendiğinde sessizce unutulacak bir insan adımı doğardı — panel o silahta tek
        /// başına anında belirir ve kimse fark etmezdi. Ekleme burada, tek yerde yaşıyor.
        /// </para>
        /// <para><see cref="WeaponFrame"/>'in alt ağacı Renderer tarafındaki kuralla aynı sebeple
        /// atlanır: çerçeve KAYNAK silaha aittir, klonda hiç yoktur.</para>
        /// </summary>
        private void CollectCanvasGroups(WeaponFrame frame)
        {
            Canvas[] canvases = GetComponentsInChildren<Canvas>(true);

            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null)
                {
                    continue;
                }

                if (frame != null && canvas.transform.IsChildOf(frame.transform))
                {
                    continue;
                }

                var group = canvas.GetComponent<CanvasGroup>();
                if (group == null)
                {
                    group = canvas.gameObject.AddComponent<CanvasGroup>();
                }

                _canvasGroups.Add(group);
            }
        }

        private void HandleHeldChanged(bool held)
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            if (!held)
            {
                // Elden çıktı: silah anında gider, geçiş yok. Yine de özgün materyaline dönsün —
                // bir sonraki çağrılışa temiz girsin.
                Restore();
                return;
            }

            if (dissolveMaterial == null)
            {
                WarnNoMaterial();
                return;
            }

            _routine = StartCoroutine(Appear());
        }

        /// <summary>Silahı yoktan var eder: <c>_Dissolve</c> 1 → 0.</summary>
        private IEnumerator Appear()
        {
            Swap();

            float elapsed = 0f;
            while (elapsed < appearSeconds)
            {
                elapsed += Time.deltaTime;

                // SmoothStep: yavaş başlayıp yavaş biten geçiş. Lineer sürüş VR'da "makine gibi"
                // görünüyor, silahın belirmesi bir jest gibi durmalı.
                float k = Mathf.SmoothStep(0f, 1f, elapsed / appearSeconds);
                SetDissolve(1f - k);
                SetUiAlpha(Mathf.InverseLerp(UiFadeStart01, 1f, k));
                yield return null;
            }

            SetDissolve(0f);
            Restore();
            _routine = null;
        }

        /// <summary>Modeli çözülme materyaline çevirir ve her Renderer'ın property block'unu
        /// KENDİ özgün görünümüyle doldurur (albedo + renk).</summary>
        private void Swap()
        {
            if (_swapped)
            {
                return;
            }

            for (int i = 0; i < _targets.Count; i++)
            {
                Target target = _targets[i];
                if (target.Renderer == null)
                {
                    continue;
                }

                Material source = target.OriginalMaterials.Length > 0 ? target.OriginalMaterials[0] : null;

                target.Block.Clear();
                WriteAppearance(target.Block, source);
                target.Block.SetFloat(DissolveId, 1f); // ilk kare TAM görünmesin
                target.Renderer.SetPropertyBlock(target.Block);

                target.Renderer.sharedMaterials = GetDissolveMaterials(target);
            }

            SetUiAlpha(0f); // panel de ilk kare TAM görünmesin
            _swapped = true;
        }

        /// <summary>Özgün materyalleri geri koyar ve property block'u temizler.</summary>
        private void Restore()
        {
            if (!_swapped)
            {
                return;
            }

            for (int i = 0; i < _targets.Count; i++)
            {
                Target target = _targets[i];
                if (target.Renderer == null)
                {
                    continue;
                }

                target.Renderer.SetPropertyBlock(null);
                target.Renderer.sharedMaterials = target.OriginalMaterials;
            }

            // ⚠️ Alfa da geri konur: geçiş yarıda kesilirse (obje kapandı, silah bırakıldı) panel
            // yarı şeffaf DONARDI ve bir dahaki çağrılışta öyle gelirdi — materyaldeki tuzağın
            // birebir aynısı.
            SetUiAlpha(1f);

            _swapped = false;
        }

        private void SetDissolve(float value)
        {
            for (int i = 0; i < _targets.Count; i++)
            {
                Target target = _targets[i];
                if (target.Renderer == null)
                {
                    continue;
                }

                target.Block.SetFloat(DissolveId, value);
                target.Renderer.SetPropertyBlock(target.Block);
            }
        }

        /// <summary>Panellerin görünürlüğünü sürer (0 = yok, 1 = tam).</summary>
        private void SetUiAlpha(float value)
        {
            for (int i = 0; i < _canvasGroups.Count; i++)
            {
                CanvasGroup group = _canvasGroups[i];
                if (group == null)
                {
                    continue;
                }

                group.alpha = value;
            }
        }

        /// <summary>
        /// Renderer'ın slot sayısı kadar çözülme materyali taşıyan diziyi döndürür (ilk çağrıda
        /// kurulur, sonra yeniden kullanılır — her tutuşta çöp üretmesin).
        /// <para>
        /// ⚠️ Bu dizi <c>.sharedMaterials</c>'a yazılır, <c>.materials</c>'a DEĞİL:
        /// <c>.materials</c> her Renderer için materyal KOPYASI üretir ve o kopyalar hiç toplanmaz
        /// (sızıntı). Çözülme materyali tek asset olarak paylaşılır; silaha özgü olan her şey
        /// property block'ta yaşıyor.
        /// </para>
        /// </summary>
        private Material[] GetDissolveMaterials(Target target)
        {
            if (target.DissolveMaterials == null)
            {
                target.DissolveMaterials = new Material[target.OriginalMaterials.Length];
            }

            for (int i = 0; i < target.DissolveMaterials.Length; i++)
            {
                target.DissolveMaterials[i] = dissolveMaterial;
            }

            return target.DissolveMaterials;
        }

        /// <summary>
        /// Özgün materyalin görünümünü çözülme materyaline taşır.
        /// <para>Doku bulunamazsa hiç yazılmaz (block'a <c>null</c> texture yazmak istisna atar) —
        /// o parça düz renk çözülür, efekt yine çalışır.</para>
        /// </summary>
        private static void WriteAppearance(MaterialPropertyBlock block, Material source)
        {
            if (source == null)
            {
                return;
            }

            Texture albedo = ReadTexture(source, BaseMapId) ?? ReadTexture(source, MainTexId);
            if (albedo != null)
            {
                block.SetTexture(BaseMapId, albedo);
            }

            block.SetColor(BaseColorId, ReadColor(source, BaseColorId, ReadColor(source, ColorId, Color.white)));
        }

        private static Texture ReadTexture(Material source, int propertyId)
        {
            return source.HasProperty(propertyId) ? source.GetTexture(propertyId) : null;
        }

        private static Color ReadColor(Material source, int propertyId, Color fallback)
        {
            return source.HasProperty(propertyId) ? source.GetColor(propertyId) : fallback;
        }

        /// <summary>
        /// Materyal atanmamışsa bir kez uyarır. <b>Neden loglanıyor:</b> efekt sessizce hiç
        /// oynamaz ve silah eskisi gibi anında belirir — yani bileşen takılı göründüğü hâlde
        /// hiçbir şey yapmaz, teşhisi pahalı bir durum.
        /// </summary>
        private static void WarnNoMaterial()
        {
            if (_warnedNoMaterial)
            {
                return;
            }

            _warnedNoMaterial = true;
            Debug.LogWarning("[SimpleWeaponDissolve] Çözülme materyali atanmamış — silah efektsiz " +
                             "belirir. WPN_* prefabının kökündeki SimpleWeaponDissolve'a " +
                             "Assets/_Shared/Materials/DissolveEffect.mat bağla.");
        }
    }
}
