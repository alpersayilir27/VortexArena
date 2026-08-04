using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VortexArena.Core.Player;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// <c>Tools &gt; VortexArena &gt; Avatars &gt; Hayalet Gövdesini Kur</c> —
    /// <c>RemoteAvatar.prefab</c>'a ölü/kalibresiz durumda çizilen İKİNCİ gövdeyi (hayalet
    /// modeli) kurar: model örneği + hayalet materyali + <see cref="GhostPoseDriver"/> bağları +
    /// <c>RemoteAvatar.ghostRoot</c>. İdempotenttir, tekrar çalıştırmak güvenlidir.
    /// <para>
    /// ⚠️ <b>Bu kurulum elle YAML'a yazılamaz:</b> hayalet, modelin (FBX) prefab ÖRNEĞİdir ve
    /// örneğin gövdesi model içindeki fileID'lere atıf yapar — o kimlikler ancak import sonrası
    /// bellidir. Aracın varlık sebebi budur; "prefabı elle düzenleyeyim" yolu sessizce bozuk
    /// referans üretir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Hayalet, karakterin ALTINA değil KARDEŞİNE kurulur.</b> <c>RemoteAvatar.visualRoot</c>
    /// karakterin kendisidir ve görünürlük onu <c>SetActive(false)</c> ile kapatır; hayalet onun
    /// altında olsaydı sürücü de kapanır, hayalet son pozunda donardı. Kardeş olduğu için
    /// görünürlük kararını <c>RemoteAvatar</c> ayrıca hayalete de taşır.
    /// </para>
    /// <para>
    /// Modeli değiştirmek = <see cref="GhostModelPath"/> sabitini değiştirip aracı tekrar
    /// çalıştırmak. Tek koşul modelin <b>Rig = Humanoid</b> olmasıdır (retarget köprüsünün ön
    /// koşulu); iskelet adlarının karakterinkiyle eşleşmesi GEREKMEZ.
    /// </para>
    /// </summary>
    internal static class GhostBodyBuilder
    {
        private const string MenuPath = "Tools/VortexArena/Avatars/Hayalet Gövdesini Kur";

        private const string RemoteAvatarPath = "Assets/_Shared/App/Prefabs/RemoteAvatar.prefab";
        private const string GhostModelPath = "Assets/ThirdPartyPackages/StarterAssetsRobot/Armature.fbx";
        private const string GhostMaterialPath = "Assets/_Shared/Materials/M_AvatarGhost.mat";

        private const string GhostRootName = "Ghost";
        private const string GhostBodyName = "GhostBody";

        [MenuItem(MenuPath, false, 60)]
        private static void BuildGhostBody()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(GhostModelPath);
            if (model == null)
            {
                Debug.LogError($"[GhostBody] Hayalet modeli bulunamadı: {GhostModelPath}. " +
                               "Dosya yeni eklendiyse Unity'ye odaklan (import bitsin) ve tekrar dene.");
                return;
            }

            Avatar ghostAvatar = FindAvatar(GhostModelPath);
            if (ghostAvatar == null || !ghostAvatar.isHuman)
            {
                Debug.LogError($"[GhostBody] {GhostModelPath} humanoid Avatar üretmiyor — " +
                               "FBX importer'da Rig = Humanoid olmalı (Avatar Definition = " +
                               "Create From This Model).");
                return;
            }

            var ghostMaterial = AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath);
            if (ghostMaterial == null)
            {
                Debug.LogError($"[GhostBody] Hayalet materyali bulunamadı: {GhostMaterialPath}");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(RemoteAvatarPath);
            if (root == null)
            {
                Debug.LogError($"[GhostBody] Prefab açılamadı: {RemoteAvatarPath}");
                return;
            }

            try
            {
                if (!Configure(root, model, ghostAvatar, ghostMaterial))
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
            Debug.Log($"[GhostBody] Hayalet gövde kuruldu: {RemoteAvatarPath} → " +
                      $"{GhostRootName}/{GhostBodyName} ({System.IO.Path.GetFileName(GhostModelPath)}).");
        }

        private static bool Configure(GameObject root, GameObject model, Avatar ghostAvatar,
                                      Material ghostMaterial)
        {
            var remoteAvatar = root.GetComponent<RemoteAvatar>();
            if (remoteAvatar == null)
            {
                Debug.LogError("[GhostBody] Prefab kökünde RemoteAvatar bileşeni yok.");
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
                Debug.LogError("[GhostBody] RemoteAvatar.character bağlı değil — hayaletin süreceği " +
                               "kaynak iskelet belirsiz.");
                return false;
            }

            Transform characterRoot = characterComponent.transform;

            // Kaynak Avatar karakterin KENDİ Animator'ından okunur: ikinci bir doğruluk kaynağı
            // (sabit yol / elle bağ) karakter modeli değiştiğinde sessizce bayatlardı.
            // ⚠️ Animator EKLENMEZ, yalnız okunur — karaktere ikinci bir poz sürücüsü takmak
            // MSDK'nın yazdığı kemikleri ezerdi.
            var sourceAnimator = characterRoot.GetComponent<Animator>();
            Avatar sourceAvatar = sourceAnimator != null ? sourceAnimator.avatar : null;
            if (sourceAvatar == null || !sourceAvatar.isHuman)
            {
                Debug.LogError($"[GhostBody] Karakterin ('{characterRoot.name}') humanoid Avatar'ı " +
                               "yok — kaynak poz okunamaz. Karakter FBX'inde Rig = Humanoid olmalı.");
                return false;
            }

            Transform ghostRoot = GetOrCreateGhostRoot(root.transform);
            Transform ghostBody = GetOrCreateGhostBody(ghostRoot, model);
            if (ghostBody == null)
            {
                return false;
            }

            int painted = ApplyGhostMaterial(ghostBody, ghostMaterial);
            if (painted == 0)
            {
                Debug.LogWarning($"[GhostBody] '{GhostBodyName}' altında Renderer yok — hayalet " +
                                 "hiç çizilmez.");
            }

            WireDriver(ghostRoot, sourceAvatar, characterRoot, ghostAvatar, ghostBody);

            // ⚠️ RemoteAvatar.ghostRoot KAPSAYICIYA bağlanır, modele değil: bileşen renderer'ları
            // ve sürücüyü bu kökün ALTINDAN toplar, sürücü de kapsayıcının üstündedir.
            serialized.FindProperty("ghostRoot").objectReferenceValue = ghostRoot.gameObject;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return true;
        }

        private static Transform GetOrCreateGhostRoot(Transform parent)
        {
            Transform existing = parent.Find(GhostRootName);
            if (existing != null)
            {
                return existing;
            }

            var go = new GameObject(GhostRootName);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static Transform GetOrCreateGhostBody(Transform ghostRoot, GameObject model)
        {
            Transform existing = ghostRoot.Find(GhostBodyName);
            if (existing != null)
            {
                return existing;
            }

            // ⚠️ Model PREFAB ÖRNEĞİ olarak konur (unpack edilmez): modelde yapılan tek bir
            // düzeltme kopya konsaydı prefabda donardı.
            var instance = PrefabUtility.InstantiatePrefab(model, ghostRoot) as GameObject;
            if (instance == null)
            {
                Debug.LogError("[GhostBody] Model örneklenemedi.");
                return null;
            }

            instance.name = GhostBodyName;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            return instance.transform;
        }

        /// <summary>
        /// Hayalet modelin TÜM renderer'larını hayalet materyaline çevirir.
        /// <para>⚠️ Materyal dizisinin uzunluğu <b>submesh sayısına eşit</b> tutulur: fazlası
        /// SON submesh'i ikinci kez çizer (saydam gövdede bu, gözle "iki kat koyu" bir leke
        /// olarak görünür).</para>
        /// </summary>
        private static int ApplyGhostMaterial(Transform ghostBody, Material ghostMaterial)
        {
            Renderer[] renderers = ghostBody.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                int slots = renderer.sharedMaterials.Length;
                if (slots <= 0)
                {
                    var skinned = renderer as SkinnedMeshRenderer;
                    slots = skinned != null && skinned.sharedMesh != null
                        ? skinned.sharedMesh.subMeshCount
                        : 1;
                }

                var materials = new Material[Mathf.Max(1, slots)];
                for (int m = 0; m < materials.Length; m++)
                {
                    materials[m] = ghostMaterial;
                }

                renderer.sharedMaterials = materials;

                // Hayalet materyalinin gölge/derinlik geçişi yok; renderer'ı da boş yere
                // gölge kuyruğuna sokma.
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                if (renderer is SkinnedMeshRenderer smr)
                {
                    // Kök her kare kaynağın üstüne taşındığı için sınırlar bayatlar; karakterde
                    // de aynı anahtar açık.
                    smr.updateWhenOffscreen = true;
                }
            }

            return renderers.Length;
        }

        private static void WireDriver(Transform ghostRoot, Avatar sourceAvatar,
                                       Transform characterRoot, Avatar ghostAvatar,
                                       Transform ghostBody)
        {
            var driver = ghostRoot.GetComponent<GhostPoseDriver>();
            if (driver == null)
            {
                driver = ghostRoot.gameObject.AddComponent<GhostPoseDriver>();
            }

            var serialized = new SerializedObject(driver);
            serialized.FindProperty("sourceAvatar").objectReferenceValue = sourceAvatar;
            serialized.FindProperty("sourceRoot").objectReferenceValue = characterRoot;
            serialized.FindProperty("ghostAvatar").objectReferenceValue = ghostAvatar;

            // Sürücünün hedefi MODEL KÖKÜdür (kapsayıcı değil): HumanPose'un gövde konumu köke
            // göredir, iki humanoid kök üst üste oturmadan poz yanlış yere uygulanır.
            serialized.FindProperty("ghostRoot").objectReferenceValue = ghostBody;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Avatar FindAvatar(string modelPath)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(modelPath);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Avatar avatar)
                {
                    return avatar;
                }
            }

            return null;
        }
    }
}
