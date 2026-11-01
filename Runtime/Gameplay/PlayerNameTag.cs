using System.Collections;
using MPBlocks.Core;
using MPBlocks.Sync;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Gives every player a display name and keeps it in a <see cref="SyncedString"/>.
    ///
    /// The owner submits the name it typed on the session screen (or set with
    /// <c>-mp-name</c>). If a player submits nothing, the server fills in "Player N" after a
    /// short grace period, so no one is ever nameless.
    ///
    /// The name is a *request*: <see cref="SyncedString"/> is server-owned, so the server
    /// sanitises and stores it, and every machine reads the stored value.
    /// </summary>
    public sealed class PlayerNameTag : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Where the name is stored. Required.")]
        [SerializeField] private SyncedString syncedName;

        [Tooltip("Prefix used when the server has to generate a name.")]
        [SerializeField] private string namePrefix = "Player";

        [Tooltip("Seconds to wait for the player's own name before generating one.")]
        [SerializeField] private float fallbackDelay = 2f;

        public string DisplayName => syncedName != null ? syncedName.Value : string.Empty;

        public override void OnNetworkSpawn()
        {
            if (syncedName == null) return;

            if (IsOwner)
            {
                var chosen = LocalPlayerName.Resolve();
                if (!string.IsNullOrEmpty(chosen))
                {
                    MPLog.Info("NAME_SUBMITTED", ("clientId", OwnerClientId), ("name", chosen));
                    syncedName.RequestSet(chosen);
                }
            }

            if (IsServer)
            {
                StartCoroutine(FallbackAfterDelay());
            }
        }

        private IEnumerator FallbackAfterDelay()
        {
            yield return new WaitForSeconds(Mathf.Max(0.5f, fallbackDelay));

            if (!string.IsNullOrEmpty(syncedName.Value)) yield break;

            var generated = $"{namePrefix} {OwnerClientId}";
            syncedName.SetServerValue(generated);
            MPLog.Info("NAME_SET", ("clientId", OwnerClientId), ("name", generated));
        }

        /// <summary>Any machine may ask; the server decides. Use for a rename field.</summary>
        public void RequestName(string name)
        {
            if (syncedName == null) return;

            var clean = LocalPlayerName.Sanitize(name);
            if (string.IsNullOrEmpty(clean)) return;

            syncedName.RequestSet(clean);
        }
    }
}
