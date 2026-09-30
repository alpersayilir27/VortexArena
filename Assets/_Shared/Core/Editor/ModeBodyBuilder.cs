using UnityEditor;
using UnityEngine;
using VortexArena.Core.Player;

namespace VortexArena.Core.Editor
{
    /// <summary>Builds a MODE body prefab out of the selected Mixamo-rigged FBX: model instance +
    /// <see cref="SkeletonPoseMirror"/> wiring. Idempotent — re-running only rewrites the mirror
    /// fields.</summary>
    /// <remarks>
    /// ⚠️ This setup cannot be hand written in YAML: the body is a prefab INSTANCE of the FBX and its
    /// overrides refer to fileIDs inside the model, which only exist after import.
    /// <para>The result is assigned to <c>ModeDefinition.bodyPrefab</c> / <c>redBodyPrefab</c>;
    /// <c>RemoteAvatar</c> instantiates it at runtime as a SIBLING of the character and binds the
    /// mirror's source itself (<see cref="SkeletonPoseMirror.BindSource"/>) — which is why
    /// <c>sourceRoot</c>/<c>sourceHips</c> are deliberately left empty here.</para>
    /// <para>⚠️ Hit boxes (<c>RemoteHitBox</c> + collider, zone picked by hand) are NOT created by this
    /// tool; they are added to the prefab by hand and survive a re-run.</para>
    /// <para>The only requirement is matching BONE NAMES with the character (same Mixamo rig); no
    /// humanoid Avatar is needed — <see cref="SkeletonPoseMirror"/> never enters muscle space.</para>
    /// </remarks>
    internal static class ModeBodyBuilder
    {
        private const string MenuPath = "Tools/VortexArena/Avatars/Mod Gövdesi Kur (seçili FBX)";

        /// <summary>Suffix of the produced prefab, next to the source FBX.</summary>
        private const string BodySuffix = "_Body";

        /// <summary>Hips bone name shared by both models (root of the skeleton).</summary>
        private const string HipsBoneName = "mixamorig:Hips";

        /// <summary>Head joint — lower end of the calibration reference (rationale in <see cref="Build"/>).</summary>
        private const string HeadBoneName = "mixamorig:Head";

        /// <summary>Top of the head — upper end of the calibration reference.</summary>
        private const string HeadTopBoneName = "mixamorig:HeadTop_End";

        [MenuItem(MenuPath, true, 62)]
        private static bool ValidateBuild()
        {
            return GetSelectedModelPath() != null;
        }

