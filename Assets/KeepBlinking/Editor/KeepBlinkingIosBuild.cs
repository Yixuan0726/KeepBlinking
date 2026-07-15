using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace KeepBlinking.Editor
{
  public static class KeepBlinkingIosBuild
  {
    private const string MvpScenePath = "Assets/Scenes/SampleScene.unity";
    private const string DefaultOutputPath = "Builds/iOS/KeepBlinking";

    [MenuItem("KeepBlinking/Build/iOS Xcode Project")]
    public static void BuildXcodeProject()
    {
      var outputPath = GetCommandLineArgument("-outputPath");
      if (string.IsNullOrWhiteSpace(outputPath))
      {
        outputPath = DefaultOutputPath;
      }

      outputPath = outputPath.Replace('\\', '/');
      Directory.CreateDirectory(outputPath);

      EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
      PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
      PlayerSettings.allowedAutorotateToPortrait = true;
      PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
      PlayerSettings.allowedAutorotateToLandscapeLeft = false;
      PlayerSettings.allowedAutorotateToLandscapeRight = false;
      PlayerSettings.iOS.targetOSVersionString = "13.0";

      var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
      {
        scenes = new[] { MvpScenePath },
        locationPathName = outputPath,
        target = BuildTarget.iOS,
        options = BuildOptions.None,
      });

      var summary = report.summary;
      if (summary.result != BuildResult.Succeeded)
      {
        throw new InvalidOperationException($"iOS Xcode project build failed: {summary.result}");
      }

      UnityEngine.Debug.Log($"KeepBlinking iOS Xcode project built: {summary.outputPath} ({summary.totalSize} bytes)");
    }

    private static string GetCommandLineArgument(string name)
    {
      var args = Environment.GetCommandLineArgs();
      for (var i = 0; i < args.Length - 1; i++)
      {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
          return args[i + 1];
        }
      }

      return null;
    }
  }
}
