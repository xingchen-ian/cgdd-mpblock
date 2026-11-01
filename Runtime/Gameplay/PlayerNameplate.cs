using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Draws the player's name above their head in the world.
    ///
    /// A world-space canvas is billboarded toward the local camera every frame, so text stays
    /// readable from any angle. Two things are deliberately hidden:
    /// - your own name, which would sit in your face;
    /// - anyone further away than <see cref="maxVisibleDistance"/>.
    ///
    /// The text comes from <see cref="PlayerNameTag"/>, i.e. from the server-owned
    /// `SyncedString`, so a client can never show a name the server did not accept.
    /// </summary>
    public sealed class PlayerNameplate : MonoBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Where the name comes from. Found on the same object when empty.")]
        [SerializeField] private PlayerNameTag nameTag;

        [Tooltip("World-space canvas that is rotated to face the camera.")]
        [SerializeField] private Canvas canvas;

        [Tooltip("Text element that shows the name.")]
        [SerializeField] private Text label;

        [Tooltip("Hide your own nameplate on your own screen.")]
        [SerializeField] private bool hideForOwner = true;

        [Tooltip("Hide nameplates further away than this (metres).")]
        [SerializeField] private float maxVisibleDistance = 30f;

        [Tooltip("Seconds between text refreshes. Text does not need to update every frame.")]
        [SerializeField] private float refreshInterval = 0.25f;

        private NetworkObject _networkObject;
        private Camera _camera;
        private float _timer;
        private string _lastText;

        private void Awake()
        {
            _networkObject = GetComponent<NetworkObject>();

            if (nameTag == null) nameTag = GetComponent<PlayerNameTag>();
            if (canvas == null) canvas = GetComponentInChildren<Canvas>(true);
            if (label == null && canvas != null) label = canvas.GetComponentInChildren<Text>(true);
        }

        private void LateUpdate()
        {
            if (label == null) return;

            var camera = ResolveCamera();

            if (camera != null && canvas != null)
            {
                bool visible = true;

                if (hideForOwner && _networkObject != null && _networkObject.IsOwner)
                {
                    visible = false;
                }
                else if (Vector3.Distance(camera.transform.position, transform.position) > maxVisibleDistance)
                {
                    visible = false;
                }

                if (canvas.enabled != visible) canvas.enabled = visible;

                // Billboard: match the camera's rotation so the text always faces the viewer.
                if (visible) canvas.transform.rotation = camera.transform.rotation;
            }

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = refreshInterval;

            var text = nameTag != null ? nameTag.DisplayName : string.Empty;
            if (text == _lastText) return;

            _lastText = text;
            label.text = text;
            MPLog.Info("NAMEPLATE", ("clientId", _networkObject != null ? _networkObject.OwnerClientId : 0), ("name", text));
        }

        private Camera ResolveCamera()
        {
            if (_camera != null && _camera.isActiveAndEnabled) return _camera;

            _camera = Camera.main;
            if (_camera != null) return _camera;

            // The player camera is not guaranteed to be tagged MainCamera.
            foreach (var candidate in FindObjectsOfType<Camera>())
            {
                if (!candidate.isActiveAndEnabled) continue;
                _camera = candidate;
                break;
            }

            return _camera;
        }
    }
}
