using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.Gameplay
{
  // Named entry point for the CARE card presentation. The inherited view owns
  // the formal Canvas, safe-area layout, touch hit regions, and install motion.
  internal sealed class CareUpgradeCardView : FirstLevelUpgradeView
  {
  }

  internal sealed class CareUpgradePreviewGraphic : MaskableGraphic
  {
    private CareUpgradeDefinition _definition;
    private float _phase;

    public FirstLevelModuleCategory Category => _definition != null ? _definition.Category : FirstLevelModuleCategory.Focus;
    public Color AccentColor => _definition != null ? _definition.AccentColor : KeepBlinkingTheme.AccentPrimary;

    public float Phase
    {
      get => _phase;
      set
      {
        var next = Mathf.Repeat(value, 1f);
        if (Mathf.Approximately(_phase, next)) return;
        _phase = next;
        SetVerticesDirty();
      }
    }

    public void Configure(CareUpgradeDefinition definition)
    {
      _definition = definition;
      SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
      vh.Clear();
      if (_definition == null) return;

      var center = rectTransform.rect.center;
      var pulse = 0.5f - 0.5f * Mathf.Cos(_phase * Mathf.PI * 2f);
      var warm = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.84f);
      var mint = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.9f);
      var gold = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentWarm, 0.92f);
      var muted = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextMuted, 0.55f);

      switch (_definition.Id)
      {
        case FirstLevelModuleId.WiderField:
          DrawField(vh, center, Mathf.Lerp(44f, 78f, pulse), Mathf.Lerp(27f, 44f, pulse), mint);
          break;
        case FirstLevelModuleId.MoreTargets:
          DrawTargets(vh, center, pulse < 0.48f ? 2 : 4, mint, warm);
          break;
        case FirstLevelModuleId.LookAwayHold:
          DrawField(vh, center, 66f, 36f, mint);
          UpgradeUiMeshUtility.AddArc(vh, center, 48f, 205f, 130f, 16, 4f, gold);
          UpgradeUiMeshUtility.AddCircle(vh, center + new Vector2(Mathf.Lerp(-18f, 54f, pulse), 0f), 5f, 12, warm);
          break;
        case FirstLevelModuleId.BlinkBloom:
          DrawEye(vh, center + new Vector2(0f, 8f), warm, mint, Mathf.Lerp(0.35f, 1f, pulse));
          DrawField(vh, center, Mathf.Lerp(48f, 74f, pulse), Mathf.Lerp(25f, 42f, pulse), mint);
          break;
        case FirstLevelModuleId.TearWave:
          DrawField(vh, center, 68f, 37f, muted);
          UpgradeUiMeshUtility.AddArc(vh, center, Mathf.Lerp(20f, 62f, pulse), 185f, 170f, 18, 4f, mint);
          break;
        case FirstLevelModuleId.QuietBlink:
        case FirstLevelModuleId.QuietReturn:
          DrawSpawnPause(vh, center, pulse, warm, gold);
          break;
        case FirstLevelModuleId.ExtraSamples:
          DrawSamples(vh, center, pulse < 0.48f ? 1 : 3, gold);
          break;
        case FirstLevelModuleId.ReturnBloom:
          DrawPhone(vh, center, warm, gold);
          DrawField(vh, center, Mathf.Lerp(45f, 76f, pulse), Mathf.Lerp(25f, 43f, pulse), mint);
          break;
        case FirstLevelModuleId.ShiftReward:
          DrawNearFar(vh, center, pulse, warm, gold);
          if (pulse > 0.58f) UpgradeUiMeshUtility.AddCircle(vh, center + new Vector2(0f, -55f), 10f, 18, gold);
          break;
        case FirstLevelModuleId.RestBloom:
          DrawEye(vh, center, warm, mint, 1f);
          DrawField(vh, center, Mathf.Lerp(42f, 82f, pulse), Mathf.Lerp(23f, 46f, pulse), mint);
          break;
        case FirstLevelModuleId.RestSample:
          DrawEye(vh, center + new Vector2(0f, 15f), warm, mint, 1f);
          if (pulse > 0.45f) UpgradeUiMeshUtility.AddCircle(vh, center + new Vector2(0f, -48f), 11f, 18, gold);
          break;
        case FirstLevelModuleId.DoublePulse:
          UpgradeUiMeshUtility.AddArc(vh, center, Mathf.Lerp(25f, 66f, pulse), 0f, 360f, 28, 4f, mint);
          DrawSamples(vh, center, pulse < 0.48f ? 1 : 2, gold);
          break;
        case FirstLevelModuleId.FieldPulse:
          UpgradeUiMeshUtility.AddArc(vh, center, Mathf.Lerp(20f, 78f, pulse), 0f, 360f, 30, 5f, mint);
          DrawField(vh, center, Mathf.Lerp(45f, 80f, pulse), Mathf.Lerp(25f, 45f, pulse), gold);
          break;
        case FirstLevelModuleId.FullRecovery:
          DrawField(vh, center, 72f, 40f, Color.Lerp(gold, mint, pulse));
          if (pulse < 0.55f)
          {
            UpgradeUiMeshUtility.AddLine(vh, center + new Vector2(-16f, 25f), center + new Vector2(-4f, 5f), 3f, muted);
            UpgradeUiMeshUtility.AddLine(vh, center + new Vector2(-4f, 5f), center + new Vector2(-13f, -18f), 3f, muted);
          }
          break;
        default:
          DrawField(vh, center, Mathf.Lerp(46f, 68f, pulse), Mathf.Lerp(26f, 38f, pulse), AccentColor);
          break;
      }
    }

    private static void DrawField(VertexHelper vh, Vector2 center, float radiusX, float radiusY, Color color)
    {
      var previous = center + new Vector2(radiusX, 0f);
      const int segments = 36;
      for (var i = 1; i <= segments; i++)
      {
        var angle = Mathf.PI * 2f * i / segments;
        var next = center + new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusY);
        UpgradeUiMeshUtility.AddLine(vh, previous, next, 4f, color);
        previous = next;
      }
    }

    private static void DrawTargets(VertexHelper vh, Vector2 center, int count, Color mint, Color warm)
    {
      var positions = count == 2
        ? new[] { new Vector2(-30f, 0f), new Vector2(30f, 0f) }
        : new[] { new Vector2(-32f, 23f), new Vector2(32f, 23f), new Vector2(-32f, -23f), new Vector2(32f, -23f) };
      for (var i = 0; i < positions.Length; i++)
      {
        UpgradeUiMeshUtility.AddCircle(vh, center + positions[i], 9f, 16, mint);
        UpgradeUiMeshUtility.AddArc(vh, center + positions[i], 15f, 0f, 360f, 18, 2.5f, warm);
      }
    }

    private static void DrawEye(VertexHelper vh, Vector2 center, Color warm, Color mint, float openness)
    {
      var height = Mathf.Lerp(3f, 25f, openness);
      UpgradeUiMeshUtility.AddParabolicArc(vh, center, 56f, height, true, 22, 4f, warm);
      UpgradeUiMeshUtility.AddParabolicArc(vh, center, 56f, height, false, 22, 4f, warm);
      UpgradeUiMeshUtility.AddCircle(vh, center, Mathf.Lerp(3f, 12f, openness), 18, mint);
    }

    private static void DrawSamples(VertexHelper vh, Vector2 center, int count, Color gold)
    {
      var spacing = 31f;
      var start = -(count - 1) * spacing * 0.5f;
      for (var i = 0; i < count; i++)
      {
        var point = center + new Vector2(start + i * spacing, 0f);
        UpgradeUiMeshUtility.AddCircle(vh, point, 11f, 18, gold);
        UpgradeUiMeshUtility.AddArc(vh, point, 16f, 20f, 300f, 16, 2f, KeepBlinkingTheme.WithAlpha(gold, 0.6f));
      }
    }

    private static void DrawSpawnPause(VertexHelper vh, Vector2 center, float phase, Color warm, Color gold)
    {
      for (var i = -1; i <= 1; i++)
      {
        UpgradeUiMeshUtility.AddCircle(vh, center + new Vector2(i * 35f, 0f), 9f, 14, KeepBlinkingTheme.WithAlpha(warm, Mathf.Lerp(0.8f, 0.2f, phase)));
      }
      UpgradeUiMeshUtility.AddLine(vh, center + new Vector2(-14f, -30f), center + new Vector2(-14f, 30f), 7f, gold);
      UpgradeUiMeshUtility.AddLine(vh, center + new Vector2(14f, -30f), center + new Vector2(14f, 30f), 7f, gold);
    }

    private static void DrawPhone(VertexHelper vh, Vector2 center, Color warm, Color gold)
    {
      UpgradeUiMeshUtility.AddRectOutline(vh, center, 24f, 42f, 4f, warm);
      UpgradeUiMeshUtility.AddLine(vh, center + new Vector2(-50f, 0f), center + new Vector2(-29f, 0f), 4f, gold);
      UpgradeUiMeshUtility.AddLine(vh, center + new Vector2(29f, 0f), center + new Vector2(50f, 0f), 4f, gold);
    }

    private static void DrawNearFar(VertexHelper vh, Vector2 center, float phase, Color warm, Color gold)
    {
      var scale = Mathf.Lerp(0.72f, 1.25f, phase);
      UpgradeUiMeshUtility.AddRectOutline(vh, center, 22f * scale, 40f * scale, 4f, warm);
      UpgradeUiMeshUtility.AddArc(vh, center, 62f, 175f, 190f, 20, 4f, gold);
    }
  }
}
