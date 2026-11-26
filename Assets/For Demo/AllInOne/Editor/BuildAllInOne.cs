using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EmbodiedAgent.AllInOne.Editor
{
    /// <summary>
    /// Builds players that contain only the integrated demo. Package id and product name are set for the build alone, so
    /// that the demo installs next to a build of another scene. Builds go to the folder Builds, which git ignores.
    /// </summary>
    public static class BuildAllInOne
    {
        internal const string KeyPattern = @"\bsk-[A-Za-z0-9_-]{8,}";

        private const string PackageId = "com.jiahaoliu.embodiedaiagentvr.allinone";
        private const string ProductName = "Embodied AI Agent All-in-One";
        private const string KeyPlaceholder = "YOUR_API_KEY";
        private const string AndroidFile = "Builds/EmbodiedAgent-AllInOne.apk";
        private const string WindowsFile = "Builds/AllInOne-Windows/EmbodiedAgent-AllInOne.exe";

        [MenuItem(AllInOnePaths.Menu + "Build for Quest (APK)")]
        public static void Android() => Build(BuildTarget.Android, NamedBuildTarget.Android, AndroidFile);

        [MenuItem(AllInOnePaths.Menu + "Build for Windows")]
        public static void Windows() => Build(BuildTarget.StandaloneWindows64, NamedBuildTarget.Standalone, WindowsFile);

        /// <summary>True when the key field is empty or holds the placeholder. The value itself is never used.</summary>
        internal static bool HasNoKey(RealtimeAPIWrapper api)
        {
            string value = new SerializedObject(api).FindProperty("apiKey").stringValue;
            return string.IsNullOrWhiteSpace(value) || value == KeyPlaceholder;
        }

        private static void Build(BuildTarget target, NamedBuildTarget named, string file)
        {
            CheckKeys();
            BuildReport report;
            using (new SettingsBackup(named))
            {
                PlayerSettings.productName = ProductName;
                PlayerSettings.SetApplicationIdentifier(named, PackageId);
                PlayerSettings.Android.forceInternetPermission = true;
                EditorUserBuildSettings.buildAppBundle = false;
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { AllInOnePaths.Scene },
                    locationPathName = file,
                    target = target,
                    targetGroup = BuildPipeline.GetBuildTargetGroup(target)
                });
            }

            BuildSummary summary = report.summary;
            Debug.Log("[AIO] Build " + summary.result + ": " + file + ", " + summary.totalSize / 1048576 + " MB, " +
                      summary.totalErrors + " errors, " + summary.totalTime.TotalSeconds.ToString("0") + " s");
            if (Application.isBatchMode) EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>Puts back everything that a build changes in the project, also when it fails.</summary>
        private sealed class SettingsBackup : IDisposable
        {
            private static readonly string[] Folders = { "ProjectSettings", "Assets/Settings" };

            private readonly NamedBuildTarget target;
            private readonly string productName, packageId;
            private readonly bool internet, bundle;
            private readonly Object[] preloaded;
            private readonly Dictionary<string, byte[]> files;

            public SettingsBackup(NamedBuildTarget target)
            {
                this.target = target;
                productName = PlayerSettings.productName;
                packageId = PlayerSettings.GetApplicationIdentifier(target);
                internet = PlayerSettings.Android.forceInternetPermission;
                bundle = EditorUserBuildSettings.buildAppBundle;
                preloaded = PlayerSettings.GetPreloadedAssets();
                files = Folders.SelectMany(folder => Directory.GetFiles(folder, "*.asset")).ToDictionary(f => f, File.ReadAllBytes);
            }

            public void Dispose()
            {
                PlayerSettings.productName = productName;
                PlayerSettings.SetApplicationIdentifier(target, packageId);
                PlayerSettings.Android.forceInternetPermission = internet;
                PlayerSettings.SetPreloadedAssets(preloaded);
                EditorUserBuildSettings.buildAppBundle = bundle;

                // A build also writes notes of its own into the settings, for example which shader variants it needed.
                AssetDatabase.SaveAssets();
                foreach (KeyValuePair<string, byte[]> file in files)
                {
                    if (File.ReadAllBytes(file.Key).SequenceEqual(file.Value)) continue;
                    File.WriteAllBytes(file.Key, file.Value);
                    if (file.Key.StartsWith("Assets")) AssetDatabase.ImportAsset(file.Key);
                }
            }
        }

        /// <summary>Stops a build that would contain a key.</summary>
        private static void CheckKeys()
        {
            EditorSceneManager.OpenScene(AllInOnePaths.Scene, OpenSceneMode.Single);
            RealtimeAPIWrapper filled = Object.FindObjectsByType<RealtimeAPIWrapper>(FindObjectsInactive.Include).FirstOrDefault(api => !HasNoKey(api));
            if (filled != null) throw new BuildFailedException("The key field of " + filled.name + " is not empty.");
            if (Regex.IsMatch(File.ReadAllText(AllInOnePaths.Scene), KeyPattern)) throw new BuildFailedException("The scene file contains something like a key.");
        }
    }
}
