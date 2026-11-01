using System;
using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace MPBlocks.Sync
{
    /// <summary>
    /// An event delivered to **one chosen player**, instead of to everyone.
    ///
    /// This is the block asymmetric and hidden-information games need: a role card, a
    /// private hint, "you are the cat and only you hear footsteps". The ordinary
    /// <see cref="NetEvent"/> cannot express it, because it only addresses the server or
    /// every client.
    ///
    /// The payload is a single int (a role id, a card id, a seat number). Firing is
    /// server → one client, server → all, or client → server.
    /// </summary>
    public sealed class TargetedNetEvent : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Shown in the inspector and in [MP] log lines.")]
        [SerializeField] private string label = "targeted";

        [Header("Events")]
        [Tooltip("Fires on the machine that receives the event.")]
        [SerializeField] private UnityEvent onReceived = new UnityEvent();

        /// <summary>Raised wherever the event arrives: (senderClientId, payload).</summary>
        public event Action<ulong, int> Received;

        public string Label => label;

        public override void OnNetworkSpawn()
        {
            // The component lives on every player; only the host's own instance runs the demo.
            if (!IsServer || !IsOwner) return;

            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg == "-mp-targeted-demo")
                {
                    StartCoroutine(Demo());
                    break;
                }
            }
        }

        /// <summary>Client to server, addressed to the server only.</summary>
        public void SendToServer(int payload = 0)
        {
            if (!IsSpawned)
            {
                MPLog.Warn("TARGETED_NOT_SPAWNED", ("event", label));
                return;
            }

            if (IsServer)
            {
                Fire(NetworkManager.LocalClientId, payload);
                return;
            }

            SendToServerServerRpc(payload);
        }

        /// <summary>Server to every client. No-op (with a warning) on a client.</summary>
        public void Broadcast(int payload = 0)
        {
            if (!IsSpawned) return;

            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "TargetedNetEvent"), ("label", label), ("reason", "Broadcast is server only"));
                return;
            }

            MPLog.Info("TARGETED_SENT", ("event", label), ("to", "all"), ("payload", payload));
            BroadcastClientRpc(NetworkManager.LocalClientId, payload);
            Fire(NetworkManager.LocalClientId, payload);
        }

        /// <summary>Server to exactly one client. No-op (with a warning) on a client.</summary>
        public void SendToClient(ulong clientId, int payload = 0)
        {
            if (!IsSpawned) return;

            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "TargetedNetEvent"), ("label", label), ("reason", "SendToClient is server only"));
                return;
            }

            var target = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } },
            };

            MPLog.Info("TARGETED_SENT", ("event", label), ("to", clientId), ("payload", payload));
            SendToClientClientRpc(NetworkManager.LocalClientId, payload, target);
        }

        [ServerRpc(RequireOwnership = false)]
        private void SendToServerServerRpc(int payload, ServerRpcParams rpcParams = default)
        {
            Fire(rpcParams.Receive.SenderClientId, payload);
        }

        [ClientRpc]
        private void BroadcastClientRpc(ulong fromClientId, int payload)
        {
            Fire(fromClientId, payload);
        }

        [ClientRpc]
        private void SendToClientClientRpc(ulong fromClientId, int payload, ClientRpcParams rpcParams = default)
        {
            Fire(fromClientId, payload);
        }

        private void Fire(ulong fromClientId, int payload)
        {
            MPLog.Info("TARGETED_RECEIVED", ("event", label), ("from", fromClientId), ("payload", payload));
            onReceived?.Invoke();
            Received?.Invoke(fromClientId, payload);
        }

        private System.Collections.IEnumerator Demo()
        {
            yield return new WaitForSeconds(3f);

            for (int round = 0; round < 6; round++)
            {
                var ids = NetworkManager.ConnectedClientsIds;
                foreach (var id in ids)
                {
                    if (id == NetworkManager.LocalClientId) continue;
                    SendToClient(id, round);
                    yield return new WaitForSeconds(0.5f);
                }

                yield return new WaitForSeconds(1.5f);
            }
        }
    }
}
