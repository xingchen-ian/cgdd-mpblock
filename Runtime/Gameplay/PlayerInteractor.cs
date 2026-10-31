using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Lets the local player interact with the nearest <see cref="Interactable"/>.
    ///
    /// Press <see cref="key"/> (E by default). With <c>-mp-interact-demo</c> on the
    /// command line it fires automatically every couple of seconds, which is how
    /// tools/mp-verify.sh proves the request → validate → approve round trip works.
    /// </summary>
    public sealed class PlayerInteractor : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Radius in metres in which the local player searches for interactables, default 3.5. Interactable.maxDistance is checked again on the server and both must be satisfied; raising this without raising the server value still gets denied as out-of-range.")]
        [SerializeField] private float range = 3.5f;

        [Tooltip("Local key that triggers an interaction, default E. Input is read only on the client that owns this player; the server-side copy never responds. Change this field to rebind.")]
        [SerializeField] private KeyCode key = KeyCode.E;

        [Tooltip("Seconds between automatic interactions when -mp-interact-demo is on the command line, used by tools/mp-verify.sh to prove the request -> validate -> approve round trip unattended. It has no effect in normal play.")]
        [SerializeField] private float demoInterval = 2f;

        private bool _demo;
        private float _timer;

        public override void OnNetworkSpawn()
        {
            if (!IsOwner) return;

            foreach (var arg in System.Environment.GetCommandLineArgs())
            {
                if (arg == "-mp-interact-demo") _demo = true;
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner) return;

            if (_demo)
            {
                _timer += Time.deltaTime;
                if (_timer < demoInterval) return;
                _timer = 0f;
                TryInteract();
                return;
            }

            if (Input.GetKeyDown(key)) TryInteract();
        }

        /// <summary>Finds the nearest interactable in range and asks the server about it.</summary>
        public void TryInteract()
        {
            Interactable nearest = null;
            float best = range;

            var candidates = FindObjectsOfType<Interactable>();
            if (_demo)
            {
                // Rotate through everything in range so a scripted run exercises more
                // than the single closest object.
                var inRange = new System.Collections.Generic.List<Interactable>();
                foreach (var candidate in candidates)
                {
                    if (Vector3.Distance(candidate.transform.position, transform.position) <= best)
                    {
                        inRange.Add(candidate);
                    }
                }

                if (inRange.Count == 0) return;
                nearest = inRange[Random.Range(0, inRange.Count)];
            }
            else
            {
                foreach (var candidate in candidates)
                {
                    float distance = Vector3.Distance(candidate.transform.position, transform.position);
                    if (distance > best) continue;

                    best = distance;
                    nearest = candidate;
                }
            }

            if (nearest == null) return;

            MPLog.Info("INTERACT_LOCAL_REQUEST", ("target", nearest.Label), ("distance", best));
            nearest.Request();
        }
    }
}
