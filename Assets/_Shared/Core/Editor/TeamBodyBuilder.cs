using UnityEditor;
using UnityEngine;
using VortexArena.Core.Player;

namespace VortexArena.Core.Editor
{
    /// <summary>Builds the RED team's separate body into <c>RemoteAvatar.prefab</c>: model instance
    /// + <see cref="SkeletonPoseMirror"/> wiring + <c>RemoteAvatar.redBodyRoot</c>. Idempotent.</summary>
    /// <remarks>
    /// ⚠️ This setup cannot be hand written in YAML: the body is a prefab INSTANCE of the FBX and
    /// its overrides refer to fileIDs inside the model, which only exist after import. That is the
    /// tool's reason to exist; editing the prefab by hand silently produces broken references.
    /// <para>⚠️ The team body is a SIBLING of the character, not a child: retarget output is in
    /// world space and a non-identity parent transform would be applied twice (same rule as in
    /// <c>ArenaNetCharacterBehaviour</c>). Being a sibling, <see cref="RemoteAvatar"/> also carries
    /// the visibility decision to this root.</para>
    /// <para>⚠️ A pose bridge, NOT a mesh swap: the two skeletons share bone NAMES but not
    /// PROPORTIONS, so binding a second mesh to the same skeleton would deform it. The bridge copies
    /// bone ROTATIONS only, so the target is drawn in its own proportions.</para>
    /// <para>Changing the model = change the <see cref="TeamModelPath"/> constant and run again. The
    /// only requirement is matching BONE NAMES with the character (same Mixamo rig); no humanoid
    /// Avatar is needed — <see cref="SkeletonPoseMirror"/> never enters muscle space.</para>
    /// </remarks>
    internal static class TeamBodyBuilder
    {
        private const string MenuPath = "Tools/VortexArena/Avatars/Takım Gövdesini Kur";

        private const string RemoteAvatarPath = "Assets/_Shared/App/Prefabs/RemoteAvatar.prefab";

        /// <summary>Red team's body model.</summary>
        private const string TeamModelPath = "Assets/_Shared/Avatars/T-Avatars/Ch18_nonPBR.fbx";

        /// <summary>FBX of the character the pose is read from — the hips BIND pose comes from
        /// here.</summary>
        /// <remarks>⚠️ This is the character's OWN model; if it changes, the team body is driven
        /// with the wrong bind reference (body drifts vertically, no error logged).</remarks>
        private const string CharacterModelPath = "Assets/ThirdPartyPackages/MixamoCharacters/Ch15_nonPBR.fbx";

        private const string TeamRootName = "RedTeamBody";
        private const string TeamBodyName = "Ch18_nonPBR";

        /// <summary>Hips bone name shared by both models (root of the skeleton).</summary>
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

            // ⚠️ The bind pose is read from the FBX ASSET, not the prefab: a prefab skeleton may be
            // frozen on the last applied pose, while the FBX asset is always in bind pose.
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

        /// <summary>Bind <c>localPosition</c> of the hips bone in an FBX asset.</summary>
        /// <remarks>Missing hips logs an ERROR and stops the setup: a half built bridge would
        /// silently draw the body at the wrong height.</remarks>
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

            // The character is read from RemoteAvatar's own field: it is the single source of truth,
            // and naming the type would pull an SDK reference into this editor asmdef.
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

            // ⚠️ redBodyRoot points at the CONTAINER, not the model: the component collects
            // renderers and the driver from under this root, and the driver sits on the container.
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

            // ⚠️ The model goes in as a PREFAB INSTANCE (never unpacked): as a copy, any fix made to
            // the model would stay frozen in the prefab.
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

            // The Animator is DISABLED, not removed: the bridge writes bones directly, so it is
            // unneeded and its per frame update is not paid. ⚠️ Removing it would create a "removed
            // component" override on the prefab instance and clash silently on a model update.
            var animator = instance.GetComponent<Animator>();
            if (animator != null)
            {
                animator.enabled = false;
            }

            return instance.transform;
        }

        /// <summary>Prepares the team body's renderers for drawing.</summary>
        /// <remarks>Materials are left alone — the body draws with its own model's materials; the
        /// ghost state material swap is <see cref="RemoteAvatar"/>'s job.</remarks>
        private static bool PrepareRenderers(Transform teamBody)
        {
            Renderer[] renderers = teamBody.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] is SkinnedMeshRenderer smr)
                {
                    // The root is moved onto the source every frame, so bounds go stale (same flag
                    // is on for the character and the ghost body); off, the body is culled as
                    // offscreen at some angles and never drawn.
                    smr.updateWhenOffscreen = true;
                }
            }

            return renderers.Length > 0;
        }

        /// <summary>Creates the bone mirror (<see cref="SkeletonPoseMirror"/>) and wires its
        /// fields.</summary>
        /// <remarks>Missing hips logs an ERROR and STOPS the setup: a half built bridge is the most
        /// expensive thing to diagnose on site (the body draws, but in the wrong place).</remarks>
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

            var driver = teamRoot.GetComponent<SkeletonPoseMirror>();
            if (driver == null)
            {
                driver = teamRoot.gameObject.AddComponent<SkeletonPoseMirror>();
            }

            // Hips height carries the proportion between the two skeletons. ⚠️ An invalid divisor
            // (0 / negative) writes 1: a made up factor would silently draw the body at the wrong
            // scale.
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

            // The driver's target is the MODEL ROOT, not the container: the bones live under it and
            // it is what gets placed on the source's world pose every frame.
            serialized.FindProperty("targetRoot").objectReferenceValue = teamBody;
            serialized.FindProperty("sourceHips").objectReferenceValue = sourceHips;
            serialized.FindProperty("targetHips").objectReferenceValue = targetHips;
            serialized.FindProperty("sourceHipsBind").vector3Value = sourceHipsBind;
            serialized.FindProperty("targetHipsBind").vector3Value = targetHipsBind;
            serialized.FindProperty("heightCalibration").floatValue = heightCalibration;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // One line proof the tool did something: a match count near zero means the two models do
            // not share the same skeleton.
            Debug.Log($"[TeamBody] Kemik aynası kuruldu: {CountMatchingBones(characterRoot, teamBody)} " +
                      $"kemik eşleşti, heightCalibration = {heightCalibration:F4}.");
            return true;
        }

        /// <summary>Bones matching by NAME across the two trees — same rule
        /// <see cref="SkeletonPoseMirror"/> applies at runtime (roots excluded).</summary>
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
