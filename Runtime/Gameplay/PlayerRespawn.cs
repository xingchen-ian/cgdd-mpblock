using System;
using System.Collections;
using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Death and respawn for an arena fighter.
    ///
    /// Every machine watches the player's <see cref="Health"/>: at zero the body visual
    /// disappears, at full health it comes back. Only the server decides *where* a dead
    /// player returns: it waits <c>respawnSeconds</c>, resets the health and teleports the
    /// owner with a targeted <c>RespawnClientRpc</c> to the next
    /// <see cref="PlayerSpawnPoint"/> (sorted by name, round-robin).
    ///
    /// The teleport is a ClientRpc on purpose. Player objects are owner-authoritative, so
    /// a server that wrote a remote player's transform would be overwritten by the owner
    /// on the next frame.
    /// </summary>
    public sealed class PlayerRespawn : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Health to watch for death. If empty, OnNetworkSpawn falls back to the Health on the same object; if that is missing too it logs RESPAWN_NO_HEALTH and never respawns.")]
        [SerializeField] private Health health;

        [Tooltip("Seconds a dead player waits before respawning; clamped to at least 0.1. Only the server timer uses it, and the respawn position comes from PlayerSpawnPoint objects sorted by name in round-robin order.")]
        [SerializeField] private float respawnSeconds = 3f;

        [Tooltip("Visual Transform hidden while dead and shown again on respawn; optional, and every machine toggles it from the health value. Do not assign the root that carries this component or the NetworkObject: deactivating it stops the coroutine and the respawn never happens.")]
        [SerializeField] private Transform visual;

        private PlayerSpawnPoint[] _spawnPoints;
        private int _spawnCursor;
        private Coroutine _respawnRoutine;

        /// <summary>The health block this respawn watches.</summary>
        public Health Health => health;

        /// <summary>Seconds a dead player waits before coming back.</summary>
        public float RespawnSeconds => respawnSeconds;

        /// <summary>Visual hidden while dead. May be null.</summary>
        public Transform Visual => visual;

        /// <summary>Code-first: set the health block to watch.</summary>
        public void SetHealth(Health value) => health = value;

        /// <summary>Code-first: seconds to wait before respawning, clamped to at least 0.</summary>
        public void SetRespawnSeconds(float seconds) => respawnSeconds = Mathf.Max(0f, seconds);

        /// <summary>Code-first: set the visual hidden while dead.</summary>
        public void SetVisual(Transform value) => visual = value;

        public override void OnNetworkSpawn()
        {
            if (health == null) health = GetComponent<Health>();

            if (health == null)
            {
                MPLog.Warn("RESPAWN_NO_HEALTH", ("clientId", OwnerClientId));
                return;
            }

            health.Changed += HandleHealthChanged;
            ApplyVisual(health.Current > 0);
        }

        public override void OnNetworkDespawn()
        {
            if (health != null) health.Changed -= HandleHealthChanged;

            if (_respawnRoutine != null)
            {
                StopCoroutine(_respawnRoutine);
                _respawnRoutine = null;
            }
        }

        private void HandleHealthChanged(int current, int max)
        {
            ApplyVisual(current > 0);

            if (!IsServer) return;
            if (current > 0) return;

            // A despawned object is a shutdown, not a death: never start a respawn then.
            if (!IsSpawned || health == null) return;

            if (_respawnRoutine != null) StopCoroutine(_respawnRoutine);
            _respawnRoutine = StartCoroutine(RespawnAfterDelay());
        }

        private IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(Mathf.Max(0.1f, respawnSeconds));

            _respawnRoutine = null;

            // The session may have shut down while waiting; touching health then throws.
            if (!IsSpawned || health == null) yield break;

            health.ResetHealth();

            PlayerSpawnPoint point = NextSpawnPoint();
            Vector3 position = point != null ? point.Position : transform.position;
            Quaternion rotation = point != null ? point.Rotation : transform.rotation;

            ClientRpcParams parameters = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { OwnerClientId } },
            };

            RespawnClientRpc(position, rotation, parameters);

            MPLog.Info("PLAYER_RESPAWNED", ("clientId", OwnerClientId), ("point", point != null ? point.name : "none"));
        }

        /// <summary>
        /// Owner only: the server never moves a remote player, it tells the owner where to
        /// be and the owner-authoritative transform syncs the new position to everyone.
        /// </summary>
        [ClientRpc]
        private void RespawnClientRpc(Vector3 position, Quaternion rotation, ClientRpcParams rpcParams = default)
        {
            if (!IsOwner) return;

            transform.position = position;
            transform.rotation = rotation;
        }

        /// <summary>
        /// Sorted by name so a scripted run is reproducible instead of depending on
        /// Unity's object enumeration order.
        /// </summary>
        private PlayerSpawnPoint NextSpawnPoint()
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0)
            {
                _spawnPoints = FindObjectsOfType<PlayerSpawnPoint>(true);
                _spawnCursor = 0;

                if (_spawnPoints == null || _spawnPoints.Length == 0)
                {
                    _spawnPoints = null;
                    return null;
                }

                Array.Sort(_spawnPoints, (a, b) => string.CompareOrdinal(a.name, b.name));
            }

            PlayerSpawnPoint point = _spawnPoints[_spawnCursor % _spawnPoints.Length];
            _spawnCursor++;
            return point;
        }

        private void ApplyVisual(bool visible)
        {
            if (visual == null) return;
            visual.gameObject.SetActive(visible);
        }
    }
}
