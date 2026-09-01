#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Audio;

namespace KeepBlinking.EditorTools
{
  /// <summary>Import and routing contract for the authored Care Routine music.</summary>
  internal sealed class CareRoutineMusicImporter : AssetPostprocessor, IPreprocessBuildWithReport
  {
    internal const string MusicFolder = "Assets/KeepBlinking/Resources/CareStation/Audio/Music";
    internal const string MusicAssetPath = MusicFolder + "/LongNight_Aventure.mp3";
    internal const string MixerAssetPath = MusicFolder + "/CareRoutineMixer.mixer";

    [InitializeOnLoadMethod]
    private static void ScheduleMixerCreation()
    {
      EditorApplication.delayCall += EnsureMusicMixer;
    }

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
      EnsureMusicMixer();
    }

    private void OnPreprocessAudio()
    {
      if (!string.Equals(assetPath, MusicAssetPath, StringComparison.OrdinalIgnoreCase)) return;
      var importer = (AudioImporter)assetImporter;
      importer.forceToMono = false;
      importer.ambisonic = false;
      importer.loadInBackground = true;
      importer.defaultSampleSettings = StreamingVorbisSettings();
      importer.SetOverrideSampleSettings("Android", StreamingVorbisSettings());
      importer.SetOverrideSampleSettings("iOS", StreamingVorbisSettings());
    }

    private static AudioImporterSampleSettings StreamingVorbisSettings()
    {
      return new AudioImporterSampleSettings
      {
        loadType = AudioClipLoadType.Streaming,
        compressionFormat = AudioCompressionFormat.Vorbis,
        preloadAudioData = false,
        quality = 0.72f,
        sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate,
      };
    }

    private static void EnsureMusicMixer()
    {
      if (EditorApplication.isCompiling || EditorApplication.isUpdating)
      {
        EditorApplication.delayCall += EnsureMusicMixer;
        return;
      }
      var existingMixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerAssetPath);
      if (existingMixer != null && existingMixer.FindMatchingGroups("Music").Length > 0) return;

      var controllerType = typeof(AudioImporter).Assembly.GetType(
        "UnityEditor.Audio.AudioMixerController", true);
      var flags = BindingFlags.Public | BindingFlags.NonPublic |
                  BindingFlags.Static | BindingFlags.Instance;
      var createMixer = controllerType.GetMethod(
        "CreateMixerControllerAtPath", flags, null, new[] { typeof(string) }, null);
      var controller = existingMixer != null
        ? (object)existingMixer
        : createMixer?.Invoke(null, new object[] { MixerAssetPath });
      if (controller == null)
        throw new InvalidOperationException("Unity could not create the Care Routine Audio Mixer.");

      var master = controllerType.GetProperty("masterGroup", flags)?.GetValue(controller);
      var createGroup = controllerType.GetMethod(
        "CreateNewGroup", flags, null, new[] { typeof(string), typeof(bool) }, null);
      var music = createGroup?.Invoke(controller, new[] { (object)"Music", false });
      var groupType = typeof(AudioImporter).Assembly.GetType(
        "UnityEditor.Audio.AudioMixerGroupController", true);
      var addChild = controllerType.GetMethod(
        "AddChildToParent", flags, null, new[] { groupType, groupType }, null);
      if (master == null || music == null || addChild == null)
        throw new InvalidOperationException("Unity could not create the Care Routine Music mixer group.");
      addChild.Invoke(controller, new[] { music, master });
      AssetDatabase.SaveAssets();
      AssetDatabase.ImportAsset(MixerAssetPath, ImportAssetOptions.ForceSynchronousImport);
      Debug.Log("Created Care Routine Audio Mixer with an independent Music group.");
    }
  }
}
#endif
