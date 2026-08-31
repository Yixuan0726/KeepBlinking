using System;
using UnityEngine;

namespace KeepBlinking.CareStation
{
  public sealed class CareStationFilterArtCatalog : ScriptableObject
  {
    [Serializable]
    public sealed class LevelSprites
    {
      public int level;
      public Sprite baseSprite;
      public Sprite[] flowFrames = Array.Empty<Sprite>();
      public Sprite crankSprite;
      public Sprite brushSprite;
      public Sprite gaugeNeedleSprite;
      public Sprite badgeSprite;
      public Vector2 crankPivot;
      public Vector2 gaugePivot;

      // Optional authored layers used by the approved Level 1 FILTER. They are
      // intentionally appended so existing Level 2/3 catalog references keep
      // their serialized field layout and continue to use the legacy layers.
      public Sprite rawLiquidSprite;
      public Sprite rawParticlesSprite;
      public Sprite filterCartridgeSprite;
      public Sprite funnelAndPipeSprite;
      public Sprite bottleSprite;
      public Sprite bottleFillSprite;
      public float rawLiquidScalePivotY = 0.59f;
      public float bottleFillScalePivotY = 0.06f;

      // Re-authored Level 1 phone sprites. These are deliberately separate
      // from the legacy full-canvas layers above: the machine never owns a
      // baked bottle, and each liquid body/surface is independently masked.
      // Level 2/3 keep using the legacy fields without migration.
      public Sprite machineBaseSprite;
      public Sprite rawLiquidBodySprite;
      public Sprite rawLiquidSurfaceSprite;
      public Sprite filterBedSprite;
      public Sprite[] filterDripFrames = Array.Empty<Sprite>();
      public Sprite[] outletFlowFrames = Array.Empty<Sprite>();
      public Sprite bottleGlassSprite;
      public Sprite bottleLiquidBodySprite;
      public Sprite bottleLiquidSurfaceSprite;

      // Legacy catalog metadata retained for L2/L3 serialization compatibility.
      // Runtime FILTER artwork is now sized by its RectTransform and always
      // renders at unit transform scale; new entries should use Vector2.one.
      public Vector2 displayScale = Vector2.one;
      public Rect normalizedHitBounds = new Rect(0f, 0f, 1f, 1f);
    }

    [SerializeField] private LevelSprites[] _levels = Array.Empty<LevelSprites>();

    public LevelSprites[] Levels => _levels;
  }
}
