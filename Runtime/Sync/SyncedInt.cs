using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Sync
{
    /// <summary>
    /// An integer that is true for every player, and correct for players who join later.
    /// Server-owned; clients use <see cref="RequestSet"/>.
    ///
    /// Use for scores, health, ammo, counters — anything that persists.
    /// </summary>
    public sealed class SyncedInt : NetworkBehaviour, IStateHashable
    {
        [Header("MPBlocks")]
        [Tooltip("Variable name shown in the Inspector and in [MP] log lines (e.g. AUTH_VIOLATION, VALUE_SET_BY_REQUEST), so the right counter is easy to find.")]
        [SerializeField] private string label = "int";

        [Tooltip("Value the server writes when the object spawns; 0 keeps the default of 0. It only takes effect at spawn time, afterwards use SetServerValue on the server or RequestSet from clients.")]
        [SerializeField] private int initialValue;

        [Header("Events")]
        [Tooltip("Fires on every machine when the value changes, with the new value, so score, health or ammo displays can be driven directly. Read Value in OnNetworkSpawn for the initial display.")]
        [SerializeField] private IntEvent onChanged = new IntEvent();

        private readonly NetworkVariable<int> _value = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public int Value => _value.Value;
        public string Label => label;
        public IntEvent OnChanged => onChanged;

        /// <inheritdoc />
        public int StateHashContribution() => _value.Value;

        public override void OnNetworkSpawn()
        {
            _value.OnValueChanged += HandleChanged;
            if (IsServer && initialValue != 0 && _value.Value == 0)
            {
                _value.Value = initialValue;
            }
        }

        public override void OnNetworkDespawn()
        {
            _value.OnValueChanged -= HandleChanged;
        }

        private void HandleChanged(int previous, int current) => onChanged?.Invoke(current);

        /// <summary>Server-only. Non-server callers are rejected and logged.</summary>
        public void SetServerValue(int value)
        {
            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "SyncedInt"), ("label", label), ("caller", "client"));
                return;
            }

            if (_value.Value != value) _value.Value = value;
        }

        /// <summary>Ask the server to change the value. Safe to call from any machine.</summary>
        public void RequestSet(int value)
        {
            if (IsServer)
            {
                SetServerValue(value);
                return;
            }

            RequestSetServerRpc(value);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestSetServerRpc(int value, ServerRpcParams rpcParams = default)
        {
            MPLog.Info(MPEvent.ValueSetByRequest, ("block", "SyncedInt"), ("label", label), ("value", value), ("from", rpcParams.Receive.SenderClientId));
            SetServerValue(value);
        }
    }
}
