using System;
using UnityEngine;
using VortexArena.Protocol;

namespace VortexArena.Net
{
    /// <summary>A server-authoritative network object (§10.10): identity from <see cref="NetIdentity"/>
    /// (or, for a dynamic one, from the server through <see cref="BindDynamicId"/>), rules from
    /// <see cref="NetObjectKind"/>, state (<c>hp</c>/<c>flags</c>/<c>owner</c>/<c>stage</c>/resting pose)
    /// ONLY from the server.
    /// <para>⚠️ <b>No presentation here</b> — closing a collider, swapping to the broken mesh, attaching
    /// the object to a hand, moving it to the resting pose are the consumer's job through
    /// <see cref="StateChanged"/> / <see cref="OwnerChanged"/>. This component is the state cache and
    /// nothing else. Its <see cref="NetStateOrigin"/> tells the consumer whether it may play effects
    /// (live change) or must stay silent (snapshot).</para>
    /// <para>The client never writes state: a hit only produces a <c>hit_report</c>
    /// (<c>ArenaCombat.ReportObjectHit</c>), a grab only an <c>object_grab</c>; the decision is the
    /// server's.</para></summary>
    [RequireComponent(typeof(NetIdentity))]
    [DisallowMultipleComponent]
    public sealed class NetObject : MonoBehaviour
    {
        /// <summary>§10.10 core bit contract: bit1 = broken. ⚠️ bit0 (held), bit2 (physics awake) and
        /// bit3 (held by the right hand) are the rest of the core contract and are never renumbered —
        /// a shift does not fail, it draws the wrong object broken.</summary>
        public const int FLAG_BROKEN = ArenaProtocol.OBJECT_FLAG_BROKEN;

        [Tooltip("Tür kuralları (tel kimliği + azami can + kavrama/olay kuralı). Boş bırakılamaz.")]
        [SerializeField] private NetObjectKind kind;

        [Tooltip("Kimliği çalışma zamanında sunucu verir (object_spawn ile doğan prefab). " +
                 "İşaretliyse sahne bake'i beklenmez.")]
        [SerializeField] private bool dynamicId;

        /// <summary>Network id; <c>0</c> when the object is not addressable yet (see <see cref="Awake"/>
        /// and <see cref="BindDynamicId"/>).</summary>
        public int NetId { get; private set; }

        public NetObjectKind Kind => kind;

        public float Hp { get; private set; }

        public float MaxHp { get; private set; }

        public int Flags { get; private set; }

        /// <summary>The <c>playerId</c> holding the object; <c>0</c> = nobody (§10.10).</summary>
        public int Owner { get; private set; }

        /// <summary>Kind-specific stage (doneness, fill level); <c>0</c> = initial in every kind. Only
        /// <see cref="Kind"/> gives it meaning — the network layer does not interpret it.</summary>
        public int Stage { get; private set; }

        /// <summary>Mode-defined per-instance text (§10.10); the network layer never interprets it.
        /// Carries what <see cref="Stage"/> cannot (a customer's order) and empty is normal.</summary>
        public string Payload { get; private set; } = "";

        public bool IsBroken => (Flags & ArenaProtocol.OBJECT_FLAG_BROKEN) != 0;

        /// <summary>Is the object in someone's hand (bit0)? Its pose then comes from the owner's hand and
        /// <see cref="RestPosition"/>/<see cref="RestRotation"/> must NOT be read (§10.10).</summary>
        public bool IsHeld => (Flags & ArenaProtocol.OBJECT_FLAG_HELD) != 0;

        /// <summary>Which hand holds it (bit3); meaningless on its own — only while <see cref="IsHeld"/>.</summary>
        public bool HeldByRightHand => (Flags & ArenaProtocol.OBJECT_FLAG_HELD_RIGHT) != 0;

        /// <summary>Is the object moving (bit2)? The owner streams <c>0x09</c> poses while it is
        /// (§6.12), everyone else interpolates them.</summary>
        public bool IsAwake => (Flags & ArenaProtocol.OBJECT_FLAG_AWAKE) != 0;

