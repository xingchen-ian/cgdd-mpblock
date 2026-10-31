using MPBlocks.Core;
using MPBlocks.Sync;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// A door that opens once enough levers have been pulled — the template's
    /// "two people have to cooperate" rule.
    ///
    /// The rule lives on the server: it counts approved lever interactions and flips
    /// <see cref="SyncedBool"/>. Every machine then animates the panel locally, which is
    /// why the door looks smooth without any position syncing.
    /// </summary>
    public sealed class CoopDoor : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Synced on/off state of the door (SyncedBool). Only the server writes it through SetServerValue once enough levers are pulled; clients just read it, and late joiners receive the current value on spawn.")]
        [SerializeField] private SyncedBool isOpen;

        [Tooltip("Levers that count towards opening the door; each entry should be an Interactable in the scene. The server subscribes to their Approved events on spawn and each lever counts only once, so a missing or duplicated reference keeps the door shut forever.")]
        [SerializeField] private Interactable[] levers;

        [Tooltip("Transform of the visual panel that slides when the door opens; optional. If left empty the isOpen state still syncs but the door never moves. The closed position is captured from localPosition on spawn, so do not move it at runtime.")]
        [SerializeField] private Transform doorPanel;

        [Tooltip("Local offset in metres the panel slides by when open, default 3.4 up. It is in the object's local space, not world space; the panel lerps between the closed position and the closed position plus this offset.")]
        [SerializeField] private Vector3 openOffset = new Vector3(0f, 3.4f, 0f);

        [Tooltip("How many levers must be pulled before the server sets isOpen to true. Clamped to at least 1; if it is larger than the levers array the door never opens, and pulled/required in the [MP] logs never match.")]
        [SerializeField] private int requiredLevers = 2;

        [Tooltip("Seconds the panel takes to slide. 0 or negative snaps it instantly; this is a local visual animation only and does not affect sync or approval.")]
        [SerializeField] private float slideSeconds = 0.6f;

        private Vector3 _closedPosition;
        private Vector3 _openPosition;
        private float _progress;

        private int _pulledLevers;
        private readonly System.Collections.Generic.HashSet<int> _usedLevers = new System.Collections.Generic.HashSet<int>();
        private System.Action<ulong>[] _handlers;

        public override void OnNetworkSpawn()
        {
            if (doorPanel != null)
            {
                _closedPosition = doorPanel.localPosition;
                _openPosition = _closedPosition + openOffset;
            }

            if (isOpen != null)
            {
                isOpen.OnChanged.AddListener(HandleOpenChanged);
                HandleOpenChanged(isOpen.Value);
            }

            if (IsServer && levers != null)
            {
                _handlers = new System.Action<ulong>[levers.Length];
                for (int i = 0; i < levers.Length; i++)
                {
                    if (levers[i] == null) continue;

                    int index = i;
                    _handlers[i] = _ => HandleLeverApproved(index);
                    levers[i].Approved += _handlers[i];
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            if (isOpen != null) isOpen.OnChanged.RemoveListener(HandleOpenChanged);

            if (levers != null && _handlers != null)
            {
                for (int i = 0; i < levers.Length && i < _handlers.Length; i++)
                {
                    if (levers[i] == null || _handlers[i] == null) continue;
                    levers[i].Approved -= _handlers[i];
                }
            }
        }

        private void HandleLeverApproved(int leverIndex)
        {
            if (!IsServer) return;
            if (isOpen != null && isOpen.Value) return;

            // Each lever only counts once, otherwise a single player could pull one
            // lever twice and defeat the whole point of a co-op door.
            if (!_usedLevers.Add(leverIndex)) return;

            _pulledLevers++;
            MPLog.Info("DOOR_PROGRESS", ("pulled", _pulledLevers), ("required", requiredLevers));

            if (_pulledLevers < Mathf.Max(1, requiredLevers)) return;

            MPLog.Info("DOOR_OPENING");
            if (isOpen != null) isOpen.SetServerValue(true);
        }

        private void HandleOpenChanged(bool open)
        {
            if (open && isOpen != null && isOpen.Value && doorPanel != null)
            {
                MPLog.Info("DOOR_OPENED");
            }
        }

        private void Update()
        {
            if (doorPanel == null || isOpen == null) return;

            float target = isOpen.Value ? 1f : 0f;
            if (Mathf.Approximately(_progress, target)) return;

            float step = slideSeconds <= 0f ? 1f : Time.deltaTime / slideSeconds;
            _progress = Mathf.MoveTowards(_progress, target, step);
            doorPanel.localPosition = Vector3.Lerp(_closedPosition, _openPosition, _progress);
        }
    }
}
