using System.IO;
using System.Linq;
using System.Collections.Generic;
using KeepBlinking.CareStation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace KeepBlinking.Tests
{
  public sealed class CareStationArtAssetTests
  {
    private const string CrewPath = "Assets/KeepBlinking/Art/CareStation/Crew/eye-care-crew-cart-grip.png";
    private const string CartPath = "Assets/KeepBlinking/Art/CareStation/Carts/care-cart-upgrades-double-decker.png";
    private const string BottlePath = "Assets/KeepBlinking/Art/CareStation/Bottles/care-sample-bottles-clear-empty-full-gold.png";
    private const string FilterFolder = "Assets/KeepBlinking/Art/CareStation/Filter";
    private const string FilterCatalogPath = "Assets/KeepBlinking/Resources/CareStation/Filter/CareStationFilterArtCatalog.asset";
    private static readonly HashSet<string> PhoneLevelOneLayers = new HashSet<string>
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

    [TestCase(CrewPath, 12)]
    [TestCase(CartPath, 3)]
    [TestCase(BottlePath, 3)]
    public void SourceSheetsHaveStableNamedSpriteCounts(string path, int expected)
    {
      Assert.That(AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().Count(), Is.EqualTo(expected));
    }

    [TestCase(CrewPath)]
    [TestCase(CartPath)]
    [TestCase(BottlePath)]
    public void ImportSettingsPreserveTransparentUncompressedSprites(string path)
    {
      var importer = AssetImporter.GetAtPath(path) as TextureImporter;
      Assert.That(importer, Is.Not.Null);
      Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
      Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Multiple));
      Assert.That(importer.alphaIsTransparency, Is.True);
      Assert.That(importer.mipmapEnabled, Is.False);
      Assert.That(importer.crunchedCompression, Is.False);
      Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
      Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear));
    }

    [TestCase("DustKeeper")]
    [TestCase("DrySpotMender")]
    [TestCase("CareCourier")]
    [TestCase("RestGuide")]
    public void CrewPrefabHasStableRequiredHierarchy(string prefabName)
    {
      var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/KeepBlinking/Resources/CareStation/Crew/{prefabName}.prefab");
      Assert.That(prefab, Is.Not.Null);
      Assert.That(prefab.GetComponent<CareCrewArtView>(), Is.Not.Null);
      Assert.That(prefab.transform.Find("CharacterRenderer"), Is.Not.Null);
      Assert.That(prefab.transform.Find("GroundAnchor"), Is.Not.Null);
      Assert.That(prefab.transform.Find("HandAnchor"), Is.Not.Null);
      Assert.That(prefab.transform.Find("FeedbackRoot"), Is.Not.Null);
      Assert.That(prefab.transform.Find("CartRoot"), Is.Not.Null);
      Assert.That(prefab.transform.Find("CartRoot/LowerSampleAnchors"), Is.Not.Null);
      Assert.That(prefab.transform.Find("CartRoot/UpperSampleAnchors"), Is.Not.Null);
    }

    [Test]
    public void EmptyBottleInteriorIsActuallyTransparent()
    {
      var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
      Assert.That(ImageConversion.LoadImage(texture, File.ReadAllBytes(BottlePath), false), Is.True);
      try
      {
        // Source point lies inside the glass body, away from the opaque outline and cork.
        Assert.That(texture.GetPixel(414, texture.height - 1 - 450).a, Is.LessThan(0.01f));
      }
      finally
      {
        Object.DestroyImmediate(texture);
      }
    }

    [Test]
    public void FilterUsesStableSeparatedAuthoredSpriteLayers()
    {
      var expected = new List<string>
      {
        "FilterL1_MachineBase.png",
        "FilterL1_RawLiquidBody.png",
        "FilterL1_RawLiquidSurface.png",
        "FilterL1_FilterBed.png",
        "FilterL1_BottleGlass.png",
        "FilterL1_BottleLiquidBody.png",
        "FilterL1_BottleLiquidSurface.png",
        "FilterL1_Badge.png",
      };
      for (var frame = 1; frame <= 4; frame++)
      {
        expected.Add($"FilterL1_FilterDrips_{frame:00}.png");
        expected.Add($"FilterL1_OutletFlow_{frame:00}.png");
      }
      for (var level = 2; level <= 3; level++)
      {
        expected.Add($"Filter_L{level}_Base.png");
        expected.Add($"Filter_L{level}_Badge.png");
        for (var frame = 1; frame <= 4; frame++) expected.Add($"Filter_L{level}_Flow_{frame:00}.png");
      }
      expected.Add("Filter_L2_Crank.png");
      expected.Add("Filter_L3_Brush.png");
      expected.Add("Filter_L3_GaugeNeedle.png");

      var actual = Directory.GetFiles(FilterFolder, "*.png", SearchOption.TopDirectoryOnly)
        .Select(Path.GetFileName)
        .OrderBy(name => name)
        .ToArray();
      Assert.That(actual, Is.SupersetOf(expected),
        "Every approved Level 1 phone sprite and the untouched Level 2/3 set must exist independently.");
      Assert.That(File.Exists(Path.Combine(FilterFolder, "Filter_L1_Crank.png")), Is.False,
        "The approved Level 1 design has no crank and must not retain the retired placeholder layer.");
    }

    [TestCase("FilterL1_MachineBase.png", 500, 750)]
    [TestCase("FilterL1_RawLiquidBody.png", 164, 164)]
    [TestCase("FilterL1_RawLiquidSurface.png", 176, 28)]
    [TestCase("FilterL1_FilterBed.png", 196, 88)]
    [TestCase("FilterL1_FilterDrips_01.png", 84, 108)]
    [TestCase("FilterL1_FilterDrips_02.png", 84, 108)]
    [TestCase("FilterL1_FilterDrips_03.png", 84, 108)]
    [TestCase("FilterL1_FilterDrips_04.png", 84, 108)]
    [TestCase("FilterL1_OutletFlow_01.png", 68, 176)]
    [TestCase("FilterL1_OutletFlow_02.png", 68, 176)]
    [TestCase("FilterL1_OutletFlow_03.png", 68, 176)]
    [TestCase("FilterL1_OutletFlow_04.png", 68, 176)]
    [TestCase("FilterL1_BottleGlass.png", 144, 216)]
    [TestCase("FilterL1_BottleLiquidBody.png", 86, 124)]
    [TestCase("FilterL1_BottleLiquidSurface.png", 90, 24)]
    public void LevelOneSpritesUsePhoneTargetPixelCanvases(string fileName, int width, int height)
    {
      var path = $"{FilterFolder}/{fileName}";
      var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
      Assert.That(texture, Is.Not.Null, path);
      Assert.That(texture.width, Is.EqualTo(width), path);
      Assert.That(texture.height, Is.EqualTo(height), path);
    }

    [Test]
    public void FilterSpritesUseHighQualityRuntimeImports()
    {
      foreach (var path in Directory.GetFiles(FilterFolder, "*.png", SearchOption.TopDirectoryOnly))
      {
        var assetPath = path.Replace('\\', '/');
        var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
        Assert.That(importer, Is.Not.Null, assetPath);
        Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite), assetPath);
        Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single), assetPath);
        Assert.That(importer.sRGBTexture, Is.True, assetPath);
        Assert.That(importer.alphaSource, Is.EqualTo(TextureImporterAlphaSource.FromInput), assetPath);
        Assert.That(importer.alphaIsTransparency, Is.True, assetPath);
        var isLevelOne = PhoneLevelOneLayers.Contains(Path.GetFileName(path));
        Assert.That(importer.mipmapEnabled, Is.EqualTo(!isLevelOne), assetPath);
        Assert.That(importer.mipmapFilter, Is.EqualTo(TextureImporterMipFilter.KaiserFilter), assetPath);
        Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), assetPath);
        Assert.That(importer.maxTextureSize, Is.EqualTo(2048), assetPath);
        Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), assetPath);
        Assert.That(importer.crunchedCompression, Is.False, assetPath);
        Assert.That(importer.filterMode,
          Is.EqualTo(isLevelOne ? FilterMode.Bilinear : FilterMode.Trilinear), assetPath);
        Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(100f), assetPath);

        var importedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        var importedSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        Assert.That(importedTexture, Is.Not.Null, assetPath);
        Assert.That(importedTexture.width, Is.GreaterThan(0).And.LessThanOrEqualTo(2048), assetPath);
        Assert.That(importedTexture.height, Is.GreaterThan(0).And.LessThanOrEqualTo(2048), assetPath);
        if (isLevelOne)
          Assert.That(importedTexture.mipmapCount, Is.EqualTo(1), assetPath);
        else
          Assert.That(importedTexture.mipmapCount, Is.GreaterThan(1), assetPath);
        Assert.That(importedSprite, Is.Not.Null, assetPath);
        Assert.That(importedSprite.rect,
          Is.EqualTo(new Rect(0f, 0f, importedTexture.width, importedTexture.height)), assetPath);

        var iosSettings = importer.GetPlatformTextureSettings(BuildPipeline.GetBuildTargetName(BuildTarget.iOS));
        Assert.That(iosSettings.overridden, Is.True, assetPath);
        Assert.That(iosSettings.maxTextureSize, Is.EqualTo(2048), assetPath);
        Assert.That(iosSettings.format, Is.EqualTo(TextureImporterFormat.RGBA32), assetPath);
        Assert.That(iosSettings.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), assetPath);
        Assert.That(iosSettings.crunchedCompression, Is.False, assetPath);

        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        Assert.That(settings.spriteAlignment, Is.EqualTo((int)SpriteAlignment.Custom), assetPath);
        Assert.That(settings.spritePivot, Is.EqualTo(new Vector2(0.5f, 0f)), assetPath);
        Assert.That(settings.spriteExtrude, Is.EqualTo(4u), assetPath);
        Assert.That(settings.spriteMeshType, Is.EqualTo(SpriteMeshType.FullRect), assetPath);
      }
    }

    [Test]
    public void FilterLayersHaveRealAlphaAndKeepFourPixelEdgesTransparent()
    {
      foreach (var path in Directory.GetFiles(FilterFolder, "*.png", SearchOption.TopDirectoryOnly))
      {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Assert.That(ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false), Is.True, path);
        try
        {
          var pixels = texture.GetPixels32();
          Assert.That(pixels.Any(pixel => pixel.a == 0), Is.True, $"{path} must contain real transparent pixels.");
          Assert.That(pixels.Any(pixel => pixel.a > 0), Is.True, $"{path} must contain visible authored artwork.");
          for (var y = 0; y < texture.height; y++)
          {
            for (var x = 0; x < texture.width; x++)
            {
              if (x >= 4 && x < texture.width - 4 && y >= 4 && y < texture.height - 4) continue;
              Assert.That(pixels[y * texture.width + x].a, Is.Zero,
                $"{path} retains non-transparent edge pixels at ({x}, {y}).");
            }
          }
        }
        finally
        {
          Object.DestroyImmediate(texture);
        }
      }
    }

    [Test]
    public void FilterCatalogContainsThreeIndependentAnimatedLevels()
    {
      var catalog = AssetDatabase.LoadAssetAtPath<CareStationFilterArtCatalog>(FilterCatalogPath);
      Assert.That(catalog, Is.Not.Null);
      Assert.That(catalog.Levels, Has.Length.EqualTo(3));
      for (var i = 0; i < catalog.Levels.Length; i++)
      {
        var level = catalog.Levels[i];
        Assert.That(level.level, Is.EqualTo(i + 1));
        Assert.That(level.baseSprite, Is.Not.Null);
        Assert.That(level.badgeSprite, Is.Not.Null);
        Assert.That(level.flowFrames, Has.Length.EqualTo(4));
      }
      var levelOne = catalog.Levels[0];
      Assert.That(levelOne.crankSprite, Is.Null);
      Assert.That(levelOne.rawLiquidSprite, Is.Null);
      Assert.That(levelOne.rawParticlesSprite, Is.Null);
      Assert.That(levelOne.filterCartridgeSprite, Is.Null);
      Assert.That(levelOne.funnelAndPipeSprite, Is.Null);
      Assert.That(levelOne.bottleSprite, Is.Null);
      Assert.That(levelOne.bottleFillSprite, Is.Null);
      AssertFilterSpritePath(levelOne.machineBaseSprite, "FilterL1_MachineBase.png");
      AssertFilterSpritePath(levelOne.baseSprite, "FilterL1_MachineBase.png");
      AssertFilterSpritePath(levelOne.rawLiquidBodySprite, "FilterL1_RawLiquidBody.png");
      AssertFilterSpritePath(levelOne.rawLiquidSurfaceSprite, "FilterL1_RawLiquidSurface.png");
      AssertFilterSpritePath(levelOne.filterBedSprite, "FilterL1_FilterBed.png");
      AssertFilterSpritePath(levelOne.bottleGlassSprite, "FilterL1_BottleGlass.png");
      AssertFilterSpritePath(levelOne.bottleLiquidBodySprite, "FilterL1_BottleLiquidBody.png");
      AssertFilterSpritePath(levelOne.bottleLiquidSurfaceSprite, "FilterL1_BottleLiquidSurface.png");
      AssertFilterSpritePath(levelOne.badgeSprite, "FilterL1_Badge.png");
      Assert.That(levelOne.filterDripFrames, Has.Length.EqualTo(4));
      Assert.That(levelOne.outletFlowFrames, Has.Length.EqualTo(4));
      Assert.That(levelOne.flowFrames, Has.Length.EqualTo(4));
      for (var frame = 0; frame < 4; frame++)
      {
        AssertFilterSpritePath(levelOne.filterDripFrames[frame], $"FilterL1_FilterDrips_{frame + 1:00}.png");
        AssertFilterSpritePath(levelOne.outletFlowFrames[frame], $"FilterL1_OutletFlow_{frame + 1:00}.png");
        Assert.That(levelOne.flowFrames[frame], Is.SameAs(levelOne.outletFlowFrames[frame]));
      }
      var dependencies = AssetDatabase.GetDependencies(FilterCatalogPath);
      Assert.That(dependencies, Does.Not.Contain($"{FilterFolder}/Filter_L1_Crank.png"));
      Assert.That(dependencies, Does.Not.Contain($"{FilterFolder}/FilterL1_Base.png"));
      Assert.That(dependencies, Does.Not.Contain($"{FilterFolder}/FilterL1_Bottle.png"));
      Assert.That(dependencies, Does.Not.Contain($"{FilterFolder}/FilterL1_BottleFill.png"));
      Assert.That(catalog.Levels[1].crankSprite, Is.Not.Null);
      Assert.That(catalog.Levels[2].brushSprite, Is.Not.Null);
      Assert.That(catalog.Levels[2].gaugeNeedleSprite, Is.Not.Null);
    }

    private static void AssertFilterSpritePath(Sprite sprite, string expectedFileName)
    {
      Assert.That(sprite, Is.Not.Null, expectedFileName);
      Assert.That(AssetDatabase.GetAssetPath(sprite),
        Is.EqualTo($"{FilterFolder}/{expectedFileName}"));
    }
  }
}
