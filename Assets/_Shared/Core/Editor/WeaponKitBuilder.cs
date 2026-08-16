using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using VortexArena.Core.Combat;
using Object = UnityEngine.Object;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <b>Silah kiti</b> — tablodaki silahların kitini üretir/günceller: <c>WD_&lt;Ad&gt;.asset</c>
    /// (WeaponDefinition), mevcut <c>WPN_&lt;Ad&gt;.prefab</c>'ların bağları/VFX'i,
    /// <c>FX_RemoteShot.prefab</c>, ön kabza göstergesi (<c>VA_GripIndicator.prefab</c>) ve
    /// <c>Resources/WeaponCatalog.asset</c>.
    /// <para>
    /// <b>Ayrı bir menü öğesi YOKTUR:</b> <c>Tools &gt; VortexArena &gt; Build &gt; Configure All Build
    /// Elements</c> her eşitlemede (<c>BuildElementsConfigurator.SyncAll</c> — "Hepsini Yapılandır" ve
    /// "Yalnız Senkronize Et") <see cref="BuildAll"/>'ı koşar; "Hazırlık" bölümü de durumunu
    /// (<see cref="AreWeaponsReady"/>) gösterir. Yani tabloya silah eklemek / kiti tazelemek =
    /// o pencerede senkronize etmek. Koşu idempotenttir; her koşuda değişmeyen asset'ler aynı
    /// içerikle yeniden yazılır (diff üretmez).
    /// </para>
    /// <para>
    /// <b>WPN prefabı YOKTAN üretilmez:</b> gövde (model hiyerarşisi, Muzzle/MuzzleFlash/Eject
    /// yerleşimi) elle ayarlanan bir şeydir ve prefab repoda yaşar; araç onu yerinde
    /// günceller. Prefab yoksa hata basılır — sessizce yanlış yerleşimli bir silah üretmek
    /// (ör. Muzzle'ı Model'in altından köke almak) geri tepmeyi ve nişanı bozuyordu.
    /// </para>
    /// <para>
    /// <b>Idempotent:</b> tekrar koşulduğunda mevcut asset'ler yerinde güncellenir
    /// (GUID korunur; SaveAsPrefabAsset var olan yola yazar, CreateAsset yalnız yoksa çağrılır).
    /// </para>
    /// <para>
    /// <b>Dialog YOK:</b> pipeline'ı kilitlememek için EditorUtility.DisplayDialog kullanılmaz;
    /// tüm çıktı Debug.Log/LogWarning/LogError ile konsola yazılır.
    /// </para>
    /// <para>
    /// <b>Tip çözümü:</b> Bu asmdef yalnız VortexArena.Core'u referanslar. Weapon /
    /// WeaponAudio / WeaponDefinition derleme zamanında bağlanır (Core/Combat'ta yaşıyorlar);
    /// WeaponAnimator, WeaponReloadGesture, WeaponCatalog,
    /// TMPro.TextMeshPro, Oculus Grabbable ve MetaXRAudioSource ise TİP ADIYLA çalışma
    /// zamanında bulunur — tip/alan bulunamazsa uyarı basılır ve devam edilir
    /// (sözleşme kayması teşhisi için).
    /// </para>
    /// <para>
    /// <b>Silaha özgü his:</b> her silahın kendi ateş/reload/dry-fire klipleri (Assets/Audio/Weapons),
    /// silaha özgü namlu alevi (renk/boyut/koni açısı) ve namlu dumanı (MuzzleFlash altında
    /// "Smoke" alt-parçacık sistemi, sub-emitter ile tetiklenir) üretilir. Ayrıca her WPN'e bir
    /// <c>Eject</c> noktası + <see cref="ShellEjector"/> bileşeni eklenir ve kalibreye göre
    /// (762x39/556x45) paylaşılan <c>Casing_*.prefab</c>'a bağlanır — ateşte kovan fırlar.
    /// </para>
    /// </summary>
    public static class WeaponKitBuilder
    {
        // ------------------------------------------------------------ sabitler

        /// <summary>Pack klasörü ileride taşınırsa yalnız bu satır değişir.</summary>
        private const string PackRoot = "Assets/ThirdPartyPackages/Low Poly AR Weapon Pack 1";

        private const string DataDir = "Assets/_Shared/Arsenal/Data";
        private const string PrefabDir = "Assets/_Shared/Arsenal/Prefabs";
        private const string FxDir = "Assets/_Shared/FX";
        private const string FxPrefabPath = FxDir + "/FX_RemoteShot.prefab";
        private const string SmokeMaterialPath = FxDir + "/M_MuzzleSmoke.mat";
        private const string CatalogDir = "Assets/_Shared/Data/Resources";
        private const string CatalogPath = CatalogDir + "/WeaponCatalog.asset";

        /// <summary>Silah çerçevesi prefabı — her WPN'in altına ÖRNEK olarak konur
        /// (bkz. <see cref="ApplyWeaponFrameKit"/>). Bu araç onu üretmez, yalnız bağlar.</summary>
        private const string WeaponFramePrefabPath = PrefabDir + "/VA_WeaponFrame.prefab";

        /// <summary>
        /// Ön kabza göstergesinin SANATI — tüm silahların paylaştığı tek prefab; <c>Weapon</c> onu
        /// boş elin yaklaştığı ön kabzaya koyar (<c>WeaponCatalog.secondaryGripIndicatorPrefab</c>).
        /// <para>Bu araç prefabı <b>yalnız yoksa</b> üretir (<see cref="EnsureGripIndicatorPrefab"/>:
        /// ince bir halka) ve kataloğa <b>yalnız alan boşsa</b> bağlar — sanatçı halkayı yerinde
        /// değiştirebilir ya da kataloğa başka bir prefab bağlayabilir, araç ikisini de ezmez.</para>
        /// </summary>
        private const string GripIndicatorPrefabPath = PrefabDir + "/VA_GripIndicator.prefab";
        private const string GripIndicatorMaterialDir = "Assets/_Shared/Materials";
        private const string GripIndicatorMaterialPath = GripIndicatorMaterialDir + "/M_GripIndicator.mat";

        // Varsayılan halkanın ölçüleri (yalnız ilk üretimde okunur; sonrası prefabın kendisidir).
        // Yarıçap "kabul mesafesi" DEĞİLDİR (o WD_*'da, silah başına ve çok daha büyüktür) — bu,
        // oyuncunun elini götüreceği NOKTAYI işaretleyen görselin büyüklüğüdür.
        private const float GripIndicatorRingRadius = 0.035f;
        private const int GripIndicatorRingSegments = 20;
        private const float GripIndicatorRingWidth = 0.004f;

        /// <summary>Halka materyalinin shader arama zinciri (ilk bulunan). "Sprites/Default" başta:
        /// vertex rengini çarpar (LineRenderer.startColor işler) ve materyal ASSET olarak durduğu
        /// için build'e kesin girer.</summary>
        private static readonly string[] GripIndicatorShaderCandidates =
        {
            "Sprites/Default",
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
        };

        /// <summary>Silah ele gelirken oynayan çözülme materyali — her WPN köküne takılan
        /// <see cref="SimpleWeaponDissolve"/>'a bağlanır (bkz. <see cref="ApplyDissolveKit"/>).
        /// Bu araç onu üretmez, yalnız bağlar.</summary>
        private const string DissolveMaterialPath = "Assets/_Shared/Materials/DissolveEffect.mat";

        // Çözülme geçişinin süresi. ⚠️ Prefabda elle değiştirilse bile araç bir sonraki koşuda
        // GERİ YAZAR (denge sayılarıyla aynı kural) — kalıcı ayar bu satırdır. Sahnede deneme
        // yaparken Play modunda bileşen üstünden oynanır, beğenilen değer buraya işlenir.
        // ⚠️ Materyal alanı bu kuralın DIŞINDADIR (yalnız boşsa yazılır): bir silaha başka bir
        // çözülme materyali bağlamak (ör. VoronoiDissolveEffect) bilinçli bir tercihtir.
        // ⚠️ Efektin GÖRÜNÜM ayarları (kenar, desen, eksen) burada YOKTUR ve eklenmez: onların tek
        // doğruluk kaynağı materyalin kendisidir, bileşen yalnız _Dissolve'u sürer.
        private const float DissolveAppearSeconds = 1.2f;

        private const float CasingMassKg = 0.01f;

        /// <summary>
        /// Prefabın içinde kalmış <b>kavrama poz düğümü</b> ağacının adı — ölü veri, silinir
        /// (bkz. <see cref="RemoveLegacyGripPoseNodes"/>).
        /// <para>⚠️ Adın tanımı BURADADIR çünkü tek okuyucu bu temizliktir: düğümü üreten taraf
        /// yok, tüketen taraf yok. Sabiti runtime'a taşımak, hiçbir şeyin okumadığı bir adı ortak
        /// koda koymak olurdu. (Stüdyo elinin öneki bunun tersidir: onu ÜRETEN taraf yaşıyor, bu
        /// yüzden tanımı orada — <see cref="GripPoseStudio.HAND_ROOT_PREFIX"/> — durur.)</para>
        /// </summary>
        private const string LegacyGripPoseRootName = "GripPoses";

        /// <summary>
        /// Kovan aileleri: <c>WeaponSpec.CasingFamily</c> → (üretilecek kovan prefabı, pack'teki
        /// mermi modeli). Yeni bir kalibre eklemek = buraya bir satır; kovan prefabı ilk koşuda
        /// üretilir, sonra dokunulmaz.
        /// <para>⚠️ Aile <b>görsel</b> bir ayrımdır, denge kolu değil: 1 cm'lik ve iki saniye
        /// yaşayan bir obje için tabanca kalibrelerini (9x19 · .45 ACP · 5.7x28) tek kovanda
        /// toplamak bilinçlidir — üçe bölmek üç asset ve üç satır maliyetine görünmeyen bir fark
        /// üretirdi.</para>
        /// </summary>
        private static readonly Dictionary<string, (string CasingPath, string PackBulletPath)> CasingFamilies =
            new Dictionary<string, (string, string)>
            {
                ["762x39"] = (PrefabDir + "/Casing_762x39.prefab", PackRoot + "/Prefabs/Bullets/Bullet_A.prefab"),
                ["556x45"] = (PrefabDir + "/Casing_556x45.prefab", PackRoot + "/Prefabs/Bullets/Bullet_B.prefab"),
                ["9x19"] = (PrefabDir + "/Casing_9x19.prefab", PackRoot + "/Prefabs/Bullets/Bullet_SMG_A.prefab"),
                ["12gauge"] = (PrefabDir + "/Casing_12gauge.prefab", PackRoot + "/Prefabs/Bullets/Bullet_ShotGun_A.prefab"),
            };

        private const string Log = "[WeaponKit] ";

        // Tüm silahlarda ortak sayılar (tablo başlığındaki varsayılanlar).
        //
        // ⚠️ Kafa çarpanı satır bazında EZİLEBİLİR (`WeaponSpec.Headshot`) ve saçmalılarda ezilir:
        // çarpan saçma BAŞINA uygulandığı için 4×, 26 hasarlı tek bir saçmayı anında öldürücü
        // yapardı — 9 saçmalık bir konide 8 m'den gelen kaza kurşunu da dahil. CS'te bunu kask
        // yumuşatıyor, burada zırh YOK.
        private const float DefaultHeadshotMultiplier = 4f;

        // Bölge çarpanları (CS2 modeli): kollar GÖVDE sayılır, yani 1× ayrı bir sabit istemez.
        // ⚠️ Denge sayılarının tek doğruluk kaynağı bu tablodur — WD_*.asset'te Inspector'dan
        // değiştirilen değer bir sonraki koşuda GERİ YAZILIR.
        private const float StomachMultiplier = 1.25f;
        private const float LegMultiplier = 0.75f;
        private const float KickBackMeters = 0.02f;
        private const float RecoilRecoverSpeed = 10f;
        private const float PitchJitter = 0.05f;

        /// <summary>Satırında <c>ReserveMode</c> yazmayan silahın rezerv kuralı (ürün varsayılanı).</summary>
        private const string DefaultReserveModeName = "DiscardMagazine";

        /// <summary>Satırında <c>SpareMags</c> yazmayan silahın yedek şarjör sayısı.</summary>
        private const int DefaultSpareMagazines = 2;

        // ---------------------------------------------------------- silah tablosu

        private struct WeaponSpec
        {
            public string Name;        // dosya eki: WD_<Name>, WPN_<Name>

            /// PackRoot/Prefabs/Weapons/<PackPrefab>.prefab — üretimde ARTIK OKUNMAZ (WPN prefabları
            /// yerinde güncellenir, modelden yeniden kurulmaz); hangi silahın hangi pack modelinden
            /// geldiğinin köken kaydı olarak duruyor.
            public string PackPrefab;
            public string WeaponId;
            public string DisplayName;

            /// ⚠️ Telde giden ağ kimliği (Docs/ArenaNet-Protokol.md §6.6) — snapshot'ta bu bayt
            /// gider. KARARLI olmak zorundadır: bu tabloda satır sırası değişirse veya bir silah
            /// silinirse KALAN silahların kimliği DEĞİŞMEMELİDİR (katalog dizi indeksi kimlik
            /// olarak kullanılmadığının sebebi budur). Yeni silah = kullanılmamış bir sayı;
            /// silinen silahın sayısı geri kullanılmaz. 0 geçersizdir (§6.6'da "el boş" rezervi).
            public int NetItemId;

            /// ItemHoldMode adı: "OneHand" (tabanca/bomba) | "TwoHand" (tüfek).
            public string HoldMode;
            /// ⚠️ Saçmalıda bu sayı <b>SAÇMA BAŞINA</b> hasardır, tetik başına değil
            /// (CS2 modeli): toplam hasar isabet eden saçma sayısından doğar.
            public int Damage;

            /// Kafa vuruşu çarpanı. <b>0 = varsayılan</b>
            /// (<see cref="DefaultHeadshotMultiplier"/>). Yalnız saçmalılarda doldurulur —
            /// gerekçe o sabitin yanında.
            public float Headshot;

            public int Rpm;
            public int Magazine;
            public float Reload;

            /// Tek tetik çekişinde atılan ışın sayısı. <b>0 ya da 1 = normal silah</b>; yalnız
            /// saçmalıda doldurulur (XM1014 6, Nova 9).
            public int Pellets;

            /// Yedek şarjör sayısı. <b>0 = varsayılan</b> (<see cref="DefaultSpareMagazines"/>).
            /// CS2'nin rezerv cephanesi şarjör boyuna bölünerek bulunur (P90 100/50 = 2).
            public int SpareMags;

            /// <c>WeaponReserveMode</c> adı. <b>null = varsayılan</b>
            /// (<see cref="DefaultReserveModeName"/> = şarjör bazlı). Tek tek fişek dolduran
            /// silahlarda (pompalı) <c>"PoolRounds"</c> yazılır: erken reload'da namludaki fişek
            /// yanmaz.
            public string ReserveMode;

            /// Hitscan menzili (metre). ⚠️ Bu bir DENGE kolu DEĞİLDİR ve öyle kullanılmaz:
            /// mesafe duvarı keskindir (bir santim ötede hasar SIFIR), yani sürekli bir eğrinin
            /// yerini tutamaz — ayarlanacak silah varsa kolu <see cref="BaseSpread"/>'dir.
            /// Sıralama CS'in "range modifier" kimliğini korur (uzun namlu daha uzağa) ve daha
            /// büyük mekanlar açıldığında anlam kazanır. Bugünkü band 18-50 m; 12×12 arenanın en
            /// uzun hattı ~17 m olduğu için duvara pratikte yalnız en kısa menzilli silahlar,
            /// yalnız daha büyük mekanlarda değer. Mesafeyle gerçekten hissedilen fark
            /// SAÇILIMDAN gelir (bkz. <see cref="BaseSpread"/>).
            public float Range;
            public float BaseSpread;
            public float BloomPerShot;
            public float MaxBloom;
            public float BloomRecovery;
            public float Kick;

            /// Ateş sesinin perdesi. ⚠️ 1.00'dan sapma yalnız **ödünç klibi maskelemek** için
            /// vardır (ör. pompalıya AK sesini kalınlaştırarak vermek): o silaha kendi ses dosyası
            /// bağlandığında bu değer 1.00'a geri alınır, yoksa gerçek ses yanlış perdeden çalar.
            public float PitchBase;

            public float Volume;
            public Color FlashColorMin;
            public Color FlashColorMax;
            public float FlashSizeMin;
            public float FlashSizeMax;
            public float FlashLifetime;
            public float FlashConeAngle;
            public float SmokeSizeMin;
            public float SmokeSizeMax;
            public float SmokeLifetime;
            public float SmokeAlpha;
            public string CasingFamily; // "762x39" | "556x45"
        }

        // ⚠️ MODEL ↔ KİMLİK bağı bu tablodadır ve PackPrefab satırdan AYRILMAZ. Paketin
        // modelleri jenerik adlarla geliyor (AR_A_1, AR_B …); hangi modelin hangi gerçek
        // silaha benzediği GÖZLE tespit edildi. Bir satırın PackPrefab'ını değiştirmek
        // "silahın modeli değişsin" demektir — istatistikleri taşımak için satırın geri
        // kalanını taşı, PackPrefab/NetItemId'yi değil.
        //
        // Denge kaynağı: CS:GO/CS2'de karşılığı olanlar (AK-47, M4A4/M4A1, FAMAS) doğrudan oradan;
        // olmayanlar (SCAR-L, G36C) PUBG + gerçek hayat teknik verisinden.
        //
        // ⚠️ Reload süresi (`Reload`) silahın reload SESİNİN uzunluğudur: tetiğin açılma anı
        // (`Weapon.reloadEndTime`) ile ses ve şarjör animasyonu aynı anda bitsin diye. Klip
        // değişirse bu sayı da değişir — yoksa animasyon erken biter ve oyuncu "bitti ama
        // sıkamıyorum" hisseder. Kendi reload sesi olmayan silahlarda (AK-47, pompalılar) sayı
        // denge değeridir ve sesle eşleşmez.
        private static readonly WeaponSpec[] Specs =
        {
            // AR_M — CS:GO M4A4 gövdesi: dengeli, orta geri tepme, en yaygın 5.56.
            new WeaponSpec
            {
                Name = "M4A4", PackPrefab = "AR_M", WeaponId = "m4a4", DisplayName = "M4A4",
                NetItemId = 1, HoldMode = "TwoHand",
                Damage = 33, Rpm = 666, Magazine = 30, Reload = 2.19f,
                Range = 40f, BaseSpread = 0.50f, BloomPerShot = 0.26f,
                MaxBloom = 2.2f, BloomRecovery = 4.5f, Kick = 2.0f, PitchBase = 1.00f, Volume = 1.0f,
                FlashColorMin = new Color(1f, 0.92f, 0.72f), FlashColorMax = new Color(1f, 0.65f, 0.32f),
                FlashSizeMin = 0.035f, FlashSizeMax = 0.065f, FlashLifetime = 0.06f, FlashConeAngle = 24f,
                SmokeSizeMin = 0.035f, SmokeSizeMax = 0.06f, SmokeLifetime = 1.0f, SmokeAlpha = 0.25f,
                CasingFamily = "556x45",
            },
            // AR_B — CS:GO AK-47: tek gövde vuruşu en yüksek, kafa vuruşu kralı, en sert geri tepme.
            // 7.62x39 olduğu için kovan ailesi de diğerlerinden ayrı.
            new WeaponSpec
            {
                Name = "AK47", PackPrefab = "AR_B", WeaponId = "ak47", DisplayName = "AK-47",
                NetItemId = 2, HoldMode = "TwoHand",
                Damage = 36, Rpm = 600, Magazine = 30, Reload = 2.43f,
                Range = 45f, BaseSpread = 0.60f, BloomPerShot = 0.32f,
                MaxBloom = 2.6f, BloomRecovery = 4.0f, Kick = 2.6f, PitchBase = 1.00f, Volume = 1.0f,
                FlashColorMin = new Color(1f, 0.55f, 0.15f), FlashColorMax = new Color(1f, 0.22f, 0.05f),
                FlashSizeMin = 0.05f, FlashSizeMax = 0.09f, FlashLifetime = 0.09f, FlashConeAngle = 34f,
                SmokeSizeMin = 0.05f, SmokeSizeMax = 0.09f, SmokeLifetime = 1.4f, SmokeAlpha = 0.35f,
                CasingFamily = "762x39",
            },
            // AR_C — PUBG SCAR-L: en kolay kontrol edilen 5.56 (en düşük geri tepme + en yavaş
            // bloom büyümesi + en hızlı toparlanma), bedeli en düşük DPS.
            // ⚠️ SUSTURUCUSU YOK — eski M4A1-S satırının kısık ses/alev değerleri bilinçle atıldı.
            new WeaponSpec
            {
                Name = "SCARL", PackPrefab = "AR_C", WeaponId = "scarl", DisplayName = "SCAR-L",
                NetItemId = 3, HoldMode = "TwoHand",
                Damage = 32, Rpm = 625, Magazine = 30, Reload = 2.06f,
                Range = 38f, BaseSpread = 0.45f, BloomPerShot = 0.20f,
                MaxBloom = 1.8f, BloomRecovery = 5.0f, Kick = 1.6f, PitchBase = 1.00f, Volume = 1.0f,
                FlashColorMin = new Color(1f, 0.85f, 0.55f), FlashColorMax = new Color(1f, 0.55f, 0.22f),
                FlashSizeMin = 0.038f, FlashSizeMax = 0.068f, FlashLifetime = 0.065f, FlashConeAngle = 26f,
                SmokeSizeMin = 0.035f, SmokeSizeMax = 0.06f, SmokeLifetime = 1.0f, SmokeAlpha = 0.28f,
                CasingFamily = "556x45",
            },
            // AR_D — PUBG G36C: en yüksek atış hızı (750 rpm), en düşük mermi başı hasar,
            // en kısa menzil. Yakın mesafe baskı silahı.
            new WeaponSpec
            {
                Name = "G36C", PackPrefab = "AR_D", WeaponId = "g36c", DisplayName = "G36C",
                NetItemId = 4, HoldMode = "TwoHand",
                Damage = 29, Rpm = 750, Magazine = 30, Reload = 2.43f,
                Range = 28f, BaseSpread = 0.70f, BloomPerShot = 0.30f,
                MaxBloom = 2.6f, BloomRecovery = 4.2f, Kick = 1.9f, PitchBase = 1.00f, Volume = 0.95f,
                FlashColorMin = new Color(1f, 0.80f, 0.45f), FlashColorMax = new Color(1f, 0.45f, 0.15f),
                FlashSizeMin = 0.042f, FlashSizeMax = 0.075f, FlashLifetime = 0.07f, FlashConeAngle = 28f,
                SmokeSizeMin = 0.04f, SmokeSizeMax = 0.07f, SmokeLifetime = 1.1f, SmokeAlpha = 0.30f,
                CasingFamily = "556x45",
            },
            // AR_E — CS:GO FAMAS: değerler CS:GO ile birebir doğrulandı (30 hasar / 666 rpm /
            // 25 şarjör / 3.30 s reload). ⚠️ 25'lik şarjör bilinçli — CS:GO'da öyle; diğer beş
            // silahın 30'undan bu yüzden ayrılıyor. Burst kipi modellenmedi.
            new WeaponSpec
            {
                Name = "FAMAS", PackPrefab = "AR_E", WeaponId = "famas", DisplayName = "FAMAS",
                NetItemId = 5, HoldMode = "TwoHand",
                Damage = 30, Rpm = 666, Magazine = 25, Reload = 2.38f,
                Range = 32f, BaseSpread = 0.65f, BloomPerShot = 0.28f,
                MaxBloom = 2.4f, BloomRecovery = 4.2f, Kick = 1.9f, PitchBase = 1.03f, Volume = 1.0f,
                FlashColorMin = new Color(1f, 0.88f, 0.58f), FlashColorMax = new Color(1f, 0.52f, 0.18f),
                FlashSizeMin = 0.03f, FlashSizeMax = 0.055f, FlashLifetime = 0.06f, FlashConeAngle = 20f,
                SmokeSizeMin = 0.035f, SmokeSizeMax = 0.06f, SmokeLifetime = 1.0f, SmokeAlpha = 0.28f,
                CasingFamily = "556x45",
            },
            // AR_A_1 — M4A1: M4A4'ün "nişancı" varyantı. En dar taban saçılım + en uzun menzil,
            // bedeli en hızlı bozulan seri atış (en yüksek bloom, en yavaş toparlanma). Tek tek
            // nişan alana ödül, tarayana ceza. Reload sesi M4A4 ile ORTAK ama pitch düşük.
            new WeaponSpec
            {
                Name = "M4A1", PackPrefab = "AR_A_1", WeaponId = "m4a1", DisplayName = "M4A1",
                NetItemId = 6, HoldMode = "TwoHand",
                Damage = 31, Rpm = 700, Magazine = 30, Reload = 2.19f,
                Range = 50f, BaseSpread = 0.35f, BloomPerShot = 0.34f,
                MaxBloom = 2.8f, BloomRecovery = 3.8f, Kick = 2.3f, PitchBase = 0.93f, Volume = 1.0f,
                FlashColorMin = new Color(1f, 0.90f, 0.74f), FlashColorMax = new Color(0.95f, 0.60f, 0.28f),
                FlashSizeMin = 0.032f, FlashSizeMax = 0.058f, FlashLifetime = 0.062f, FlashConeAngle = 19f,
                SmokeSizeMin = 0.032f, SmokeSizeMax = 0.055f, SmokeLifetime = 0.95f, SmokeAlpha = 0.26f,
                CasingFamily = "556x45",
            },
            // AR_O — CS2 AUG: değerler CS2 ile birebir (28 hasar / 666 rpm / 30 şarjör / 3.80 s
            // reload / 0.98 range modifier → AK ile aynı menzil sınıfı). Bullpup + dürbün kimliği
            // saçılımda: tabanı SCAR-L'den dar, bloom'u en yavaş büyüyenlerden ve geri tepmesi
            // düşük — bedeli 5.56'nın en düşük DPS'i ve en uzun reload'ı.
            new WeaponSpec
            {
                Name = "AUG", PackPrefab = "AR_O", WeaponId = "aug", DisplayName = "AUG",
                NetItemId = 7, HoldMode = "TwoHand",
                Damage = 28, Rpm = 666, Magazine = 30, Reload = 2.19f,
                Range = 46f, BaseSpread = 0.42f, BloomPerShot = 0.22f,
                MaxBloom = 2.0f, BloomRecovery = 4.8f, Kick = 1.7f, PitchBase = 0.98f, Volume = 1.0f,
                FlashColorMin = new Color(1f, 0.87f, 0.60f), FlashColorMax = new Color(1f, 0.58f, 0.24f),
                FlashSizeMin = 0.033f, FlashSizeMax = 0.060f, FlashLifetime = 0.062f, FlashConeAngle = 22f,
                SmokeSizeMin = 0.034f, SmokeSizeMax = 0.058f, SmokeLifetime = 1.0f, SmokeAlpha = 0.27f,
                CasingFamily = "556x45",
            },
            // AR_L — CS2 Galil AR: 30 hasar / 666 rpm / 35 şarjör / 3.00 s / 0.98 range modifier.
            // "Ucuz AK": AK'nın menzil sınıfında ama tek vuruşu zayıf, buna karşılık daha hızlı ve
            // 35'lik şarjörle en uzun seri. Bedeli 5.56'ların en geniş taban saçılımı.
            new WeaponSpec
            {
                Name = "GALIL", PackPrefab = "AR_L", WeaponId = "galilar", DisplayName = "Galil AR",
                NetItemId = 8, HoldMode = "TwoHand",
                Damage = 30, Rpm = 666, Magazine = 35, Reload = 2.25f, SpareMags = 2,
                Range = 44f, BaseSpread = 0.62f, BloomPerShot = 0.34f,
                MaxBloom = 2.8f, BloomRecovery = 3.8f, Kick = 2.5f, PitchBase = 1.02f, Volume = 1.0f,
                FlashColorMin = new Color(1f, 0.78f, 0.42f), FlashColorMax = new Color(1f, 0.42f, 0.12f),
                FlashSizeMin = 0.045f, FlashSizeMax = 0.082f, FlashLifetime = 0.075f, FlashConeAngle = 30f,
                SmokeSizeMin = 0.042f, SmokeSizeMax = 0.072f, SmokeLifetime = 1.2f, SmokeAlpha = 0.32f,
                CasingFamily = "556x45",
            },
            // SMG_O — CS2 P90: 26 hasar / 857 rpm / 50 şarjör / 3.40 s / 0.84 range modifier.
            // 50'lik şarjör + en düşük geri tepme = tarama silahı; bedeli menzil ve mermi başı hasar.
            new WeaponSpec
            {
                Name = "P90", PackPrefab = "SMG_O", WeaponId = "p90", DisplayName = "P90",
                NetItemId = 9, HoldMode = "TwoHand",
                Damage = 26, Rpm = 857, Magazine = 50, Reload = 2.80f, SpareMags = 2,
                Range = 24f, BaseSpread = 0.85f, BloomPerShot = 0.24f,
                MaxBloom = 2.6f, BloomRecovery = 5.5f, Kick = 1.2f, PitchBase = 1.00f, Volume = 0.92f,
                FlashColorMin = new Color(1f, 0.90f, 0.68f), FlashColorMax = new Color(1f, 0.62f, 0.28f),
                FlashSizeMin = 0.026f, FlashSizeMax = 0.048f, FlashLifetime = 0.05f, FlashConeAngle = 26f,
                SmokeSizeMin = 0.028f, SmokeSizeMax = 0.048f, SmokeLifetime = 0.85f, SmokeAlpha = 0.22f,
                CasingFamily = "9x19",
            },
            // SMG_M — CS2 MP9: 26 hasar / 857 rpm / 30 şarjör / 2.10 s / 0.75 range modifier.
            // Oyundaki EN HIZLI reload ve en kısa menzil: köşe tutan, sık şarjör değiştiren silah.
            new WeaponSpec
            {
                Name = "MP9", PackPrefab = "SMG_M", WeaponId = "mp9", DisplayName = "MP9",
                NetItemId = 10, HoldMode = "TwoHand",
                Damage = 26, Rpm = 857, Magazine = 30, Reload = 2.14f, SpareMags = 4,
                Range = 18f, BaseSpread = 0.90f, BloomPerShot = 0.26f,
                MaxBloom = 2.8f, BloomRecovery = 5.8f, Kick = 1.1f, PitchBase = 1.00f, Volume = 0.90f,
                FlashColorMin = new Color(1f, 0.92f, 0.72f), FlashColorMax = new Color(1f, 0.66f, 0.32f),
                FlashSizeMin = 0.024f, FlashSizeMax = 0.044f, FlashLifetime = 0.048f, FlashConeAngle = 28f,
                SmokeSizeMin = 0.026f, SmokeSizeMax = 0.045f, SmokeLifetime = 0.8f, SmokeAlpha = 0.20f,
                CasingFamily = "9x19",
            },
            // SMG_L — CS2 UMP-45: 35 hasar / 666 rpm / 25 şarjör / 3.50 s / 0.82 range modifier.
            // SMG'lerin en sert vuranı (bir tüfekten bile yüksek mermi hasarı) ama en yavaşı;
            // 25'lik şarjör hatayı affetmiyor.
            new WeaponSpec
            {
                Name = "UMP45", PackPrefab = "SMG_L", WeaponId = "ump45", DisplayName = "UMP-45",
                NetItemId = 11, HoldMode = "TwoHand",
                Damage = 35, Rpm = 666, Magazine = 25, Reload = 2.14f, SpareMags = 4,
                Range = 22f, BaseSpread = 0.75f, BloomPerShot = 0.30f,
                MaxBloom = 2.6f, BloomRecovery = 4.6f, Kick = 1.9f, PitchBase = 1.00f, Volume = 0.96f,
                FlashColorMin = new Color(1f, 0.72f, 0.34f), FlashColorMax = new Color(1f, 0.38f, 0.10f),
                FlashSizeMin = 0.032f, FlashSizeMax = 0.058f, FlashLifetime = 0.058f, FlashConeAngle = 30f,
                SmokeSizeMin = 0.032f, SmokeSizeMax = 0.055f, SmokeLifetime = 1.0f, SmokeAlpha = 0.26f,
                CasingFamily = "9x19",
            },
            // ShotGun_C — CS2 XM1014 gövdesi: 171 rpm / 7 fişek. Yarı otomatik: Nova'dan hızlı ve
            // daha affedici, tek atışı zayıf.
            // ⚠️ Reload süresi CS'te fişek fişektir; burada tam şarjörün TOPLAM süresi yazılır
            // (fişek fişek dolum modellenmiyor) — bunun karşılığı `PoolRounds`: erken reload'da
            // namludaki fişek yanmaz.
            // ⚠️ Hasar ve saçılım CS'ten SAPAR (CS: 20 hasar, ~5° koni, 0.70 range modifier) ve
            // sapmanın sebebi arena ölçeğidir — gerekçe aşağıdaki NOVA satırında, tek yerde.
            new WeaponSpec
            {
                Name = "XM1014", PackPrefab = "ShotGun_C", WeaponId = "xm1014", DisplayName = "XM1014",
                NetItemId = 12, HoldMode = "TwoHand",
                Damage = 10, Headshot = 2f, Rpm = 171, Magazine = 7, Reload = 4.50f, Pellets = 6,
                SpareMags = 4, ReserveMode = "PoolRounds",
                Range = 26f, BaseSpread = 10.0f, BloomPerShot = 0.60f,
                MaxBloom = 1.5f, BloomRecovery = 2.5f, Kick = 3.2f, PitchBase = 1.00f, Volume = 1.0f,
                FlashColorMin = new Color(1f, 0.72f, 0.30f), FlashColorMax = new Color(1f, 0.32f, 0.06f),
                FlashSizeMin = 0.075f, FlashSizeMax = 0.130f, FlashLifetime = 0.10f, FlashConeAngle = 46f,
                SmokeSizeMin = 0.075f, SmokeSizeMax = 0.125f, SmokeLifetime = 1.7f, SmokeAlpha = 0.42f,
                CasingFamily = "12gauge",
            },
            // ShotGun_B — CS2 Nova gövdesi: 68 rpm / 8 fişek. Pompalı: temas mesafesinde tek atışta
            // öldürür, ıskalarsa bir sonraki atış çok geç gelir.
            //
            // ⚠️ SAÇMALILARIN DENGE SAYILARI CS'TEN BİLİNÇLİ SAPAR (Nova'da CS: 26 hasar, ~6° koni,
            // 0.70 range modifier; burada 13 hasar, 12° koni, mesafe eğrisi yok). Sebep ARENA
            // ÖLÇEĞİDİR ve CS'in eğrisini eklemek bunu çözmez: CS'in pompalısı "3 m ender bir
            // mesafedir" varsayımıyla ayarlıdır (0.70 katsayısı ~9.5 m'de bir işler), oysa 12×12
            // free-roam arenada en uzun hat ~17 m — yani CS formülü Nova'yı arenanın öbür ucunda
            // ancak yarıya indirir, temas mesafesindeki hasara hiç dokunmaz. Hasarın mesafeyle
            // hissedilmesini burada SAÇILIM sağlıyor (Docs/Sistem-Ozeti.md §7): koni büyüdükçe
            // gövdeye değen saçma sayısı düşüyor, yani sayıların ayarlandığı yer taban hasar +
            // koni açısıdır. ⚠️ `Range` bu ayarın kolu DEĞİLDİR (alanın kendi notuna bak): keskin
            // bir mesafe duvarı, sürekli bir eğrinin yerini tutmaz.
            //
            // ⚠️ TABLODAKİ DERECE TEK ELLEDİR: iki elle tutulan silahta `twoHandSpreadMultiplier`
            // (0.45) ile ÇARPILIR, yani buradaki 12° sahada 5.4°'lik bir koni demektir. Pompalı
            // tanım gereği iki elle tutulduğu için saçmalı satırlarda GERÇEK değer daima yarıdır —
            // koni açısını tabloya bakarak tahmin etmek, silahı iki kat dar sanmaktır.
            new WeaponSpec
            {
                Name = "NOVA", PackPrefab = "ShotGun_B", WeaponId = "nova", DisplayName = "Nova",
                NetItemId = 13, HoldMode = "TwoHand",
                Damage = 13, Headshot = 2f, Rpm = 68, Magazine = 8, Reload = 5.00f, Pellets = 9,
                SpareMags = 4, ReserveMode = "PoolRounds",
                Range = 25f, BaseSpread = 12.0f, BloomPerShot = 0.60f,
                MaxBloom = 1.5f, BloomRecovery = 2.5f, Kick = 4.0f, PitchBase = 1.00f, Volume = 1.0f,
                FlashColorMin = new Color(1f, 0.68f, 0.26f), FlashColorMax = new Color(1f, 0.28f, 0.05f),
                FlashSizeMin = 0.085f, FlashSizeMax = 0.150f, FlashLifetime = 0.115f, FlashConeAngle = 50f,
                SmokeSizeMin = 0.085f, SmokeSizeMax = 0.140f, SmokeLifetime = 1.9f, SmokeAlpha = 0.46f,
                CasingFamily = "12gauge",
            },
        };

        private enum BuildOutcome { Rebound, Failed }

        private static int _warnings;

        // Kavramanın eski authoring kalıntılarından (GripSocket_Primary/Secondary işaretçileri ve
        // Hands/Hand_* el rig'i) kaçı silindi. Authoring kavrama tezgâhına geçti; bu düğümleri
        // artık kimse okumuyor ve prefabda kalmaları sonraki okuyucuya "burası hâlâ ayarlanabilir"
        // derdi.
        private static int _legacyNodesRemoved;

        // Kavraması yazılmamış silahlar (oyunda eli idle'da kalacak WPN'ler). ⚠️ Bu rapor ŞART —
        // stüdyoda kavrama yazmak tek seferlik bir insan adımı ve atlandığında hiçbir hata
        // basılmaz, belirtisi yalnız "el silaha sarılmıyor" olur.
        private static readonly List<string> _unbakedWeapons = new List<string>();

        private static readonly Dictionary<string, Type> ResolvedTypes = new Dictionary<string, Type>();

        // ------------------------------------------------------------ giriş

        /// <summary>
        /// Tam akış: WD asset'leri → WPN prefablarının güncellenmesi → FX + gösterge → katalog →
        /// ikinci geçiş. Tek satırlık özet döner (eşitleme raporuna girer); ayrıntı konsoldadır.
        /// <para>Çağıran <c>BuildElementsConfigurator.SyncAll</c>'dır (ve "Hazırlık" satırı);
        /// menü öğesi yoktur.</para>
        /// </summary>
        public static string BuildAll()
        {
            _warnings = 0;
            _legacyNodesRemoved = 0;
            _unbakedWeapons.Clear();

            int wdNew = 0;
            int wpnRebound = 0, wpnFailed = 0;
            bool fxCreated = false;
            bool indicatorCreated = false;
            bool catalogCreated = false;
            string summary;

            EnsureFolder(DataDir);
            EnsureFolder(PrefabDir);
            EnsureFolder(FxDir);
            EnsureFolder(CatalogDir);

            // Hata yarıda keserse sahnede çöp GO kalmasın diye tüm geçici instance'lar burada izlenir.
            var live = new List<GameObject>();
            try
            {
                // ---- ADIM 1: WeaponDefinition asset'leri (prefab alanı ADIM 5'te bağlanır).
                var defs = new WeaponDefinition[Specs.Length];
                for (int i = 0; i < Specs.Length; i++)
                {
                    defs[i] = EnsureDefinition(Specs[i], ref wdNew);
                }

                AssetDatabase.SaveAssets();

                // ---- ADIM 2: WPN prefabları (kovan/duman kaynak asset'leri önce, WPN'ler onlara muhtaç).
                // Yalnız TABLODA GEÇEN aileler üretilir: kullanılmayan bir kalibre için asset
                // açmak, sonradan "bu kovan neyin?" diye bakılacak ölü bir dosya bırakırdı.
                var casings = new Dictionary<string, GameObject>();
                for (int i = 0; i < Specs.Length; i++)
                {
                    string family = Specs[i].CasingFamily;
                    if (string.IsNullOrEmpty(family) || casings.ContainsKey(family))
                    {
                        continue;
                    }

                    if (!CasingFamilies.TryGetValue(family, out var source))
                    {
                        Warn(Specs[i].Name + ": '" + family + "' kovan ailesi CasingFamilies'te yok — " +
                             "kovan bağlanamayacak.");
                        continue;
                    }

                    casings[family] = EnsureCasingPrefab(source.CasingPath, source.PackBulletPath, live);
                }

                Material smokeMaterial = EnsureMuzzleSmokeMaterial();

                for (int i = 0; i < Specs.Length; i++)
                {
                    if (defs[i] == null)
                    {
                        wpnFailed++;
                        continue;
                    }

                    try
                    {
                        switch (BuildWeaponPrefab(Specs[i], defs[i], casings, smokeMaterial))
                        {
                            case BuildOutcome.Rebound: wpnRebound++; break;
                            default: wpnFailed++; break;
                        }
                    }
                    catch (Exception e)
                    {
                        wpnFailed++;
                        Debug.LogError(Log + Specs[i].Name + ": prefab güncellemesi hata verdi — " + e);
                    }
                }

                // ---- ADIM 3: uzak atış FX prefabı + ön kabza göstergesi (varsa dokunulmaz).
                fxCreated = EnsureRemoteShotFx(live);
                indicatorCreated = EnsureGripIndicatorPrefab(live);

                // ---- ADIM 4: WeaponCatalog.
                catalogCreated = UpdateCatalog();

                // ---- ADIM 5: WD.prefab ← WPN ikinci geçişi.
                LinkDefinitionPrefabs();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                summary = "silah kiti: WD " + wdNew + " yeni / " + (Specs.Length - wdNew) + " güncellendi · " +
                          "WPN " + wpnRebound + " güncellendi, " + wpnFailed + " başarısız · " +
                          "FX_RemoteShot " + (fxCreated ? "üretildi" : "mevcut") + " · " +
                          "VA_GripIndicator " + (indicatorCreated ? "üretildi" : "mevcut") + " · " +
                          "WeaponCatalog " + (catalogCreated ? "üretildi" : "güncellendi") + " · " +
                          "eski kavrama düğümü " + _legacyNodesRemoved + " silindi · " +
                          _warnings + " uyarı.";
                Debug.Log(Log + summary);

                ReportUnbakedWeapons();
                ReportSilentWeapons(defs);
            }
            finally
            {
                for (int i = 0; i < live.Count; i++)
                {
                    if (live[i] != null)
                    {
                        Object.DestroyImmediate(live[i]);
                    }
                }
            }

            return summary;
        }

        // ------------------------------------------------- ADIM 1: WD asset'leri

        /// <summary>WD_&lt;Ad&gt;.asset'i yoksa yaratır, alanları sözleşmedeki adlarla yazar.</summary>
        private static WeaponDefinition EnsureDefinition(WeaponSpec spec, ref int createdCount)
        {
            string path = DataDir + "/WD_" + spec.Name + ".asset";
            string ctx = "WD_" + spec.Name;

            var def = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
            if (def == null)
            {
                // Yolda başka tipte bir asset varsa üstüne yazma — CreateAsset GUID'i öldürür.
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                {
                    Warn(ctx + ": '" + path + "' yolunda WeaponDefinition olmayan bir asset var — dokunulmadı.");
                    return null;
                }

                def = ScriptableObject.CreateInstance<WeaponDefinition>();
                AssetDatabase.CreateAsset(def, path);
                createdCount++;
            }

            var so = new SerializedObject(def);

            SetString(so, "weaponId", spec.WeaponId, ctx);
            SetString(so, "displayName", spec.DisplayName, ctx);
            // §6.6: ağ kimliği + kaç elle tutulduğu. Tablo bunların doğruluk kaynağıdır ve her
            // koşuda EZER — kimliği Inspector'dan elle değiştirmek kalıcı olmaz, tabloyu düzenle.
            SetNumber(so, "netItemId", spec.NetItemId, ctx);
            SetEnumByName(so, "holdMode", spec.HoldMode, ctx);
            SetNumber(so, "damage", spec.Damage, ctx);
            SetNumber(so, "headshotMultiplier",
                spec.Headshot > 0f ? spec.Headshot : DefaultHeadshotMultiplier, ctx);
            SetNumber(so, "stomachMultiplier", StomachMultiplier, ctx);
            SetNumber(so, "legMultiplier", LegMultiplier, ctx);
            SetNumber(so, "fireRateRpm", spec.Rpm, ctx);
            SetNumber(so, "range", spec.Range, ctx);
            SetNumber(so, "pelletCount", spec.Pellets > 0 ? spec.Pellets : 1, ctx);
            SetNumber(so, "baseSpreadDegrees", spec.BaseSpread, ctx);
            SetNumber(so, "bloomPerShotDegrees", spec.BloomPerShot, ctx);
            SetNumber(so, "maxBloomDegrees", spec.MaxBloom, ctx);
            SetNumber(so, "bloomRecoveryPerSecond", spec.BloomRecovery, ctx);
            SetNumber(so, "kickDegrees", spec.Kick, ctx);
            SetNumber(so, "kickBackMeters", KickBackMeters, ctx);
            SetNumber(so, "recoilRecoverSpeed", RecoilRecoverSpeed, ctx);
            SetNumber(so, "magazineSize", spec.Magazine, ctx);
            SetNumber(so, "spareMagazines", spec.SpareMags > 0 ? spec.SpareMags : DefaultSpareMagazines, ctx);
            SetEnumByName(so, "reserveMode",
                string.IsNullOrEmpty(spec.ReserveMode) ? DefaultReserveModeName : spec.ReserveMode, ctx);
            SetNumber(so, "reloadTime", spec.Reload, ctx);
            // ⚠️ SES KLİPLERİ BU TABLODAN GELMEZ ve bu araç onlara HİÇ dokunmaz — beş klip alanının
            // (fireClips · magOutClip · magInClip · dryFireClip · pickupClip) tek doğruluk kaynağı
            // WD_<Ad>.asset'in Inspector'ıdır, klip oraya elle sürüklenir.
            // Gerekçe haptik alanlarınınkiyle aynıdır (aşağı bak): ses kulakla seçilen bir şeydir,
            // dosya adını koda yazmak onu iki yerden yönetilir yapar. Tabloda tutulduğunda kural
            // "yalnız alan boşsa yaz" olmak zorundaydı — yani bir sesi değiştirmek için önce
            // asset'teki alanı boşaltmak gerekiyordu ve bunu bilmeyen "değişiklik inmedi" sanıyordu.
            // Bedeli: yeni silah SESSİZ doğar. Onu ReportSilentWeapons koşu sonunda listeler.
            SetNumber(so, "firePitchBase", spec.PitchBase, ctx);
            SetNumber(so, "firePitchJitter", PitchJitter, ctx);
            SetNumber(so, "fireVolume", spec.Volume, ctx);
            // ⚠️ hapticAmplitude / hapticDuration BİLEREK yazılmaz ve bu tabloya alınmaz: vuruş
            // hissi gözlükle deneyerek ayarlanan bir değerdir, Inspector'da tutulur. Buraya bir
            // satır eklemek, her koşuda o elle bulunmuş ayarı sessizce ezerdi. Yeni WD asset'i
            // sınıftaki varsayılanlarla doğar (bkz. WeaponDefinition).
            // "prefab" alanı ADIM 5'te (WPN üretildikten sonra) bağlanır.

            so.ApplyModifiedPropertiesWithoutUndo();
            return def;
        }

        // ------------------------------------------------ ADIM 2: WPN prefabları

        /// <summary>
        /// Mevcut WPN_&lt;Ad&gt;.prefab'ı yerinde günceller (<see cref="RebindExistingPrefab"/>):
        /// definition bağları + namlu alevi/duman/kovan kiti + kavrama kiti. Gövdeye
        /// (model, Muzzle/MuzzleFlash/Eject konumu) DOKUNULMAZ — onlar elle ayarlanır.
        /// Prefab yoksa üretilmez, hata basılır: eksik silahın prefabı repoya elle eklenir.
        /// </summary>
        private static BuildOutcome BuildWeaponPrefab(WeaponSpec spec, WeaponDefinition def,
            Dictionary<string, GameObject> casings, Material smokeMaterial)
        {
            string wpnPath = PrefabDir + "/WPN_" + spec.Name + ".prefab";
            string ctx = "WPN_" + spec.Name;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(wpnPath) == null)
            {
                Debug.LogError(Log + ctx + ": '" + wpnPath + "' yok — bu araç WPN prefabı ÜRETMEZ, " +
                               "yalnız mevcudu günceller. Prefabı repoya ekleyip tekrar çalıştır.");
                return BuildOutcome.Failed;
            }

            RebindExistingPrefab(wpnPath, spec, def, casings, smokeMaterial, ctx);
            return BuildOutcome.Rebound;
        }

        /// <summary>
        /// WPN içeriğini açıp definition bağlarını tazeler VE silaha özgü namlu alevi/duman/kovan
        /// kitini (<see cref="ApplyVfxAndShellKit"/>) uygular — aracın TEK üretim yolu budur;
        /// model/Muzzle konumuna DOKUNULMAZ (elle ayarlanmış olabilir).
        /// </summary>
        private static void RebindExistingPrefab(string wpnPath, WeaponSpec spec, WeaponDefinition def,
            Dictionary<string, GameObject> casings, Material smokeMaterial, string ctx)
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(wpnPath);
            try
            {
                var weapon = contents.GetComponent<Weapon>();
                if (weapon != null)
                {
                    var so = new SerializedObject(weapon);
                    SetObjectRef(so, "definition", def, ctx);
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                else
                {
                    Warn(ctx + ": mevcut prefabda Weapon bileşeni yok — definition bağlanamadı.");
                }

                var weaponAudio = contents.GetComponent<WeaponAudio>();
                if (weaponAudio != null)
                {
                    var so = new SerializedObject(weaponAudio);
                    var defProp = so.FindProperty("definition");
                    if (defProp != null)
                    {
                        defProp.objectReferenceValue = def;
                    }

                    var srcProp = so.FindProperty("source");
                    if (srcProp != null && srcProp.objectReferenceValue == null)
                    {
                        Transform muzzleT = FindDeepChild(contents.transform, "Muzzle");
                        if (muzzleT != null)
                        {
                            srcProp.objectReferenceValue = muzzleT.GetComponent<AudioSource>();
                        }
                    }

                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                if (weapon != null)
                {
                    ApplyVfxAndShellKit(contents, spec, casings, smokeMaterial, ctx);
                }

                // Tek çalışan yol burası — kavrama kiti de burada uygulanmazsa mevcut WPN'ler
                // mesafeden kavranabilir kalırdı.
                ApplyGripKit(contents, def, ctx);
                ApplyWeaponFrameKit(contents, ctx);
                ApplyDissolveKit(contents, ctx);

                PrefabUtility.SaveAsPrefabAsset(contents, wpnPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        // ------------------------------------------------ ADIM 3: FX_RemoteShot

        /// <summary>
        /// FX_RemoteShot.prefab yoksa üretir (varsa dokunmaz): bulunan ilk WPN prefabının
        /// Muzzle'ındaki AudioSource + MetaXRAudioSource köke kopyalanır, MuzzleFlash "Flash"
        /// adlı child olarak klonlanır. Döner: bu koşuda üretildi mi.
        /// </summary>
        private static bool EnsureRemoteShotFx(List<GameObject> live)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FxPrefabPath) != null)
            {
                return false; // varsa dokunma
            }

            GameObject sourcePrefab = null;
            for (int i = 0; i < Specs.Length && sourcePrefab == null; i++)
            {
                sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/WPN_" + Specs[i].Name + ".prefab");
            }

            if (sourcePrefab == null)
            {
                Warn("FX_RemoteShot: kaynak yok (hiçbir WPN prefabı bulunamadı) — üretilemedi.");
                return false;
            }

            var srcInst = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
            live.Add(srcInst);
            var fxRoot = new GameObject("FX_RemoteShot");
            live.Add(fxRoot);

            Transform muzzleT = FindDeepChild(srcInst.transform, "Muzzle");
            if (muzzleT != null)
            {
                // Önce AudioSource (MetaXRAudioSource ona muhtaç), sonra MetaXRAudioSource.
                var src = muzzleT.GetComponent<AudioSource>();
                if (src != null)
                {
                    ComponentUtility.CopyComponent(src);
                    ComponentUtility.PasteComponentAsNew(fxRoot);
                }
                else
                {
                    Warn("FX_RemoteShot: kaynağın Muzzle'ında AudioSource yok.");
                }

                // MetaXRAudioSource tipine derleme referansı YOK — tip adı üzerinden kopyala.
                Component[] comps = muzzleT.GetComponents<Component>();
                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] != null && comps[i].GetType().Name == "MetaXRAudioSource")
                    {
                        ComponentUtility.CopyComponent(comps[i]);
                        ComponentUtility.PasteComponentAsNew(fxRoot);
                    }
                }
            }
            else
            {
                Warn("FX_RemoteShot: kaynakta 'Muzzle' child'ı yok — ses bileşenleri kopyalanamadı.");
            }

            Transform flashT = FindDeepChild(srcInst.transform, "MuzzleFlash");
            if (flashT != null)
            {
                GameObject flash = Object.Instantiate(flashT.gameObject, fxRoot.transform);
                flash.name = "Flash";
                flash.transform.localPosition = Vector3.zero;
                flash.transform.localRotation = Quaternion.identity;
            }
            else
            {
                Warn("FX_RemoteShot: kaynakta 'MuzzleFlash' child'ı yok — flash kopyalanamadı.");
            }

            PrefabUtility.SaveAsPrefabAsset(fxRoot, FxPrefabPath, out bool saved);
            Object.DestroyImmediate(fxRoot);
            Object.DestroyImmediate(srcInst);

            if (!saved)
            {
                Debug.LogError(Log + "FX_RemoteShot: SaveAsPrefabAsset başarısız: " + FxPrefabPath);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Ön kabza göstergesinin prefabını YOKSA üretir (varsa dokunmaz): tek bir
        /// <see cref="LineRenderer"/> halka (yerel XY düzleminde, kapalı, ince şerit).
        /// <para>
        /// <b>Neden prefab:</b> gösterge SANATTIR ve sanatın yeri prefabtır — <c>Weapon</c> yalnız
        /// yerini, ölçeğini ve rengini sürer. Halka başlangıçtır: sanatçı bu prefabı yerinde
        /// değiştirebilir (araç bir daha dokunmaz) ya da kataloğa bambaşka bir prefab bağlayabilir.
        /// Renk çalışma anında yazıldığı için burada BEYAZ bırakılır.
        /// </para>
        /// <para>⚠️ Materyal ASSET olarak üretilir (<see cref="GripIndicatorMaterialPath"/>), çalışma
        /// anında <c>Shader.Find</c> ile DEĞİL: hiçbir asset'in referanslamadığı shader build'den
        /// striplenir ve gösterge sahada sessizce çizilmez.</para>
        /// </summary>
        private static bool EnsureGripIndicatorPrefab(List<GameObject> live)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(GripIndicatorPrefabPath) != null)
            {
                return false; // varsa dokunma
            }

            Material material = EnsureGripIndicatorMaterial();
            if (material == null)
            {
                Warn("VA_GripIndicator: halka için shader bulunamadı (Sprites/Default dahil) — gösterge " +
                     "prefabı üretilemedi, katalogda alan boş kalır (gösterge çizilmez).");
                return false;
            }

            var root = new GameObject("VA_GripIndicator");
            live.Add(root);

            var line = root.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            // Yerel uzay + loop: halka köşeleri bir kez yazılır, çalışma anında yalnız kök taşınır.
            line.useWorldSpace = false;
            line.loop = true;
            line.numCapVertices = 0;
            line.numCornerVertices = 0;
            line.textureMode = LineTextureMode.Stretch;
            // Şeridin KALINLIĞI kameraya baksın (ince bant yandan yok olmasın). Halkanın DÜZLEMİ ise
            // çalışma anında kökün kameraya çevrilmesiyle çözülür (Weapon.IndicatorRotation).
            line.alignment = LineAlignment.View;
            line.startWidth = GripIndicatorRingWidth;
            line.endWidth = GripIndicatorRingWidth;
            line.startColor = Color.white;
            line.endColor = Color.white;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            line.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            var points = new Vector3[GripIndicatorRingSegments];
            for (int i = 0; i < GripIndicatorRingSegments; i++)
            {
                float angle = i / (float)GripIndicatorRingSegments * Mathf.PI * 2f;
                points[i] = new Vector3(Mathf.Cos(angle) * GripIndicatorRingRadius,
                    Mathf.Sin(angle) * GripIndicatorRingRadius, 0f);
            }

            line.positionCount = points.Length;
            line.SetPositions(points);

            PrefabUtility.SaveAsPrefabAsset(root, GripIndicatorPrefabPath, out bool saved);
            Object.DestroyImmediate(root);

            if (!saved)
            {
                Debug.LogError(Log + "VA_GripIndicator: SaveAsPrefabAsset başarısız: " + GripIndicatorPrefabPath);
                return false;
            }

            Debug.Log(Log + "VA_GripIndicator.prefab üretildi (" + GripIndicatorPrefabPath + ").");
            return true;
        }

        /// <summary>Halka materyali yoksa üretir (varsa dokunmaz); shader bulunamazsa <c>null</c>.</summary>
        private static Material EnsureGripIndicatorMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(GripIndicatorMaterialPath);
            if (existing != null)
            {
                return existing;
            }

            Shader shader = null;
            for (int i = 0; i < GripIndicatorShaderCandidates.Length && shader == null; i++)
            {
                shader = Shader.Find(GripIndicatorShaderCandidates[i]);
            }

            if (shader == null)
            {
                return null;
            }

            EnsureFolder(GripIndicatorMaterialDir);
            var material = new Material(shader) { name = "M_GripIndicator" };
            AssetDatabase.CreateAsset(material, GripIndicatorMaterialPath);
            return material;
        }

        /// <summary>
        /// Kovan prefabı yoksa üretir (varsa dokunmaz): pack'teki mermi modelini
        /// unpack eder, küçük bir Rigidbody + bounds'a göre BoxCollider ekler.
        /// Fiziksel doğruluk hedeflenmez — kısa ömürlü, havuzlanan bir FX objesidir.
        /// <para>⚠️ <b>"Varsa dokunmaz" YETMEZ, mevcut olanın SAĞLAM olduğu da doğrulanır.</b>
        /// Kovan prefabı pack modelinden unpack edilir, yani mesh'i pack'in FBX'ine bir referanstır;
        /// pack klasörü taşındığında (ya da FBX yeni kimlikle yeniden import edildiğinde) o referans
        /// kopar ve prefab <b>mesh'siz</b> kalır. Belirti sinsi: kovan fırlar, fizik çalışır, hata
        /// basılmaz — sadece <b>çizilmez</b>, yani "kovan çıkmıyor" diye okunur. Koşulsuz erken
        /// dönülürse araç o prefabı bir daha hiç onarmaz; kırık asset her koşuda sağlam sayılır.
        /// Tek ipucu Project penceresindeki önizlemenin jenerik mavi küpe dönmesidir.</para>
        /// </summary>
        private static GameObject EnsureCasingPrefab(string path, string sourcePackPath, List<GameObject> live)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                if (HasRenderableMesh(existing))
                {
                    return existing;
                }

                // Kırık: aşağıdaki üretim yolu aynı yolun ÜSTÜNE yazar. SaveAsPrefabAsset asset
                // GUID'ini korur, yani WPN prefablarındaki casingPrefab bağları kopmaz.
                Warn("Kovan prefabı '" + path + "' mesh'siz (pack taşınmış olabilir) — " +
                     "kaynaktan yeniden üretiliyor.");
            }

            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePackPath);
            if (sourcePrefab == null)
            {
                Warn("Kovan kaynağı yok: " + sourcePackPath + " — '" + path + "' üretilemedi.");
                return null;
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
            live.Add(inst);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = System.IO.Path.GetFileNameWithoutExtension(path);

            var rb = inst.AddComponent<Rigidbody>();
            rb.mass = CasingMassKg;

            Bounds b = ComputeLocalBounds(inst.transform, inst, "Casing:" + inst.name);
            var box = inst.AddComponent<BoxCollider>();
            box.center = b.center;
            box.size = new Vector3(Mathf.Max(b.size.x, 0.006f), Mathf.Max(b.size.y, 0.006f), Mathf.Max(b.size.z, 0.006f));

            PrefabUtility.SaveAsPrefabAsset(inst, path, out bool saved);
            Object.DestroyImmediate(inst);

            if (!saved)
            {
                Debug.LogError(Log + "Kovan prefabı kaydedilemedi: " + path);
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>
        /// Prefabın çizilebilir en az bir mesh'i var mı: <c>MeshFilter</c> zinciri kopmuşsa
        /// (mesh <c>null</c>) obje sahnede vardır, fiziği çalışır ama GÖRÜNMEZ.
        /// </summary>
        private static bool HasRenderableMesh(GameObject go)
        {
            var filters = go.GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
            {
                if (filters[i].sharedMesh != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// M_MuzzleFlash.mat'ın kopyası ama Additive yerine Alpha blend — namlu dumanı
        /// için (parlayan ışık değil, gri/soluk duman görünsün). Yoksa üretir, varsa dokunmaz.
        /// </summary>
        private static Material EnsureMuzzleSmokeMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(SmokeMaterialPath);
            if (existing != null)
            {
                return existing;
            }

            var flashMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/M_MuzzleFlash.mat");
            if (flashMat == null)
            {
                Warn("M_MuzzleFlash.mat bulunamadı — M_MuzzleSmoke üretilemedi.");
                return null;
            }

            var smokeMat = new Material(flashMat) { name = "M_MuzzleSmoke" };
            smokeMat.SetFloat("_Blend", 0f); // Alpha (flash'ta 2 = Additive)
            smokeMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            smokeMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            smokeMat.SetColor("_BaseColor", new Color(0.6f, 0.6f, 0.6f, 1f));

            AssetDatabase.CreateAsset(smokeMat, SmokeMaterialPath);
            return smokeMat;
        }

        // ------------------------------------------------ ADIM 4: WeaponCatalog

        /// <summary>WeaponCatalog.asset'i yoksa yaratır; definitions (tablo sırası) + remoteShotFxPrefab yazar.</summary>
        private static bool UpdateCatalog()
        {
            Type catalogType = ResolveType("WeaponCatalog");
            if (catalogType == null || !typeof(ScriptableObject).IsAssignableFrom(catalogType))
            {
                Warn("WeaponCatalog tipi bulunamadı — katalog atlandı (script henüz derlenmemiş olabilir).");
                return false;
            }

            bool created = false;
            var catalog = AssetDatabase.LoadAssetAtPath(CatalogPath, catalogType) as ScriptableObject;
            if (catalog == null)
            {
                // Yolda başka tipte bir asset varsa GUID'i korumak için ezme.
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(CatalogPath)))
                {
                    Warn("WeaponCatalog: '" + CatalogPath + "' yolunda farklı tipte bir asset var — dokunulmadı.");
                    return false;
                }

                catalog = ScriptableObject.CreateInstance(catalogType);
                AssetDatabase.CreateAsset(catalog, CatalogPath);
                created = true;
            }

            var so = new SerializedObject(catalog);

            var defsProp = so.FindProperty("definitions");
            if (defsProp == null || !defsProp.isArray)
            {
                Warn("WeaponCatalog: 'definitions' alanı yok ya da dizi değil (sözleşme kayması?).");
            }
            else
            {
                var found = new List<WeaponDefinition>(Specs.Length);
                for (int i = 0; i < Specs.Length; i++)
                {
                    string wdPath = DataDir + "/WD_" + Specs[i].Name + ".asset";
                    var def = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(wdPath);
                    if (def != null)
                    {
                        found.Add(def);
                    }
                    else
                    {
                        Warn("WeaponCatalog: '" + wdPath + "' yok — katalog dışı kaldı.");
                    }
                }

                defsProp.arraySize = found.Count;
                for (int i = 0; i < found.Count; i++)
                {
                    defsProp.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
                }
            }

            var fxProp = so.FindProperty("remoteShotFxPrefab");
            if (fxProp == null)
            {
                Warn("WeaponCatalog: 'remoteShotFxPrefab' alanı yok (sözleşme kayması?).");
            }
            else
            {
                var fx = AssetDatabase.LoadAssetAtPath<GameObject>(FxPrefabPath);
                if (fx != null)
                {
                    fxProp.objectReferenceValue = fx;
                }
                else
                {
                    Warn("WeaponCatalog: FX_RemoteShot.prefab yok — remoteShotFxPrefab olduğu gibi bırakıldı.");
                }
            }

            // Ön kabza göstergesi: YALNIZ alan boşsa bağlanır (sanatçının bağladığı başka bir prefab
            // ezilmesin — FX alanından farkı budur, gerekçe GripIndicatorPrefabPath'te).
            var indicatorProp = so.FindProperty("secondaryGripIndicatorPrefab");
            if (indicatorProp == null)
            {
                Warn("WeaponCatalog: 'secondaryGripIndicatorPrefab' alanı yok (sözleşme kayması?).");
            }
            else if (indicatorProp.objectReferenceValue == null)
            {
                var indicator = AssetDatabase.LoadAssetAtPath<GameObject>(GripIndicatorPrefabPath);
                if (indicator != null)
                {
                    indicatorProp.objectReferenceValue = indicator;
                }
                else
                {
                    Warn("WeaponCatalog: VA_GripIndicator.prefab yok — secondaryGripIndicatorPrefab boş " +
                         "kaldı (ön kabza göstergesi çizilmez; tam kit koşusu üretir).");
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return created;
        }

        // ------------------------------------------- ADIM 5: WD.prefab ikinci geçişi

        /// <summary>Her WD'nin 'prefab' alanına ilgili WPN prefabını bağlar.</summary>
        private static void LinkDefinitionPrefabs()
        {
            for (int i = 0; i < Specs.Length; i++)
            {
                string ctx = "WD_" + Specs[i].Name;
                var def = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(DataDir + "/WD_" + Specs[i].Name + ".asset");
                if (def == null)
                {
                    Warn(ctx + ": asset yok — prefab bağlanamadı.");
                    continue;
                }

                string wpnPath = PrefabDir + "/WPN_" + Specs[i].Name + ".prefab";
                var wpn = AssetDatabase.LoadAssetAtPath<GameObject>(wpnPath);
                if (wpn == null)
                {
                    Warn(ctx + ": '" + wpnPath + "' yok — prefab alanı boş bırakıldı.");
                    continue;
                }

                var so = new SerializedObject(def);
                SetObjectRef(so, "prefab", wpn, ctx);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ------------------------------------------------------------ yardımcılar

        /// <summary>Uyarı sayacı + konsol (dialog YOK — pipeline kilitlenmesin).</summary>
        private static void Warn(string message)
        {
            _warnings++;
            Debug.LogWarning(Log + message);
        }

        /// <summary>"Assets/..." klasör zincirini eksik halkalarıyla kurar.</summary>
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string[] parts = path.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        /// <summary>Adı eşleşen ilk torunu döner (derinlemesine arama).</summary>
        private static Transform FindDeepChild(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == name)
                {
                    return child;
                }

                Transform hit = FindDeepChild(child, name);
                if (hit != null)
                {
                    return hit;
                }
            }

            return null;
        }

        /// <summary>
        /// Verilen instance'ın tüm Renderer'larının birleşik bounds'u, kökün worldToLocalMatrix'iyle
        /// KÖK yerel uzayına çevrilir (8 köşe tek tek dönüştürülür).
        /// </summary>
        private static Bounds ComputeLocalBounds(Transform root, GameObject instance, string ctx)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            Matrix4x4 toRoot = root.worldToLocalMatrix;

            bool hasAny = false;
            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;

            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];

                // ⚠️ KAPALI Renderer'ın `bounds`'u BAYATTIR — Unity onu güncellemez, en son
                // çizildiği yerdeki DÜNYA kutusunu döndürür. Çerçeve (VA_WeaponFrame) prefabda
                // varsayılan olarak kapalı durduğu için buraya metrelerce ötede bir kutu sızıyor
                // ve ölçüyü tümden kaydırıyordu (kovan çıkışı silahın 2.5 m önünde doğuyordu).
                if (!renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                // Çerçeve silahın GÖVDESİ değil sunum kabıdır: açık olsa da ölçüye girmez,
                // yoksa "silah ne kadar geniş" sorusunun cevabı çerçevenin boyu olurdu.
                if (renderer.GetComponentInParent<WeaponFrame>(true) != null)
                {
                    continue;
                }

                Bounds wb = renderer.bounds;
                if (wb.size.sqrMagnitude <= 0f)
                {
                    continue;
                }

                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? wb.min.x : wb.max.x,
                        (c & 2) == 0 ? wb.min.y : wb.max.y,
                        (c & 4) == 0 ? wb.min.z : wb.max.z);
                    Vector3 local = toRoot.MultiplyPoint3x4(corner);
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                    hasAny = true;
                }
            }

            if (!hasAny)
            {
                Warn(ctx + ": modelde Renderer yok — kaba varsayılan ölçüler kullanıldı.");
                return new Bounds(new Vector3(0f, 0.01f, 0.16f), new Vector3(0.08f, 0.18f, 0.68f));
            }

            var bounds = new Bounds();
            bounds.SetMinMax(min, max);
            return bounds;
        }

        /// <summary>
        /// Kısa ("WeaponAnimator") ya da tam ("TMPro.TextMeshPro") tip adını yüklü assembly'lerde
        /// arar; kısa ad çakışırsa VortexArena.* namespace'i tercih edilir. Bulunanlar cache'lenir.
        /// </summary>
        private static Type ResolveType(string name)
        {
            if (ResolvedTypes.TryGetValue(name, out Type cached))
            {
                return cached;
            }

            bool dotted = name.IndexOf('.') >= 0;
            Type found = null;

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int a = 0; a < assemblies.Length; a++)
            {
                if (dotted)
                {
                    Type t = null;
                    try
                    {
                        t = assemblies[a].GetType(name, false);
                    }
                    catch
                    {
                        // dinamik/bozuk assembly — atla
                    }

                    if (t != null)
                    {
                        found = PickPreferred(found, t);
                    }
                }
                else
                {
                    Type[] types;
                    try
                    {
                        types = assemblies[a].GetTypes();
                    }
                    catch (ReflectionTypeLoadException e)
                    {
                        types = e.Types;
                    }
                    catch
                    {
                        continue;
                    }

                    for (int t = 0; t < types.Length; t++)
                    {
                        if (types[t] != null && types[t].Name == name)
                        {
                            found = PickPreferred(found, types[t]);
                        }
                    }
                }
            }

            if (found != null)
            {
                ResolvedTypes[name] = found;
            }

            return found;
        }

        /// <summary>Aynı kısa ada iki aday çıkarsa VortexArena.* olanı kazanır.</summary>
        private static Type PickPreferred(Type current, Type candidate)
        {
            if (current == null)
            {
                return candidate;
            }

            bool currentVa = current.Namespace != null && current.Namespace.StartsWith("VortexArena", StringComparison.Ordinal);
            bool candidateVa = candidate.Namespace != null && candidate.Namespace.StartsWith("VortexArena", StringComparison.Ordinal);
            return !currentVa && candidateVa ? candidate : current;
        }

        /// <summary>GetComponent ?? AddComponent — tip adıyla (script henüz yoksa uyarı + null).</summary>
        private static Component EnsureComponentByTypeName(GameObject go, string typeName, string ctx)
        {
            Type type = ResolveType(typeName);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
            {
                Warn(ctx + ": '" + typeName + "' tipi bulunamadı — bileşen eklenemedi (script henüz derlenmemiş olabilir).");
                return null;
            }

            Component existing = go.GetComponent(type);
            return existing != null ? existing : go.AddComponent(type);
        }

        /// <summary>Kök üzerindeki bileşenlerde tam tip adı eşleşmesi arar.</summary>
        private static Component FindComponentByTypeFullName(GameObject go, string fullName)
        {
            Component[] comps = go.GetComponents<Component>();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] != null && comps[i].GetType().FullName == fullName)
                {
                    return comps[i];
                }
            }

            return null;
        }

        /// <summary>Bileşen null değilse verilen (alan, referans) çiftlerini SerializedObject ile bağlar.</summary>
        private static void BindFields(Component target, string ctx, params (string field, Object value)[] refs)
        {
            if (target == null)
            {
                return;
            }

            var so = new SerializedObject(target);
            for (int i = 0; i < refs.Length; i++)
            {
                SetObjectRef(so, refs[i].field, refs[i].value, ctx);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>FindProperty; alan yoksa uyarı basıp null döner (sözleşme kayması teşhisi).</summary>
        private static SerializedProperty FindProp(SerializedObject so, string field, string ctx)
        {
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
            {
                Warn(ctx + ": '" + field + "' alanı bulunamadı (sözleşme kayması?).");
            }

            return p;
        }

        private static void SetString(SerializedObject so, string field, string value, string ctx)
        {
            SerializedProperty p = FindProp(so, field, ctx);
            if (p != null)
            {
                p.stringValue = value;
            }
        }

        /// <summary>Alan float da int de olsa yazar (sözleşmede tip belirtilmedi — ikisine de dayanıklı).</summary>
        private static void SetNumber(SerializedObject so, string field, double value, string ctx)
        {
            SerializedProperty p = FindProp(so, field, ctx);
            if (p == null)
            {
                return;
            }

            switch (p.propertyType)
            {
                case SerializedPropertyType.Float:
                    p.floatValue = (float)value;
                    break;
                case SerializedPropertyType.Integer:
                    p.intValue = (int)Math.Round(value);
                    break;
                default:
                    Warn(ctx + ": '" + field + "' sayısal değil (" + p.propertyType + ").");
                    break;
            }
        }

        /// <summary>
        /// Obje referansı yazar. <paramref name="value"/> null ise ve <paramref name="allowNull"/>
        /// kapalıysa mevcut değer korunur (prefabdaki çalışan bağı null ile ezmemek için).
        /// </summary>
        private static void SetObjectRef(SerializedObject so, string field, Object value, string ctx, bool allowNull = false)
        {
            SerializedProperty p = FindProp(so, field, ctx);
            if (p == null)
            {
                return;
            }

            if (p.propertyType != SerializedPropertyType.ObjectReference)
            {
                Warn(ctx + ": '" + field + "' obje referansı değil (" + p.propertyType + ").");
                return;
            }

            if (value == null && !allowNull)
            {
                return;
            }

            p.objectReferenceValue = value;
        }

        /// <summary>Enum alanını ÜYE ADIYLA yazar — int değeri sözleşmeye gömmemek için.</summary>
        private static void SetEnumByName(SerializedObject so, string field, string memberName, string ctx)
        {
            SerializedProperty p = FindProp(so, field, ctx);
            if (p == null)
            {
                return;
            }

            if (p.propertyType != SerializedPropertyType.Enum)
            {
                Warn(ctx + ": '" + field + "' enum değil (" + p.propertyType + ").");
                return;
            }

            int index = Array.IndexOf(p.enumNames, memberName);
            if (index < 0)
            {
                Warn(ctx + ": enum '" + field + "' içinde '" + memberName + "' üyesi yok (mevcut: " +
                     string.Join(", ", p.enumNames) + ").");
                return;
            }

            p.enumValueIndex = index;
        }

        /// <summary>
        /// <see cref="SetObjectRef"/> gibi ama HÂLÂ BOŞSA yazar — alanda zaten bir değer varsa
        /// (Inspector'dan elle sürüklenmiş olabilir) dokunmaz. Ses klibi alanları için: bu araç
        /// tekrar çalıştırıldığında kullanıcının seçtiği klip SİLİNMESİN diye.
        /// </summary>
        private static void SetObjectRefIfEmpty(SerializedObject so, string field, Object value, string ctx)
        {
            SerializedProperty p = FindProp(so, field, ctx);
            if (p == null)
            {
                return;
            }

            if (p.propertyType != SerializedPropertyType.ObjectReference)
            {
                Warn(ctx + ": '" + field + "' obje referansı değil (" + p.propertyType + ").");
                return;
            }

            if (p.objectReferenceValue != null)
            {
                return; // elle atanmış — dokunma
            }

            p.objectReferenceValue = value;
        }


        /// <summary>
        /// Namlu alevi/duman/kovan kitini bir WPN kökü üzerinde kurar
        /// (<see cref="RebindExistingPrefab"/>). Muzzle/MuzzleFlash'ı OLDUĞU YERDE bulur ve
        /// TAŞIMAZ — model/namlu konumu elle ayarlanmıştır, araç onu bozmaz.
        /// </summary>
        private static void ApplyVfxAndShellKit(GameObject root, WeaponSpec spec,
            Dictionary<string, GameObject> casings, Material smokeMaterial, string ctx)
        {
            Transform rootT = root.transform;
            Transform flashT = FindDeepChild(rootT, "MuzzleFlash");
            ParticleSystem flashPs = flashT != null ? flashT.GetComponent<ParticleSystem>() : null;
            if (flashPs != null)
            {
                ConfigureMuzzleFlash(flashPs, spec);
                ConfigureMuzzleSmoke(flashPs, spec, smokeMaterial);
            }
            else
            {
                Warn(ctx + ": MuzzleFlash/ParticleSystem bulunamadı — namlu alevi/dumanı ayarlanamadı.");
            }

            // ---- Kovan çıkış noktası.
            // ⚠️ MEVCUT `Eject` TAŞINMAZ — Muzzle/MuzzleFlash ile aynı kural: yeri elle ayarlanır,
            // araç yalnız bağlar. Burada bir kez hesaplanıp her koşuda yeniden yazılıyordu ve
            // hesap silahın alt ağacındaki her Renderer'a bakıyordu; prefabdaki KAPALI çerçeve
            // (VA_WeaponFrame) bayat dünya bounds'u döndürdüğü için ölçü kayıyor, elle yapılan
            // ayar sessizce siliniyordu (Docs/Sistem-Ozeti.md §7).
            // ⚠️ Arama DERİN olmak zorunda: `Eject` silahın kökünde değil MODELİN içinde yaşıyor
            // (kovan çıkışı gövdenin bir noktasıdır, silahın orijininin değil). Yalnız doğrudan
            // çocuklara bakılırsa mevcut düğüm bulunamaz ve her koşu kökte İKİNCİ bir `Eject`
            // üretip elle ayarlanmış olanı sessizce devre dışı bırakır.
            Transform ejectT = FindDeepChild(rootT, "Eject");
            if (ejectT == null)
            {
                // Yalnız İLK kurulumda kaba bir başlangıç noktası verilir (yoksa silahın
                // orijininde doğardı); sonrası elle ayarlanır ve bir daha dokunulmaz.
                Bounds bounds = ComputeLocalBounds(rootT, root, ctx);
                var ejectGo = new GameObject("Eject");
                ejectT = ejectGo.transform;
                ejectT.SetParent(rootT, false);
                ejectT.localPosition = new Vector3(
                    bounds.extents.x * 0.9f,
                    bounds.center.y + bounds.extents.y * 0.25f,
                    bounds.center.z - bounds.extents.z * 0.2f);
                ejectT.localRotation = Quaternion.identity;
                Warn(ctx + ": 'Eject' yoktu, kaba bir başlangıç noktasıyla üretildi — " +
                     "kovan çıkışını sahnede gözle ayarla, araç bir daha taşımaz.");
            }

            Component shellEjector = EnsureComponentByTypeName(root, "ShellEjector", ctx);
            GameObject casingForSpec = null;
            if (!string.IsNullOrEmpty(spec.CasingFamily))
            {
                casings.TryGetValue(spec.CasingFamily, out casingForSpec);
            }

            BindFields(shellEjector, ctx, ("casingPrefab", casingForSpec), ("ejectPoint", ejectT));
        }

        /// <summary>
        /// Kavrama kitini bir WPN kökü üzerinde kurar (idempotent): ISDK'nın <b>İKİ</b> yakın-kavrama
        /// bileşeni (kumanda + el hattı) korunur ve filtresiz bırakılır, mesafeden kavrama kökten
        /// silinir, eski kavrama kalıntıları temizlenir.
        /// <para>
        /// ⚠️ <b>Kökte kavrama FİLTRESİ YOKTUR ve bağlanmaz</b> (<c>_interactorFilters</c> boş):
        /// eski soket kapısı bileşeni kaldırıldı — silah ana ele verilerek/çağrılarak geliyor, ana
        /// kabza için oyuncunun elini bir yere götürmesi gerekmiyor; ön kabzanın kapısı ve
        /// göstergesi <see cref="Weapon"/>'ın kendisindedir (<c>IsHandOnSecondaryGrip</c>). Bu araç
        /// listeyi her koşuda BOŞALTIR: kaldırılan bileşenin filtre listesinde bıraktığı boş (missing)
        /// giriş ISDK'nın <c>Start</c> denetiminde (<c>AssertCollectionItems</c>) patlar ve silah
        /// kavranamaz olurdu. Aynı sebeple kökte kalmış eksik script bileşenleri de silinir.
        /// </para>
        /// <para>
        /// ⚠️ <b>Neden iki bileşen:</b> <c>GrabInteractable</c> (kumanda hattı) ile
        /// <c>HandGrabInteractable</c> (el hattı) birlikte tutulur, çünkü hangisinin koşacağını
        /// ISDK rig'i seçiyor — interactor grubu "el izleniyor mu" sorusuna göre değişiyor ve o da
        /// <c>OVRManager.controllerDrivenHandPosesType</c>'a bağlı. Tek bileşen bırakılsa o
        /// anahtarın her değişimi silahı sessizce kavranamaz yapardı
        /// (<c>Docs/Sistem-Ozeti.md</c> §7). İkisi de aynı <c>Grabbable</c>'ı besler, yani
        /// <see cref="Weapon"/> tarafında tek bir olay yolu kalır.
        /// </para>
        /// <para>
        /// ⚠️ <c>HandGrabInteractable._handGrabPoses</c> listesi <b>boş bırakılır</b>: liste dolduğu
        /// anda ISDK kavrama adaylığını poz skoruna göre hesaplamaya başlar ve bugünkü kavrama hissi
        /// (collider mesafesi) sessizce değişirdi. Aynı sebeple <c>_handAligment</c> alanına da
        /// dokunulmaz — poz listesi boş kaldığı sürece o alanın etkisi yoktur.
        /// </para>
        /// <para>
        /// ⚠️ <b>Kavramayı bu araç YAZMAZ.</b> Kavramanın tek kaynağı Kavrama Pozu Stüdyosu'nda
        /// yazılan pozdur ve o poz doğrudan <c>WD_*.asset</c>'e yazılır (prefabda karşılığı olan
        /// bir düğüm YOKTUR — düğüm açmak, kaydın ikinci bir tarifini üretirdi). Araç burada yalnız
        /// <b>temizlik ve denetim</b> yapar: eski soket işaretçileri, prefabda kalmış el rig'i ve
        /// poz düğümleri, kavraması yazılmamış silah raporu.
        /// </para>
        /// </summary>
        private static void ApplyGripKit(GameObject root, ItemDefinition definition, string ctx)
        {
            // Kaldırılan soket bileşeninin (ve başka bir eski scriptin) kökte bıraktığı eksik
            // script kayıtları: bileşen artık derlenmediği için tipten bulunamaz, Unity'nin kendi
            // temizliğiyle silinir.
            int missing = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
            if (missing > 0)
            {
                Debug.Log(Log + ctx + ": kökte " + missing + " eksik script bileşeni silindi.");
            }

            // Kökte mesafeden kavrama YOK: silah çerçeveden seçilir (VA_WeaponFrame), kökün kendisi
            // uzaktan kavranırsa çerçeve atlanır ve silah odanın öbür ucundan alınabilir olurdu.
            // ⚠️ İki hat da silinir: hangisinin koşacağını rig seçiyor, biri unutulursa yasak
            // yarım kalır ve silah bazı yapılandırmalarda uzaktan kavranabilir olur.
            RemoveRootComponent(root, "Oculus.Interaction.DistanceGrabInteractable", ctx,
                "kökte mesafeden kavrama yok (çerçeveden seçilir)");
            RemoveRootComponent(root, "Oculus.Interaction.HandGrab.DistanceHandGrabInteractable", ctx,
                "kökte mesafeden kavrama yok (el hattı; çerçeveden seçilir)");

            // Mesafe kavraması gidince asset'te duran sağlayıcı da gider: kalan tek tüketicisi
            // HandGrabInteractable'dır ve o, alan boşsa kendi örneğini ÇALIŞMA ANINDA ekliyor.
            // Prefabda tutmak sonraki okuyucuya "burada elle ayarlanmış bir hareket var" der.
            Component moveProvider = FindComponentByTypeFullName(root, "Oculus.Interaction.MoveTowardsTargetProvider");
            if (moveProvider != null)
            {
                Object.DestroyImmediate(moveProvider, true);
                Debug.Log(Log + ctx + ": MoveTowardsTargetProvider kaldırıldı (çalışma anında kurulur).");
            }

            ApplyReleasePhysics(root, ctx);

            Component grabbable = FindComponentByTypeFullName(root, "Oculus.Interaction.Grabbable");
            Rigidbody body = root.GetComponent<Rigidbody>();

            Component grabInteractable = FindComponentByTypeFullName(root, "Oculus.Interaction.GrabInteractable");
            if (grabInteractable == null)
            {
                Warn(ctx + ": kökte Oculus.Interaction.GrabInteractable yok — kumanda hattı eksik " +
                     "(silah yalnız el hattından kavranır).");
            }
            else
            {
                ClearInteractorFilters(grabInteractable, ctx);
            }

            // El hattı araç tarafından ÜRETİLİR (kumanda hattının aksine): yeni bir silah eklendiğinde
            // elle kurulum adımı doğmasın diye.
            Component handGrab = EnsureComponentByTypeName(
                root, "Oculus.Interaction.HandGrab.HandGrabInteractable", ctx);
            if (handGrab == null)
            {
                return;
            }

            var handSo = new SerializedObject(handGrab);
            SerializedProperty rb = handSo.FindProperty("_rigidbody");
            SerializedProperty pointable = handSo.FindProperty("_pointableElement");
            if (rb != null)
            {
                rb.objectReferenceValue = body;
            }

            if (pointable != null)
            {
                pointable.objectReferenceValue = grabbable;
            }

            handSo.ApplyModifiedPropertiesWithoutUndo();

            if (body == null)
            {
                // Rigidbody yoksa HandGrabInteractable Start'ta assert atıp kendini durdurur;
                // belirtisi "silah bazen alınmıyor" olur, sebebi hiç görünmez.
                Warn(ctx + ": kökte Rigidbody yok — HandGrabInteractable çalışamaz (silah el hattında " +
                     "kavranamaz).");
            }

            ClearInteractorFilters(handGrab, ctx);

            RemoveLegacySocketNodes(root, ctx);
            RemoveLegacyHandNodes(root, ctx);
            RemoveLegacyGripPoseNodes(root, ctx);
            RemoveStudioHandNodes(root, ctx);
            NoteIfUnbaked(definition, ctx);
        }

        /// <summary>
        /// Eski authoring akışından kalan <c>GripSocket_Primary/Secondary</c> işaretçi düğümlerini
        /// siler.
        /// <para>
        /// ⚠️ <b>Temizlik burada yapılır, prefab dosyasına elle dokunularak değil:</b> düğüm silmek
        /// üç YAML kaydını (GameObject + Transform + MonoBehaviour) ve ebeveynin çocuk listesini
        /// birlikte ilgilendiriyor, ayrıca bu prefabların örnekleri başka prefabların içinde duruyor
        /// (<c>VA_WeaponCanvas</c>). Unity'nin kendi API'sinden geçmek o bağların hepsini doğru
        /// çözer.
        /// </para>
        /// <para>
        /// ⚠️ Arama <b>ada göredir</b>, bileşen tipine göre değil: <c>GripSocketMarker</c> tipi
        /// kaldırıldığı için düğümlerde artık eksik script duruyor ve tipten gitmek onları hiç
        /// bulamazdı.
        /// </para>
        /// <para>
        /// ⚠️ Arama <b>TÜM ALT AĞACI</b> gezer, kökün doğrudan çocuklarına bakmakla yetinmez: eski
        /// araç işaretçiyi silahın köküne koyuyordu ama onları elle model dalına
        /// (<c>Model/&lt;pack&gt;/…</c>) taşımak serbestti ve gerçekte taşınmışlardı. Yalnız köke
        /// bakan bir temizlik hiçbir şey bulamaz, üstelik <b>sessizce</b> başarılı görünürdü.
        /// </para>
        /// </summary>
        private static void RemoveLegacySocketNodes(GameObject root, string ctx)
        {
            RemoveLegacySocketNode(root, "GripSocket_Primary", ctx);
            RemoveLegacySocketNode(root, "GripSocket_Secondary", ctx);
        }

        private static void RemoveLegacySocketNode(GameObject root, string nodeName, string ctx)
        {
            // Ters gezinti: aynı adda birden çok düğüm kalmış olabilir (eski araç ikincisini
            // üretmezdi ama elle kopyalanmış olabilir) ve hepsi gitmeli.
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = all.Length - 1; i >= 0; i--)
            {
                Transform node = all[i];
                if (node == null || node.name != nodeName)
                {
                    continue;
                }

                Object.DestroyImmediate(node.gameObject, true);
                _legacyNodesRemoved++;
                Debug.Log(Log + ctx + ": eski '" + nodeName + "' işaretçisi silindi — kavrama " +
                          "stüdyoda WD_*.asset'e yazılır, prefabta işaretçi durmaz.");
            }
        }

        /// <summary>
        /// Prefabın içinde kalmış <c>Hands/Hand_*</c> ağacını siler — <b>ölü veri</b>.
        /// <para>
        /// Kavrama, prefabın içinde duran bir el modelinden değil, stüdyoda yazılan pozdan geliyor
        /// ve tanıma (<c>WD_*.asset</c>) yazılıyor. Prefabın içinde duran el rig'inin okuyanı yok.
        /// </para>
        /// <para>⚠️ Sessizce BIRAKILMAZ: (1) açık kalırsa arenada havada duran bir el olarak
        /// görünür — silah sahnede de duruyor (raf/masa, <c>WeaponFrame</c>, <c>VA_WeaponCanvas</c>)
        /// ve uzak avatarın elinde de; (2) duran bir kopya kavramanın ikinci bir tarifidir ve
        /// "hangisi geçerli" sorusunu her açanın kafasında yeniden doğurur. Runtime emniyeti
        /// (<see cref="ItemHandRig.HideAll"/>) hâlâ yerinde — o, henüz temizlenmemiş prefablar
        /// içindir.</para>
        /// </summary>
        private static void RemoveLegacyHandNodes(GameObject root, string ctx)
        {
            Transform node = root.transform.Find(ItemHandRig.RootNodeName);
            if (node == null)
            {
                return;
            }

            Object.DestroyImmediate(node.gameObject, true);
            _legacyNodesRemoved++;
            Debug.Log(Log + ctx + ": eski '" + ItemHandRig.RootNodeName + "' el rig'i silindi — " +
                      "kavrama WD_*.asset'e yazılır, prefabda el modeli durmaz.");
        }

        /// <summary>
        /// Prefabın içinde kalmış <c>GripPoses/…</c> ağacını siler — <b>ölü veri</b>.
        /// <para>
        /// Kavrama tanımın kendi alanlarında yaşıyor; prefabtaki poz düğümlerinin okuyanı yok.
        /// ⚠️ Sessizce BIRAKILMAZ: duran bir düğüm kavramanın ikinci bir tarifidir ve "hangisi
        /// geçerli" sorusunu prefabı her açanın kafasında yeniden doğurur.
        /// </para>
        /// </summary>
        private static void RemoveLegacyGripPoseNodes(GameObject root, string ctx)
        {
            Transform node = root.transform.Find(LegacyGripPoseRootName);
            if (node == null)
            {
                return;
            }

            Object.DestroyImmediate(node.gameObject, true);
            _legacyNodesRemoved++;
            Debug.Log(Log + ctx + ": eski '" + LegacyGripPoseRootName + "' poz düğümü silindi — " +
                      "kavrama WD_*.asset'e yazılır, prefabta poz durmaz.");
        }

        /// <summary>
        /// Prefabın içine kazara girmiş <b>authoring eli</b> varsa siler.
        /// <para>⚠️ Bu eller prefab stage sahnesinin ayrı kökleridir ve diske yazılmazlar — ama
        /// Hierarchy'de sürüklenerek prefabın altına taşınabilirler. O hâlde el modeli prefaba girer
        /// ve arenada havada duran bir el olarak görünür (silah sahnede de duruyor: raf,
        /// <c>WeaponFrame</c>, <c>VA_WeaponCanvas</c>).</para>
        /// </summary>
        private static void RemoveStudioHandNodes(GameObject root, string ctx)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = all.Length - 1; i >= 0; i--)
            {
                Transform node = all[i];
                if (node == null || node == root.transform ||
                    !node.name.StartsWith(GripPoseStudio.HAND_ROOT_PREFIX))
                {
                    continue;
                }

                string nodeName = node.name;
                Object.DestroyImmediate(node.gameObject, true);
                _legacyNodesRemoved++;
                Debug.Log(Log + ctx + ": prefabın içinde kalmış authoring eli ('" + nodeName +
                          "') silindi — o eller prefaba KONMAZ, sahnenin ayrı kökleridir.");
            }
        }

        /// <summary>
        /// Kavraması hiç yazılmamış silahı koşu sonundaki rapora ekler.
        /// <para>⚠️ Ölçüt <see cref="ItemDefinition.HasGrip"/>'tir, <c>GetGrip</c> değil: okuma yolu
        /// eksik eli ÖTEKİ elin kaydına düşürür, yani <c>GetGrip</c> ile bakılsaydı yarım yazılmış
        /// silah "tamam" görünürdü. Sorulan el SAĞDIR — silah en az bir elden yazılmışsa öteki el
        /// düşme yoluyla makul bir duruş alır; hiç yazılmamışsa sağ el de boştur.</para>
        /// </summary>
        private static void NoteIfUnbaked(ItemDefinition definition, string ctx)
        {
            if (IsUnbaked(definition))
            {
                _unbakedWeapons.Add(ctx);
            }
        }

        /// <summary>
        /// "Kavraması yazılmamış" ölçütü — koşu sonu raporu ile hazırlık denetiminin TEK kaynağı.
        /// İkinci bir yerde tekrarlansaydı ölçüt bir gün sessizce ayrışırdı.
        /// </summary>
        private static bool IsUnbaked(ItemDefinition definition)
        {
            return definition == null || !definition.HasGrip(GripSocketKind.Primary, true);
        }

        /// <summary>"Ateş sesi atanmamış" ölçütü — <see cref="IsUnbaked"/> ile aynı gerekçe.</summary>
        private static bool IsSilent(WeaponDefinition definition)
        {
            AudioClip[] clips = definition != null ? definition.FireClips : null;
            for (int i = 0; clips != null && i < clips.Length; i++)
            {
                if (clips[i] != null)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Tablodaki silahların kiti eksiksiz mi — <b>HİÇBİR ŞEY YAZMAZ</b> (build hazırlık
        /// panelinin okuduğu denetim). Koşu sonundaki iki raporun (kavraması yazılmamış · ateş sesi
        /// atanmamış) aynı ölçütlerini WD asset'lerini diskten okuyarak uygular; WD hiç yoksa araç
        /// bu silah için hiç çalışmamış demektir.
        /// </summary>
        internal static bool AreWeaponsReady(out string detail)
        {
            var missing = new List<string>();
            var unbaked = new List<string>();
            var silent = new List<string>();

            for (int i = 0; i < Specs.Length; i++)
            {
                string path = DataDir + "/WD_" + Specs[i].Name + ".asset";
                var def = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(path);
                if (def == null)
                {
                    missing.Add(Specs[i].Name);
                    continue;
                }

                if (IsUnbaked(def))
                {
                    unbaked.Add(Specs[i].Name);
                }

                if (IsSilent(def))
                {
                    silent.Add(Specs[i].Name);
                }
            }

            var problems = new List<string>();
            if (missing.Count > 0)
            {
                problems.Add("WD asset'i yok: " + string.Join(", ", missing));
            }

            if (unbaked.Count > 0)
            {
                problems.Add("kavraması yazılmamış: " + string.Join(", ", unbaked));
            }

            if (silent.Count > 0)
            {
                problems.Add("ateş sesi atanmamış: " + string.Join(", ", silent));
            }

            if (problems.Count > 0)
            {
                detail = string.Join(" · ", problems);
                return false;
            }

            detail = $"{Specs.Length} silah: kavrama ve ateş sesi tamam.";
            return true;
        }

        /// <summary>
        /// Ateş sesi atanmamış silahları tek uyarıda listeler. ⚠️ Bu rapor ŞART: klipler bu araçtan
        /// gelmediği için (bkz. <see cref="EnsureDefinition"/>) yeni bir silah <b>sessiz</b> doğar ve
        /// belirtisi yalnız "ateş sesi duyulmuyor"dur — hiçbir yerde hata basılmaz.
        /// </summary>
        private static void ReportSilentWeapons(WeaponDefinition[] defs)
        {
            var silent = new List<string>();
            for (int i = 0; i < defs.Length; i++)
            {
                if (defs[i] == null)
                {
                    continue;
                }

                if (IsSilent(defs[i]))
                {
                    silent.Add(defs[i].name);
                }
            }

            if (silent.Count == 0)
            {
                return;
            }

            Debug.LogWarning(Log + "Ateş sesi ATANMAMIŞ silahlar: " + string.Join(", ", silent) +
                             ". Bu silahlar sessiz ateş eder. Düzeltme: Assets/_Shared/Arsenal/Data/" +
                             "WD_<Ad>.asset'i seç ve Fire Clips / Mag Out Clip / Dry Fire Clip / " +
                             "Pickup Clip alanlarına klip sürükle (klipler bu araçtan gelmez).");
        }

        private static void ReportUnbakedWeapons()
        {
            if (_unbakedWeapons.Count == 0)
            {
                return;
            }

            Debug.LogWarning(Log + "Kavraması YAZILMAMIŞ silahlar: " +
                             string.Join(", ", _unbakedWeapons) + ". Bu silahlarda oyuncunun eli " +
                             "silaha sarılmaz (idle duruşunda kalır). Düzeltme: Tools > VortexArena > " +
                             "Weapons > Kavrama Pozu Stüdyosu → WPN_* prefabını prefab kipinde aç → " +
                             "Elleri Oluştur → Kaydet.");
        }

        /// <summary>Kökteki bir bileşeni tam tip adıyla siler (yoksa sessizce geçer).</summary>
        private static void RemoveRootComponent(GameObject root, string fullName, string ctx, string why)
        {
            Component component = FindComponentByTypeFullName(root, fullName);
            if (component == null)
            {
                return;
            }

            Object.DestroyImmediate(component, true);
            Debug.Log(Log + ctx + ": " + component.GetType().Name + " kaldırıldı — " + why + ".");
        }

        /// <summary>
        /// Bir ISDK interactable'ının <c>_interactorFilters</c> listesini BOŞALTIR (idempotent).
        /// <para>Kökte kavrama filtresi yoktur (gerekçe <see cref="ApplyGripKit"/>'te). Liste
        /// boşaltılmazsa kaldırılan soket bileşeninden kalan eksik giriş ISDK'nın <c>Start</c>
        /// denetiminde patlar ve silah kavranamaz olur — hata mesajı ise silahı değil bir
        /// koleksiyonu işaret eder.</para>
        /// </summary>
        private static void ClearInteractorFilters(Component interactable, string ctx)
        {
            var so = new SerializedObject(interactable);
            SerializedProperty filters = so.FindProperty("_interactorFilters");
            if (filters == null || !filters.isArray)
            {
                Warn(ctx + ": " + interactable.GetType().Name + " üzerinde '_interactorFilters' alanı yok " +
                     "ya da dizi değil (ISDK sözleşme kayması?) — filtre listesi denetlenemedi.");
                return;
            }

            if (filters.arraySize == 0)
            {
                return; // zaten boş — idempotent
            }

            filters.arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log(Log + ctx + ": " + interactable.GetType().Name + " filtre listesi boşaltıldı.");
        }

        /// <summary>
        /// Silah çerçevesi kitini bir WPN kökü üzerinde kurar (idempotent): <c>VA_WeaponFrame</c>
        /// prefabının bir ÖRNEĞİ kökün altına konur.
        /// <para>
        /// <b>Çerçeve nedir:</b> sahnedeki silah artık yerden alınmaz — çerçevenin içinde durur ve
        /// oradan hiç ayrılmaz. Oyuncu <c>WeaponFrame.maxGrabDistance</c> mesafesinden nişan alıp
        /// grip'e basınca silahın bir KLONU eline
        /// gelir (<see cref="WeaponFrame"/> → <c>WeaponGranter.SelectWeapon</c>). Yani her
        /// <c>WPN_*</c> prefabı hem "elde tutulan silah" hem "sahnede duran kaynak" olarak
        /// kullanılır; hangisi olduğunu ÇERÇEVENİN varlığı belirler (klonda çerçeve yok edilir).
        /// </para>
        /// <para>
        /// <b>Neden prefab ÖRNEĞİ (unpack DEĞİL):</b> çerçevede yapılan tek bir düzeltme —
        /// kavrama menzili, ışın rengi, collider boyu — altı silaha birden insin. Unpack edilseydi
        /// her değişiklik altı prefabı tek tek açmak demek olurdu (sahneye altyapı prefabı koyma
        /// kuralının aynısı).
        /// </para>
        /// <para>
        /// ⚠️ Bu, <see cref="ApplyGripKit"/>'in mesafe-kavrama silme adımlarıyla (kumanda ve
        /// el hattı) ÇELİŞMEZ: o adımlar <see cref="FindComponentByTypeFullName"/> kullanıyor ve o metot yalnız
        /// KÖKÜN bileşenlerine bakıyor (çocuklara inmiyor). Yasak <c>WPN_*</c> kökü içindir —
        /// çerçeve ayrı bir objedir ve seçim oradan yapılır.
        /// İnseydi araç kendi eklediği çerçevenin kavramasını siler ve silah hiç alınamaz olurdu.
        /// </para>
        /// </summary>
        private static void ApplyWeaponFrameKit(GameObject root, string ctx)
        {
            // Zaten var mı — pasif çocukları da tarar (çerçeve görseli pasif başlıyor).
            if (root.GetComponentInChildren<WeaponFrame>(true) != null)
            {
                return; // idempotent
            }

            var framePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponFramePrefabPath);
            if (framePrefab == null)
            {
                Warn(ctx + ": '" + WeaponFramePrefabPath + "' bulunamadı — silah çerçevesi eklenemedi. " +
                     "Çerçeve prefabı bu araç tarafından ÜRETİLMEZ (elle authoring gerektiren bir " +
                     "görsel/ISDK kurulumu), yalnız bağlanır. Prefab geri gelene kadar bu silah " +
                     "sahnede alınamaz kalır.");
                return;
            }

            var frame = (GameObject)PrefabUtility.InstantiatePrefab(framePrefab, root.transform);
            if (frame == null)
            {
                Warn(ctx + ": VA_WeaponFrame örneklenemedi.");
                return;
            }

            frame.name = framePrefab.name;
            frame.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            frame.transform.localScale = Vector3.one;

            Debug.Log(Log + ctx + ": VA_WeaponFrame eklendi — silah artık çerçevesinden, " +
                      "maxGrabDistance mesafesinden alınır.");
        }

        /// <summary>
        /// Çözülme kitini bir WPN kökü üzerinde kurar (idempotent):
        /// <see cref="SimpleWeaponDissolve"/> bileşeni + <c>DissolveEffect.mat</c> bağı. Silah ele
        /// geldiğinde model kısa bir süre çözülme materyaline çevrilip yoktan var edilir; efekt
        /// bitince özgün materyaller geri konur.
        /// <para>
        /// <b>Neden araçta:</b> bileşen her <c>WPN_*</c> köküne gerekiyor ve elle eklendiğinde yeni
        /// silahta sessizce unutulurdu — silah eskisi gibi anında belirir, kimse fark etmez.
        /// <see cref="ApplyWeaponFrameKit"/> ile aynı gerekçe.
        /// </para>
        /// <para>
        /// ⚠️ Materyal alanı yalnız <b>BOŞSA</b> yazılır (ses klipleriyle aynı kural): bir silaha
        /// elle başka bir çözülme materyali bağlanmışsa araç onu ezmez.
        /// </para>
        /// </summary>
        private static void ApplyDissolveKit(GameObject root, string ctx)
        {
            var dissolve = root.GetComponent<SimpleWeaponDissolve>();
            if (dissolve == null)
            {
                dissolve = root.AddComponent<SimpleWeaponDissolve>();
                Debug.Log(Log + ctx + ": SimpleWeaponDissolve eklendi — silah ele çözülerek gelir.");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(DissolveMaterialPath);
            if (material == null)
            {
                Warn(ctx + ": '" + DissolveMaterialPath + "' bulunamadı — çözülme materyali " +
                     "bağlanamadı. Bileşen takılı kalır ama efekt oynamaz (silah eskisi gibi " +
                     "anında belirir).");
                return;
            }

            var so = new SerializedObject(dissolve);
            SetObjectRefIfEmpty(so, "dissolveMaterial", material, ctx);
            SetNumber(so, "appearSeconds", DissolveAppearSeconds, ctx);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Bırakma anındaki fiziği söker: <c>Grabbable._throwWhenUnselected = false</c>.
        /// <para>
        /// ⚠️ <b>Neden zorunlu:</b> ISDK bırakışta silaha elin İZLENEN hızını uygular
        /// (<c>ThrowWhenUnselected</c>). Ama bu silahın kökü tutuş boyunca ISDK tarafından
        /// taşınmıyor — <c>Weapon.ApplyCanonicalGrip</c> her kare kanonik kavramadan ışınlıyor
        /// (§6.6). Işınlanan bir gövdeden türetilen "hız" fiziksel bir büyüklük değil, kare
        /// farkının artığıdır; bırakınca silah elden fırlıyordu.
        /// </para>
        /// <para>
        /// <c>_kinematicWhileSelected</c> AÇIK bırakılır (varsayılan): tutuş boyunca gövde
        /// kinematik olduğu için yerçekimi hız biriktirmez, bırakınca silah bulunduğu yerden
        /// düşer. İkisi birlikte "bırakınca yere düşer, fırlamaz" davranışını verir.
        /// </para>
        /// <para>⚠️ Fırlatma gerektiren eşya (bomba) bu kapıdan GEÇMEZ: onun atılışı
        /// <c>ArenaCombat.ReportThrow</c> ile telde bildirilen kendi balistiğidir, ISDK'nın
        /// fizik impulsu değil (Faz 4).</para>
        /// </summary>
        private static void ApplyReleasePhysics(GameObject root, string ctx)
        {
            Component grabbable = FindComponentByTypeFullName(root, "Oculus.Interaction.Grabbable");
            if (grabbable == null)
            {
                Warn(ctx + ": kökte Oculus.Interaction.Grabbable yok — bırakma fiziği ayarlanamadı " +
                     "(silah bırakılınca elden fırlayabilir).");
                return;
            }

            var so = new SerializedObject(grabbable);
            SerializedProperty throwProp = so.FindProperty("_throwWhenUnselected");
            if (throwProp == null)
            {
                Warn(ctx + ": Grabbable'da '_throwWhenUnselected' alanı yok (ISDK sözleşme kayması?) — " +
                     "bırakma fiziği ayarlanamadı.");
                return;
            }

            if (!throwProp.boolValue)
            {
                return; // zaten kapalı — idempotent
            }

            throwProp.boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log(Log + ctx + ": Grabbable._throwWhenUnselected kapatıldı — silah bırakılınca " +
                      "fırlamaz (poz kanonik kavramadan sürülüyor, ISDK'nın hız tahmini geçersiz).");
        }

        /// <summary>Namlu alevi particle modüllerini (renk/boyut/ömür/koni açısı) silaha göre ayarlar.</summary>
        private static void ConfigureMuzzleFlash(ParticleSystem ps, WeaponSpec spec)
        {
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(spec.FlashColorMin, spec.FlashColorMax);
            main.startSize = new ParticleSystem.MinMaxCurve(spec.FlashSizeMin, spec.FlashSizeMax);
            main.startLifetime = spec.FlashLifetime;

            var shape = ps.shape;
            if (shape.enabled)
            {
                shape.angle = spec.FlashConeAngle;
            }
        }

        /// <summary>
        /// MuzzleFlash'ın altında "Smoke" adlı bir child particle sistemi kurar/günceller ve
        /// flash'ın Sub Emitters modülüne "Birth" tetikleyicisiyle bağlar — Weapon.Fire()'daki
        /// tek bir muzzleFlash.Emit() çağrısı hem alevi hem dumanı otomatik tetikler.
        /// </summary>
        private static void ConfigureMuzzleSmoke(ParticleSystem flashPs, WeaponSpec spec, Material smokeMaterial)
        {
            Transform flashT = flashPs.transform;
            Transform smokeT = flashT.Find("Smoke");
            GameObject smokeGo = smokeT != null ? smokeT.gameObject : new GameObject("Smoke");
            if (smokeT == null)
            {
                smokeGo.transform.SetParent(flashT, false);
            }

            ParticleSystem smokePs = smokeGo.GetComponent<ParticleSystem>();
            if (smokePs == null)
            {
                smokePs = smokeGo.AddComponent<ParticleSystem>();
            }

            var main = smokePs.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.3f;
            main.startLifetime = spec.SmokeLifetime;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.35f);
            main.startSize = new ParticleSystem.MinMaxCurve(spec.SmokeSizeMin, spec.SmokeSizeMax);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.6f, 0.6f, spec.SmokeAlpha));
            main.gravityModifier = -0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = smokePs.emission;
            emission.enabled = true;
            emission.rateOverTime = 22f;

            var shape = smokePs.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 18f;
            shape.radius = 0.01f;

            var renderer = smokeGo.GetComponent<ParticleSystemRenderer>();
            if (renderer == null)
            {
                renderer = smokeGo.AddComponent<ParticleSystemRenderer>();
            }
            if (smokeMaterial != null)
            {
                renderer.sharedMaterial = smokeMaterial;
            }

            var subEmitters = flashPs.subEmitters;
            subEmitters.enabled = true;
            bool alreadyLinked = false;
            for (int i = 0; i < subEmitters.subEmittersCount; i++)
            {
                if (subEmitters.GetSubEmitterSystem(i) == smokePs)
                {
                    alreadyLinked = true;
                    break;
                }
            }
            if (!alreadyLinked)
            {
                subEmitters.AddSubEmitter(smokePs, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing, 1f);
            }
        }
    }
}
