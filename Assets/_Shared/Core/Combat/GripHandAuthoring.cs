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
    /// ⚠️ Bu objenin <b>transformu KUMANDA (anchor) çerçevesidir</b> — <c>OVRCameraRig</c> el
    /// anchor'ının silah üstündeki yeri; kayda giren şey bu kökün KONUMUDUR (<see cref="ItemGripPose"/>:
    /// dönüş yok, silah oyunda her zaman kumandayla hizalıdır — kök de silahla hizalı tutulur, yalnız
    /// taşınır). ISDK hayalet eli ve kumanda modeli bu kökün ÇOCUKLARIDIR (<see cref="Puppet"/>);
    /// kayda giren şey kökün konumudur, çocukların değil.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GripHandAuthoring : MonoBehaviour
    {
        /// <summary>Kök gizmosunun kumanda ileri okunun uzunluğu (m) — silah ölçeğinde okunur olsun.</summary>
        private const float GizmoForwardLength = 0.08f;
        private const float GizmoSideLength = 0.03f;
        private const float GizmoOriginRadius = 0.008f;

        [SerializeField] private GripSocketKind _kind;
        [SerializeField] private bool _rightHand = true;

        // ⚠️ [SerializeField] ve bilerek öyle: obje DontSave olduğu için diske hiç yazılmaz ama
        // DOMAIN RELOAD'ı (script derlemesi) yaşar. Serialize edilmeseydi her derlemeden sonra
        // puppet referansı kaybolur ve preset değişimi sessizce hiçbir şey yapmazdı.
        [SerializeField] private HandPuppet _puppet;

        [Tooltip("Bu elin parmak duruşu — kayda giden tek parmak bilgisi budur.")]
        [SerializeField] private HandGripPreset _preset = HandGripPreset.Firing;

        // Hayalet elin köke (kumandaya) göre yerel pozu ve kaynağı (ölçülmüş sabit mi, iskeletten
        // tahmin mi). Kayda GİRMEZ; yalnız görsel çocuğun nereye oturtulacağıdır ve elle kaydırılmış
        // çocuğu geri getirmek için saklanır (DontSave obje domain reload'ı yaşar → SerializeField).
        [SerializeField] private Vector3 _ghostOffsetPosition;
        [SerializeField] private Quaternion _ghostOffsetRotation = Quaternion.identity;
        [SerializeField] private bool _ghostOffsetMeasured;

        public GripSocketKind Kind => _kind;
        public bool RightHand => _rightHand;
        public Handedness Handedness => _rightHand ? Handedness.Right : Handedness.Left;
        public HandPuppet Puppet => _puppet;

        /// <summary>Hayalet elin kumanda köküne göre yerel pozu (anchor→bilek); stüdyo yazar.</summary>
        public Pose GhostOffset => new Pose(_ghostOffsetPosition, _ghostOffsetRotation);

        /// <summary>Ofset ölçülmüş sabitten mi geldi (<c>false</c> = iskeletten tahmin, görsel yaklaşık).</summary>
        public bool GhostOffsetMeasured => _ghostOffsetMeasured;

        /// <summary>Stüdyonun hayalet ofsetini yazdığı tek kapı.</summary>
        public void SetGhostOffset(in Pose offset, bool measured)
        {
            _ghostOffsetPosition = offset.position;
            _ghostOffsetRotation = offset.rotation;
            _ghostOffsetMeasured = measured;
        }

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

        /// <summary>
        /// Kumanda kökünün gizmosu: mavi ok = kumandanın ilerisi (= namlu yönü; kök silahla hizalı
        /// tutulur), yeşil = yukarı, kırmızı = sağ, ortada küçük küre. Kökün kendisinde renderer
        /// yoktur — bu gizmo olmasa kullanıcı neyi sürüklediğini göremezdi.
        /// </summary>
        private void OnDrawGizmos()
        {
            Transform t = transform;
            Vector3 origin = t.position;

            Gizmos.color = new Color(0.25f, 0.55f, 1f, 1f);
            Gizmos.DrawRay(origin, t.forward * GizmoForwardLength);
            Gizmos.color = new Color(0.35f, 0.9f, 0.35f, 1f);
            Gizmos.DrawRay(origin, t.up * GizmoSideLength);
            Gizmos.color = new Color(1f, 0.4f, 0.4f, 1f);
            Gizmos.DrawRay(origin, t.right * GizmoSideLength);
            Gizmos.color = new Color(1f, 1f, 1f, 0.9f);
            Gizmos.DrawWireSphere(origin, GizmoOriginRadius);
        }
    }
}
#endif
