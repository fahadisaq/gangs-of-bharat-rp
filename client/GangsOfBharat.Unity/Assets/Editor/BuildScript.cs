// UNVERIFIED: written offline, never compiled against the real UnityEditor.
// Local fallback (scripts/build-android-mac.sh): -executeMethod GangsOfBharat.EditorTools.BuildScript.BuildAndroid
// Unity Build Automation: set the pre-export method to GangsOfBharat.EditorTools.BuildScript.PreExport (UBA runs the build itself).
// Optional arguments: -outputPath <file.apk>  (default build/Android/GangsOfBharat.apk under the repo root)
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GangsOfBharat.EditorTools
{
    public static class BuildScript
    {
        private const string DefaultOutput = "build/Android/GangsOfBharat.apk";
        private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
        private const string DefaultPackageName = "com.gangsofbharat.rp";
        private const string DefaultVersion = "0.1.0";

        // Unity Build Automation pre-export method. Does not call BuildPipeline: UBA builds the player itself.
        // Optional env vars (set in the UBA build configuration): GOB_VERSION, GOB_BUNDLE_CODE, GOB_PACKAGE_NAME.
        public static void PreExport()
        {
            PlayerSettings.SetApplicationIdentifier(
                NamedBuildTarget.Android,
                Environment.GetEnvironmentVariable("GOB_PACKAGE_NAME") ?? DefaultPackageName);
            PlayerSettings.bundleVersion = Environment.GetEnvironmentVariable("GOB_VERSION") ?? DefaultVersion;
            if (int.TryParse(Environment.GetEnvironmentVariable("GOB_BUNDLE_CODE"), out int code))
            {
                PlayerSettings.Android.bundleVersionCode = code;
            }
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.buildAppBundle = false;
            EnableBothInputHandlers();
            EnsureScene();
            AssetDatabase.SaveAssets();
            Debug.Log("PreExport done: " + PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android)
                + " v" + PlayerSettings.bundleVersion);
        }

        // The repo has no ProjectSettings.asset, so Unity defaults to the old Input Manager and the
        // Input System code would get no input. 2 = "Both". There is no public API for this setting.
        private static void EnableBothInputHandlers()
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets.Length == 0)
            {
                Debug.LogWarning("PreExport: ProjectSettings.asset not found; input handler left unchanged.");
                return;
            }
            var settings = new SerializedObject(assets[0]);
            SerializedProperty handler = settings.FindProperty("activeInputHandler");
            if (handler == null)
            {
                Debug.LogWarning("PreExport: activeInputHandler property not found; input handler left unchanged.");
                return;
            }
            handler.intValue = 2;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }

        // If Build Settings has no enabled scene, create an empty bootstrap scene so the player can be built.
        private static void EnsureScene()
        {
            if (EditorBuildSettings.scenes.Any(s => s.enabled))
            {
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(BootstrapScenePath));
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, BootstrapScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootstrapScenePath, true) };
            Debug.Log("No scene in Build Settings; created " + BootstrapScenePath);
        }

        public static void BuildAndroid()
        {
            try
            {
                string[] scenes = EditorBuildSettings.scenes
                    .Where(s => s.enabled)
                    .Select(s => s.path)
                    .ToArray();
                if (scenes.Length == 0)
                {
                    throw new InvalidOperationException("No enabled scenes in Build Settings.");
                }

                string output = ReadArg("-outputPath") ?? DefaultOutput;
                if (!Path.IsPathRooted(output))
                {
                    // Project root is client/GangsOfBharat.Unity; output is relative to the repo root.
                    string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
                    output = Path.Combine(repoRoot, output);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(output));

                EditorUserBuildSettings.buildAppBundle = false;
                ApplyOptionalSigning();

                var options = new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = output,
                    target = BuildTarget.Android,
                    options = BuildOptions.None
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new Exception("Build finished with result " + report.summary.result);
                }
                Debug.Log("Build succeeded: " + output + " (" + report.summary.totalSize + " bytes)");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError("Build failed: " + e);
                EditorApplication.Exit(1);
            }
        }

        // Signs with a release keystore when the env vars are set, otherwise Unity uses the debug keystore.
        private static void ApplyOptionalSigning()
        {
            string keystore = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PATH");
            if (string.IsNullOrEmpty(keystore))
            {
                return;
            }
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = keystore;
            PlayerSettings.Android.keystorePass = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PASSWORD");
            PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable("ANDROID_KEY_ALIAS");
            PlayerSettings.Android.keyaliasPass = Environment.GetEnvironmentVariable("ANDROID_KEY_PASSWORD");
        }

        private static string ReadArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name)
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}
