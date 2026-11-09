using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Server-authoritative character selection at session start.
    ///
    /// Put one of these on a scene object (with a <see cref="NetworkObject"/> on the same
    /// GameObject) and assign the scene's <see cref="CharacterRoster"/> (either on this
    /// component or on the <see cref="NetConfig"/>). Every connected player then sees a small
    /// panel: click a character and the server records it on that player's
    /// <see cref="PlayerAppearance"/>. By default each character can be claimed by one player
    /// at a time; <see cref="allowDuplicates"/> relaxes that.
    ///
    /// The server also assigns a unique character automatically when a player spawns, so a game
    /// works even if nobody touches the panel. Disconnected players release their character.
    /// Late joiners see the claimed characters because the selection is replicated state.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class CharacterSelect : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Characters offered to players. Leave empty to use the Character Roster on the scene's NetConfig. An empty roster disables character selection.")]
        [SerializeField] private CharacterRoster roster;

        [Tooltip("When false (default), each character can be claimed by one player at a time. When true, several players may pick the same character.")]
        [SerializeField] private bool allowDuplicates;

        [Tooltip("Show the selection panel to players. Turn off for a dedicated server or for automation-only runs.")]
        [SerializeField] private bool showPanel = true;

        [Tooltip("Hide the panel entirely once the choice is confirmed. Off shows 'Playing as X' instead.")]
        [SerializeField] private bool hidePanelWhenLocked;

        [Tooltip("Shown in [MP] log lines.")]
        [SerializeField] private string label = "character-select";

        private readonly NetworkList<int> _ownerBySlot = new NetworkList<int>(
            null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private CharacterRoster _resolvedRoster;

        /// <summary>The scene's active selector, if one exists.</summary>
        public static CharacterSelect Instance { get; private set; }

        /// <summary>Characters offered by this selector (serialized field, or the NetConfig roster).</summary>
        public CharacterRoster Roster => _resolvedRoster;

        /// <summary>True when several players may choose the same character.</summary>
        public bool AllowDuplicates => allowDuplicates;

        /// <summary>Shown in [MP] log lines.</summary>
        public string Label => label;

        /// <summary>Code-first: roster to offer.</summary>
        public void SetRoster(CharacterRoster value) => roster = value;

        /// <summary>Code-first: allow several players per character.</summary>
        public void SetAllowDuplicates(bool value) => allowDuplicates = value;

        /// <summary>Code-first: whether to show the panel.</summary>
        public void SetShowPanel(bool value) => showPanel = value;

        public override void OnNetworkSpawn()
        {
            Instance = this;

            var bootstrap = FindObjectOfType<NetBootstrap>();
            _resolvedRoster = roster != null
                ? roster
                : bootstrap != null && bootstrap.Config != null ? bootstrap.Config.CharacterRoster : null;

            if (IsServer)
            {
                if (_resolvedRoster == null || _resolvedRoster.Count == 0)
                {
                    MPLog.Warn("CHARACTER_SELECT_DISABLED",
                        ("label", label),
                        ("reason", "no roster"),
                        ("hint", "assign a CharacterRoster or run MPBlocks > Add Character Selection to Scene"));
                    return;
                }

                if (_ownerBySlot.Count == 0)
                {
                    for (int i = 0; i < _resolvedRoster.Count; i++) _ownerBySlot.Add(-1);
                }

                NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
                MPLog.Info("CHARACTER_SELECT_READY",
                    ("label", label),
                    ("count", _resolvedRoster.Count),
                    ("unique", !allowDuplicates));

                if (!allowDuplicates && bootstrap != null && bootstrap.Config != null
                    && _resolvedRoster.Count < bootstrap.Config.MaxPlayers)
                {
                    MPLog.Warn("CHARACTER_SELECT_SHORT",
                        ("label", label),
                        ("characters", _resolvedRoster.Count),
                        ("maxPlayers", bootstrap.Config.MaxPlayers),
                        ("hint", "create one character per player slot"));
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkManager != null)
            {
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }

            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Server only. Called by <see cref="PlayerAppearance"/> when a player object spawns:
        /// claim the first free character (unique mode) or an index based on the client id.
        /// </summary>
        public void RegisterPlayerServer(PlayerAppearance appearance)
        {
            if (!IsServer || appearance == null) return;
            if (_resolvedRoster == null || _resolvedRoster.Count == 0) return;
            if (_ownerBySlot.Count == 0)
            {
                for (int i = 0; i < _resolvedRoster.Count; i++) _ownerBySlot.Add(-1);
            }

            ulong clientId = appearance.OwnerClientId;
            int index = FindFreeSlot();

            if (allowDuplicates)
            {
                index = (int)(clientId % (ulong)_resolvedRoster.Count);
            }
            else if (index < 0)
            {
                // More players than characters: fall back to sharing instead of refusing to spawn.
                index = (int)(clientId % (ulong)_resolvedRoster.Count);
                MPLog.Warn("CHARACTER_SELECT_EXHAUSTED", ("clientId", clientId), ("index", index));
            }

            ClaimSlot(index, clientId);
            appearance.SetCharacterServer(index);

            var definition = _resolvedRoster.Get(index);
            MPLog.Info("CHARACTER_ASSIGNED",
                ("label", label),
                ("clientId", clientId),
                ("index", index),
                ("character", definition != null ? definition.DisplayName : index.ToString()));
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (!IsServer) return;

            for (int i = 0; i < _ownerBySlot.Count; i++)
            {
                if (_ownerBySlot[i] != (int)clientId) continue;

                _ownerBySlot[i] = -1;
                MPLog.Info("CHARACTER_RELEASED", ("label", label), ("clientId", clientId), ("index", i));
            }
        }

        /// <summary>Client asks to preview a character. Refused once the choice is confirmed.</summary>
        public void RequestSelect(int index)
        {
            if (!IsSpawned) return;

            if (IsServer)
            {
                HandleSelect(NetworkManager.LocalClientId, index);
                return;
            }

            RequestSelectServerRpc(index);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestSelectServerRpc(int index, ServerRpcParams rpcParams = default)
        {
            HandleSelect(rpcParams.Receive.SenderClientId, index);
        }

        private void HandleSelect(ulong clientId, int index)
        {
            if (!IsServer) return;
            if (_resolvedRoster == null || _resolvedRoster.Count == 0) return;

            int clamped = Mathf.Clamp(index, 0, _resolvedRoster.Count - 1);

            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
            {
                MPLog.Warn("CHARACTER_SELECT_DENIED", ("label", label), ("clientId", clientId), ("reason", "no player object"));
                return;
            }

            var appearance = client.PlayerObject.GetComponent<PlayerAppearance>();
            if (appearance == null)
            {
                MPLog.Warn("CHARACTER_SELECT_DENIED", ("label", label), ("clientId", clientId), ("reason", "no PlayerAppearance"));
                return;
            }

            if (appearance.IsLocked)
            {
                MPLog.Info("CHARACTER_SELECT_DENIED", ("label", label), ("clientId", clientId), ("index", clamped), ("reason", "locked"));
                return;
            }

            if (!allowDuplicates && IsClaimedByOther(clamped, clientId))
            {
                MPLog.Info("CHARACTER_SELECT_DENIED", ("label", label), ("clientId", clientId), ("index", clamped), ("reason", "taken"));
                return;
            }

            int previous = appearance.CharacterIndex;
            if (!allowDuplicates && previous >= 0 && previous < _ownerBySlot.Count && _ownerBySlot[previous] == (int)clientId)
            {
                _ownerBySlot[previous] = -1;
            }

            ClaimSlot(clamped, clientId);
            appearance.SetCharacterServer(clamped);

            var definition = _resolvedRoster.Get(clamped);
            MPLog.Info("CHARACTER_SELECTED",
                ("label", label),
                ("clientId", clientId),
                ("index", clamped),
                ("character", definition != null ? definition.DisplayName : clamped.ToString()));
        }

        /// <summary>Client asks to lock in the current choice. One way — there is no unlock.</summary>
        public void ConfirmSelection()
        {
            if (!IsSpawned) return;

            if (IsServer)
            {
                HandleConfirm(NetworkManager.LocalClientId);
                return;
            }

            ConfirmServerRpc();
        }

        [ServerRpc(RequireOwnership = false)]
        private void ConfirmServerRpc(ServerRpcParams rpcParams = default)
        {
            HandleConfirm(rpcParams.Receive.SenderClientId);
        }

        private void HandleConfirm(ulong clientId)
        {
            if (!IsServer) return;

            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject == null)
            {
                MPLog.Warn("CHARACTER_CONFIRM_DENIED", ("label", label), ("clientId", clientId), ("reason", "no player object"));
                return;
            }

            var appearance = client.PlayerObject.GetComponent<PlayerAppearance>();
            if (appearance == null)
            {
                MPLog.Warn("CHARACTER_CONFIRM_DENIED", ("label", label), ("clientId", clientId), ("reason", "no PlayerAppearance"));
                return;
            }

            appearance.SetLockedServer(true);
            MPLog.Info("CHARACTER_CONFIRMED", ("label", label), ("clientId", clientId), ("index", appearance.CharacterIndex));
        }

        private int FindFreeSlot()
        {
            for (int i = 0; i < _ownerBySlot.Count; i++)
            {
                if (_ownerBySlot[i] == -1) return i;
            }

            return -1;
        }

        private void ClaimSlot(int index, ulong clientId)
        {
            if (index < 0 || index >= _ownerBySlot.Count) return;
            _ownerBySlot[index] = (int)clientId;
        }

        private bool IsClaimedByOther(int index, ulong clientId)
        {
            if (index < 0 || index >= _ownerBySlot.Count) return false;
            int owner = _ownerBySlot[index];
            return owner != -1 && owner != (int)clientId;
        }

        private bool _demoStarted;

        private void Update()
        {
            if (_demoStarted || !IsSpawned) return;
            if (!HasArgument("-mp-character-demo")) return;

            var manager = NetworkManager.Singleton;
            if (manager == null || manager.LocalClient == null || manager.LocalClient.PlayerObject == null) return;

            _demoStarted = true;
            StartCoroutine(CharacterDemo());
        }

        /// <summary>
        /// Test hook: pick, confirm, then try to change again. The last step must be refused,
        /// which is what proves the confirm actually locks the choice.
        /// </summary>
        private System.Collections.IEnumerator CharacterDemo()
        {
            yield return new WaitForSeconds(2f);

            RequestSelect(0);
            yield return new WaitForSeconds(0.8f);

            ConfirmSelection();
            yield return new WaitForSeconds(0.8f);

            RequestSelect(1);

            MPLog.Info("CHARACTER_DEMO_DONE");
        }

        private static bool HasArgument(string name)
        {
            try
            {
                foreach (var arg in System.Environment.GetCommandLineArgs())
                {
                    if (arg == name) return true;
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }

        private void OnGUI()
        {
            if (!showPanel || !IsSpawned) return;
            if (_resolvedRoster == null || _resolvedRoster.Count == 0) return;
            if (NetworkManager.Singleton == null) return;

            var localClient = NetworkManager.Singleton.LocalClient;
            var player = localClient != null ? localClient.PlayerObject : null;
            if (player == null) return;

            var appearance = player.GetComponent<PlayerAppearance>();
            if (appearance == null) return;

            var currentDefinitionName = _resolvedRoster.Get(appearance.CharacterIndex) != null
                ? _resolvedRoster.Get(appearance.CharacterIndex).DisplayName
                : appearance.CharacterIndex.ToString();

            if (appearance.IsLocked)
            {
                if (hidePanelWhenLocked) return;

                var lockedRect = new Rect(Screen.width - 320f, 20f, 300f, 52f);
                GUI.Box(lockedRect, "Playing as " + currentDefinitionName + "  (locked)");
                return;
            }

            const float width = 300f;
            float height = 104f + _resolvedRoster.Count * 30f;
            var rect = new Rect(Screen.width - width - 20f, 20f, width, height);

            GUI.Box(rect, "Choose your character");
            GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 24f, rect.width - 20f, rect.height - 30f));

            var current = appearance.CharacterIndex;
            for (int i = 0; i < _resolvedRoster.Count; i++)
            {
                var definition = _resolvedRoster.Get(i);
                string display = definition != null ? definition.DisplayName : "Character " + i;
                bool mine = current == i;
                bool taken = !allowDuplicates && IsClaimedByOther(i, localClient.ClientId);

                using (new GuiEnabledScope(!taken))
                {
                    string text = (mine ? "● " : string.Empty) + display + (taken ? "  (taken)" : string.Empty);
                    if (GUILayout.Button(text, GUILayout.Height(26f)))
                    {
                        RequestSelect(i);
                    }
                }
            }

            var currentDefinition = _resolvedRoster.Get(current);
            GUILayout.Label("You: " + (currentDefinition != null ? currentDefinition.DisplayName : current.ToString()));
            GUILayout.Label("Claimed: " + ClaimedCount() + " / " + _resolvedRoster.Count);

            GUILayout.Space(4f);
            using (new GuiEnabledScope(current >= 0))
            {
                if (GUILayout.Button("Confirm", GUILayout.Height(28f)))
                {
                    ConfirmSelection();
                }
            }

            GUILayout.Label("Picking is free until you confirm.");

            GUILayout.EndArea();
        }

        private int ClaimedCount()
        {
            int claimed = 0;
            for (int i = 0; i < _ownerBySlot.Count; i++)
            {
                if (_ownerBySlot[i] != -1) claimed++;
            }

            return claimed;
        }

        private readonly struct GuiEnabledScope : System.IDisposable
        {
            private readonly bool _previous;

            public GuiEnabledScope(bool enabled)
            {
                _previous = GUI.enabled;
                GUI.enabled = enabled;
            }

            public void Dispose() => GUI.enabled = _previous;
        }
    }
}
