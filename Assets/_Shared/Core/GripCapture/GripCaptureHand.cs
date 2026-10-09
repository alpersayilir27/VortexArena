using System;
using System.Collections.Generic;
using Oculus.Interaction.HandGrab.Visuals;
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEngine;
using VortexArena.Core.Combat;

namespace VortexArena.Core.GripCapture
{
    /// <summary>
    /// One side's capture hand: the live ghost puppet driven by tracking and clamped to the item's
    /// surface, its collider capsules, the ghost controller, and the frozen ghosts of captured slots.
    /// </summary>
    public sealed class GripCaptureHand
    {
        private const string GhostProviderPath =
            "Packages/com.meta.xr.sdk.interaction/Runtime/Prefabs/HandGrab/OpenXRGhostProvider.asset";

        private const string ControllerPrefabPath =
            "Packages/com.meta.xr.sdk.core/Prefabs/OVRControllerPrefab.prefab";

        /// <summary>The hand's own capsules sit here so the clamp never queries them.</summary>
        private const int HandLayer = 2;

        private const float PalmProbeRadius = 0.02f;

        private const int ItemMask = 1 << 0;

        private static readonly Color FrozenColour = new Color(0.25f, 1f, 0.4f, 0.35f);
        private static readonly Color ControllerColour = new Color(1f, 1f, 1f, 0.3f);

        private readonly bool _rightHand;
        private readonly List<GripFingerClamp.Segment> _segments = new List<GripFingerClamp.Segment>();
        private readonly List<CapsuleCollider> _capsules = new List<CapsuleCollider>();
        private readonly Dictionary<GripSocketKind, GameObject> _frozen =
            new Dictionary<GripSocketKind, GameObject>();

        private Transform _parent;
        private HandGhostProvider _provider;
        private HandPuppet _livePuppet;
        private GameObject _liveGhost;
        private GameObject _controllerGhost;
        private Transform _capsuleRoot;

        private HandJointMap[] _maps;
        private HandJointRotation[] _tracked;
        private int[] _drivableSlots;
        private Quaternion[] _open;
        private Quaternion[] _target;
        private Quaternion[] _clamped;
        private readonly bool[] _touching = new bool[5];
        private readonly bool[] _penetrating = new bool[5];

        public GripCaptureHand(bool rightHand)
        {
            _rightHand = rightHand;
        }

        public bool RightHand => _rightHand;

        /// <summary>Tracked this frame and the ghost is posed — the only state a capture accepts.</summary>
        public bool Valid { get; private set; }

        public bool PalmInside { get; private set; }

        public Pose Wrist { get; private set; }

        /// <summary>The clamped joint array (<c>HAND_JOINT_IDS</c> order) — read only.</summary>
        public Quaternion[] ClampedJoints => _clamped;

        public bool[] Touching => _touching;

        public bool[] Penetrating => _penetrating;

