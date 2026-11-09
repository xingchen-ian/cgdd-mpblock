using MPBlocks.Core;
using MPBlocks.Sync;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Sample locomotion driver for a rigged character.
    ///
    /// Only the owning client writes the Animator (it knows the input). A
    /// <see cref="SyncedAnimator"/> on the same player replicates the selected parameters to
    /// everyone else, so remote copies animate too. The Animator usually lives on the visual
    /// prefab instantiated by <see cref="PlayerAppearance"/>; this block rebinds automatically
    /// when the chosen character changes.
    ///
    /// Expected Animator parameters (optional — missing ones are skipped):
    ///   - float  Speed     (0 idle .. 1 full run, or metres per second)
    ///   - bool   Grounded  (jumping/falling when false)
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerAnimator : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Shown in [MP] log lines.")]
        [SerializeField] private string label = "player";

        [Tooltip("Animator to drive. Leave empty to find the first Animator under the player (including the spawned character visual).")]
        [SerializeField] private Animator animator;

        [Tooltip("PlayerAppearance on this player. Used to rebind the Animator when the chosen character changes. Found automatically when empty.")]
        [SerializeField] private PlayerAppearance appearance;

        [Tooltip("SyncedAnimator on this player. When present, its Animator is rebound with the new character and the parameters are replicated to other clients.")]
        [SerializeField] private SyncedAnimator syncedAnimator;

        [Header("Locomotion")]
        [Tooltip("Float parameter receiving the movement speed. Leave empty to skip. Use the same name in SyncedAnimator's float parameters to replicate it.")]
        [SerializeField] private string speedParameter = "Speed";

        [Tooltip("Float parameter receiving the strafe input (-1 left, 1 right). Leave empty to skip. Use the same name in SyncedAnimator's float parameters to replicate it.")]
        [SerializeField] private string moveXParameter = "MoveX";

        [Tooltip("Float parameter receiving the forward input (-1 back, 1 forward). Leave empty to skip.")]
        [SerializeField] private string moveZParameter = "MoveZ";

        [Tooltip("Bool parameter receiving grounded state. Leave empty to skip. Use the same name in SyncedAnimator's bool parameters to replicate it.")]
        [SerializeField] private string groundedParameter = "Grounded";

        [Tooltip("How quickly the speed parameter follows the measured speed (higher = snappier).")]
        [SerializeField] private float speedSmoothing = 12f;

        [Tooltip("Root bone whose horizontal motion is locked after every animation frame, so a clip can never slide the model away from the player object (and its camera). Mixamo names it mixamorig:Hips; leave empty to disable.")]
        [SerializeField] private string rootBoneName = "mixamorig:Hips";

        [Tooltip("Layers counted as ground for the grounded parameter.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("Downward ray length used to test the ground, in metres.")]
        [SerializeField] private float groundCheckDistance = 0.35f;

        private int _speedHash;
        private int _groundedHash;
        private int _moveXHash;
        private int _moveZHash;
        private bool _hasSpeed;
        private bool _hasMoveX;
        private bool _hasMoveZ;
        private bool _hasGrounded;
        private float _smoothedSpeed;
        private Transform _rootBone;
        private Vector3 _rootBoneBind;
        private Vector3 _lastPosition;
        private bool _hasLastPosition;

        /// <summary>Shown in [MP] log lines.</summary>
        public string Label => label;

        /// <summary>Animator being driven, or null before binding.</summary>
        public Animator Animator => animator;

        /// <summary>Code-first: the Animator to drive.</summary>
        public void SetAnimator(Animator value)
        {
            animator = value;
            RebindParameters();
        }

        /// <summary>Code-first: parameter receiving the movement speed.</summary>
        public void SetSpeedParameter(string value) => speedParameter = value;

        /// <summary>Code-first: parameter receiving grounded state.</summary>
        public void SetGroundedParameter(string value) => groundedParameter = value;

        /// <summary>Code-first: label used in [MP] log lines.</summary>
        public void SetLabel(string value) => label = value;

        public override void OnNetworkSpawn()
        {
            if (appearance == null) appearance = GetComponent<PlayerAppearance>();
            if (syncedAnimator == null) syncedAnimator = GetComponent<SyncedAnimator>();

            if (appearance != null) appearance.VisualChanged += HandleVisualChanged;

            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            RebindParameters();

            _lastPosition = transform.position;
            _hasLastPosition = true;

            MPLog.Info("PLAYER_ANIMATOR_READY",
                ("label", label),
                ("clientId", OwnerClientId),
                ("animator", animator != null ? animator.name : "none"),
                ("rootBone", _rootBone != null ? _rootBone.name : "none"));
        }

        public override void OnNetworkDespawn()
        {
            if (appearance != null) appearance.VisualChanged -= HandleVisualChanged;
        }

        private void HandleVisualChanged(GameObject visual)
        {
            // The character visual was replaced: drive the new rig and let SyncedAnimator
            // bind its Animator as well.
            animator = visual != null ? visual.GetComponentInChildren<Animator>(true) : null;
            if (animator == null) animator = GetComponentInChildren<Animator>(true);

            RebindParameters();
            syncedAnimator?.RebindAnimator();
        }

        private void RebindParameters()
        {
            _hasSpeed = false;
            _hasGrounded = false;
            _hasMoveX = false;
            _hasMoveZ = false;

            BindRootBone();

            if (animator == null) return;

            _speedHash = Animator.StringToHash(speedParameter);
            _groundedHash = Animator.StringToHash(groundedParameter);
            _moveXHash = string.IsNullOrEmpty(moveXParameter) ? 0 : Animator.StringToHash(moveXParameter);
            _moveZHash = string.IsNullOrEmpty(moveZParameter) ? 0 : Animator.StringToHash(moveZParameter);

            foreach (var parameter in animator.parameters)
            {
                if (parameter.nameHash == _speedHash) _hasSpeed = true;
                if (parameter.nameHash == _groundedHash) _hasGrounded = true;
                if (_moveXHash != 0 && parameter.nameHash == _moveXHash) _hasMoveX = true;
                if (_moveZHash != 0 && parameter.nameHash == _moveZHash) _hasMoveZ = true;
            }
        }

        /// <summary>
        /// Finds the hip bone and remembers its rest position. Humanoid clips can still carry
        /// horizontal translation on that bone even with root motion disabled and the clip
        /// baked, which is what makes a character wander out of the camera view.
        /// </summary>
        private void BindRootBone()
        {
            _rootBone = null;
            if (string.IsNullOrEmpty(rootBoneName) || animator == null) return;

            foreach (var candidate in animator.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == rootBoneName)
                {
                    _rootBone = candidate;
                    break;
                }
            }

            if (_rootBone == null)
            {
                foreach (var candidate in animator.GetComponentsInChildren<Transform>(true))
                {
                    if (candidate.name.EndsWith("Hips", System.StringComparison.OrdinalIgnoreCase))
                    {
                        _rootBone = candidate;
                        break;
                    }
                }
            }

            if (_rootBone == null) return;

            // Prefer the avatar's bind pose over the current animated pose: locking to a
            // mid-animation offset would pin the character slightly off centre forever.
            _rootBoneBind = _rootBone.localPosition;

            var avatar = animator.avatar;
            if (avatar != null)
            {
                var skeleton = avatar.humanDescription.skeleton;
                for (int i = 0; i < skeleton.Length; i++)
                {
                    if (skeleton[i].name != _rootBone.name) continue;
                    _rootBoneBind = skeleton[i].position;
                    break;
                }
            }
        }

        /// <summary>Runs after the Animator evaluated, so the correction wins for this frame.</summary>
        private void LateUpdate()
        {
            if (_rootBone == null) return;

            var position = _rootBone.localPosition;
            if (Mathf.Approximately(position.x, _rootBoneBind.x) && Mathf.Approximately(position.z, _rootBoneBind.z)) return;

            _rootBone.localPosition = new Vector3(_rootBoneBind.x, position.y, _rootBoneBind.z);
        }

        private void Update()
        {
            // The owner drives the animation; remote copies read the values replicated by
            // SyncedAnimator instead of trying to simulate input.
            if (!IsSpawned || !IsOwner) return;
            BindRootBone();

            if (animator == null) return;

            float speed = 0f;
            if (_hasLastPosition)
            {
                Vector3 delta = transform.position - _lastPosition;
                speed = delta.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            }

            _lastPosition = transform.position;
            _hasLastPosition = true;

            _smoothedSpeed = Mathf.Lerp(_smoothedSpeed, speed, Mathf.Clamp01(speedSmoothing * Time.deltaTime));

            if (_hasSpeed) animator.SetFloat(_speedHash, _smoothedSpeed);

            // Direction drives the 2D blend tree so strafing/backpedalling plays the matching clip.
            if (_hasMoveX) animator.SetFloat(_moveXHash, Input.GetAxisRaw("Horizontal"));
            if (_hasMoveZ) animator.SetFloat(_moveZHash, Input.GetAxisRaw("Vertical"));
            if (_hasGrounded)
            {
                bool grounded = Physics.Raycast(
                    transform.position + Vector3.up * 0.1f,
                    Vector3.down,
                    Mathf.Max(0.05f, groundCheckDistance),
                    groundMask,
                    QueryTriggerInteraction.Ignore);

                animator.SetBool(_groundedHash, grounded);
            }
        }
    }
}
