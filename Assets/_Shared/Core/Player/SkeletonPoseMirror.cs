using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// İkinci bir modeli (takım gövdesi) karakterin canlı iskeletinden sürer — <b>kemik aynası</b>
    /// ile: iki ağaçta ADI eşleşen kemiklerin <c>localRotation</c>'ı birebir kopyalanır.
    /// <para>
    /// ⚠️ <b>Neden <see cref="HumanPoseHandler"/> KULLANILMIYOR:</b> <c>GetHumanPose</c> gövde
    /// konumunu (<c>bodyPosition</c>) DÜNYA uzayında döndürüyor, <c>SetHumanPose</c> ise onu köke
    /// GÖRELİ uyguluyor — iki kök üst üste oturtulduğunda öteleme ikinci kez uygulanır ve gövde
    /// arenanın dışına kayar. Aynı Mixamo iskeletini paylaşan iki model için kas uzayına hiç
    /// girmeye gerek yok: kemik adları birebir aynı.
    /// </para>
    /// <para>
    /// ⚠️ <b>Ön koşul kemik ADLARININ eşleşmesidir</b> (aynı Mixamo rig'i). Humanoid Avatar
    /// gerekmez; eşleşme yoksa bileşen hata basıp kendini kapatır.
    /// </para>
    /// <para>
    /// ⚠️ <b>Execution order yüksektir ve öyle kalmalı:</b> okunacak şey SDK'nın o kare
    /// <b>uygulanmış</b> pozudur. <c>CharacterRetargeter</c> ve
    /// <see cref="ArenaNetCharacterBehaviour"/> kendi <c>LateUpdate</c>'lerinde yazıyor; daha erken
    /// koşan bir sürücü bir kare bayat gövde çizerdi.
    /// </para>
    /// <para>
    /// Görünmezken hiç koşmaz: <see cref="RemoteAvatar"/> bu bileşenin <c>enabled</c>'ını çizilen
    /// gövdeyle birlikte açıp kapatır.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(30100)]
    public class SkeletonPoseMirror : MonoBehaviour
    {
        [Header("Kaynak — ağdan sürülen karakter")]
        [Tooltip("Retargeter'ın yazdığı iskeletin kökü (ArenaNetCharacterBehaviour'un oturttuğu kök).")]
        [SerializeField] private Transform sourceRoot;

        [Header("Hedef — aynalanan model")]
        [Tooltip("Aynalanan modelin kökü. Model değiştirmek bu kökü değiştirmektir.")]
        [SerializeField] private Transform targetRoot;

        [Header("Kalça (iskeletin kökü)")]
        [Tooltip("Kaynak modelin kalça kemiği (mixamorig:Hips).")]
        [SerializeField] private Transform sourceHips;

        [Tooltip("Hedef modelin kalça kemiği (mixamorig:Hips).")]
        [SerializeField] private Transform targetHips;

        [Tooltip("Kaynak kalçanın BIND pozundaki localPosition'ı. ⚠️ Çalışma anında hesaplanamaz: " +
                 "prefabdaki iskelet bind pozunda olmak zorunda değil, bu yüzden editör aracı " +
                 "FBX asset'inden okuyup buraya yazar.")]
        [SerializeField] private Vector3 sourceHipsBind;

        [Tooltip("Hedef kalçanın BIND pozundaki localPosition'ı (gerekçe: sourceHipsBind).")]
        [SerializeField] private Vector3 targetHipsBind;

        [Tooltip("Hedefin iskelet kolonunu kaynağınkine eşitleyen tek tip çarpan. Kemik " +
                 "kopyalamada hedef KENDİ boyunda çizilir; iki modelin kolon uzunluğu farklıysa " +
                 "bu çarpan onu eşitler. Oyuncunun gerçek boyu AYRI bir şeydir (kaynağın " +
                 "localScale'i) ve ayrıca çarpılır.")]
        [SerializeField] private float heightCalibration = 1f;

        // ⚠️ Diziler bir kez kurulur: kare başına GetComponentsInChildren çağırmak çöp üretir
        // (bu bileşen her uzak oyuncuda 72/sn koşuyor).
        private Transform[] _source;
        private Transform[] _target;

        private bool _ready;

        private void Awake()
        {
            _ready = TrySetup();
        }

        /// <summary>
        /// Kemik eşleşmesini kurar. ⚠️ <b>Hata basar ve kendini kapatır:</b> sessiz kalsaydı gövde
        /// T-pozunda donar ve bu sahada "ağ bozuk" diye okunurdu — oysa tek eksik bir prefab bağı
        /// ya da uyuşmayan bir iskelettir.
        /// </summary>
        private bool TrySetup()
        {
            if (sourceRoot == null || targetRoot == null)
            {
                Debug.LogError("[SkeletonPoseMirror] sourceRoot/targetRoot boş — gövde " +
                               "sürülemeyecek (T-pozunda donar).", this);
                enabled = false;
                return false;
            }

            // Hedefin adlarını bir kez sözlüğe al; kaynağı gezerken arama böylece O(1) olur.
            Transform[] targetBones = targetRoot.GetComponentsInChildren<Transform>(true);
            var byName = new Dictionary<string, Transform>(targetBones.Length);
            for (int i = 0; i < targetBones.Length; i++)
            {
                byName[targetBones[i].name] = targetBones[i];
            }

            Transform[] sourceBones = sourceRoot.GetComponentsInChildren<Transform>(true);
            var sources = new List<Transform>(sourceBones.Length);
            var targets = new List<Transform>(sourceBones.Length);

            for (int i = 0; i < sourceBones.Length; i++)
            {
                Transform bone = sourceBones[i];

                // ⚠️ Kökün KENDİSİ hariç: kökün yerleşimini her kare LateUpdate yazıyor
                // (kaynağın dünya pozu), yerel dönüşünü kopyalamak onu ezerdi.
                if (bone == sourceRoot)
                {
                    continue;
                }

                if (byName.TryGetValue(bone.name, out Transform match) && match != targetRoot)
                {
                    sources.Add(bone);
                    targets.Add(match);
                }
            }

            if (sources.Count == 0)
            {
                Debug.LogError("[SkeletonPoseMirror] İki modelin kemik adları hiç eşleşmiyor — " +
                               "bu bileşenin tek ön koşulu aynı iskeleti (Mixamo) paylaşmalarıdır. " +
                               "Gövde sürülemeyecek (T-pozunda donar).", this);
                enabled = false;
                return false;
            }

            _source = sources.ToArray();
            _target = targets.ToArray();
            return true;
        }

        private void LateUpdate()
        {
            if (!_ready)
            {
                return;
            }

            targetRoot.SetPositionAndRotation(sourceRoot.position, sourceRoot.rotation);

            // İki çarpan farklı şeylerdir ve karıştırılmaz: kaynağın localScale'i oyuncunun GERÇEK
            // boyudur (blob'un 0. ekleminden gelir, SDK yazar), heightCalibration ise iki modelin
            // iskelet kolonu arasındaki sabit farktır.
            targetRoot.localScale = sourceRoot.localScale * heightCalibration;

            // ⚠️ Yalnız localRotation kopyalanır, localPosition DEĞİL: kemik uzunlukları hedefin
            // KENDİ modelinden gelmeli, yoksa hedef kaynağın oranlarına zorlanır ve mesh deforme
            // olur.
            for (int i = 0; i < _source.Length; i++)
            {
                _target[i].localRotation = _source[i].localRotation;
            }

            // ⚠️ Kalça istisnadır: o, iskeletin köküdür ve konumu POZA aittir (çömelme, adım).
            // Kendi bind'ine GÖRE aktarılır — ham konum kopyalansaydı iki modelin farklı kalça
            // yüksekliği doğrudan bir kaymaya dönüşürdü.
            if (sourceHips != null && targetHips != null)
            {
                targetHips.localPosition = sourceHips.localPosition - sourceHipsBind + targetHipsBind;
            }
        }
    }
}