        /// <summary>Am I the owner — the single question the grab bridge and the pose sender ask.
        /// <c>false</c> while nobody owns it or before we have a <c>playerId</c>.</summary>
        public bool IsMine
        {
            get
            {
                ArenaClient client = ArenaClient.Instance;
                return Owner > 0 && client != null && client.PlayerId == Owner;
            }
        }

        /// <summary>⚠️ The resting pose is in <b>ARENA space</b> (§3) — the world conversion is the
        /// consumer's (<c>ArenaSpace</c> is not visible from this asmdef). <see cref="HasRestPose"/> is
        /// false while the server sends none: an object that never moved has its pose in the scene
        /// already and must not be teleported to the origin.</summary>
        public Vector3 RestPosition { get; private set; }

        /// <inheritdoc cref="RestPosition"/>
        public Quaternion RestRotation { get; private set; } = Quaternion.identity;

        /// <inheritdoc cref="RestPosition"/>
        public bool HasRestPose { get; private set; }

        /// <summary>State arrived from the server. THE single hook for presentation (breakable cover,
        /// target board, resting pose); raised only on a real value change.</summary>
        public event System.Action<NetObject, NetStateOrigin> StateChanged;

        /// <summary>Ownership changed; the argument is the PREVIOUS owner (<c>0</c> = nobody).
        /// <para>Raised BEFORE <see cref="StateChanged"/> so the grab bridge can undo an optimistic local
        /// grab ("the owner is no longer me", §10.10) before presentation reacts to the same message.</para></summary>
        public event System.Action<NetObject, int> OwnerChanged;

        /// <summary>A cosmetic <c>object_event</c> relayed by the server for THIS object (§10.10). A
        /// state-changing event never arrives here — its result is <c>object_state</c>.</summary>
        public event System.Action<ObjectEventMsg> EventReceived;

        /// <summary>False = not registered, the object is invisible to the network (bad bake / no kind).</summary>
        private bool _registered;

        private void Awake()
        {
            var identity = GetComponent<NetIdentity>();
            long sceneId = identity != null ? identity.SceneId : 0L;
            bool idOk = sceneId >= ArenaProtocol.NET_ID_SCENE_MIN && sceneId <= ArenaProtocol.NET_ID_SCENE_MAX;

            if (kind == null)
            {
                // Loud on purpose: a silently network-invisible object is diagnosed in the field as
                // "the object does not break", days later.
                Debug.LogError($"[NetObject] '{name}' için NetObjectKind atanmamış — obje ağa " +
                               "kaydedilmedi, sunucudan gelen durumu almayacak.", this);
            }

            if (!idOk && !dynamicId)
            {
                Debug.LogError($"[NetObject] '{name}' sahne kimliği geçersiz ({sceneId}); geçerli aralık " +
                               $"{ArenaProtocol.NET_ID_SCENE_MIN}..{ArenaProtocol.NET_ID_SCENE_MAX}. " +
                               "GameObject > VortexArena > Network Parent ile bake edin — obje ağa kaydedilmedi.",
                    this);
            }

            NetId = idOk && !dynamicId ? (int)sceneId : 0;
            MaxHp = kind != null ? kind.MaxHp : 0f;

            // Intact until world_state arrives: a scene is drawn whole, the server corrects it.
            Hp = MaxHp;
            Flags = 0;
            Owner = 0;
            Stage = 0;
            Payload = "";
            HasRestPose = false;
        }

        private void OnEnable()
        {
            if (NetId <= 0 || kind == null)
            {
                return;
            }

            NetObjectRegistry.Register(NetId, this);
            _registered = true;
        }

        private void OnDisable()
        {
            if (!_registered)
            {
                return;
            }

            NetObjectRegistry.Unregister(NetId, this);
            _registered = false;
        }

