#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace KeepBlinking.EditorTools
{
  /// <summary>Enforces the shared FILTER sprite import contract.</summary>
  internal sealed class CareStationFilterArtImporter : AssetPostprocessor
  {
    private const string ArtFolder = "Assets/KeepBlinking/Art/CareStation/Filter";

    private void OnPreprocessTexture()
    {
      if (!assetPath.StartsWith(ArtFolder + "/", StringComparison.Ordinal) ||
          !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return;
      var importer = (TextureImporter)assetImporter;
      importer.textureType = TextureImporterType.Sprite;
      importer.spriteImportMode = SpriteImportMode.Single;
      importer.sRGBTexture = true;
      importer.alphaSource = TextureImporterAlphaSource.FromInput;
      importer.alphaIsTransparency = true;
      importer.mipmapEnabled = true;
      importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
      importer.wrapMode = TextureWrapMode.Clamp;
      importer.filterMode = FilterMode.Trilinear;
      importer.maxTextureSize = 2048;
      importer.textureCompression = TextureImporterCompression.Uncompressed;
      importer.crunchedCompression = false;
      importer.npotScale = TextureImporterNPOTScale.None;
      importer.spritePixelsPerUnit = 100f;

      var iosPlatform = BuildPipeline.GetBuildTargetName(BuildTarget.iOS);
      var iosSettings = importer.GetPlatformTextureSettings(iosPlatform);
      iosSettings.name = iosPlatform;
      iosSettings.overridden = true;
      iosSettings.maxTextureSize = 2048;
      iosSettings.format = TextureImporterFormat.RGBA32;
      iosSettings.textureCompression = TextureImporterCompression.Uncompressed;
      iosSettings.crunchedCompression = false;
      importer.SetPlatformTextureSettings(iosSettings);

      var settings = new TextureImporterSettings();
      importer.ReadTextureSettings(settings);
      settings.spriteAlignment = (int)SpriteAlignment.Custom;
      settings.spritePivot = new Vector2(0.5f, 0f);
      settings.spriteExtrude = 4;
      settings.spriteMeshType = SpriteMeshType.FullRect;
      importer.SetTextureSettings(settings);
    }

    [MenuItem("KeepBlinking/Care Station/Reimport FILTER Art")]
    private static void ReimportFromMenu()
    {
      AssetDatabase.ImportAsset(ArtFolder, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
    }
  }
}
#endif
