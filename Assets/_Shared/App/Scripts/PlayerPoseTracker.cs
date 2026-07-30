using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.App
{
    /// <summary>
    /// Yerel oyuncunun poz kaynağı (Lobby + arena sahnelerine konur): BB rig
    /// anchor'larını bulur ve kendini UdpStateChannel'a IPoseSource olarak kaydeder.
    /// Dünya→arena dönüşümü BURADA yapılır (ArenaSpace); Net katmanı yalnız hazır
    /// arena-uzayı pozları alır. Admin rolü poz göndermez.
    /// <para>
    /// <b>Kalibrasyon kapısı YOKTUR:</b> kayıt anchor'lar bulunur bulunmaz yapılır,
    /// hizalama beklenmez. Kalibrasyondan önceki pozlar arena ile ÖRTÜŞMEZ (rig henüz
    /// hizalanmadığı için ofsetlidir) ama gönderilir — oyuncunun bağlı ve hareket
    /// hâlinde olduğu ağdan görülebilsin diye. Kalibrasyon bitince rig hizalanır ve
    /// aynı kaynak kendiliğinden doğru uzayda poz vermeye başlar; yeniden kaydolmak
    /// gerekmez.
    /// </para>
    /// <para>
    /// <b>Poz + eşya bildiriminin TEK kapısı burasıdır</b> (§6.2: <c>itemL</c>/<c>itemR</c>/
    /// <c>gripFlags</c> pozla aynı pakette gider). ⚠️ Bu sınıf eşya durumunu <b>ÜRETMEZ</b> —
    /// <see cref="HeldItems"/>'tan okur; üreten taraf <c>Weapon</c>/<c>WeaponGranter</c>'dır.
    /// Buraya "elde ne var" keşfi (silah listesi tarama, grab olayına abonelik) eklenirse aynı
    /// bilginin ikinci bir kaynağı doğar.
    /// </para>
    /// </summary>
    public class PlayerPoseTracker : MonoBehaviour, IPoseSource
    {
        private Transform _head;
        private Transform _handL;
        private Transform _handR;

        private void Start()
        {
            if (AppSession.Role != AppSession.RolePlayer)
            {
                enabled = false; // admin poz göndermez
                return;
            }

            OVRCameraRig rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig == null)
            {
                Debug.LogWarning("[PlayerPoseTracker] OVRCameraRig bulunamadı; poz gönderimi devre dışı.");
                enabled = false;
                return;
            }

            _head = rig.centerEyeAnchor;
            _handL = rig.leftHandAnchor;
            _handR = rig.rightHandAnchor;

            // Kalibrasyon beklenmez: kaynak burada kaydolur, hizalama sonradan gelince
            // aynı kaynağın verdiği pozlar kendiliğinden doğru uzaya oturur.
            ArenaClient.Instance?.UdpChannel?.SetPoseSource(this);
        }

        private void OnDestroy()
        {
            ArenaClient.Instance?.UdpChannel?.ClearPoseSource(this);
        }

        /// <summary>Anchor'ların dünya pozlarını ArenaSpace ile arena uzayına çevirip verir.</summary>
        public bool TryGetArenaPoses(out Pose head, out Pose handL, out Pose handR)
        {
            if (_head == null || _handL == null || _handR == null)
            {
                head = Pose.identity;
                handL = Pose.identity;
                handR = Pose.identity;
                return false;
            }

            head = ArenaSpace.WorldToArena(new Pose(_head.position, _head.rotation));
            handL = ArenaSpace.WorldToArena(new Pose(_handL.position, _handL.rotation));
            handR = ArenaSpace.WorldToArena(new Pose(_handR.position, _handR.rotation));
            return true;
        }

        /// <summary>
        /// §6.2: o an elde tutulan eşya baytları — yalnız <see cref="HeldItems"/>'ın son
        /// bildirilen durumunu telin beklediği biçime çevirir.
        /// </summary>
        public void GetHeldItems(out byte itemL, out byte itemR, out byte gripFlags)
        {
            itemL = HeldItems.Left;
            itemR = HeldItems.Right;

            // ⚠️ bit0 (FLAG_ALIVE) BURADA ASLA yazılmaz: o bitin yazarı yalnız sunucudur, istemci
            // kendini canlı ilan edemez (§6.2/§6.3). Sunucu gelen baytı maskeyle süzüyor ama
            // doğrusu hiç yazmamaktır — maske ileride gevşerse bu satır kuralı taşımaya devam eder.
            gripFlags = 0;
            if (HeldItems.GripLinked)
            {
                gripFlags |= SnapshotEntry.FLAG_GRIP_LINKED;
            }

            if (HeldItems.PrimaryRight)
            {
                gripFlags |= SnapshotEntry.FLAG_PRIMARY_RIGHT;
            }
        }
    }
}
