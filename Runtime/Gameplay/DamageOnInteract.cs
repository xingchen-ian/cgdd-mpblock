using System.Collections;
using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Turns an approved interaction into damage, and a death into a point.
    ///
    /// This is the "weapon" half of the interaction model: the client only ever asks to
    /// interact, this component decides what that means, and only the server applies it.
    /// When health reaches zero the score goes up and the target comes back after a
    /// short delay, so the template is replayable without restarting.
    /// </summary>
    public sealed class DamageOnInteract : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Interactable that triggers the damage. The server subscribes to its Approved event on spawn, so only server-approved interactions reduce health; leave it empty and this component does nothing.")]
        [SerializeField] private Interactable interactable;

        [Tooltip("Health of the target to reduce. It only applies while that Health is alive; leave it empty and there is no damage, no death scoring and no respawn.")]
        [SerializeField] private Health health;

        [Tooltip("Damage applied per approved interaction. Applied on the server only through Health.ApplyDamage; damage at or above the target's current health kills it outright and triggers the scoring and respawn logic below.")]
        [SerializeField] private int damage = 20;

        [Tooltip("Scoreboard that receives the point when the target dies; optional. If empty, the server calls FindObjectOfType for the first Scoreboard in the scene at the moment the point is needed; if none is found, no point is awarded but respawn still happens.")]
        [SerializeField] private Scoreboard scoreboard;

        [Tooltip("Which scoreboard slot gets the point when the target dies (0-based). It must be inside the Scoreboard's slotCount, otherwise AddScore silently ignores it; 0 means always credit the first team.")]
        [SerializeField] private int scoreSlot;

        [Tooltip("Seconds before the target respawns at full health. The server waits at least 0.5 s; while dead the target cannot take damage and the visual is hidden.")]
        [SerializeField] private float respawnSeconds = 3f;

        [Header("Visual")]
        [Tooltip("Visual Transform hidden while dead and shown again on respawn; optional. If empty the look does not change and the object is only logically un-hittable. Do not assign the root that carries this component or the NetworkObject: deactivating it stops the respawn coroutine and the target never comes back.")]
        [SerializeField] private Transform visual;

        public override void OnNetworkSpawn()
        {
            if (IsServer && scoreboard == null)
            {
                scoreboard = FindObjectOfType<Scoreboard>();
            }

            if (IsServer && interactable != null)
            {
                interactable.Approved += HandleApproved;
            }

            if (health != null)
            {
                health.Changed += HandleHealthChanged;
                HandleHealthChanged(health.Current, health.Max);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (interactable != null) interactable.Approved -= HandleApproved;
            if (health != null) health.Changed -= HandleHealthChanged;
        }

        private void HandleApproved(ulong clientId)
        {
            if (!IsServer) return;
            if (health == null || health.IsDead) return;

            MPLog.Info("TARGET_HIT", ("damage", damage), ("by", clientId));
            health.ApplyDamage(damage);
        }

        private void HandleHealthChanged(int current, int max)
        {
            if (visual != null)
            {
                visual.gameObject.SetActive(current > 0);
            }

            if (!IsServer) return;
            if (current > 0) return;

            MPLog.Info("TARGET_DIED", ("slot", scoreSlot));

            // NetBootstrap spawns the scoreboard prefab in OnServerStarted, which runs
            // after NGO's scene-object sweep. A reference found in OnNetworkSpawn is
            // therefore null on the server; look again when the point is actually needed.
            if (scoreboard == null) scoreboard = FindObjectOfType<Scoreboard>();

            if (scoreboard != null)
            {
                scoreboard.AddScore(scoreSlot, 1);
            }

            StartCoroutine(RespawnAfterDelay());
        }

        private IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(Mathf.Max(0.5f, respawnSeconds));

            if (health != null) health.ResetHealth();
            MPLog.Info("TARGET_RESPAWNED");
        }
    }
}
