using System.Collections.Generic;
using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Diagnostics
{
    /// <summary>
    /// Periodically hashes the server-authoritative world and compares machines.
    ///
    /// Each machine computes the same hash from: the number of connected clients, the
    /// sorted ids of spawned network objects, and every <see cref="IStateHashable"/>
    /// contribution. Every owner reports its hash to the server, which answers with
    /// STATE_HASH_MATCH or STATE_HASH_MISMATCH in the log. That log line is what
    /// tools/mp-verify.sh asserts on, and it is also the fastest way a student can tell
    /// "my state is diverging" from "my network is fine but my game logic is wrong".
    ///
    /// Owner-authoritative transforms are deliberately excluded: they are expected to
    /// differ for a moment while packets travel.
    /// </summary>
    public sealed class StateHash : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Seconds between world-state hash comparisons. 2 s is frequent enough to catch drift without flooding the log; smaller is more sensitive but writes more [MP] lines.")]
        [SerializeField] private float interval = 2f;

        [Tooltip("Also hash the set of spawned object ids. Leave OFF when a Spawner runs: " +
                 "objects appear between a client's sample and the server's, which looks like " +
                 "a mismatch even though nothing is wrong.")]
        [SerializeField] private bool includeObjectIds;

        [Tooltip("How many recent server hashes a client hash may match. State changes take a " +
                 "moment to propagate, so a client is normally a sample behind.")]
        [SerializeField] private int matchWindow = 6;

        [Tooltip("Seconds to wait after spawn before comparing. Players are still receiving the " +
                 "initial object set during this window and would otherwise report noise.")]
        [SerializeField] private float warmupSeconds = 6f;

        private float _timer;
        private float _spawnTime;
        private readonly System.Collections.Generic.List<int> _recentServerHashes = new System.Collections.Generic.List<int>();
        private readonly List<ulong> _ids = new List<ulong>();
        private readonly List<int> _contributions = new List<int>();

        public override void OnNetworkSpawn()
        {
            _spawnTime = Time.time;
        }

        private void Update()
        {
            // One reporter per machine: only the local owner of this object acts.
            if (!IsSpawned || !IsOwner) return;
            if (Time.time - _spawnTime < warmupSeconds) return;

            _timer += Time.deltaTime;
            if (_timer < interval) return;
            _timer = 0f;

            int hash = ComputeHash();

            if (IsServer)
            {
                _recentServerHashes.Add(hash);
                while (_recentServerHashes.Count > Mathf.Max(1, matchWindow)) _recentServerHashes.RemoveAt(0);

                MPLog.Info("STATE_HASH_SELF", ("hash", hash));
            }
            else
            {
                ReportHashServerRpc(hash);
            }
        }

        [ServerRpc]
        private void ReportHashServerRpc(int clientHash, ServerRpcParams rpcParams = default)
        {
            int serverHash = ComputeHash();
            ulong sender = rpcParams.Receive.SenderClientId;

            // A client is normally one sample behind the server, so accept a match against
            // any hash the server produced recently. Without this, every state change looks
            // like divergence.
            bool matched = clientHash == serverHash || _recentServerHashes.Contains(clientHash);

            if (matched)
            {
                MPLog.Info(MPEvent.StateHashMatch, ("clientId", sender), ("hash", clientHash));
            }
            else
            {
                MPLog.Warn(MPEvent.StateHashMismatch, ("clientId", sender), ("server", serverHash), ("client", clientHash));
            }
        }

        private int ComputeHash()
        {
            var manager = NetworkManager;
            if (manager == null) return 0;

            _ids.Clear();
            _contributions.Clear();

            var objects = FindObjectsOfType<NetworkObject>();
            foreach (var candidate in objects)
            {
                if (candidate == null || !candidate.IsSpawned) continue;
                _ids.Add(candidate.NetworkObjectId);

                var behaviours = candidate.GetComponents<NetworkBehaviour>();
                foreach (var behaviour in behaviours)
                {
                    if (behaviour is IStateHashable hashable)
                    {
                        _contributions.Add(hashable.StateHashContribution());
                    }
                }
            }

            // Deterministic across machines: the server assigns these ids, so both ends
            // sort into the same order.
            _ids.Sort();
            _contributions.Sort();

            unchecked
            {
                uint hash = 2166136261u;
                hash = Mix(hash, manager.ConnectedClientsIds.Count);

                if (includeObjectIds)
                {
                    foreach (var id in _ids)
                    {
                        hash = Mix(hash, (int)(id & 0xFFFFFFFF));
                        hash = Mix(hash, (int)((id >> 32) & 0xFFFFFFFF));
                    }
                }

                foreach (var value in _contributions)
                {
                    hash = Mix(hash, value);
                }

                return (int)hash;
            }
        }

        private static uint Mix(uint hash, int value)
        {
            unchecked
            {
                hash ^= (uint)value;
                hash *= 16777619u;
                return hash;
            }
        }
    }
}
