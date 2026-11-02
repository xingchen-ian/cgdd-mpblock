using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// A server-simulated bullet for the arena template.
    ///
    /// The server is the only machine that moves it, tests hits and despawns it; clients
    /// merely see the replicated <c>SyncedTransform</c> (server authority). Damage is
    /// applied on the server through <see cref="Health.ApplyDamage"/>, so a client cannot
    /// kill anyone by editing the prefab.
    ///
    /// A hit on a dead player, on the shooter's own body, or on anything without a
    /// <see cref="Health"/> is ignored. A death credits one point to the shooter's
    /// <see cref="Scoreboard"/> slot (client id maps to slot index).
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class Projectile : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Projectile display name, used only in the Inspector and in [MP] logs (the block/label pair in AUTH_VIOLATION). It is not a network identifier; it only affects log readability.")]
        [SerializeField] private string label = "projectile";

        [Tooltip("Hit radius in metres. Every frame the server compares the distance from the projectile to each player's hit centre (player origin plus bodyCenterHeight); larger is easier to hit and can feel like aim assist, smaller can pass through a model without registering.")]
        [SerializeField] private float hitRadius = 0.8f;

        [Tooltip("Height in metres above a player's origin used as that player's hit centre, default 1. It should usually sit near the middle of the character capsule; getting it wrong shifts hit detection too high or too low.")]
        [SerializeField] private float bodyCenterHeight = 1f;

        private ulong _shooterClientId;
        private Vector3 _direction = Vector3.forward;
        private int _damage;
        private float _speed = 28f;
        private float _lifetime = 3f;
        private float _age;
        private bool _launched;
        private Scoreboard _scoreboard;

        /// <summary>Shown in [MP] log lines.</summary>
        public string Label => label;

        /// <summary>The client id of the player that fired this projectile.</summary>
        public ulong ShooterClientId => _shooterClientId;

        /// <summary>Distance from a player centre that counts as a hit.</summary>
        public float HitRadius => hitRadius;

        /// <summary>Normalised travel direction.</summary>
        public Vector3 Direction => _direction;

        /// <summary>Damage applied by a hit.</summary>
        public int Damage => _damage;

        /// <summary>True once <see cref="Launch"/> has been accepted on the server.</summary>
        public bool Launched => _launched;

        /// <summary>
        /// Server only. Called by <see cref="PlayerShooter"/> right after the projectile
        /// has been spawned; sets the travel data and starts the simulation on the next
        /// server frame. Clients that call this are ignored with an AUTH_VIOLATION log.
        /// </summary>
        /// <param name="ownerClientId">Owner id of the player that fired.</param>
        /// <param name="direction">Travel direction, normalised here.</param>
        /// <param name="damage">Damage applied to the first player hit.</param>
        /// <param name="speed">Travel speed in metres per second.</param>
        /// <param name="lifetime">Seconds before an unhit projectile despawns.</param>
        public void Launch(ulong ownerClientId, Vector3 direction, int damage, float speed, float lifetime)
        {
            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "Projectile"), ("label", label), ("caller", "client"));
                return;
            }

            if (!IsSpawned) return;

            _shooterClientId = ownerClientId;
            _direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
            _damage = Mathf.Max(1, damage);
            _speed = Mathf.Max(0.1f, speed);
            _lifetime = Mathf.Max(0.1f, lifetime);
            _age = 0f;
            _launched = true;
            transform.rotation = Quaternion.LookRotation(_direction, Vector3.up);
        }

        private void Update()
        {
            // Clients only render the replicated transform; the server owns the flight.
            if (!IsSpawned || !IsServer || !_launched) return;

            transform.position += _direction * (_speed * Time.deltaTime);

            _age += Time.deltaTime;
            if (_age >= _lifetime)
            {
                DespawnSelf();
                return;
            }

            CheckHit();
        }

        private void CheckHit()
        {
            Health[] candidates = FindObjectsOfType<Health>();

            foreach (Health health in candidates)
            {
                if (health == null || health.IsDead) continue;

                NetworkObject victimObject = health.GetComponent<NetworkObject>();
                if (victimObject == null || !victimObject.IsSpawned) continue;

                // The shooter cannot shoot itself.
                if (victimObject.OwnerClientId == _shooterClientId) continue;

                Vector3 center = health.transform.position + Vector3.up * bodyCenterHeight;
                if ((center - transform.position).sqrMagnitude > hitRadius * hitRadius) continue;

                health.ApplyDamage(_damage, _shooterClientId);
                MPLog.Info("PROJECTILE_HIT", ("owner", _shooterClientId), ("victim", victimObject.OwnerClientId), ("damage", _damage));

                if (health.IsDead) HandleKill(victimObject.OwnerClientId);

                DespawnSelf();
                return;
            }
        }

        private void HandleKill(ulong victimClientId)
        {
            MPLog.Info("PLAYER_KILLED", ("killer", _shooterClientId), ("victim", victimClientId));

            if (_scoreboard == null) _scoreboard = FindObjectOfType<Scoreboard>();
            if (_scoreboard == null || _scoreboard.SlotCount <= 0) return;

            int slot = Mathf.Clamp((int)_shooterClientId, 0, _scoreboard.SlotCount - 1);
            _scoreboard.AddScore(slot, 1);
        }

        private void DespawnSelf()
        {
            if (!IsSpawned || !IsServer) return;
            NetworkObject.Despawn();
        }
    }
}
