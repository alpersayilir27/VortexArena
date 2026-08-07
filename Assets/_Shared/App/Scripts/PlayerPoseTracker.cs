using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Core.Combat;
using VortexArena.Core.Player;
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
        /// <summary>
        /// Hiç geçerli örnek alınamamışsa kullanılan DİNLENME ofseti (sağ el, kafaya göreli metre;
        /// sol elin X'i terslenir). Oturum boyunca kumandası hiç çalışmamış bir oyuncu için de bir
        /// el pozu üretmek zorundayız — paket sabit uzunluklu, "eli olmayan oyuncu" diye bir tel
        /// durumu yok.
        /// <para>⚠️ Sıfır poz KULLANILMAZ: sorunun kendisi odur (el rig orijinine, oyuncunun
        /// ayağının dibine düşer). Kaba bir bel hizası duruşu yanlış ama <b>okunabilir</b>.</para>
        /// </summary>
        private static readonly Vector3 RestOffsetRight = new Vector3(0.20f, -0.45f, 0.25f);

        private Transform _head;
        private Transform _handL;
        private Transform _handR;

        /// <summary>
        /// Son GEÇERLİ el pozu, <b>kafaya göreli</b> (0 = sol, 1 = sağ). Kumanda düştüğünde bu poz
        /// o karenin kafa dünya pozuyla yeniden kurulur.
        /// <para>⚠️ <b>Arena uzayında dondurmak seçenek DEĞİL:</b> free-roam'da oyuncu yürümeye
        /// devam eder ve dondurulmuş el gövdeden kopup odanın ortasında asılı kalırdı. Kafaya
        /// göreli tutulan el, oyuncu hareket ettikçe onunla birlikte taşınır.</para>
        /// </summary>
        private readonly Pose[] _handRelative = new Pose[2];
        private readonly bool[] _hasHandRelative = new bool[2];

        // Kumanda durumu yalnız DEĞİŞTİĞİNDE loglanır: her karede basmak konsolu boğar ve
        // gerçek olay (pil bitişi) görünmez olur.
        private int _loggedStateL = ArenaProtocol.CONTROLLER_UNKNOWN;
        private int _loggedStateR = ArenaProtocol.CONTROLLER_UNKNOWN;

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

        private void Update()
        {
            // Kare başına tek örnek (idempotent): el geçerliliğini bu karede okuyan herkes —
            // poz, eşya bayrakları, iskelet kökü emniyeti — aynı cevabı görsün.
            ControllerTracking.Tick();

            ReportControllerState();
        }

        /// <summary>
        /// Anchor'ların dünya pozlarını ArenaSpace ile arena uzayına çevirip verir.
        /// <para>⚠️ Sıra korunur: eller ÖNCE dünya uzayında kurulur (canlı ya da tutulan), arenaya
        /// çevrim en sonda yapılır — tutma hesabı kafanın dünya pozuna dayanıyor.</para>
        /// </summary>
        public bool TryGetArenaPoses(out Pose head, out Pose handL, out Pose handR)
        {
            if (_head == null || _handL == null || _handR == null)
            {
                head = Pose.identity;
                handL = Pose.identity;
                handR = Pose.identity;
                return false;
            }

            Vector3 headPos = _head.position;
            Quaternion headRot = _head.rotation;

            head = ArenaSpace.WorldToArena(new Pose(headPos, headRot));
            handL = ArenaSpace.WorldToArena(ResolveHandWorld(0, _handL, headPos, headRot));
            handR = ArenaSpace.WorldToArena(ResolveHandWorld(1, _handR, headPos, headRot));
            return true;
        }

        /// <summary>
        /// Bir elin DÜNYA pozunu verir: kaynak geçerliyse canlı anchor (ve poz kafaya göreli
        /// saklanır), değilse saklanan kafa-göreli poz bu karenin kafasıyla yeniden kurulur.
        /// <para>⚠️ Anchor'ı koşulsuz okumak bu sınıfın <b>yapamayacağı</b> şeydir: rig el
        /// anchor'ını kaynak yokken de yazar ve yazdığı değer rig orijinidir
        /// (<see cref="ControllerTracking"/> sınıf notu).</para>
        /// </summary>
        private Pose ResolveHandWorld(int index, Transform anchor, Vector3 headPos, Quaternion headRot)
        {
            Quaternion headInverse = Quaternion.Inverse(headRot);

            if (ControllerTracking.IsValid(index == 1))
            {
                Vector3 pos = anchor.position;
                Quaternion rot = anchor.rotation;

                _handRelative[index] = new Pose(headInverse * (pos - headPos), headInverse * rot);
                _hasHandRelative[index] = true;
                return new Pose(pos, rot);
            }

            Pose relative = _hasHandRelative[index]
                ? _handRelative[index]
                : new Pose(MirrorForHand(index, RestOffsetRight), Quaternion.identity);

            return new Pose(headPos + headRot * relative.position, headRot * relative.rotation);
        }

        /// <summary>Sağ el ofsetini gerekiyorsa sol ele aynalar (X terslenir).</summary>
        private static Vector3 MirrorForHand(int index, Vector3 rightOffset)
        {
            return index == 1 ? rightOffset : new Vector3(-rightOffset.x, rightOffset.y, rightOffset.z);
        }

        /// <summary>
        /// Kumanda durumunu Net katmanına iter (§5.1). ⚠️ Ölçümü App yapar: <c>VortexArena.Net</c>
        /// Oculus.VR'ı referanslamaz, veri aşağı doğru itilir (<c>battery</c>/<c>rttMs</c> deseni).
        /// </summary>
        private void ReportControllerState()
        {
            int stateL = ControllerTracking.GetState(false);
            int stateR = ControllerTracking.GetState(true);

            ArenaClient.Instance?.ReportControllerState(stateL, stateR);

            if (stateL != _loggedStateL)
            {
                _loggedStateL = stateL;
                Debug.LogWarning($"[PlayerPoseTracker] Sol kumanda durumu: {DescribeState(stateL)}");
            }

            if (stateR != _loggedStateR)
            {
                _loggedStateR = stateR;
                Debug.LogWarning($"[PlayerPoseTracker] Sağ kumanda durumu: {DescribeState(stateR)}");
            }
        }

        private static string DescribeState(int state)
        {
            switch (state)
            {
                case ArenaProtocol.CONTROLLER_OK:
                    return "izleniyor (el pozu ölçüm).";
                case ArenaProtocol.CONTROLLER_UNTRACKED:
                    return "bağlı ama pozu geçersiz (görüş dışı/uykuda) — el son geçerli pozunda tutuluyor.";
                case ArenaProtocol.CONTROLLER_LOST:
                    return "bağlı DEĞİL — pil bitmiş olabilir. El son geçerli pozunda tutuluyor, " +
                           "poz bayat işaretleniyor.";
                default:
                    return "bilinmiyor (rig yok ya da henüz örneklenmedi).";
            }
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

            // Bayat el: poz ÖLÇÜM değil tahmindir (son geçerli poz kafaya göreli taşınıyor).
            // Alıcı bunu bilmezse bayat eli nişan/temas teşhisinde ölçüm sanardı — kapı
            // TryGetArenaPoses'takiyle aynı, ikisi de aynı karenin ControllerTracking örneğini okur.
            if (!ControllerTracking.IsValid(false))
            {
                gripFlags |= SnapshotEntry.FLAG_HAND_L_STALE;
            }

            if (!ControllerTracking.IsValid(true))
            {
                gripFlags |= SnapshotEntry.FLAG_HAND_R_STALE;
            }

            // §10.9: kafa bir iç engelin içinde. ⚠️ Bu bir ÖLÇÜM bildirimidir, ceza değil —
            // canı sunucu kendi tikinde eritir. Ölçen taraf (ObstacleViolationProbe) Core'da kendini
            // önyükleyen bir tekildir; burada yalnız okunur, HeldItems ile aynı seam deseni.
            if (ObstacleViolationProbe.IsViolating)
            {
                gripFlags |= SnapshotEntry.FLAG_IN_OBSTACLE;
            }
        }
    }
}
