using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Sync
{
    /// <summary>
    /// A float that is true for every player, and correct for players who join later.
    /// Server-owned; clients use <see cref="RequestSet"/>.
    /// The change event carries the new value, so a slider, a timer or a fill bar can be
    /// driven from the Inspector without writing code.
    /// </summary>
    public sealed class SyncedFloat : NetworkBehaviour, IStateHashable
    {
        [Header("MPBlocks")]
        [Tooltip("Variable name shown in the Inspector and in [MP] log lines (e.g. AUTH_VIOLATION, VALUE_SET_BY_REQUEST), so the right value is easy to find.")]
        [SerializeField] private string label = "float";

        [Tooltip("Value the server writes when the object spawns; 0 keeps the default of 0. It only takes effect at spawn time, afterwards use SetServerValue on the server or RequestSet from clients.")]
        [SerializeField] private float initialValue;

        [Header("Events")]
        [Tooltip("Fires on every machine when the value changes, with the new value, so a slider, health bar or timer can be driven directly. Read Value in OnNetworkSpawn for the initial display.")]
        [SerializeField] private FloatEvent onChanged = new FloatEvent();

        private readonly NetworkVariable<float> _value = new NetworkVariable<float>(
            0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public float Value => _value.Value;
        public string Label => label;
        public FloatEvent OnChanged => onChanged;

        /// <inheritdoc />
        public int StateHashContribution() => Mathf.RoundToInt(_value.Value * 1000f);

        public override void OnNetworkSpawn()
        {
            _value.OnValueChanged += HandleChanged;
            if (IsServer && !Mathf.Approximately(initialValue, 0f) && Mathf.Approximately(_value.Value, 0f))
            {
                _value.Value = initialValue;
            }
        }

        public override void OnNetworkDespawn()
        {
            _value.OnValueChanged -= HandleChanged;
        }

        private void HandleChanged(float previous, float current) => onChanged?.Invoke(current);

        /// <summary>Server-only. Non-server callers are rejected and logged.</summary>
        public void SetServerValue(float value)
        {
            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "SyncedFloat"), ("label", label), ("caller", "client"));
                return;
            }

            if (!Mathf.Approximately(_value.Value, value)) _value.Value = value;
        }

        /// <summary>Ask the server to change the value. Safe to call from any machine.</summary>
        public void RequestSet(float value)
        {
            if (IsServer)
            {
                SetServerValue(value);
                return;
            }

            RequestSetServerRpc(value);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestSetServerRpc(float value, ServerRpcParams rpcParams = default)
        {
            MPLog.Info(MPEvent.ValueSetByRequest, ("block", "SyncedFloat"), ("label", label), ("value", value), ("from", rpcParams.Receive.SenderClientId));
            SetServerValue(value);
        }
    }
}
