using System;
using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Which team or faction this object belongs to. Public state: every player can see every
    /// player's team, which is what team games, scoreboards and friendly-fire rules need.
    ///
    /// Contrast with <see cref="PlayerPrivateState"/>: a role is secret, a team is not.
    /// 0 means "unassigned".
    /// </summary>
    public sealed class Team : NetworkBehaviour, IStateHashable
    {
        [Header("MPBlocks")]
        [Tooltip("Shown in the inspector and in [MP] log lines.")]
        [SerializeField] private string label = "team";

        [Header("Events")]
        [Tooltip("Fires on every machine when the team changes.")]
        [SerializeField] private UnityEvent onTeamChanged = new UnityEvent();

        /// <summary>Raised on every machine with the new team id.</summary>
        public event Action<int> TeamChanged;

        private readonly NetworkVariable<int> _team = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public int TeamId => _team.Value;
        public bool IsUnassigned => _team.Value == 0;
        public string Label => label;

        public bool IsOnTeam(int teamId) => _team.Value == teamId;

        public override void OnNetworkSpawn()
        {
            _team.OnValueChanged += HandleChanged;
        }

        public override void OnNetworkDespawn()
        {
            _team.OnValueChanged -= HandleChanged;
        }

        private void HandleChanged(int previous, int current)
        {
            // Logged on every machine: this is how a student checks "does this client
            // actually know the other players' teams?".
            MPLog.Info("TEAM_CHANGED", ("clientId", OwnerClientId), ("team", current));
            onTeamChanged?.Invoke();
            TeamChanged?.Invoke(current);
        }

        /// <summary>Server only. Assigns this object to a team (0 clears it).</summary>
        public void SetServerTeam(int teamId)
        {
            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "Team"), ("label", label), ("caller", "client"));
                return;
            }

            if (_team.Value == teamId) return;

            _team.Value = teamId;
            MPLog.Info("TEAM_SET", ("label", label), ("clientId", OwnerClientId), ("team", teamId));
        }

        /// <inheritdoc />
        public int StateHashContribution() => _team.Value;
    }
}
