using System.IO;
using System.Linq;
using KeepBlinking.CareStation;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace KeepBlinking.Tests
{
  public sealed class CareStationWorkerArtTests
  {
    private const string WorkerAssetFolder = "Assets/KeepBlinking/Resources/CareStation/Worker";

    [Test]
    public void FormalWorkerCatalogLoadsAllApprovedTransparentLayers()
    {
      var catalog = CareStationWorkerArtCatalog.LoadFromResources();
      Assert.That(catalog.IsComplete, Is.True,
        "The formal Worker must never fall back to the retired graybox because a required layer is missing.");
      Assert.That(Directory.GetFiles(WorkerAssetFolder, "*.png", SearchOption.TopDirectoryOnly),
        Has.Length.EqualTo(25));
    }

    [Test]
    public void FormalWorkerUsesItsOwnUncompressedSpriteAtlas()
    {
      const string atlasPath = "Assets/KeepBlinking/Art/CareStation/Worker/CareStationWorker.spriteatlas";
      var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);
      Assert.That(atlas, Is.Not.Null,
        "The Worker importer must build the dedicated atlas before formal art tests run.");
      var platform = atlas.GetPlatformSettings(BuildPipeline.GetBuildTargetName(BuildTarget.iOS));
      Assert.That(platform.overridden, Is.True);
      Assert.That(platform.maxTextureSize, Is.EqualTo(2048));
      Assert.That(platform.format, Is.EqualTo(TextureImporterFormat.RGBA32));
      Assert.That(platform.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed));
    }

    [Test]
    public void FormalWorkerSpritesUseMobileSafeImportSettings()
    {
      var paths = Directory.GetFiles(WorkerAssetFolder, "*.png", SearchOption.TopDirectoryOnly)
        .Select(path => path.Replace('\\', '/'))
        .ToArray();
      Assert.That(paths, Has.Length.EqualTo(25));
      foreach (var path in paths)
      {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        Assert.That(importer, Is.Not.Null, path);
        Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite), path);
        Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single), path);
        Assert.That(importer.spritePixelsPerUnit, Is.EqualTo(100f), path);
        Assert.That(importer.alphaSource, Is.EqualTo(TextureImporterAlphaSource.FromInput), path);
        Assert.That(importer.alphaIsTransparency, Is.True, path);
        Assert.That(importer.mipmapEnabled, Is.False, path);
        Assert.That(importer.wrapMode, Is.EqualTo(TextureWrapMode.Clamp), path);
        Assert.That(importer.filterMode, Is.EqualTo(FilterMode.Bilinear), path);
        Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), path);
        var ios = importer.GetPlatformTextureSettings(BuildPipeline.GetBuildTargetName(BuildTarget.iOS));
        Assert.That(ios.overridden, Is.True, path);
        Assert.That(ios.maxTextureSize, Is.EqualTo(2048), path);
        Assert.That(ios.format, Is.EqualTo(TextureImporterFormat.RGBA32), path);
      }
    }

    [Test]
    public void FormalWorkerPngsHaveTransparentExtrudedEdgesAndNoChromaResidue()
    {
      foreach (var path in Directory.GetFiles(WorkerAssetFolder, "*.png", SearchOption.TopDirectoryOnly))
      {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
          Assert.That(ImageConversion.LoadImage(texture, File.ReadAllBytes(path), false), Is.True, path);
          var pixels = texture.GetPixels32();
          for (var x = 0; x < texture.width; x++)
          {
            Assert.That(pixels[x].a, Is.Zero, path + " bottom edge");
            Assert.That(pixels[(texture.height - 1) * texture.width + x].a, Is.Zero, path + " top edge");
          }
          for (var y = 0; y < texture.height; y++)
          {
            Assert.That(pixels[y * texture.width].a, Is.Zero, path + " left edge");
            Assert.That(pixels[y * texture.width + texture.width - 1].a, Is.Zero, path + " right edge");
          }
          Assert.That(pixels.Any(pixel => pixel.a > 8 && pixel.r >= 170 && pixel.b >= 165 && pixel.g <= 70),
            Is.False, path + " contains chroma-key residue");
        }
        finally
        {
          Object.DestroyImmediate(texture);
        }
      }
    }

    [TestCase(1, 1)]
    [TestCase(2, 2)]
    [TestCase(3, 3)]
    public void WorkerLevelsExposeOneTwoAndThreeVisibleCharacters(int workerLevel, int expected)
    {
      Assert.That(CareStationWorkerVisualRules.VisibleCountForLevel(workerLevel), Is.EqualTo(expected));
    }

    [TestCase(1, CareStationWorkerExpression.Angry)]
    [TestCase(2, CareStationWorkerExpression.Focused)]
    [TestCase(3, CareStationWorkerExpression.Happy)]
    public void WorkerLevelsUseApprovedDefaultExpressions(
      int workerLevel,
      CareStationWorkerExpression expected)
    {
      Assert.That(CareStationWorkerVisualRules.ExpressionForLevel(workerLevel), Is.EqualTo(expected));
    }

    [TestCase(1f, 0f, CareStationWorkerFacing.Right)]
    [TestCase(1f, 1f, CareStationWorkerFacing.BackRight)]
    [TestCase(0f, 1f, CareStationWorkerFacing.Back)]
    [TestCase(-1f, 1f, CareStationWorkerFacing.BackLeft)]
    [TestCase(-1f, 0f, CareStationWorkerFacing.Left)]
    [TestCase(-1f, -1f, CareStationWorkerFacing.FrontLeft)]
    [TestCase(0f, -1f, CareStationWorkerFacing.Front)]
    [TestCase(1f, -1f, CareStationWorkerFacing.FrontRight)]
    public void MovementChoosesNearestOfEightDirections(
      float x,
      float y,
      CareStationWorkerFacing expected)
    {
      Assert.That(CareStationWorkerVisualRules.FacingForMovement(
        new Vector2(x, y), CareStationWorkerFacing.Front), Is.EqualTo(expected));
    }

    [TestCase(CareStationWorkerFacing.Front)]
    [TestCase(CareStationWorkerFacing.BackLeft)]
    [TestCase(CareStationWorkerFacing.Right)]
    public void StoppingRetainsLastValidFacing(CareStationWorkerFacing lastFacing)
    {
      Assert.That(CareStationWorkerVisualRules.FacingForMovement(Vector2.zero, lastFacing), Is.EqualTo(lastFacing));
    }

    [Test]
    public void BackFacingsHideFaceWhileSideFacingsRemainRecognizable()
    {
      Assert.That(CareStationWorkerVisualRules.FaceVisible(CareStationWorkerFacing.Back), Is.False);
      Assert.That(CareStationWorkerVisualRules.FaceVisible(CareStationWorkerFacing.BackLeft), Is.False);
      Assert.That(CareStationWorkerVisualRules.FaceVisible(CareStationWorkerFacing.BackRight), Is.False);
      Assert.That(CareStationWorkerVisualRules.FaceVisible(CareStationWorkerFacing.Left), Is.True);
      Assert.That(CareStationWorkerVisualRules.FaceVisible(CareStationWorkerFacing.Right), Is.True);
      Assert.That(CareStationWorkerVisualRules.IsSide(CareStationWorkerFacing.Left), Is.True);
      Assert.That(CareStationWorkerVisualRules.IsSide(CareStationWorkerFacing.Right), Is.True);
    }

    [Test]
    public void VisualRulesDoNotMutateStationEconomy()
    {
      var save = new CareStationSaveData
      {
        workerLevel = 3,
        crewCount = 4,
        storedFullBottles = 23,
        storedGoldBottles = 2,
        pendingIncidentXP = 7,
      };

      Assert.That(CareStationWorkerVisualRules.VisibleCountForLevel(save.workerLevel), Is.EqualTo(3));
      Assert.That(CareStationWorkerVisualRules.ExpressionForLevel(save.workerLevel), Is.EqualTo(CareStationWorkerExpression.Happy));
      Assert.That(save.crewCount, Is.EqualTo(4));
      Assert.That(save.storedFullBottles, Is.EqualTo(23));
      Assert.That(save.storedGoldBottles, Is.EqualTo(2));
      Assert.That(save.pendingIncidentXP, Is.EqualTo(7));
    }
  }
}
