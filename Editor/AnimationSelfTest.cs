using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MPBlocks.Editor
{
    /// <summary>
    /// Headless check of a generated character controller. Batch mode cannot render, but the
    /// Animator evaluates on the CPU, so every state can be driven and inspected from a script:
    /// that is how "the key fires but nothing plays" gets a definite cause instead of a guess.
    ///
    /// Run it from a terminal:
    ///   unity build &lt;project&gt; --execute-method MPBlocks.Editor.AnimationSelfTest.Run
    /// or from the menu: MPBlocks &gt; Diagnostics &gt; Animation Self Test.
    /// </summary>
    public static class AnimationSelfTest
    {
        private const string GeneratedFolder = "Assets/Character/_Generated";

        [MenuItem("MPBlocks/Diagnostics/Animation Self Test")]
        public static void RunFromMenu()
        {
            var result = Execute();
            EditorUtility.DisplayDialog("MPBlocks",
                result.Fail == 0
                    ? $"Animation self test passed ({result.Pass} checks)."
                    : $"Animation self test FAILED ({result.Fail} of {result.Pass + result.Fail}). See the Console.",
                "OK");
        }

        /// <summary>Batch-mode entry point (--execute-method).</summary>
        public static void Run()
        {
            var result = Execute();

            if (result.Fail > 0)
            {
                throw new InvalidOperationException(
                    $"Animation self test failed: {result.Fail} check(s). See the [MP] SELFTEST_FAIL lines above.");
            }
        }

        private static Result Execute()
        {
            var result = new Result();

            var guids = AssetDatabase.FindAssets("t:GameObject", new[] { GeneratedFolder });
            var prefabs = new List<string>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("_Visual.prefab", StringComparison.OrdinalIgnoreCase)) prefabs.Add(path);
            }

            if (prefabs.Count == 0)
            {
                Debug.LogWarning($"[MP] WARN SELFTEST_NO_CHARACTERS folder={GeneratedFolder}");
                Debug.Log("[MP] INFO SELFTEST characters=0 pass=0 fail=0");
                return result;
            }

            foreach (var path in prefabs)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;

                CheckCharacter(prefab, result);
            }

            Debug.Log($"[MP] INFO SELFTEST characters={prefabs.Count} pass={result.Pass} fail={result.Fail}");
            return result;
        }

        private static void CheckCharacter(GameObject prefab, Result result)
        {
            var name = prefab.name.Replace("_Visual", string.Empty);

            // Work in a scratch scene so nothing in the user's scenes is touched.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var instance = UnityEngine.Object.Instantiate(prefab);
            var animator = instance.GetComponentInChildren<Animator>(true);

            if (animator == null)
            {
                Log(result, name, "has-animator", "FAIL", "the visual prefab has no Animator");
                Cleanup(instance, scene);
                return;
            }

            Log(result, name, "has-animator", "PASS", animator.name);

            var controller = animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
            if (controller == null)
            {
                Log(result, name, "has-controller", "FAIL", "no controller assigned");
                Cleanup(instance, scene);
                return;
            }

            Log(result, name, "has-controller", "PASS", controller.name);

            // 1. every state must carry a motion: a state with null motion plays nothing, which is
            //    what "the character freezes after an action" looks like.
            var states = controller.layers.Length > 0 ? controller.layers[0].stateMachine.states : null;
            if (states == null || states.Length == 0)
            {
                Log(result, name, "has-states", "FAIL", "the controller has no states");
                Cleanup(instance, scene);
                return;
            }

            var missing = new List<string>();
            var stateNames = new List<string>();

            foreach (var state in states)
            {
                stateNames.Add(state.state.name);
                if (state.state.motion == null) missing.Add(state.state.name);
            }

            if (missing.Count == 0)
            {
                Log(result, name, "states-have-motion", "PASS", string.Join(",", stateNames));
            }
            else
            {
                Log(result, name, "states-have-motion", "FAIL",
                    $"no motion on: {string.Join(",", missing)} — the folder is missing that clip file");
            }

            // 2. the parameters the runtime scripts write must exist
            foreach (var parameter in new[] { "Speed", "MoveX", "MoveZ", "Grounded" })
            {
                bool found = false;
                foreach (var candidate in controller.parameters)
                {
                    if (candidate.name == parameter) found = true;
                }

                Log(result, name, $"parameter:{parameter}", found ? "PASS" : "FAIL",
                    found ? "declared" : "missing — rebuild the controller with Import from Assets/Character");
            }

            // Drive the animator. Batch mode still evaluates the graph; culling would stop it.
            animator.enabled = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;

            var tick = 0.1f;

            // Grounded is false by default, so an unprimed rig sits in Jump and every speed
            // check would read the jump clip.
            animator.SetBool("Grounded", true);
            animator.SetFloat("Speed", 0f);
            for (int i = 0; i < 8; i++) animator.Update(tick);

            CheckSpeed(animator, result, name, 0f, "Idle");
            CheckSpeed(animator, result, name, 1.6f, "Walk");
            CheckSpeed(animator, result, name, 4.5f, "Run");

            // 3. jump is driven by Grounded == false
            animator.SetFloat("Speed", 0f);
            animator.SetBool("Grounded", false);
            for (int i = 0; i < 6; i++) animator.Update(tick);

            var jumpState = animator.GetCurrentAnimatorStateInfo(0);
            bool inJump = jumpState.IsName("Jump");
            Log(result, name, "jump", inJump ? "PASS" : "FAIL",
                inJump
                    ? $"state=Jump clip={CurrentClip(animator)}"
                    : $"stayed in state={StateName(animator, jumpState.shortNameHash)} clip={CurrentClip(animator)}");

            animator.SetBool("Grounded", true);
            for (int i = 0; i < 6; i++) animator.Update(tick);

            // 4. every Trigger must actually switch the rig to a state of the same name
            var triggers = new List<string>();
            foreach (var parameter in controller.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Trigger) triggers.Add(parameter.name);
            }

            if (triggers.Count == 0)
            {
                Log(result, name, "triggers", "PASS", "none declared (locomotion only)");
            }

            foreach (var trigger in triggers)
            {
                animator.SetTrigger(trigger);
                for (int i = 0; i < 4; i++) animator.Update(tick);

                var info = animator.GetCurrentAnimatorStateInfo(0);
                bool reached = info.IsName(trigger);
                Log(result, name, $"trigger:{trigger}", reached ? "PASS" : "FAIL",
                    reached
                        ? $"state={trigger} clip={CurrentClip(animator)}"
                        : $"stayed in state={StateName(animator, info.shortNameHash)} clip={CurrentClip(animator)}");

                // return to idle for the next trigger
                for (int i = 0; i < 12; i++) animator.Update(tick);
            }

            Cleanup(instance, scene);
        }

        private static void CheckSpeed(Animator animator, Result result, string name, float speed, string expected)
        {
            animator.SetFloat("Speed", speed);
            for (int i = 0; i < 6; i++) animator.Update(0.1f);

            var info = animator.GetCurrentAnimatorStateInfo(0);
            bool ok = info.IsName(expected);
            var actual = StateName(animator, info.shortNameHash);

            Log(result, name, $"speed:{speed:0.#}->{expected}", ok ? "PASS" : "FAIL",
                ok ? $"state={actual} clip={CurrentClip(animator)}" : $"stayed in state={actual} clip={CurrentClip(animator)}");
        }

        /// <summary>Maps the current state hash back to its name, so failures say where it got stuck.</summary>
        private static string StateName(Animator animator, int hash)
        {
            var controller = animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
            if (controller == null || controller.layers.Length == 0) return "unknown";

            foreach (var state in controller.layers[0].stateMachine.states)
            {
                if (state.state.nameHash == hash) return state.state.name;
            }

            return "unknown";
        }

        private static string CurrentClip(Animator animator)
        {
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            return clips.Length > 0 && clips[0].clip != null ? clips[0].clip.name : "none";
        }

        private static void Log(Result result, string character, string check, string outcome, string detail)
        {
            if (outcome == "PASS") result.Pass++;
            else result.Fail++;

            var line = $"[MP] {(outcome == "PASS" ? "INFO SELFTEST" : "ERROR SELFTEST_FAIL")} " +
                       $"character={character} check={check} outcome={outcome} detail={detail}";

            if (outcome == "PASS") Debug.Log(line);
            else Debug.LogError(line);
        }

        private static void Cleanup(GameObject instance, UnityEngine.SceneManagement.Scene scene)
        {
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            EditorSceneManager.CloseScene(scene, true);
        }

        private sealed class Result
        {
            public int Pass;
            public int Fail;
        }
    }
}
