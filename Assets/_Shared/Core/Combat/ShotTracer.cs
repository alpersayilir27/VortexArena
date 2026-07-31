using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Mermi izi (tracer): namludan vuruş noktasına kısa süre çizilen çizgi + o çizgi boyunca
    /// dağılan duman izi. Sahnede DURMAZ ve kendi başına bir şey dinlemez — yalnız çizer;
    /// <b>ne çizileceğine iki çağıran karar verir:</b> atanın kendi izi için <see cref="Weapon"/>,
    /// uzak oyuncuların izi için <see cref="RemoteShotFx"/> (§6.4/6.5). İkisi ayrı olmak ZORUNDA:
    /// sunucu atış olayını atana geri yollamaz, istemci de kendi <c>playerId</c>'sini süzer.
    /// <para>
    /// ⚠️ <b>Duman ayrı bir giriş noktası DEĞİLDİR</b> ve öyle olmamalı: <see cref="Play"/>'in
    /// içinde, çizginin hemen ardından yayılır. İkinci bir <c>PlaySmoke</c> kapısı açılsaydı iki
    /// çağıranın (yerel/uzak) birini çağırıp diğerini unutması mümkün olurdu — yani aynı silah
    /// kendi ekranında dumanlı, karşı ekranda dumansız görünürdü. Tek kapı = iki yolun sunumu
    /// tanım gereği aynı.
    /// </para>
    /// <para>
    /// ⚠️ <b>Havuz PAYLAŞIMLIDIR</b> (<see cref="Shared"/>) ve çağıran başına açılmaz: silahlar
    /// <c>weaponSource:"random"</c> modlarında sürekli üretilip yok ediliyor, silah başına havuz
    /// materyali + <c>Update</c> döngüsünü silah sayısınca çoğaltırdı.
    /// </para>
    /// <para>
    /// ⚠️ <b>HAVUZLU ve round-robin</b>: hedef yük tam ateşte ~53 olay/sn (16 oyuncu × üçte bir
    /// tracer). Bu hızda <c>Instantiate</c>/<c>Destroy</c> döngüsü Quest'te doğrudan GC dikeni
    /// demektir; düğümler bir kez üretilir, sonra yalnız yeniden konumlanır. Havuz dolduğunda en
    /// eski tracer kesilir (yeni atışı düşürmek, bir kare fazla duran çizgiden kötüdür).
    /// </para>
    /// <para>
    /// Duman için ayrı bir havuz YOKTUR: tüm atışlar TEK bir <see cref="ParticleSystem"/>'e
    /// manuel <c>Emit</c> yapar. Parçacık sistemi kendi parçacık dizisini zaten havuzlar
    /// (<c>maxParticles</c> tavanı) ve tek sistem = tek draw call — atış başına GameObject
    /// üreten bir <c>TrailRenderer</c> çözümü Quest'te hem GC hem draw call olarak ödenirdi.
    /// </para>
    /// <para>
    /// Görünüm parametreleri (<c>tracerColor/Width/Lifetime</c>) eşyanın kendisinden gelir
    /// (<see cref="ItemDefinition"/>) — tracer uzak çizim verisidir, playtest'te oradan ayarlanır.
    /// Duman aynı üç alandan TÜRETİLİR (aşağıdaki <c>Smoke*</c> sabitleri): eşyaya ayrı duman
    /// alanları eklenmedi çünkü tabanın ölçütü "ağın + uzak çizimin ihtiyacı" ve duman zaten
    /// tracer'ın kendisine biniyor — <c>tracerEveryNthRound</c> tracer'ı kapattığında duman da
    /// kapanır (iki çağıran da sayacı Play'den ÖNCE uyguluyor).
    /// </para>
    /// </summary>
    public class ShotTracer : MonoBehaviour
    {
        /// <summary>Havuzdaki çizgi sayısı (aynı anda canlı kalabilecek tracer).</summary>
        // ~53 olay/sn × 0.06 sn ömür ≈ 3-4 eşzamanlı; ömür playtest'te uzatılabildiği için pay var.
        private const int PoolSize = 24;

        /// <summary>Bu mesafeden kısa "atış"a tracer çizilmez (dejenere/eksik mesafe).</summary>
        private const float MinTracerMeters = 0.5f;

        /// <summary>Çizgi materyalinin shader arama zinciri (ilk bulunan kullanılır).</summary>
        // ⚠️ "Sprites/Default" başta duruyor çünkü Graphics Settings'in *Always Included Shaders*
        // listesinde varsayılan olarak bulunur → build'de kesin paketlenir (çalışma anında
        // Shader.Find ile bulunan, hiçbir materyalde referanslanmayan shader STRIPLENİR ve tracer
        // sahada sessizce çizilmez). Vertex rengini de çarptığı için LineRenderer.startColor işler.
        private static readonly string[] ShaderCandidates =
        {
            "Sprites/Default",
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
        };

        /// <summary>
        /// Duman materyalinin shader arama zinciri. Başı çizgiyle AYNI sebeple "Sprites/Default":
        /// Always Included olduğu için stripleme riski yok, alfa harmanlar ve parçacığın vertex
        /// rengini çarpar (parçacık başına alfa ancak böyle işler). Parçacığa özel URP shader'ı
        /// yedekte durur — o listede olmadığı için ilk sıraya KOYULMAZ.
        /// </summary>
        private static readonly string[] SmokeShaderCandidates =
        {
            "Sprites/Default",
            "Universal Render Pipeline/Particles/Unlit",
            "Particles/Standard Unlit",
        };

        // ------------------------------------------------------------------ duman ayarları
        //
        // Hepsi tracer'ın kendi üç alanından (renk/kalınlık/ömür) TÜRETME katsayısıdır; duman
        // eşyada ayrı alan olarak yaşamaz (sınıf özetindeki gerekçe).

        /// <summary>Duman puf'ları arasındaki hedef mesafe (m): yol bu aralıkla örneklenir.</summary>
        private const float SmokePuffSpacingMeters = 0.75f;

        /// <summary>Atış başına puf sayısının alt/üst sınırı (uzun atış bütçeyi yemesin).</summary>
        private const int SmokePuffsMin = 2;
        private const int SmokePuffsMax = 14;

        /// <summary>
        /// Sistemin parçacık tavanı. Kaba hesap: ~53 tracer/sn × ~0.36 sn duman ömrü × 14 puf
        /// ≈ 265 eşzamanlı; 384 pay bırakır. ⚠️ Tavana vurulduğunda parçacık sistemi <b>YENİ</b>
        /// emisyonu düşürür (çizgi havuzunun "en eskiyi kes" davranışının tersi) — duman kozmetik
        /// olduğu için bu kabul edilir, yoksa tavan aşımı eski dumanları kırpardı.
        /// </summary>
        private const int SmokeMaxParticles = 384;

        /// <summary>
        /// Duman ömrü = tracer ömrü × bu katsayı, sonra aşağıdaki banda kırpılır.
        /// <para>⚠️ Duman tracer ömrüyle <b>birebir</b> DEĞİL, ORANTILI: varsayılan tracer ömrü
        /// 0.06 sn ve o sürede sönen bir duman gözle hiç görülmez (tek karelik gri leke). Kırpma
        /// bandı da bu yüzden var — playtest'te tracer ömrü uçlara çekilse bile duman kullanılabilir
        /// kalır.</para>
        /// </summary>
        private const float SmokeLifetimeScale = 6f;
        private const float SmokeLifetimeMinSeconds = 0.25f;
        private const float SmokeLifetimeMaxSeconds = 1.2f;

        /// <summary>Puf boyutu = tracer kalınlığı × (namluda Near, isabette Far) katsayısı.</summary>
        // Yol boyunca BÜYÜR: duman dağıldıkça genişler, bu da "namludan isabete doğru sönümlenme"
        // okumasının yarısıdır (diğer yarısı aşağıdaki alfa rampası).
        private const float SmokeSizeNearScale = 3f;
        private const float SmokeSizeFarScale = 8f;

        /// <summary>Puf alfası: namluda yoğun, isabet noktasına doğru sönümlenir.</summary>
        private const float SmokeAlphaNear = 0.55f;
        private const float SmokeAlphaFar = 0.05f;

        /// <summary>
        /// İsabet ucundaki puf ömrünün namludakine oranı: uzak uç önce ölür, yani iz gözle
        /// isabetten namluya doğru "yenir". Sönümlenme yönünü tek başına alfa taşımaz.
        /// </summary>
        private const float SmokeFarLifetimeScale = 0.55f;

        /// <summary>Yukarı doğru hafif sürüklenme (m/sn) — duman düşmez, yavaşça yükselir.</summary>
        private const float SmokeDriftMetersPerSecond = 0.12f;

        /// <summary>Puf'un ideal yolundan sapma miktarı, kendi boyutunun katı olarak.</summary>
        private const float SmokeJitterOfSize = 0.35f;

        /// <summary>Dumanın tracer renginden aldığı ton payı (kalanı nötr gri).</summary>
        // Duman parlayan bir iz değil: rengin tamamı alınsa sarı tracer'da "sarı bulut" olurdu.
        private const float SmokeTintFromTracer = 0.25f;

        /// <summary>Duman dokusunun kenar uzunluğu (px) — yumuşak radyal düşüş için yeterli.</summary>
        private const int SmokeTextureSize = 32;

        /// <summary>Havuz düğümü; LineRenderer üretim anında önbelleklenir.</summary>
        private sealed class TracerNode
        {
            public LineRenderer Line;
            public float ExpireAt;
            public bool Active;
        }

        private readonly TracerNode[] _pool = new TracerNode[PoolSize];
        private int _nextNode;

        private Material _material;
        private bool _warnedNoShader;

        private ParticleSystem _smoke;
        private Material _smokeMaterial;
        private Texture2D _smokeTexture;
        private bool _smokeSetupFailed;
        private bool _warnedNoSmokeShader;

        private static ShotTracer _shared;

        /// <summary>
        /// Tüm mermi izlerinin kullandığı TEK havuz; ilk istendiğinde kendini kurar ve
        /// <c>DontDestroyOnLoad</c> olur (harita değişiminde havuz + materyal yeniden kurulmasın).
        /// Sahneye konmaz, kimse referans bağlamaz — çağıran yalnız <c>ShotTracer.Shared.Play(…)</c>
        /// der.
        /// </summary>
        public static ShotTracer Shared
        {
            get
            {
                if (_shared == null)
                {
                    var go = new GameObject("[ShotTracer]");
                    DontDestroyOnLoad(go);
                    _shared = go.AddComponent<ShotTracer>();
                }

                return _shared;
            }
        }

        /// <summary>
        /// Bir tracer çizer: çizgi + yol boyunca duman izi. <paramref name="lifetime"/> ve
        /// <paramref name="width"/> eşyanın tanımından gelir; geçersiz (≤0) değerler güvenli tabana
        /// çekilir ve duman da o düzeltilmiş değerlerden türetilir.
        /// Mesafe çok kısaysa hiç çizilmez (return false).
        /// <para>⚠️ Dönüş değeri <b>çizginin</b> durumudur: duman kurulamasa (shader yok) bile
        /// çizgi çizildiyse <c>true</c> döner — duman kozmetik bir eklentidir, tracer'ın varlığını
        /// yönetmez.</para>
        /// </summary>
        public bool Play(Vector3 from, Vector3 to, Color color, float width, float lifetime)
        {
            Vector3 delta = to - from;
            float sqrDistance = delta.sqrMagnitude;
            if (sqrDistance < MinTracerMeters * MinTracerMeters)
            {
                return false;
            }

            Material material = EnsureMaterial();
            if (material == null)
            {
                return false;
            }

            TracerNode node = TakeNode(material);
            if (node == null || node.Line == null)
            {
                return false;
            }

            float w = width > 0f ? width : 0.02f;
            float life = lifetime > 0f ? lifetime : 0.06f;

            node.Line.startWidth = w;
            node.Line.endWidth = w;
            node.Line.startColor = color;
            node.Line.endColor = color;
            node.Line.SetPosition(0, from);
            node.Line.SetPosition(1, to);
            node.Line.enabled = true;

            // Time.unscaledTime: maç duraklatılıp timeScale düşse bile tracer takılı kalmasın.
            node.ExpireAt = Time.unscaledTime + life;
            node.Active = true;

            EmitSmoke(from, delta, Mathf.Sqrt(sqrDistance), color, w, life);
            return true;
        }

        /// <summary>Ömrü geçen çizgileri kapatır (havuz düğümü yok EDİLMEZ, yalnız gizlenir).</summary>
        private void Update()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < _pool.Length; i++)
            {
                TracerNode node = _pool[i];
                if (node == null || !node.Active || now < node.ExpireAt)
                {
                    continue;
                }

                node.Active = false;
                if (node.Line != null)
                {
                    node.Line.enabled = false;
                }
            }
        }

        // ---------------------------------------------------------------------- duman

        /// <summary>
        /// Mermi yolu boyunca duman puf'ları yayar: namluda yoğun/küçük/uzun ömürlü, isabet
        /// noktasına doğru soluk/geniş/kısa ömürlü — yani iz o yönde sönümlenerek kaybolur.
        /// <para>
        /// ⚠️ Puf sayısı MESAFEDEN türetilir, sabit değildir: sabit sayı kısa atışta puf'ları
        /// üst üste yığıp opak bir leke, uzun atışta ise kopuk noktalar üretirdi.
        /// </para>
        /// <para>
        /// Zamanlama için ayrı bir iş yoktur: parçacıklar kendi <c>startLifetime</c>'larıyla
        /// ölür, <c>Update</c> onlara dokunmaz. Sistem <c>useUnscaledTime</c> ile koşar —
        /// çizginin ömrü de <c>Time.unscaledTime</c> ekseninde ölçülüyor, ikisi ayrı saatte
        /// koşsa maç duraklatıldığında duman donar, çizgi kaybolurdu.
        /// </para>
        /// </summary>
        private void EmitSmoke(Vector3 from, Vector3 delta, float distance, Color color, float width, float lifetime)
        {
            ParticleSystem smoke = EnsureSmoke();
            if (smoke == null)
            {
                return;
            }

            int puffs = Mathf.Clamp(
                Mathf.RoundToInt(distance / SmokePuffSpacingMeters), SmokePuffsMin, SmokePuffsMax);

            float baseLife = Mathf.Clamp(
                lifetime * SmokeLifetimeScale, SmokeLifetimeMinSeconds, SmokeLifetimeMaxSeconds);

            // Duman parlayan bir iz değil, dağılan bir bulut: rengin çoğu nötr gri, tracer tonu
            // yalnız tanınacak kadar karışır (alfa aşağıda puf başına verilir).
            Color tint = Color.Lerp(new Color(0.62f, 0.62f, 0.64f), color, SmokeTintFromTracer);

            // Struct: döngü boyunca yeniden kullanılır, hiçbir çağrı ayırma yapmaz.
            var emit = new ParticleSystem.EmitParams();

            for (int i = 0; i < puffs; i++)
            {
                // t=0 namlu, t=1 isabet. Tek puf ihtimali yok (SmokePuffsMin=2) ama bölme yine de
                // korunuyor — sabit sonradan düşürülürse burası sessizce NaN üretirdi.
                float t = puffs > 1 ? i / (float)(puffs - 1) : 0f;

                float size = width * Mathf.Lerp(SmokeSizeNearScale, SmokeSizeFarScale, t);

                tint.a = Mathf.Lerp(SmokeAlphaNear, SmokeAlphaFar, t);

                emit.position = from + delta * t + Random.insideUnitSphere * (size * SmokeJitterOfSize);
                emit.startSize = size;
                emit.startColor = tint;
                emit.startLifetime = baseLife * Mathf.Lerp(1f, SmokeFarLifetimeScale, t);
                emit.velocity = Vector3.up * SmokeDriftMetersPerSecond;
                emit.rotation = Random.Range(0f, 360f);

                smoke.Emit(emit, 1);
            }
        }

        /// <summary>
        /// Tüm atışların PAYLAŞTIĞI duman sistemi (tek sistem = tek draw call). Emisyon modülü
        /// KAPALIDIR: parçacıklar yalnız <see cref="EmitSmoke"/>'un manuel <c>Emit</c>'iyle doğar.
        /// Kurulum bir kez yapılır; başarısız olursa (shader yok) bir daha denenmez.
        /// </summary>
        private ParticleSystem EnsureSmoke()
        {
            if (_smoke != null)
            {
                return _smoke;
            }

            if (_smokeSetupFailed)
            {
                return null;
            }

            Material material = EnsureSmokeMaterial();
            if (material == null)
            {
                _smokeSetupFailed = true;
                return null;
            }

            var go = new GameObject("[TracerSmoke]");
            go.transform.SetParent(transform, false);

            var ps = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = SmokeMaxParticles;
            // Puf'lar dünyada duran bir izdir: kök (bu DDOL objesi) hiç kıpırdamasa da uzay
            // açıkça World seçilir — Local olsaydı bir gün kök taşındığında tüm izler onunla giderdi.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            // Çizginin ömrüyle AYNI saat (Play'deki Time.unscaledTime): maç duraklatılıp timeScale
            // düşse bile duman çizgiyle birlikte sönmeye devam eder.
            main.useUnscaledTime = true;
            main.gravityModifier = 0f;
            // Ölçekleme kökten miras alınmaz: puf boyutu metre cinsinden hesaplanıyor.
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            // ⚠️ AlwaysSimulate: kökün kendisi dünya orijininde duruyor, puf'lar ise arenanın her
            // yerinde. Otomatik culling'de sistem "görüş alanı dışında" sayılıp simülasyonu
            // duraklatabilir → duman ekranın ortasında donar. Tavan zaten 384 parçacık.
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            // Emisyon KAPALI: tek kaynak manuel Emit.
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = false;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;

            // Sönümlenerek kaybolma: alfa rampası. startColor ile ÇARPILIR, yani puf başına
            // verilen (namluda yoğun → isabette soluk) alfa korunur.
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0.75f, 0f),
                    new GradientAlphaKey(1f, 0.15f),
                    new GradientAlphaKey(0f, 1f),
                });
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(gradient);

            // Dağılma: puf söndükçe genişler.
            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
                1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 1.9f));

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            // Duman bir ışık kaynağı değil, bir iz: gölge/ışık probu kapalı (Quest bütçesi) —
            // çizgideki gerekçenin aynısı.
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            // playOnAwake kapalı olduğu için simülasyon elle başlatılır; emisyon kapalı olduğundan
            // sistem hiçbir şey üretmeden boşta koşar ve Emit'i beklemeye hazır durur.
            ps.Play();

            _smoke = ps;
            return _smoke;
        }

        /// <summary>
        /// Duman materyali: çizgininkinden AYRI bir örnek olmak zorunda — dokusu var ve o doku
        /// paylaşılan materyale yazılsa <see cref="LineRenderer"/> de o dokuyla çizilirdi.
        /// </summary>
        private Material EnsureSmokeMaterial()
        {
            if (_smokeMaterial != null)
            {
                return _smokeMaterial;
            }

            for (int i = 0; i < SmokeShaderCandidates.Length; i++)
            {
                Shader shader = Shader.Find(SmokeShaderCandidates[i]);
                if (shader == null)
                {
                    continue;
                }

                _smokeMaterial = new Material(shader) { name = "M_TracerSmoke(runtime)" };
                _smokeMaterial.mainTexture = EnsureSmokeTexture();
                return _smokeMaterial;
            }

            if (!_warnedNoSmokeShader)
            {
                _warnedNoSmokeShader = true;
                Debug.LogWarning(
                    "[ShotTracer] Duman izi için shader bulunamadı (Sprites/Default dahil) — mermi " +
                    "izi dumansız çizilecek. Graphics Settings > Always Included Shaders listesini kontrol et.");
            }

            return null;
        }

        /// <summary>
        /// Yumuşak radyal düşüşlü duman dokusu — çalışma anında üretilir.
        /// <para>⚠️ Projede bir duman dokusu var (<c>_Shared/FX/M_MuzzleSmoke.mat</c>) ama o
        /// <c>Resources/</c> altında DEĞİL, yani çalışma anında yüklenemez; serialize alan açmak
        /// ise <see cref="ShotTracer"/>'ı sahneye konması gereken bir bileşene çevirirdi (bugün
        /// kendini önyükleyen bir tekil ve öyle kalmalı — her yeni arenaya elle kurulum adımı
        /// eklemeyi reddediyoruz). 32×32 RGBA ≈ 4 KB, bir kez üretilir.</para>
        /// </summary>
        private Texture2D EnsureSmokeTexture()
        {
            if (_smokeTexture != null)
            {
                return _smokeTexture;
            }

            var texture = new Texture2D(SmokeTextureSize, SmokeTextureSize, TextureFormat.RGBA32, false)
            {
                name = "T_TracerSmoke(runtime)",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[SmokeTextureSize * SmokeTextureSize];
            float half = SmokeTextureSize * 0.5f;

            for (int y = 0; y < SmokeTextureSize; y++)
            {
                for (int x = 0; x < SmokeTextureSize; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;

                    // Karesel düşüş: kenarda sert bir daire değil, yumuşak bir puf.
                    float falloff = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    byte alpha = (byte)(falloff * falloff * 255f);

                    pixels[y * SmokeTextureSize + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            // makeNoLongerReadable: CPU kopyası atılır (doku bir daha okunmuyor).
            texture.Apply(false, true);

            _smokeTexture = texture;
            return _smokeTexture;
        }

        // ---------------------------------------------------------------------- havuz

        /// <summary>Round-robin: sıradaki (en eski) düğümü döndürür; henüz yoksa tembel üretir.</summary>
        private TracerNode TakeNode(Material material)
        {
            TracerNode node = _pool[_nextNode];
            if (node == null || node.Line == null)
            {
                node = CreateNode(material);
                _pool[_nextNode] = node;
            }

            _nextNode = (_nextNode + 1) % PoolSize;
            return node;
        }

        private TracerNode CreateNode(Material material)
        {
            var go = new GameObject("[Tracer]");
            go.transform.SetParent(transform, false);

            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            // Tracer bir ışık kaynağı değil, bir iz: gölge/ışık probu kapalı (Quest bütçesi).
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            line.enabled = false;

            return new TracerNode { Line = line };
        }

        /// <summary>
        /// Tüm tracer'ların PAYLAŞTIĞI materyal (renk LineRenderer'ın vertex rengiyle verilir —
        /// eşya başına materyal örneği açmak SRP batch'ini bölerdi).
        /// </summary>
        private Material EnsureMaterial()
        {
            if (_material != null)
            {
                return _material;
            }

            for (int i = 0; i < ShaderCandidates.Length; i++)
            {
                Shader shader = Shader.Find(ShaderCandidates[i]);
                if (shader != null)
                {
                    _material = new Material(shader) { name = "M_ShotTracer(runtime)" };
                    return _material;
                }
            }

            if (!_warnedNoShader)
            {
                _warnedNoShader = true;
                Debug.LogWarning(
                    "[ShotTracer] Tracer için shader bulunamadı (Sprites/Default dahil) — mermi izi " +
                    "çizilmeyecek. Graphics Settings > Always Included Shaders listesini kontrol et.");
            }

            return null;
        }

        private void OnDestroy()
        {
            if (_shared == this)
            {
                // Statik alan yıkılmış bileşene bağlı kalmaz: bir sonraki Play isteği havuzu
                // yeniden kurar (Play modundan çıkışta domain reload kapalıysa bu şart).
                _shared = null;
            }

            if (_material != null)
            {
                Destroy(_material);
                _material = null;
            }

            // Duman materyali ve dokusu da çalışma anında üretildi: ikisi de elle yok edilir,
            // yoksa domain reload kapalıyken Play'den her çıkışta sızarlar.
            if (_smokeMaterial != null)
            {
                Destroy(_smokeMaterial);
                _smokeMaterial = null;
            }

            if (_smokeTexture != null)
            {
                Destroy(_smokeTexture);
                _smokeTexture = null;
            }
        }
    }
}
