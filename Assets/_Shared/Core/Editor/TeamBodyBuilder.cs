using UnityEditor;
using UnityEngine;
using VortexArena.Core.Player;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Avatars &gt; Takım Gövdesini Kur</c> —
    /// <c>RemoteAvatar.prefab</c>'a KIRMIZI takımın ayrı gövdesini kurar: model örneği +
    /// <see cref="SkeletonPoseMirror"/> bağları + <c>RemoteAvatar.redBodyRoot</c>. İdempotenttir,
    /// tekrar çalıştırmak güvenlidir.
    /// <para>
    /// ⚠️ <b>Bu kurulum elle YAML'a yazılamaz:</b> gövde, modelin (FBX) prefab ÖRNEĞİdir ve
    /// örneğin gövdesi model içindeki fileID'lere atıf yapar — o kimlikler ancak import sonrası
    /// bellidir. Aracın varlık sebebi budur; "prefabı elle düzenleyeyim" yolu sessizce bozuk
    /// referans üretir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Takım gövdesi karakterin ALTINA değil KARDEŞİNE kurulur.</b> Retarget çıktısı dünya
    /// uzayındadır ve dolu bir ebeveyn dönüşümü ikinci kez uygulanır (aynı kural
    /// <c>ArenaNetCharacterBehaviour</c>'da). Kardeş olduğu için görünürlük kararını
    /// <see cref="RemoteAvatar"/> ayrıca bu köke de taşır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Mesh takası DEĞİL, poz köprüsü:</b> iki modelin iskelet ADLARI aynı olsa da ORANLARI
    /// farklı — aynı iskelete ikinci bir mesh bağlamak deforme bir gövde üretirdi. Köprü yalnız
    /// kemik DÖNÜŞLERİNİ kopyaladığı için hedef kendi oranlarında çizilir.
    /// </para>
    /// <para>
    /// Modeli değiştirmek = <see cref="TeamModelPath"/> sabitini değiştirip aracı tekrar
    /// çalıştırmak. Tek koşul modelin iskelet KEMİK ADLARININ karakterinkiyle eşleşmesidir (aynı
    /// Mixamo rig'i); humanoid Avatar gerekmez — <see cref="SkeletonPoseMirror"/> kas uzayına hiç
    /// girmez.
    /// </para>
    /// </summary>
    internal static class TeamBodyBuilder
    {
        private const string MenuPath = "Tools/VortexArena/Avatars/Takım Gövdesini Kur";

        private const string RemoteAvatarPath = "Assets/_Shared/App/Prefabs/RemoteAvatar.prefab";

        /// <summary>Kırmızı takımın gövde modeli.</summary>
        private const string TeamModelPath = "Assets/_Shared/Avatars/T-Avatars/Ch18_nonPBR.fbx";

        /// <summary>
        /// Kaynak pozu okunacak karakter modelinin FBX'i — kalçanın BIND pozu buradan okunur.
        /// <para>⚠️ Bu yol karakterin KENDİ modelidir; değişirse takım gövdesi yanlış bind
        /// referansıyla sürülür (gövde dikeyde kayar, hata basılmaz).</para>
        /// </summary>
        private const string CharacterModelPath = "Assets/ThirdPartyPackages/MixamoCharacters/Ch15_nonPBR.fbx";

        private const string TeamRootName = "RedTeamBody";
        private const string TeamBodyName = "Ch18_nonPBR";

        /// <summary>İki modelin de paylaştığı kalça kemiğinin adı (iskeletin kökü).</summary>
        private const string HipsBoneName = "mixamorig:Hips";

        [MenuItem(MenuPath, false, 61)]
        private static void BuildTeamBody()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(TeamModelPath);
            if (model == null)
            {
                Debug.LogError($"[TeamBody] Takım gövdesi modeli bulunamadı: {TeamModelPath}. " +
                               "Dosya yeni eklendiyse Unity'ye odaklan (import bitsin) ve tekrar dene.");
                return;
            }

            // ⚠️ Bind pozu FBX ASSET'inden okunur, prefabdan DEĞİL: prefabdaki iskelet bind
            // pozunda olmak zorunda değil (son uygulanan poz üstünde donmuş olabilir), FBX
            // asset'i ise her zaman bind pozundadır.
            if (!TryReadHipsBind(CharacterModelPath, out Vector3 sourceHipsBind) ||
                !TryReadHipsBind(TeamModelPath, out Vector3 targetHipsBind))
            {
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(RemoteAvatarPath);
            if (root == null)
            {
                Debug.LogError($"[TeamBody] Prefab açılamadı: {RemoteAvatarPath}");
                return;
            }

            try
            {
                if (!Configure(root, model, sourceHipsBind, targetHipsBind))
                {
                    return;
                }

                PrefabUtility.SaveAsPrefabAsset(root, RemoteAvatarPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[TeamBody] Kırmızı takım gövdesi kuruldu: {RemoteAvatarPath} → " +
                      $"{TeamRootName}/{TeamBodyName} ({System.IO.Path.GetFileName(TeamModelPath)}).");
        }

        /// <summary>
        /// Bir FBX asset'indeki kalça kemiğinin bind <c>localPosition</c>'ı. Bulunamazsa HATA
        /// basılır ve kurulum durur: yarım kurulmuş bir köprü (kalçasız) gövdeyi sessizce yanlış
        /// yükseklikte çizerdi.
        /// </summary>
        private static bool TryReadHipsBind(string modelPath, out Vector3 bind)
        {
            bind = Vector3.zero;

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (asset == null)
            {
                Debug.LogError($"[TeamBody] Model bulunamadı: {modelPath}");
                return false;
            }

            Transform hips = FindBone(asset.transform, HipsBoneName);
            if (hips == null)
            {
                Debug.LogError($"[TeamBody] '{HipsBoneName}' kemiği {modelPath} içinde yok — " +
                               "iki modelin aynı Mixamo iskeletini paylaşması gerekiyor.");
                return false;
            }

            bind = hips.localPosition;
            return true;
        }

        private static Transform FindBone(Transform root, string boneName)
        {
            Transform[] bones = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i].name == boneName)
                {
                    return bones[i];
                }
            }

            return null;
        }

        private static bool Configure(GameObject root, GameObject model, Vector3 sourceHipsBind,
                                      Vector3 targetHipsBind)
        {
            var remoteAvatar = root.GetComponent<RemoteAvatar>();
            if (remoteAvatar == null)
            {
                Debug.LogError("[TeamBody] Prefab kökünde RemoteAvatar bileşeni yok.");
                return false;
            }

            // Karakter, RemoteAvatar'ın KENDİ alanından okunur — hem tek doğruluk kaynağı olduğu
            // için hem de tipi adıyla anmamak için: bileşen Movement SDK arayüzü uyguluyor ve
            // adını yazmak bu editör asmdef'ine SDK referansı ekletirdi.
            var serialized = new SerializedObject(remoteAvatar);
            SerializedProperty characterProperty = serialized.FindProperty("character");
            var characterComponent = characterProperty != null
                ? characterProperty.objectReferenceValue as Component
                : null;

            if (characterComponent == null)
            {
                Debug.LogError("[TeamBody] RemoteAvatar.character bağlı değil — takım gövdesinin " +
                               "süreceği kaynak iskelet belirsiz.");
                return false;
            }

            Transform characterRoot = characterComponent.transform;

            Transform teamRoot = GetOrCreateTeamRoot(root.transform);
            Transform teamBody = GetOrCreateTeamBody(teamRoot, model);
            if (teamBody == null)
            {
                return false;
            }

            if (!PrepareRenderers(teamBody))
            {
                Debug.LogWarning($"[TeamBody] '{TeamBodyName}' altında Renderer yok — takım gövdesi " +
                                 "hiç çizilmez.");
            }

            if (!WireDriver(teamRoot, characterRoot, teamBody, sourceHipsBind, targetHipsBind))
            {
                return false;
            }

            // ⚠️ RemoteAvatar.redBodyRoot KAPSAYICIYA bağlanır, modele değil: bileşen renderer'ları
            // ve sürücüyü bu kökün ALTINDAN topluyor, sürücü de kapsayıcının üstünde.
            serialized.FindProperty("redBodyRoot").objectReferenceValue = teamRoot.gameObject;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return true;
        }

        private static Transform GetOrCreateTeamRoot(Transform parent)
        {
            Transform existing = parent.Find(TeamRootName);
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject(TeamRootName);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static Transform GetOrCreateTeamBody(Transform teamRoot, GameObject model)
        {
            Transform existing = teamRoot.Find(TeamBodyName);
            if (existing != null)
            {
                return existing;
            }

            // ⚠️ Model PREFAB ÖRNEĞİ olarak konur (unpack edilmez): modelde yapılan tek bir
            // düzeltme kopya konsaydı prefabda donardı.
            var instance = PrefabUtility.InstantiatePrefab(model, teamRoot) as GameObject;
            if (instance == null)
            {
                Debug.LogError("[TeamBody] Model örneklenemedi.");
                return null;
            }

            instance.name = TeamBodyName;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            // Animator KAPATILIR (sökülmez): köprü kemikleri doğrudan yazıyor, yani Animator'a
            // ihtiyaç yok ve kapalıyken her kare çalışan Animator
            // güncellemesi de ödenmez. ⚠️ Sökmek yerine kapatmanın sebebi prefab örneği olması:
            // bileşen sökmek "removed component" override'ı üretir ve model güncellenince
            // sessizce çakışır.
            var animator = instance.GetComponent<Animator>();
            if (animator != null)
            {
                animator.enabled = false;
            }

            return instance.transform;
        }

        /// <summary>
        /// Takım gövdesinin renderer'larını çizime hazırlar. Materyallere DOKUNULMAZ — gövde
        /// kendi modelinin materyalleriyle çizilir (hayalet durumunda materyal takasını
        /// <see cref="RemoteAvatar"/> yapar).
        /// </summary>
        private static bool PrepareRenderers(Transform teamBody)
        {
            Renderer[] renderers = teamBody.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] is SkinnedMeshRenderer smr)
                {
                    // Kök her kare kaynağın üstüne taşındığı için sınırlar bayatlar (karakterde ve
                    // hayalet gövdesinde de aynı anahtar açık) — kapalıyken gövde bazı açılarda
                    // kadraj dışı sayılıp hiç çizilmez.
                    smr.updateWhenOffscreen = true;
                }
            }

            return renderers.Length > 0;
        }

        /// <summary>
        /// Kemik aynasını (<see cref="SkeletonPoseMirror"/>) kurar ve alanlarını bağlar.
        /// Kalça bulunamazsa HATA basıp kurulumu DURDURUR: yarım kurulmuş bir köprü sahada
        /// teşhis edilmesi en pahalı şeydir (gövde çizilir ama yanlış yerde durur).
        /// </summary>
        private static bool WireDriver(Transform teamRoot, Transform characterRoot,
                                       Transform teamBody, Vector3 sourceHipsBind,
                                       Vector3 targetHipsBind)
        {
            Transform sourceHips = FindBone(characterRoot, HipsBoneName);
            Transform targetHips = FindBone(teamBody, HipsBoneName);
            if (sourceHips == null || targetHips == null)
            {
                Debug.LogError($"[TeamBody] '{HipsBoneName}' iki ağacın birinde bulunamadı " +
                               $"(kaynak: {(sourceHips != null ? "var" : "YOK")}, " +
                               $"hedef: {(targetHips != null ? "var" : "YOK")}) — kemik aynası " +
                               "kurulmadı, prefab kaydedilmiyor.");
                return false;
            }

            // ⚠️ Eski kas-uzayı köprüsü SÖKÜLÜR: kalsaydı kendi LateUpdate'inde AYNI kökü yazmaya
            // devam eder ve kemik aynasıyla yarışırdı (RemoteAvatar artık onu kapatmıyor, yani
            // kimse susturmuyor).
            var legacy = teamRoot.GetComponent<GhostPoseDriver>();
            if (legacy != null)
            {
                Object.DestroyImmediate(legacy);
            }

            var driver = teamRoot.GetComponent<SkeletonPoseMirror>();
            if (driver == null)
            {
                driver = teamRoot.gameObject.AddComponent<SkeletonPoseMirror>();
            }

            // Hedefin iskelet kolonu kaynağınkinden farklıysa gövde kendi boyunda çizilir; oranı
            // kalça yüksekliği taşır. ⚠️ Bölen geçersizse (0 / negatif) 1 yazılır: uydurma bir
            // çarpan gövdeyi sessizce yanlış ölçekte çizerdi.
            float heightCalibration = 1f;
            if (targetHipsBind.y > 0f && sourceHipsBind.y > 0f)
            {
                heightCalibration = sourceHipsBind.y / targetHipsBind.y;
            }
            else
            {
                Debug.LogWarning($"[TeamBody] Kalça bind yüksekliği geçersiz " +
                                 $"(kaynak {sourceHipsBind.y}, hedef {targetHipsBind.y}) — " +
                                 "heightCalibration 1 yazıldı, gövde kendi boyunda çizilecek.");
            }

            var serialized = new SerializedObject(driver);
            serialized.FindProperty("sourceRoot").objectReferenceValue = characterRoot;

            // Sürücünün hedefi MODEL KÖKÜdür (kapsayıcı değil): kemikler o kökün altında ve her
            // kare kaynağın dünya pozuna oturtulan da bu köktür.
            serialized.FindProperty("targetRoot").objectReferenceValue = teamBody;
            serialized.FindProperty("sourceHips").objectReferenceValue = sourceHips;
            serialized.FindProperty("targetHips").objectReferenceValue = targetHips;
            serialized.FindProperty("sourceHipsBind").vector3Value = sourceHipsBind;
            serialized.FindProperty("targetHipsBind").vector3Value = targetHipsBind;
            serialized.FindProperty("heightCalibration").floatValue = heightCalibration;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // Operatör aracın gerçekten iş yaptığını tek satırda görsün: eşleşme sayısı sıfıra
            // yakınsa iki model aynı iskeleti paylaşmıyor demektir.
            Debug.Log($"[TeamBody] Kemik aynası kuruldu: {CountMatchingBones(characterRoot, teamBody)} " +
                      $"kemik eşleşti, heightCalibration = {heightCalibration:F4}.");
            return true;
        }

        /// <summary>
        /// İki ağaçta ADI eşleşen kemik sayısı — <see cref="SkeletonPoseMirror"/>'ın çalışma anında
        /// uyguladığı kuralın aynısı (kökler hariç).
        /// </summary>
        private static int CountMatchingBones(Transform sourceRoot, Transform targetRoot)
        {
            Transform[] targetBones = targetRoot.GetComponentsInChildren<Transform>(true);
            var names = new System.Collections.Generic.HashSet<string>();
            for (int i = 0; i < targetBones.Length; i++)
            {
                if (targetBones[i] != targetRoot)
                {
                    names.Add(targetBones[i].name);
                }
            }

            int matched = 0;
            Transform[] sourceBones = sourceRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < sourceBones.Length; i++)
            {
                if (sourceBones[i] != sourceRoot && names.Contains(sourceBones[i].name))
                {
                    matched++;
                }
            }

            return matched;
        }
    }
}
