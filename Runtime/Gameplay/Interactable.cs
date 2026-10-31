using System;
using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// The three-step interaction model: **client requests, server validates, server
    /// announces the result.**
    ///
    /// A client never says "I opened the door". It says "I want to interact", and the
    /// server decides. That single change is what stops a game where anyone can do
    /// anything, and it is the block students should reach for first when they add
    /// pickups, levers, buttons or weapons.
    ///
    /// Validation is: the requester's player object is within <see cref="maxDistance"/>,
    /// and — if set — the optional <see cref="Validator"/> hook agrees. The hook is where
    /// a game adds "do they have the key", "is it their turn", "are they alive".
    /// </summary>
    public sealed class Interactable : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Display name of the interaction. Used only in the Inspector and in [MP] logs, e.g. Lever_A; it is not a network identifier, so changing it never breaks logic.")]
        [SerializeField] private string label = "interactable";

        [Tooltip("Server-side distance limit in metres: a request from a player farther than this is denied with reason=out-of-range. Note that PlayerInteractor also has its own 3.5m search range on the client, and both must be satisfied.")]
        [SerializeField] private float maxDistance = 3f;

        [Header("Events")]
        [Tooltip("UnityEvent fired on every client (including the Host) when the server approves an interaction; optional Inspector wiring. It is only a local presentation hook, so real state changes should be written by the server through Synced* blocks; leaving it unbound does not affect approval or sync.")]
        [SerializeField] private UnityEvent onApproved = new UnityEvent();

        [Tooltip("UnityEvent fired only on the requesting client when the server denies an interaction; optional. Common reasons are out-of-range (client range fine but maxDistance too small) and Validator returning false, so it is a good place for a hint message.")]
        [SerializeField] private UnityEvent onDenied = new UnityEvent();

        /// <summary>Raised on every machine with the clientId that interacted.</summary>
        public event Action<ulong> Approved;

        /// <summary>Raised on the requester's machine with its own clientId.</summary>
        public event Action<ulong> Denied;

        /// <summary>
        /// Optional extra server-side check. Return false to refuse. Runs in addition to
        /// the distance check, never instead of it.
        /// </summary>
        public Func<ulong, bool> Validator { get; set; }

        public string Label => label;
        public float MaxDistance => maxDistance;

        /// <summary>Ask the server to let the local player interact. Safe from any machine.</summary>
        public void Request()
        {
            if (!IsSpawned)
            {
                MPLog.Warn("INTERACT_NOT_SPAWNED", ("label", label));
                return;
            }

            if (IsServer)
            {
                HandleRequestOnServer(NetworkManager.LocalClientId);
                return;
            }

            RequestServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestServerRpc(ServerRpcParams rpcParams = default)
        {
            HandleRequestOnServer(rpcParams.Receive.SenderClientId);
        }

        private void HandleRequestOnServer(ulong clientId)
        {
            MPLog.Info("INTERACT_REQUESTED", ("label", label), ("clientId", clientId));

            if (!IsWithinRange(clientId))
            {
                Deny(clientId, "out-of-range");
                return;
            }

            if (Validator != null && !Validator(clientId))
            {
                Deny(clientId, "rejected-by-game-rule");
                return;
            }

            MPLog.Info("INTERACT_APPROVED", ("label", label), ("clientId", clientId));
            ApprovedClientRpc(clientId);
        }

        private void Deny(ulong clientId, string reason)
        {
            MPLog.Info("INTERACT_DENIED", ("label", label), ("clientId", clientId), ("reason", reason));

            var target = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } },
            };

            DeniedClientRpc(clientId, target);
        }

        private bool IsWithinRange(ulong clientId)
        {
            var manager = NetworkManager;
            if (manager == null) return false;
            if (!manager.ConnectedClients.TryGetValue(clientId, out var client)) return false;

            var player = client.PlayerObject;
            if (player == null) return false;

            return Vector3.Distance(player.transform.position, transform.position) <= maxDistance;
        }

        [ClientRpc]
        private void ApprovedClientRpc(ulong clientId)
        {
            onApproved?.Invoke();
            Approved?.Invoke(clientId);
        }

        [ClientRpc]
        private void DeniedClientRpc(ulong clientId, ClientRpcParams rpcParams = default)
        {
            onDenied?.Invoke();
            Denied?.Invoke(clientId);
        }
    }
}
