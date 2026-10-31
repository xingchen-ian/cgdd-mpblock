using System;
using System.Collections;
using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace MPBlocks.Gameplay
{
    /// <summary>
    /// Server-authoritative health for anything that can be damaged.
    ///
    /// <see cref="ApplyDamage"/> is deliberately **server only**. A client that wants to
    /// hurt something must go through a validated path (normally an
    /// <see cref="Interactable"/>, e.g. a weapon), which the server checks before calling
    /// ApplyDamage. Letting clients call it directly is how students accidentally build
    /// a game where anyone can kill anyone.
    /// </summary>
    public sealed class Health : NetworkBehaviour, IStateHashable
    {
        [Header("MPBlocks")]
        [Tooltip("Debug name shown in the Inspector and in [MP] log lines such as HEALTH_READY / HEALTH_CHANGED. It is not a network identifier, so renaming it never breaks sync; keep it distinct so logs stay grep-able.")]
        [SerializeField] private string label = "health";

        [Tooltip("Health at spawn and the maximum. The server sets the current value to it in OnNetworkSpawn; changing it at runtime heals nothing and Heal is clamped by it. 0 or negative means the object is dead on spawn.")]
        [SerializeField] private int maxHealth = 100;

        [Header("Events")]
        [Tooltip("UnityEvent fired on every machine each time health changes; optional Inspector wiring. It carries no value, so read Health.Current or subscribe to Changed for numbers. Late joiners do not get it for the initial value they receive on spawn.")]
        [SerializeField] private UnityEvent onHealthChanged = new UnityEvent();

        [Tooltip("UnityEvent fired once on each machine when health crosses from above 0 to 0; optional. Respawning (ResetHealth) does not fire it again, so it is the right hook for death effects and sounds.")]
        [SerializeField] private UnityEvent onDied = new UnityEvent();

        private readonly NetworkVariable<int> _current = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Raised on every machine: (current, max).</summary>
        public event Action<int, int> Changed;

        /// <summary>Raised once when health crosses to zero.</summary>
        public event Action Died;

        public string Label => label;
        public int Max => maxHealth;
        public int Current => _current.Value;
        public bool IsDead => _current.Value <= 0;

        /// <inheritdoc />
        public int StateHashContribution() => _current.Value;

        public override void OnNetworkSpawn()
        {
            _current.OnValueChanged += HandleChanged;

            if (IsServer)
            {
                _current.Value = maxHealth;
                MPLog.Info("HEALTH_READY", ("label", label), ("max", maxHealth));

                if (HasArgument("-mp-combat-demo"))
                {
                    StartCoroutine(CombatDemo());
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            _current.OnValueChanged -= HandleChanged;
        }

        private void HandleChanged(int previous, int current)
        {
            onHealthChanged?.Invoke();
            Changed?.Invoke(current, maxHealth);

            if (previous > 0 && current <= 0)
            {
                onDied?.Invoke();
                Died?.Invoke();
            }
        }

        /// <summary>Server only. Reduces health by <paramref name="amount"/>.</summary>
        public void ApplyDamage(int amount)
        {
            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "Health"), ("label", label), ("caller", "client"));
                return;
            }

            if (amount <= 0 || IsDead) return;

            int next = Mathf.Max(0, _current.Value - amount);
            _current.Value = next;
            MPLog.Info("HEALTH_CHANGED", ("label", label), ("value", next), ("damage", amount));
        }

        /// <summary>Server only. Restores health, clamped to the maximum.</summary>
        public void Heal(int amount)
        {
            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "Health"), ("label", label), ("caller", "client"));
                return;
            }

            if (amount <= 0) return;

            _current.Value = Mathf.Min(maxHealth, _current.Value + amount);
            MPLog.Info("HEALTH_CHANGED", ("label", label), ("value", _current.Value), ("heal", amount));
        }

        /// <summary>Server only. Resets to full and lets the object live again.</summary>
        public void ResetHealth()
        {
            if (!IsServer)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "Health"), ("label", label), ("caller", "client"));
                return;
            }

            _current.Value = maxHealth;
            MPLog.Info("HEALTH_RESET", ("label", label), ("value", maxHealth));
        }

        private IEnumerator CombatDemo()
        {
            yield return new WaitForSeconds(3f);

            for (int i = 0; i < 6; i++)
            {
                ApplyDamage(15);
                yield return new WaitForSeconds(2f);
            }
        }

        private static bool HasArgument(string name)
        {
            try
            {
                foreach (var arg in Environment.GetCommandLineArgs())
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
    }
}
