using UnityEngine;

namespace VortexArena.Modes.Zombie
{
    /// <summary>The ragdoll of ONE zombie body: the bone rigidbodies, their colliders (the hit boxes
    /// themselves) and the budget that decides whether this corpse may be simulated at all.</summary>
    /// <remarks>⚠️ Everything here is written by <c>ZombieLookBuilder</c> — a hand edit on a look
    /// prefab is lost on the next run of the tool.
    /// <para>⚠️ <b>The hit boxes ARE the ragdoll colliders.</b> A second collider set would double the
    /// skinned body's physics cost and the two would disagree about where the zombie is.</para>
    /// <para>⚠️ The budget is a HARD cap: a wave can collapse thirty bodies in one frame (a Dev kill
    /// does exactly that) and thirty eleven-body ragdolls stall the headset. Over the cap the view
    /// falls back to the animated death.</para></remarks>
    [DisallowMultipleComponent]
    public class ZombieRagdoll : MonoBehaviour
    {
        /// <summary>Physics layer the corpse moves to while it falls. ⚠️ Mirrors the layer the shot
        /// raycast excludes (<c>ArenaLayers.ZombieRagdoll</c>): resolved by NAME here so this module
        /// does not depend on the constant's assembly.</summary>
        public const string LayerName = "ZombieRagdoll";

        /// <summary>Simultaneously simulated corpses on this headset.</summary>
        private const int MaxActive = 8;

        private static int s_active;

        // -2 = not looked up yet; -1 = the layer does not exist.
        private static int s_layer = -2;

        [Header("Bileşenler")]
        [SerializeField] private Animator animator;
        [SerializeField] private Transform hips;

        [Tooltip("Kemik rigidbody'leri — ZombieLookBuilder yazar.")]
        [SerializeField] private Rigidbody[] bodies;

        [Tooltip("Vuruş kutularının collider'ları: aynı anda ragdoll gövdesi.")]
        [SerializeField] private Collider[] colliders;

        private bool _collapsed;
        private bool _counted;

        /// <summary>Hit box layers before <see cref="Collapse"/> moved them — the local kill prediction
        /// can be wrong, and the body then has to become a target again.</summary>
        private int[] _layersBefore;

        public Transform Hips => hips;

        /// <summary>This body's own hit-box colliders — the blood raycast is restricted to them.</summary>
        public Collider[] Colliders => colliders;

        /// <summary>Would a collapse be allowed right now (budget + a usable layer).</summary>
        public static bool HasBudget => s_active < MaxActive && ResolveLayer() >= 0;

        // Statics survive a disabled domain reload: without this the budget stays spent after the
        // first play session and no corpse ever ragdolls again.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_active = 0;
            s_layer = -2;
        }

