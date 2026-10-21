using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Unity.Cursory.EditorTools
{
    /// <summary>
    /// The Windows client build (UC-T27) that <c>deploy.bat</c> / <c>tools/deploy.ps1</c> publish to C:\Apps\Unity.Cursory,
    /// the same shape as Unity.Eve's and Unity.SpaceGod's: one StandaloneWindows64 player of <see cref="ProjectSetup.ScenePath"/>.
    /// The dedicated server is built separately, by Unity.Multiplayer's <c>mp-host game</c> (root <c>deploy.ps1</c>).
    /// Player settings (window mode, resolution) are left as ProjectSettings has them, so a deploy never dirties the repo.
    ///
    /// Batch: powershell -File tools/unity.ps1 -Method Unity.Cursory.EditorTools.CursoryBuild.BuildWindowsRelease -Graphics
    /// </summary>
    public static class CursoryBuild
    {
        public const string ExecutableName = "Unity.Cursory.exe";
        public const string ReleaseOutput = "Build/WindowsRelease/" + ExecutableName;
        public const string DevelopmentOutput = "Build/Windows/" + ExecutableName;

        [MenuItem("Unity.Cursory/Build/Windows Player")]
        public static void BuildWindows() => Build(DevelopmentOutput, BuildOptions.Development);

        [MenuItem("Unity.Cursory/Build/Windows Player (Release)")]
        public static void BuildWindowsRelease() => Build(ReleaseOutput, BuildOptions.None);

        private static void Build(string output, BuildOptions buildOptions)
        {
            try
            {
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ProjectSetup.ScenePath },
                    locationPathName = output,
                    target = BuildTarget.StandaloneWindows64,
                    subtarget = (int)StandaloneBuildSubtarget.Player,
                    options = buildOptions,
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                bool ok = report.summary.result == BuildResult.Succeeded;
                Debug.Log($"[Build] {report.summary.result} {report.summary.totalSize / (1024 * 1024)} MB, " +
                          $"{report.summary.totalErrors} errors -> {output}");
                if (!ok)
                {
                    foreach (var m in report.steps.SelectMany(s => s.messages).Where(m => m.type == LogType.Error))
                        Debug.Log($"[Build] {m.content}");
                }

                if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }
    }
}
