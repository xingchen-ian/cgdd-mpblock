using System.Collections.Generic;
using MPBlocks.Core;
using Unity.Netcode;
using UnityEngine;

namespace MPBlocks.Sync
{
    /// <summary>
    /// Replicates selected <see cref="Animator"/> parameters across the network.
    ///
    /// Configure the parameter names in the Inspector (or with
    /// <see cref="ConfigureParameters"/>); the authority polls those parameters and sends only
    /// the values that changed, plus a periodic full snapshot so a late joiner recovers the
    /// current state. Triggers are explicit: call <see cref="SetTrigger(string)"/> instead of
    /// <c>animator.SetTrigger</c>, because a trigger cannot be polled.
    ///
    /// Authority is owner by default (the player's own machine drives their animation) and can
    /// be switched to Server for NPCs. The block supports an Animator on a child GameObject,
    /// which is how the character visual prefab instantiated by
    /// <c>MPBlocks.Gameplay.PlayerAppearance</c> is animated; call
    /// <see cref="RebindAnimator()"/> when that child is created.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class SyncedAnimator : NetworkBehaviour
    {
        [Header("MPBlocks")]
        [Tooltip("Shown in [MP] log lines.")]
        [SerializeField] private string label = "animator";

        [Tooltip("Animator to synchronize. May be left empty when the Animator is on a child (for example a character visual prefab); call RebindAnimator() after the visual is created.")]
        [SerializeField] private Animator animator;

        [Tooltip("Owner = the owning client drives the animation (players); Server = the server drives it (NPCs).")]
        [SerializeField] private MPAuthority authority = MPAuthority.Owner;

        [Header("Parameters")]
        [Tooltip("Float parameters to synchronize, for example Speed or AimPitch. Names must exist in the Animator or they are skipped with a warning.")]
        [SerializeField] private string[] floatParameters = System.Array.Empty<string>();

        [Tooltip("Bool parameters to synchronize, for example Grounded or Crouching.")]
        [SerializeField] private string[] boolParameters = System.Array.Empty<string>();

        [Tooltip("Int parameters to synchronize, for example WeaponType.")]
        [SerializeField] private string[] intParameters = System.Array.Empty<string>();

        [Tooltip("Trigger parameters that SetTrigger() may fire. A trigger is an event, so it is sent once and not replayed for late joiners.")]
        [SerializeField] private string[] triggerParameters = System.Array.Empty<string>();

        [Tooltip("Seconds between full snapshots. Keeps late joiners and packet-loss recovery accurate; 0.5s costs almost nothing.")]
        [SerializeField] private float fullSyncInterval = 0.5f;

        [Tooltip("A float change smaller than this is ignored (avoids sending tiny animation noise).")]
        [SerializeField] private float floatEpsilon = 0.01f;

        private int[] _floatHashes = System.Array.Empty<int>();
        private int[] _boolHashes = System.Array.Empty<int>();
        private int[] _intHashes = System.Array.Empty<int>();
        private int[] _triggerHashes = System.Array.Empty<int>();
        private float[] _floatValues = System.Array.Empty<float>();
        private bool[] _boolValues = System.Array.Empty<bool>();
        private int[] _intValues = System.Array.Empty<int>();

        private float _timer;
        private bool _bound;

        /// <summary>The Animator being synchronized. May be null until bound.</summary>
        public Animator Animator => animator;

        /// <summary>Owner or Server authority.</summary>
        public MPAuthority Authority => authority;

        /// <summary>Shown in [MP] log lines.</summary>
        public string Label => label;

        /// <summary>True once an Animator and its parameters have been resolved.</summary>
        public bool IsBound => _bound;

        private bool HasAuthority => authority == MPAuthority.Server ? IsServer : IsOwner;

        /// <summary>Code-first: assign the Animator and rebuild the parameter cache.</summary>
        public void SetAnimator(Animator value)
        {
            animator = value;
            BindParameters();
        }

        /// <summary>Finds the first Animator in the children (for example in a spawned visual) and binds it.</summary>
        public void RebindAnimator()
        {
            animator = GetComponentInChildren<Animator>(true);
            BindParameters();
        }

        /// <summary>Code-first: owner or server authority.</summary>
        public void SetAuthority(MPAuthority value)
        {
            authority = value;
            MPLog.Info("SYNCED_ANIMATOR_AUTHORITY", ("label", label), ("authority", value.ToString()));
        }

        /// <summary>Code-first: replace the synchronized parameter lists and rebind.</summary>
        public void ConfigureParameters(string[] floats, string[] bools, string[] ints, string[] triggers)
        {
            floatParameters = floats ?? System.Array.Empty<string>();
            boolParameters = bools ?? System.Array.Empty<string>();
            intParameters = ints ?? System.Array.Empty<string>();
            triggerParameters = triggers ?? System.Array.Empty<string>();
            BindParameters();
        }

        /// <summary>Code-first: label used in [MP] log lines.</summary>
        public void SetLabel(string value) => label = value;

        /// <summary>Code-first: seconds between full snapshots.</summary>
        public void SetFullSyncInterval(float seconds) => fullSyncInterval = Mathf.Max(0.1f, seconds);

        public override void OnNetworkSpawn()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>(true);
            BindParameters();
        }

