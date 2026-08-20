using System;
using UnityEditor;
using UnityEngine;

namespace VortexArena.Core.Editor
{
    /// <summary>
    /// İskelet akışının (§6.9 <c>0x07</c>) <b>eklem listesi denetimi</b>: gönderen ile alıcının
    /// gönderdiği/beklediği eklem kümesi aynı mı.
    /// <para>
    /// <b>Neden bir denetim gerekiyor:</b> blob SDK'nın native serileştirmesidir ve <b>opaktır</b> —
    /// listeler ayrışırsa hiçbir yerde hata çıkmaz, yalnız uzak gövdeler bozuk çizilir. Yani bu,
    /// sürüm uyumsuzluğunun sessiz biçimidir ve tek koruması bugüne kadar dokümandaki bir cümleydi.
    /// Kural makineye devredilmezse iki prefabtan birinin Inspector'ında yapılan masum bir düzenleme
    /// sahada "herkesin gövdesi garip" olarak geri döner.
    /// </para>
    /// <para>
    /// ⚠️ <b>Bu sınıf HİÇBİR ŞEY YAZMAZ</b> (<see cref="BuildReadiness"/> sözleşmesi). Ayrışmayı
    /// düzeltmek insan adımıdır: hangi listenin doğru olduğu bir tercih değil bir KARAR — parmakları
    /// kesen liste §6.9'un kendisidir, aracın "birini diğerine kopyalarım" demesi yanlış olanı
    /// yaymak olabilirdi.
    /// </para>
    /// <para>
    /// ⚠️ <b>Liste RUNTIME'da hesaplanmaz ve hesaplanmamalı</b>: bu bir hesap değil, iki prefabta
    /// serialize edilmiş bir VERİdir — çalışma anında ad/hiyerarşi tarayıp yeniden üretmek, listeyi
    /// yazan ikinci bir taraf açar ve tam da bu denetimin engellediği ayrışmayı üretir.
    /// </para>
    /// <para>
    /// ⚠️ Alanlar SDK'nın <b>private</b> alanları olduğu için <see cref="SerializedObject"/> ile
    /// okunur; bileşen de tipiyle değil ADIYLA bulunur. Sebep bilinçli: bu denetim uğruna
    /// <c>VortexArena.Core.Editor</c>'a Movement SDK derleme referansı eklemek, editör derlemesini
    /// bir denetim için pakete bağlamak olurdu. Alan/bileşen adı sapıp bulunamazsa satır ✗ olur ve
    /// gerekçesini yazar — sessizce "temiz" demez.
    /// </para>
    /// </summary>
    internal static class SkeletonStreamGuard
    {
        /// <summary>Gönderen gövde (yalnız ağ kaynağı, hiç çizilmez).</summary>
        private const string LocalBodyPrefabPath =
            "Assets/_Shared/Avatars/Resources/LocalBodyAvatar.prefab";

        /// <summary>Alıcı gövde (uzak oyuncular + admin gözlemcisi).</summary>
        private const string RemoteAvatarPrefabPath = "Assets/_Shared/App/Prefabs/RemoteAvatar.prefab";

        private const string RetargeterTypeName = "NetworkCharacterRetargeter";
        private const string SyncFieldName = "_bodyIndicesToSync";
        private const string SendFieldName = "_bodyIndicesToSend";

        /// <summary>
        /// İki prefabın DÖRT listesi de (her prefabta <c>sync</c> + <c>send</c>) birebir aynı mı.
        /// <para>⚠️ <b>Boş liste de ✗'tir:</b> SDK boş diziyi çalışma anında "tüm eklemler" diye
        /// doldurur, yani boş bırakılan prefab parmakları da göndermeye başlar — §6.9'un kestiği 40
        /// eklem sessizce geri gelir ve blob tavanına (<c>SKELETON_MAX_BLOB_BYTES</c>) yaklaşır.</para>
        /// </summary>
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

        /// <summary>Bir prefabın retargeter'ından iki listeyi okur; okunamazsa gerekçesini yazar.</summary>
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

        /// <summary>⚠️ Ağaç PASİF çocukları da kapsayarak taranır: retargeter, gövdesi kapalı bir
        /// düğümün altında durabilir.</summary>
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

        /// <summary>⚠️ SIRA da karşılaştırılır, yalnız içerik değil: blob eklemleri listedeki sırayla
        /// taşıyor.</summary>
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
