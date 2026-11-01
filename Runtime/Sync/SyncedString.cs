using MPBlocks.Core;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Sync
{
    /// <summary>
    /// A string that is true for every player: a nickname, a room name, a status line.
    ///
    /// Netcode for GameObjects cannot replicate a plain `string` in a NetworkVariable, so this
    /// block stores a <see cref="FixedString64Bytes"/> internally (63 bytes, UTF-8) and exposes
    /// an ordinary `string`. Anything longer is silently truncated — names do not need more.
    ///
    /// Server-owned, like every value block: clients ask through <see cref="RequestSet"/>.
    /// A late joiner receives the current value, because it is state and not an event.
    /// </summary>
    public sealed class SyncedString : NetworkBehaviour, IStateHashable
    {
        [Header("MPBlocks")]
        [Tooltip("Shown in the inspector and in [MP] log lines.")]
        [SerializeField] private string label = "string";

        [Tooltip("Value applied by the server when the object spawns.")]
        [SerializeField] private string initialValue = string.Empty;

        [Header("Events")]
        [Tooltip("Fires on every machine when the value changes.")]
        [SerializeField] private StringEvent onChanged = new StringEvent();

        private readonly NetworkVariable<FixedString64Bytes> _value = new NetworkVariable<FixedString64Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public string Value => _value.Value.ToString();

        public string Label => label;
        public StringEvent OnChanged => onChanged;

        public override void OnNetworkSpawn()
        {
            _value.OnValueChanged += HandleChanged;

            if (IsServer && !string.IsNullOrEmpty(initialValue) && string.IsNullOrEmpty(Value))
            {
                _value.Value = new FixedString64Bytes(initialValue);
            }
        }

        public override void OnNetworkDespawn()
        {
            _value.OnValueChanged -= HandleChanged;
        }

        private void HandleChanged(FixedString64Bytes previous, FixedString64Bytes current)
        {
            onChanged?.Invoke(current.ToString());
        }

        /// <summary>Server only. Non-server callers are rejected and logged.</summary>
        public void SetServerValue(string value)
        {
            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "SyncedString"), ("label", label), ("caller", "client"));
                return;
            }

            var next = new FixedString64Bytes(value ?? string.Empty);
            if (_value.Value.Equals(next)) return;

            _value.Value = next;
            MPLog.Info("VALUE_CHANGED", ("block", "SyncedString"), ("label", label), ("value", next.ToString()));
        }

        /// <summary>Ask the server to change the value. Safe from any machine.</summary>
        public void RequestSet(string value)
        {
            if (IsServer)
            {
                SetServerValue(value);
                return;
            }

            RequestSetServerRpc(value ?? string.Empty);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestSetServerRpc(string value, ServerRpcParams rpcParams = default)
        {
            MPLog.Info(MPEvent.ValueSetByRequest, ("block", "SyncedString"), ("label", label),
                ("value", value), ("from", rpcParams.Receive.SenderClientId));
            SetServerValue(value);
        }

        /// <inheritdoc />
        public int StateHashContribution()
        {
            // Not string.GetHashCode(): that is randomised per process on .NET Core and would
            // make two machines disagree about identical text.
            unchecked
            {
                int hash = 17;
                var text = Value;
                for (int i = 0; i < text.Length; i++) hash = hash * 31 + text[i];
                return hash;
            }
        }
    }
}
