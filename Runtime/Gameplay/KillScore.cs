using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Turns a kill into a point: when this player's <see cref="Health"/> reaches zero, the
    /// client that dealt the killing blow gets <see cref="pointsPerKill"/> on the scoreboard.
    ///
    /// Server only. The attacker comes from the projectile that hit, so a client cannot award
    /// itself points by calling something.
    ///
    /// Scoreboard slot = client id, which is how the toolkit's scoreboard is laid out:
    /// host 0, clients 1..3.
    /// </summary>
    public sealed class KillScore : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Shown in [MP] log lines.")]
        [SerializeField] private string label = "kill-score";

        [Tooltip("Health of this player. Found on the same object when empty.")]
        [SerializeField] private Health health;

        [Tooltip("Scoreboard to award. Found in the scene at spawn when empty — the server spawns it from NetConfig.")]
        [SerializeField] private Scoreboard scoreboard;

        [Tooltip("Points awarded to the killer.")]
        [SerializeField] private int pointsPerKill = 1;

        [Tooltip("Award a point to the victim as well (0 = nobody). Useful to make dying less punishing.")]
        [SerializeField] private int consolationPoints;

        public override void OnNetworkSpawn()
        {
            if (health == null) health = GetComponent<Health>();

            if (!IsServer)
            {
                // Clients only need the replicated scores; nothing to subscribe to.
                return;
            }

            if (health == null)
            {
                MPLog.Warn("KILL_SCORE_NO_HEALTH", ("label", label));
                return;
            }

            // The scoreboard is spawned by the bootstrap, so it may not exist yet at this
            // moment. Resolve it lazily when a kill actually happens.
            if (scoreboard == null) scoreboard = FindObjectOfType<Scoreboard>();

            health.DiedBy += HandleDiedBy;

            if (HasArgument("-mp-arena-demo"))
            {
                StartCoroutine(ArenaDemo());
            }
        }

        public override void OnNetworkDespawn()
        {
            if (health != null) health.DiedBy -= HandleDiedBy;
        }

        private void HandleDiedBy(ulong attackerClientId)
        {
            if (!IsServer) return;

            if (attackerClientId != OwnerClientId)
            {
                Award(attackerClientId, pointsPerKill, "kill");
            }

            if (consolationPoints > 0)
            {
                Award(OwnerClientId, consolationPoints, "consolation");
            }
        }

        private void Award(ulong clientId, int points, string reason)
        {
            if (points <= 0) return;

            if (scoreboard == null) scoreboard = FindObjectOfType<Scoreboard>();

            if (scoreboard == null)
            {
                MPLog.Warn("KILL_SCORE_NO_SCOREBOARD", ("label", label), ("hint", "set Scoreboard Prefab on the NetConfig"));
                return;
            }

            int slot = (int)clientId;
            if (slot < 0 || slot >= scoreboard.SlotCount)
            {
                MPLog.Warn("KILL_SCORE_NO_SLOT", ("label", label), ("clientId", clientId), ("slots", scoreboard.SlotCount));
                return;
            }

            scoreboard.AddScore(slot, points);
            MPLog.Info("KILL_SCORE", ("label", label), ("killer", (int)clientId), ("points", points), ("reason", reason));
        }

        /// <summary>Test hook (-mp-arena-demo): kill the host and prove the point lands.</summary>
        private System.Collections.IEnumerator ArenaDemo()
        {
            yield return new WaitForSeconds(3f);

            ulong attacker = 1;
            health.ApplyDamage(Mathf.Max(1, health.Max), attacker);

            yield return new WaitForSeconds(0.5f);

            MPLog.Info("ARENA_DEMO_DONE",
                ("score", scoreboard != null && scoreboard.SlotCount > 1 ? scoreboard.GetScore(1) : -1));
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
