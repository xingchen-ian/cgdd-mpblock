using System.Collections.Generic;
using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Server-side object spawner: periodically instantiates a registered prefab up to a
    /// maximum count.
    ///
    /// Every spawn is announced to clients by Netcode for GameObjects, so this is the
    /// block that makes bullets, enemies, pickups and dropped items possible. Nothing
    /// spawns locally — the server owns the decision, and the count is capped so a
    /// runaway timer cannot flood the session.
    /// </summary>
    public sealed class Spawner : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Prefab to spawn. It must contain a NetworkObject and be registered in NetConfig's spawnablePrefabs, otherwise clients receive the spawn message but cannot create it. Empty means only a config error is logged and nothing spawns.")]
        [SerializeField] private GameObject prefab;

        [Tooltip("Seconds between spawns in normal mode. 0 or negative stops automatic spawning (the spawnOnStart first spawn still happens), and the maxAlive cap still applies.")]
        [SerializeField] private float interval = 3f;

        [Tooltip("Maximum number of live objects this spawner keeps. Destroyed or despawned objects are pruned from the count automatically, and new spawn requests are silently skipped at the cap; 0 or negative disables spawning.")]
        [SerializeField] private int maxAlive = 6;

        [Tooltip("Spawn radius in metres: new objects appear at a random point inside a circle around this object on the ground. Height is fixed at 0.5m above the spawner's position; too large and objects land in cover or outside the arena.")]
        [SerializeField] private float radius = 6f;

        [Tooltip("Whether to spawn one object immediately when the server starts (OnNetworkSpawn). It only affects the first spawn; later ones come from interval or demoInterval, and it only runs on the server.")]
        [SerializeField] private bool spawnOnStart = true;

        [Tooltip("Spawn interval in seconds used when -mp-spawn-demo is on the command line (default 0.8) for automated verification. It has no effect in normal play, where interval still controls the rate.")]
        [SerializeField] private float demoInterval = 0.8f;

        private readonly List<NetworkObject> _alive = new List<NetworkObject>();
        private float _timer;
        private bool _demo;

        public int AliveCount
        {
            get
            {
                Prune();
                return _alive.Count;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            foreach (var arg in System.Environment.GetCommandLineArgs())
            {
                if (arg == "-mp-spawn-demo") _demo = true;
            }

            MPLog.Info("SPAWNER_READY", ("prefab", prefab != null ? prefab.name : "none"), ("maxAlive", maxAlive));

            if (spawnOnStart) SpawnOne();
        }

        private void Update()
        {
            if (!IsServer || prefab == null) return;

            float step = _demo ? demoInterval : interval;
            if (step <= 0f) return;

            _timer += Time.deltaTime;
            if (_timer < step) return;
            _timer = 0f;

            SpawnOne();
        }

        /// <summary>Server only. Spawns one instance if under the cap.</summary>
        public void SpawnOne()
        {
            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "Spawner"), ("caller", "client"));
                return;
            }

            if (prefab == null)
            {
                MPLog.Warn(MPEvent.ConfigError, ("reason", "Spawner has no prefab"));
                return;
            }

            Prune();
            if (_alive.Count >= maxAlive) return;

            Vector2 offset = Random.insideUnitCircle * Mathf.Max(0f, radius);
            var position = transform.position + new Vector3(offset.x, 0.5f, offset.y);
            var instance = Instantiate(prefab, position, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));

            var networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                MPLog.Error(MPEvent.ConfigError, ("reason", "spawned prefab has no NetworkObject"), ("prefab", prefab.name));
                Destroy(instance);
                return;
            }

            networkObject.Spawn();
            _alive.Add(networkObject);

            MPLog.Info("SPAWNED", ("prefab", prefab.name), ("alive", _alive.Count), ("networkObjectId", networkObject.NetworkObjectId));
        }

        /// <summary>Server only. Despawns everything this spawner created.</summary>
        public void ClearAll()
        {
            if (!IsServer) return;

            foreach (var networkObject in _alive)
            {
                if (networkObject != null && networkObject.IsSpawned)
                {
                    networkObject.Despawn(true);
                }
            }

            _alive.Clear();
            MPLog.Info("SPAWNER_CLEARED");
        }

        private void Prune()
        {
            for (int i = _alive.Count - 1; i >= 0; i--)
            {
                if (_alive[i] == null || !_alive[i].IsSpawned) _alive.RemoveAt(i);
            }
        }
    }
}
