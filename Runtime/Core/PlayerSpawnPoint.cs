using UnityEngine;

namespace MPBlocks.Core
{
    /// <summary>
    /// Marks where a newly connected player should appear.
    ///
    /// The server picks spawn points round-robin (sorted by name, so a scripted run is
    /// reproducible) and hands the position to Netcode for GameObjects as part of the
    /// connection approval response. That matters because player objects are
    /// owner-authoritative: the server cannot simply move a client's body afterwards
    /// without the owning client immediately overwriting it.
    ///
    /// No NetworkBehaviour: this is scene markup, not networked state. Put four of these
    /// in an arena and players stop spawning on top of each other.
    /// </summary>
    public sealed class PlayerSpawnPoint : MonoBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Only used to tell spawn points apart in [MP] logs (e.g. SPAWN_POINT_ASSIGNED); optional. The server assigns points round-robin after sorting them by name, so renaming a point changes the order.")]
        [SerializeField] private string label = "spawn";

        public string Label => label;
        public Vector3 Position => transform.position;
        public Quaternion Rotation => transform.rotation;
    }
}