        private void BindParameters()
        {
            _bound = false;

            if (animator == null)
            {
                MPLog.Warn("SYNCED_ANIMATOR_NO_ANIMATOR", ("label", label));
                return;
            }

            var available = animator.parameters;
            _floatHashes = Filter(floatParameters, available, AnimatorControllerParameterType.Float);
            _boolHashes = Filter(boolParameters, available, AnimatorControllerParameterType.Bool);
            _intHashes = Filter(intParameters, available, AnimatorControllerParameterType.Int);
            // Triggers are events: replicate every one the controller declares, in controller
            // order (deterministic on every machine), so adding an action state does not require
            // editing this component's list first.
            var listedTriggers = Filter(triggerParameters, available, AnimatorControllerParameterType.Trigger);
            var mergedTriggers = new System.Collections.Generic.List<int>(listedTriggers);

            foreach (var parameter in available)
            {
                if (parameter.type != AnimatorControllerParameterType.Trigger) continue;
                if (mergedTriggers.Contains(parameter.nameHash)) continue;
                mergedTriggers.Add(parameter.nameHash);
            }

            _triggerHashes = mergedTriggers.ToArray();

            _floatValues = new float[_floatHashes.Length];
            _boolValues = new bool[_boolHashes.Length];
            _intValues = new int[_intHashes.Length];

            for (int i = 0; i < _floatHashes.Length; i++) _floatValues[i] = animator.GetFloat(_floatHashes[i]);
            for (int i = 0; i < _boolHashes.Length; i++) _boolValues[i] = animator.GetBool(_boolHashes[i]);
            for (int i = 0; i < _intHashes.Length; i++) _intValues[i] = animator.GetInteger(_intHashes[i]);

            _bound = true;
            MPLog.Info("SYNCED_ANIMATOR_READY",
                ("label", label),
                ("authority", authority.ToString()),
                ("floats", _floatHashes.Length),
                ("bools", _boolHashes.Length),
                ("ints", _intHashes.Length),
                ("triggers", _triggerHashes.Length));
        }

        private static int[] Filter(string[] names, AnimatorControllerParameter[] available,
            AnimatorControllerParameterType type)
        {
            if (names == null || names.Length == 0) return System.Array.Empty<int>();

            var result = new List<int>(names.Length);
            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;

                bool found = false;
                foreach (var parameter in available)
                {
                    if (parameter.type != type || parameter.name != name) continue;

                    result.Add(parameter.nameHash);
                    found = true;
                    break;
                }

                if (!found)
                {
                    MPLog.Warn("SYNCED_ANIMATOR_PARAMETER_MISSING",
                        ("label", "animator"),
                        ("parameter", name),
                        ("type", type.ToString()));
                }
            }