        /// <summary>Hands the body over to physics and pushes it along the shot. <c>false</c> = the
        /// budget is spent (or the body was never built): the caller must play the animated death.</summary>
        /// <param name="pointWorld">Where the killing round landed — the impulse goes into the nearest
        /// body, so a headshot throws the head and a leg shot sweeps the leg.</param>
        /// <param name="directionWorld">Shot direction, unit length.</param>
        /// <param name="impulse">Total impulse (N·s).</param>
        public bool Collapse(Vector3 pointWorld, Vector3 directionWorld, float impulse)
        {
            if (_collapsed)
            {
                return true;
            }

            if (bodies == null || bodies.Length == 0)
            {
                return false;
            }

            int layer = ResolveLayer();
            if (layer < 0 || s_active >= MaxActive)
            {
                return false;
            }

            _collapsed = true;
            _counted = true;
            s_active++;

            // ⚠️ The Animator goes first: it writes bone transforms every frame and would fight the
            // solver into a twitching heap.
            if (animator != null)
            {
                animator.enabled = false;
            }

            if (colliders != null)
            {
                _layersBefore ??= new int[colliders.Length];

                for (int i = 0; i < colliders.Length; i++)
                {
                    if (colliders[i] != null)
                    {
                        _layersBefore[i] = colliders[i].gameObject.layer;
                        colliders[i].gameObject.layer = layer;
                    }
                }
            }

            Rigidbody nearest = null;
            float best = float.MaxValue;

            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody body = bodies[i];
                if (body == null)
                {
                    continue;
                }

                body.isKinematic = false;

                // Cleared explicitly: a kinematic body keeps the velocity it was last given, and an
                // animated corpse would launch on the pose delta of its final frame.
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;

                float distance = (body.worldCenterOfMass - pointWorld).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    nearest = body;
                }
            }

            if (nearest != null && impulse > 0f && directionWorld.sqrMagnitude > 1e-6f)
            {
                nearest.AddForceAtPosition(directionWorld.normalized * impulse, pointWorld,
                    ForceMode.Impulse);
            }

            return true;
        }

        /// <summary>Takes the corpse back: the body stands up again because the LOCAL kill prediction
        /// was wrong (the server still reports it alive). The Animator owns the pose from here on.</summary>
        /// <remarks>⚠️ A chain already severed does NOT come back to physics (its rigidbodies were
        /// destroyed) — only its hit boxes do, so the limb can be shot again.</remarks>
        public void Restore()
        {
            if (!_collapsed)
            {
                return;
            }

            _collapsed = false;

            if (bodies != null)
            {
                for (int i = 0; i < bodies.Length; i++)
                {
                    if (bodies[i] != null)
                    {
                        bodies[i].linearVelocity = Vector3.zero;
                        bodies[i].angularVelocity = Vector3.zero;
                        bodies[i].isKinematic = true;
                    }
                }
            }

            if (colliders != null && _layersBefore != null)
            {
                for (int i = 0; i < colliders.Length && i < _layersBefore.Length; i++)
                {
                    if (colliders[i] != null)
                    {
                        colliders[i].gameObject.layer = _layersBefore[i];
                    }
                }
            }

            if (animator != null)
            {
                animator.enabled = true;
            }

            Release();
        }

        /// <summary>Takes a severed chain out of physics: its joints and rigidbodies are DESTROYED and
        /// its hit boxes disabled. ⚠️ Called before the bone is scaled to nothing — PhysX reports a
        /// degenerate collider every frame, and a joint to a zero-scale body shakes the corpse.</summary>
        public void DetachChain(Transform chainRoot)
        {
            if (chainRoot == null)
            {
                return;
            }

            if (colliders != null)
            {
                for (int i = 0; i < colliders.Length; i++)
                {
                    Collider part = colliders[i];
                    if (part != null && part.transform.IsChildOf(chainRoot))
                    {
                        part.enabled = false;
                    }
                }
            }

            if (bodies == null)
            {
                return;
            }

            // Joints first: a joint whose connected body is already gone logs an error.
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody body = bodies[i];
                if (body == null || !body.transform.IsChildOf(chainRoot))
                {
                    continue;
                }

                if (body.TryGetComponent(out CharacterJoint joint))
                {
                    Destroy(joint);
                }
            }

            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody body = bodies[i];
                if (body == null || !body.transform.IsChildOf(chainRoot))
                {
                    continue;
                }

                // ⚠️ The slot is cleared NOW, not when the Destroy lands at the end of the frame: the
                // sever and the collapse happen in the SAME frame on a lethal hit, and a doomed body
                // would otherwise be the one the killing impulse is spent on.
                bodies[i] = null;
                Destroy(body);
            }
        }

        /// <summary>Re-enables a detached chain's hit boxes (prediction rollback).</summary>
        public void EnableChainColliders(Transform chainRoot)
        {
            if (chainRoot == null || colliders == null)
            {
                return;
            }

            for (int i = 0; i < colliders.Length; i++)
            {
                Collider part = colliders[i];
                if (part != null && part.transform.IsChildOf(chainRoot))
                {
                    part.enabled = true;
                }
            }
        }

        /// <summary>Stops the simulation and gives the budget back — the dissolve then runs on a
        /// settled pose instead of a body still sliding.</summary>
        public void Freeze()
        {
            if (bodies != null)
            {
                for (int i = 0; i < bodies.Length; i++)
                {
                    if (bodies[i] != null && !bodies[i].isKinematic)
                    {
                        bodies[i].linearVelocity = Vector3.zero;
                        bodies[i].angularVelocity = Vector3.zero;
                        bodies[i].isKinematic = true;
                    }
                }
            }

            Release();
        }

        // The corpse is destroyed by NetObjectSpawner with no warning: the budget has to come back
        // here, or a few despawns leak it to zero and nothing ragdolls for the rest of the match.
        private void OnDisable()
        {
            Release();
        }

        private void Release()
        {
            if (!_counted)
            {
                return;
            }

            _counted = false;
            s_active = Mathf.Max(0, s_active - 1);
        }

        private static int ResolveLayer()
        {
            if (s_layer != -2)
            {
                return s_layer;
            }

            s_layer = LayerMask.NameToLayer(LayerName);
            if (s_layer < 0)
            {
                // An ERROR: without the layer a corpse would keep blocking rounds and colliding with
                // the living wave — the view falls back to the animated death instead.
                Debug.LogError($"[Zombie] '{LayerName}' layer'ı projede tanımlı değil — zombi " +
                               "cesetleri fizikle düşmüyor. Project Settings > Tags and Layers " +
                               "altında bu adla bir user layer açılmalı.");
            }

            return s_layer;
        }
    }
}
