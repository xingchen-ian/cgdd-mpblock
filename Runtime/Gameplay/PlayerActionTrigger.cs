using MPBlocks.Core;
using MPBlocks.Sync;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Presses one key to fire an animation Trigger — the missing half of
    /// <see cref="SyncedAnimator"/>, which can already send triggers but needs something to ask.
    ///
    /// The character importer adds a state and a Trigger parameter for every action clip it
    /// finds (attack / punch / kick / wave / …), so the usual setup is:
    ///
    ///   drop attack.fbx into the character folder → Import from Assets/Character →
    ///   press F in game.
    ///
    /// Only the owning client reads the key; everyone else sees the action through
    /// SyncedAnimator.
    /// </summary>
    public sealed class PlayerActionTrigger : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Shown in [MP] log lines.")]
        [SerializeField] private string label = "action";

        [Tooltip("Animator bridge that replicates the trigger.")]
        [SerializeField] private SyncedAnimator syncedAnimator;

        [Tooltip("Trigger parameter to fire. Leave empty to use the first trigger in the controller.")]
        [SerializeField] private string actionParameter = "Attack";

        [Tooltip("Key that fires the action.")]
        [SerializeField] private KeyCode key = KeyCode.F;

        [Tooltip("Minimum seconds between two presses.")]
        [SerializeField] private float cooldown = 0.5f;

        [Tooltip("Keep a Health on this object from acting while dead.")]
        [SerializeField] private bool ignoreWhenDead = true;

        private Health _health;
        private float _nextAllowed;

        /// <summary>Trigger this component fires, resolved on spawn.</summary>
        public string ActionParameter => actionParameter;

        public override void OnNetworkSpawn()
        {
            _health = GetComponent<Health>();

            if (syncedAnimator == null) syncedAnimator = GetComponent<SyncedAnimator>();
            if (syncedAnimator == null)
            {
                MPLog.Warn("ACTION_NO_SYNCED_ANIMATOR", ("label", label));
                return;
            }

            if (IsOwner && !HasAction(actionParameter)) actionParameter = FirstTrigger();

            MPLog.Info("ACTION_READY", ("label", label), ("action", actionParameter));
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner) return;
            if (syncedAnimator == null) return;

            if (ignoreWhenDead && _health != null && _health.IsDead) return;
            if (Time.time < _nextAllowed) return;
            if (!Input.GetKeyDown(key)) return;

            if (!HasAction(actionParameter)) actionParameter = FirstTrigger();
            if (string.IsNullOrEmpty(actionParameter)) return;

            _nextAllowed = Time.time + Mathf.Max(0f, cooldown);
            syncedAnimator.SetTrigger(actionParameter);
            MPLog.Info("PLAYER_ACTION", ("label", label), ("clientId", OwnerClientId), ("action", actionParameter));
        }

        /// <summary>True when the animator actually declares this trigger; "Attack" on a controller without it must not be silent.</summary>
        private bool HasAction(string parameter)
        {
            if (string.IsNullOrEmpty(parameter)) return false;

            var animator = syncedAnimator != null ? syncedAnimator.Animator : null;
            if (animator == null) return false;

            int hash = Animator.StringToHash(parameter);
            foreach (var candidate in animator.parameters)
            {
                if (candidate.type == AnimatorControllerParameterType.Trigger && candidate.nameHash == hash) return true;
            }

            MPLog.Warn("ACTION_TRIGGER_MISSING",
                ("label", label),
                ("parameter", parameter),
                ("hint", "the controller has no such trigger; falling back to the first one"));
            return false;
        }

        private string FirstTrigger()
        {
            var animator = syncedAnimator != null ? syncedAnimator.Animator : null;
            if (animator == null) return string.Empty;

            foreach (var parameter in animator.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Trigger) return parameter.name;
            }

            return string.Empty;
        }

        // ---- code-first API ----

        public void TriggerAction() => syncedAnimator?.SetTrigger(actionParameter);

        public void SetAction(string parameter) => actionParameter = parameter;

        public void SetKey(KeyCode value) => key = value;
    }
}
