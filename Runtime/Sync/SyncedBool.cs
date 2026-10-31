using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Sync
{
    /// <summary>
    /// A boolean that is true for every player, and stays true for players who join later.
    ///
    /// State is owned by the server. Clients that need to change it call
    /// <see cref="RequestSet"/>, which is a request the server may grant — the lesson
    /// being that clients ask, they do not set.
    ///
    /// Use this for anything persistent (door open, round active, team ready).
    /// Use <see cref="NetEvent"/> for things that merely happen.
    /// </summary>
    public sealed class SyncedBool : NetworkBehaviour, IStateHashable
    {
        [Header("MPBlocks")]
        [Tooltip("Variable name shown in the Inspector and in [MP] log lines (e.g. AUTH_VIOLATION, VALUE_SET_BY_REQUEST), so the right toggle is easy to find.")]
        [SerializeField] private string label = "bool";

        [Tooltip("Value the server writes when the object spawns; unchecked means false. It only takes effect at spawn time, afterwards use SetServerValue on the server or RequestSet from clients.")]
        [SerializeField] private bool initialValue;

        [Header("Events")]
        [Tooltip("Fires on every machine when the value changes, with the new value, so animation or lights can be driven directly. Read Value in OnNetworkSpawn for the initial display instead of relying on this callback alone.")]
        [SerializeField] private BoolEvent onChanged = new BoolEvent();

        private readonly NetworkVariable<bool> _value = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public bool Value => _value.Value;
        public string Label => label;
        public BoolEvent OnChanged => onChanged;

        /// <inheritdoc />
        public int StateHashContribution() => _value.Value ? 1 : 0;

        public override void OnNetworkSpawn()
        {
            _value.OnValueChanged += HandleChanged;
            if (IsServer && initialValue && !_value.Value)
            {
                _value.Value = true;
            }
        }

        public override void OnNetworkDespawn()
        {
            _value.OnValueChanged -= HandleChanged;
        }

        private void HandleChanged(bool previous, bool current) => onChanged?.Invoke(current);

        /// <summary>Server-only. Non-server callers are rejected and logged.</summary>
        public void SetServerValue(bool value)
        {
            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "SyncedBool"), ("label", label), ("caller", "client"));
                return;
            }

            if (_value.Value != value) _value.Value = value;
        }

        /// <summary>Ask the server to change the value. Safe to call from any machine.</summary>
        public void RequestSet(bool value)
        {
            if (IsServer)
            {
                SetServerValue(value);
                return;
            }

            RequestSetServerRpc(value);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestSetServerRpc(bool value, ServerRpcParams rpcParams = default)
        {
            MPLog.Info(MPEvent.ValueSetByRequest, ("block", "SyncedBool"), ("label", label), ("value", value), ("from", rpcParams.Receive.SenderClientId));
            SetServerValue(value);
        }
    }
}
