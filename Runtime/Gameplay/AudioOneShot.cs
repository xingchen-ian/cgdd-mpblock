using MPBlocks.Core;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Plays one clip on a game event, on any object — the non-interactable twin of
    /// <see cref="InteractSound"/>.
    ///
    /// The common uses:
    /// - a bullet prefab: Trigger = OnEnable, clip = Shot_Fire (every machine hears the shot
    ///   because the projectile is spawned everywhere);
    /// - a player prefab: Trigger = OnDeath / OnHealthChanged with the matching clips;
    /// - anything else: Trigger = Manual and call <see cref="Play"/> from your own code.
    ///
    /// Placeholder clips are generated into Assets/Audio by
    /// MPBlocks > Audio > Generate Placeholder Sounds; drag one into Clip.
    /// </summary>
    public sealed class AudioOneShot : MonoBehaviour
    {
        /// <summary>When the clip fires.</summary>
        public enum Trigger
        {
            /// <summary>Only when something calls Play().</summary>
            Manual,
            /// <summary>When this object becomes active — right after a prefab is instantiated.</summary>
            OnEnable,
            /// <summary>Every time the watched Health changes (rate limited).</summary>
            OnHealthChanged,
            /// <summary>When the watched Health reaches zero.</summary>
            OnDeath,
        }

        [Header("MPBlocks")]
        [Tooltip("Shown in [MP] log lines.")]
        [SerializeField] private string label = "sfx";

        [Tooltip("Clip to play. Generate placeholders with MPBlocks > Audio > Generate Placeholder Sounds.")]
        [SerializeField] private AudioClip clip;

        [Tooltip("What makes it fire. Manual means your code calls Play().")]
        [SerializeField] private Trigger trigger = Trigger.Manual;

        [Tooltip("Health watched by OnHealthChanged / OnDeath. Found on the same object when empty.")]
        [SerializeField] private Health health;

        [Range(0f, 1f)]
        [SerializeField] private float volume = 0.8f;

        [Range(0f, 1f)]
        [Tooltip("0 = flat 2D, 1 = fully positioned in 3D.")]
        [SerializeField] private float spatialBlend = 0.6f;

        [Tooltip("Minimum seconds between two plays of OnHealthChanged, so a burst of damage does not stack into noise.")]
        [SerializeField] private float minInterval = 0.08f;

        private AudioSource _source;
        private float _lastPlayTime = -999f;

        /// <summary>The clip this component plays.</summary>
        public AudioClip Clip => clip;

        private void Awake()
        {
            if (health == null) health = GetComponent<Health>();
            _source = GetComponent<AudioSource>();
        }

        private void OnEnable()
        {
            if (trigger == Trigger.OnEnable)
            {
                Play();
                return;
            }

            if (health == null) return;

            if (trigger == Trigger.OnHealthChanged) health.Changed += HandleHealthChanged;
            else if (trigger == Trigger.OnDeath) health.Died += Play;
        }

        private void OnDisable()
        {
            if (health == null) return;

            if (trigger == Trigger.OnHealthChanged) health.Changed -= HandleHealthChanged;
            else if (trigger == Trigger.OnDeath) health.Died -= Play;
        }

        private void HandleHealthChanged(int current, int max) => Play();

        /// <summary>Plays the assigned clip. Safe to call from a UnityEvent or your own code.</summary>
        public void Play()
        {
            if (clip == null) return;

            if (Time.time - _lastPlayTime < Mathf.Max(0f, minInterval)) return;
            _lastPlayTime = Time.time;

            if (_source == null)
            {
                _source = GetComponent<AudioSource>();
                if (_source == null)
                {
                    _source = gameObject.AddComponent<AudioSource>();
                    _source.playOnAwake = false;
                }
            }

            _source.spatialBlend = spatialBlend;
            _source.PlayOneShot(clip, volume);

            MPLog.Info("AUDIO_ONESHOT", ("label", label), ("clip", clip.name), ("trigger", trigger.ToString()));
        }

        // ---- code-first wiring ----

        public void SetClip(AudioClip value) => clip = value;

        public void SetTrigger(Trigger value) => trigger = value;

        public void SetHealth(Health value) => health = value;

        public void SetVolume(float value) => volume = Mathf.Clamp01(value);
    }
}
