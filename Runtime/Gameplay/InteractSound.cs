using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Plays a sound when an <see cref="Interactable"/> on the same object is approved (or
    /// denied) — the audio twin of the colour change that <see cref="InteractToggle"/> does.
    ///
    /// Lives on the interactable so the clip is edited where the object is, and so every
    /// lever can sound different without touching any script.
    ///
    /// Where it plays: the approval is broadcast to every machine, so by default everyone
    /// hears it, positioned in 3D at the object. Turn on <see cref="localPlayerOnly"/> when
    /// only the player who pressed the key should hear it.
    /// </summary>
    [RequireComponent(typeof(Interactable))]
    public sealed class InteractSound : MonoBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Shown in [MP] log lines.")]
        [SerializeField] private string label = "interact-sound";

        [Tooltip("Interactable to listen to. Found on the same object when empty.")]
        [SerializeField] private Interactable interactable;

        [Tooltip("Played when the server approves an interaction.")]
        [SerializeField] private AudioClip approvedClip;

        [Tooltip("Played when the server refuses an interaction (too far, game rule).")]
        [SerializeField] private AudioClip deniedClip;

        [Range(0f, 1f)]
        [SerializeField] private float volume = 0.8f;

        [Range(0f, 1f)]
        [Tooltip("0 = flat 2D, 1 = fully positional. Levers usually want about 0.7.")]
        [SerializeField] private float spatialBlend = 0.7f;

        [Tooltip("Only the player who pressed the key hears it. Off lets the whole room hear it in 3D.")]
        [SerializeField] private bool localPlayerOnly;

        [Tooltip("AudioSource to use. One is added when empty.")]
        [SerializeField] private AudioSource source;

        /// <summary>Played clip for the last interaction, for tests and code-driven games.</summary>
        public AudioClip LastClip { get; private set; }

        private void Awake()
        {
            if (interactable == null) interactable = GetComponent<Interactable>();

            if (source == null) source = GetComponent<AudioSource>();

            if (source == null)
            {
                source = gameObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
            }

            source.spatialBlend = spatialBlend;
            source.volume = volume;
        }

        private void OnEnable()
        {
            if (interactable == null) return;

            interactable.Approved += HandleApproved;
            interactable.Denied += HandleDenied;
        }

        private void OnDisable()
        {
            if (interactable == null) return;

            interactable.Approved -= HandleApproved;
            interactable.Denied -= HandleDenied;
        }

        private void HandleApproved(ulong clientId)
        {
            if (!IsLocalRelevant(clientId)) return;
            Play(approvedClip, "approved", clientId);
        }

        private void HandleDenied(ulong clientId)
        {
            if (!IsLocalRelevant(clientId)) return;
            Play(deniedClip, "denied", clientId);
        }

        private bool IsLocalRelevant(ulong clientId)
        {
            if (!localPlayerOnly) return true;

            var manager = NetworkManager.Singleton;
            return manager != null && manager.LocalClientId == clientId;
        }

        private void Play(AudioClip clip, string reason, ulong clientId)
        {
            if (clip == null || source == null) return;

            LastClip = clip;
            source.PlayOneShot(clip, volume);
            MPLog.Info("INTERACT_SOUND",
                ("label", label),
                ("clip", clip.name),
                ("reason", reason),
                ("clientId", clientId));
        }

        // ---- code-first wiring ----

        public void SetInteractable(Interactable value) => interactable = value;

        public void SetClips(AudioClip approved, AudioClip denied)
        {
            approvedClip = approved;
            deniedClip = denied;
        }

        public void SetLocalPlayerOnly(bool value) => localPlayerOnly = value;
    }
}
