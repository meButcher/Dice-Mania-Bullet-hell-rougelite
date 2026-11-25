using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DiceMania.EditorTools
{
    /// <summary>
    /// Web build entry points. From the command line:
    ///   Unity -batchmode -quit -projectPath . -executeMethod DiceMania.EditorTools.WebBuild.BuildWeb -logFile build.log
    /// Output: Builds/Web
    /// </summary>
    public static class WebBuild
    {
        public const string OutputPath = "Builds/Web";

        [MenuItem("Dice Mania/Build Web", priority = 20)]
        public static void BuildWebMenu() => Build(false);

        public static void BuildWeb() => Build(true);

        static void Build(bool exitOnFinish)
        {
            ProjectSetup.Setup();
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);

            Directory.CreateDirectory(OutputPath);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };
            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            catch (Exception e)
            {
                Debug.LogError("[DiceMania] Build threw: " + e);
                if (exitOnFinish) EditorApplication.Exit(1);
                return;
            }
            var s = report.summary;
            Debug.Log($"[DiceMania] Web build {s.result}: {s.totalSize / (1024f * 1024f):0.0} MB, {s.totalErrors} errors, {s.totalTime}");
            if (exitOnFinish) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
