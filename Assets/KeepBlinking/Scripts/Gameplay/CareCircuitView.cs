using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.Gameplay
{
  internal sealed class CareCircuitView : MonoBehaviour
  {
    private CanvasGroup _group;
    private CareCircuitGraphic _graphic;

    private void EnsureCreated()
    {
      if (_group != null) return;
      var safe = FirstLevelUiFactory.CreateCanvas(transform, "Care Circuit Canvas", 1080, out _, out _group);
      var root = FirstLevelUiFactory.CreateObject("Care Circuit", safe).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(root, new Vector2(0.82f, 0.075f), new Vector2(0.82f, 0.075f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(150f, 150f));
      _graphic = root.gameObject.AddComponent<CareCircuitGraphic>();
      _graphic.raycastTarget = false;
      _group.blocksRaycasts = false;
      _group.interactable = false;
    }

    public void SetVisible(bool visible) { EnsureCreated(); _group.alpha = visible ? 1f : 0f; }
    public void SetSegments(bool move, bool focus, bool rest, bool release) { EnsureCreated(); _graphic.SetSegments(move, focus, rest, release); }
    public void Light(CareCircuitSegment segment) { EnsureCreated(); _graphic.Light(segment); }
    public void PlayPulse() { EnsureCreated(); _graphic.PlayPulse(); }
    public void PlayConversion(CareExperienceState state, float seconds) { EnsureCreated(); _graphic.PlayConversion(state, seconds); }
  }

  internal sealed class CareCircuitGraphic : MaskableGraphic
  {
    private readonly bool[] _lit = new bool[4];
    private readonly float[] _flashUntil = new float[4];
    private float _pulseAt = -99f;
    private float _conversionAt = -99f;
    private float _conversionSeconds = 0.5f;
    private CareExperienceState _conversionState;

    public void SetSegments(bool move, bool focus, bool rest, bool release)
    {
      _lit[0] = move; _lit[1] = focus; _lit[2] = rest; _lit[3] = release;
      SetVerticesDirty();
    }

    public void Light(CareCircuitSegment segment)
    {
      var index = (int)segment;
      _lit[index] = true;
      _flashUntil[index] = Time.unscaledTime + 0.85f;
      SetVerticesDirty();
    }

    public void PlayPulse() { _pulseAt = Time.unscaledTime; SetVerticesDirty(); }
    public void PlayConversion(CareExperienceState state, float seconds)
    {
      _conversionState = state; _conversionAt = Time.unscaledTime; _conversionSeconds = Mathf.Max(0.1f, seconds); SetVerticesDirty();
    }

    private void Update()
    {
      if (Time.unscaledTime - _pulseAt < 1f || Time.unscaledTime - _conversionAt < _conversionSeconds || HasFlash()) SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
      vh.Clear();
      var center = rectTransform.rect.center;
      var radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.29f;
      for (var i = 0; i < 4; i++)
      {
        var angle = (90f - i * 90f) * Mathf.Deg2Rad;
        var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        var point = center + direction * radius * 0.75f;
        var flash = Mathf.Clamp01((_flashUntil[i] - Time.unscaledTime) / 0.85f);
        var color = _lit[i] ? KeepBlinkingTheme.AccentPrimary : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.18f);
        color = Color.Lerp(color, KeepBlinkingTheme.TextPrimary, flash);
        UpgradeUiMeshUtility.AddCircle(vh, point, radius * (0.32f + 0.07f * flash), 20, color);
        UpgradeUiMeshUtility.AddLine(vh, center + direction * radius * 0.18f, point, 3f, KeepBlinkingTheme.WithAlpha(color, 0.65f));
        DrawIcon(vh, i, point, radius * 0.17f, KeepBlinkingTheme.BackgroundPrimary);
      }
      UpgradeUiMeshUtility.AddCircle(vh, center, radius * 0.16f, 18, KeepBlinkingTheme.AccentWarm);
      var pulse = Time.unscaledTime - _pulseAt;
      if (pulse >= 0f && pulse < 1f)
        UpgradeUiMeshUtility.AddArc(vh, center, Mathf.Lerp(radius * 0.4f, radius * 2.2f, pulse), 0f, 360f, 48, 4f, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 1f - pulse));
      var conversion = (Time.unscaledTime - _conversionAt) / _conversionSeconds;
      if (conversion >= 0f && conversion < 1f)
        UpgradeUiMeshUtility.AddArc(vh, center, Mathf.Lerp(radius * 0.25f, radius * 1.8f, conversion), 0f, 360f, 42, 3f, KeepBlinkingTheme.WithAlpha(CareExperienceStateInfo.Color(_conversionState), 1f - conversion));
    }

    private bool HasFlash() { for (var i = 0; i < 4; i++) if (_flashUntil[i] > Time.unscaledTime) return true; return false; }

    private static void DrawIcon(VertexHelper vh, int index, Vector2 center, float size, Color color)
    {
      if (index == 0)
      {
        UpgradeUiMeshUtility.AddLine(vh, center + Vector2.left * size, center + Vector2.right * size, 2.4f, color);
        UpgradeUiMeshUtility.AddCircle(vh, center + Vector2.right * size, size * 0.24f, 10, color);
      }
      else if (index == 1)
      {
        UpgradeUiMeshUtility.AddArc(vh, center, size, 0f, 360f, 18, 2f, color);
        UpgradeUiMeshUtility.AddCircle(vh, center, size * 0.28f, 10, color);
      }
      else if (index == 2)
      {
        UpgradeUiMeshUtility.AddParabolicArc(vh, center, size, size * 0.3f, true, 10, 2f, color);
        UpgradeUiMeshUtility.AddParabolicArc(vh, center, size, size * 0.08f, false, 10, 2f, color);
      }
      else
      {
        UpgradeUiMeshUtility.AddRectOutline(vh, center, size * 0.45f, size * 0.72f, 2f, color);
        UpgradeUiMeshUtility.AddLine(vh, center + Vector2.left * size, center + Vector2.left * size * 0.55f, 2f, color);
        UpgradeUiMeshUtility.AddLine(vh, center + Vector2.right * size * 0.55f, center + Vector2.right * size, 2f, color);
      }
    }
  }
}
