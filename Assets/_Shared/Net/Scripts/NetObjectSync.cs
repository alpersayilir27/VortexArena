using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Protocol;

namespace VortexArena.Net
{
    /// <summary>Applies server-authoritative network object state (§10.10) to the scene and carries the
    /// client's object messages the other way: <c>object_state</c> to one object, <c>world_state</c> to
    /// all of them, <c>object_spawn</c>/<c>object_despawn</c> to the spawner as a request, and
    /// <c>object_grab</c>/<c>object_release</c>/<c>object_rest</c>/<c>object_event</c> out to the server.
    /// <para>Never placed in a scene (ArenaClient pattern): <c>world_state</c> can arrive BEFORE the
    /// scene loads, so it self-bootstraps and goes DontDestroyOnLoad.</para>
    /// <para>⚠️ <b>This class instantiates nothing:</b> a spawn is published as
    /// <see cref="SpawnRequested"/>, and the listener resolves the prefab from <c>kind</c> through
    /// <see cref="NetSpawnCatalog"/>, instantiates it and registers it with
    /// <see cref="RegisterSpawned"/>.</para></summary>
    public sealed class NetObjectSync : MonoBehaviour
    {
        public static NetObjectSync Instance { get; private set; }

        /// <summary>An <c>object_spawn</c> arrived (§10.10): the spawner resolves the prefab from
        /// <c>kind</c> (<see cref="NetSpawnCatalog"/>), instantiates it at <c>pos</c>/<c>rot</c>
        /// (ARENA space, §3) and registers it with
        /// <see cref="RegisterSpawned"/> <b>during this call</b>. Registering later loses the state that
        /// came with the spawn.</summary>
        public static event Action<ObjectStateMsg> SpawnRequested;

        /// <summary>An <c>object_despawn</c> arrived: the listener destroys the instance. Destroying is
        /// left to the spawner that created it — the two must stay in one place.</summary>
        public static event Action<int> DespawnRequested;

        /// <summary>A relayed cosmetic <c>object_event</c> (§10.10) with the object it belongs to. For
        /// per-object presentation use <see cref="NetObject.EventReceived"/>; this one is for listeners
        /// that are not on the object (the game mode).</summary>
        public static event Action<NetObject, ObjectEventMsg> ObjectEventReceived;

        // Reused outgoing DTOs (the ArenaCombat.ReportObjectHit pattern): no GC on the interaction path.
        // ⚠️ EVERY field is written on EVERY call — an unwritten one leaks the previous message's value.
        private static readonly ObjectGrabMsg GrabOut = new ObjectGrabMsg();
        private static readonly ObjectReleaseMsg ReleaseOut = new ObjectReleaseMsg { pos = new float[3], rot = new float[4] };
        private static readonly ObjectRestMsg RestOut = new ObjectRestMsg { pos = new float[3], rot = new float[4] };
        private static readonly ObjectEventMsg EventOut = new ObjectEventMsg();

        private static readonly int[] EmptyInts = Array.Empty<int>();
        private static readonly float[] EmptyFloats = Array.Empty<float>();

        /// <summary>Last <c>world_state</c> whose scene is not loaded yet (§5.3).
        /// <para>⚠️ It arrives before the scene load and the <c>netId</c>s have no counterpart yet;
        /// dropped instead of buffered, a late joiner would see broken covers intact. A message for
        /// ANOTHER scene is buffered too — it is applied once that scene loads — but a newer
        /// <c>world_state</c> always REPLACES the buffered one.</para></summary>
        private WorldStateMsg _pending;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
            {
                return;
            }

