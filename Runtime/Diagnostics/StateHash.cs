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

        private float _timer;
        private readonly List<ulong> _ids = new List<ulong>();
        private readonly List<int> _contributions = new List<int>();

        private void Update()
        {
            // One reporter per machine: only the local owner of this object acts.
            if (!IsSpawned || !IsOwner) return;

            _timer += Time.deltaTime;
            if (_timer < interval) return;
            _timer = 0f;

            int hash = ComputeHash();

            if (IsServer)
            {
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

            if (clientHash == serverHash)
            {
                MPLog.Info(MPEvent.StateHashMatch, ("clientId", sender), ("hash", serverHash));
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

                foreach (var id in _ids)
                {
                    hash = Mix(hash, (int)(id & 0xFFFFFFFF));
                    hash = Mix(hash, (int)((id >> 32) & 0xFFFFFFFF));
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
