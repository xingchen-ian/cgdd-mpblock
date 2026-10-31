using MPBlocks.Core;
using MPBlocks.Sync;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Turns an approved <see cref="Interactable"/> into a persistent on/off state: the
    /// server flips a <see cref="SyncedBool"/>, and every machine — including a player who
    /// joins later — repaints a renderer from that value.
    ///
    /// This is the smallest complete example of the MPBlocks interaction model:
    /// the client only *asks* to interact, <see cref="Interactable"/> validates and
    /// approves on the server, and the result is replicated state rather than a one-off
    /// event. That is why late joiners see the current pad colour without any replay.
    ///
    /// The block adds no RPCs of its own: <see cref="Interactable"/> already owns
    /// request → validate → approve, so this component only reacts to the approval on the
    /// server and to <see cref="SyncedBool.OnChanged"/> everywhere else.
    /// </summary>
    public sealed class InteractToggle : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Display name of the toggle, used only in the Inspector and in [MP] logs (TOGGLE_FLIPPED). It is not a network identifier, so renaming it does not affect sync.")]
        [SerializeField] private string label = "toggle";

        [Tooltip("Interactable that drives this toggle. The server subscribes to its Approved event and flips state when it fires; clients do not subscribe. Leave it empty and the toggle never changes.")]
        [SerializeField] private Interactable interactable;

        [Tooltip("Server-owned on/off state (SyncedBool). This component only writes it on the server via SetServerValue, and every machine (including late joiners) repaints from OnChanged or the current value. Without it nothing is synced.")]
        [SerializeField] private SyncedBool state;

        [Tooltip("Renderer repainted from the toggle state; optional. If empty only the bool is synced and no colour is shown. Writing through material creates a material instance at runtime; that is fine for demos but best avoided in performance-sensitive scenes.")]
        [SerializeField] private Renderer targetRenderer;

        [Tooltip("Colour shown on the renderer while the state is false. Only used when targetRenderer is set; the colour goes into the material instance and does not affect other objects that share the same material.")]
        [SerializeField] private Color offColor = new Color(0.25f, 0.25f, 0.25f);

        [Tooltip("Colour shown on the renderer while the state is true. Late joiners repaint once from the current state on spawn, so they see the correct colour immediately.")]
        [SerializeField] private Color onColor = Color.white;

        /// <summary>Name used in [MP] log lines.</summary>
        public string Label
        {
            get => label;
            set => label = value;
        }

        /// <summary>The interaction that drives this toggle.</summary>
        public Interactable Interactable
        {
            get => interactable;
            set => interactable = value;
        }

        /// <summary>Server-owned state that every machine replays on spawn.</summary>
        public SyncedBool State
        {
            get => state;
            set => state = value;
        }

        /// <summary>Renderer repainted when the state changes. May be left empty.</summary>
        public Renderer TargetRenderer
        {
            get => targetRenderer;
            set => targetRenderer = value;
        }

        /// <summary>Colour applied while the toggle is off.</summary>
        public Color OffColor
        {
            get => offColor;
            set => offColor = value;
        }

        /// <summary>Colour applied while the toggle is on.</summary>
        public Color OnColor
        {
            get => onColor;
            set => onColor = value;
        }

        /// <summary>Current state, safe to read on any machine.</summary>
        public bool Value => state != null && state.Value;

        /// <summary>
        /// Code-first wiring for the three references. Safe to call before or after the
        /// object spawns; if it is already spawned the listeners are re-bound and the
        /// renderer immediately shows the current state.
        /// </summary>
        public void Bind(Interactable source, SyncedBool syncedState, Renderer visual)
        {
            bool spawned = IsSpawned;
            if (spawned) Unsubscribe();

            interactable = source;
            state = syncedState;
            targetRenderer = visual;

            if (spawned) Subscribe();
        }

        /// <inheritdoc />
        public override void OnNetworkSpawn()
        {
            Subscribe();
        }

        /// <inheritdoc />
        public override void OnNetworkDespawn()
        {
            Unsubscribe();
        }

        /// <summary>Repaints the target renderer from a state value. Safe on any machine.</summary>
        public void ApplyState(bool on)
        {
            if (targetRenderer == null) return;

            targetRenderer.material.color = on ? onColor : offColor;
        }

        private void Subscribe()
        {
            // Only the server may change the state, so only the server needs approvals.
            // Clients learn the result through the replicated SyncedBool instead.
            if (IsServer && interactable != null)
            {
                interactable.Approved += HandleApproved;
            }

            if (state != null)
            {
                state.OnChanged.AddListener(HandleStateChanged);

                // NetworkVariable.OnValueChanged does not fire for the value a late
                // joiner receives during spawn, so apply the current value once here.
                ApplyState(state.Value);
            }
        }

        private void Unsubscribe()
        {
            if (interactable != null) interactable.Approved -= HandleApproved;
            if (state != null) state.OnChanged.RemoveListener(HandleStateChanged);
        }

        private void HandleApproved(ulong clientId)
        {
            if (!IsServer || state == null) return;

            // The server decides; the network replicates the decision to everyone.
            state.SetServerValue(!state.Value);
            MPLog.Info("TOGGLE_FLIPPED", ("block", "InteractToggle"), ("label", label), ("value", state.Value), ("clientId", clientId));
        }

        private void HandleStateChanged(bool on)
        {
            ApplyState(on);
        }
    }
}
