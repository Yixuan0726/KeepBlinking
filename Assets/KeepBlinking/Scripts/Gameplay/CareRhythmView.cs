using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.Gameplay
{
  internal sealed class CareRhythmView : MonoBehaviour
  {
    private CanvasGroup _group;
    private CareRhythmGraphic _graphic;

    public void SetPetals(bool focus, bool blink, bool distance, bool rest)
    {
      EnsureCreated();
      _graphic.SetPetals(focus, blink, distance, rest);
    }

    public void FlashPetal(CareRhythmPetal petal)
    {
      EnsureCreated();
      _graphic.FlashPetal(petal);
    }

    public void FlashCategory(FirstLevelModuleCategory category)
    {
      EnsureCreated();
      switch (category)
      {
        case FirstLevelModuleCategory.Focus: _graphic.FlashPetal(CareRhythmPetal.Focus); break;
        case FirstLevelModuleCategory.Blink: _graphic.FlashPetal(CareRhythmPetal.Blink); break;
        case FirstLevelModuleCategory.Distance: _graphic.FlashPetal(CareRhythmPetal.Distance); break;
        case FirstLevelModuleCategory.Rest: _graphic.FlashPetal(CareRhythmPetal.Rest); break;
        case FirstLevelModuleCategory.Rhythm: _graphic.PlayPulse(); break;
      }
    }

    public void PlayCarePulse()
    {
      EnsureCreated();
      _graphic.PlayPulse();
    }

    public void SetVisible(bool visible)
    {
      EnsureCreated();
      _group.alpha = visible ? 1f : 0f;
    }

    private void EnsureCreated()
    {
      if (_group != null) return;
      var safe = FirstLevelUiFactory.CreateCanvas(transform, "Care Rhythm Canvas", 1080, out _, out _group);
      var coreObject = FirstLevelUiFactory.CreateObject("Care Rhythm Core", safe);
      var coreRect = coreObject.GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(coreRect, new Vector2(0.82f, 0.075f), new Vector2(0.82f, 0.075f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150f, 150f));
      _graphic = coreObject.AddComponent<CareRhythmGraphic>();
      _graphic.raycastTarget = false;
      _group.alpha = 1f;
      _group.blocksRaycasts = false;
      _group.interactable = false;
    }
  }

  internal sealed class CareRhythmGraphic : MaskableGraphic
  {
    private readonly bool[] _lit = new bool[4];
    private readonly float[] _flashUntil = new float[4];
    private float _pulseStartedAt = -999f;

    public void SetPetals(bool focus, bool blink, bool distance, bool rest)
    {
      _lit[0] = focus;
      _lit[1] = blink;
      _lit[2] = distance;
      _lit[3] = rest;
      SetVerticesDirty();
    }

    public void FlashPetal(CareRhythmPetal petal)
    {
      _flashUntil[(int)petal] = Time.unscaledTime + 0.9f;
      SetVerticesDirty();
    }

    public void PlayPulse()
    {
      _pulseStartedAt = Time.unscaledTime;
      SetVerticesDirty();
    }

    private void Update()
    {
      if (Time.unscaledTime - _pulseStartedAt < 1f || HasActiveFlash()) SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
      vh.Clear();
      var center = rectTransform.rect.center;
      var radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.29f;
      for (var i = 0; i < 4; i++)
      {
        var angle = 90f - i * 90f;
        var direction = Direction(angle);
        var petalCenter = center + direction * radius * 0.72f;
        var flash = Mathf.Clamp01((_flashUntil[i] - Time.unscaledTime) / 0.9f);
        var color = _lit[i]
          ? KeepBlinkingTheme.AccentPrimary
          : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.18f);
        color = Color.Lerp(color, KeepBlinkingTheme.TextPrimary, flash);
        UpgradeUiMeshUtility.AddCircle(vh, petalCenter, radius * (0.34f + flash * 0.08f), 24, color);
        UpgradeUiMeshUtility.AddLine(vh, center + direction * radius * 0.18f, petalCenter, 3f, KeepBlinkingTheme.WithAlpha(color, 0.72f));
        DrawPetalIcon(vh, i, petalCenter, radius * 0.18f, _lit[i] || flash > 0f
          ? KeepBlinkingTheme.BackgroundPrimary
          : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.48f));
      }
      UpgradeUiMeshUtility.AddCircle(vh, center, radius * 0.18f, 20, KeepBlinkingTheme.AccentWarm);
      var pulseElapsed = Time.unscaledTime - _pulseStartedAt;
      if (pulseElapsed >= 0f && pulseElapsed <= 1f)
      {
        var t = Mathf.Clamp01(pulseElapsed);
        UpgradeUiMeshUtility.AddArc(vh, center, Mathf.Lerp(radius * 0.4f, radius * 2.1f, t), 0f, 360f, 64, 4f, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 1f - t));
      }
    }

    private bool HasActiveFlash()
    {
      for (var i = 0; i < _flashUntil.Length; i++) if (_flashUntil[i] > Time.unscaledTime) return true;
      return false;
    }

    private static void DrawPetalIcon(VertexHelper vh, int index, Vector2 center, float size, Color color)
    {
      switch (index)
      {
        case 0: // FOCUS: centered field.
          UpgradeUiMeshUtility.AddArc(vh, center, size, 0f, 360f, 18, 2f, color);
          UpgradeUiMeshUtility.AddCircle(vh, center, size * 0.28f, 12, color);
          break;
        case 1: // BLINK: gently closed eyelids.
          UpgradeUiMeshUtility.AddParabolicArc(vh, center, size, size * 0.42f, true, 10, 2.2f, color);
          UpgradeUiMeshUtility.AddParabolicArc(vh, center, size, size * 0.12f, false, 10, 2.2f, color);
          break;
        case 2: // DISTANCE: phone silhouette.
          UpgradeUiMeshUtility.AddRectOutline(vh, center, size * 0.52f, size * 0.85f, 2.2f, color);
          UpgradeUiMeshUtility.AddLine(vh, center + Vector2.left * size * 0.95f, center + Vector2.left * size * 0.63f, 2f, color);
          UpgradeUiMeshUtility.AddLine(vh, center + Vector2.right * size * 0.63f, center + Vector2.right * size * 0.95f, 2f, color);
          break;
        default: // REST: slow circular path.
          UpgradeUiMeshUtility.AddArc(vh, center, size, 25f, 285f, 16, 2.2f, color);
          UpgradeUiMeshUtility.AddCircle(vh, center + Direction(25f) * size, size * 0.22f, 10, color);
          break;
      }
    }

    private static Vector2 Direction(float degrees)
    {
      var radians = degrees * Mathf.Deg2Rad;
      return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    }
  }
}
