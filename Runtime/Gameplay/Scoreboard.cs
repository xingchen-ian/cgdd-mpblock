using System;
using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// A server-owned scoreboard with a fixed number of slots (teams or players).
    ///
    /// Scores persist for late joiners because they are replicated state, not events.
    /// The server changes them; clients ask through <see cref="RequestAddScore"/>.
    ///
    /// The server spawns this object automatically when it is assigned to
    /// <c>NetConfig.scoreboardPrefab</c>, so a scene does not have to place one.
    /// </summary>
    public sealed class Scoreboard : NetworkBehaviour, IStateHashable
    {
        [Header("MPBlocks")]
        [Tooltip("Scoreboard display name, used only in the Inspector and in code through the Label property; it is not a network identifier, so renaming it does not affect scoring. [MP] logs currently do not print it.")]
        [SerializeField] private string label = "scoreboard";

        [Tooltip("Number of score slots and therefore the length of the score array: 2 for a duel, 4 for four teams. It is only used to initialise when the server first spawns with an empty score list, so changing it at runtime does nothing; late joiners receive all current scores.")]
        [SerializeField] private int slotCount = 2;

        [Tooltip("Display name per slot, used by ScoreboardHUD; can be left short. Extra names are ignored and missing ones fall back to Slot N. It is not synced state, so each client reads its local config and a rename must be applied on every machine.")]
        [SerializeField] private string[] slotNames = { "Team A", "Team B", "Team C", "Team D" };

        [Tooltip("Score every slot starts at, written when the server first spawns the scoreboard; ResetScores also returns to this value. Changing it at runtime does not change existing scores.")]
        [SerializeField] private int startingScore;

        private readonly NetworkList<int> _scores = new NetworkList<int>(
            null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>
        /// NetworkList owns a native container; NGO reports "A Native Collection has not been
        /// disposed" for every list that is not disposed when the object goes away.
        /// </summary>
        public override void OnDestroy()
        {
            _scores?.Dispose();
            base.OnDestroy();
        }

        /// <summary>Raised on every machine whenever any score changes. The HUD listens to this.</summary>
        public event Action Changed;

        public string Label => label;
        public int SlotCount => _scores.Count;

        public string SlotName(int slot)
        {
            if (slotNames != null && slot >= 0 && slot < slotNames.Length && !string.IsNullOrWhiteSpace(slotNames[slot]))
            {
                return slotNames[slot];
            }

            return "Slot " + slot;
        }

        public int GetScore(int slot)
        {
            if (slot < 0 || slot >= _scores.Count) return 0;
            return _scores[slot];
        }

        /// <inheritdoc />
        public int StateHashContribution()
        {
            int sum = 0;
            for (int i = 0; i < _scores.Count; i++) sum = unchecked(sum * 31 + _scores[i]);
            return sum;
        }

        public override void OnNetworkSpawn()
        {
            _scores.OnListChanged += HandleListChanged;

            if (IsServer && _scores.Count == 0)
            {
                for (int i = 0; i < Mathf.Max(1, slotCount); i++)
                {
                    _scores.Add(startingScore);
                }

                MPLog.Info("SCOREBOARD_READY", ("slots", _scores.Count));

                if (HasArgument("-mp-score-demo"))
                {
                    StartCoroutine(ScoreDemo());
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            _scores.OnListChanged -= HandleListChanged;
        }

        private void HandleListChanged(NetworkListEvent<int> _) => Changed?.Invoke();

        /// <summary>
        /// Test/demo helper: adds one point to slot 0 every 1.5s, six times. Lets
        /// tools/mp-verify.sh prove that a score change actually reaches clients.
        /// </summary>
        private System.Collections.IEnumerator ScoreDemo()
        {
            yield return new WaitForSeconds(2f);

            for (int i = 0; i < 6; i++)
            {
                AddScore(0, 1);
                yield return new WaitForSeconds(1.5f);
            }
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

        /// <summary>Server only. Adds <paramref name="delta"/> to a slot.</summary>
        public void AddScore(int slot, int delta)
        {
            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "Scoreboard"), ("caller", "client"));
                return;
            }

            if (slot < 0 || slot >= _scores.Count) return;

            _scores[slot] = _scores[slot] + delta;
            MPLog.Info("SCORE_CHANGED", ("slot", slot), ("value", _scores[slot]), ("delta", delta));
        }

        /// <summary>Server only. Sets a slot to an absolute score.</summary>
        public void SetScore(int slot, int value)
        {
            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "Scoreboard"), ("caller", "client"));
                return;
            }

            if (slot < 0 || slot >= _scores.Count) return;
            _scores[slot] = value;
            MPLog.Info("SCORE_CHANGED", ("slot", slot), ("value", value), ("delta", 0));
        }

        /// <summary>Server only. Puts every slot back to its starting value.</summary>
        public void ResetScores()
        {
            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "Scoreboard"), ("caller", "client"));
                return;
            }

            for (int i = 0; i < _scores.Count; i++)
            {
                _scores[i] = startingScore;
            }

            MPLog.Info("SCOREBOARD_RESET", ("slots", _scores.Count));
        }

        /// <summary>Ask the server to add to a slot. Safe from any machine.</summary>
        public void RequestAddScore(int slot, int delta)
        {
            if (IsServer)
            {
                AddScore(slot, delta);
                return;
            }

            RequestAddScoreServerRpc(slot, delta);
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestAddScoreServerRpc(int slot, int delta, ServerRpcParams rpcParams = default)
        {
            MPLog.Info("SCORE_REQUESTED", ("slot", slot), ("delta", delta), ("from", rpcParams.Receive.SenderClientId));
            AddScore(slot, delta);
        }
    }
}
