using System;
using Unity.Multiplayer;
using Unity.Multiplayer.Core;
using Unity.Multiplayer.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.Cursory.EditorTools
{
    /// <summary>
    /// Cursory's dedicated server (UC-T13), built entirely by Unity.Multiplayer: the bootable scene is networked by
    /// the library's <see cref="NetSceneSetup"/> plus Cursory's <see cref="LevelServer"/>, and the package is
    /// built by the library's <see cref="ServerPackageBuild"/> (UM-T23). This class only opens that scene first, so
    /// a batch build reads the scene's NetBootstrap settings instead of an empty untitled scene's defaults.
    ///
    /// Batch (what <c>deploy.ps1</c> runs): Unity -batchmode -quit -nographics -projectPath . -executeMethod
    /// Unity.Cursory.EditorTools.ServerBuild.BuildFromCommandLine [-serverProfile GenericVps|RunPod] [-serverOutput dir]
    /// [-port n] [-transport udp|websocket] [-serverBackend mono|il2cpp] [-serverDevelopment]
    /// </summary>
    public static class ServerBuild
    {
        /// <summary>The one scene clients and the server both boot (NGO scene sync needs them to match).</summary>
        public const string ServerScene = "Assets/Unity.Cursory/Scenes/Main.unity";

        public const string SetUpMenuPath = "Unity.Cursory/Set Up Server Scene";

        /// <summary>Networks <see cref="ServerScene"/> (library setup) and adds the <see cref="LevelServer"/>; rerunning changes nothing.</summary>
        [MenuItem(SetUpMenuPath)]
        public static void SetUpServerScene()
        {
            Scene scene = EditorSceneManager.OpenScene(ServerScene, OpenSceneMode.Single);
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

        /// <summary>Opens <see cref="ServerScene"/> and builds the server package with the library; exits 1 on failure.</summary>
        public static void BuildFromCommandLine()
        {
            string error;
            ServerPackageBuildResult result = null;
            try
            {
                Scene scene = EditorSceneManager.OpenScene(ServerScene, OpenSceneMode.Single);
                ServerPackageBuildOptions options = ServerPackageBuild.OptionsFromArgs(Environment.GetCommandLineArgs(), out error);
                if (error == null)
                {
                    // The library's batch options carry only the scene's port and transport; keep the rest of the
                    // scene's NetBootstrap (tick rate, player cap, join codes, ...) with any -port/-transport on top.
                    NetConfig config = ServerPackageBuild.ConfigFromScene(scene);
                    config.Port = options.Config.Port;
                    config.Transport = options.Config.Transport;
                    options.Config = config;
                    options.Scenes = new[] { ServerScene };
                    result = ServerPackageBuild.Build(options);
                    error = result.Succeeded ? null : result.Error;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (error == null)
            {
                Debug.Log("[Unity.Cursory] Server package (" + result.Spec.Profile.Id + ") built in " + result.OutputDirectory);
                return;
            }

            Debug.LogError("[Unity.Cursory] Server package build failed: " + error);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
