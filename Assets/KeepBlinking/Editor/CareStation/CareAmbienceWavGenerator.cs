#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KeepBlinking.EditorTools
{
  /// <summary>Unity menu and import contract for offline-authored care ambience.</summary>
  internal sealed class CareAmbienceWavGenerator : AssetPostprocessor
  {
    [MenuItem("KeepBlinking/Care Station/Audio/Generate Four Care Ambience WAVs")]
    private static void GenerateFromMenu()
    {
      var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
      if (string.IsNullOrEmpty(projectRoot))
        throw new InvalidOperationException("Could not resolve the Unity project root.");

      CareAmbienceSynthesis.GenerateAll(projectRoot);
      AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
      Debug.Log("Generated four deterministic Care Station ambience WAVs at " +
                CareAmbienceSynthesis.AssetFolder + ".");
    }

    private void OnPreprocessAudio()
    {
      if (!assetPath.StartsWith(CareAmbienceSynthesis.AssetFolder + "/", StringComparison.Ordinal) ||
          !assetPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) return;

      var importer = (AudioImporter)assetImporter;
      importer.forceToMono = false;
      importer.ambisonic = false;
      importer.loadInBackground = true;
      importer.defaultSampleSettings = new AudioImporterSampleSettings
      {
        loadType = AudioClipLoadType.CompressedInMemory,
        compressionFormat = AudioCompressionFormat.Vorbis,
        preloadAudioData = true,
        quality = 0.72f,
        sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate,
        sampleRateOverride = CareAmbienceSynthesis.SampleRate,
      };
    }
  }
}
#endif