        /// <summary>Fingers resting on the item's surface.</summary>
        public int TouchCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _touching.Length; i++)
                {
                    if (_touching[i])
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Builds the live ghost, its capsules and the ghost controller.</summary>
        public bool Build(Transform parent)
        {
            _parent = parent;
            _provider = AssetDatabase.LoadAssetAtPath<HandGhostProvider>(GhostProviderPath);
            if (_provider == null)
            {
                Debug.LogError($"[GripCapture] Hayalet el sağlayıcısı yüklenemedi: {GhostProviderPath}");
                return false;
            }

            _liveGhost = InstantiateGhost(parent, out _livePuppet);
            if (_liveGhost == null)
            {
                return false;
            }

            _liveGhost.name = _rightHand ? "SagElCanli" : "SolElCanli";
            _maps = BuildJointMaps(_livePuppet);
            _drivableSlots = BuildDrivableSlots();
            _tracked = new HandJointRotation[_drivableSlots.Length];
            // March start = flat bind hand; the curled idle pose may already sit inside a handle.
            _open = HandPoseLibrary.BindJointRotations(_rightHand);
            _target = new Quaternion[FingersMetadata.HAND_JOINT_IDS.Length];
            _clamped = new Quaternion[FingersMetadata.HAND_JOINT_IDS.Length];

            var capsuleRoot = new GameObject(_rightHand ? "SagElKapsul" : "SolElKapsul");
            capsuleRoot.transform.SetParent(parent, false);
            _capsuleRoot = capsuleRoot.transform;

            BuildControllerGhost(parent);
            Hide();
            return true;
        }

        /// <summary>Reads tracking, clamps the fingers to the item and moves every visual.</summary>
        public void Tick(IHand hand, GripCaptureItem item)
        {
            Valid = false;

            bool tracked = hand != null && hand.IsConnected && hand.IsTrackedDataValid &&
                           hand.IsHighConfidence;

            if (!tracked || item == null || !item.Placed ||
                !hand.GetRootPose(out Pose wrist) ||
                !hand.GetJointPosesLocal(out ReadOnlyHandJointPoses local) ||
                local.Count == 0)
            {
                Hide();
                return;
            }

            Wrist = wrist;
            ReadTracked(local);

            Quaternion[] target = HandPoseLibrary.BuildJointRotations(_tracked, _rightHand);
            Array.Copy(target, _target, Mathf.Min(target.Length, _target.Length));

            _liveGhost.SetActive(true);
            _livePuppet.SetRootPose(wrist);
            _livePuppet.SetJointRotations(_target);

            GripFingerClamp.Apply(_maps, _open, _target, _clamped, _touching, _penetrating, _segments);

            _livePuppet.SetJointRotations(_clamped);
            _livePuppet.SetRootPose(wrist);

            UpdateCapsules();
            PalmInside = ProbePalm(wrist);
            UpdateControllerGhost(item);

            Valid = true;
        }

        /// <summary>Hides the live visuals (tracking lost).</summary>
        public void Hide()
        {
            Valid = false;
            PalmInside = false;

            if (_liveGhost != null)
            {
                _liveGhost.SetActive(false);
            }

            if (_controllerGhost != null)
            {
                _controllerGhost.SetActive(false);
            }

            for (int i = 0; i < _capsules.Count; i++)
            {
                _capsules[i].gameObject.SetActive(false);
            }

            for (int i = 0; i < _touching.Length; i++)
            {
                _touching[i] = false;
                _penetrating[i] = false;
            }
        }

        /// <summary>
        /// Builds this frame's grip record for the item.
        /// <para>⚠️ The anchor's rotation is FORCED to the item's: the format carries no anchor
        /// rotation (in game the item rotation IS the anchor rotation), so recording the hand's
        /// natural controller tilt here would be silently dropped and the wrist offset would be
        /// measured against a frame that never exists in game.</para>
        /// </summary>
        public bool TryRecord(GripCaptureItem item, out ItemGripPose pose)
        {
            pose = default;
            if (!Valid || item == null || item.Root == null)
            {
                return false;
            }

            Pose anchorToWrist = HandPoseLibrary.AnchorToWrist(_rightHand);
            Quaternion naturalRotation = Wrist.rotation * Quaternion.Inverse(anchorToWrist.rotation);
            Vector3 anchorPosition = Wrist.position - naturalRotation * anchorToWrist.position;

            Quaternion toItem = Quaternion.Inverse(item.Rotation);

            // ⚠️ Not InverseTransformPoint: the record is unscaled metres and weapon roots are 0.8.
            Vector3 anchorInItem = toItem * (anchorPosition - item.Position);
            var wristInAnchor = new Pose(toItem * (Wrist.position - anchorPosition),
                toItem * Wrist.rotation);

            pose = ItemGripPose.From(anchorInItem, wristInAnchor, BuildRecordJoints());
            return true;
        }

        /// <summary>
        /// Replaces this slot's frozen ghost: a second puppet posed with <paramref name="joints"/>,
        /// parented to the item so it turns with it.
        /// </summary>
        public void CreateFrozenGhost(GripSocketKind kind, in Pose wrist, Quaternion[] joints,
            Transform itemRoot)
        {
            if (_frozen.TryGetValue(kind, out GameObject previous) && previous != null)
            {
                UnityEngine.Object.Destroy(previous);
            }

            _frozen.Remove(kind);

            GameObject ghost = InstantiateGhost(_parent, out HandPuppet puppet);
            if (ghost == null)
            {
                return;
            }

            ghost.name = $"{(_rightHand ? "SagEl" : "SolEl")}Donmus{kind}";
            puppet.SetRootPose(wrist);
            puppet.SetJointRotations(joints);

            // worldPositionStays keeps the world scale: the item root is scaled and the hand must not
            // inherit that scale.
            ghost.transform.SetParent(itemRoot, true);
            GripGhostMaterial.Apply(ghost, FrozenColour);

            _frozen[kind] = ghost;
        }

        private HandJointRotation[] BuildRecordJoints()
        {
            var joints = new HandJointRotation[_drivableSlots.Length];
            HandJointId[] ids = FingersMetadata.HAND_JOINT_IDS;

            for (int i = 0; i < _drivableSlots.Length; i++)
            {
                int slot = _drivableSlots[i];
                joints[i] = HandJointRotation.From(ids[slot].ToString(), _clamped[slot]);
            }

            return joints;
        }

        /// <summary>
        /// Converts the tracked parent-relative rotations into the record's convention.
        /// <para>⚠️ The proximal joints of index/middle/ring fold their metacarpal in: the record is
        /// WRIST space (what <c>SyntheticHand.AmendMetacarpalRotation</c> expects). The default
        /// skeleton's metacarpals are identity, so the ghost still draws it correctly.</para>
        /// </summary>
        private void ReadTracked(ReadOnlyHandJointPoses local)
        {
            HandJointId[] ids = FingersMetadata.HAND_JOINT_IDS;

            for (int i = 0; i < _drivableSlots.Length; i++)
            {
                int slot = _drivableSlots[i];
                HandJointId id = ids[slot];
                Quaternion rotation = local[id].rotation;

                if (id == HandJointId.HandIndex1 || id == HandJointId.HandMiddle1 ||
                    id == HandJointId.HandRing1)
                {
                    rotation = local[(HandJointId)((int)id - 1)].rotation * rotation;
                }

                _tracked[i] = HandJointRotation.From(id.ToString(), rotation);
            }
        }

        private GameObject InstantiateGhost(Transform parent, out HandPuppet puppet)
        {
            puppet = null;
            HandGhost prefab = _provider.GetHand(_rightHand ? Handedness.Right : Handedness.Left);
            if (prefab == null)
            {
                Debug.LogError("[GripCapture] Hayalet el prefab'ı sağlayıcıda yok.");
                return null;
            }

            HandGhost instance = UnityEngine.Object.Instantiate(prefab, parent);
            puppet = instance.GetComponent<HandPuppet>();
            if (puppet == null)
            {
                Debug.LogError("[GripCapture] Hayalet elde HandPuppet bulunamadı.");
                UnityEngine.Object.Destroy(instance.gameObject);
                return null;
            }

            // Scale 1: the in-game hand uses the default skeleton size, so a scaled ghost would
            // record a grip that does not fit the hand the player sees.
            puppet.Scale = 1f;
            return instance.gameObject;
        }

        private static HandJointMap[] BuildJointMaps(HandPuppet puppet)
        {
            HandJointId[] ids = FingersMetadata.HAND_JOINT_IDS;
            var maps = new HandJointMap[ids.Length];

            List<HandJointMap> jointMaps = puppet.JointMaps;
            for (int i = 0; i < ids.Length; i++)
            {
                for (int j = 0; j < jointMaps.Count; j++)
                {
                    if (jointMaps[j] != null && jointMaps[j].id == ids[i])
                    {
                        maps[i] = jointMaps[j];
                        break;
                    }
                }
            }

            return maps;
        }

        private static int[] BuildDrivableSlots()
        {
            HandJointId[] ids = FingersMetadata.HAND_JOINT_IDS;
            var slots = new List<int>(ids.Length);
            for (int i = 0; i < ids.Length; i++)
            {
                if (HandPoseLibrary.IsDrivable(ids[i]))
                {
                    slots.Add(i);
                }
            }

            return slots.ToArray();
        }

        private void UpdateCapsules()
        {
            while (_capsules.Count < _segments.Count)
            {
                var segment = new GameObject($"Kapsul{_capsules.Count}");
                segment.layer = HandLayer;
                segment.transform.SetParent(_capsuleRoot, false);
                var capsule = segment.AddComponent<CapsuleCollider>();
                capsule.isTrigger = true;
                capsule.direction = 1;
                _capsules.Add(capsule);
            }

            for (int i = 0; i < _capsules.Count; i++)
            {
                CapsuleCollider capsule = _capsules[i];
                if (i >= _segments.Count)
                {
                    capsule.gameObject.SetActive(false);
                    continue;
                }

                GripFingerClamp.Segment segment = _segments[i];
                Vector3 delta = segment.B - segment.A;
                float length = delta.magnitude;

                capsule.gameObject.SetActive(true);
                capsule.transform.position = (segment.A + segment.B) * 0.5f;
                capsule.transform.rotation = length > 1e-5f
                    ? Quaternion.FromToRotation(Vector3.up, delta)
                    : Quaternion.identity;

                capsule.radius = segment.Radius;
                capsule.height = length + segment.Radius * 2f;
            }
        }

        private bool ProbePalm(in Pose wrist)
        {
            int slot = FingersMetadata.HandJointIdToIndex(HandJointId.HandMiddle1);
            if (slot < 0 || slot >= _maps.Length || _maps[slot] == null ||
                _maps[slot].transform == null)
            {
                return false;
            }

            Vector3 centre = (wrist.position + _maps[slot].transform.position) * 0.5f;
            return Physics.CheckSphere(centre, PalmProbeRadius, ItemMask,
                QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// The semi-transparent controller model: it shows where the real controller sits in game for
        /// this pose — a grip that looks natural around it looks natural in game.
        /// </summary>
        private void BuildControllerGhost(Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ControllerPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[GripCapture] Kumanda prefab'ı yüklenemedi: {ControllerPrefabPath}");
                return;
            }

            string modelName = _rightHand ? "MetaQuestTouchPlus_Right" : "MetaQuestTouchPlus_Left";
            Transform model = prefab.transform.Find(modelName);
            if (model == null)
            {
                Debug.LogWarning($"[GripCapture] Kumanda modeli bulunamadı: {modelName}");
                return;
            }

            _controllerGhost = UnityEngine.Object.Instantiate(model.gameObject, parent);
            _controllerGhost.name = _rightHand ? "SagKumandaHayalet" : "SolKumandaHayalet";
            _controllerGhost.transform.localScale = model.localScale;
            GripCaptureItem.StripToRenderers(_controllerGhost);
            GripGhostMaterial.Apply(_controllerGhost, ControllerColour);
            _controllerGhost.SetActive(false);
        }

        private void UpdateControllerGhost(GripCaptureItem item)
        {
            if (_controllerGhost == null)
            {
                return;
            }

            Pose anchorToWrist = HandPoseLibrary.AnchorToWrist(_rightHand);
            Quaternion naturalRotation = Wrist.rotation * Quaternion.Inverse(anchorToWrist.rotation);
            Vector3 anchorPosition = Wrist.position - naturalRotation * anchorToWrist.position;

            _controllerGhost.SetActive(true);
            _controllerGhost.transform.SetPositionAndRotation(anchorPosition, item.Rotation);
        }
    }
}
