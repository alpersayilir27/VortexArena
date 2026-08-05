using System;
using System.Collections.Generic;
using System.Reflection;
using Oculus.Interaction.HandGrab;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using VortexArena.Core.Combat;
using Object = UnityEngine.Object;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Weapons &gt; Build Weapon Prefabs</c> — tablodaki silahların kitini
    /// üretir/günceller: <c>WD_&lt;Ad&gt;.asset</c> (WeaponDefinition), mevcut
    /// <c>WPN_&lt;Ad&gt;.prefab</c>'ların bağları/VFX'i, <c>FX_RemoteShot.prefab</c> ve
    /// <c>Resources/WeaponCatalog.asset</c>.
    /// <para>
    /// <b>WPN prefabı YOKTAN üretilmez:</b> gövde (model hiyerarşisi, Muzzle/MuzzleFlash yerleşimi,
    /// kavrama pozları) elle ayarlanan bir şeydir ve prefab repoda yaşar; araç onu yerinde
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
        private const string PackRoot = "Assets/Low Poly AR Weapon Pack 1";

        private const string AudioRoot = "Assets/Audio/Weapons";

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

        private const string Casing762Path = PrefabDir + "/Casing_762x39.prefab";
        private const string Casing556Path = PrefabDir + "/Casing_556x45.prefab";
        private const string BulletPack762Path = PackRoot + "/Prefabs/Bullets/Bullet_A.prefab";
        private const string BulletPack556Path = PackRoot + "/Prefabs/Bullets/Bullet_B.prefab";
        private const float CasingMassKg = 0.01f;

        private const string Log = "[BuildWeaponPrefabs] ";

        // Tüm silahlarda ortak sayılar (tablo başlığındaki varsayılanlar).
        private const float HeadshotMultiplier = 4f;

        // Bölge çarpanları (CS2 modeli): kollar GÖVDE sayılır, yani 1× ayrı bir sabit istemez.
        // ⚠️ Denge sayılarının tek doğruluk kaynağı bu tablodur — WD_*.asset'te Inspector'dan
        // değiştirilen değer bir sonraki koşuda GERİ YAZILIR.
        private const float StomachMultiplier = 1.25f;
        private const float LegMultiplier = 0.75f;
        private const int SpareMagazines = 2;
        private const float KickBackMeters = 0.02f;
        private const float RecoilRecoverSpeed = 10f;
        private const float PitchJitter = 0.05f;
        private const string ReserveModeName = "DiscardMagazine";

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
            public int Damage;
            public int Rpm;
            public int Magazine;
            public float Reload;

            /// Hitscan menzili (metre). ⚠️ Bu bir DENGE kolu DEĞİLDİR: arenaların en uzun
            /// çatışma mesafesi ~20 m, en kısa menzil bile 28 m — yani hiçbir silah arena
            /// içinde menzile takılmaz. Sıralama CS'in "range modifier" kimliğini korur
            /// (uzun namlu daha uzağa) ve daha büyük mekanlar açıldığında anlam kazanır.
            /// Mesafeyle gerçekten hissedilen fark SAÇILIMDAN gelir (bkz. BaseSpread).
            public float Range;
            public float BaseSpread;
            public float BloomPerShot;
            public float MaxBloom;
            public float BloomRecovery;
            public float Kick;
            public string[] FireClips;
            public string MagOutClip;
            public float PitchBase;
            public float Volume;

            public string DryFireClip;   // artık silaha özel (eskiden paylaşılan DryFireClipName sabiti vardı)
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
        // Denge kaynağı: CS:GO/CS2'de karşılığı olanlar (AK-47, M4A1, FAMAS) doğrudan oradan;
        // olmayanlar (SCAR-L, G36C) PUBG + gerçek hayat teknik verisinden; M16 CS'te yok,
        // gerçek M16A4 (uzun namlu, 5.56) üzerinden M4'ün "nişancı" varyantı olarak kuruldu.
        private static readonly WeaponSpec[] Specs =
        {
            // AR_A_1 — CS:GO M4A4/M4A1 gövdesi: dengeli, orta geri tepme, en yaygın 5.56.
            new WeaponSpec
            {
                Name = "M4A1", PackPrefab = "AR_A_1", WeaponId = "m4a1", DisplayName = "M4A1",
                NetItemId = 1, HoldMode = "TwoHand",
                Damage = 33, Rpm = 666, Magazine = 30, Reload = 3.07f,
                Range = 40f, BaseSpread = 0.50f, BloomPerShot = 0.26f,
                MaxBloom = 2.2f, BloomRecovery = 4.5f, Kick = 2.0f, PitchBase = 1.00f, Volume = 1.0f,
                FireClips = new[] { "SFX_M4A4_Shot_01.wav", "SFX_M4A4_Shot_02.wav" },
                MagOutClip = "SFX_M4A4_Reload.wav", DryFireClip = "SFX_M4A4_DryFire.wav",
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
                FireClips = new[] { "SFX_AK47_Shot_01.wav", "SFX_AK47_Shot_02.wav" },
                MagOutClip = "SFX_AK47_Reload.wav", DryFireClip = "SFX_AK47_DryFire.wav",
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
                Damage = 32, Rpm = 625, Magazine = 30, Reload = 2.90f,
                Range = 38f, BaseSpread = 0.45f, BloomPerShot = 0.20f,
                MaxBloom = 1.8f, BloomRecovery = 5.0f, Kick = 1.6f, PitchBase = 0.94f, Volume = 1.0f,
                FireClips = new[] { "SFX_AUG_Shot_01.wav", "SFX_AUG_Shot_02.wav" },
                MagOutClip = "SFX_AUG_Reload.wav", DryFireClip = "SFX_AUG_DryFire.wav",
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
                Damage = 29, Rpm = 750, Magazine = 30, Reload = 2.70f,
                Range = 28f, BaseSpread = 0.70f, BloomPerShot = 0.30f,
                MaxBloom = 2.6f, BloomRecovery = 4.2f, Kick = 1.9f, PitchBase = 1.10f, Volume = 0.95f,
                FireClips = new[] { "SFX_GALIL_Shot_01.wav", "SFX_GALIL_Shot_02.wav" },
                MagOutClip = "SFX_GALIL_Reload.wav", DryFireClip = "SFX_GALIL_DryFire.wav",
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
                Damage = 30, Rpm = 666, Magazine = 25, Reload = 3.30f,
                Range = 32f, BaseSpread = 0.65f, BloomPerShot = 0.28f,
                MaxBloom = 2.4f, BloomRecovery = 4.2f, Kick = 1.9f, PitchBase = 1.03f, Volume = 1.0f,
                FireClips = new[] { "SFX_FAMAS_Shot_01.wav", "SFX_FAMAS_Shot_02.wav" },
                MagOutClip = "SFX_FAMAS_Reload.wav", DryFireClip = "SFX_FAMAS_DryFire.wav",
                FlashColorMin = new Color(1f, 0.88f, 0.58f), FlashColorMax = new Color(1f, 0.52f, 0.18f),
                FlashSizeMin = 0.03f, FlashSizeMax = 0.055f, FlashLifetime = 0.06f, FlashConeAngle = 20f,
                SmokeSizeMin = 0.035f, SmokeSizeMax = 0.06f, SmokeLifetime = 1.0f, SmokeAlpha = 0.28f,
                CasingFamily = "556x45",
            },
            // AR_A_2 — M16 (SUSTURUCUSUZ): CS'te karşılığı yok, gerçek M16A4 üzerinden kuruldu.
            // 20" namlu = en dar taban saçılım + en uzun menzil, bedeli en hızlı bozulan seri
            // atış (en yüksek bloom, en yavaş toparlanma) ve en uzun reload. Tek tek nişan alana
            // ödül, tarayana ceza. Ses M4 ailesiyle ORTAK ama pitch düşük — aynı silah, uzun namlu.
            new WeaponSpec
            {
                Name = "M16", PackPrefab = "AR_A_2", WeaponId = "m16", DisplayName = "M16",
                NetItemId = 6, HoldMode = "TwoHand",
                Damage = 31, Rpm = 700, Magazine = 30, Reload = 3.40f,
                Range = 50f, BaseSpread = 0.35f, BloomPerShot = 0.34f,
                MaxBloom = 2.8f, BloomRecovery = 3.8f, Kick = 2.3f, PitchBase = 0.93f, Volume = 1.0f,
                FireClips = new[] { "SFX_M4A4_Shot_01.wav", "SFX_M4A4_Shot_02.wav" },
                MagOutClip = "SFX_M4A4_Reload.wav", DryFireClip = "SFX_M4A4_DryFire.wav",
                FlashColorMin = new Color(1f, 0.90f, 0.74f), FlashColorMax = new Color(0.95f, 0.60f, 0.28f),
                FlashSizeMin = 0.032f, FlashSizeMax = 0.058f, FlashLifetime = 0.062f, FlashConeAngle = 19f,
                SmokeSizeMin = 0.032f, SmokeSizeMax = 0.055f, SmokeLifetime = 0.95f, SmokeAlpha = 0.26f,
                CasingFamily = "556x45",
            },
        };

        private enum BuildOutcome { Rebound, Failed }

        private static int _warnings;

        // Koşu özetinin kavrama pozu satırı: "kaç düğüm üretildi / kaçı zaten vardı / kaçı onarıldı".
        // Sayaç olmadan araç sessiz kalırdı — sağlam düğüme dokunulmadığı için ikinci koşu hiçbir iz
        // bırakmaz. Onarım AYRI sayılır: "mevcut" ile aynı kefeye konsaydı, kullanıcı pozunun
        // sıfırlandığını (parmakları tekrar bükmesi gerektiğini) hiç öğrenemezdi.
        private static int _posesCreated;
        private static int _posesExisting;
        private static int _posesRepaired;

        private static readonly Dictionary<string, Type> ResolvedTypes = new Dictionary<string, Type>();

        // ------------------------------------------------------------ menüler

        /// <summary>Tam akış: WD asset'leri → WPN prefablarının güncellenmesi → FX → katalog → ikinci geçiş.</summary>
        [MenuItem("Tools/VortexArena/Weapons/Build Weapon Prefabs", false, 20)]
        public static void BuildAll()
        {
            _warnings = 0;
            _posesCreated = 0;
            _posesExisting = 0;
            _posesRepaired = 0;

            int wdNew = 0;
            int wpnRebound = 0, wpnFailed = 0;
            bool fxCreated = false;
            bool catalogCreated = false;

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
                GameObject casing762 = EnsureCasingPrefab(Casing762Path, BulletPack762Path, live);
                GameObject casing556 = EnsureCasingPrefab(Casing556Path, BulletPack556Path, live);
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
                        switch (BuildWeaponPrefab(Specs[i], defs[i], casing762, casing556, smokeMaterial))
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

                // ---- ADIM 3: uzak atış FX prefabı (varsa dokunulmaz).
                fxCreated = EnsureRemoteShotFx(live);

                // ---- ADIM 4: WeaponCatalog.
                catalogCreated = UpdateCatalog();

                // ---- ADIM 5: WD.prefab ← WPN ikinci geçişi.
                LinkDefinitionPrefabs();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log(Log + "Bitti: WD " + wdNew + " yeni / " + (Specs.Length - wdNew) + " güncellendi · " +
                          "WPN " + wpnRebound + " güncellendi, " + wpnFailed + " başarısız · " +
                          "FX_RemoteShot " + (fxCreated ? "üretildi" : "mevcut") + " · " +
                          "WeaponCatalog " + (catalogCreated ? "üretildi" : "güncellendi") + " · " +
                          "kavrama pozu düğümü " + _posesCreated + " üretildi / " + _posesExisting +
                          " mevcut (dokunulmadı) / " + _posesRepaired + " onarıldı · " +
                          _warnings + " uyarı.");
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
        }

        /// <summary>Yalnız ADIM 4+5: kataloğu ve WD.prefab bağlarını tazeler; prefab üretmez.</summary>
        [MenuItem("Tools/VortexArena/Weapons/Build Weapon Prefabs (Yalnız Kataloğu Tazele)", false, 21)]
        public static void RefreshCatalogOnly()
        {
            _warnings = 0;

            bool catalogCreated = UpdateCatalog();
            LinkDefinitionPrefabs();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(Log + "Katalog tazelendi (WeaponCatalog " + (catalogCreated ? "üretildi" : "güncellendi") + ") · " +
                      _warnings + " uyarı.");
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
            SetNumber(so, "headshotMultiplier", HeadshotMultiplier, ctx);
            SetNumber(so, "stomachMultiplier", StomachMultiplier, ctx);
            SetNumber(so, "legMultiplier", LegMultiplier, ctx);
            SetNumber(so, "fireRateRpm", spec.Rpm, ctx);
            SetNumber(so, "range", spec.Range, ctx);
            SetNumber(so, "baseSpreadDegrees", spec.BaseSpread, ctx);
            SetNumber(so, "bloomPerShotDegrees", spec.BloomPerShot, ctx);
            SetNumber(so, "maxBloomDegrees", spec.MaxBloom, ctx);
            SetNumber(so, "bloomRecoveryPerSecond", spec.BloomRecovery, ctx);
            SetNumber(so, "kickDegrees", spec.Kick, ctx);
            SetNumber(so, "kickBackMeters", KickBackMeters, ctx);
            SetNumber(so, "recoilRecoverSpeed", RecoilRecoverSpeed, ctx);
            SetNumber(so, "magazineSize", spec.Magazine, ctx);
            SetNumber(so, "spareMagazines", SpareMagazines, ctx);
            SetEnumByName(so, "reserveMode", ReserveModeName, ctx);
            SetNumber(so, "reloadTime", spec.Reload, ctx);
            // Ses klipleri YALNIZ boşsa doldurulur — WD_<Ad>.asset'te Inspector'dan elle
            // sürüklenen bir klip varsa bu araç bir daha çalıştırılsa da SİLİNMEZ/EZİLMEZ.
            // Sesi değiştirmek için: Assets/_Shared/Arsenal/Data/WD_<Ad>.asset'i seç, ilgili
            // alana (Fire Clips / Mag Out Clip / Dry Fire Clip) yeni bir AudioClip sürükle.
            SetClipArrayIfEmpty(so, "fireClips", spec.FireClips, ctx);
            SetObjectRefIfEmpty(so, "magOutClip", LoadClip(spec.MagOutClip, ctx), ctx);
            SetObjectRefIfEmpty(so, "dryFireClip", LoadClip(spec.DryFireClip, ctx), ctx);
            // magInClip / pickupClip: spec'te karşılığı yok — hiç dokunulmaz (elle atanmışsa kalır).
            SetNumber(so, "firePitchBase", spec.PitchBase, ctx);
            SetNumber(so, "firePitchJitter", PitchJitter, ctx);
            SetNumber(so, "fireVolume", spec.Volume, ctx);
            // "prefab" alanı ADIM 5'te (WPN üretildikten sonra) bağlanır.

            so.ApplyModifiedPropertiesWithoutUndo();
            return def;
        }

        // ------------------------------------------------ ADIM 2: WPN prefabları

        /// <summary>
        /// Mevcut WPN_&lt;Ad&gt;.prefab'ı yerinde günceller (<see cref="RebindExistingPrefab"/>):
        /// definition bağları + namlu alevi/duman/kovan kiti + kavrama soketi kiti. Gövdeye
        /// (model, Muzzle/MuzzleFlash konumu, kavrama pozları) DOKUNULMAZ — onlar elle ayarlanır.
        /// Prefab yoksa üretilmez, hata basılır: eksik silahın prefabı repoya elle eklenir.
        /// </summary>
        private static BuildOutcome BuildWeaponPrefab(WeaponSpec spec, WeaponDefinition def,
            GameObject casing762, GameObject casing556, Material smokeMaterial)
        {
            string wpnPath = PrefabDir + "/WPN_" + spec.Name + ".prefab";
            string ctx = "WPN_" + spec.Name;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(wpnPath) == null)
            {
                Debug.LogError(Log + ctx + ": '" + wpnPath + "' yok — bu araç WPN prefabı ÜRETMEZ, " +
                               "yalnız mevcudu günceller. Prefabı repoya ekleyip tekrar çalıştır.");
                return BuildOutcome.Failed;
            }

            RebindExistingPrefab(wpnPath, spec, def, casing762, casing556, smokeMaterial, ctx);
            return BuildOutcome.Rebound;
        }

        /// <summary>
        /// WPN içeriğini açıp definition bağlarını tazeler VE silaha özgü namlu alevi/duman/kovan
        /// kitini (<see cref="ApplyVfxAndShellKit"/>) uygular — aracın TEK üretim yolu budur;
        /// model/Muzzle konumuna DOKUNULMAZ (elle ayarlanmış olabilir).
        /// </summary>
        private static void RebindExistingPrefab(string wpnPath, WeaponSpec spec, WeaponDefinition def,
            GameObject casing762, GameObject casing556, Material smokeMaterial, string ctx)
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
                    ApplyVfxAndShellKit(contents, spec, casing762, casing556, smokeMaterial, ctx);
                }

                // Tek çalışan yol burası — soket kiti de burada uygulanmazsa mevcut WPN'ler
                // filtresiz (yani mesafeden kavranabilir) kalırdı.
                ApplyGripSocketKit(contents, ctx);
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
        /// Kovan prefabı yoksa üretir (varsa dokunmaz): pack'teki mermi modelini
        /// unpack eder, küçük bir Rigidbody + bounds'a göre BoxCollider ekler.
        /// Fiziksel doğruluk hedeflenmez — kısa ömürlü, havuzlanan bir FX objesidir.
        /// </summary>
        private static GameObject EnsureCasingPrefab(string path, string sourcePackPath, List<GameObject> live)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                return existing;
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
        /// <see cref="SetClipArray"/> gibi ama dizi zaten AYNI UZUNLUKTA ve TAMAMEN DOLUYSA
        /// dokunmaz — kullanıcının Inspector'dan elle seçtiği klipler korunur.
        /// </summary>
        private static void SetClipArrayIfEmpty(SerializedObject so, string field, string[] clipNames, string ctx)
        {
            SerializedProperty p = FindProp(so, field, ctx);
            if (p == null)
            {
                return;
            }

            if (!p.isArray)
            {
                Warn(ctx + ": '" + field + "' dizi değil.");
                return;
            }

            bool alreadyFilled = p.arraySize == clipNames.Length;
            for (int i = 0; alreadyFilled && i < p.arraySize; i++)
            {
                if (p.GetArrayElementAtIndex(i).objectReferenceValue == null)
                {
                    alreadyFilled = false;
                }
            }

            if (alreadyFilled)
            {
                return; // elle atanmış — dokunma
            }

            p.arraySize = clipNames.Length;
            for (int i = 0; i < clipNames.Length; i++)
            {
                p.GetArrayElementAtIndex(i).objectReferenceValue = LoadClip(clipNames[i], ctx);
            }
        }

        /// <summary>AudioRoot altından klip yükler; ad null ise sessizce null döner.</summary>
        private static AudioClip LoadClip(string fileName, string ctx)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            string path = AudioRoot + "/" + fileName;
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip == null)
            {
                Warn(ctx + ": ses dosyası yok: " + path);
            }

            return clip;
        }

        /// <summary>
        /// Namlu alevi/duman/kovan kitini bir WPN kökü üzerinde kurar
        /// (<see cref="RebindExistingPrefab"/>). Muzzle/MuzzleFlash'ı OLDUĞU YERDE bulur ve
        /// TAŞIMAZ — model/namlu konumu elle ayarlanmıştır, araç onu bozmaz.
        /// </summary>
        private static void ApplyVfxAndShellKit(GameObject root, WeaponSpec spec,
            GameObject casing762, GameObject casing556, Material smokeMaterial, string ctx)
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
            Transform ejectT = rootT.Find("Eject");
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
            GameObject casingForSpec = spec.CasingFamily == "762x39" ? casing762 : casing556;
            BindFields(shellEjector, ctx, ("casingPrefab", casingForSpec), ("ejectPoint", ejectT));
        }

        /// <summary>
        /// Kavrama soketi kitini bir WPN kökü üzerinde kurar (idempotent): <see cref="ItemGripSockets"/>
        /// bileşeni + ISDK'nın <b>İKİ</b> yakın-kavrama bileşeninin <c>_interactorFilters</c>
        /// listesine bağlanması.
        /// <para>
        /// Filtre ISDK'nın tasarlanmış uzatma noktasıdır (<c>Interactable&lt;,&gt;.CanBeSelectedBy</c>
        /// her filtreyi sorar): kavramanın ALGISI ISDK'da kalır, biz yalnız "izin var mı" sorusuna
        /// cevap veririz.
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
        /// Kavrama pozu düğümleri (<c>GripPoses/Pose_&lt;Primary|Secondary&gt;_&lt;R|L&gt;</c>) de burada
        /// kurulur (<see cref="ApplyGripPoseNodes"/>) ama onlar <b>saf veridir</b>: ISDK'nın kavrama
        /// adaylığına GİRMEZ, çünkü <c>HandGrabInteractable._handGrabPoses</c> listesi <b>boş bırakılır</b>.
        /// Liste dolduğu anda ISDK kavramayı poz skoruna göre hesaplamaya başlar ve bugünkü kavrama
        /// hissi (collider mesafesi) sessizce değişirdi. Aynı sebeple <c>_handAligment</c> alanına da
        /// dokunulmaz: poz listesi boş kaldığı sürece o alanın etkisi yoktur.
        /// </para>
        /// <para>
        /// ⚠️ <b>Parmak pozunu araç YAZMAZ</b> — yalnız düğümü açar, içini insan
        /// <c>Tools &gt; VortexArena &gt; Weapons &gt; Kavrama Pozu Stüdyosu</c> ile doldurur: kavrama pozu
        /// bir ölçü değil bir tasarım kararıdır, gözle ayarlanır. Düğüm zaten varsa araç onun pozuna
        /// <b>dokunmaz</b> (elle ayarlanmış poz her koşuda silinmesin) — yalnız poz kullanılamaz
        /// hâldeyse varsayılanı geri yazar.
        /// </para>
        /// </summary>
        private static void ApplyGripSocketKit(GameObject root, string ctx)
        {
            // Tip Core'da yaşıyor (derleme zamanında bağlı) — tip adıyla arama gerekmez.
            ItemGripSockets sockets = root.GetComponent<ItemGripSockets>();
            if (sockets == null)
            {
                sockets = root.AddComponent<ItemGripSockets>();
            }

            // Soket tasarımı "eli soketin üstüne getir" demektir; mesafeden kavrama bunun tam zıddı.
            // Filtreye güvenip bileşeni bırakmak YETMEZ: ItemGripSockets.Filter el çözülemediğinde
            // FAIL-OPEN'dır (bkz. WarnFailOpen) ve o oturumlarda silah odanın öbür ucundan
            // kavranabilir kalırdı. Kapı "kapalı" demiyor, "çoğu zaman kapalı" diyor.
            // ⚠️ İki hat da silinir: hangisinin koşacağını rig seçiyor, biri unutulursa yasak
            // yarım kalır ve silah bazı yapılandırmalarda uzaktan kavranabilir olur.
            RemoveRootComponent(root, "Oculus.Interaction.DistanceGrabInteractable", ctx,
                "soket tasarımında mesafeden kavrama yok");
            RemoveRootComponent(root, "Oculus.Interaction.HandGrab.DistanceHandGrabInteractable", ctx,
                "soket tasarımında mesafeden kavrama yok (el hattı)");

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
                Warn(ctx + ": kökte Oculus.Interaction.GrabInteractable yok — kumanda hattının soket " +
                     "filtresi bağlanamadı (silah mesafe/soket ayrımı olmadan kavranır).");
            }
            else
            {
                BindSocketFilter(grabInteractable, sockets, ctx);
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

            BindSocketFilter(handGrab, sockets, ctx);

            // ⚠️ Poz düğümleri BİLEREK handGrab'ın _handGrabPoses listesine bağlanmaz (yukarıdaki not):
            // burada üretilenler kavramanın girdisi değil, kavrandıktan SONRA parmakları süren veridir.
            ApplyGripPoseNodes(root, ctx);
        }

        /// <summary>
        /// Kavrama pozu düğümlerini kurar (idempotent): <c>GripPoses/Pose_&lt;Kind&gt;_&lt;R|L&gt;</c>.
        /// Primary her silahta iki el için, Secondary yalnız çift elli silahlarda — poz el başınadır,
        /// silah iki elin de <b>ana</b> eli olabildiği için sağ/sol ayrı düğümdür.
        /// <para>
        /// Düğüm adları <see cref="ItemGripPoses"/>'ten gelir: üretici (bu araç) ile tüketici
        /// (<c>HandGripPoser</c> + stüdyo) kendi string'ini taşısaydı bir harflik sapma hata değil
        /// <b>sessiz bulunamama</b> üretirdi.
        /// </para>
        /// <para>
        /// ⚠️ Mevcut düğümün <b>pozuna</b> dokunulmaz — ses klipleriyle aynı gerekçe: elle ayarlanmış poz
        /// aracın her koşusunda silinseydi araç kullanılamaz olurdu. Tek istisna onarımdır: poz
        /// <b>kullanılamaz</b> hâldeyse (bkz. <see cref="RepairGripPoseNode"/>) varsayılanı geri gelir.
        /// </para>
        /// </summary>
        private static void ApplyGripPoseNodes(GameObject root, string ctx)
        {
            Transform rootT = root.transform;
            Transform posesRoot = rootT.Find(ItemGripPoses.RootNodeName);
            if (posesRoot == null)
            {
                var posesGo = new GameObject(ItemGripPoses.RootNodeName);
                posesRoot = posesGo.transform;
                posesRoot.SetParent(rootT, false);
                posesRoot.localPosition = Vector3.zero;
                posesRoot.localRotation = Quaternion.identity;
                posesRoot.localScale = Vector3.one;
            }

            var weapon = root.GetComponent<Weapon>();
            WeaponDefinition def = weapon != null ? weapon.Definition : null;
            if (def == null)
            {
                // Tanım olmadan çift ellilik bilinemez; Primary'yi yine de kurmak "hiç kurmamak"tan
                // iyidir, eksik Secondary bir sonraki koşuda (tanım bağlanınca) gelir.
                Warn(ctx + ": Weapon.definition boş — çift ellilik okunamadı, yalnız Primary poz " +
                     "düğümleri kuruldu.");
            }

            int created = 0;
            created += EnsureGripPoseNode(posesRoot, rootT, GripSocketKind.Primary, true, ctx) ? 1 : 0;
            created += EnsureGripPoseNode(posesRoot, rootT, GripSocketKind.Primary, false, ctx) ? 1 : 0;
            if (def != null && def.IsTwoHanded)
            {
                created += EnsureGripPoseNode(posesRoot, rootT, GripSocketKind.Secondary, true, ctx) ? 1 : 0;
                created += EnsureGripPoseNode(posesRoot, rootT, GripSocketKind.Secondary, false, ctx) ? 1 : 0;
            }

            if (created > 0)
            {
                Debug.Log(Log + ctx + ": " + created + " kavrama pozu düğümü üretildi (bind duruşunda) — " +
                          "parmakları Kavrama Pozu Stüdyosu'ndan yaz, araç bir daha dokunmaz.");
            }
        }

        /// <summary>
        /// Tek bir poz düğümünü kurar; zaten varsa (ve pozu sağlamsa) hiçbir şeye dokunmadan
        /// <c>false</c> döner.
        /// <para>
        /// Yazılan iki şey: <c>_relativeTo</c> (silahın KÖKÜ — poz ölçüsü ona göre saklanır, aksi hâlde
        /// düğümün kendi yerel uzayına düşer) ve ilgili elin varsayılan <c>HandPose</c>'u.
        /// </para>
        /// <para>
        /// ⚠️ <b>Poz SERİALİZE ALAN ADIYLA yazılmaz, ISDK'nın public API'siyle yazılır</b>
        /// (<see cref="HandGrabPose.InjectOptionalHandPose"/>). <c>HandGrabPose</c> aynı pozu iki ayrı
        /// alanda taşıyor — <c>_handPose</c> (OVR) ve <c>_targetHandPose</c> (OpenXR) — ve hangisinin
        /// canlı olduğunu <c>ISDK_OPENXR_HAND</c> belirliyor. O tanım paketin kendi asmdef'inde
        /// <c>versionDefines</c> ile ve <b>boş ifadeyle</b> üretiliyor: yani ISDK derlenirken HER ZAMAN
        /// açık, bizim derlememizde ise HİÇ tanımlı değil. Alan adına yazan bir araç bu yüzden ölü
        /// alanı doldurur; belirti sessizdir — düğüm vardır, <c>Uses Hand Pose</c> işaretlidir, ama
        /// canlı poz boştur ve el bind duruşunda kalır.
        /// <b>Kural: <c>#if</c>'li bir ISDK alanına asla alan adıyla yazma, public API'sini çağır</b>
        /// (define'ı kendi asmdef'imize kopyalamak da yasak: ISDK'nın iç ayrıntısı ikinci bir doğruluk
        /// kaynağı olur ve paket değişince sessizce sapar).
        /// </para>
        /// <para>
        /// ⚠️ <c>_usesHandPose</c> ayrıca yazılmaz: <c>InjectOptionalHandPose</c> onu pozla birlikte
        /// kendi kurar. Boş kalsaydı <c>HandGrabPose.UsesHandPose()</c> false döner ve düğüm yalnız bir
        /// konum işaretçisi olurdu.
        /// </para>
        /// <para>
        /// ⚠️ <b>Mevcut düğüm ONARILIR ama üzerine yazılmaz:</b> yalnız canlı pozu kullanılamaz hâldeyse
        /// (<see cref="GripPoseStudio.NeedsPoseRepair"/> — eski koşuların ölü alana yazdığı düğümler)
        /// varsayılan poz yeniden enjekte edilir. Kullanılabilir bir poz varsa dokunulmaz: elle bükülmüş
        /// parmaklar aracın her koşusunda silinseydi araç kullanılamaz olurdu.
        /// </para>
        /// <para>
        /// Parmakların kendisi burada AYARLANMAZ — düğüm ISDK'nın bind duruşuyla açılır, içini insan
        /// <c>Kavrama Pozu Stüdyosu</c>'ndan doldurur.
        /// </para>
        /// </summary>
        private static bool EnsureGripPoseNode(Transform posesRoot, Transform itemRoot,
            GripSocketKind kind, bool rightHand, string ctx)
        {
            string nodeName = ItemGripPoses.NodeName(kind, rightHand);
            Transform existing = posesRoot.Find(nodeName);
            if (existing != null)
            {
                _posesExisting++;
                RepairGripPoseNode(existing.GetComponent<HandGrabPose>(), rightHand, ctx);
                return false;
            }

            var node = new GameObject(nodeName);
            Transform nodeT = node.transform;
            nodeT.SetParent(posesRoot, false);
            nodeT.localPosition = Vector3.zero;
            nodeT.localRotation = Quaternion.identity;
            nodeT.localScale = Vector3.one;

            // Tip artık derleme zamanında bağlı (asmdef Oculus.Interaction referanslıyor) — tip adından
            // gitmeye gerek yok; public API'yi çağırabilmenin ön koşulu da bu referanstır.
            var pose = node.AddComponent<HandGrabPose>();
            pose.InjectAllHandGrabPose(itemRoot);
            pose.InjectOptionalHandPose(DefaultHandPose(rightHand));

            EditorUtility.SetDirty(pose);
            _posesCreated++;
            return true;
        }

        /// <summary>
        /// Canlı pozu kullanılamaz hâldeki bir düğüme varsayılan pozu yeniden yazar (sağlamsa
        /// dokunmaz).
        /// <para>⚠️ Onarım kapısı ŞART: pozu ISDK'nın ölü alanında kalmış bir düğüm "zaten var"
        /// sayıldığı için sonraki koşularda da hiç düzelmez ve kullanıcının tek çaresi düğümü elle
        /// silmek olurdu.</para>
        /// </summary>
        private static void RepairGripPoseNode(HandGrabPose pose, bool rightHand, string ctx)
        {
            if (!GripPoseStudio.NeedsPoseRepair(pose))
            {
                return;
            }

            pose.InjectOptionalHandPose(DefaultHandPose(rightHand));
            EditorUtility.SetDirty(pose);
            _posesRepaired++;

            Debug.Log(Log + ctx + ": " + pose.gameObject.name + " düğümünde kullanılabilir poz yoktu — " +
                      "varsayılan poz yazıldı (parmakları Kavrama Pozu Stüdyosu'ndan tekrar bük).");
        }

        /// <summary>
        /// İlgili elin bind duruşundaki varsayılan pozu.
        /// <para>⚠️ Poz kurma <b>tek yerde</b> durur (<see cref="GripPoseStudio.CreateDefaultHandPose"/>):
        /// <c>new HandPose(handedness)</c> eklem dizisini sıfır quaternion'larla bırakıyor ve o pozla
        /// açılan düğüm hiç çizilmiyor — ikinci bir kurulum yazılsaydı biri o tuzağa düşerdi.</para>
        /// </summary>
        private static HandPose DefaultHandPose(bool rightHand)
        {
            return GripPoseStudio.CreateDefaultHandPose(
                rightHand
                    ? Oculus.Interaction.Input.Handedness.Right
                    : Oculus.Interaction.Input.Handedness.Left);
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
        /// Bir ISDK interactable'ının <c>_interactorFilters</c> listesini soket bileşenine
        /// sabitler (idempotent).
        /// <para>Silahın başka bir interactor filtresi yok ve olması da beklenmiyor: liste tek
        /// elemana indirilir.</para>
        /// </summary>
        private static void BindSocketFilter(Component interactable, Object filter, string ctx)
        {
            var so = new SerializedObject(interactable);
            SerializedProperty filters = so.FindProperty("_interactorFilters");
            if (filters == null || !filters.isArray)
            {
                Warn(ctx + ": " + interactable.GetType().Name + " üzerinde '_interactorFilters' alanı yok " +
                     "ya da dizi değil (ISDK sözleşme kayması?) — soket filtresi bağlanamadı.");
                return;
            }

            if (filters.arraySize == 1 && filters.GetArrayElementAtIndex(0).objectReferenceValue == filter)
            {
                return; // zaten bağlı — idempotent
            }

            filters.arraySize = 1;
            filters.GetArrayElementAtIndex(0).objectReferenceValue = filter;
            so.ApplyModifiedPropertiesWithoutUndo();
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
        /// ⚠️ Bu, <see cref="ApplyGripSocketKit"/>'in mesafe-kavrama silme adımlarıyla (kumanda ve
        /// el hattı) ÇELİŞMEZ: o adımlar <see cref="FindComponentByTypeFullName"/> kullanıyor ve o metot yalnız
        /// KÖKÜN bileşenlerine bakıyor (çocuklara inmiyor). Yasak <c>WPN_*</c> kökü içindir —
        /// soketli yakın kavramanın zıddı olduğu için; çerçeve ayrı bir objedir ve soket kullanmaz.
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
