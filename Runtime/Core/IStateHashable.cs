namespace MPBlocks.Core
{
    /// <summary>
    /// Implemented by blocks whose value should take part in the periodic world-state
    /// hash. Only include state that is owned by the server: owner-authoritative
    /// transforms legitimately differ for a moment because of latency, so hashing them
    /// would report divergence constantly.
    /// </summary>
    public interface IStateHashable
    {
        /// <summary>A deterministic contribution to the world hash.</summary>
        int StateHashContribution();
    }
}
