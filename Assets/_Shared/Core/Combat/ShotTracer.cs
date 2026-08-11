using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Mermi izi (tracer): namludan vuruş noktasına çizilen, ömrü boyunca <b>sönerek kaybolan</b>
    /// çizgi + o çizgi boyunca dağılan duman izi. Sahnede DURMAZ ve kendi başına bir şey
    /// dinlemez — yalnız çizer;
    /// <b>ne çizileceğine iki çağıran karar verir:</b> atanın kendi izi için <see cref="Weapon"/>,
    /// uzak oyuncuların izi için <see cref="RemoteShotFx"/> (§6.4/6.5). İkisi ayrı olmak ZORUNDA:
    /// sunucu atış olayını atana geri yollamaz, istemci de kendi <c>playerId</c>'sini süzer.
    /// <para>
    /// ⚠️ <b>Bir tetik çekişi tek çizgi DEĞİLDİR</b>: saçmalı silahta (<c>PelletCount &gt; 1</c>)
    /// yaylımın her saçması kendi izini alır ve hepsi <see cref="Play"/> ile TEK çağrıda çizilir.
    /// Saçma başına ayrı çağrı YAPILMAZ — duman bütçesi ve çizgi kalınlığı yaylımın tamamına göre
    /// ölçülüyor, saçma saçma çağrılsa ikisi de saçma sayısınca çoğalırdı.
    /// </para>
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
        // ⚠️ Tavanı ORTALAMA olay hızı değil, TEK KAREDEKİ yığılma belirler: saçmalı silahta bir
        // tetik çekişi aynı karede PelletCount kadar çizgi ister (XM1014 6, Nova 9). Ortalamaya
        // göre boyutlanmış bir havuzda (~53 olay/sn × 0.1 sn ömür ≈ 5 eşzamanlı) iki oyuncunun
        // üst üste gelen yaylımı birbirinin çizgilerini keser ve yelpaze eksik/asimetrik çizilir —
        // belirtisi "bazı saçmaların izi hiç çıkmıyor" olur, yani teşhisi zor. Taban bu yüzden
        // birkaç TAM yaylımdır: MaxScatterLines × ~4 eşzamanlı yaylım.
        private const int PoolSize = 48;

        /// <summary>Bu mesafeden kısa "atış"a tracer çizilmez (dejenere/eksik mesafe).</summary>
        private const float MinTracerMeters = 0.5f;

        /// <summary>
        /// Tek yaylımda çizilebilecek en çok çizgi (saçma) sayısı — <b>iki çağıran da</b> saçma
        /// sayısını buna kırpar.
        /// <para>Sabit burada durur çünkü ölçüsü <see cref="PoolSize"/> ile aynı bütçeden gelir:
        /// çağıranlar kendi tavanlarını taşısaydı biri büyütüldüğünde havuz sessizce yetmez
        /// olurdu. Bugünkü en kalabalık silah 9 saçmalıdır (Nova); pay ileride gelecek daha
        /// saçmalı bir silah içindir.</para>
        /// </summary>
        public const int MaxScatterLines = 12;

        /// <summary>
        /// Yaylım (saçmalı) atışta çizginin incelme oranı.
        /// <para>Tek mermilik kalınlık, aynı namludan çıkan 6-9 çizgide namlu dibinde tek bir opak
        /// huniye dönüşür ve <b>yelpazenin açısı</b> okunmaz olur — oysa saçmalının görsel kimliği
        /// tam olarak odur. Eşyaya ayrı bir "saçma kalınlığı" alanı EKLENMEZ, ölçü
        /// <c>tracerWidth</c>'ten türetilir: iki sayı olsaydı playtest'te biri ayarlanıp öteki
        /// unutulurdu.</para>
        /// </summary>
        private const float ScatterWidthScale = 0.6f;

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
        /// Sistemin parçacık tavanı. Kaba hesap en kötü hâl üzerinden: ~53 tracer/sn ×
        /// <see cref="SmokeLifetimeMaxSeconds"/> × 14 puf ≈ 371 eşzamanlı. ⚠️ Tavana vurulduğunda parçacık sistemi <b>YENİ</b>
        /// emisyonu düşürür (çizgi havuzunun "en eskiyi kes" davranışının tersi) — duman kozmetik
        /// olduğu için bu kabul edilir, yoksa tavan aşımı eski dumanları kırpardı.
        /// </summary>
        private const int SmokeMaxParticles = 384;

        /// <summary>
        /// Duman ömrü = tracer ömrü × bu katsayı, sonra aşağıdaki banda kırpılır.
        /// <para>⚠️ Katsayı <b>küçük tutulur</b>: duman, sönen çizginin ARDINDA kalan bir tortudur,
        /// ayrı bir bulut değil. Çizgiden kat kat uzun yaşarsa (ilk denemede 6×) iz çoktan
        /// gitmişken havada asılı duran gri lekeler kalır — atışla bağı kopar ve "sis" gibi durur.
        /// Birebir de yapılmaz: çizgiyle tam aynı anda ölen duman, dağıldığını gösteremeden
        /// kaybolur.</para>
        /// <para>Kırpma bandı, playtest'te <c>tracerLifetime</c> uçlara çekilse bile dumanı
        /// kullanılabilir tutar.</para>
        /// </summary>
        private const float SmokeLifetimeScale = 2.5f;
        private const float SmokeLifetimeMinSeconds = 0.15f;
        private const float SmokeLifetimeMaxSeconds = 0.5f;

        /// <summary>Puf boyutu = tracer kalınlığı × (namluda Near, isabette Far) katsayısı.</summary>
        // Yol boyunca BÜYÜR: duman dağıldıkça genişler, bu da "namludan isabete doğru sönümlenme"
        // okumasının yarısıdır (diğer yarısı aşağıdaki alfa rampası).
        private const float SmokeSizeNearScale = 3f;
        private const float SmokeSizeFarScale = 8f;

        /// <summary>Puf alfası: namluda yoğun, isabet noktasına doğru sönümlenir.</summary>
        private const float SmokeAlphaNear = 0.4f;
        private const float SmokeAlphaFar = 0.04f;

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

        // ------------------------------------------------------------------ sönme (çizgi)

        /// <summary>
        /// Ömrün ilk bu kadarlık kısmında çizgi TAM parlaklıkta durur, sönme ondan sonra başlar.
        /// <para>⚠️ Sıfır YAPILMAZ: mermi izi önce <b>okunabilir bir çizgi</b> olmalı, sonra
        /// sönmeli. İlk kareden itibaren sönen bir iz, kısa ömürde (0.1 sn ≈ 7 kare) hiç tam
        /// parlaklık göstermez ve "soluk bir hayalet" gibi durur.</para>
        /// </summary>
        private const float FadeHoldFraction = 0.25f;

        /// <summary>Sönerken çizginin inceldiği oran (ömrün sonunda kalınlığın kaçta kaçı).</summary>
        // Yalnız alfa düşseydi iz "silikleşir" ama aynı kalınlıkta durur; incelme onu dağılıyor
        // gösterir. 1f yazmak inceltmeyi tümden kapatır.
        private const float FadeEndWidthScale = 0.55f;

        /// <summary>Havuz düğümü; LineRenderer üretim anında önbelleklenir.</summary>
        private sealed class TracerNode
        {
            public LineRenderer Line;
            public bool Active;

            /// <summary>Sönme eğrisinin ekseni: doğuş anı + toplam ömür (<c>Time.unscaledTime</c>).</summary>
            // ⚠️ Ayrı bir ExpireAt alanı TUTULMAZ — StartAt+Duration'dan türer ve aynı bilginin
            // ikinci kopyası, ömür ortada değişirse ikisinin sapmasına açık olurdu.
            public float StartAt;
            public float Duration;

            /// <summary>Sönmenin başlangıç değerleri (her karede bunlardan yeniden hesaplanır).</summary>
            // Çizgiden GERİ OKUNMAZ: okunan değer zaten sönmüş olan olurdu ve iz kare kare
            // katlanarak kaybolurdu (klasik "fade'i kendi çıktısına uygulama" hatası).
            public Color BaseColor;
            public float BaseWidth;
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
        /// Bir tetik çekişinin TÜM izlerini çizer: <paramref name="count"/> kadar çizgi (saçmalı
        /// silahta yelpaze, normal silahta tek çizgi) + yaylımın tamamına yayılan <b>tek</b> duman
        /// bütçesi. Dönüş: gerçekten çizilen çizgi sayısı.
        /// <para>
        /// <paramref name="lifetime"/> ve <paramref name="width"/> eşyanın tanımından gelir;
        /// geçersiz (≤0) değerler güvenli tabana çekilir ve duman da o düzeltilmiş değerlerden
        /// türetilir. Mesafesi çok kısa olan çizgi atlanır (yaylımın geri kalanı etkilenmez).
        /// </para>
        /// <para>
        /// ⚠️ <b>Tek mermilik ayrı bir aşırı yükleme YOKTUR</b> ve eklenmez: normal silah da bu
        /// yoldan, tek elemanlı bir dizi ile geçer. İki imza olsaydı sönme/duman/havuz davranışının
        /// tek mermide ve yaylımda sapması an meselesiydi.
        /// </para>
        /// <para>
        /// ⚠️ <b>Duman saçma başına ÇOĞALTILMAZ.</b> Tek mermilik bütçe her saçmaya ayrı ayrı
        /// verilseydi 9 saçmalı bir yaylım tek karede ~9 kat puf yayardı: parçacık tavanı
        /// (<see cref="SmokeMaxParticles"/>) tek atışta dolar, sonraki atışların dumanı sessizce
        /// düşer ve oyuncunun namlusunun önünde Quest'in fill-rate'ini yiyen opak bir duvar
        /// belirir. Bütçe bu yüzden yaylımın TAMAMINA aittir ve puf'lar saçmalar arasında sırayla
        /// dağıtılır — sonuç aynı bütçeyle koni biçimli, tek çizgininkinden daha geniş bir izdir.
        /// </para>
        /// <para>
        /// ⚠️ Çizgiler <b>ortak bir orijinden</b> çıkar (namlu) ve uçları çağıranın verdiği
        /// noktalardır: saçmaların yönünü/mesafesini bilen taraf çağırandır (yerelde gerçek
        /// ışınlar, uzakta yeniden üretilmiş yelpaze), burası yalnız çizer.
        /// </para>
        /// </summary>
        /// <param name="toPoints">Saçma uçları (dünya uzayı); ilk <paramref name="count"/> tanesi okunur.</param>
        /// <param name="count">Çizilecek saçma sayısı; <see cref="MaxScatterLines"/>'a kırpılır.</param>
        public int Play(Vector3 from, Vector3[] toPoints, int count, Color color, float width, float lifetime)
        {
            if (toPoints == null)
            {
                return 0;
            }

            count = Mathf.Min(count, Mathf.Min(toPoints.Length, MaxScatterLines));
            if (count < 1)
            {
                return 0;
            }

            Material material = EnsureMaterial();
            if (material == null)
            {
                return 0;
            }

            float w = width > 0f ? width : 0.02f;
            if (count > 1)
            {
                w *= ScatterWidthScale;
            }

            float life = lifetime > 0f ? lifetime : 0.06f;
            float now = Time.unscaledTime;

            int drawn = 0;
            float longestSqr = 0f;

            for (int i = 0; i < count; i++)
            {
                Vector3 to = toPoints[i];
                float sqrDistance = (to - from).sqrMagnitude;
                if (sqrDistance < MinTracerMeters * MinTracerMeters)
                {
                    // Tek saçma dejenere olabilir (namlu dibindeki hedef, engel yutması) — yaylımın
                    // geri kalanı bundan etkilenmez.
                    continue;
                }

                TracerNode node = TakeNode(material);
                if (node == null || node.Line == null)
                {
                    continue;
                }

                node.Line.startWidth = w;
                node.Line.endWidth = w;
                node.Line.startColor = color;
                node.Line.endColor = color;
                node.Line.SetPosition(0, from);
                node.Line.SetPosition(1, to);
                node.Line.enabled = true;

                // Time.unscaledTime: maç duraklatılıp timeScale düşse bile tracer takılı kalmasın.
                // ⚠️ Yaylımın tamamı AYNI damgayı taşır (döngü içinde yeniden okunmaz): bir tetik
                // çekişinin izleri birlikte doğup birlikte sönmeli, yoksa kare içi sapma yelpazeyi
                // sönerken parçalar.
                node.StartAt = now;
                node.Duration = life;
                node.BaseColor = color;
                node.BaseWidth = w;
                node.Active = true;

                drawn++;
                longestSqr = Mathf.Max(longestSqr, sqrDistance);
            }

            if (drawn > 0)
            {
                EmitSmoke(from, toPoints, count, Mathf.Sqrt(longestSqr), color, w, life);
            }

            return drawn;
        }

        /// <summary>
        /// Canlı çizgileri <b>söndürür</b>: alfa düşer, kalınlık incelir; ömrü dolan kapatılır
        /// (havuz düğümü yok EDİLMEZ, yalnız gizlenir).
        /// <para>
        /// ⚠️ İz eskiden ömrünün sonunda <c>enabled = false</c> ile <b>bir anda</b> kayboluyordu —
        /// göz bunu "sönme" değil "pat diye kesilme" olarak okuyor. Sönme bu yüzden ömrün
        /// KENDİSİNE yayıldı; ayrı bir sönme süresi alanı açılmadı, çünkü o zaman
        /// <c>tracerLifetime</c> "iz ne kadar durur" olmaktan çıkıp ikinci bir sayıyla
        /// pazarlık eden bir değere dönerdi.
        /// </para>
        /// <para>
        /// Maliyet: kare başına yalnız <b>canlı</b> düğümler dokunulur (tipik 3-4 tane) ve
        /// dokunulan şey vertex rengi/kalınlığı — materyal örneği açılmaz, SRP batch'i bölünmez.
        /// </para>
        /// </summary>
        private void Update()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < _pool.Length; i++)
            {
                TracerNode node = _pool[i];
                if (node == null || !node.Active)
                {
                    continue;
                }

                if (node.Line == null)
                {
                    node.Active = false;
                    continue;
                }

                float t = (now - node.StartAt) / node.Duration;
                if (t >= 1f)
                {
                    node.Active = false;
                    node.Line.enabled = false;
                    continue;
                }

                Color color = node.BaseColor;
                color.a = node.BaseColor.a * FadeAlphaAt(t);
                node.Line.startColor = color;
                node.Line.endColor = color;

                float width = node.BaseWidth * Mathf.Lerp(1f, FadeEndWidthScale, t);
                node.Line.startWidth = width;
                node.Line.endWidth = width;
            }
        }

        /// <summary>
        /// Sönme eğrisi: <paramref name="t"/> 0 (doğuş) → 1 (ömrün sonu) için 1 → 0 çarpanı.
        /// <para>Doğrusal DEĞİL: kısa bir tam parlaklık payından sonra <c>1 − u²</c> ile hızlanarak
        /// söner. Doğrusal sönme "kısılan bir lamba" gibi durur; hızlanan sönme mermi izinin
        /// kendi parlaklık düşüşüne benzer ve son kareler zaten görünmez olduğu için izin
        /// "bitişi" gözle temiz okunur.</para>
        /// </summary>
        private static float FadeAlphaAt(float t)
        {
            if (t <= FadeHoldFraction)
            {
                return 1f;
            }

            float u = (t - FadeHoldFraction) / (1f - FadeHoldFraction);
            return 1f - u * u;
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
        /// ⚠️ Bütçe <b>bir tetik çekişine</b> aittir, saçmaya değil (gerekçe
        /// <see cref="Play"/>'de): puf'lar namludan uca doğru ilerlerken saçmalar arasında
        /// SIRAYLA dağıtılır, yani aynı bütçe yelpazenin tamamını tarar. Tek saçmada (normal
        /// silah) sıra tek yola düşer ve davranış eskisiyle aynıdır.
        /// </para>
        /// <para>
        /// Zamanlama için ayrı bir iş yoktur: parçacıklar kendi <c>startLifetime</c>'larıyla
        /// ölür, <c>Update</c> onlara dokunmaz. Sistem <c>useUnscaledTime</c> ile koşar —
        /// çizginin ömrü de <c>Time.unscaledTime</c> ekseninde ölçülüyor, ikisi ayrı saatte
        /// koşsa maç duraklatıldığında duman donar, çizgi kaybolurdu.
        /// </para>
        /// </summary>
        /// <param name="distance">Yaylımdaki EN UZUN yolun uzunluğu (puf sayısının ölçüsü).</param>
        private void EmitSmoke(
            Vector3 from, Vector3[] toPoints, int count, float distance, Color color, float width, float lifetime)
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

                // Saçmalar arasında sırayla: puf namludan uca ilerlerken bir sonraki saçmanın
                // yoluna geçer, böylece tek bütçe koninin tamamını tarar.
                Vector3 path = Vector3.Lerp(from, toPoints[i % count], t);

                emit.position = path + Random.insideUnitSphere * (size * SmokeJitterOfSize);
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
