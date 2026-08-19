#if UNITY_EDITOR
using System.Collections.Generic;
using Oculus.Interaction.HandGrab.Visuals;
using Oculus.Interaction.Input;
using UnityEngine;

namespace VortexArena.Core.Combat
{
    /// <summary>
    /// Kavrama Pozu Stüdyosu'nun sahneye koyduğu <b>tek elin</b> kimliği ve ayar yüzeyi: hangi
    /// kavrama noktasına ait olduğu, hangi el olduğu ve hayalet elin puppet'ı.
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
    /// ⚠️ <b>Parmaklar ELİN KEMİKLERİNDE yaşar, bu bileşende DEĞİL.</b> Riglenen duruş hayalet elin
    /// eklem <see cref="Transform"/>'larının kendisidir; kullanıcı onları Scene View'da çevirir,
    /// Kaydet onları oradan okur (<c>GripPoseStudio</c>). Bileşene ikinci bir "duruş" alanı
    /// EKLENMEZ: iki kopya, tezgâhta görülen el ile kaydedilen elin sessizce ayrışması demektir —
    /// oysa bu aracın bütün işi ikisinin aynı olması. Aynı sebeple duruşu bu bileşenden
    /// <b>kaydeden</b> bir düğme de yoktur: yazan tek düğme stüdyo penceresindedir.
    /// </para>
    /// <para>
    /// ⚠️ <b>Metakarpallar riglenmez</b> (<see cref="HandPoseLibrary.IsDrivable"/>): OpenXR dalında
    /// sentetik el, proksimal eklemlerin dönüşünü bilek uzayında beklediği için metakarpı çevirmek
    /// tezgâhta doğru, oyunda kaymış bir el üretirdi. Gerekçenin tamamı
    /// <see cref="HandPoseLibrary"/>'de.
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
        // puppet referansı kaybolur ve parmak eklemleri artık bulunamazdı.
        [SerializeField] private HandPuppet _puppet;

        // ⚠️ Hayalet elin köke (kumandaya) göre pozu BURADA SAKLANMAZ: tek kaynağı
        // ItemGripAuthority.ResolveAnchorToWrist'tir ve stüdyo eli her tazelediğinde oradan okur.
        // Kopyası burada dursaydı, ofsetin tanımı değiştiğinde tezgâhta duran eller eski değeri
        // taşımaya devam ederdi (obje DontSave'dir ama domain reload'ı yaşar).

        public GripSocketKind Kind => _kind;
        public bool RightHand => _rightHand;
        public Handedness Handedness => _rightHand ? Handedness.Right : Handedness.Left;
        public HandPuppet Puppet => _puppet;

        /// <summary>
        /// Eli tanıtır. Stüdyo el kurarken bir kez çağırır; parmak duruşunu ayrıca
        /// <see cref="ApplyPose"/> yazar.
        /// </summary>
        public void Resolve(HandPuppet puppet, GripSocketKind kind, bool rightHand)
        {
            _puppet = puppet;
            _kind = kind;
            _rightHand = rightHand;
        }

        /// <summary>
        /// Kayıtlı bir parmak duruşunu hayalet elin kemiklerine yazar (kayıt boşsa boş elin duruşu).
        /// Stüdyo eli KURARKEN çağırır — el tezgâha son kaydedildiği duruşta gelsin.
        /// <para>⚠️ Kurulmuş bir elde yeniden çağrılmaz: kullanıcının o an çevirdiği kemikleri
        /// diskteki duruşa geri atmak, "riglemeye çalışıyorum ama parmaklar sıfırlanıyor" olurdu.</para>
        /// <para>⚠️ Puppet yoksa SESSİZ geçer: el kurulumunun ortasında bileşen puppet çözülmeden
        /// önce de buraya düşebiliyor ve orada hata basmak, gerçek bir sorunu olmayan her el
        /// kurulumunda konsola satır atardı.</para>
        /// </summary>
        public void ApplyPose(HandJointRotation[] joints)
        {
            if (_puppet == null)
            {
                return;
            }

            _puppet.SetJointRotations(HandPoseLibrary.BuildJointRotations(joints, _rightHand));
        }

        /// <summary>
        /// Bu elin <b>riglenebilir</b> parmak eklemleri (metakarpallar hariç — gerekçe sınıf
        /// uyarısında). Stüdyo bunları hem seçime açar hem kayda alır; puppet yoksa boş dizi.
        /// </summary>
        public List<HandJointMap> DrivableJoints()
        {
            var result = new List<HandJointMap>();
            List<HandJointMap> maps = _puppet != null ? _puppet.JointMaps : null;

            for (int i = 0; maps != null && i < maps.Count; i++)
            {
                HandJointMap map = maps[i];
                if (map == null || map.transform == null || !HandPoseLibrary.IsDrivable(map.id))
                {
                    continue;
                }

                result.Add(map);
            }

            return result;
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
