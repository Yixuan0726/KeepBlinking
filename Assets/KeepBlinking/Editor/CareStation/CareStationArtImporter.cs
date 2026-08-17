#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using KeepBlinking.CareStation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.EditorTools
{
  internal sealed class CareStationArtImporter : AssetPostprocessor
  {
    private const string CrewPath = "Assets/KeepBlinking/Art/CareStation/Crew/eye-care-crew-cart-grip.png";
    private const string CartPath = "Assets/KeepBlinking/Art/CareStation/Carts/care-cart-upgrades-double-decker.png";
    private const string BottlePath = "Assets/KeepBlinking/Art/CareStation/Bottles/care-sample-bottles-clear-empty-full-gold.png";
    private const string PrefabFolder = "Assets/KeepBlinking/Resources/CareStation/Crew";
    private static bool _queued;
    private static bool _processing;

    [MenuItem("KeepBlinking/Care Station/Rebuild Art Assets")]
    private static void RebuildFromMenu()
    {
      EnsureArtAssets(true);
    }

    private static void QueueRebuild()
    {
      if (_queued) return;
      _queued = true;
      EditorApplication.delayCall += () =>
      {
        _queued = false;
        EnsureArtAssets(false);
      };
    }

    private static void EnsureArtAssets(bool force)
    {
      if (_processing || EditorApplication.isCompiling || EditorApplication.isUpdating) { QueueRebuild(); return; }
      if (!System.IO.File.Exists(CrewPath) || !System.IO.File.Exists(CartPath) || !System.IO.File.Exists(BottlePath)) return;
      _processing = true;
      try
      {
        var changed = false;
        changed |= Configure(CrewPath, BuildCrewRects(), force);
        changed |= Configure(CartPath, BuildCartRects(), force);
        changed |= Configure(BottlePath, BuildBottleRects(), force);
        if (changed)
        {
          QueueRebuild();
          return;
        }
        BuildPrefabs();
      }
      finally
      {
        _processing = false;
      }
    }

    private static bool Configure(string path, SpriteMetaData[] requestedRects, bool force)
    {
      AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
      var importer = AssetImporter.GetAtPath(path) as TextureImporter;
      if (importer == null) return false;

      var basicChanged = importer.textureType != TextureImporterType.Sprite ||
                         importer.spriteImportMode != SpriteImportMode.Multiple ||
                         importer.mipmapEnabled || !importer.alphaIsTransparency ||
                         importer.textureCompression != TextureImporterCompression.Uncompressed ||
                         importer.crunchedCompression || importer.filterMode != FilterMode.Bilinear;
      importer.textureType = TextureImporterType.Sprite;
      importer.spriteImportMode = SpriteImportMode.Multiple;
      importer.alphaIsTransparency = true;
      importer.mipmapEnabled = false;
      importer.textureCompression = TextureImporterCompression.Uncompressed;
      importer.crunchedCompression = false;
      importer.filterMode = FilterMode.Bilinear;
      importer.wrapMode = TextureWrapMode.Clamp;
      importer.npotScale = TextureImporterNPOTScale.None;
      importer.spritePixelsPerUnit = 100f;

      #pragma warning disable 618
      var current = importer.spritesheet;
      var rectsMatch = !force && SpriteRectsMatch(current, requestedRects);
      if (!rectsMatch)
        importer.spritesheet = requestedRects;
      #pragma warning restore 618

      if (!basicChanged && rectsMatch) return false;
      importer.SaveAndReimport();
      return true;
    }

    private static bool SpriteRectsMatch(SpriteMetaData[] current, SpriteMetaData[] requested)
    {
      if (current == null || current.Length != requested.Length) return false;
      var byName = current.ToDictionary(rect => rect.name);
      foreach (var rect in requested)
      {
        if (!byName.TryGetValue(rect.name, out var existing)) return false;
        if (existing.rect != rect.rect || existing.alignment != rect.alignment || Vector2.Distance(existing.pivot, rect.pivot) > 0.0001f) return false;
      }
      return true;
    }

    private static SpriteMetaData[] BuildCrewRects()
    {
      const float cell = 362f;
      var roles = new[] { "DustKeeper", "DrySpotMender", "CareCourier", "RestGuide" };
      var states = new[] { "Idle", "Walk", "Work" };
      // Source rows are top-to-bottom Idle, Walk, Work. Unity texture rects use a bottom-left origin.
      var result = new List<SpriteMetaData>(12);
      for (var row = 0; row < 3; row++)
      for (var column = 0; column < 4; column++)
      {
        result.Add(NewRect(
          roles[column] + "_" + states[row],
          new Rect(column * cell, (2 - row) * cell, cell, cell),
          new Vector2(0.5f, 0.5f)));
      }
      return result.ToArray();
    }

    private static SpriteMetaData[] BuildCartRects()
    {
      return new[]
      {
        NewRect("SmallBasket", new Rect(75f, 231f, 376f, 264f), new Vector2(1f, 0.08f)),
        NewRect("DeepCart", new Rect(512f, 212f, 517f, 405f), new Vector2(1f, 0.05f)),
        NewRect("CareTower", new Rect(1104f, 182f, 566f, 602f), new Vector2(1f, 0.03f)),
      };
    }

    private static SpriteMetaData[] BuildBottleRects()
    {
      return new[]
      {
        NewRect("EmptyBottle", new Rect(256f, 190f, 318f, 478f), new Vector2(0.5f, 0.02f)),
        NewRect("MintBottle", new Rect(758f, 191f, 319f, 479f), new Vector2(0.5f, 0.02f)),
        NewRect("GoldBottle", new Rect(1251f, 182f, 346f, 480f), new Vector2(0.5f, 0.02f)),
      };
    }

    private static SpriteMetaData NewRect(string name, Rect rect, Vector2 pivot)
    {
      return new SpriteMetaData
      {
        name = name,
        rect = rect,
        alignment = (int)SpriteAlignment.Custom,
        pivot = pivot,
      };
    }

    private static void BuildPrefabs()
    {
      EnsureFolder("Assets/KeepBlinking/Resources");
      EnsureFolder("Assets/KeepBlinking/Resources/CareStation");
      EnsureFolder(PrefabFolder);
      var crewSprites = LoadSprites(CrewPath);
      var cartSprites = LoadSprites(CartPath);
      var bottleSprites = LoadSprites(BottlePath);
      if (crewSprites.Count != 12 || cartSprites.Count != 3 || bottleSprites.Count != 3) { QueueRebuild(); return; }

      BuildPrefab(CareCrewRole.DustKeeper, "DustKeeper", crewSprites, cartSprites, bottleSprites);
      BuildPrefab(CareCrewRole.DrySpotMender, "DrySpotMender", crewSprites, cartSprites, bottleSprites);
      BuildPrefab(CareCrewRole.CareCourier, "CareCourier", crewSprites, cartSprites, bottleSprites);
      BuildPrefab(CareCrewRole.RestGuide, "RestGuide", crewSprites, cartSprites, bottleSprites);
      AssetDatabase.SaveAssets();
      Debug.Log("Care Station art imported: 12 crew sprites, 3 carts, 3 bottles, and 4 preview prefabs are ready.");
    }

    private static Dictionary<string, Sprite> LoadSprites(string path)
    {
      return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToDictionary(sprite => sprite.name);
    }

    private static void BuildPrefab(
      CareCrewRole role,
      string spritePrefix,
      IReadOnlyDictionary<string, Sprite> crewSprites,
      IReadOnlyDictionary<string, Sprite> cartSprites,
      IReadOnlyDictionary<string, Sprite> bottleSprites)
    {
      var root = CreateUiObject("CareCrewRoot");
      root.sizeDelta = new Vector2(500f, 420f);

      var ground = CreateAnchor("GroundAnchor", root, new Vector2(0f, -125f));
      var hand = CreateAnchor("HandAnchor", root, role == CareCrewRole.CareCourier ? new Vector2(-42f, -65f) : new Vector2(-80f, 15f));
      var feedback = CreateAnchor("FeedbackRoot", root, new Vector2(0f, -118f));
      var ripple = AddImage("Mint Ripple", feedback);
      ripple.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
      ripple.rectTransform.sizeDelta = new Vector2(190f, 52f);
      ripple.color = Color.clear;

      var cartRoot = CreateAnchor("CartRoot", root, hand.anchoredPosition);
      var cartRenderer = AddImage("CartRenderer", cartRoot);
      cartRenderer.rectTransform.pivot = new Vector2(1f, 0.45f);
      cartRenderer.rectTransform.anchoredPosition = Vector2.zero;
      var cartAnchor = CreateAnchor("CartAnchor", cartRoot, Vector2.zero);
      cartAnchor.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
      var lower = CreateAnchor("LowerSampleAnchors", cartRoot, Vector2.zero);
      var upper = CreateAnchor("UpperSampleAnchors", cartRoot, Vector2.zero);
      var lowerImages = CreateBottleSlots(lower, 4, "LowerSampleAnchor");
      var upperImages = CreateBottleSlots(upper, 4, "UpperSampleAnchor");

      var character = AddImage("CharacterRenderer", root);
      character.rectTransform.sizeDelta = new Vector2(300f, 300f);
      character.rectTransform.anchoredPosition = role == CareCrewRole.CareCourier ? new Vector2(70f, 22f) : new Vector2(0f, 22f);
      character.preserveAspect = true;

      var view = root.gameObject.AddComponent<CareCrewArtView>();
      view.EditorConfigure(
        role,
        character,
        crewSprites[spritePrefix + "_Idle"],
        crewSprites[spritePrefix + "_Walk"],
        crewSprites[spritePrefix + "_Work"],
        ground,
        hand,
        feedback,
        ripple,
        cartRoot,
        cartRenderer,
        cartSprites["SmallBasket"],
        cartSprites["DeepCart"],
        cartSprites["CareTower"],
        lower,
        upper,
        lowerImages,
        upperImages,
        bottleSprites["EmptyBottle"],
        bottleSprites["MintBottle"],
        bottleSprites["GoldBottle"]);

      cartRoot.gameObject.SetActive(role == CareCrewRole.CareCourier);
      var path = PrefabFolder + "/" + spritePrefix + ".prefab";
      PrefabUtility.SaveAsPrefabAsset(root.gameObject, path);
      UnityEngine.Object.DestroyImmediate(root.gameObject);
    }

    private static Image[] CreateBottleSlots(RectTransform parent, int count, string prefix)
    {
      var result = new Image[count];
      for (var i = 0; i < count; i++)
      {
        var anchor = CreateAnchor(prefix + (i + 1), parent, Vector2.zero);
        var bottle = AddImage("BottleRenderer", anchor);
        bottle.rectTransform.sizeDelta = new Vector2(31f, 48f);
        bottle.preserveAspect = true;
        result[i] = bottle;
      }
      return result;
    }

    private static RectTransform CreateUiObject(string name)
    {
      return new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).GetComponent<RectTransform>();
    }

    private static RectTransform CreateAnchor(string name, Transform parent, Vector2 position)
    {
      var rect = CreateUiObject(name);
      rect.SetParent(parent, false);
      rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
      rect.anchoredPosition = position;
      rect.sizeDelta = Vector2.zero;
      return rect;
    }

    private static Image AddImage(string name, Transform parent)
    {
      var rect = CreateUiObject(name);
      rect.SetParent(parent, false);
      rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
      rect.anchoredPosition = Vector2.zero;
      var image = rect.gameObject.AddComponent<Image>();
      image.raycastTarget = false;
      return image;
    }

    private static void EnsureFolder(string path)
    {
      if (AssetDatabase.IsValidFolder(path)) return;
      var slash = path.LastIndexOf('/');
      AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
  }
}
#endif
