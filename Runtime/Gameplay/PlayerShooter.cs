using System;
using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Server-authoritative shooting for one arena fighter.
    ///
    /// The owning client only *asks*: the left mouse button sends
    /// <c>ShootServerRpc(direction)</c> and nothing else. The server checks that the
    /// shooter is alive and off cooldown, then spawns a <see cref="Projectile"/> and
    /// launches it. No client ever applies damage — that is what makes "who killed whom"
    /// trustworthy in the arena template.
    ///
    /// <c>-mp-shoot-demo</c> makes the owning client fire at the nearest other alive
    /// player every ~1.2 s, so a headless run can prove shoot → hit → death → score
    /// without anyone touching a mouse.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerShooter : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Prefab instantiated for every shot. It must contain a NetworkObject and a Projectile, and be registered in NetConfig's spawnablePrefabs. Empty or misconfigured means the server logs SHOOT_DENIED and drops the shot; the server Instantiates and Spawns it for all clients.")]
        [SerializeField] private GameObject projectilePrefab;

        [Tooltip("Minimum seconds between shots, default 0.35. The client rate-limits locally first and the server validates again after ShootServerRpc, so lowering it does not give a modified client an advantage; clamped to at least 0.05.")]
        [SerializeField] private float fireInterval = 0.35f;

        [Tooltip("Damage one hit applies, dealt by the server via Health.ApplyDamage when the Projectile hits. Server-side only and clamped to at least 1; set it to the target's health or more for a one-shot kill and a point.")]
        [SerializeField] private int damage = 25;

        [Tooltip("Height in metres above the player origin where the projectile spawns, default 1.2. The muzzle is also offset 0.8m along the player's forward axis; too low and the shot can start inside cover and never hit.")]
        [SerializeField] private float muzzleHeight = 1.2f;

        [Tooltip("Projectile travel speed in metres per second, used by the server to advance the Projectile. Too fast and it can tunnel through a target (hits are a per-frame distance check with no sweep); too slow and it is easy to dodge; clamped to at least 0.1.")]
        [SerializeField] private float projectileSpeed = 28f;

        [Tooltip("Seconds an unhit projectile lives before despawning, which prevents leaks. Only the server times it; too short and distant targets cannot be hit, too long and projectiles pile up.")]
        [SerializeField] private float projectileLifetime = 3f;

        [Tooltip("Player display name, used only in the Inspector and in code through the Label property; it is not a network identifier, so changing it does not affect shooting. Note that [MP] logs currently use the owner/damage fields and do not print it.")]
        [SerializeField] private string label = "player";

        private const float MuzzleForwardOffset = 0.8f;
        private const float DemoInterval = 1.2f;
        private const float MinimumInterval = 0.05f;

        private Health _health;
        private float _nextLocalFireTime;
        private float _nextServerFireTime;
        private bool _demo;
        private float _demoTimer;

        /// <summary>Shown in the inspector and in [MP] log lines.</summary>
        public string Label => label;

        /// <summary>Prefab spawned by every accepted shot.</summary>
        public GameObject ProjectilePrefab => projectilePrefab;

        /// <summary>Damage one hit applies on the server.</summary>
        public int Damage => damage;

        /// <summary>Minimum seconds between two accepted shots.</summary>
        public float FireInterval => fireInterval;

        /// <summary>Height above the player origin where the projectile starts.</summary>
        public float MuzzleHeight => muzzleHeight;

        /// <summary>Where the server instantiates the projectile.</summary>
        public Vector3 MuzzlePosition =>
            transform.position + transform.forward * MuzzleForwardOffset + Vector3.up * muzzleHeight;

        /// <summary>Code-first: set the projectile prefab.</summary>
        public void SetProjectilePrefab(GameObject prefab) => projectilePrefab = prefab;

        /// <summary>Code-first: damage per hit, clamped to at least 1.</summary>
        public void SetDamage(int value) => damage = Mathf.Max(1, value);

        /// <summary>Code-first: minimum seconds between shots, clamped to at least 0.05.</summary>
        public void SetFireInterval(float seconds) => fireInterval = Mathf.Max(MinimumInterval, seconds);

        /// <summary>Code-first: muzzle height above the player origin.</summary>
        public void SetMuzzleHeight(float metres) => muzzleHeight = metres;

        /// <summary>Code-first: projectile speed in metres per second.</summary>
        public void SetProjectileSpeed(float metresPerSecond) => projectileSpeed = Mathf.Max(0.1f, metresPerSecond);

        /// <summary>Code-first: seconds before an unhit projectile despawns.</summary>
        public void SetProjectileLifetime(float seconds) => projectileLifetime = Mathf.Max(0.1f, seconds);

        public override void OnNetworkSpawn()
        {
            _health = GetComponent<Health>();

            if (!IsOwner) return;

            _demo = HasArgument("-mp-shoot-demo");
            _demoTimer = 0f;
            _nextLocalFireTime = 0f;
        }

        private void Update()
        {
            // Input is local: remote machines do nothing at all here.
            if (!IsSpawned || !IsOwner) return;
            if (_health != null && _health.IsDead) return;

            if (_demo)
            {
                _demoTimer += Time.deltaTime;
                if (_demoTimer < DemoInterval) return;

                _demoTimer = 0f;
                TryShoot(AimAtNearestEnemy());
                return;
            }

            if (!Input.GetMouseButton(0)) return;
            TryShoot(transform.forward);
        }

        /// <summary>
        /// Client side: rate-limit locally and ask the server. The server repeats the
        /// checks, so a modified client gains nothing.
        /// </summary>
        private void TryShoot(Vector3 direction)
        {
            if (!IsSpawned || !IsOwner) return;
            if (Time.time < _nextLocalFireTime) return;

            if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;
            direction = direction.normalized;

            _nextLocalFireTime = Time.time + Mathf.Max(MinimumInterval, fireInterval);
            ShootServerRpc(direction);
        }

        /// <summary>Headless demo aim: the nearest other alive player, or straight ahead.</summary>
        private Vector3 AimAtNearestEnemy()
        {
            Health[] candidates = FindObjectsOfType<Health>();
            Health nearest = null;
            float bestDistance = float.MaxValue;

            foreach (Health candidate in candidates)
            {
                if (candidate == null || candidate == _health || candidate.IsDead) continue;

                NetworkObject candidateObject = candidate.GetComponent<NetworkObject>();
                if (candidateObject == null || !candidateObject.IsSpawned) continue;
                if (candidateObject.OwnerClientId == OwnerClientId) continue;

                float distance = (candidate.transform.position - transform.position).sqrMagnitude;
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                nearest = candidate;
            }

            if (nearest == null) return transform.forward;

            Vector3 target = nearest.transform.position + Vector3.up * 1f;
            Vector3 direction = target - MuzzlePosition;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        }

        /// <summary>
        /// Server side: validate the request, then spawn and launch the projectile. The
        /// shooter is the owner of this object, so NGO rejects calls from anyone else
        /// before this body runs.
        /// </summary>
        [ServerRpc]
        private void ShootServerRpc(Vector3 direction, ServerRpcParams rpcParams = default)
        {
            if (_health == null) _health = GetComponent<Health>();
            if (_health == null || _health.IsDead) return;
            if (Time.time < _nextServerFireTime) return;

            if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;
            direction = direction.normalized;

            if (projectilePrefab == null)
            {
                MPLog.Warn("SHOOT_DENIED", ("owner", rpcParams.Receive.SenderClientId), ("reason", "no-projectile-prefab"));
                return;
            }

            _nextServerFireTime = Time.time + Mathf.Max(MinimumInterval, fireInterval);

            Vector3 muzzle = MuzzlePosition;
            GameObject instance = Instantiate(projectilePrefab, muzzle, Quaternion.LookRotation(direction, Vector3.up));
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            Projectile projectile = instance.GetComponent<Projectile>();

            if (networkObject == null || projectile == null)
            {
                MPLog.Error("SHOOT_DENIED", ("owner", rpcParams.Receive.SenderClientId), ("reason", "projectile-prefab-invalid"));
                Destroy(instance);
                return;
            }

            networkObject.Spawn();
            projectile.Launch(OwnerClientId, direction, damage, projectileSpeed, projectileLifetime);

            MPLog.Info("PLAYER_SHOOT", ("owner", rpcParams.Receive.SenderClientId), ("damage", damage));
        }

        private static bool HasArgument(string name)
        {
            try
            {
                foreach (string argument in Environment.GetCommandLineArgs())
                {
                    if (argument == name) return true;
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