            return result.ToArray();
        }

        private void Update()
        {
            if (!IsSpawned || !_bound || animator == null || !HasAuthority) return;

            _timer += Time.deltaTime;
            bool full = _timer >= Mathf.Max(0.1f, fullSyncInterval);
            if (full) _timer = 0f;

            for (int i = 0; i < _floatHashes.Length; i++)
            {
                float value = animator.GetFloat(_floatHashes[i]);
                if (!full && Mathf.Abs(value - _floatValues[i]) <= floatEpsilon) continue;

                _floatValues[i] = value;
                CommitFloat(i, value);
            }

            for (int i = 0; i < _boolHashes.Length; i++)
            {
                bool value = animator.GetBool(_boolHashes[i]);
                if (!full && value == _boolValues[i]) continue;

                _boolValues[i] = value;
                CommitBool(i, value);
            }

            for (int i = 0; i < _intHashes.Length; i++)
            {
                int value = animator.GetInteger(_intHashes[i]);
                if (!full && value == _intValues[i]) continue;

                _intValues[i] = value;
                CommitInt(i, value);
            }
        }

        // ---------------------------------------------------------------- public setters

        /// <summary>Authority only: set a float parameter locally; the change is synchronized by the parameter poll.</summary>
        public void SetFloat(string parameter, float value)
        {
            if (animator == null) return;
            animator.SetFloat(Animator.StringToHash(parameter), value);
        }

        /// <summary>Authority only: set a bool parameter locally; the change is synchronized by the parameter poll.</summary>
        public void SetBool(string parameter, bool value)
        {
            if (animator == null) return;
            animator.SetBool(Animator.StringToHash(parameter), value);
        }

        /// <summary>Authority only: set an int parameter locally; the change is synchronized by the parameter poll.</summary>
        public void SetInteger(string parameter, int value)
        {
            if (animator == null) return;
            animator.SetInteger(Animator.StringToHash(parameter), value);
        }

        /// <summary>
        /// Authority only: fire a trigger on every machine. Use this instead of
        /// <c>animator.SetTrigger</c>; triggers are events and cannot be polled.
        /// </summary>
        public void SetTrigger(string parameter)
        {
            if (animator == null)
            {
                MPLog.Warn("SYNCED_ANIMATOR_NO_ANIMATOR", ("label", label));
                return;
            }

            if (!HasAuthority)
            {
                MPLog.Warn(MPEvent.AuthViolation, ("block", "SyncedAnimator"), ("label", label), ("caller", "client"));
                return;
            }

            int index = FindTriggerIndex(Animator.StringToHash(parameter));
            if (index < 0)
            {
                MPLog.Warn("SYNCED_ANIMATOR_TRIGGER_MISSING", ("label", label), ("parameter", parameter));
                return;
            }

            animator.SetTrigger(_triggerHashes[index]);
            MPLog.Info("ANIMATOR_TRIGGER", ("label", label), ("parameter", parameter), ("clientId", OwnerClientId));

            if (IsServer)
            {
                if (NetworkManager.ConnectedClientsIds.Count > 1) ApplyTriggerClientRpc(index);
            }
            else
            {
                SubmitTriggerServerRpc(index);
            }
        }

        private int FindTriggerIndex(int hash)
        {
            for (int i = 0; i < _triggerHashes.Length; i++)
            {
                if (_triggerHashes[i] == hash) return i;
            }

            return -1;
        }

        // ---------------------------------------------------------------- replication

        private void CommitFloat(int index, float value)
        {
            if (IsServer)
            {
                ApplyFloatLocal(index, value);
                if (NetworkManager.ConnectedClientsIds.Count > 1) ApplyFloatClientRpc(index, value);
                return;
            }

            SubmitFloatServerRpc(index, value);
        }

        private void CommitBool(int index, bool value)
        {
            if (IsServer)
            {
                ApplyBoolLocal(index, value);
                if (NetworkManager.ConnectedClientsIds.Count > 1) ApplyBoolClientRpc(index, value);
                return;
            }

            SubmitBoolServerRpc(index, value);
        }

        private void CommitInt(int index, int value)
        {
            if (IsServer)
            {
                ApplyIntLocal(index, value);
                if (NetworkManager.ConnectedClientsIds.Count > 1) ApplyIntClientRpc(index, value);
                return;
            }

            SubmitIntServerRpc(index, value);
        }

        private void ApplyFloatLocal(int index, float value)
        {
            if (animator == null || index < 0 || index >= _floatHashes.Length) return;

            animator.SetFloat(_floatHashes[index], value);
            if (index < _floatValues.Length) _floatValues[index] = value;
        }

        private void ApplyBoolLocal(int index, bool value)
        {
            if (animator == null || index < 0 || index >= _boolHashes.Length) return;

            animator.SetBool(_boolHashes[index], value);
            if (index < _boolValues.Length) _boolValues[index] = value;
        }

        private void ApplyIntLocal(int index, int value)
        {
            if (animator == null || index < 0 || index >= _intHashes.Length) return;

            animator.SetInteger(_intHashes[index], value);
            if (index < _intValues.Length) _intValues[index] = value;
        }

        private void ApplyTriggerLocal(int index)
        {
            if (animator == null || index < 0 || index >= _triggerHashes.Length) return;
            animator.SetTrigger(_triggerHashes[index]);
            MPLog.Info("ANIMATOR_TRIGGER", ("label", label), ("index", index), ("remote", true));
        }

        [ServerRpc]
        private void SubmitFloatServerRpc(int index, float value, ServerRpcParams rpcParams = default)
        {
            ApplyFloatLocal(index, value);
            ApplyFloatClientRpc(index, value);
        }

        [ServerRpc]
        private void SubmitBoolServerRpc(int index, bool value, ServerRpcParams rpcParams = default)
        {
            ApplyBoolLocal(index, value);
            ApplyBoolClientRpc(index, value);
        }

        [ServerRpc]
        private void SubmitIntServerRpc(int index, int value, ServerRpcParams rpcParams = default)
        {
            ApplyIntLocal(index, value);
            ApplyIntClientRpc(index, value);
        }

        [ServerRpc]
        private void SubmitTriggerServerRpc(int index, ServerRpcParams rpcParams = default)
        {
            ApplyTriggerLocal(index);
            ApplyTriggerClientRpc(index);
        }

        [ClientRpc]
        private void ApplyFloatClientRpc(int index, float value) => ApplyFloatLocal(index, value);

        [ClientRpc]
        private void ApplyBoolClientRpc(int index, bool value) => ApplyBoolLocal(index, value);

        [ClientRpc]
        private void ApplyIntClientRpc(int index, int value) => ApplyIntLocal(index, value);

        [ClientRpc]
        private void ApplyTriggerClientRpc(int index) => ApplyTriggerLocal(index);
    }
}
