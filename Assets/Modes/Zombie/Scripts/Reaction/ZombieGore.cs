using System.Collections.Generic;
using UnityEngine;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The dismemberment of ONE zombie body: which segment a round struck, taking it off at
    /// that segment's root joint, the stump cap, the gibs and the blood that goes with it.</summary>
    /// <remarks>Driven by <see cref="ZombieView"/> (not a component): everything it needs — look,
    /// ragdoll, type — is resolved there, and a component would have to be wired onto every look prefab
    /// by the builder.
    /// <para>⚠️ A limb comes off by SCALING its bone to nothing: the body is one skinned mesh, so the
    /// vertices weighted to that bone collapse into the joint and the stump cap hides the hole. Nothing
    /// is cut, which is why it costs no mesh work on Quest.</para>
    /// <para>⚠️ The chain's rigidbodies and joints are DESTROYED and its colliders disabled BEFORE the
    /// scale goes to zero: PhysX reports a degenerate collider as an error per frame and a joint to a
    /// zero-scale body shakes the whole corpse.</para>
    /// <para>⚠️ The scale is rewritten every <c>LateUpdate</c> (<see cref="Tick"/>): the Animator and
    /// the humanoid retarget may write bone scale, and a head that grows back mid-fall is worse than
    /// no head pop at all.</para></remarks>
    internal class ZombieGore
    {
        /// <summary>Not exactly zero: a zero-scale bone makes the skinning normals NaN, and the whole
        /// body then renders black.</summary>
        private const float Tiny = 0.0001f;

        /// <summary>One cut: the bone that was collapsed, the scale it had and the cap hiding the
        /// hole.</summary>
        private struct CutEntry
        {
            public Transform Bone;
            public Vector3 Scale;
            public Transform Cap;
        }

        private readonly ZombieView _view;
        private readonly ZombieLook _look;
        private readonly ZombieRagdoll _ragdoll;
        private readonly Transform _root;
        private readonly ZombieReactionTuning _tuning;
        private readonly int _netId;

        /// <summary>The Dev is never dismembered: its end is staged, and a boss losing an arm to a
        /// stray round undercuts the fight.</summary>
        private readonly bool _allowed;

        private readonly List<CutEntry> _cuts = new List<CutEntry>(3);

        /// <summary>Which limbs are already off — indexed by <see cref="ZombieLimb"/>.</summary>
        private readonly bool[] _gone = new bool[6];

        private bool _headGone;

        public ZombieGore(ZombieView view, ZombieLook look, ZombieRagdoll ragdoll, Transform root,
            int netId, bool allowed)
        {
            _view = view;
            _look = look;
            _ragdoll = ragdoll;
            _root = root;
            _netId = netId;
            _allowed = allowed;
            _tuning = ZombieReactionTuning.Live;
        }

        public bool HeadGone => _headGone;

        /// <summary>Anything came off this body.</summary>
        public bool AnyCut => _cuts.Count > 0;

        /// <summary>Keeps the collapsed bones collapsed. Called from <c>LateUpdate</c>, i.e. after the
        /// Animator wrote the pose.</summary>
        public void Tick()
        {
            for (int i = 0; i < _cuts.Count; i++)
            {
                Transform bone = _cuts[i].Bone;
                if (bone != null)
                {
                    bone.localScale = new Vector3(Tiny, Tiny, Tiny);
                }
            }
        }

        /// <summary>Puts every cut bone back and hides its cap — the local kill prediction was wrong and
        /// the body stands up again.</summary>
        public void Restore()
        {
            for (int i = 0; i < _cuts.Count; i++)
            {
                CutEntry cut = _cuts[i];

                if (cut.Bone != null)
                {
                    cut.Bone.localScale = cut.Scale;

                    if (_ragdoll != null)
                    {
                        _ragdoll.EnableChainColliders(cut.Bone);
                    }
                }

                if (cut.Cap != null)
                {
                    cut.Cap.gameObject.SetActive(false);
                }
            }

            _cuts.Clear();
            System.Array.Clear(_gone, 0, _gone.Length);
            _headGone = false;
        }

        // ------------------------------------------------------------------- entry points

        /// <summary>HEAD POP: always on a lethal head hit, never on the Dev.</summary>
        public ZombieLimb PopHead(Vector3 point, Vector3 direction, bool byLocal)
        {
            Transform head = _look != null ? _look.HeadBone : null;
            if (!_allowed || _headGone || head == null)
            {
                return ZombieLimb.None;
            }

            if (!Cut(head, ZombieSegment.Neck, ZombieLimb.Head, point, direction, byLocal,
                    _tuning.GibsPerHeadPop, true))
            {
                return ZombieLimb.None;
            }

            _headGone = true;
            return ZombieLimb.Head;
        }

        /// <summary>Takes the limb the round struck off at that SEGMENT's root joint (forearm → elbow,
        /// upper arm → shoulder, shin → knee, thigh → hip). Torso hits sever nothing.</summary>
        public ZombieLimb SeverAt(Collider hitbox, Vector3 point, Vector3 direction, bool byLocal)
        {
            if (!_allowed || hitbox == null)
            {
                return ZombieLimb.None;
            }

            ZombieSegment segment = SegmentOf(hitbox.name, out ZombieLimb limb);
            if (segment == ZombieSegment.None || segment == ZombieSegment.Torso)
            {
                return ZombieLimb.None;
            }

            if (segment == ZombieSegment.Neck)
            {
                return PopHead(point, direction, byLocal);
            }

            return Cut(hitbox.transform.parent, segment, limb, point, direction, byLocal,
                _tuning.GibsPerSever, false)
                ? limb
                : ZombieLimb.None;
        }

        /// <summary>Crippling: takes ONE leg off at the knee. The side is the leg hit box nearest
        /// <paramref name="hintPoint"/>; with no hint (late joiner, admin) the <c>netId</c>'s parity
        /// decides, so every headset that has no point at all still picks the same leg.</summary>
        public ZombieLimb SeverLegAtKnee(Vector3 hintPoint, bool hasHint, Vector3 direction, bool byLocal)
        {
            if (!_allowed)
            {
                return ZombieLimb.None;
            }

            bool left = hasHint ? NearestLegIsLeft(hintPoint) : (_netId & 1) == 0;

            Collider shin = FindHitbox(left ? "Hit_Leg_L" : "Hit_Leg_R");
            if (shin == null)
            {
                return ZombieLimb.None;
            }

            ZombieLimb limb = left ? ZombieLimb.LeftLeg : ZombieLimb.RightLeg;
            Vector3 point = hasHint ? hintPoint : shin.bounds.center;

            return Cut(shin.transform.parent, ZombieSegment.Shin, limb, point, direction, byLocal,
                _tuning.GibsPerSever, false)
                ? limb
                : ZombieLimb.None;
        }

        // ------------------------------------------------------------------- the cut itself

        private bool Cut(Transform bone, ZombieSegment segment, ZombieLimb limb, Vector3 point,
            Vector3 direction, bool byLocal, int gibs, bool skull)
        {
            int index = (int)limb;
            if (bone == null || index <= 0 || index >= _gone.Length || _gone[index])
            {
                return false;
            }

            for (int i = 0; i < _cuts.Count; i++)
            {
                // Already cut, or inside something already cut: a second pass would spawn a cap in mid-air.
                if (_cuts[i].Bone == bone || bone.IsChildOf(_cuts[i].Bone))
                {
                    return false;
                }
            }

            Transform parent = bone.parent;

            // Outward = along the limb, away from the body: the cap's +Y and the gib spray both use it.
            Vector3 outward = parent != null ? bone.position - parent.position : Vector3.zero;
            if (outward.sqrMagnitude < 1e-8f)
            {
                outward = _root != null ? _root.up : Vector3.up;
            }

            outward.Normalize();

            // ⚠️ Physics first, scale second (see the class remarks).
            if (_ragdoll != null)
            {
                _ragdoll.DetachChain(bone);
            }

            var cut = new CutEntry
            {
                Bone = bone,
                Scale = bone.localScale,
                Cap = BuildCap(bone, parent, segment, outward),
            };

            bone.localScale = new Vector3(Tiny, Tiny, Tiny);
            _cuts.Add(cut);
            _gone[index] = true;

            ZombieGibs.Shared.Burst(point, outward, gibs, skull);

            ZombieBurstFx.Shared.Play(
                skull ? _tuning.HeadBurstPrefab : _tuning.SeverBurstPrefab, point, outward);

            ZombieBloodDecals.Shared.SplatOnFloor(bone.position);

            if (_view != null)
            {
                _view.PlayGore(skull ? _tuning.RandomHeadPop() : _tuning.RandomLimbSever());

                // Layered on top, not sequenced: the wet spray belongs to the same instant as the cut,
                // and there is no scheduler here to delay it with.
                _view.PlayGore(_tuning.RandomGibSplat());
            }

            ZombieFeelEvents.RaiseDismember(
                new ZombieDismemberPresentation(_netId, limb, point, byLocal));

            return true;
        }

        /// <summary>The cap that closes the hole. ⚠️ Parented to the bone's PARENT, never to the bone
        /// itself: that one is about to be scaled to nothing and would take the cap with it.</summary>
        private Transform BuildCap(Transform bone, Transform parent, ZombieSegment segment, Vector3 outward)
        {
            GameObject prefab = segment == ZombieSegment.Neck
                ? _tuning.NeckStumpPrefab
                : _tuning.LimbStumpPrefab;

            if (prefab == null || parent == null)
            {
                return null;
            }

            GameObject instance = Object.Instantiate(prefab, parent);
            instance.name = "[Stump]";

            // +Y out of the cut; the neck cap's front follows the body's forward (its mesh is not
            // rotationally symmetric, the limb caps are).
            Vector3 forward = _root != null ? _root.forward : Vector3.forward;
            forward -= outward * Vector3.Dot(forward, outward);
            Quaternion rotation = forward.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(forward.normalized, outward)
                : Quaternion.FromToRotation(Vector3.up, outward);

            instance.transform.SetPositionAndRotation(bone.position, rotation);

            // The cap is authored at StumpMeshRadius in metres; the bone carries the rig's own scale.
            float boneScale = Mathf.Abs(parent.lossyScale.x) > 0.0001f ? parent.lossyScale.x : 1f;
            float wanted = _tuning.StumpRadiusOf(segment) / _tuning.StumpMeshRadius;
            instance.transform.localScale = Vector3.one * (wanted / boneScale);

            Remap(instance);
            return instance.transform;
        }

        /// <summary>Puts the tuning asset's two gore materials on the cap: the model's slots are named
        /// <c>Flesh</c>/<c>Bone</c>.</summary>
        private void Remap(GameObject instance)
        {
            MeshRenderer[] renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                MeshRenderer renderer = renderers[r];
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                Material[] authored = renderer.sharedMaterials;
                var materials = new Material[authored.Length];

                for (int i = 0; i < authored.Length; i++)
                {
                    bool bone = authored[i] != null && authored[i].name.IndexOf(
                        "Bone", System.StringComparison.OrdinalIgnoreCase) >= 0;

                    materials[i] = bone && _tuning.BoneMaterial != null
                        ? _tuning.BoneMaterial
                        : _tuning.FleshMaterial;
                }

                renderer.sharedMaterials = materials;
            }
        }

        // ------------------------------------------------------------------- hit box lookup

        /// <summary>Which segment a hit box belongs to. ⚠️ Matched on the NAMES
        /// <c>ZombieLookBuilder</c> writes — renaming a box there silently stops the dismemberment.</summary>
        public static ZombieSegment SegmentOf(string hitboxName, out ZombieLimb limb)
        {
            limb = ZombieLimb.None;

            if (string.IsNullOrEmpty(hitboxName))
            {
                return ZombieSegment.None;
            }

            if (hitboxName.Contains("Head"))
            {
                limb = ZombieLimb.Head;
                return ZombieSegment.Neck;
            }

            bool left = hitboxName.EndsWith("_L");

            if (hitboxName.Contains("UpperArm"))
            {
                limb = left ? ZombieLimb.LeftArm : ZombieLimb.RightArm;
                return ZombieSegment.UpperArm;
            }

            if (hitboxName.Contains("ForeArm"))
            {
                limb = left ? ZombieLimb.LeftArm : ZombieLimb.RightArm;
                return ZombieSegment.ForeArm;
            }

            if (hitboxName.Contains("UpLeg"))
            {
                limb = left ? ZombieLimb.LeftLeg : ZombieLimb.RightLeg;
                return ZombieSegment.Thigh;
            }

            // ⚠️ After UpLeg: "Hit_UpLeg_L" contains "Leg" too.
            if (hitboxName.Contains("Leg"))
            {
                limb = left ? ZombieLimb.LeftLeg : ZombieLimb.RightLeg;
                return ZombieSegment.Shin;
            }

            return ZombieSegment.Torso;
        }

        /// <summary>The body's own hit box nearest a world point — the fallback when the shot was
        /// relayed and no collider came with it.</summary>
        public Collider NearestHitbox(Vector3 point)
        {
            Collider[] parts = _ragdoll != null ? _ragdoll.Colliders : null;
            if (parts == null)
            {
                return null;
            }

            Collider best = null;
            float nearest = float.MaxValue;

            for (int i = 0; i < parts.Length; i++)
            {
                Collider part = parts[i];
                if (part == null || !part.enabled)
                {
                    continue;
                }

                float distance = (part.bounds.ClosestPoint(point) - point).sqrMagnitude;
                if (distance < nearest)
                {
                    nearest = distance;
                    best = part;
                }
            }

            return best;
        }

        private Collider FindHitbox(string name)
        {
            Collider[] parts = _ragdoll != null ? _ragdoll.Colliders : null;
            if (parts == null)
            {
                return null;
            }

            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] != null && parts[i].name == name)
                {
                    return parts[i];
                }
            }

            return null;
        }

        private bool NearestLegIsLeft(Vector3 point)
        {
            float left = DistanceTo("Hit_Leg_L", point);
            float leftUp = DistanceTo("Hit_UpLeg_L", point);
            float right = DistanceTo("Hit_Leg_R", point);
            float rightUp = DistanceTo("Hit_UpLeg_R", point);

            return Mathf.Min(left, leftUp) <= Mathf.Min(right, rightUp);
        }

        private float DistanceTo(string name, Vector3 point)
        {
            Collider part = FindHitbox(name);
            return part != null ? (part.bounds.ClosestPoint(point) - point).sqrMagnitude : float.MaxValue;
        }
    }
}
