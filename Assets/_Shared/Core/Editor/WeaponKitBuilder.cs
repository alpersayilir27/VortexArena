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
    /// <c>Tools &gt; VortexArena &gt; Build Weapon Prefabs</c> — Low Poly AR Weapon Pack
    /// modellerinden 6 silahın tüm kitini üretir/günceller:
    /// <c>WD_&lt;Ad&gt;.asset</c> (WeaponDefinition), <c>WPN_&lt;Ad&gt;.prefab</c>
    /// (AK47_Red şablonundan), <c>FX_RemoteShot.prefab</c> ve
    /// <c>Resources/WeaponCatalog.asset</c>.
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
    /// </summary>
    public static class WeaponKitBuilder
    {
        // ------------------------------------------------------------ sabitler

        /// <summary>Pack klasörü ileride taşınırsa yalnız bu satır değişir.</summary>
        private const string PackRoot = "Assets/Low Poly AR Weapon Pack 1";

        private const string AudioRoot = "Assets/Audio/Weapons";
        private const string TemplatePath = "Assets/_Shared/Arsenal/Prefabs/AK47_Red.prefab";

        private const string DataDir = "Assets/_Shared/Arsenal/Data";
        private const string PrefabDir = "Assets/_Shared/Arsenal/Prefabs";
        private const string FxDir = "Assets/_Shared/FX";
        private const string FxPrefabPath = FxDir + "/FX_RemoteShot.prefab";
        private const string CatalogDir = "Assets/_Shared/Data/Resources";
        private const string CatalogPath = CatalogDir + "/WeaponCatalog.asset";

        private const string Log = "[BuildWeaponPrefabs] ";

        // Tüm silahlarda ortak sayılar (tablo başlığındaki varsayılanlar).
        private const float HeadshotMultiplier = 4f;
        private const int SpareMagazines = 2;
        private const float KickBackMeters = 0.02f;
        private const float RecoilRecoverSpeed = 10f;
        private const float Range = 60f;
        private const float PitchJitter = 0.05f;
        private const string ReserveModeName = "DiscardMagazine";
        private const string DryFireClipName = "SFX_DryFire.wav";

        private static readonly string[] AkFireClips = { "SFX_AK47_Shot_01.wav", "SFX_AK47_Shot_02.wav" };
        private static readonly string[] M4FireClips = { "SFX_M4_Shot_01.wav", "SFX_M4_Shot_02.wav" };
        private const string AkMagOutClip = "SFX_AK47_Reload_HQ.wav";
        private const string M4MagOutClip = "SFX_M4_Reload_HQ2.wav";

        // ---------------------------------------------------------- silah tablosu

        private struct WeaponSpec
        {
            public string Name;        // dosya eki: WD_<Name>, WPN_<Name>
            public string PackPrefab;  // PackRoot/Prefabs/Weapons/<PackPrefab>.prefab
            public string WeaponId;
            public string DisplayName;
            public int Damage;
            public int Rpm;
            public int Magazine;
            public float Reload;
            public float BaseSpread;
            public float BloomPerShot;
            public float MaxBloom;
            public float BloomRecovery;
            public float Kick;
            public string[] FireClips;
            public string MagOutClip;
            public float PitchBase;
            public float Volume;
        }

        private static readonly WeaponSpec[] Specs =
        {
            new WeaponSpec { Name = "AK47",  PackPrefab = "AR_A_1", WeaponId = "ak47",  DisplayName = "AK-47",    Damage = 36, Rpm = 600, Magazine = 30, Reload = 2.43f, BaseSpread = 1.10f, BloomPerShot = 0.30f, MaxBloom = 2.5f, BloomRecovery = 4.0f, Kick = 2.4f, FireClips = AkFireClips, MagOutClip = AkMagOutClip, PitchBase = 1.00f, Volume = 1.0f },
            new WeaponSpec { Name = "M4A4",  PackPrefab = "AR_B",   WeaponId = "m4a4",  DisplayName = "M4A4",     Damage = 33, Rpm = 666, Magazine = 30, Reload = 3.07f, BaseSpread = 0.90f, BloomPerShot = 0.25f, MaxBloom = 2.2f, BloomRecovery = 4.5f, Kick = 2.0f, FireClips = M4FireClips, MagOutClip = M4MagOutClip, PitchBase = 1.00f, Volume = 1.0f },
            new WeaponSpec { Name = "M4A1S", PackPrefab = "AR_C",   WeaponId = "m4a1s", DisplayName = "M4A1-S",   Damage = 38, Rpm = 600, Magazine = 25, Reload = 3.07f, BaseSpread = 0.80f, BloomPerShot = 0.22f, MaxBloom = 2.0f, BloomRecovery = 4.5f, Kick = 1.8f, FireClips = M4FireClips, MagOutClip = M4MagOutClip, PitchBase = 0.92f, Volume = 0.80f },
            new WeaponSpec { Name = "Galil", PackPrefab = "AR_D",   WeaponId = "galil", DisplayName = "Galil AR", Damage = 30, Rpm = 666, Magazine = 35, Reload = 3.03f, BaseSpread = 1.20f, BloomPerShot = 0.30f, MaxBloom = 2.6f, BloomRecovery = 4.0f, Kick = 2.2f, FireClips = AkFireClips, MagOutClip = AkMagOutClip, PitchBase = 1.06f, Volume = 1.0f },
            new WeaponSpec { Name = "FAMAS", PackPrefab = "AR_E",   WeaponId = "famas", DisplayName = "FAMAS",    Damage = 30, Rpm = 666, Magazine = 25, Reload = 3.30f, BaseSpread = 1.10f, BloomPerShot = 0.28f, MaxBloom = 2.4f, BloomRecovery = 4.2f, Kick = 1.9f, FireClips = M4FireClips, MagOutClip = M4MagOutClip, PitchBase = 1.05f, Volume = 1.0f },
            new WeaponSpec { Name = "AUG",   PackPrefab = "AR_A_2", WeaponId = "aug",   DisplayName = "AUG",      Damage = 28, Rpm = 666, Magazine = 30, Reload = 3.80f, BaseSpread = 0.90f, BloomPerShot = 0.25f, MaxBloom = 2.2f, BloomRecovery = 4.5f, Kick = 1.9f, FireClips = M4FireClips, MagOutClip = M4MagOutClip, PitchBase = 0.97f, Volume = 1.0f },
        };

        private enum BuildOutcome { FromTemplate, Rebound, Failed }

        private static int _warnings;
        private static readonly Dictionary<string, Type> ResolvedTypes = new Dictionary<string, Type>();

        // ------------------------------------------------------------ menüler

        /// <summary>Tam akış: WD asset'leri → WPN prefabları → FX → katalog → ikinci geçiş.</summary>
        [MenuItem("Tools/VortexArena/Build Weapon Prefabs")]
        public static void BuildAll()
        {
            _warnings = 0;

            int wdNew = 0;
            int wpnTemplate = 0, wpnRebound = 0, wpnFailed = 0;
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
                var template = AssetDatabase.LoadAssetAtPath<GameObject>(TemplatePath);
                if (template == null)
                {
                    Warn("Şablon yok: " + TemplatePath + " — mevcut WPN prefabları yalnız yerinde güncellenecek.");
                }

                // ---- ADIM 1: WeaponDefinition asset'leri (prefab alanı ADIM 5'te bağlanır).
                var defs = new WeaponDefinition[Specs.Length];
                for (int i = 0; i < Specs.Length; i++)
                {
                    defs[i] = EnsureDefinition(Specs[i], ref wdNew);
                }

                AssetDatabase.SaveAssets();

                // ---- ADIM 2: WPN prefabları.
                for (int i = 0; i < Specs.Length; i++)
                {
                    if (defs[i] == null)
                    {
                        wpnFailed++;
                        continue;
                    }

                    try
                    {
                        switch (BuildWeaponPrefab(Specs[i], defs[i], template, live))
                        {
                            case BuildOutcome.FromTemplate: wpnTemplate++; break;
                            case BuildOutcome.Rebound: wpnRebound++; break;
                            default: wpnFailed++; break;
                        }
                    }
                    catch (Exception e)
                    {
                        wpnFailed++;
                        Debug.LogError(Log + Specs[i].Name + ": prefab üretimi hata verdi — " + e);
                    }
                }

                // ---- ADIM 3: uzak atış FX prefabı (varsa dokunulmaz).
                fxCreated = EnsureRemoteShotFx(template, live);

                // ---- ADIM 4: WeaponCatalog.
                catalogCreated = UpdateCatalog();

                // ---- ADIM 5: WD.prefab ← WPN ikinci geçişi.
                LinkDefinitionPrefabs();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log(Log + "Bitti: WD " + wdNew + " yeni / " + (Specs.Length - wdNew) + " güncellendi · " +
                          "WPN şablondan " + wpnTemplate + ", yerinde " + wpnRebound + ", hata " + wpnFailed + " · " +
                          "FX_RemoteShot " + (fxCreated ? "üretildi" : "mevcut") + " · " +
                          "WeaponCatalog " + (catalogCreated ? "üretildi" : "güncellendi") + " · " +
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
        [MenuItem("Tools/VortexArena/Build Weapon Prefabs (Yalnız Kataloğu Tazele)")]
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
            SetNumber(so, "damage", spec.Damage, ctx);
            SetNumber(so, "headshotMultiplier", HeadshotMultiplier, ctx);
            SetNumber(so, "fireRateRpm", spec.Rpm, ctx);
            SetNumber(so, "range", Range, ctx);
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
            SetClipArray(so, "fireClips", spec.FireClips, ctx);
            SetObjectRef(so, "magOutClip", LoadClip(spec.MagOutClip, ctx), ctx, true);
            SetObjectRef(so, "magInClip", null, ctx, true);
            SetObjectRef(so, "dryFireClip", LoadClip(DryFireClipName, ctx), ctx, true);
            SetObjectRef(so, "pickupClip", null, ctx, true);
            SetNumber(so, "firePitchBase", spec.PitchBase, ctx);
            SetNumber(so, "firePitchJitter", PitchJitter, ctx);
            SetNumber(so, "fireVolume", spec.Volume, ctx);
            // "prefab" alanı ADIM 5'te (WPN üretildikten sonra) bağlanır.

            so.ApplyModifiedPropertiesWithoutUndo();
            return def;
        }

        // ------------------------------------------------ ADIM 2: WPN prefabları

        /// <summary>
        /// Şablon varsa: instantiate + tam unpack (yoksa WPN silinecek şablonun variant'ı olurdu),
        /// Model içi pack modeliyle değiştirilir (nested prefab bağı yaşar), bileşen/referans
        /// bağları kurulur ve WPN_&lt;Ad&gt;.prefab'a kaydedilir. Şablon yoksa mevcut WPN yerinde
        /// yalnız definition bağlarıyla güncellenir.
        /// </summary>
        private static BuildOutcome BuildWeaponPrefab(WeaponSpec spec, WeaponDefinition def, GameObject template, List<GameObject> live)
        {
            string wpnPath = PrefabDir + "/WPN_" + spec.Name + ".prefab";
            string ctx = "WPN_" + spec.Name;

            var existingWpn = AssetDatabase.LoadAssetAtPath<GameObject>(wpnPath);

            GameObject packPrefab = null;
            if (template != null)
            {
                string packPath = PackRoot + "/Prefabs/Weapons/" + spec.PackPrefab + ".prefab";
                packPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(packPath);
                if (packPrefab == null)
                {
                    Debug.LogError(Log + ctx + ": pack prefabı yok: " + packPath);
                }
            }

            if (template == null || packPrefab == null)
            {
                // Güncelleme modu: şablon (veya pack modeli) yok ama WPN varsa yalnız bağları tazele.
                if (existingWpn != null)
                {
                    RebindExistingPrefab(wpnPath, def, ctx);
                    return BuildOutcome.Rebound;
                }

                Debug.LogError(Log + ctx + ": ne şablon/pack modeli ne de mevcut WPN prefabı var — üretilemedi.");
                return BuildOutcome.Failed;
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(template);
            live.Add(inst);

            // ZORUNLU: tam unpack — aksi hâlde SaveAsPrefabAsset şablonun variant'ını üretir.
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            inst.name = "WPN_" + spec.Name;
            Transform rootT = inst.transform;

            // Şablonda eski Assembly-CSharp kalıntısı missing script kalırsa temizle (yoksa sessiz geçer).
            RemoveMissingScripts(inst);

            Transform modelT = rootT.Find("Model");
            if (modelT == null)
            {
                modelT = FindDeepChild(rootT, "Model");
            }

            if (modelT == null)
            {
                Debug.LogError(Log + ctx + ": şablonda 'Model' child'ı yok — üretilemedi.");
                Object.DestroyImmediate(inst);
                return BuildOutcome.Failed;
            }

            // YAML gerçeği: şablonda Muzzle, Model'in ALTINDA (MuzzleFlash da Muzzle'ın altında).
            // Model içi boşaltılmadan önce Muzzle köke alınır; MuzzleFlash Muzzle'ın altında kalır.
            Transform muzzleT = FindDeepChild(rootT, "Muzzle");
            Transform flashT = FindDeepChild(rootT, "MuzzleFlash");

            if (muzzleT == null)
            {
                Warn(ctx + ": şablonda 'Muzzle' child'ı bulunamadı.");
            }
            else if (muzzleT.parent != rootT)
            {
                muzzleT.SetParent(rootT, false);
            }

            if (flashT != null && (muzzleT == null || !flashT.IsChildOf(muzzleT)) && flashT.parent != rootT)
            {
                // Beklenmedik yerleşim: flash'ı muzzle'ın (o da yoksa kökün) altına kurtar.
                flashT.SetParent(muzzleT != null ? muzzleT : rootT, false);
            }

            if (flashT == null)
            {
                Warn(ctx + ": şablonda 'MuzzleFlash' child'ı bulunamadı.");
            }

            // Model içini boşalt (eski AK47 parçaları); Modelin kendi local TRS'ine dokunulmaz.
            for (int i = modelT.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(modelT.GetChild(i).gameObject);
            }

            // Pack modelini Model altına nested prefab olarak tak — unpack ETME, bağ yaşasın.
            var packInst = (GameObject)PrefabUtility.InstantiatePrefab(packPrefab, modelT);
            packInst.transform.localPosition = Vector3.zero;
            packInst.transform.localRotation = Quaternion.identity;
            packInst.transform.localScale = Vector3.one;

            // Pack modelinin birleşik bounds'u KÖK yerel uzayında.
            Bounds bounds = ComputeLocalBounds(rootT, packInst, ctx);
            if (bounds.size.z < bounds.size.x)
            {
                Debug.LogWarning(Log + ctx + ": bounds'un Z uzunluğu X'ten kısa — pack modeli +Z'ye bakmıyor olabilir; " +
                                 "hizalama sonradan elle/parametreyle düzeltilecek.");
            }

            // Muzzle namlu ucuna: (0, merkez Y, maxZ + 5 mm). Flash aynı konumda (Muzzle altındaysa local sıfır).
            var muzzleLocal = new Vector3(0f, bounds.center.y, bounds.max.z + 0.005f);
            if (muzzleT != null)
            {
                muzzleT.localPosition = muzzleLocal;
                muzzleT.localRotation = Quaternion.identity;
            }

            if (flashT != null)
            {
                if (muzzleT != null && flashT.IsChildOf(muzzleT))
                {
                    flashT.localPosition = Vector3.zero;
                    flashT.localRotation = Quaternion.identity;
                }
                else
                {
                    flashT.localPosition = muzzleLocal;
                    flashT.localRotation = Quaternion.identity;
                }
            }

            // Kök BoxCollider yeni modele göre (eksen başına min 4 cm).
            var box = inst.GetComponent<BoxCollider>();
            if (box == null)
            {
                box = inst.AddComponent<BoxCollider>();
            }

            box.center = bounds.center;
            box.size = new Vector3(
                Mathf.Max(bounds.size.x, 0.04f),
                Mathf.Max(bounds.size.y, 0.04f),
                Mathf.Max(bounds.size.z, 0.04f));

            // Eksik bileşenler (şablon eski sürümden geldiyse) — tip adıyla, derleme bağımlılığı almadan.
            Weapon weapon = inst.GetComponent<Weapon>();
            if (weapon == null)
            {
                weapon = inst.AddComponent<Weapon>();
            }

            WeaponAudio weaponAudio = inst.GetComponent<WeaponAudio>();
            if (weaponAudio == null)
            {
                weaponAudio = inst.AddComponent<WeaponAudio>();
            }

            Component animator = EnsureComponentByTypeName(inst, "WeaponAnimator", ctx);
            Component reloadGesture = EnsureComponentByTypeName(inst, "WeaponReloadGesture", ctx);

            // Cephane göstergesi artık silah üstünde DEĞİL (AmmoHud, ekran-köşesi paneli) —
            // eski kurulumdan kalan AmmoDisplay child'ı/bileşeni varsa temizle.
            RemoveLegacyAmmoDisplay(inst);

            // ---- Referans bağları (alan adları sözleşmeden BİREBİR).
            Component grabbable = FindComponentByTypeFullName(inst, "Oculus.Interaction.Grabbable");
            if (grabbable == null)
            {
                Warn(ctx + ": kökte Oculus.Interaction.Grabbable yok.");
            }

            ParticleSystem flashPs = flashT != null ? flashT.GetComponent<ParticleSystem>() : null;
            if (flashT != null && flashPs == null)
            {
                Warn(ctx + ": MuzzleFlash üzerinde ParticleSystem yok.");
            }

            AudioSource muzzleSource = muzzleT != null ? muzzleT.GetComponent<AudioSource>() : null;
            if (muzzleT != null && muzzleSource == null)
            {
                Warn(ctx + ": Muzzle üzerinde AudioSource yok.");
            }

            var weaponSo = new SerializedObject(weapon);
            SetObjectRef(weaponSo, "definition", def, ctx);
            SetObjectRef(weaponSo, "muzzle", muzzleT, ctx);
            SetObjectRef(weaponSo, "modelPivot", modelT, ctx);
            SetObjectRef(weaponSo, "grabbable", grabbable, ctx);
            SetObjectRef(weaponSo, "muzzleFlash", flashPs, ctx);
            SetObjectRef(weaponSo, "weaponAudio", weaponAudio, ctx);
            // hitEffectPrefab + inputActions: şablondan gelen değerler KORUNUR — dokunma.
            weaponSo.ApplyModifiedPropertiesWithoutUndo();

            var audioSo = new SerializedObject(weaponAudio);
            SetObjectRef(audioSo, "source", muzzleSource, ctx);
            var audioDefProp = audioSo.FindProperty("definition");
            if (audioDefProp != null)
            {
                // Alan yoksa sessiz atla — Configure runtime'da da çağrılıyor.
                audioDefProp.objectReferenceValue = def;
            }

            audioSo.ApplyModifiedPropertiesWithoutUndo();

            BindFields(animator, ctx, ("weapon", weapon), ("weaponAudio", weaponAudio), ("modelRoot", modelT));
            BindFields(reloadGesture, ctx, ("weapon", weapon));

            // NOT: eski Weapon alan kalıntıları (team, damage vs.) yeni script'te alan olmadığı
            // için kayıt sırasında kendiliğinden düşer — ek işlem gerekmez.

            PrefabUtility.SaveAsPrefabAsset(inst, wpnPath, out bool saved);
            Object.DestroyImmediate(inst);

            if (!saved)
            {
                Debug.LogError(Log + ctx + ": SaveAsPrefabAsset başarısız: " + wpnPath);
                return BuildOutcome.Failed;
            }

            return BuildOutcome.FromTemplate;
        }

        /// <summary>Güncelleme modu: WPN içeriğini açıp yalnız definition bağlarını tazeler.</summary>
        private static void RebindExistingPrefab(string wpnPath, WeaponDefinition def, string ctx)
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

                PrefabUtility.SaveAsPrefabAsset(contents, wpnPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// Eski kurulumdan kalan silah-üstü göstergesini söker: "AmmoDisplay" child'ı ve
        /// (varsa) WeaponAmmoDisplay bileşeni. Gösterge artık AmmoHud'dadır (ekran köşesi).
        /// </summary>
        private static void RemoveLegacyAmmoDisplay(GameObject inst)
        {
            Transform ammoT = inst.transform.Find("AmmoDisplay");
            if (ammoT != null)
            {
                UnityEngine.Object.DestroyImmediate(ammoT.gameObject);
            }

            Type displayType = ResolveType("VortexArena.Core.Combat.WeaponAmmoDisplay");
            if (displayType != null && typeof(Component).IsAssignableFrom(displayType))
            {
                var legacy = inst.GetComponent(displayType);
                if (legacy != null)
                {
                    UnityEngine.Object.DestroyImmediate(legacy);
                }
            }
        }

        // ------------------------------------------------ ADIM 3: FX_RemoteShot

        /// <summary>
        /// FX_RemoteShot.prefab yoksa üretir (varsa dokunmaz): şablonun (o da yoksa ilk WPN'in)
        /// Muzzle'ındaki AudioSource + MetaXRAudioSource köke kopyalanır, MuzzleFlash "Flash"
        /// adlı child olarak klonlanır. Döner: bu koşuda üretildi mi.
        /// </summary>
        private static bool EnsureRemoteShotFx(GameObject template, List<GameObject> live)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(FxPrefabPath) != null)
            {
                return false; // varsa dokunma
            }

            GameObject sourcePrefab = template;
            if (sourcePrefab == null)
            {
                for (int i = 0; i < Specs.Length && sourcePrefab == null; i++)
                {
                    sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/WPN_" + Specs[i].Name + ".prefab");
                }
            }

            if (sourcePrefab == null)
            {
                Warn("FX_RemoteShot: kaynak yok (ne şablon ne WPN prefabı) — üretilemedi.");
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

        /// <summary>Missing-script kalıntılarını kök + tüm çocuklardan temizler.</summary>
        private static void RemoveMissingScripts(GameObject go)
        {
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
            for (int i = 0; i < go.transform.childCount; i++)
            {
                RemoveMissingScripts(go.transform.GetChild(i).gameObject);
            }
        }

        /// <summary>
        /// Pack instance'ın tüm Renderer'larının birleşik bounds'u, kökün worldToLocalMatrix'iyle
        /// KÖK yerel uzayına çevrilir (8 köşe tek tek dönüştürülür).
        /// </summary>
        private static Bounds ComputeLocalBounds(Transform root, GameObject packInstance, string ctx)
        {
            Renderer[] renderers = packInstance.GetComponentsInChildren<Renderer>(true);
            Matrix4x4 toRoot = root.worldToLocalMatrix;

            bool hasAny = false;
            Vector3 min = Vector3.positiveInfinity;
            Vector3 max = Vector3.negativeInfinity;

            for (int r = 0; r < renderers.Length; r++)
            {
                Bounds wb = renderers[r].bounds;
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
                Warn(ctx + ": pack modelinde Renderer yok — şablonun collider ölçüleri varsayıldı.");
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
        /// kapalıysa mevcut değer korunur (çalışan şablon bağını null ile ezmemek için).
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

        /// <summary>AudioClip dizisini dosya adlarından doldurur.</summary>
        private static void SetClipArray(SerializedObject so, string field, string[] clipNames, string ctx)
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

        /// <summary>Derleme referansı olmayan bileşenlerde (TMP) public property'yi reflection ile yazar.</summary>
        private static void SetReflectedProperty(Component target, string propertyName, object value, string ctx)
        {
            if (target == null)
            {
                return;
            }

            PropertyInfo p = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanWrite)
            {
                Warn(ctx + ": " + target.GetType().Name + "." + propertyName + " property'si yazılamadı.");
                return;
            }

            try
            {
                p.SetValue(target, value);
            }
            catch (Exception e)
            {
                Warn(ctx + ": " + target.GetType().Name + "." + propertyName + " ataması başarısız — " + e.Message);
            }
        }

        /// <summary>Enum tipli property'yi üye adıyla yazar (ör. TMP alignment = Center).</summary>
        private static void SetReflectedEnumProperty(Component target, string propertyName, string memberName, string ctx)
        {
            if (target == null)
            {
                return;
            }

            PropertyInfo p = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (p == null || !p.CanWrite || !p.PropertyType.IsEnum)
            {
                Warn(ctx + ": " + target.GetType().Name + "." + propertyName + " enum property'si yazılamadı.");
                return;
            }

            try
            {
                p.SetValue(target, Enum.Parse(p.PropertyType, memberName));
            }
            catch (Exception e)
            {
                Warn(ctx + ": " + target.GetType().Name + "." + propertyName + "=" + memberName + " ataması başarısız — " + e.Message);
            }
        }
    }
}
