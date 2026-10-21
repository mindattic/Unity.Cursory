using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Unity.Cursory.EditorTools
{
    /// <summary>
    /// The Linux player UC-T14's nightly compose smoke runs (smoke/docker-compose.yml): one StandaloneLinux64 build of
    /// <see cref="ProjectSetup.ScenePath"/> plays every role, as in Unity.Multiplayer's own nightly smoke. Launched
    /// with <c>-mode server -level 2 -joincodefile ...</c> it is the dedicated server; with <c>-bot A|B
    /// -joincodefile ...</c> it is a <see cref="BotDriver"/>. A plain player rather than the Dedicated Server
    /// subtarget, because GameCI's editor image has no Linux Dedicated Server build support.
    ///
    /// Batch: Unity -batchmode -quit -nographics -projectPath . -executeMethod
    /// Unity.Cursory.EditorTools.BotBuild.BuildFromCommandLine [-botOutput dir] (default smoke/build/Cursory.x86_64).
    /// </summary>
    public static class BotBuild
    {
        public const string ExecutableName = "Cursory.x86_64";

        public static void BuildFromCommandLine()
        {
            try
            {
                string output = Arg("-botOutput");
                if (string.IsNullOrWhiteSpace(output))
                {
                    output = Path.GetFullPath(Path.Combine(Application.dataPath, "../smoke/build"));
                }

                Directory.CreateDirectory(output);
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ProjectSetup.ScenePath },
                    locationPathName = Path.Combine(output, ExecutableName),
                    target = BuildTarget.StandaloneLinux64,
                    subtarget = (int)(string.Equals(Arg("-botSubtarget"), "server", StringComparison.OrdinalIgnoreCase)
                        ? StandaloneBuildSubtarget.Server
                        : StandaloneBuildSubtarget.Player),
                    options = Arg("-botDevelopment") != null ? BuildOptions.Development : BuildOptions.None,
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                bool ok = report.summary.result == BuildResult.Succeeded;
                Debug.Log("[Unity.Cursory] Bot/smoke Linux player build " + (ok ? "succeeded" : "FAILED (" + report.summary.result + ")") + ": " + output);
                if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            }

            return null;
        }
    }
}