            var go = new GameObject("[NetObjectSync]");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<NetObjectSync>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Persistent singleton: subscribe in Awake/OnDestroy, not OnEnable/OnDisable, so server
            // events are not missed while the object is disabled.
            NetEvents.OnObjectState += HandleObjectState;
            NetEvents.OnObjectSpawn += HandleObjectSpawn;
            NetEvents.OnObjectDespawn += HandleObjectDespawn;
            NetEvents.OnObjectEvent += HandleObjectEvent;
            NetEvents.OnWorldState += HandleWorldState;
            NetEvents.OnDisconnected += HandleDisconnected;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            NetEvents.OnObjectState -= HandleObjectState;
            NetEvents.OnObjectSpawn -= HandleObjectSpawn;
            NetEvents.OnObjectDespawn -= HandleObjectDespawn;
            NetEvents.OnObjectEvent -= HandleObjectEvent;
            NetEvents.OnWorldState -= HandleWorldState;
            NetEvents.OnDisconnected -= HandleDisconnected;
            SceneManager.sceneLoaded -= HandleSceneLoaded;

            Instance = null;
        }

        // ------------------------------------------------------------- outgoing (§5.1)

        /// <summary>§5.1: asks for a network object (<c>object_grab</c>). <b>It has no reply</b> — the
        /// answer is the broadcast <c>object_state.owner</c>, so the caller grabs locally at once and
        /// undoes it from <see cref="NetObject.OwnerChanged"/> if the owner turns out to be someone else.
        /// <para>Silent no-op with no connection or a bad id.</para></summary>
        public static void SendGrab(int netId, bool rightHand)
        {
            ArenaClient client = ArenaClient.Instance;
            if (netId <= 0 || client == null || !client.IsConnected)
            {
                return;
            }

            GrabOut.netId = netId;
            GrabOut.hand = rightHand ? 1 : 0;
            client.Send(GrabOut);
        }

        /// <summary>§5.1: the object LEFT THE HAND (<c>object_release</c>); only the OWNER may send it.
        /// Server: <c>Held</c> falls, <c>Awake</c> rises, <b>ownership is KEPT</b> — the flight window
        /// begins and <see cref="UdpStateChannel.SendObjectPose"/> carries it.
        /// <para>⚠️ <b>Not the same moment as <see cref="SendRest"/></b>: one message for both would keep
        /// the wire saying "in hand" for the whole flight. The pose here is the release pose; the pose is
        /// in ARENA space — the world conversion is the caller's, this layer does not see
        /// <c>ArenaSpace</c>.</para></summary>
        public static void SendRelease(int netId, Vector3 arenaPosition, Quaternion arenaRotation)
        {
            ArenaClient client = ArenaClient.Instance;
            if (netId <= 0 || client == null || !client.IsConnected)
            {
                return;
            }

            ReleaseOut.netId = netId;
            ReleaseOut.pos[0] = arenaPosition.x;
            ReleaseOut.pos[1] = arenaPosition.y;
            ReleaseOut.pos[2] = arenaPosition.z;
            ReleaseOut.rot[0] = arenaRotation.x;
            ReleaseOut.rot[1] = arenaRotation.y;
            ReleaseOut.rot[2] = arenaRotation.z;
            ReleaseOut.rot[3] = arenaRotation.w;
            client.Send(ReleaseOut);
        }

        /// <summary>§5.1: the object STOPPED (<c>object_rest</c>); only the OWNER may send it. Server:
        /// <c>Awake</c> falls, <c>owner = 0</c>, and the pose becomes the table's RESTING pose.
        /// <para>⚠️ <b>Stopping is measured by the CLIENT</b> (<c>OBJECT_REST_SPEED</c> below
        /// <c>OBJECT_REST_SECONDS</c>) — the server has no physics and no metres, so it cannot tell when
        /// a thrown object came to rest. The pose is in ARENA space (the caller converts).</para></summary>
        public static void SendRest(int netId, Vector3 arenaPosition, Quaternion arenaRotation)
        {
            ArenaClient client = ArenaClient.Instance;
            if (netId <= 0 || client == null || !client.IsConnected)
            {
                return;
            }

            RestOut.netId = netId;
            RestOut.pos[0] = arenaPosition.x;
            RestOut.pos[1] = arenaPosition.y;
            RestOut.pos[2] = arenaPosition.z;
            RestOut.rot[0] = arenaRotation.x;
            RestOut.rot[1] = arenaRotation.y;
            RestOut.rot[2] = arenaRotation.z;
            RestOut.rot[3] = arenaRotation.w;
            client.Send(RestOut);
        }

