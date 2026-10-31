using MPBlocks.Core;
using Unity.Netcode.Components;
using UnityEngine;

namespace MPBlocks.Sync
{
    /// <summary>Who is allowed to move this transform.</summary>
    public enum MPAuthority
    {
        /// <summary>Only the server may move it. Cheat-resistant, but a player's own
        /// movement feels delayed because input has to round-trip.</summary>
        Server = 0,

        /// <summary>The owning client moves it. Responsive, correct for a player's own
        /// character. The trade-off is that the owner could cheat about its position.</summary>
        Owner = 1,
    }

    /// <summary>
    /// Position / rotation / scale sync with an explicit authority choice.
    ///
    /// This block is a thin, opinionated wrapper over Netcode for GameObjects'
    /// <see cref="NetworkTransform"/>: it keeps the upstream interpolation and
    /// threshold logic and only turns "who may commit" into a visible enum.
    ///
    /// Player characters should use <see cref="MPAuthority.Owner"/>; world objects
    /// moved by the game itself should use <see cref="MPAuthority.Server"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SyncedTransform : NetworkTransform
    {
        [Header("MPBlocks")]
        [Tooltip("Who may move this object: Owner lets the owning client move it, which feels responsive and suits player characters; Server lets only the server move it, which resists cheating but adds input delay. Default is Owner.")]
        [SerializeField] private MPAuthority authority = MPAuthority.Owner;

        [Tooltip("Name shown in the Inspector and in [MP] log lines (e.g. AUTHORITY_CHANGED), so multiple objects in the scene can be told apart.")]
        [SerializeField] private string label = "transform";

        public MPAuthority Authority
        {
            get => authority;
            set
            {
                if (authority == value) return;
                authority = value;
                MPLog.Info("AUTHORITY_CHANGED", ("block", label), ("authority", authority.ToString()));
            }
        }

        public string Label
        {
            get => label;
            set => label = value;
        }

        protected override bool OnIsServerAuthoritative() => authority == MPAuthority.Server;
    }
}
