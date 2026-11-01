using System;
using System.Collections;
using System.Collections.Generic;
using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Splits players into teams and keeps the assignment consistent on the server.
    ///
    /// Teams are public: every machine can read every player's <see cref="Team"/>, so a
    /// scoreboard or a friendly-fire check can be written locally without asking the server.
    /// </summary>
    public sealed class TeamManager : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("How many teams to spread players across.")]
        [SerializeField] private int teamCount = 2;

        [Tooltip("Display colour per team id, index 0 = team 1.")]
        [SerializeField] private Color[] teamColors =
        {
            new Color(0.85f, 0.30f, 0.30f),
            new Color(0.30f, 0.45f, 0.85f),
            new Color(0.35f, 0.75f, 0.40f),
            new Color(0.85f, 0.75f, 0.25f),
        };

        [Tooltip("Assign teams as soon as players spawn.")]
        [SerializeField] private bool assignOnStart = true;

        [Tooltip("Give players who join later a team.")]
        [SerializeField] private bool assignOnJoin = true;

        [Header("Events")]
        [Tooltip("Fires on the server after teams have been assigned.")]
        [SerializeField] private UnityEvent onTeamsAssigned = new UnityEvent();

        /// <summary>Raised on the server after an assignment pass.</summary>
        public event Action TeamsAssigned;

        public int TeamCount => Mathf.Max(1, teamCount);

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            if (assignOnJoin) NetworkManager.OnClientConnectedCallback += HandleClientConnected;

            if (assignOnStart || HasArgument("-mp-team-demo"))
            {
                StartCoroutine(AssignWhenReady());
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && assignOnJoin)
            {
                NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            }
        }

        private void HandleClientConnected(ulong clientId) => StartCoroutine(AssignWhenReady());

        private IEnumerator AssignWhenReady()
        {
            yield return new WaitForSeconds(1f);
            AssignTeams();
        }

        /// <summary>Server only. Spreads players across teams, balanced by player count.</summary>
        public void AssignTeams()
        {
            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "TeamManager"), ("caller", "client"));
                return;
            }

            var ids = new List<ulong>(NetworkManager.ConnectedClientsIds);
            ids.Sort();

            for (int i = 0; i < ids.Count; i++)
            {
                int team = (i % TeamCount) + 1;
                StartCoroutine(AssignTo(ids[i], team));
            }

            onTeamsAssigned?.Invoke();
            TeamsAssigned?.Invoke();
            MPLog.Info("TEAMS_ASSIGNED", ("players", ids.Count), ("teams", TeamCount));
        }

        /// <summary>Server only. Reads a player's team.</summary>
        public int ServerReadTeam(ulong clientId)
        {
            var team = FindTeam(clientId);
            return team != null ? team.TeamId : 0;
        }

        /// <summary>Display colour for a team id (1-based). Falls back to white.</summary>
        public Color ColorFor(int teamId)
        {
            if (teamColors == null || teamId <= 0 || teamId > teamColors.Length) return Color.white;
            return teamColors[teamId - 1];
        }

        private IEnumerator AssignTo(ulong clientId, int teamId)
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                var team = FindTeam(clientId);
                if (team != null)
                {
                    team.SetServerTeam(teamId);
                    MPLog.Info("TEAM_ASSIGNED", ("clientId", clientId), ("team", teamId));
                    yield break;
                }

                yield return null;
            }

            MPLog.Warn("TEAM_ASSIGN_FAILED", ("clientId", clientId), ("reason", "no player object"));
        }

        private Team FindTeam(ulong clientId)
        {
            var manager = NetworkManager;
            if (manager == null) return null;
            if (!manager.ConnectedClients.TryGetValue(clientId, out var client)) return null;
            if (client.PlayerObject == null) return null;

            return client.PlayerObject.GetComponent<Team>();
        }

        private static bool HasArgument(string name)
        {
            try
            {
                foreach (var arg in System.Environment.GetCommandLineArgs())
                {
                    if (arg == name) return true;
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }
    }
}
