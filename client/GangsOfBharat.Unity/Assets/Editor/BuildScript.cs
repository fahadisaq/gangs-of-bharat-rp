// UNVERIFIED: written offline, never compiled against the real UnityEditor.
// Called from codemagic.yaml with: -executeMethod GangsOfBharat.EditorTools.BuildScript.BuildAndroid
// Optional arguments: -outputPath <file.apk>  (default build/Android/GangsOfBharat.apk under the repo root)
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GangsOfBharat.EditorTools
{
    public static class BuildScript
    {
        private const string DefaultOutput = "build/Android/GangsOfBharat.apk";

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
