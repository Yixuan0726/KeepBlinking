using System.IO;
using System.Linq;
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
  }
}
