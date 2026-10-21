using System.IO;
using Unity.Multiplayer.Editor;
using Unity.Multiplayer.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Unity.Cursory.EditorTools
{
    /// <summary>
    /// Writes the project's URP 2D pipeline and its main scene. Both are generated rather than
    /// hand-made so they stay reproducible, and both are committed: they are written only when
    /// missing, because Unity gives every object a fresh file ID and regenerating on each run would
    /// leave a no-op diff. To change them, edit this file, run the menu item, and commit both.
    ///
    /// Batch: Unity -batchmode -quit -projectPath . -executeMethod Unity.Cursory.EditorTools.ProjectSetup.SetUpAll
    /// </summary>
    public static class ProjectSetup
    {
        public const string SettingsFolder = "Assets/Unity.Cursory/Settings";
        public const string RendererPath = SettingsFolder + "/Renderer2D.asset";
        public const string PipelinePath = SettingsFolder + "/UniversalRP-2D.asset";
        public const string ScenePath = "Assets/Unity.Cursory/Scenes/Main.unity";

        /// <summary>The empty world's backdrop, Cursory's dark room colour.</summary>
        static readonly Color Backdrop = new Color(0.07f, 0.08f, 0.10f, 1f);

        /// <summary>The library front end's settings for Cursory (UC-T12): the menus' title.</summary>
        public const string FrontEndSettingsPath = SettingsFolder + "/FrontEndSettings.asset";

        /// <summary>Batch entry point: pipeline, project settings, scene and front end, each only if missing.</summary>
        public static void SetUpAll()
        {
            SetUpRendering();
            SetUpProjectSettings();
            if (!File.Exists(ScenePath)) RebuildMainScene();
            SetUpServerScene();
            SetUpFrontEnd();
        }

        /// <summary>
        /// Gives <see cref="ScenePath"/> the library's drop-in front end exactly as a new game gets it (UC-T12): MindAttic ▸
        /// Multiplayer ▸ Add Front End (<see cref="NetFrontEndSetup"/>) adds a NetFrontEnd beside the NetBootstrap, and
        /// its Settings field gets <see cref="FrontEndSettingsPath"/> (title "Cursory"; every other field the library's
        /// default). Rerunning changes nothing.
        /// </summary>
        [MenuItem("Unity.Cursory/Set Up Front End")]
        public static void SetUpFrontEnd()
        {
            var settings = AssetDatabase.LoadAssetAtPath<NetFrontEndSettings>(FrontEndSettingsPath);
            if (settings == null)
            {
                Directory.CreateDirectory(SettingsFolder);
                settings = ScriptableObject.CreateInstance<NetFrontEndSettings>();
                settings.title = "Cursory";
                AssetDatabase.CreateAsset(settings, FrontEndSettingsPath);
                AssetDatabase.SaveAssets();
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            settings = AssetDatabase.LoadAssetAtPath<NetFrontEndSettings>(FrontEndSettingsPath);   // opening the scene unloaded it
            NetFrontEndSetupResult result = NetFrontEndSetup.Run(scene);
            if (!result.Succeeded)
            {
                Debug.LogError("[Unity.Cursory] " + result.Message);
                return;
            }

            bool changed = result.Changes.Count > 0;
            var frontEnd = new SerializedObject(result.FrontEnd);
            SerializedProperty field = frontEnd.FindProperty("settings");
            if (field.objectReferenceValue != settings)
            {
                field.objectReferenceValue = settings;
                frontEnd.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }

            Debug.Log("[Unity.Cursory] Front end " + (changed ? "set up: " + string.Join(" ", result.Changes) : "already set up."));
        }

        /// <summary>A URP asset whose only renderer is the 2D renderer, made the default for every quality level.</summary>
        [MenuItem("Unity.Cursory/Set Up URP 2D")]
        public static void SetUpRendering()
        {
            Directory.CreateDirectory(SettingsFolder);
            var renderer = AssetDatabase.LoadAssetAtPath<Renderer2DData>(RendererPath);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<Renderer2DData>();
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }
            GraphicsSettings.defaultRenderPipeline = pipeline;
            for (int level = 0; level < QualitySettings.names.Length; level++)
            {
                QualitySettings.SetQualityLevel(level, false);
                QualitySettings.renderPipeline = null; // every level falls back to the default above
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Unity.Cursory] URP 2D pipeline: " + PipelinePath);
        }

        /// <summary>
        /// Top-down world with no gravity, stepped only by the server's tick (Script mode, UC-T6); product
        /// identity; the Input System as the only input backend.
        /// </summary>
        [MenuItem("Unity.Cursory/Set Up Project Settings")]
        public static void SetUpProjectSettings()
        {
            Physics2D.gravity = Vector2.zero;
            Physics2D.simulationMode = SimulationMode2D.Script; // ServerSimulation steps it; clients never simulate
            PlayerSettings.companyName = "MindAttic";
            PlayerSettings.productName = "Unity.Cursory";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true; // a host or a second local client keeps ticking unfocused

            var player = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input = player.FindProperty("activeInputHandler");
            if (input != null)
            {
                input.intValue = 1; // Input System package only
                player.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>The main scene: a top-down orthographic camera over the world plane and a global 2D light.</summary>
        [MenuItem("Unity.Cursory/Rebuild Main Scene")]
        public static void RebuildMainScene()
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraGo.transform.SetPositionAndRotation(new Vector3(0f, 0f, -TopDownCamera.Height), Quaternion.identity);
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5.4f; // TopDownCamera resizes it to the screen at runtime
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Backdrop;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 100f;
            cameraGo.AddComponent<UniversalAdditionalCameraData>();
            cameraGo.AddComponent<AudioListener>();
            cameraGo.AddComponent<TopDownCamera>();

            var lightGo = new GameObject("Global Light 2D");
            var light = lightGo.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.intensity = 1f;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[Unity.Cursory] Scene written to " + ScenePath);
        }

        /// <summary>
        /// Networks <see cref="ScenePath"/>, the one scene clients and the dedicated server both boot (library
        /// <see cref="NetSceneSetup"/>), and adds Cursory's <see cref="LevelServer"/>; rerunning changes nothing. The
        /// server package itself needs no Cursory code: <c>mp-host game</c> builds it with the library's batch entry
        /// point, which opens this scene as the first Build Settings scene (UM-T58).
        /// </summary>
        [MenuItem("Unity.Cursory/Set Up Server Scene")]
        public static void SetUpServerScene()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            NetSceneSetupResult result = NetSceneSetup.Run(scene, new NetSceneSetupOptions());
            bool changed = result.Changes.Count > 0;
            if (result.Root.GetComponent<LevelServer>() == null)
            {
                Undo.AddComponent<LevelServer>(result.Root);
                changed = true;
            }

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }

            Debug.Log("[Unity.Cursory] Server scene " + (changed ? "set up: " + string.Join(" ", result.Changes) : "already set up."));
        }
    }
}
