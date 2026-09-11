using System;
using UnityEditor;
using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.Core.Editor
{
    /// <summary>Joint checks for the skeleton stream (§6.9 <c>0x07</c>): sender and receiver must agree on the joints.</summary>
    /// <remarks>
    /// The blob carries joints by TARGET INDEX (<see cref="SkeletonWire.JOINT_INDICES"/>): if the two
    /// prefabs resolve an index to different bones no error appears anywhere, remote bodies are simply
    /// drawn wrong. An innocent Inspector edit on either prefab comes back as "everyone's body looks strange".
    /// <para>⚠️ This class WRITES NOTHING (<see cref="BuildReadiness"/> contract). Fixing a drift is a human
    /// step: which side is correct is a decision, and "copy one onto the other" could spread the wrong one.</para>
    /// <para>⚠️ The fields are SDK <b>private</b> fields, so they are read via <see cref="SerializedObject"/>
    /// and the component is found by NAME, not by type: referencing the Movement SDK assembly from
    /// <c>VortexArena.Core.Editor</c> just for this check would tie the editor assembly to the package. If a
    /// field/component name drifts the row reports ✗ with its reason instead of silently claiming "clean".</para>
    /// </remarks>
    internal static class SkeletonStreamGuard
    {
        /// <summary>Sender body (network source only, never drawn).</summary>
        private const string LocalBodyPrefabPath =
            "Assets/_Shared/Avatars/Resources/LocalBodyAvatar.prefab";

        /// <summary>Receiver body (remote players + admin spectator).</summary>
        private const string RemoteAvatarPrefabPath = "Assets/_Shared/App/Prefabs/RemoteAvatar.prefab";

        private const string RetargeterTypeName = "NetworkCharacterRetargeter";
        private const string SyncFieldName = "_bodyIndicesToSync";
        private const string SendFieldName = "_bodyIndicesToSend";
        private const string JointPairsFieldName = "_jointPairs";
        private const string JointFieldName = "Joint";
        private const string HideUntilValidFieldName = "_objectsToHideUntilValid";
        private const string HipsNameSuffix = "Hips";

        /// <summary>Whether the SDK index lists match AND both prefabs resolve the wire joints to the same bones.</summary>
        /// <remarks>
        /// ⚠️ An empty SDK list is ✗ too: the SDK fills an empty array with "all joints" at runtime, which
        /// brings back the per-frame cost of the trimmed finger joints.
        /// <para>⚠️ A filled <c>_objectsToHideUntilValid</c> is ✗: only the SDK's <c>ReceiveData</c> shows those
        /// objects, and that path is no longer called — the remote body would never appear.</para>
        /// </remarks>
        internal static bool AreJointListsMatched(out string detail)
        {
            if (!TryReadLists(LocalBodyPrefabPath, out int[] localSync, out int[] localSend, out detail) ||
                !TryReadLists(RemoteAvatarPrefabPath, out int[] remoteSync, out int[] remoteSend, out detail))
            {
                return false;
            }

            if (localSync.Length == 0 || localSend.Length == 0 ||
                remoteSync.Length == 0 || remoteSend.Length == 0)
            {
                detail = "listelerden biri BOŞ — SDK onu çalışma anında 'tüm eklemler' diye doldurur " +
                         "ve parmaklar geri girer (§6.9)";
                return false;
            }

            if (!Same(localSync, localSend))
            {
                detail = $"gönderen prefabta sync ({localSync.Length}) ile send ({localSend.Length}) " +
                         "listeleri farklı";
                return false;
            }

            if (!Same(remoteSync, remoteSend))
            {
                detail = $"alıcı prefabta sync ({remoteSync.Length}) ile send ({remoteSend.Length}) " +
                         "listeleri farklı";
                return false;
            }

            if (!Same(localSync, remoteSync))
            {
                detail = $"gönderen ({localSync.Length} eklem) ile alıcı ({remoteSync.Length} eklem) " +
                         "listeleri farklı";
                return false;
            }

            if (!TryReadWireJoints(LocalBodyPrefabPath, out string[] localNames, out detail) ||
                !TryReadWireJoints(RemoteAvatarPrefabPath, out string[] remoteNames, out detail))
            {
                return false;
            }

            for (int i = 0; i < localNames.Length; i++)
            {
                if (localNames[i] != remoteNames[i])
                {
                    detail = $"tel eklemi {SkeletonWire.JOINT_INDICES[i]} iki prefabda farklı kemiğe çözülüyor: " +
                             $"gönderen '{localNames[i]}', alıcı '{remoteNames[i]}'";
                    return false;
                }
            }

            detail = $"{localSync.Length} eklem, dört liste de aynı · tel listesi {SkeletonWire.JointCount} " +
                     $"eklem ({SkeletonWire.BLOB_BYTES} B), iki prefabda aynı kemikler";
            return true;
        }

        /// <summary>Reads both SDK index lists from a prefab's retargeter; reports why on failure.</summary>
        private static bool TryReadLists(string prefabPath, out int[] sync, out int[] send, out string detail)
        {
            sync = Array.Empty<int>();
            send = Array.Empty<int>();

            if (!TryFindRetargeter(prefabPath, out Component retargeter, out detail))
            {
                return false;
            }

            var serialized = new SerializedObject(retargeter);
            if (!TryReadIndexArray(serialized, SyncFieldName, out sync) ||
                !TryReadIndexArray(serialized, SendFieldName, out send))
            {
                detail = $"'{SyncFieldName}'/'{SendFieldName}' okunamadı ({prefabPath}) — SDK alan adı " +
                         "değişmiş olabilir";
                return false;
            }

            detail = string.Empty;
            return true;
        }

        /// <summary>Bone names of <see cref="SkeletonWire.JOINT_INDICES"/> in wire order, plus the root/hips/hide rules.</summary>
        private static bool TryReadWireJoints(string prefabPath, out string[] names, out string detail)
        {
            names = Array.Empty<string>();

            if (!TryFindRetargeter(prefabPath, out Component retargeter, out detail))
            {
                return false;
            }

            var serialized = new SerializedObject(retargeter);
            SerializedProperty pairs = serialized.FindProperty(JointPairsFieldName);
            SerializedProperty hide = serialized.FindProperty(HideUntilValidFieldName);
            if (pairs == null || !pairs.isArray || hide == null || !hide.isArray)
            {
                detail = $"'{JointPairsFieldName}'/'{HideUntilValidFieldName}' okunamadı ({prefabPath}) — SDK " +
                         "alan adı değişmiş olabilir";
                return false;
            }

            if (hide.arraySize > 0)
            {
                detail = $"'{HideUntilValidFieldName}' dolu ({hide.arraySize} obje, {prefabPath}) — onları açan " +
                         "SDK ReceiveData yolu çağrılmıyor, uzak gövde hiç görünmez";
                return false;
            }

            names = new string[SkeletonWire.JointCount];
            for (int i = 0; i < names.Length; i++)
            {
                int index = SkeletonWire.JOINT_INDICES[i];
                if (!TryReadJoint(pairs, index, prefabPath, out Transform joint, out detail))
                {
                    return false;
                }

                if (index == 0 && joint != retargeter.transform)
                {
                    detail = $"'{JointPairsFieldName}[0]' retargeter'ın kendi objesi değil ('{joint.name}', " +
                             $"{prefabPath}) — alıcı 0. eklemi kök sayıp yazmaz";
                    return false;
                }

                names[i] = joint.name;
            }

            if (!TryReadJoint(pairs, SkeletonWire.HIPS_INDEX, prefabPath, out Transform hips, out detail))
            {
                return false;
            }

            if (!hips.name.EndsWith(HipsNameSuffix, StringComparison.Ordinal))
            {
                detail = $"kalça indeksi {SkeletonWire.HIPS_INDEX} kalça kemiği değil ('{hips.name}', {prefabPath}) " +
                         "— kalça konumu yanlış kemiğe yazılır";
                return false;
            }

            detail = string.Empty;
            return true;
        }

        private static bool TryReadJoint(
            SerializedProperty pairs, int index, string prefabPath, out Transform joint, out string detail)
        {
            joint = null;

            if (index >= pairs.arraySize)
            {
                detail = $"'{JointPairsFieldName}' {pairs.arraySize} eleman, tel eklemi {index} yok ({prefabPath})";
                return false;
            }

            SerializedProperty jointProperty =
                pairs.GetArrayElementAtIndex(index).FindPropertyRelative(JointFieldName);
            joint = jointProperty != null ? jointProperty.objectReferenceValue as Transform : null;
            if (joint == null)
            {
                detail = $"'{JointPairsFieldName}[{index}].{JointFieldName}' boş ({prefabPath})";
                return false;
            }

            detail = string.Empty;
            return true;
        }

        private static bool TryFindRetargeter(string prefabPath, out Component retargeter, out string detail)
        {
            retargeter = null;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                detail = $"prefab bulunamadı: {prefabPath}";
                return false;
            }

            retargeter = FindByTypeName(prefab, RetargeterTypeName);
            if (retargeter == null)
            {
                detail = $"'{RetargeterTypeName}' yok: {prefabPath}";
                return false;
            }

            detail = string.Empty;
            return true;
        }

        /// <summary>⚠️ Scans INACTIVE children too: the retargeter may sit under a disabled
        /// node.</summary>
        private static Component FindByTypeName(GameObject prefab, string typeName)
        {
            Component[] components = prefab.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] != null && components[i].GetType().Name == typeName)
                {
                    return components[i];
                }
            }

            return null;
        }

        private static bool TryReadIndexArray(SerializedObject serialized, string fieldName, out int[] values)
        {
            values = Array.Empty<int>();

            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null || !property.isArray)
            {
                return false;
            }

            values = new int[property.arraySize];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = property.GetArrayElementAtIndex(i).intValue;
            }

            return true;
        }

        /// <summary>ORDER is compared too, not just content.</summary>
        private static bool Same(int[] a, int[] b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
