using System.Collections;
using System.Collections.Generic;
using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Hands out roles to players and rotates them mid-match — the server side of every
    /// asymmetric or hidden-role design.
    ///
    /// Roles live in each player's <see cref="PlayerPrivateState"/>, so a player only ever
    /// learns their own. Rotation is just a reassignment: nothing needs to be re-sent as an
    /// event, and a player who joins mid-match still gets a role.
    /// </summary>
    public sealed class RoleManager : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Role ids to distribute, in order. 0 is conventionally 'none'.")]
        [SerializeField] private int[] roleIds = { 1, 2, 3, 4 };

        [Tooltip("Assign roles as soon as players spawn.")]
        [SerializeField] private bool assignOnStart = true;

        [Tooltip("Assign a role to players who join later.")]
        [SerializeField] private bool assignOnJoin = true;

        [Tooltip("Rotate roles automatically this often. 0 disables automatic rotation.")]
        [SerializeField] private float autoRotateSeconds = 0f;

        [Header("Events")]
        [Tooltip("Fires on the server after roles have been (re)assigned.")]
        [SerializeField] private UnityEvent onRolesAssigned = new UnityEvent();

        private int _rotation;
        private Coroutine _autoRotate;

        public int RoleCount => roleIds != null ? roleIds.Length : 0;

        public override void OnNetworkSpawn()
        {
            if (!IsServer) return;

            if (assignOnJoin) NetworkManager.OnClientConnectedCallback += HandleClientConnected;

            bool demo = HasArgument("-mp-role-demo");
            if (demo) autoRotateSeconds = Mathf.Max(4f, autoRotateSeconds);

            if (assignOnStart || demo)
            {
                StartCoroutine(AssignWhenReady());
            }

            if (autoRotateSeconds > 0f)
            {
                _autoRotate = StartCoroutine(AutoRotate());
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && assignOnJoin)
            {
                NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            }
        }

        private void HandleClientConnected(ulong clientId)
        {
            StartCoroutine(AssignWhenReady());
        }

        private IEnumerator AssignWhenReady()
        {
            // Player objects spawn a frame or two after the connection, and joining players
            // may still be arriving.
            yield return new WaitForSeconds(1f);
            AssignAll();
        }

        /// <summary>Server only. Assigns a role to every connected player.</summary>
        public void AssignAll()
        {
            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "RoleManager"), ("caller", "client"));
                return;
            }

            if (roleIds == null || roleIds.Length == 0) return;

            var ids = new List<ulong>(NetworkManager.ConnectedClientsIds);
            ids.Sort();

            for (int i = 0; i < ids.Count; i++)
            {
                int role = roleIds[(i + _rotation) % roleIds.Length];
                StartCoroutine(AssignTo(ids[i], role));
            }

            onRolesAssigned?.Invoke();
            MPLog.Info("ROLES_ASSIGNED", ("players", ids.Count), ("rotation", _rotation));
        }

        /// <summary>Server only. Shifts every role by one position.</summary>
        public void RotateRoles()
        {
            if (!IsServer) return;

            _rotation++;
            MPLog.Info("ROLES_ROTATING", ("rotation", _rotation));
            AssignAll();
        }

        /// <summary>Server only. Reads a player's assigned role.</summary>
        public int ServerReadRole(ulong clientId)
        {
            var state = FindState(clientId);
            return state != null ? state.ServerRead() : 0;
        }

        private IEnumerator AssignTo(ulong clientId, int role)
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                var state = FindState(clientId);
                if (state != null)
                {
                    state.SetServerRole(role);
                    MPLog.Info("ROLE_ASSIGNED", ("clientId", clientId), ("role", role));
                    yield break;
                }

                yield return null;
            }

            MPLog.Warn("ROLE_ASSIGN_FAILED", ("clientId", clientId), ("reason", "no player object"));
        }

        private PlayerPrivateState FindState(ulong clientId)
        {
            var manager = NetworkManager;
            if (manager == null) return null;
            if (!manager.ConnectedClients.TryGetValue(clientId, out var client)) return null;
            if (client.PlayerObject == null) return null;

            return client.PlayerObject.GetComponent<PlayerPrivateState>();
        }

        private IEnumerator AutoRotate()
        {
            yield return new WaitForSeconds(autoRotateSeconds);

            while (isActiveAndEnabled)
            {
                RotateRoles();
                yield return new WaitForSeconds(autoRotateSeconds);
            }
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
