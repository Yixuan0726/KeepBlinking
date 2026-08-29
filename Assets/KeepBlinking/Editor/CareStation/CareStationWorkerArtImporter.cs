#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace KeepBlinking.EditorTools
{
  internal sealed class CareStationWorkerArtImporter : AssetPostprocessor
  {
    internal const string WorkerFolder = "Assets/KeepBlinking/Resources/CareStation/Worker";
    internal const string AtlasPath = "Assets/KeepBlinking/Art/CareStation/Worker/CareStationWorker.spriteatlas";
    private static bool _queued;

    [InitializeOnLoadMethod]
    private static void EnsureAtlasAfterDomainReload()
    {
      // The PNGs and this importer can arrive in the same checkout. In that
      // case texture import may finish before the new editor assembly reloads,
      // so also guarantee the dedicated atlas from the first safe delay call.
      QueueAtlasRefresh();
    }

    private void OnPreprocessTexture()
    {
      if (!assetPath.StartsWith(WorkerFolder + "/", StringComparison.OrdinalIgnoreCase) ||
          !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return;
      var importer = (TextureImporter)assetImporter;
      importer.textureType = TextureImporterType.Sprite;
      importer.spriteImportMode = SpriteImportMode.Single;
      importer.spritePixelsPerUnit = 100f;
      importer.spritePivot = new Vector2(0.5f, 0f);
      importer.alphaSource = TextureImporterAlphaSource.FromInput;
      importer.alphaIsTransparency = true;
      importer.sRGBTexture = true;
      importer.mipmapEnabled = false;
      importer.wrapMode = TextureWrapMode.Clamp;
      importer.filterMode = FilterMode.Bilinear;
      importer.textureCompression = TextureImporterCompression.Uncompressed;
      importer.crunchedCompression = false;
      importer.maxTextureSize = 2048;
      importer.npotScale = TextureImporterNPOTScale.None;

      var ios = importer.GetPlatformTextureSettings("iPhone");
      ios.overridden = true;
      ios.maxTextureSize = 2048;
      ios.format = TextureImporterFormat.RGBA32;
      ios.textureCompression = TextureImporterCompression.Uncompressed;
      ios.crunchedCompression = false;
      importer.SetPlatformTextureSettings(ios);

      var settings = new TextureImporterSettings();
      importer.ReadTextureSettings(settings);
      settings.spriteAlignment = (int)SpriteAlignment.Custom;
      settings.spritePivot = new Vector2(0.5f, 0f);
      settings.spriteExtrude = 4;
      settings.spriteMeshType = SpriteMeshType.FullRect;
      importer.SetTextureSettings(settings);
    }

    private static void OnPostprocessAllAssets(
      string[] importedAssets,
      string[] deletedAssets,
      string[] movedAssets,
      string[] movedFromAssetPaths)
    {
      if (!importedAssets.Any(path => path.StartsWith(WorkerFolder + "/", StringComparison.OrdinalIgnoreCase)))
        return;
      QueueAtlasRefresh();
    }

    [MenuItem("KeepBlinking/Care Station/Rebuild Worker Sprite Atlas")]
    private static void RebuildAtlasFromMenu()
    {
      BuildAtlas();
    }

    private static void QueueAtlasRefresh()
    {
      if (_queued) return;
      _queued = true;
      EditorApplication.delayCall += () =>
      {
        _queued = false;
        if (!EditorApplication.isCompiling && !EditorApplication.isUpdating) BuildAtlas();
      };
    }

    private static void BuildAtlas()
    {
      if (!AssetDatabase.IsValidFolder(WorkerFolder)) return;
      EnsureFolder("Assets/KeepBlinking/Art");
      EnsureFolder("Assets/KeepBlinking/Art/CareStation");
      EnsureFolder("Assets/KeepBlinking/Art/CareStation/Worker");
      var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);
      if (atlas == null)
      {
        atlas = new SpriteAtlas();
        AssetDatabase.CreateAsset(atlas, AtlasPath);
      }

      var existing = SpriteAtlasExtensions.GetPackables(atlas);
      if (existing != null && existing.Length > 0) SpriteAtlasExtensions.Remove(atlas, existing);
      var folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(WorkerFolder);
      if (folder != null) SpriteAtlasExtensions.Add(atlas, new UnityEngine.Object[] { folder });

      atlas.SetPackingSettings(new SpriteAtlasPackingSettings
      {
        blockOffset = 1,
        enableRotation = false,
        enableTightPacking = false,
        padding = 4,
      });
      atlas.SetTextureSettings(new SpriteAtlasTextureSettings
      {
        readable = false,
        generateMipMaps = false,
        sRGB = true,
        filterMode = FilterMode.Bilinear,
      });
      atlas.SetPlatformSettings(new TextureImporterPlatformSettings
      {
        name = "DefaultTexturePlatform",
        overridden = true,
        maxTextureSize = 2048,
        format = TextureImporterFormat.RGBA32,
        textureCompression = TextureImporterCompression.Uncompressed,
        crunchedCompression = false,
      });
      atlas.SetPlatformSettings(new TextureImporterPlatformSettings
      {
        name = "iPhone",
        overridden = true,
        maxTextureSize = 2048,
        format = TextureImporterFormat.RGBA32,
        textureCompression = TextureImporterCompression.Uncompressed,
        crunchedCompression = false,
      });
      EditorUtility.SetDirty(atlas);
      AssetDatabase.SaveAssets();
    }

    private static void EnsureFolder(string path)
    {
      if (AssetDatabase.IsValidFolder(path)) return;
      var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
      if (string.IsNullOrEmpty(parent)) return;
      EnsureFolder(parent);
      AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
  }
}
#endif
