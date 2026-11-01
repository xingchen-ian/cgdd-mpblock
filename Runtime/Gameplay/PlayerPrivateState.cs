using System;
using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// State that belongs to **one player only**: their role, their secret card, their
    /// private objective.
    ///
    /// The value is a `NetworkVariable` with owner-only read permission, so:
    /// - the owning client can read it,
    /// - the server can read it,
    /// - **no other client can read it at all** (not hidden in the UI — not sent),
    /// - a player who joins late still receives it, because it is state, not an event.
    ///
    /// That last point is what <see cref="MPBlocks.Sync.TargetedNetEvent"/> cannot do: an
    /// event told to one player is gone if that player reconnects. A role has to persist.
    ///
    /// Do not add this to <c>StateHash</c>: each machine can only read its own value, so the
    /// hashes would legitimately disagree.
    /// </summary>
    public sealed class PlayerPrivateState : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Shown in the inspector and in [MP] log lines.")]
        [SerializeField] private string label = "private";

        [Tooltip("Value treated as 'no role yet'.")]
        [SerializeField] private int none = 0;

        [Header("Events")]
        [Tooltip("Fires on the owning client when its private value changes.")]
        [SerializeField] private UnityEvent onRoleChanged = new UnityEvent();

        /// <summary>Raised on the owning client with the new value.</summary>
        public event Action<int> RoleChanged;

        /// <summary>The owner's value. Every other machine sees <see cref="None"/>.</summary>
        public int Role => IsOwner ? _role.Value : none;

        public int None => none;

        public bool HasRole => IsOwner && _role.Value != none;

        public string Label => label;

        private readonly NetworkVariable<int> _role = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Owner, NetworkVariableWritePermission.Server);

        public override void OnNetworkSpawn()
        {
            // Only the owner subscribes: nobody else can read the value anyway.
            if (!IsOwner) return;

            _role.OnValueChanged += HandleChanged;
            HandleChanged(none, _role.Value);
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner) _role.OnValueChanged -= HandleChanged;
        }

        private void HandleChanged(int previous, int current)
        {
            MPLog.Info("PRIVATE_STATE_CHANGED", ("label", label), ("value", current));
            onRoleChanged?.Invoke();
            RoleChanged?.Invoke(current);
        }

        /// <summary>Server only. Sets this player's private value.</summary>
        public void SetServerRole(int role)
        {
            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "PlayerPrivateState"), ("label", label), ("caller", "client"));
                return;
            }

            _role.Value = role;
            MPLog.Info("PRIVATE_STATE_SET", ("label", label), ("clientId", OwnerClientId), ("value", role));
        }

        /// <summary>Server only. Clears the value.</summary>
        public void ClearRole() => SetServerRole(none);

        /// <summary>
        /// Server convenience: read this player's value (the server can read owner-only
        /// variables, which is how a game enforces "only the seer may peek").
        /// </summary>
        public int ServerRead() => IsServer ? _role.Value : none;
    }
}
