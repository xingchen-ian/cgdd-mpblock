using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Minimal keyboard mover for a player avatar: WASD to walk, hold right mouse to
    /// look. It does nothing on machines that do not own the object — movement input is
    /// local, the resulting transform is synced by <c>SyncedTransform</c> with owner
    /// authority — and nothing while the owner is dead (the arena template awards no
    /// points for a corpse that keeps walking into the score zone).
    ///
    /// This is a sample block so a fresh scene is immediately playable. Groups are
    /// expected to replace it with their own controller.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public sealed class SimplePlayerMover : NetworkBehaviour
    {
        [Tooltip("Local player movement speed in metres per second, default 4.5. Input is read only on the client that owns this player and other machines have no input; tune the feel here rather than editing the sync blocks.")]
        [SerializeField] private float moveSpeed = 4.5f;

        [Tooltip("Turning sensitivity in degrees per mouse unit while holding the right mouse button. Owner-only; too high feels dizzy, and 0 makes turning impossible.")]
        [SerializeField] private float lookSensitivity = 2.5f;

        [Tooltip("Player camera; optional. OnNetworkSpawn enables it only on the client that owns this player so nobody sees multiple views; leave it empty and the camera is not managed.")]
        [SerializeField] private Camera playerCamera;

        [Tooltip("Local-space position applied to the player camera on the owning client. (0,0,0) keeps the prefab's own value, which for a human-sized character model puts the camera inside its head. Try (0, 2.0, -3.5) for a third-person view.")]
        [SerializeField] private Vector3 cameraOffset;

        private float _yaw;
        private Health _health;

        public override void OnNetworkSpawn()
        {
            bool isOwner = IsOwner;
            _health = GetComponent<Health>();

            if (playerCamera != null)
            {
                playerCamera.enabled = isOwner;

                if (isOwner && cameraOffset != Vector3.zero)
                {
                    playerCamera.transform.localPosition = cameraOffset;
                }
            }

            if (isOwner)
            {
                _yaw = transform.eulerAngles.y;
                MPLog.Info("LOCAL_PLAYER_READY", ("clientId", OwnerClientId), ("networkObjectId", NetworkObjectId));
            }
        }

        private void Update()
        {
            if (!IsOwner) return;
            if (_health != null && _health.IsDead) return;

            float x = Input.GetAxisRaw("Horizontal");
            float z = Input.GetAxisRaw("Vertical");

            if (x != 0f || z != 0f)
            {
                var move = transform.right * x + transform.forward * z;
                if (move.sqrMagnitude > 1f) move.Normalize();
                transform.position += move * (moveSpeed * Time.deltaTime);
            }

            if (Input.GetMouseButton(1))
            {
                _yaw += Input.GetAxis("Mouse X") * lookSensitivity;
                transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            }
        }
    }
}
