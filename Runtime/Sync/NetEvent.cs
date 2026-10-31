using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace MPBlocks.Sync
{
    /// <summary>
    /// A thing that happens once: a button was pressed, a lever was pulled, a round began.
    ///
    /// Events are NOT replayed for players who join later. If the effect must persist,
    /// it belongs in a Synced* value instead. That distinction is the single most useful
    /// idea in this package: a newcomer must be able to see the world as it is, and
    /// cannot be told about everything that already happened.
    ///
    /// Both directions are exposed as Inspector buttons, so a design can be assembled
    /// without writing code.
    /// </summary>
    public sealed class NetEvent : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Event name shown in the Inspector and in [MP] log lines (EVENT_RECEIVED), so multiple events on one object can be told apart.")]
        [SerializeField] private string label = "event";

        [Header("Events")]
        [Tooltip("Fires on the server when the event arrives from a client, or when the event is raised on the server itself. Use it for one-off effects only: late joiners never receive replayed events.")]
        [SerializeField] private UnityEvent onServerReceived = new UnityEvent();

        [Tooltip("Fires on clients after the server calls SendToAll. Events are not replayed for players who join later, so use a Synced* value for anything that must persist.")]
        [SerializeField] private UnityEvent onClientReceived = new UnityEvent();

        /// <summary>Send from a client to the server. Also safe to call on the server (fires locally).</summary>
        public void SendToServer()
        {
            if (!IsSpawned)
            {
                MPLog.Warn("EVENT_NOT_SPAWNED", ("event", label));
                return;
            }

            if (IsServer)
            {
                MPLog.Info(MPEvent.EventReceived, ("event", label), ("by", "server"), ("from", "local"));
                onServerReceived.Invoke();
                return;
            }

            SendToServerRpc();
        }

        /// <summary>Send from the server to every client. Fires locally on the server too.</summary>
        public void SendToAll()
        {
            if (!IsSpawned)
            {
                MPLog.Warn("EVENT_NOT_SPAWNED", ("event", label));
                return;
            }

            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "NetEvent"), ("label", label), ("reason", "SendToAll is server only"));
                return;
            }

            ReceiveOnClientsClientRpc();
            onServerReceived.Invoke();
        }

        [ServerRpc(RequireOwnership = false)]
        private void SendToServerRpc(ServerRpcParams rpcParams = default)
        {
            MPLog.Info(MPEvent.EventReceived, ("event", label), ("by", "server"), ("from", rpcParams.Receive.SenderClientId));
            onServerReceived.Invoke();
        }

        [ClientRpc]
        private void ReceiveOnClientsClientRpc()
        {
            MPLog.Info(MPEvent.EventReceived, ("event", label), ("by", "clients"));
            onClientReceived.Invoke();
        }
    }
}