        /// <summary>§5.1: raises an object-specific interaction (<c>object_event</c>). The arrays are the
        /// KIND's own contract; <paramref name="name"/> is validated on the server against
        /// <c>kinds[].events[]</c> and silently rejected when missing.
        /// <para>⚠️ <b>Not for unreliable/cosmetic noise</b> (a bounce sound, sparks) — that channel is
        /// UDP <c>0x04</c>. This one shares the reliable queue with damage and phase messages.</para></summary>
        public static void SendEvent(int netId, string name, int[] i = null, float[] f = null, string s = null)
        {
            ArenaClient client = ArenaClient.Instance;
            if (netId <= 0 || string.IsNullOrEmpty(name) || client == null || !client.IsConnected)
            {
                return;
            }

            EventOut.netId = netId;
            EventOut.name = name;
            EventOut.i = i ?? EmptyInts;
            EventOut.f = f ?? EmptyFloats;
            EventOut.s = s ?? "";
            client.Send(EventOut);
        }

        // ------------------------------------------------------------- dynamic objects (§10.10)

        /// <summary>Puts a spawned instance into the SAME lookup as the scene objects and writes the
        /// state that came with the spawn. Called by the Core spawner from
        /// <see cref="SpawnRequested"/>.</summary>
        /// <returns>False when the id or the instance is unusable (the spawner then destroys it).</returns>
        public static bool RegisterSpawned(int netId, NetObject instance)
        {
            return instance != null && instance.BindDynamicId(netId);
        }

        // ------------------------------------------------------------- incoming (§5.3)

        private void HandleObjectState(ObjectStateMsg msg)
        {
            Apply(msg, NetStateOrigin.Live);
        }

        private void HandleObjectSpawn(ObjectStateMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            // ⚠️ An id that already exists is NOT a spawn: the server allocated it twice or a despawn was
            // lost. Writing the state onto the live object is the harmless branch.
            if (NetObjectRegistry.TryGet(msg.netId, out NetObject existing))
            {
                Debug.LogWarning($"[NetObjectSync] netId {msg.netId} için doğuş geldi ama örnek zaten var — " +
                                 "durum var olan objeye yazıldı, yeni obje yaratılmadı.", existing);
                ApplyTo(existing, msg, NetStateOrigin.Snapshot);
                return;
            }

            RequestSpawn(msg);
        }

        private void HandleObjectDespawn(ObjectDespawnMsg msg)
        {
            if (msg == null || msg.netId <= 0)
            {
                return;
            }

            DespawnRequested?.Invoke(msg.netId);
        }

        private void HandleObjectEvent(ObjectEventMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            if (!NetObjectRegistry.TryGet(msg.netId, out NetObject netObject))
            {
                Debug.LogWarning($"[NetObjectSync] '{msg.name}' olayı bilinmeyen netId {msg.netId} için geldi — " +
                                 "obje henüz doğmamış ya da çoktan silinmiş olabilir.");
                return;
            }

            netObject.RaiseEvent(msg);
            ObjectEventReceived?.Invoke(netObject, msg);
        }

        private void HandleWorldState(WorldStateMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            if (!IsSceneLoaded(msg.sceneName))
            {
                _pending = msg;
                return;
            }

            _pending = null;
            ApplyAll(msg);
        }

        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            WorldStateMsg pending = _pending;
            if (pending == null || !IsSceneLoaded(pending.sceneName))
            {
                return;
            }

