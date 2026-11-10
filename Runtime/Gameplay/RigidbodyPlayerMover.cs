using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Sample Rigidbody player controller for networked movement.
    ///
    /// Movement is produced by physics instead of by writing <c>transform.position</c>,
    /// but the replication model is exactly the same as <see cref="SimplePlayerMover"/>:
    /// the owning client simulates its own body, and <see cref="MPBlocks.Sync.SyncedTransform"/>
    /// (owner authority) replicates the resulting transform to everyone else. Every remote
    /// copy of the body is made kinematic, because two machines simulating the same body
    /// would fight each other.
    ///
    /// Requirements on the player prefab: `NetworkObject`, `Rigidbody`, a collider, and
    /// `SyncedTransform` with `Authority = Owner`. Do not use this and
    /// <see cref="SimplePlayerMover"/> on the same object.
    ///
    /// Rigidbody tips: freeze rotation X/Z so the capsule cannot tip over, enable
    /// interpolation, and use a single capsule collider. Physics state (velocity, forces,
    /// contacts) is not synchronized — see docs/recipes/rigidbody-sync.md.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class RigidbodyPlayerMover : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Shown in the Inspector and in [MP] log lines.")]
        [SerializeField] private string label = "player";

        [Header("Movement")]
        [Tooltip("Speed while Shift is held, in metres per second. The animation switches to Run above about 2.5 m/s.")]
        [SerializeField] private float runSpeed = 4.5f;

        [Tooltip("Hold this key to use Run Speed.")]
        [SerializeField] private KeyCode runKey = KeyCode.LeftShift;

        [Tooltip("Horizontal speed in metres per second at full input. Applied in FixedUpdate; vertical velocity (gravity/jump) is preserved.")]
        [SerializeField] private float moveSpeed = 5f;

        [Tooltip("Degrees per mouse unit while holding the right mouse button. The resulting yaw is applied to the Rigidbody in FixedUpdate.")]
        [SerializeField] private float lookSensitivity = 2.5f;

        [Tooltip("Upward speed applied when jumping from the ground. Set 0 to disable jumping; the jump key is Space.")]
        [SerializeField] private float jumpSpeed = 5f;

        [Tooltip("Layers treated as ground for the jump raycast. Set this to your ground/props layer so a player standing on another player does not count as grounded by accident.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("Length of the downward ray that decides whether the player is grounded, in metres. Start slightly inside the capsule and keep it short.")]
        [SerializeField] private float groundCheckDistance = 0.35f;

        [Header("Camera")]
        [Tooltip("Camera enabled only on the owning client. May be left empty when the camera is handled elsewhere.")]
        [SerializeField] private Camera playerCamera;

        [Tooltip("Local-space position applied to the player camera on the owning client. (0,0,0) keeps the prefab value, which for a human character puts the camera inside its head. Try (0, 2.0, -3.5) for third person.")]
        [SerializeField] private Vector3 cameraOffset;

        private Rigidbody _body;
        private Health _health;
        private float _yaw;
        private float _appliedYaw;
        private bool _grounded;
        private float _groundProbeOffset = 0.1f;
        private readonly RaycastHit[] _groundHits = new RaycastHit[8];
        private bool _jumpQueued;

        /// <summary>Shown in [MP] log lines.</summary>
        public string Label => label;

        /// <summary>Horizontal movement speed in metres per second.</summary>
        public float MoveSpeed => moveSpeed;

        /// <summary>Mouse sensitivity while the right mouse button is held.</summary>
        public float LookSensitivity => lookSensitivity;

        /// <summary>Upward speed of a jump; 0 disables jumping.</summary>
        public float JumpSpeed => jumpSpeed;

        /// <summary>Layers used by the ground raycast.</summary>
        public LayerMask GroundMask => groundMask;

        /// <summary>Code-first: camera that should only be enabled for the owner.</summary>
        public void SetPlayerCamera(Camera value) => playerCamera = value;

        /// <summary>Code-first: horizontal speed, clamped to at least 0.1.</summary>
        public void SetMoveSpeed(float value) => moveSpeed = Mathf.Max(0.1f, value);

        /// <summary>Code-first: mouse sensitivity, clamped to at least 0.</summary>
        public void SetLookSensitivity(float value) => lookSensitivity = Mathf.Max(0f, value);

        /// <summary>Code-first: jump speed, clamped to at least 0 (0 disables jumping).</summary>
        public void SetJumpSpeed(float value) => jumpSpeed = Mathf.Max(0f, value);

        /// <summary>Code-first: layers used by the ground raycast.</summary>
        public void SetGroundMask(LayerMask value) => groundMask = value;

        /// <summary>Code-first: label used in [MP] log lines.</summary>
        public void SetLabel(string value) => label = value;

        public override void OnNetworkSpawn()
        {
            _body = GetComponent<Rigidbody>();
            _health = GetComponent<Health>();

            if (!IsOwner)
            {
                // Remote copies must not simulate: SyncedTransform drives their transform.
                // (NetworkRigidbody from NGO does the same thing automatically; use one or
                // the other, not both.)
                _body.isKinematic = true;
                _body.interpolation = RigidbodyInterpolation.None;
                return;
            }

            // The owner simulates; ensure the body was not left kinematic in the prefab.
            _body.isKinematic = false;
            _body.interpolation = RigidbodyInterpolation.Interpolate;

            // The controller owns the yaw: freezing all physics rotation stops contacts
            // from injecting angular velocity, which is what made the child camera shake
            // left/right when the capsule bumped into something.
            _body.constraints = RigidbodyConstraints.FreezeRotation;
            _body.angularVelocity = Vector3.zero;
            var capsule = GetComponent<CapsuleCollider>();
            if (capsule != null) _groundProbeOffset = capsule.center.y - capsule.height * 0.5f + 0.1f;

            _yaw = transform.eulerAngles.y;
            _appliedYaw = _yaw;
            _body.rotation = Quaternion.Euler(0f, _yaw, 0f);

            if (playerCamera != null)
            {
                playerCamera.enabled = true;
                if (cameraOffset != Vector3.zero) playerCamera.transform.localPosition = cameraOffset;

                // Two spawned players would otherwise leave two enabled listeners in the scene.
                var listener = playerCamera.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = true;
            }

            MPLog.Info("RIGIDBODY_PLAYER_READY", ("label", label), ("clientId", OwnerClientId));
        }

        /// <summary>
        /// A rigidbody player's origin is the capsule centre, so a probe 0.1 m above it never
        /// reaches the floor and the jump check below refused to fire. Probe from just above the
        /// capsule bottom and skip our own colliders (a ray started inside them hits them first).
        /// </summary>
        private bool ProbeGround()
        {
            var origin = transform.position + Vector3.up * _groundProbeOffset;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits,
                Mathf.Max(0.05f, groundCheckDistance), groundMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                var hit = _groundHits[i];
                if (hit.collider == null) continue;
                if (hit.collider.transform.IsChildOf(transform)) continue;
                return true;
            }

            return false;
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            if (_health != null && _health.IsDead) return;

            if (Input.GetMouseButton(1))
            {
                _yaw += Input.GetAxis("Mouse X") * lookSensitivity;
            }

            _grounded = ProbeGround();

            // Read the jump key here: GetKeyDown can be missed in FixedUpdate.
            if (Input.GetKeyDown(KeyCode.Space) && !_jumpQueued)
            {
                if (_grounded)
                {
                    _jumpQueued = true;
                    MPLog.Info("PLAYER_JUMP_QUEUED", ("label", label), ("clientId", OwnerClientId), ("grounded", true));
                }
                else
                {
                    MPLog.Warn("PLAYER_JUMP_REFUSED", ("label", label), ("clientId", OwnerClientId),
                        ("reason", "the ground probe found nothing — check the floor collider and groundMask"));
                }
            }
        }

        private void FixedUpdate()
        {
            if (!IsSpawned || !IsOwner) return;
            if (_body == null || _body.isKinematic) return;
            if (_health != null && _health.IsDead) return;

            float x = Input.GetAxisRaw("Horizontal");
            float z = Input.GetAxisRaw("Vertical");

            var rotation = Quaternion.Euler(0f, _yaw, 0f);
            var direction = rotation * new Vector3(x, 0f, z);
            if (direction.sqrMagnitude > 1f) direction.Normalize();

            float speed = Input.GetKey(runKey) ? Mathf.Max(moveSpeed, runSpeed) : moveSpeed;

            var velocity = _body.velocity;
            _body.velocity = new Vector3(direction.x * speed, velocity.y, direction.z * speed);

            // Only touch rotation when the mouse actually changed the yaw. Continuously
            // re-writing the rotation while in contact with a collider fights the contact
            // solver and makes the view jitter.
            if (Mathf.Abs(Mathf.DeltaAngle(_appliedYaw, _yaw)) > 0.01f)
            {
                _appliedYaw = _yaw;
                _body.rotation = Quaternion.Euler(0f, _yaw, 0f);
            }

            // Rotation is frozen, but zero this anyway in case a contact slips one through.
            _body.angularVelocity = Vector3.zero;

            if (!_jumpQueued) return;

            _jumpQueued = false;
            if (!_grounded) return;

            _body.velocity = new Vector3(direction.x * speed, jumpSpeed, direction.z * speed);
            MPLog.Info("PLAYER_JUMP", ("label", label), ("clientId", OwnerClientId), ("jumpSpeed", jumpSpeed));
        }
    }
}
