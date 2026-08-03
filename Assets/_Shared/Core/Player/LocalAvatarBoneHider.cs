using UnityEngine;

namespace VortexArena.Core.Player
{
    /// <summary>
    /// Birinci şahıs gövde avatarında istenmeyen uzuvları gizler.
    /// <para>
    /// Gövde tek <see cref="SkinnedMeshRenderer"/> olduğu için "şu uzvu gösterme" renderer
    /// kapatarak yapılamaz; tek güvenli yol kemiği sıfıra yakın ÖLÇEKLEMEKtir (çocuk kemikler
    /// ölçeği miras alır, iskelet bozulmaz).
    /// </para>
    /// <para>
    /// ⚠️ Kemikler <b>isimle değil</b> <see cref="Animator.GetBoneTransform"/> ile bulunur: model
    /// değiştiğinde (Mixamo öneki, farklı rig adlandırması) burada tek satır bile değişmesin.
    /// </para>
    /// <para>
    /// <see cref="DefaultExecutionOrder"/> yüksektir çünkü Movement SDK retargeting kemikleri her
    /// kare yazıyor — ölçek EN SON basılmalı. ⚠️ SDK'nın <c>ApplyPoseJob</c>'u kemiklere
    /// <c>SetLocalPositionAndRotation</c> ile yazıyor, yani bugün ölçeği ezmiyor; sıralama yine de
    /// garanti altına alınır ki retargeting'e ölçek yazan bir yol eklendiğinde uzuv sessizce geri
    /// gelmesin.
    /// </para>
    /// <para>
    /// ⚠️ <b>Gizleme her kare GERİ ALINIR ve bu zorunludur, üslup değil:</b> ağa giden iskelet
    /// blob'u kemiklerin <b>canlı Unity transformlarından</b> okunuyor ve okuma
    /// <c>localScale</c>'i de kapsıyor (<c>SkeletonJobs.GetPoseJob</c>). SDK ölçeği bir daha
    /// yazmadığı için buradaki sıfırlar kalıcıdır ve serileştirmeye olduğu gibi girerdi: uzak
    /// tarafta bacaklar kalçaya, kafa göğse ÇÖKER — belirtisi "oyuncular havada duruyor"dur, çünkü
    /// görünen gövde kalçada biter. Bu yüzden ölçek <see cref="Update"/>'te gerçek değerine
    /// döndürülür (gönderim <c>LateUpdate</c>'te yapılıyor) ve yalnız
    /// <see cref="LateUpdate"/>'te — çizimden hemen önce — gizlenir. Yani telde tam gövde gider,
    /// ekranda uzuv görünmez.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(30000)]
    public class LocalAvatarBoneHider : MonoBehaviour
    {
        /// <summary>
        /// Gizlenecek kemikler. Varsayılan neden bunlar:
        /// <list type="bullet">
        /// <item><b>Head/Neck</b> — kamera tam onların içinde durur; gizlenmezse oyuncu kafatasının
        /// içini görür.</item>
        /// <item><b>LeftUpperLeg/RightUpperLeg</b> — bacak izleme YOKTUR: Quest 3'te bacakta sensör
        /// yok, <c>BodyJointSet.FullBody</c> seçilse bile alt gövde ÜRETİLİR (generative legs).
        /// Uzaktan bakan için ikna edici, ama kendi bakışında aşağı bakınca uydurma adımlar
        /// görünürdü. ⚠️ Bu liste bir tercih değil, <b>izlenmeyen uzuvların listesidir</b>: gerçek
        /// bacak izlemesi gelirse buradan çıkarılırlar.</item>
        /// </list>
        /// <para>Alt bacak/ayak ayrıca yazılmaz: çocuk kemikler ölçeği miras alır.</para>
        /// </summary>
        [Tooltip("Sıfıra yakın ölçeklenerek gizlenecek humanoid kemikler (çocukları da kaybolur).")]
        [SerializeField] private HumanBodyBones[] hiddenBones =
        {
            HumanBodyBones.Head,
            HumanBodyBones.Neck,
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.RightUpperLeg
        };

        [Tooltip("Gizlenen kemiğe uygulanan ölçek (0'a çok yakın: mesh görünmez, iskelet bozulmaz).")]
        [SerializeField] private float hiddenScale = 0.0001f;

        /// <summary>Awake'te BİR KEZ çözülen kemikler — <c>GetBoneTransform</c> her karede
        /// çağrılmaz (sözlük araması, LateUpdate'te bedava değil).</summary>
        private Transform[] _bones;

        /// <summary>Kemiklerin gizlemeden ÖNCEKİ gerçek ölçekleri; her kare bunlara döndürülür
        /// (gerekçe sınıf özetinde: gizli ölçek ağa sızıyor).</summary>
        private Vector3[] _originalScales;

        private void Awake()
        {
            var animator = GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
            {
                Debug.LogWarning("[LocalAvatarBoneHider] Humanoid Animator bulunamadı; kemik gizleme " +
                                 "kapatıldı (model importer'da Rig > Animation Type = Humanoid olmalı).", this);
                enabled = false;
                return;
            }

            _bones = new Transform[hiddenBones.Length];
            _originalScales = new Vector3[hiddenBones.Length];
            for (int i = 0; i < hiddenBones.Length; i++)
            {
                _bones[i] = animator.GetBoneTransform(hiddenBones[i]);
                _originalScales[i] = _bones[i] != null ? _bones[i].localScale : Vector3.one;
                if (_bones[i] == null)
                {
                    // Tek seferlik (Awake): eksik kemik kalıcı bir kurulum hatasıdır, her karede
                    // loglamak konsolu boğardı.
                    Debug.LogWarning($"[LocalAvatarBoneHider] '{hiddenBones[i]}' kemiği avatarda " +
                                     "çözülemedi; o uzuv gizlenmeyecek.", this);
                }
            }
        }

        /// <summary>
        /// Gizlemeyi geri alır — <b>ağa doğru gövde gitsin diye</b>.
        /// <para>Tüm <c>Update</c>'ler tüm <c>LateUpdate</c>'lerden önce koştuğu için, iskeleti
        /// serileştiren <c>NetworkCharacterHandler.LateUpdate</c> kemikleri her zaman GERÇEK
        /// ölçekleriyle okur. Gerekçenin tamamı sınıf özetinde.</para>
        /// </summary>
        private void Update()
        {
            ApplyScales(hidden: false);
        }

        private void LateUpdate()
        {
            ApplyScales(hidden: true);
        }

        private void ApplyScales(bool hidden)
        {
            if (_bones == null)
            {
                return;
            }

            Vector3 scale = Vector3.one * hiddenScale;
            for (int i = 0; i < _bones.Length; i++)
            {
                if (_bones[i] != null)
                {
                    _bones[i].localScale = hidden ? scale : _originalScales[i];
                }
            }
        }
    }
}
