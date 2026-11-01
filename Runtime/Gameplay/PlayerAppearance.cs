using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Applies the character chosen for this player (a tint and/or an optional visual model).
    ///
    /// The index is a server-owned <see cref="NetworkVariable{T}"/>, so every machine — including
    /// a player who joins later — renders the same character. The server picks the initial
    /// character (via <see cref="CharacterSelect"/> when one is in the scene) and changes it
    /// when the player chooses another one.
    ///
    /// It sits on the player prefab and reads the <see cref="CharacterRoster"/> from the scene's
    /// <see cref="NetBootstrap"/>, so the prefab does not need a per-game asset reference.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerAppearance : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Shown in [MP] log lines.")]
        [SerializeField] private string label = "player";

        [Tooltip("Parent for an optional character visual prefab. When empty, the player root is used.")]
        [SerializeField] private Transform visualRoot;

        [Tooltip("Renderer tinted when the chosen character has no Visual Prefab. When empty, the first Renderer in the children is used (the Body capsule).")]
        [SerializeField] private Renderer bodyRenderer;

        private readonly NetworkVariable<int> _character = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<bool> _locked = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private CharacterRoster _roster;
        private GameObject _visualInstance;
        private MaterialPropertyBlock _block;

        /// <summary>Currently applied character index (replicated).</summary>
        public int CharacterIndex => _character.Value;

        /// <summary>True once this player confirmed their character; further changes are refused.</summary>
        public bool IsLocked => _locked.Value;

        /// <summary>The visual prefab instance for the current character, or null when the character only tints the body.</summary>
        public GameObject VisualInstance => _visualInstance;

        /// <summary>Raised on every machine after the appearance changes (visual instance may be null for tint-only characters). Animation scripts use this to rebind their Animator.</summary>
        public event System.Action<GameObject> VisualChanged;

        /// <summary>Shown in [MP] log lines.</summary>
        public string Label => label;

        /// <summary>True when a roster with at least one character is configured.</summary>
        public bool HasRoster => _roster != null && _roster.Count > 0;

        /// <summary>Code-first: parent for optional visual models.</summary>
        public void SetVisualRoot(Transform value) => visualRoot = value;

        /// <summary>Code-first: renderer tinted when the character has no visual prefab.</summary>
        public void SetBodyRenderer(Renderer value) => bodyRenderer = value;

        /// <summary>Code-first: label used in [MP] log lines.</summary>
        public void SetLabel(string value) => label = value;

        public override void OnNetworkSpawn()
        {
            var bootstrap = FindObjectOfType<NetBootstrap>();
            _roster = bootstrap != null && bootstrap.Config != null ? bootstrap.Config.CharacterRoster : null;

            if (visualRoot == null) visualRoot = transform;
            if (bodyRenderer == null) bodyRenderer = GetComponentInChildren<Renderer>();

            _character.OnValueChanged += HandleCharacterChanged;

            if (IsServer)
            {
                if (CharacterSelect.Instance != null)
                {
                    CharacterSelect.Instance.RegisterPlayerServer(this);
                }
                else
                {
                    AssignDefaultCharacter();
                }
            }

            Apply(_character.Value);
        }

        public override void OnNetworkDespawn()
        {
            _character.OnValueChanged -= HandleCharacterChanged;

            if (_visualInstance != null)
            {
                Destroy(_visualInstance);
                _visualInstance = null;
            }
        }

        private void HandleCharacterChanged(int previous, int current) => Apply(current);

        private void AssignDefaultCharacter()
        {
            if (!HasRoster) return;

            int index = (int)(OwnerClientId % (ulong)_roster.Count);
            SetCharacterServer(index);
        }

        /// <summary>Server only. Freezes this player's character choice.</summary>
        public void SetLockedServer(bool value)
        {
            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "PlayerAppearance"), ("label", label), ("caller", "client"));
                return;
            }

            if (_locked.Value == value) return;

            _locked.Value = value;
            MPLog.Info("CHARACTER_LOCKED", ("label", label), ("clientId", OwnerClientId), ("locked", value));
        }

        /// <summary>Server only. Selects the character index applied to this player.</summary>
        public void SetCharacterServer(int index)
        {
            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "PlayerAppearance"), ("label", label), ("caller", "client"));
                return;
            }

            if (!HasRoster)
            {
                MPLog.Warn("CHARACTER_NO_ROSTER", ("label", label), ("clientId", OwnerClientId));
                return;
            }

            index = Mathf.Clamp(index, 0, _roster.Count - 1);
            if (_character.Value == index)
            {
                Apply(index);
                return;
            }

            _character.Value = index;
            var definition = _roster.Get(index);
            MPLog.Info("PLAYER_CHARACTER_SET",
                ("label", label),
                ("clientId", OwnerClientId),
                ("index", index),
                ("character", definition != null ? definition.DisplayName : index.ToString()));
        }

        private void Apply(int index)
        {
            if (!HasRoster) return;

            var definition = _roster.Get(index);
            if (definition == null) return;

            if (definition.VisualPrefab != null)
            {
                if (_visualInstance != null) Destroy(_visualInstance);

                var parent = visualRoot != null ? visualRoot : transform;
                _visualInstance = Instantiate(definition.VisualPrefab, parent);
                _visualInstance.transform.localPosition = Vector3.zero;
                _visualInstance.transform.localRotation = Quaternion.identity;

                if (bodyRenderer != null) bodyRenderer.enabled = false;
                VisualChanged?.Invoke(_visualInstance);
                return;
            }

            if (_visualInstance != null)
            {
                Destroy(_visualInstance);
                _visualInstance = null;
            }

            if (bodyRenderer == null)
            {
                VisualChanged?.Invoke(null);
                return;
            }

            bodyRenderer.enabled = true;
            _block ??= new MaterialPropertyBlock();
            bodyRenderer.GetPropertyBlock(_block);
            _block.SetColor("_BaseColor", definition.Color);
            _block.SetColor("_Color", definition.Color);
            bodyRenderer.SetPropertyBlock(_block);

            VisualChanged?.Invoke(null);
        }
    }
}
