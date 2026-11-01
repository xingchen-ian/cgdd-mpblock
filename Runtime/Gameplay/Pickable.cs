using System;
using System.Collections;
using MPBlocks.Core;
using MPBlocks.Sync;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// An object a player can take: a phone, a flag, a key, or an animal to possess.
    ///
    /// Taking it **moves ownership** from the server to that player, which is the part the
    /// other blocks cannot express. Once owned, the new owner may drive the object's
    /// <see cref="SyncedTransform"/> (owner authority), and every machine parents it to
    /// that player's `HoldPoint` child, if one exists.
    ///
    /// Validation is not duplicated here: attach an <see cref="Interactable"/> and the
    /// server only grants the pickup after the normal request → validate round trip.
    /// </summary>
    public sealed class Pickable : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Shown in the inspector and in [MP] log lines.")]
        [SerializeField] private string label = "pickable";

        [Tooltip("Optional. When set, an approved interaction with it grants the pickup.")]
        [SerializeField] private Interactable requestInteractable;

        [Tooltip("Child name on the player prefab that a held object is parented to.")]
        [SerializeField] private string holdPointName = "HoldPoint";

        [Header("Events")]
        [Tooltip("Fires on every machine when a player takes the object.")]
        [SerializeField] private UnityEvent onPickedUp = new UnityEvent();

        [Tooltip("Fires on every machine when the object is released.")]
        [SerializeField] private UnityEvent onDropped = new UnityEvent();

        /// <summary>Raised on every machine with the new owner's clientId.</summary>
        public event Action<ulong> Picked;

        /// <summary>Raised on every machine when the object returns to the world.</summary>
        public event Action Dropped;

        public string Label => label;

        public bool IsHeld => IsSpawned && OwnerClientId != NetworkManager.ServerClientId;

        public override void OnNetworkSpawn()
        {
            if (IsServer && requestInteractable != null)
            {
                requestInteractable.Approved += HandleInteractionApproved;
            }

            if (IsHeld)
            {
                StartCoroutine(AttachWhenPossible(OwnerClientId));
            }
        }

        public override void OnNetworkDespawn()
        {
            if (requestInteractable != null)
            {
                requestInteractable.Approved -= HandleInteractionApproved;
            }
        }

        private void HandleInteractionApproved(ulong clientId)
        {
            if (!IsServer) return;
            GrantTo(clientId);
        }

        /// <summary>Server only. Moves ownership of this object to <paramref name="clientId"/>.</summary>
        public void GrantTo(ulong clientId)
        {
            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "Pickable"), ("label", label), ("caller", "client"));
                return;
            }

            if (clientId == OwnerClientId) return;

            NetworkObject.ChangeOwnership(clientId);
            MPLog.Info("PICKUP_GRANTED", ("label", label), ("owner", clientId), ("objectId", NetworkObjectId));

            PickedClientRpc(clientId);
        }

        /// <summary>Server only. Returns the object to the world.</summary>
        public void ReturnToWorld()
        {
            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "Pickable"), ("label", label), ("caller", "client"));
                return;
            }

            if (OwnerClientId == NetworkManager.ServerClientId) return;

            NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
            MPLog.Info("PICKUP_RELEASED", ("label", label));

            DroppedClientRpc();
        }

        /// <summary>Convenience for the holding player: release what you are holding.</summary>
        public void RequestDrop()
        {
            if (!IsHeld) return;

            if (IsServer)
            {
                ReturnToWorld();
                return;
            }

            if (IsOwner) RequestDropServerRpc();
        }

        [ServerRpc]
        private void RequestDropServerRpc(ServerRpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            ReturnToWorld();
        }

        [ClientRpc]
        private void PickedClientRpc(ulong newOwner)
        {
            transform.SetParent(null, true);
            StartCoroutine(AttachWhenPossible(newOwner));

            onPickedUp?.Invoke();
            Picked?.Invoke(newOwner);
        }

        [ClientRpc]
        private void DroppedClientRpc()
        {
            transform.SetParent(null, true);

            onDropped?.Invoke();
            Dropped?.Invoke();
        }

        /// <summary>
        /// The player object may not exist yet on a late-joining client, so keep trying for
        /// a moment before giving up and leaving the object where it is.
        /// </summary>
        private IEnumerator AttachWhenPossible(ulong ownerClientId)
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                var manager = NetworkManager;
                if (manager != null
                    && manager.ConnectedClients.TryGetValue(ownerClientId, out var client)
                    && client.PlayerObject != null)
                {
                    var holdPoint = FindHoldPoint(client.PlayerObject.transform);
                    if (holdPoint != null)
                    {
                        transform.SetParent(holdPoint, false);
                        transform.localPosition = Vector3.zero;
                        transform.localRotation = Quaternion.identity;
                        MPLog.Info("PICKUP_ATTACHED", ("label", label), ("to", holdPoint.name));
                        yield break;
                    }
                }

                yield return null;
            }
        }

        private Transform FindHoldPoint(Transform player)
        {
            if (string.IsNullOrEmpty(holdPointName)) return player;

            var child = player.Find(holdPointName);
            return child != null ? child : null;
        }
    }
}
