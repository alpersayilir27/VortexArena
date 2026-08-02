using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Silah ele geldiğinde <b>çözülerek belirme</b> geçişi oynatır: silahın modeli geçici olarak
    /// dissolve materyaline çevrilir, <c>_Dissolve</c> 1→0 sürülür, sonra özgün materyaller geri
    /// konur.
    /// <para>
    /// ⚠️ <b>Silahın kendi görünümü korunur:</b> geçiş boyunca özgün materyalin albedo dokusu ve
    /// rengi (<c>_BaseMap</c>/<c>_BaseColor</c>) dissolve materyaline
    /// <see cref="MaterialPropertyBlock"/> ile taşınır. Taşınmasaydı silah çözülürken düz renkli
    /// bir siluete dönerdi — dissolve materyali TEK bir asset ve hangi silaha takıldığını bilmiyor.
    /// </para>
    /// <para>
    /// <b>Kapı <see cref="Weapon.HeldChanged"/>'dir</b>, çağrı noktaları değil: silahı ele alan üç
    /// ayrı yol var (<see cref="WeaponGranter"/>'ın rastgele verdiği silah, çerçeveden çağrılan
    /// kalıcı klon, ISDK ile doğrudan kavrama) ve her birine ayrı ayrı efekt eklemek, yeni bir yol
    /// açıldığında sessizce unutulacak bir adım demekti. <see cref="WeaponFrame"/> aynı olayı aynı
    /// sebeple dinliyor.
    /// </para>
    /// <para>
    /// <b>Bırakışta kaybolmayı bir KOPYA sürdürür</b> (<see cref="WeaponDissolveGhost"/>): silah
    /// bırakıldığı anda yok ediliyor (<c>Destroy</c>) ya da gizleniyor (<c>SetActive(false)</c>),
    /// yani geçişin bu obje üstünde koşacak karesi yok. Kopya silahın bırakıldığı dünya pozunda
    /// kalır ve bağımsız çözülür.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(Weapon))]
    public class WeaponDissolve : MonoBehaviour
    {
        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        private static readonly int EdgeColorId = Shader.PropertyToID("_Edge_Color");
        private static readonly int EdgeWidthId = Shader.PropertyToID("_Edge_Width");
        private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");

        // Albedo iki isimden okunur: URP/Lit `_BaseMap` yazar, eski Standard/mobil shader'lar
        // `_MainTex`. Silah paketinin materyali ikisini de taşıyor; hangisi doluysa o kullanılır.
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        // Materyal atanmamışsa uyarı OTURUM başına bir kez: eksikse her silahta eksiktir
        // (hepsi aynı prefab kitinden geliyor), örnek başına loglamak aynı satırı çoğaltırdı.
        private static bool _warnedNoMaterial;

        [Header("Materyal")]
        [Tooltip("Assets/_Shared/Materials/DissolveEffect.mat — geçiş boyunca modele takılan materyal.")]
        [SerializeField] private Material dissolveMaterial;

        [Header("Zamanlama")]
        [Tooltip("Silahın tamamen belirme süresi (sn). ⚠️ Kalıcı değer WeaponKitBuilder'dadır — " +
                 "araç her koşuda buraya geri yazar; burada yapılan değişiklik yalnız denemeliktir.")]
        [SerializeField] private float appearSeconds = 1.2f;

        [Tooltip("Silah elden çıkarken yerinde kalan kopyanın kaybolma süresi (sn). " +
                 "0 = kaybolma efekti yok, silah eskisi gibi anında gider.")]
        [SerializeField] private float disappearSeconds = 0.9f;

        [Header("Görünüm")]
        [Tooltip("Çözülme kenarının rengi (HDR — bloom varsa parlar).")]
        [SerializeField, ColorUsage(true, true)] private Color edgeColor = new Color(0.4f, 0.85f, 1f, 1f);

        [Tooltip("Kenar bandının kalınlığı. Materyaldeki _Edge_Width'i EZER.")]
        [SerializeField] private float edgeWidth = 0.08f;

        [Tooltip("Çözülme deseninin sıklığı. Materyaldeki _NoiseScale'i EZER. Desen silahın YEREL " +
                 "uzayında üretiliyor (UV'de değil), yani ölçü METRE BAŞINA periyottur: silah ~0.7 m " +
                 "olduğu için 12 gibi bir değer avuç içi kadar iri lekeler verir, ince parçalanma " +
                 "için 60-150 arası gerekir. VoronoiDissolve materyalinde aynı alan HÜCRE " +
                 "YOĞUNLUĞUDUR (30-60 arası iyi başlangıç).")]
        [SerializeField] private float noiseScale = 60f;

        // Uygulama kapanırken kopya ÜRETİLMEZ: OnApplicationQuit tüm OnDisable'lardan önce koşar,
        // bayrak da bu yüzden static (silah örneği başına değil, oturum başına bir gerçek).
        private static bool _quitting;

        private Weapon _weapon;
        private Coroutine _routine;
        private bool _swapped;

        /// <summary>Silah şu an tutuluyor sayılıyor mu — elden çıkışta bir kopya bırakılmalı mı.
        /// <para>Ayrı bir bayrak tutuluyor çünkü çıkışın İKİ yolu var: <c>HeldChanged(false)</c>
        /// (ana yol) ve doğrudan kapanma/yok edilme (<see cref="OnDisable"/>). İkincisinde olay
        /// hiç yayınlanmayabilir — bileşenlerin <c>OnDisable</c> sırası garanti değildir, bizimki
        /// önce koşarsa abonelik zaten kalkmış olur.</para></summary>
        private bool _ghostPending;

        private readonly List<Target> _targets = new List<Target>();

        /// <summary>Efektin dokunduğu tek bir Renderer ve onu eski hâline döndürmek için gereken
        /// her şey. Property block Renderer BAŞINA tutulur: albedo dokusu silahtan silaha değil,
        /// aynı silahın parçaları arasında bile değişiyor (gövde ile dürbün camı ayrı materyal).</summary>
        private sealed class Target
        {
            public Renderer Renderer;
            public Material[] OriginalMaterials;
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

            // Yedek çıkış yolu (bkz. _ghostPending): silah HeldChanged'i yayınlamadan kapandıysa
            // kopyayı buradan bırakırız. Kopya ayrı bir objede yaşadığı için bu objenin
            // kapanıyor olması onu etkilemez.
            if (_ghostPending)
            {
                _ghostPending = false;
                SpawnGhost();
            }

            // ⚠️ Obje kapanınca coroutine ÖLÜR (çerçeve klonu bırakılınca gizleniyor). Materyali
            // geri koymazsak silah bir dahaki çağrılışında YARI ÇÖZÜLMÜŞ hâlde belirir; ayrıca
            // property block'lu renderer SRP Batcher'a giremediği için maliyeti de sürerdi.
            Restore();
        }

        private void OnApplicationQuit()
        {
            _quitting = true;
        }

        /// <summary>
        /// Efektin uygulanacağı Renderer'ları bir kez toplar.
        /// <para>
        /// Yalnız <b>katı gövde</b> alınır: namlu alevi/duman (<see cref="ParticleSystemRenderer"/>)
        /// ve nişan ışını (<see cref="LineRenderer"/>) kendi materyalleriyle çizilir — dissolve
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
        }

        private void HandleHeldChanged(bool held)
        {
            if (dissolveMaterial == null)
            {
                WarnNoMaterial();
                return;
            }

            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            if (held)
            {
                _ghostPending = true;
                _routine = StartCoroutine(Appear());
                return;
            }

            // Elden çıktı: kaybolmayı bir KOPYA devralır (gerekçe: WeaponDissolveGhost). Silahın
            // kendisi hemen özgün materyaline döner — bir sonraki çağrılışa temiz girsin.
            _ghostPending = false;
            SpawnGhost();
            Restore();
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
                yield return null;
            }

            SetDissolve(0f);
            Restore();
            _routine = null;
        }

        /// <summary>Modeli dissolve materyaline çevirir ve her Renderer'ın property block'unu
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

                // ⚠️ `.materials` DEĞİL `.sharedMaterials`: `.materials` her Renderer için materyal
                // KOPYASI üretir ve o kopyalar hiç toplanmaz (sızıntı). Dissolve materyali tek
                // asset olarak paylaşılır; silaha özgü olan her şey property block'ta yaşıyor.
                var swapped = new Material[target.OriginalMaterials.Length];
                for (int m = 0; m < swapped.Length; m++)
                {
                    swapped[m] = dissolveMaterial;
                }

                target.Renderer.sharedMaterials = swapped;
            }

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

        /// <summary>
        /// Silahın o andaki görünümünden tek kullanımlık bir <see cref="WeaponDissolveGhost"/>
        /// üretir: her parçanın mesh'i, dünya pozu ve albedosu kopyalanır — kaybolmayı kopya
        /// sürdürür, silah kendi yoluna gider.
        /// <para>
        /// ⚠️ Yalnız <see cref="MeshFilter"/> taşıyan parçalar kopyalanır. Bugünkü silahlarda
        /// hepsi öyle; deri sarılı bir model (<see cref="SkinnedMeshRenderer"/>) gelirse o parça
        /// kopyada çizilmez — eksik bir kopya, yanlış duran bir kopyadan iyidir.
        /// </para>
        /// </summary>
        private void SpawnGhost()
        {
            if (dissolveMaterial == null || disappearSeconds <= 0f || _targets.Count == 0)
            {
                return;
            }

            // Oyun kapanırken / sahne boşaltılırken obje üretilmez: kimse görmeyecek ve Unity
            // "sahne yıkılırken obje yaratıldı" diye uyarır.
            if (!Application.isPlaying || _quitting || !gameObject.scene.isLoaded)
            {
                return;
            }

            var root = new GameObject("[WeaponDissolveGhost]");
            var ghost = root.AddComponent<WeaponDissolveGhost>();

            for (int i = 0; i < _targets.Count; i++)
            {
                Target target = _targets[i];
                if (target.Renderer == null)
                {
                    continue;
                }

                var filter = target.Renderer.GetComponent<MeshFilter>();
                Mesh mesh = filter != null ? filter.sharedMesh : null;
                if (mesh == null)
                {
                    continue;
                }

                var part = new GameObject(target.Renderer.name);

                // Kök identity bırakıldığı için parçanın YEREL transformu = dünya transformu;
                // silahın hiyerarşisini yeniden kurmaya gerek yok.
                Transform source = target.Renderer.transform;
                part.transform.SetParent(root.transform, false);
                part.transform.SetPositionAndRotation(source.position, source.rotation);
                part.transform.localScale = source.lossyScale;

                part.AddComponent<MeshFilter>().sharedMesh = mesh;

                var partRenderer = part.AddComponent<MeshRenderer>();
                partRenderer.sharedMaterial = dissolveMaterial;

                // Kopya çeyrek saniye yaşıyor — gölge ve prob maliyeti gereksiz.
                partRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                partRenderer.receiveShadows = false;
                partRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                partRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

                var block = new MaterialPropertyBlock();
                WriteAppearance(block, target.OriginalMaterials.Length > 0 ? target.OriginalMaterials[0] : null);
                block.SetFloat(DissolveId, 0f); // katı başlar, çözülerek gider
                partRenderer.SetPropertyBlock(block);

                ghost.AddPart(partRenderer, block);
            }

            ghost.Begin(disappearSeconds);
        }

        /// <summary>
        /// Özgün materyalin görünümünü dissolve materyaline taşır.
        /// <para>Doku bulunamazsa hiç yazılmaz (block'a <c>null</c> texture yazmak istisna atar) —
        /// o parça düz renk çözülür, efekt yine çalışır.</para>
        /// </summary>
        private void WriteAppearance(MaterialPropertyBlock block, Material source)
        {
            block.SetColor(EdgeColorId, edgeColor);
            block.SetFloat(EdgeWidthId, edgeWidth);
            block.SetFloat(NoiseScaleId, noiseScale);

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
            Debug.LogWarning("[WeaponDissolve] Dissolve materyali atanmamış — silah efektsiz belirir. " +
                             "WPN_* prefabının kökündeki WeaponDissolve'a " +
                             "Assets/_Shared/Materials/DissolveEffect.mat bağla.");
        }
    }
}
