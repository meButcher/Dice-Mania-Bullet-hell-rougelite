using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DiceMania.EditorTools
{
    /// <summary>
    /// One-click project configuration: creates the boot scene, registers it for builds and applies Web player settings.
    /// Runs automatically the first time the project is opened (if the scene is missing).
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        public const string ScenePath = "Assets/_Game/Scenes/Main.unity";

        static ProjectSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(ScenePath) && !Application.isBatchMode) Setup();
            };
        }

        [MenuItem("Dice Mania/Setup Project", priority = 0)]
        public static void Setup()
        {
            Directory.CreateDirectory("Assets/_Game/Scenes");
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            PlayerSettings.companyName = "meButcher";
            PlayerSettings.productName = "Dice Mania Survivors";
            PlayerSettings.colorSpace = ColorSpace.Gamma; // cheaper on WebGL2 + matches the neon look
            PlayerSettings.runInBackground = true;

            // Web
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true; // itch.io serves files without Content-Encoding headers
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Minimal);
            PlayerSettings.stripEngineCode = true;

            AssetDatabase.SaveAssets();
            Debug.Log("[DiceMania] Project setup complete. Press Play in " + ScenePath + " (or any scene).");
        }
    }
}