        /// <summary>Gives a spawned instance the id the SERVER allocated (§10.10) and puts it into the
        /// same lookup as the scene objects — the spawner calls it right after Instantiate.
        /// <para>⚠️ Only the dynamic range is accepted: a scene id arriving here would collide with a
        /// baked object and the two would share one state.</para></summary>
        /// <returns>False when the id is out of range or the object is already bound.</returns>
        public bool BindDynamicId(int netId)
        {
            if (netId < ArenaProtocol.NET_ID_DYNAMIC_MIN || netId > ArenaProtocol.NET_ID_DYNAMIC_MAX)
            {
                Debug.LogError($"[NetObject] '{name}' dinamik kimliği geçersiz ({netId}); geçerli aralık " +
                               $"{ArenaProtocol.NET_ID_DYNAMIC_MIN}..{ArenaProtocol.NET_ID_DYNAMIC_MAX}.", this);
                return false;
            }

            if (NetId > 0)
            {
                Debug.LogError($"[NetObject] '{name}' zaten {NetId} kimliğine bağlı; {netId} yazılmadı.", this);
                return false;
            }

            if (kind == null)
            {
                Debug.LogError($"[NetObject] '{name}' için NetObjectKind atanmamış — dinamik obje ağa " +
                               "kaydedilmedi.", this);
                return false;
            }

            NetId = netId;
            NetObjectRegistry.Register(NetId, this);
            _registered = true;
            return true;
        }

        /// <summary>Does the kind's named flag hold (§10.10 bit4+)? False when the kind does not name it —
        /// presentation reads bits by NAME, never by number.</summary>
        public bool HasKindFlag(string flagName)
        {
            return kind != null && kind.TryGetFlagMask(flagName, out int mask) && (Flags & mask) != 0;
        }

        /// <summary>Reads one value out of the <c>k:v;k:v</c> shaped <see cref="Payload"/>
        /// (<c>"slot:1;r:bun_bottom,patty,bun_top"</c>). The value is everything after the FIRST colon,
        /// so a value may contain colons itself.
        /// <para>⚠️ Tolerant on purpose: a partial or malformed payload yields <c>false</c>, never an
        /// exception — the mode owns the format and may gain keys later.</para></summary>
        public bool TryGetPayloadValue(string key, out string value)
        {
            value = "";

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(Payload))
            {
                return false;
            }

            string[] tokens = Payload.Split(';');
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];
                int separator = token.IndexOf(':');
                if (separator <= 0)
                {
                    continue;
                }

                if (!string.Equals(token.Substring(0, separator).Trim(), key, StringComparison.Ordinal))
                {
                    continue;
                }

                value = token.Substring(separator + 1).Trim();
                return true;
            }

            return false;
        }

        /// <summary>Health ratio for the damage visual; <c>1</c> for a kind that takes no damage.</summary>
        public float HealthRatio => MaxHp > 0f ? Mathf.Clamp01(Hp / MaxHp) : 1f;

        /// <summary>Writes server state; raises the events only if something changed.</summary>
        /// <param name="restPosition">Resting pose in ARENA space; ignored when
        /// <paramref name="hasRestPose"/> is false.</param>
        internal void ApplyState(float hp, int flags, int owner, int stage, string payload,
            Vector3 restPosition, Quaternion restRotation, bool hasRestPose, NetStateOrigin origin)
        {
            bool poseChanged = hasRestPose &&
                               (!HasRestPose || RestPosition != restPosition || RestRotation != restRotation);

            payload ??= "";

            if (Mathf.Approximately(Hp, hp) && Flags == flags && Owner == owner && Stage == stage &&
                string.Equals(Payload, payload, StringComparison.Ordinal) && !poseChanged)
            {
                return;
            }

            int previousOwner = Owner;

            Hp = hp;
            Flags = flags;
            Owner = owner;
            Stage = stage;
            Payload = payload;

            if (hasRestPose)
            {
                RestPosition = restPosition;
                RestRotation = restRotation;
                HasRestPose = true;
            }

            if (previousOwner != owner)
            {
                OwnerChanged?.Invoke(this, previousOwner);
            }

            StateChanged?.Invoke(this, origin);
        }

        /// <summary>Publishes a relayed cosmetic event on this object.</summary>
        internal void RaiseEvent(ObjectEventMsg msg)
        {
            EventReceived?.Invoke(msg);
        }
    }
}
