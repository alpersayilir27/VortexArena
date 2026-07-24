using UnityEngine;
using VortexArena.Core.Arena;
using VortexArena.Net;

namespace VortexArena.App
{
    /// <summary>
    /// Yerel oyuncunun poz kaynağı (Lobby + arena sahnelerine konur): BB rig
    /// anchor'larını bulur, kalibrasyon kapısını bekler ve kendini UdpStateChannel'a
    /// IPoseSource olarak kaydeder. Dünya→arena dönüşümü BURADA yapılır (ArenaSpace);
    /// Net katmanı yalnız hazır arena-uzayı pozları alır. Admin rolü poz göndermez.
    /// </summary>
    public class PlayerPoseTracker : MonoBehaviour, IPoseSource
    {
        private Transform _head;
        private Transform _handL;
        private Transform _handR;
        private bool _calibrationSubscribed;

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

            // Kalibrasyon kapısı: kalibratör yoksa (Lobby — origin yok, dünya=arena)
            // hemen kaydol; varsa hizalama tamamlanana dek bekle.
            ArenaCalibrator calibrator = FindFirstObjectByType<ArenaCalibrator>();
            if (calibrator == null || calibrator.IsCalibrated)
            {
                RegisterPoseSource();
                return;
            }

            ArenaCalibrator.Calibrated += HandleCalibrated;
            _calibrationSubscribed = true;
        }

        private void OnDestroy()
        {
            if (_calibrationSubscribed)
            {
                ArenaCalibrator.Calibrated -= HandleCalibrated;
                _calibrationSubscribed = false;
            }

            ArenaClient.Instance?.UdpChannel?.ClearPoseSource(this);
        }

        private void HandleCalibrated()
        {
            ArenaCalibrator.Calibrated -= HandleCalibrated;
            _calibrationSubscribed = false;
            RegisterPoseSource();
        }

        private void RegisterPoseSource()
        {
            ArenaClient.Instance?.UdpChannel?.SetPoseSource(this);
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
    }
}
