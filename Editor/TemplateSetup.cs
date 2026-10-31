using MPBlocks.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MPBlocks.Editor
{
    /// <summary>
    /// Generates the three teaching templates and knows how to build the "empty" scene.
    ///
    /// The templates are ordinary scenes under Assets/Scenes/Templates; they are created
    /// from C# so they stay reviewable and regenerable. They are registered in Build
    /// Settings but disabled: they are starting points for students, not what ships. The
    /// active build scene is whichever game scene the New Game Scene window created.
    /// </summary>
    public static class TemplateSetup
    {
        public const string Template1File = "Template1_WorldInteraction";
        public const string Template2File = "Template2_CoopMission";
        public const string Template3File = "Template3_ArenaCombat";

        public static GameSpec Template1Spec => GameSpec.ForTemplate(Template1File, "template1-world", 4, 7777);
        public static GameSpec Template2Spec => GameSpec.ForTemplate(Template2File, "template2-coop", 4, 7777);
        public static GameSpec Template3Spec => GameSpec.ForTemplate(Template3File, "template3-arena", 4, 7777);

        /// <summary>Editor menu: regenerate all templates, then open the first one.</summary>
        [MenuItem("MPBlocks/Generate Teaching Templates")]
        public static void GenerateFromMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            GenerateAll();
            SceneKit.OpenScene(SceneKit.TemplateScenePath(Template1File));
        }

        /// <summary>Batch entry point used by tooling (`--execute-method`).</summary>
        public static void GenerateForCI()
        {
            GenerateAll();
        }

        public static void GenerateAll()
        {
            SceneKit.EnsureProjectScaffolding();

            var template1 = Template1Spec;
            Template1Builder.Build(template1);
            SceneKit.RegisterScene(template1.ScenePath, false);

            var template2 = Template2Spec;
            Template2Builder.Build(template2);
            SceneKit.RegisterScene(template2.ScenePath, false);

            var template3 = Template3Spec;
            Template3Builder.Build(template3);
            SceneKit.RegisterScene(template3.ScenePath, false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[MP] INFO SETUP_TEMPLATES_DONE count=3");
        }

        /// <summary>
        /// The "no template" starting point: infrastructure, ground and light, four spawn
        /// points and the shared player prefab. Everything else is the group's design.
        /// </summary>
        public static void BuildEmptyScene(GameSpec spec)
        {
            SceneKit.EnsureProjectScaffolding();

            var scene = SceneKit.NewEmptyScene();

            var config = SceneKit.CreateNetConfig(spec, SceneKit.PlayerPrefab(), null);
            SceneKit.CreateInfrastructure(config);

            SceneKit.CreateGround(new Vector3(4f, 1f, 4f), new Color(0.22f, 0.24f, 0.26f));
            SceneKit.CreateDirectionalLight();
            SceneKit.CreateHintText("WASD move | RMB look | E interact");

            SceneKit.CreateSpawnPoint("Spawn_A", new Vector3(-6f, 0f, -6f), Vector3.zero);
            SceneKit.CreateSpawnPoint("Spawn_B", new Vector3(6f, 0f, -6f), Vector3.zero);
            SceneKit.CreateSpawnPoint("Spawn_C", new Vector3(-6f, 0f, 6f), Vector3.zero);
            SceneKit.CreateSpawnPoint("Spawn_D", new Vector3(6f, 0f, 6f), Vector3.zero);

            SceneKit.SaveScene(scene, spec.ScenePath);
            Debug.Log("[MP] INFO SETUP_EMPTY_SCENE path=" + spec.ScenePath);
        }
    }
}
