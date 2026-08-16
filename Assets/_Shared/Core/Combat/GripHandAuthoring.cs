#if UNITY_EDITOR
using Oculus.Interaction.HandGrab.Visuals;
using Oculus.Interaction.Input;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Kavrama Pozu Stüdyosu'nun sahneye koyduğu <b>tek elin</b> kimliği ve ayar yüzeyi: hangi
    /// kavrama noktasına ait olduğu, hangi el olduğu ve parmak preset'i.
    /// <para>
    /// ⚠️ <b>Bu bileşen bilerek RUNTIME asmdef'indedir</b> (<c>VortexArena.Core</c>) ve dosyanın
    /// tamamı <c>#if UNITY_EDITOR</c> sarmalındadır. Editör asmdef'ine konamaz: Unity, editör
    /// derlemesinde tanımlı bir <see cref="MonoBehaviour"/>'ı bir GameObject'e eklemeyi reddeder
    /// ("it is an editor script") ve <c>AddComponent</c> sessizce <c>null</c> döner — stüdyo el
    /// kurarken tam o satırda patlar. Build güvenliği iki yerden gelir: sarmal sayesinde tip
    /// oyunun derlemesine hiç girmez, objeler de <see cref="HideFlags.DontSave"/> olduğu için
    /// sahneye/prefaba yazılmaz (yani "missing script" bırakacak bir örnek oluşmaz).
    /// </para>
    /// <para>
    /// ⚠️ <b>Parmaklar burada AYARLANMAZ, preset'ten gelir.</b> Eklem dizisinin tek kaynağı
    /// <see cref="HandGripPresets"/>'tir ve stüdyoda görülen el ile oyundaki sentetik el
    /// <b>aynı diziyi</b> ISDK'nın aynı JointMap yolundan uygular — yani tezgâhta görülen parmak
    /// duruşu oyunda birebir tekrarlanır. Parmak/eklem slider'ı eklemek bu kimliği bozardı:
    /// tezgâhta ayarlanan ama kayda giremeyen bir duruş, oyunda hiç görülmeyecek bir ince ayar
    /// olurdu.
    /// </para>
    /// <para>
    /// ⚠️ Bu objenin <b>transformu ISDK BİLEK çerçevesidir</b> ve başka hiçbir şeye çevrilmez:
    /// kayıt da bilek uzayında tutulur (<see cref="ItemGripPose"/>). Anchor (kumanda) uzayına
    /// köprü runtime'ın işidir (<see cref="ItemGripAuthority"/>) — authoring döngüsüne ölçülmemiş
    /// bir düzeltme sokmak, kullanıcının gözüyle "düzgün" gördüğü eli o düzeltmenin hatası kadar
    /// yanlış yere koyardı.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GripHandAuthoring : MonoBehaviour
    {
        [SerializeField] private GripSocketKind _kind;
        [SerializeField] private bool _rightHand = true;

        // ⚠️ [SerializeField] ve bilerek öyle: obje DontSave olduğu için diske hiç yazılmaz ama
        // DOMAIN RELOAD'ı (script derlemesi) yaşar. Serialize edilmeseydi her derlemeden sonra
        // puppet referansı kaybolur ve preset değişimi sessizce hiçbir şey yapmazdı.
        [SerializeField] private HandPuppet _puppet;

        [Tooltip("Bu elin parmak duruşu — kayda giden tek parmak bilgisi budur.")]
        [SerializeField] private HandGripPreset _preset = HandGripPreset.Firing;

        public GripSocketKind Kind => _kind;
        public bool RightHand => _rightHand;
        public Handedness Handedness => _rightHand ? Handedness.Right : Handedness.Left;
        public HandPuppet Puppet => _puppet;

        /// <summary>
        /// Elin parmak duruşu. Yazınca kemiklere anında uygulanır — tezgâhta seçilen preset ile
        /// ekranda görülen el arasında bir "uygula" adımı kalmasın.
        /// </summary>
        public HandGripPreset Preset
        {
            get => _preset;
            set
            {
                _preset = value;
                ApplyPreset();
            }
        }

        /// <summary>
        /// Eli tanıtır ve parmaklarını preset'e sokar. Stüdyo el kurarken bir kez çağırır.
        /// </summary>
        public void Resolve(HandPuppet puppet, GripSocketKind kind, bool rightHand,
            HandGripPreset preset)
        {
            _puppet = puppet;
            _kind = kind;
            _rightHand = rightHand;
            _preset = preset;
            ApplyPreset();
        }

        /// <summary>
        /// Preset'in eklem dizisini puppet'a yazar.
        /// <para>⚠️ Puppet yoksa SESSİZ geçer: el kurulumu sırasında bileşen puppet çözülmeden
        /// önce de <c>OnValidate</c> ile buraya düşebiliyor ve orada hata basmak, gerçek bir sorunu
        /// olmayan her el kurulumunda konsola satır atardı.</para>
        /// </summary>
        public void ApplyPreset()
        {
            if (_puppet == null)
            {
                return;
            }

            _puppet.SetJointRotations(HandGripPresets.JointRotations(_preset, _rightHand));
        }

        // Inspector'dan (ya da bir preset alanına elle dokunulduğunda) değişen duruş Scene View'da
        // anında görünsün. ⚠️ Repaint BURADAN çağrılmaz: SceneView editör API'sidir ve bu dosya
        // runtime asmdef'indedir — çağıran taraf (GripHandAuthoringEditor) tazeler.
        private void OnValidate()
        {
            ApplyPreset();
        }
    }
}
#endif
