#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KeepBlinking.EditorTools
{
  /// <summary>Enforces the shared FILTER sprite import contract.</summary>
  internal sealed class CareStationFilterArtImporter : AssetPostprocessor
  {
    private const string ArtFolder = "Assets/KeepBlinking/Art/CareStation/Filter";
    private static readonly HashSet<string> PhoneLevelOneLayers = new HashSet<string>(StringComparer.Ordinal)
    {
      "FilterL1_MachineBase.png",
      "FilterL1_RawLiquidBody.png",
      "FilterL1_RawLiquidSurface.png",
      "FilterL1_FilterBed.png",
      "FilterL1_FilterDrips_01.png",
      "FilterL1_FilterDrips_02.png",
      "FilterL1_FilterDrips_03.png",
      "FilterL1_FilterDrips_04.png",
      "FilterL1_OutletFlow_01.png",
      "FilterL1_OutletFlow_02.png",
      "FilterL1_OutletFlow_03.png",
      "FilterL1_OutletFlow_04.png",
      "FilterL1_BottleGlass.png",
      "FilterL1_BottleLiquidBody.png",
      "FilterL1_BottleLiquidSurface.png"
    };

    private void OnPreprocessTexture()
    {
      if (!assetPath.StartsWith(ArtFolder + "/", StringComparison.Ordinal) ||
          !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return;
      var isLevelOne = PhoneLevelOneLayers.Contains(Path.GetFileName(assetPath));
      var importer = (TextureImporter)assetImporter;
      importer.textureType = TextureImporterType.Sprite;
      importer.spriteImportMode = SpriteImportMode.Single;
      importer.sRGBTexture = true;
      importer.alphaSource = TextureImporterAlphaSource.FromInput;
      importer.alphaIsTransparency = true;
      // The phone-sized Level 1 sprites render directly in Screen Space UI.
      // Mips add pale fringes around transparent glass/liquid edges there.
      // Leave the established Level 2/3 contract untouched.
      importer.mipmapEnabled = !isLevelOne;
      importer.mipmapFilter = TextureImporterMipFilter.KaiserFilter;
      importer.wrapMode = TextureWrapMode.Clamp;
      importer.filterMode = isLevelOne ? FilterMode.Bilinear : FilterMode.Trilinear;
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
