using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Shows or hides this object depending on the **local player's private role** — the
    /// cheapest way to build asymmetric presentation: "only the seer sees the ghost",
    /// "only the cat sees the rats' footprints".
    ///
    /// It reads <see cref="PlayerPrivateState"/>, which is owner-only, so no other machine
    /// can read the role even if it wanted to.
    ///
    /// Important: this hides **presentation only**. Netcode for GameObjects sends a networked
    /// object's state to everyone, so a hidden object's position is still on the wire. Use this
    /// for a class project; do not mistake it for anti-cheat.
    ///
    /// Never target an object that has a `NetworkObject` on it — hiding a spawned object
    /// breaks replication. Point it at a child visual instead.
    /// </summary>
    public sealed class ShowForRole : MonoBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Shown in the inspector and in [MP] log lines.")]
        [SerializeField] private string label = "role-only";

        [Tooltip("Roles that can see this object. 0 means 'no role assigned'.")]
        [SerializeField] private int[] visibleForRoles = { 1 };

        [Tooltip("Object to toggle. Empty means this GameObject.")]
        [SerializeField] private GameObject target;

        [Tooltip("Also visible before a role has been assigned.")]
        [SerializeField] private bool visibleWhenUnassigned = true;

        private PlayerPrivateState _state;
        private int _lastRole = int.MinValue;

        private GameObject Target
        {
            get
            {
                if (target != null) return target;
                target = gameObject;
                return target;
            }
        }

        private void Awake()
        {
            if (Target.GetComponent<NetworkObject>() != null)
            {
                Debug.LogWarning(
                    $"[MP] WARN ROLE_VISIBILITY_ON_NETWORKED_OBJECT label={label} " +
                    "hiding a NetworkObject breaks replication — point the target at a child visual instead");
            }
        }

        private void Update()
        {
            if (_state == null)
            {
                _state = FindLocalState();
                if (_state == null) return;
            }

            int role = _state.Role;
            if (role == _lastRole) return;

            _lastRole = role;
            Apply(role);
        }

        private void Apply(int role)
        {
            bool visible;
            if (role == 0 && visibleWhenUnassigned)
            {
                visible = true;
            }
            else
            {
                visible = false;
                if (visibleForRoles != null)
                {
                    foreach (int allowed in visibleForRoles)
                    {
                        if (allowed == role)
                        {
                            visible = true;
                            break;
                        }
                    }
                }
            }

            Target.SetActive(visible);
            MPLog.Info("ROLE_VISIBILITY", ("label", label), ("role", role), ("active", visible));
        }

        private static PlayerPrivateState FindLocalState()
        {
            var manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening) return null;
            if (manager.LocalClient == null || manager.LocalClient.PlayerObject == null) return null;

            return manager.LocalClient.PlayerObject.GetComponent<PlayerPrivateState>();
        }
    }
}
