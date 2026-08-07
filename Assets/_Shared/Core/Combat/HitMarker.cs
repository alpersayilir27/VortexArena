using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// <b>İsabet göstergesi:</b> vuruşun değdiği noktada kısa süre yanıp sönerek kaybolan bir X.
    /// Oyuncunun "vurdum" geri bildirimidir — can sunucudan gelene kadar (§10.3) ekranda hiçbir
    /// şey değişmediği için tetiği çeken oyuncu isabetini başka türlü göremez.
    ///
    /// <para><b>YALNIZ vuran oyuncu görür ve bu bir süzme DEĞİL, yapısaldır:</b> tek çağıranı
    /// <see cref="ArenaCombat.ReportHit"/>'tir ve o metot yalnız hasarı VEREN istemcide koşar.
    /// Protokolde karşılığı YOKTUR ve eklenmez — telde bir "isabet göstergesi" mesajı olsaydı
    /// vurulan oyuncu da kendi gövdesinde X görürdü.</para>
    ///
    /// <para><b>Görünüm koddan DEĞİL editörden ayarlanır:</b> boy, saydamlık, ömür, eğriler,
    /// kontur, materyal ve dilersen görünümün tamamının yerini alan bir prefab
    /// <see cref="HitMarkerStyle"/> asset'indedir
    /// (<c>Assets/_Shared/Data/Resources/HitMarkerStyle.asset</c>). Asset yoksa koddaki
    /// varsayılanlarla çalışır. ⚠️ Buradaki sabitler <b>ayar değildir</b>: havuzun büyüklüğü ve
    /// shader arama zinciri gibi, editörde ayarlanmasının anlamı olmayan yapısal değerlerdir.</para>
    ///
    /// <para><b>Sahnede hiçbir kurulum adımı yoktur</b> (<see cref="ShotTracer"/> /
    /// <see cref="WeaponGranter"/> kalıbı): ilk isabette kendini önyükler,
    /// <c>DontDestroyOnLoad</c> olur ve harita değişiminde havuzu korur. Sahneye bileşen koymak
    /// her yeni arenaya elle bir adım eklerdi — ayarın <c>Resources</c>'ta yaşamasının sebebi de
    /// budur (bağlanacak bir referans alanı yok).</para>
    ///
    /// <para><b>Neden prosedürel X iki <see cref="LineRenderer"/> ile çiziliyor</b> (doku + quad
    /// değil): çizgi geometrisi her mesafede keskindir, doku üretmeyi/yüklemeyi gerektirmez ve tam
    /// olarak <see cref="ShotTracer"/>'ın kanıtlanmış yolunu kullanır — paylaşılan materyal +
    /// vertex rengi (materyal örneği açılmadığı için SRP batch'i bölünmez). İki çizgi ayrı olmak
    /// zorunda: bir X tek bir polyline ile birleştirici bir kenar çizmeden ifade edilemez.
    /// Kendi görselini isteyen zaten prefab yolunu kullanır.</para>
    ///
    /// <para>⚠️ <b>İşaret dünyada SABİT bir noktada durur, hedefe yapışmaz.</b> Vuruşun nerede
    /// olduğunu gösterir; hedefin çocuğu yapılsaydı ölüp yok olan ya da ışınlanan bir avatarla
    /// birlikte kaçardı. Ömür bu yüzden kısadır — hedef bu sürede kayda değer uzaklaşamaz.</para>
    ///
    /// <para>⚠️ <b>Duvar arkasından görünmez:</b> derinlik testi açıktır. İşaret hep ışının
    /// GERÇEKTEN değdiği noktada durur, yani hitscan vuruşta tanım gereği görünür; siperin
    /// arkasına düşen bir alan-etkisi vuruşunda görünmemesi doğrudur.</para>
    /// </summary>
    public class HitMarker : MonoBehaviour
    {
        /// <summary>Aynı anda canlı kalabilecek işaret sayısı.</summary>
        // 600 RPM'de 10 isabet/sn × 0.3 sn ömür ≈ 3 eşzamanlı; alan etkisi (tek patlamada n hedef)
        // için pay bırakıldı. Havuz dolarsa en eski işaret kesilir — yeni isabeti göstermemek,
        // bir kare fazla duran X'ten kötüdür.
        private const int PoolSize = 12;

        /// <summary><c>Camera.main</c> bulunamadığında yeniden deneme aralığı (her karede aranmaz).</summary>
        private const float CameraRetrySeconds = 1f;

        /// <summary>
        /// Materyalin shader arama zinciri (ilk bulunan kullanılır) — <see cref="ShotTracer"/> ile
        /// AYNI ve aynı sebeple: "Sprites/Default" Graphics Settings'in <i>Always Included Shaders</i>
        /// listesindedir, yani build'de kesin paketlenir (yalnız <c>Shader.Find</c> ile bulunan ve
        /// hiçbir materyalde referanslanmayan shader STRIPLENİR ve işaret sahada sessizce çizilmez).
        /// Vertex rengini de çarptığı için <c>LineRenderer.startColor</c> ile sönme işler.
        /// <para>⚠️ Bu zincir yalnız <b>yedektir</b>: kendi materyalini
        /// <see cref="HitMarkerStyle.LineMaterial"/>'a bağlarsan o kullanılır.</para>
        /// </summary>
        private static readonly string[] ShaderCandidates =
        {
            "Sprites/Default",
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
        };

        /// <summary>Kontur çizgilerinin sıralama sırası — ana çizginin ARKASINDA kalsınlar.</summary>
        // Aynı materyal + aynı kuyruk: hangisinin önce çizileceğini yalnız sortingOrder belirler.
        // Derinlikle çözmeye çalışmak (konturu 1 mm geriye itmek) hem z-fighting'e açık, hem de
        // işaret yüzeye yakın olduğu için konturu yüzeyin içine gömerdi.
        private const int OutlineSortingOrder = -1;

        /// <summary>Havuz düğümü: bir işaretin sahnedeki bütün parçaları + o an nerede durduğu.</summary>
        // ⚠️ Düğüm İKİ görünümden birini taşır (prosedürel X ya da prefab örneği) ve hangisi
        // olduğunu KURULURKEN öğrenir: ayarda prefab değişirse düğüm yeniden kurulur (TakeNode).
        private sealed class MarkerNode
        {
            /// <summary>Düğümün kökü — prefab yolunda prefab örneğinin kendisidir.</summary>
            public Transform Root;

            /// <summary>Prosedürel yol: X'in iki kolu ve (varsa) konturun iki kolu.</summary>
            public LineRenderer ArmA;
            public LineRenderer ArmB;
            public LineRenderer OutlineA;
            public LineRenderer OutlineB;

            /// <summary>Prefab yolu: örneğin parçacık sistemleri (her isabette baştan oynatılır).</summary>
            public ParticleSystem[] Particles;

            /// <summary>Prefabın kendi ölçeği — boy çarpanı bununla çarpılır, ezilmez.</summary>
            public Vector3 PrefabScale;

            /// <summary>Bu düğüm prefab yoluyla mı kuruldu (ayar değişimini yakalamak için).</summary>
            public bool UsesPrefab;

            public bool Active;

            /// <summary>İsabetin dünya noktası — işaret her karede buradan yeniden kurulur.</summary>
            public Vector3 Anchor;

            /// <summary>Doğuş anı (<c>Time.unscaledTime</c>): maç duraklatılsa da işaret takılı kalmasın.</summary>
            public float StartAt;
        }

        private readonly MarkerNode[] _pool = new MarkerNode[PoolSize];
        private int _nextNode;

        private HitMarkerStyle _style;
        private bool _styleIsFallback;

        private Material _runtimeMaterial;
        private bool _warnedNoShader;

        private Camera _eye;
        private float _cameraRetryTimer;

        private static HitMarker _shared;

        /// <summary>
        /// Tüm isabet göstergelerinin kullandığı TEK havuz; ilk istendiğinde kendini kurar ve
        /// <c>DontDestroyOnLoad</c> olur. Sahneye konmaz, kimse referans bağlamaz — çağıran yalnız
        /// <c>HitMarker.Shared.Play(nokta)</c> der.
        /// </summary>
        public static HitMarker Shared
        {
            get
            {
                if (_shared == null)
                {
                    var go = new GameObject("[HitMarker]");
                    DontDestroyOnLoad(go);
                    _shared = go.AddComponent<HitMarker>();
                }

                return _shared;
            }
        }

        /// <summary>
        /// Görünüm ayarı: <c>Resources</c>'taki asset, yoksa koddaki varsayılanlarla dolu bellek
        /// içi bir örnek. ⚠️ Örnek <b>bir kez</b> açılır ve <see cref="OnDestroy"/> onu yok eder —
        /// her isabette <c>CreateInstance</c> çağırmak asset olmayan projede sızıntı olurdu.
        /// </summary>
        private HitMarkerStyle Style
        {
            get
            {
                if (_style != null)
                {
                    return _style;
                }

                _style = HitMarkerStyle.Load();
                if (_style == null)
                {
                    _style = ScriptableObject.CreateInstance<HitMarkerStyle>();
                    _styleIsFallback = true;
                }

                return _style;
            }
        }

        /// <summary>
        /// Verilen dünya noktasında bir isabet işareti başlatır.
        /// <para>⚠️ Bunu <b>doğrudan çağırma</b>: tek çağıranı <see cref="ArenaCombat.ReportHit"/>'tir
        /// ve öyle kalmalı. İkinci bir çağıran, vuruşu bildirmeden işaret gösterebilir (oyuncuya
        /// yalan) ya da bildirdiği hâlde göstermeyi unutabilir — yani "vurdum mu" sorusunun cevabı
        /// hasar kaynağına göre değişirdi.</para>
        /// <para>Çizim ilk <c>LateUpdate</c>'te yapılır (aynı kare): burada yalnız havuz düğümü
        /// ayrılır — kamera o an henüz karenin nihai yerinde değildir.</para>
        /// </summary>
        /// <returns>İşaret gerçekten kuyruğa alındı mı (düğüm kurulamazsa <c>false</c>).</returns>
        public bool Play(Vector3 worldPoint)
        {
            MarkerNode node = TakeNode();
            if (node == null)
            {
                return false;
            }

            node.Anchor = worldPoint;
            node.StartAt = Time.unscaledTime;
            node.Active = true;

            // Prefab yolunda parçacıklar baştan oynatılır: havuzdan gelen bir örnek önceki
            // isabetin parçacıklarını taşıyor olabilir.
            if (node.UsesPrefab)
            {
                node.Root.gameObject.SetActive(true);
                RestartParticles(node);
            }

            return true;
        }

        /// <summary>
        /// Canlı işaretleri her karede yeniden kurar: göze döndürür, mesafeye göre ölçekler,
        /// söndürür; ömrü dolanı gizler (havuz düğümü yok EDİLMEZ).
        /// <para><c>LateUpdate</c>: rig ve kafa <c>Update</c>'te hareket ediyor, daha erken koşan
        /// bir yönelim bir kare gerideki bakış açısına göre kurulur ve hızlı baş hareketinde X
        /// eğik görünür.</para>
        /// </summary>
        private void LateUpdate()
        {
            Camera eye = ResolveEye();
            HitMarkerStyle style = Style;
            float now = Time.unscaledTime;
            float lifetime = style.LifetimeSeconds;

            for (int i = 0; i < _pool.Length; i++)
            {
                MarkerNode node = _pool[i];
                if (node == null || !node.Active)
                {
                    continue;
                }

                if (node.Root == null)
                {
                    node.Active = false;
                    continue;
                }

                float t = (now - node.StartAt) / lifetime;

                // Kamera yoksa (sahne değişimi, admin rig'i kapalı) işaret gizlenir: son bilinen
                // yönelimde donmuş bir X bırakmak, hiç göstermemekten kötüdür.
                if (t >= 1f || eye == null)
                {
                    Hide(node);
                    continue;
                }

                Draw(node, style, eye, t);
            }
        }

        /// <summary>
        /// Bir işaretin o karedeki hâli: göze dik bir düzlemde, mesafeyle ölçeklenmiş, sönen X.
        /// <para>Kolların yönü <b>kameranın kendi sağ/yukarı eksenlerinden</b> gelir (noktadan göze
        /// bakan bir dönüş değil): ekrana paralel bir düzlem, işaretin görüş alanının kenarında
        /// bile aynı X olarak okunmasını sağlar ve stereo iki gözde tutarlıdır. <b>Kaldırma</b> ise
        /// göze doğrudur — yüzeyden kaçmanın doğru yönü kameranın bakış ekseni değil, o noktadan
        /// göze giden vektördür.</para>
        /// </summary>
        private void Draw(MarkerNode node, HitMarkerStyle style, Camera eye, float t)
        {
            Transform eyeTransform = eye.transform;
            Vector3 toEye = eyeTransform.position - node.Anchor;
            float distance = toEye.magnitude;

            // Göz tam isabet noktasındaysa yön tanımsızdır (namlusunu kendine dayamış oyuncu).
            if (distance < 1e-3f)
            {
                Hide(node);
                return;
            }

            float size = style.SizeAt(distance) * Mathf.Max(0f, style.SizeScaleAt(t));
            Vector3 center = node.Anchor + (toEye / distance) * style.SurfaceLiftMeters;

            if (node.UsesPrefab)
            {
                node.Root.position = center;
                node.Root.localScale = node.PrefabScale * size;
                if (style.FaceCamera)
                {
                    // Kameranın dönüşünün aynısı = ekrana paralel. Unity'nin varsayılan Quad'ı bu
                    // hâlde kameraya bakar (görünen yüzü -Z'dir).
                    node.Root.rotation = eyeTransform.rotation;
                }

                return;
            }

            // Yarı boy: kollar merkezden ±(sağ ± yukarı) × yarıboy uzanır, yani X'in sınırlayıcı
            // karesinin kenarı tam `size` olur.
            float half = size * 0.5f;
            Vector3 diagonalA = (eyeTransform.right + eyeTransform.up) * half;
            Vector3 diagonalB = (eyeTransform.right - eyeTransform.up) * half;

            float alpha = Mathf.Clamp01(style.AlphaAt(t));
            float thickness = style.ThicknessFor(size);
            Material material = EnsureMaterial(style);

            Color color = style.Color;
            color.a *= alpha;
            Apply(node.ArmA, material, center - diagonalA, center + diagonalA, color, thickness);
            Apply(node.ArmB, material, center - diagonalB, center + diagonalB, color, thickness);

            if (node.OutlineA == null || node.OutlineB == null)
            {
                return;
            }

            // Kontur ayarda kapatılmış olabilir: düğüm zaten kuruldu, yalnız gizlenir (havuz
            // düğümünü yeniden kurmak için sebep değil — ayar Play kipinde açılıp kapanabiliyor).
            if (!style.HasOutline)
            {
                node.OutlineA.enabled = false;
                node.OutlineB.enabled = false;
                return;
            }

            Color outline = style.OutlineColor;
            outline.a *= alpha;
            float outlineThickness = thickness * style.OutlineThicknessScale;
            Apply(node.OutlineA, material, center - diagonalA, center + diagonalA, outline, outlineThickness);
            Apply(node.OutlineB, material, center - diagonalB, center + diagonalB, outline, outlineThickness);
        }

        /// <summary>
        /// Bir kolu yazar. Materyal <b>yalnız değiştiyse</b> atanır: ayardaki materyali Play
        /// kipinde değiştirmek anında etki etsin, ama her karede gereksiz atama olmasın.
        /// </summary>
        private static void Apply(LineRenderer line, Material material, in Vector3 from, in Vector3 to,
            in Color color, float width)
        {
            if (material != null && line.sharedMaterial != material)
            {
                line.sharedMaterial = material;
            }

            line.SetPosition(0, from);
            line.SetPosition(1, to);
            line.startColor = color;
            line.endColor = color;
            line.startWidth = width;
            line.endWidth = width;
            line.enabled = true;
        }

        private static void Hide(MarkerNode node)
        {
            node.Active = false;

            if (node.UsesPrefab)
            {
                if (node.Root != null)
                {
                    node.Root.gameObject.SetActive(false);
                }

                return;
            }

            SetEnabled(node.ArmA, false);
            SetEnabled(node.ArmB, false);
            SetEnabled(node.OutlineA, false);
            SetEnabled(node.OutlineB, false);
        }

        private static void SetEnabled(LineRenderer line, bool value)
        {
            if (line != null)
            {
                line.enabled = value;
            }
        }

        private static void RestartParticles(MarkerNode node)
        {
            if (node.Particles == null)
            {
                return;
            }

            for (int i = 0; i < node.Particles.Length; i++)
            {
                ParticleSystem ps = node.Particles[i];
                if (ps == null)
                {
                    continue;
                }

                ps.Clear(true);
                ps.Play(true);
            }
        }

        // ---------------------------------------------------------------------- havuz

        /// <summary>
        /// Round-robin: sıradaki (en eski) düğümü döndürür; henüz yoksa tembel üretir.
        /// <para>Düğüm <b>yanlış görünümle</b> kurulmuşsa (ayardaki prefab sonradan bağlandı ya da
        /// kaldırıldı) yıkılıp yeniden kurulur — yoksa prefabı bağlamak Play'i yeniden başlatmayı
        /// gerektirirdi.</para>
        /// </summary>
        private MarkerNode TakeNode()
        {
            HitMarkerStyle style = Style;
            bool wantsPrefab = style.MarkerPrefab != null;

            MarkerNode node = _pool[_nextNode];
            if (node != null && (node.Root == null || node.UsesPrefab != wantsPrefab))
            {
                if (node.Root != null)
                {
                    Destroy(node.Root.gameObject);
                }

                node = null;
            }

            if (node == null)
            {
                node = wantsPrefab ? CreatePrefabNode(style) : CreateLineNode(style);
                _pool[_nextNode] = node;
            }

            _nextNode = (_nextNode + 1) % PoolSize;
            return node;
        }

        private MarkerNode CreatePrefabNode(HitMarkerStyle style)
        {
            GameObject instance = Instantiate(style.MarkerPrefab, transform);
            instance.name = "[HitMark]";
            instance.SetActive(false);

            return new MarkerNode
            {
                Root = instance.transform,
                UsesPrefab = true,
                PrefabScale = style.MarkerPrefab.transform.localScale,
                Particles = instance.GetComponentsInChildren<ParticleSystem>(true),
            };
        }

        private MarkerNode CreateLineNode(HitMarkerStyle style)
        {
            var root = new GameObject("[HitMark]");
            root.transform.SetParent(transform, false);

            Material material = EnsureMaterial(style);

            // ⚠️ Kontur düğümleri ayarda kapalı olsa bile üretilir: ayar Play kipinde açıldığında
            // havuzun yarısı konturlu, yarısı kontursuz olmasın (üretim tek seferlik ve bedeli
            // kapalı iken yalnız iki devre dışı bileşendir).
            return new MarkerNode
            {
                Root = root.transform,
                UsesPrefab = false,
                OutlineA = CreateArm(root.transform, material, "OutlineA", OutlineSortingOrder),
                OutlineB = CreateArm(root.transform, material, "OutlineB", OutlineSortingOrder),
                ArmA = CreateArm(root.transform, material, "ArmA", 0),
                ArmB = CreateArm(root.transform, material, "ArmB", 0),
            };
        }

        private static LineRenderer CreateArm(Transform parent, Material material, string name, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.textureMode = LineTextureMode.Stretch;
            line.sortingOrder = sortingOrder;
            // View: şerit her kameraya kendi düzleminde çevrilir — kollar kenardan bakıldığında
            // incelip kaybolmaz (konumları zaten göze dik kuruluyor, bu onun ikinci emniyeti).
            line.alignment = LineAlignment.View;
            // İşaret bir ışık kaynağı değil, bir gösterge: gölge/ışık probu kapalı (Quest bütçesi).
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            line.enabled = false;

            return line;
        }

        /// <summary>
        /// Çizgi materyali: önce ayardaki (kendi parlama/glow materyalin), yoksa tüm işaretlerin
        /// PAYLAŞTIĞI çalışma anı materyali. Renk vertex renginden geldiği için işaret başına
        /// materyal örneği açılmaz (açılsaydı SRP batch'i bölünürdü).
        /// </summary>
        private Material EnsureMaterial(HitMarkerStyle style)
        {
            if (style.LineMaterial != null)
            {
                return style.LineMaterial;
            }

            if (_runtimeMaterial != null)
            {
                return _runtimeMaterial;
            }

            for (int i = 0; i < ShaderCandidates.Length; i++)
            {
                Shader shader = Shader.Find(ShaderCandidates[i]);
                if (shader != null)
                {
                    _runtimeMaterial = new Material(shader) { name = "M_HitMarker(runtime)" };
                    return _runtimeMaterial;
                }
            }

            if (!_warnedNoShader)
            {
                _warnedNoShader = true;
                Debug.LogWarning(
                    "[HitMarker] İsabet göstergesi için shader bulunamadı (Sprites/Default dahil) — " +
                    "işaret çizilmeyecek. Graphics Settings > Always Included Shaders listesini kontrol " +
                    "et ya da HitMarkerStyle.asset'e kendi materyalini bağla.");
            }

            return null;
        }

        /// <summary>
        /// Bakış kamerası (<c>Camera.main</c>); bulunamazsa saniyede bir yeniden denenir — her
        /// karede <c>Camera.main</c> aramak etiket taraması demektir.
        /// </summary>
        private Camera ResolveEye()
        {
            if (_eye != null)
            {
                return _eye;
            }

            _cameraRetryTimer -= Time.unscaledDeltaTime;
            if (_cameraRetryTimer > 0f)
            {
                return null;
            }

            _cameraRetryTimer = CameraRetrySeconds;
            _eye = Camera.main;
            return _eye;
        }

        private void OnDestroy()
        {
            if (_shared == this)
            {
                // Statik alan yıkılmış bileşene bağlı kalmaz: bir sonraki Play isteği havuzu
                // yeniden kurar (Play modundan çıkışta domain reload kapalıysa bu şart).
                _shared = null;
            }

            if (_runtimeMaterial != null)
            {
                // Çalışma anında üretildi: elle yok edilir, yoksa domain reload kapalıyken
                // Play'den her çıkışta sızar. (Ayardaki materyal bir ASSET'tir, ona dokunulmaz.)
                Destroy(_runtimeMaterial);
                _runtimeMaterial = null;
            }

            if (_styleIsFallback && _style != null)
            {
                // Asset bulunamadığı için bellekte açılan örnek — o da bir Unity nesnesi.
                Destroy(_style);
            }

            _style = null;
        }
    }
}
