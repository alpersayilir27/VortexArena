using System;
using UnityEditor;
using UnityEngine;

namespace VortexArena.Core.Editor
{
    /// <summary>Joint list check for the skeleton stream (§6.9 <c>0x07</c>): sender and receiver
    /// must agree on the joint set.</summary>
    /// <remarks>
    /// The blob is the SDK's opaque native serialization: if the lists drift no error appears
    /// anywhere, remote bodies are simply drawn wrong. An innocent Inspector edit on either prefab
    /// comes back from the field as "everyone's body looks strange".
    /// <para>⚠️ This class WRITES NOTHING (<see cref="BuildReadiness"/> contract). Fixing a drift is
    /// a human step: which list is correct is a decision, not a preference — the finger-trimming
    /// list is §6.9 itself, and "copy one onto the other" could spread the wrong one.</para>
    /// <para>⚠️ The list is not computed at RUNTIME and must not be: it is DATA serialized in two
    /// prefabs, not a calculation. Rebuilding it by scanning names/hierarchy would open a second
    /// author for the list and produce exactly the drift this check prevents.</para>
    /// <para>⚠️ The fields are SDK <b>private</b> fields, so they are read via
    /// <see cref="SerializedObject"/> and the component is found by NAME, not by type: referencing
    /// the Movement SDK assembly from <c>VortexArena.Core.Editor</c> just for this check would tie
    /// the editor assembly to the package. If a field/component name drifts the row reports ✗ with
    /// its reason instead of silently claiming "clean".</para>
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

        /// <summary>Whether all FOUR lists (<c>sync</c> + <c>send</c> per prefab) are identical.
        /// </summary>
        /// <remarks>⚠️ An empty list is ✗ too: the SDK fills an empty array with "all joints" at
        /// runtime, so the emptied prefab starts sending fingers — the 40 joints §6.9 trims come
        /// back silently and approach the blob ceiling (<c>SKELETON_MAX_BLOB_BYTES</c>).</remarks>
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
                         "ve parmaklar tele geri girer (§6.9)";
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

            detail = $"{localSync.Length} eklem, dört liste de aynı";
            return true;
        }

        /// <summary>Reads both lists from a prefab's retargeter; reports why on failure.</summary>
        private static bool TryReadLists(string prefabPath, out int[] sync, out int[] send, out string detail)
        {
            sync = Array.Empty<int>();
            send = Array.Empty<int>();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                detail = $"prefab bulunamadı: {prefabPath}";
                return false;
            }

            Component retargeter = FindByTypeName(prefab, RetargeterTypeName);
            if (retargeter == null)
            {
                detail = $"'{RetargeterTypeName}' yok: {prefabPath}";
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

        /// <summary>⚠️ ORDER is compared too, not just content: the blob carries joints in list
        /// order.</summary>
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
