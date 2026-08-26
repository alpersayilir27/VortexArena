using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using VortexArena.Core.Arena;
using VortexArena.Net;
using VortexArena.Protocol;

namespace VortexArena.Core.World
{
    /// <summary>The single listener of <c>object_spawn</c>/<c>object_despawn</c> (§10.10): resolves the
    /// prefab from <c>kind</c> through <see cref="NetSpawnCatalog"/>, instantiates it at the arena pose
    /// converted to world space and hands the server-allocated id to the instance.
    /// <para>Never placed in a scene (the <c>NetObjectSync</c> pattern): a dynamic object can arrive with
    /// the <c>world_state</c> of a late joiner, so it self-bootstraps and goes DontDestroyOnLoad.</para>
    /// <para>⚠️ Only what THIS bridge created is ever destroyed — a scene object is baked, not spawned,
    /// and destroying it would leave a hole no reset restores.</para></summary>
    public class NetObjectSpawner : MonoBehaviour
    {
        /// <summary>Catalog asset name under <c>Assets/_Shared/Data/Resources/</c> — no scene references
        /// it (the GameCatalog rationale); moving or renaming it stops every dynamic spawn.</summary>
        public const string CatalogResourceName = "NetSpawnCatalog";

        private static NetObjectSpawner _instance;

        private readonly Dictionary<int, GameObject> _spawned = new Dictionary<int, GameObject>();
        private readonly List<int> _idScratch = new List<int>();

        /// <summary>Kinds already reported as missing — the warning is worth one line per kind, but a
        /// dispenser spawning every few seconds would otherwise flood the console.</summary>
        private readonly HashSet<string> _warnedKinds = new HashSet<string>();

        private NetSpawnCatalog _catalog;
        private bool _catalogWarned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
            {
                return;
            }

            var go = new GameObject("[NetObjectSpawner]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<NetObjectSpawner>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;

            // Persistent singleton: subscribe in Awake/OnDestroy, not OnEnable/OnDisable.
            NetObjectSync.SpawnRequested += HandleSpawnRequested;
            NetObjectSync.DespawnRequested += HandleDespawnRequested;
            NetEvents.OnDisconnected += HandleDisconnected;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private void OnDestroy()
        {
            if (_instance != this)
            {
                return;
            }

            NetObjectSync.SpawnRequested -= HandleSpawnRequested;
            NetObjectSync.DespawnRequested -= HandleDespawnRequested;
            NetEvents.OnDisconnected -= HandleDisconnected;
            SceneManager.sceneLoaded -= HandleSceneLoaded;

            _instance = null;
        }

        /// <summary>⚠️ Registration happens INSIDE this call (<see cref="NetObjectSync.SpawnRequested"/>
        /// contract): the state that came with the spawn is written right after we return, and an
        /// instance registered later would miss it.</summary>
        private void HandleSpawnRequested(ObjectStateMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            GameObject prefab = ResolvePrefab(msg.kind);
            if (prefab == null)
            {
                return;
            }

            bool hasPose = TryReadWorldPose(msg, out Vector3 worldPosition, out Quaternion worldRotation);
            GameObject instance = hasPose
                ? Instantiate(prefab, worldPosition, worldRotation)
                : Instantiate(prefab);

            instance.name = $"{prefab.name}_{msg.netId}";

            NetObject netObject = instance.GetComponent<NetObject>();
            if (netObject == null)
            {
                netObject = instance.GetComponentInChildren<NetObject>(true);
            }

            if (netObject == null || !NetObjectSync.RegisterSpawned(msg.netId, netObject))
            {
                Debug.LogError($"[NetObjectSpawner] '{msg.kind}' prefabı ağ nesnesi olarak kaydedilemedi " +
                               "(NetObject bileşeni yok ya da Dynamic Id işaretli değil) — örnek silindi.", prefab);
                Destroy(instance);
                return;
            }

            _spawned[msg.netId] = instance;
        }

        private void HandleDespawnRequested(int netId)
        {
            if (!_spawned.TryGetValue(netId, out GameObject instance))
            {
                return; // not ours: a scene object is never destroyed (§10.10)
            }

            _spawned.Remove(netId);

            if (instance != null)
            {
                Destroy(instance);
            }
        }

        /// <summary>Scene change: the load already destroyed the instances, but the LEDGER would keep
        /// their ids — and a stale row makes the next round's spawn look like "it already exists".</summary>
        private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            PruneDestroyed();
        }

        private void HandleDisconnected()
        {
            DestroyAll();
        }

        private GameObject ResolvePrefab(string kind)
        {
            if (string.IsNullOrEmpty(kind))
            {
                return null;
            }

            if (_catalog == null)
            {
                _catalog = Resources.Load<NetSpawnCatalog>(CatalogResourceName);
            }

            if (_catalog == null)
            {
                if (!_catalogWarned)
                {
                    _catalogWarned = true;
                    Debug.LogError($"[NetObjectSpawner] '{CatalogResourceName}' kataloğu bulunamadı " +
                                   "(Assets/_Shared/Data/Resources altında olmalı) — dinamik objeler doğmayacak.");
                }

                return null;
            }

            GameObject prefab = _catalog.Find(kind);
            if (prefab == null && _warnedKinds.Add(kind))
            {
                // Loud on purpose: silently swallowed, the field symptom is "malzeme hiç çıkmıyor".
                Debug.LogWarning($"[NetObjectSpawner] '{kind}' türü NetSpawnCatalog'da yok — obje yaratılmadı.");
            }

            return prefab;
        }

        /// <summary>Reads the spawn pose (ARENA space, §3) and converts it to world; false when the
        /// server sent none, in which case the prefab's own pose stands.</summary>
        private static bool TryReadWorldPose(ObjectStateMsg msg, out Vector3 worldPosition, out Quaternion worldRotation)
        {
            worldPosition = Vector3.zero;
            worldRotation = Quaternion.identity;

            if (msg.pos == null || msg.pos.Length < 3 || msg.rot == null || msg.rot.Length < 4)
            {
                return false;
            }

            worldPosition = ArenaSpace.ArenaToWorld(new Vector3(msg.pos[0], msg.pos[1], msg.pos[2]));
            worldRotation = ArenaSpace.ArenaToWorld(new Quaternion(msg.rot[0], msg.rot[1], msg.rot[2], msg.rot[3]));
            return true;
        }

        private void PruneDestroyed()
        {
            _idScratch.Clear();

            foreach (KeyValuePair<int, GameObject> pair in _spawned)
            {
                if (pair.Value == null)
                {
                    _idScratch.Add(pair.Key);
                }
            }

            for (int i = 0; i < _idScratch.Count; i++)
            {
                _spawned.Remove(_idScratch[i]);
            }
        }

        private void DestroyAll()
        {
            foreach (KeyValuePair<int, GameObject> pair in _spawned)
            {
                if (pair.Value != null)
                {
                    Destroy(pair.Value);
                }
            }

            _spawned.Clear();
        }
    }
}