        [MenuItem(MenuPath, false, 62)]
        private static void Build()
        {
            string modelPath = GetSelectedModelPath();
            if (modelPath == null)
            {
                Debug.LogError("[ModeBody] Önce Project penceresinde bir model (FBX) seç.");
                return;
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            var character = AssetDatabase.LoadAssetAtPath<GameObject>(TeamBodyBuilder.CharacterModelPath);
            if (model == null || character == null)
            {
                Debug.LogError($"[ModeBody] Model açılamadı (seçili: {modelPath}, karakter: " +
                               $"{TeamBodyBuilder.CharacterModelPath}). Dosya yeni eklendiyse " +
                               "Unity'ye odaklan (import bitsin) ve tekrar dene.");
                return;
            }

            // ⚠️ Bind pose and heights are read from the FBX ASSETS, not from a prefab: a prefab skeleton
            // may be frozen on the last applied pose, while the FBX asset is always in bind pose.
            if (!TryReadBone(character, HipsBoneName, out Transform sourceHips) ||
                !TryReadBone(model, HipsBoneName, out Transform targetHips) ||
                !TryReadBone(character, HeadBoneName, out Transform sourceHead) ||
                !TryReadBone(model, HeadBoneName, out Transform targetHead) ||
                !TryReadBone(character, HeadTopBoneName, out Transform sourceHeadTop) ||
                !TryReadBone(model, HeadTopBoneName, out Transform targetHeadTop))
            {
                return;
            }

            float sourceHeadY = HeadCentreY(character.transform, sourceHead, sourceHeadTop);
            float targetHeadY = HeadCentreY(model.transform, targetHead, targetHeadTop);

            // ⚠️ Calibrated on the HEAD CENTRE (Head↔HeadTop_End midpoint ≈ eye height), not on the hips
            // like Ch18: a toon body's short legs would push its head far above the player's real head.
            // Not the head top alone either — the auto-rigger puts HeadTop_End on top of tall hair.
            float heightCalibration = 1f;
            if (sourceHeadY > 0f && targetHeadY > 0f)
            {
                heightCalibration = sourceHeadY / targetHeadY;
            }
            else
            {
                Debug.LogWarning($"[ModeBody] Kafa ortası yüksekliği geçersiz (kaynak {sourceHeadY}, " +
                                 $"hedef {targetHeadY}) — heightCalibration 1 yazıldı, gövde kendi " +
                                 "boyunda çizilecek.");
            }

            string bodyName = System.IO.Path.GetFileNameWithoutExtension(modelPath) + BodySuffix;
            string outputPath = System.IO.Path.GetDirectoryName(modelPath).Replace('\\', '/') +
                                "/" + bodyName + ".prefab";

            bool saved = AssetDatabase.LoadAssetAtPath<GameObject>(outputPath) != null
                ? UpdateExisting(outputPath, model, sourceHips.localPosition, targetHips.localPosition,
                    heightCalibration)
                : CreateNew(outputPath, bodyName, model, sourceHips.localPosition,
                    targetHips.localPosition, heightCalibration);

            if (!saved)
            {
                return;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[ModeBody] Mod gövdesi kuruldu: {outputPath} · heightCalibration = " +
                      $"{heightCalibration:F4} (kafa ortası: karakter {sourceHeadY:F3} m, gövde " +
                      $"{targetHeadY:F3} m). Vuruş kutuları (RemoteHitBox, bölge elle seçilir) bu " +
                      "prefaba ELLE eklenir; araç tekrar çalıştırılınca korunur.");
        }

        /// <summary>Head centre height in the model root's space (bind pose).</summary>
        private static float HeadCentreY(Transform root, Transform head, Transform headTop)
        {
            return (root.InverseTransformPoint(head.position).y +
                    root.InverseTransformPoint(headTop.position).y) * 0.5f;
        }

        /// <summary>Selected asset's path when it is a model (FBX); otherwise null.</summary>
        private static string GetSelectedModelPath()
        {
            var selected = Selection.activeObject as GameObject;
            if (selected == null)
            {
                return null;
            }

            string path = AssetDatabase.GetAssetPath(selected);
            return !string.IsNullOrEmpty(path) && AssetImporter.GetAtPath(path) is ModelImporter
                ? path
                : null;
        }

        /// <summary>Finds a bone in an FBX asset.</summary>
        /// <remarks>A missing bone logs an ERROR and stops the setup: a half built bridge would
        /// silently draw the body at the wrong height.</remarks>
        private static bool TryReadBone(GameObject asset, string boneName, out Transform bone)
        {
            bone = FindBone(asset.transform, boneName);
            if (bone != null)
            {
                return true;
            }

            Debug.LogError($"[ModeBody] '{boneName}' kemiği {AssetDatabase.GetAssetPath(asset)} " +
                           "içinde yok — iki modelin aynı Mixamo iskeletini paylaşması gerekiyor. " +
                           "Prefab yazılmadı.");
            return false;
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

        private static bool CreateNew(string outputPath, string bodyName, GameObject model,
                                      Vector3 sourceHipsBind, Vector3 targetHipsBind,
                                      float heightCalibration)
        {
            var root = new GameObject(bodyName);
            try
            {
                // ⚠️ The model goes in as a PREFAB INSTANCE (never unpacked): as a copy, any fix made to
                // the model would stay frozen in this prefab.
                var instance = PrefabUtility.InstantiatePrefab(model, root.transform) as GameObject;
                if (instance == null)
                {
                    Debug.LogError("[ModeBody] Model örneklenemedi.");
                    return false;
                }

                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;

                // The Animator is DISABLED, not removed: the bridge writes bones directly. ⚠️ Removing it
                // would create a "removed component" override and clash silently on a model update.
                var animator = instance.GetComponent<Animator>();
                if (animator != null)
                {
                    animator.enabled = false;
                }

                PrepareRenderers(instance.transform);

                if (!WireDriver(root, instance.transform, sourceHipsBind, targetHipsBind, heightCalibration))
                {
                    return false;
                }

                PrefabUtility.SaveAsPrefabAsset(root, outputPath);
                return true;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Re-runs on an existing prefab: ONLY the mirror fields are rewritten, so hand placed
        /// hit boxes and material overrides survive.</summary>
        private static bool UpdateExisting(string outputPath, GameObject model, Vector3 sourceHipsBind,
                                           Vector3 targetHipsBind, float heightCalibration)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(outputPath);
            if (root == null)
            {
                Debug.LogError($"[ModeBody] Prefab açılamadı: {outputPath}");
                return false;
            }

            try
            {
                Transform instance = FindModelInstance(root, model);
                if (instance == null)
                {
                    Debug.LogError($"[ModeBody] {outputPath} içinde {model.name} örneği yok — bu prefab " +
                                   "bu FBX'ten kurulmamış. Prefabı sil ya da başka ad ver.");
                    return false;
                }

                if (!WireDriver(root, instance, sourceHipsBind, targetHipsBind, heightCalibration))
                {
                    return false;
                }

                PrefabUtility.SaveAsPrefabAsset(root, outputPath);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>The prefab instance under <paramref name="root"/> whose source is
        /// <paramref name="model"/>.</summary>
        private static Transform FindModelInstance(GameObject root, GameObject model)
        {
            string modelPath = AssetDatabase.GetAssetPath(model);
            Transform[] all = root.GetComponentsInChildren<Transform>(true);

            for (int i = 0; i < all.Length; i++)
            {
                GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(all[i].gameObject);
                if (source != null && AssetDatabase.GetAssetPath(source) == modelPath &&
                    PrefabUtility.GetNearestPrefabInstanceRoot(all[i].gameObject) == all[i].gameObject)
                {
                    return all[i];
                }
            }

            return null;
        }

        /// <summary>Prepares the body's renderers for drawing.</summary>
        /// <remarks>Materials are left alone — the body draws with its own model's materials; the ghost
        /// state material swap is <c>RemoteAvatar</c>'s job.</remarks>
        private static void PrepareRenderers(Transform body)
        {
            Renderer[] renderers = body.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] is SkinnedMeshRenderer smr)
                {
                    // The root is moved onto the source every frame, so bounds go stale; off, the body
                    // is culled as offscreen at some angles and never drawn.
                    smr.updateWhenOffscreen = true;
                }
            }

            if (renderers.Length == 0)
            {
                Debug.LogWarning($"[ModeBody] '{body.name}' altında Renderer yok — gövde hiç çizilmez.");
            }
        }

        /// <summary>Writes the mirror's fields. <c>sourceRoot</c>/<c>sourceHips</c> stay EMPTY on
        /// purpose: <c>RemoteAvatar</c> binds the live skeleton at runtime.</summary>
        /// <remarks>Missing hips logs an ERROR and STOPS the setup: a half built bridge is the most
        /// expensive thing to diagnose on site (the body draws, but in the wrong place).</remarks>
        private static bool WireDriver(GameObject root, Transform body, Vector3 sourceHipsBind,
                                       Vector3 targetHipsBind, float heightCalibration)
        {
            Transform targetHips = FindBone(body, HipsBoneName);
            if (targetHips == null)
            {
                Debug.LogError($"[ModeBody] '{HipsBoneName}' gövde ağacında bulunamadı — kemik aynası " +
                               "kurulmadı, prefab kaydedilmiyor.");
                return false;
            }

            var driver = root.GetComponent<SkeletonPoseMirror>();
            if (driver == null)
            {
                driver = root.AddComponent<SkeletonPoseMirror>();
            }

            var serialized = new SerializedObject(driver);
            serialized.FindProperty("sourceRoot").objectReferenceValue = null;
            serialized.FindProperty("sourceHips").objectReferenceValue = null;

            // The driver's target is the MODEL ROOT, not the prefab root: the bones live under it and it
            // is what gets placed on the source's world pose every frame.
            serialized.FindProperty("targetRoot").objectReferenceValue = body;
            serialized.FindProperty("targetHips").objectReferenceValue = targetHips;
            serialized.FindProperty("sourceHipsBind").vector3Value = sourceHipsBind;
            serialized.FindProperty("targetHipsBind").vector3Value = targetHipsBind;
            serialized.FindProperty("heightCalibration").floatValue = heightCalibration;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return true;
        }
    }
}
