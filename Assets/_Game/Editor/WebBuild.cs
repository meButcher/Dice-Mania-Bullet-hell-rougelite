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

        /// <summary>Windowed Windows build for quick local playtests: Builds/Win/DiceMania.exe.
        /// CLI: -executeMethod DiceMania.EditorTools.WebBuild.BuildWindows</summary>
        [MenuItem("Dice Mania/Build Windows (playtest)", priority = 21)]
        public static void BuildWindowsMenu() => BuildWin(false);

        public static void BuildWindows() => BuildWin(true);

        static void BuildWin(bool exitOnFinish)
        {
            ProjectSetup.Setup();
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            // D3D11 + classic swap chain: plays nicely with screen recorders / remote-control screenshots.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 });
            PlayerSettings.useFlipModelSwapchain = false;
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.ScenePath },
                // CLI: -buildOut <path/DiceMania.exe> builds somewhere else (e.g. while the last build is still running).
                locationPathName = Arg("-buildOut", "Builds/Win/DiceMania.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report;
            try
            {
                report = BuildPipeline.BuildPlayer(options);
            }
            catch (Exception e)
            {
                Debug.LogError("[DiceMania] Windows build threw: " + e);
                if (exitOnFinish) EditorApplication.Exit(1);
                return;
            }
            var s = report.summary;
            Debug.Log($"[DiceMania] Windows build {s.result}: {s.totalSize / (1024f * 1024f):0.0} MB, {s.totalErrors} errors, {s.totalTime}");
            if (exitOnFinish) EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }

        static string Arg(string name, string fallback)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
            return fallback;
        }

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