            _pending = null;
            ApplyAll(pending);
        }

        private void HandleDisconnected()
        {
            _pending = null;
        }

        private void ApplyAll(WorldStateMsg msg)
        {
            if (msg.objects == null)
            {
                return;
            }

            for (int i = 0; i < msg.objects.Length; i++)
            {
                Apply(msg.objects[i], NetStateOrigin.Snapshot);
            }
        }

        /// <summary>Applies one entry. An unknown DYNAMIC id is a spawn (a late joiner gets the objects
        /// that were born before them in <c>world_state</c> too); an unknown SCENE id means the export
        /// and the scene have drifted apart and gets one log line.</summary>
        private void Apply(ObjectStateMsg msg, NetStateOrigin origin)
        {
            if (msg == null)
            {
                return;
            }

            if (!NetObjectRegistry.TryGet(msg.netId, out NetObject netObject))
            {
                if (msg.netId >= ArenaProtocol.NET_ID_DYNAMIC_MIN && msg.netId <= ArenaProtocol.NET_ID_DYNAMIC_MAX)
                {
                    RequestSpawn(msg);
                    return;
                }

                Debug.LogWarning($"[NetObjectSync] netId {msg.netId} sahnede yok — sunucu tablosu ile sahne " +
                                 "birbirinden kaymış olabilir (henüz yüklenmemiş bir sahnenin objesi de olabilir).");
                return;
            }

            // kind is a CHECK, not a drawing source (§5.3): a mismatch means the export and the scene
            // have drifted apart — logged rather than breaking the wrong object silently.
            string sceneKind = netObject.Kind != null ? netObject.Kind.Kind : "";
            if (!string.IsNullOrEmpty(msg.kind) && !string.Equals(msg.kind, sceneKind, StringComparison.Ordinal))
            {
                Debug.LogWarning($"[NetObjectSync] netId {msg.netId} türü sunucuda '{msg.kind}', sahnede " +
                                 $"'{sceneKind}' — export ile sahne birbirinden kaymış. Haritayı yeniden dışa aktarın.",
                    netObject);
            }

            ApplyTo(netObject, msg, origin);
        }

        /// <summary>Publishes the spawn request and writes the state onto the instance the listener
        /// registered. ⚠️ The state is applied as a SNAPSHOT: the object was just created, so its
        /// creation effect is the spawner's business, not a state change effect.</summary>
        private void RequestSpawn(ObjectStateMsg msg)
        {
            SpawnRequested?.Invoke(msg);

            if (!NetObjectRegistry.TryGet(msg.netId, out NetObject spawned))
            {
                Debug.LogWarning($"[NetObjectSync] netId {msg.netId} ('{msg.kind}') doğuş isteğine karşılık " +
                                 "örnek kaydedilmedi — bu tür NetSpawnCatalog'da yok ya da spawner bağlı değil.");
                return;
            }

            ApplyTo(spawned, msg, NetStateOrigin.Snapshot);
        }

        private static void ApplyTo(NetObject netObject, ObjectStateMsg msg, NetStateOrigin origin)
        {
            bool hasPose = TryReadPose(msg, out Vector3 position, out Quaternion rotation);
            netObject.ApplyState(msg.hp, msg.flags, msg.owner, msg.stage, msg.s,
                position, rotation, hasPose, origin);
        }

        /// <summary>Reads the arena-space resting pose; false when the server sent none (an object that
        /// never moved already sits in the right place, §5.3) or the arrays are short.</summary>
        private static bool TryReadPose(ObjectStateMsg msg, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            if (msg.pos == null || msg.pos.Length < 3 || msg.rot == null || msg.rot.Length < 4)
            {
                return false;
            }

            position = new Vector3(msg.pos[0], msg.pos[1], msg.pos[2]);
            rotation = new Quaternion(msg.rot[0], msg.rot[1], msg.rot[2], msg.rot[3]);
            return true;
        }

        /// <summary>Is <paramref name="sceneName"/> the loaded active scene (exact match — the scene
        /// name is the catalog key).</summary>
        private static bool IsSceneLoaded(string sceneName)
        {
            return !string.IsNullOrEmpty(sceneName) &&
                   string.Equals(SceneManager.GetActiveScene().name, sceneName, StringComparison.Ordinal);
        }
    }
}
