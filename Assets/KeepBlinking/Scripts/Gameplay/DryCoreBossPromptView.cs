using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.Gameplay
{
  public enum DryCoreBossPrompt
  {
    None,
    SoftBlink,
    CloseEyes,
    WaitForTone,
    Open,
    PushAway,
    Complete,
  }

  internal sealed class DryCoreBossPromptView : MonoBehaviour
  {
    private CanvasGroup _group;
    private TextMeshProUGUI _label;
    private DryCorePromptIconGraphic _icon;
    private float _targetAlpha;

    internal static DryCoreBossPromptView Create(RectTransform safeRoot)
    {
      var root = FirstLevelUiFactory.CreateObject("Boss Action Prompt", safeRoot);
      var rect = root.GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(
        rect,
        new Vector2(0.5f, 0.28f), new Vector2(0.5f, 0.28f), new Vector2(0.5f, 0.5f),
        Vector2.zero, new Vector2(700f, 128f));
      var group = root.AddComponent<CanvasGroup>();
      group.alpha = 0f;
      var background = root.AddComponent<Image>();
      background.sprite = FirstLevelUiFactory.RoundedSprite;
      background.type = Image.Type.Sliced;
      background.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceOverlay, 0.93f);
      background.raycastTarget = false;

      var accent = FirstLevelUiFactory.CreateImage("Accent", root.transform, KeepBlinkingTheme.AccentPrimary, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(accent.rectTransform, new Vector2(0f, 0.15f), new Vector2(0f, 0.85f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(6f, 0f));

      var iconObject = FirstLevelUiFactory.CreateObject("Action Icon", root.transform);
      var iconRect = iconObject.GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(iconRect, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(84f, 0f), new Vector2(80f, 80f));
      var icon = iconObject.AddComponent<DryCorePromptIconGraphic>();
      icon.raycastTarget = false;

      var label = FirstLevelUiFactory.CreateText(
        "Action Label", root.transform, string.Empty, 30f, FontStyles.Bold,
        TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary, true);
      FirstLevelUiFactory.SetRect(label.rectTransform, new Vector2(0f, 0f), Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(44f, 0f), new Vector2(-158f, -28f));
      label.enableAutoSizing = true;
      label.fontSizeMin = 22f;
      label.fontSizeMax = 30f;
      label.maxVisibleLines = 2;

      var view = root.AddComponent<DryCoreBossPromptView>();
      view._group = group;
      view._label = label;
      view._icon = icon;
      view.SetPrompt(DryCoreBossPrompt.None);
      return view;
    }

    internal void SetPrompt(DryCoreBossPrompt prompt)
    {
      if (_label == null || _icon == null)
      {
        return;
      }

      _icon.Prompt = prompt;
      _targetAlpha = prompt == DryCoreBossPrompt.None ? 0f : 1f;
      switch (prompt)
      {
        case DryCoreBossPrompt.SoftBlink:
          SetCopy("HOLD CENTER", KeepBlinkingTheme.AccentPrimary);
          break;
        case DryCoreBossPrompt.CloseEyes:
          SetCopy("CLOSE EYES", new Color32(0x91, 0xB8, 0xD0, 0xFF));
          break;
        case DryCoreBossPrompt.WaitForTone:
          SetCopy("WAIT FOR TONE", new Color32(0x91, 0xB8, 0xD0, 0xFF));
          break;
        case DryCoreBossPrompt.Open:
          SetCopy("OPEN", KeepBlinkingTheme.TextPrimary);
          break;
        case DryCoreBossPrompt.PushAway:
          SetCopy("PUSH AWAY", KeepBlinkingTheme.AccentWarm);
          break;
        case DryCoreBossPrompt.Complete:
          SetCopy("OBSERVATION COMPLETE", KeepBlinkingTheme.AccentPrimary);
          break;
        default:
          _label.text = string.Empty;
          break;
      }
    }

    private void SetCopy(string copy, Color accent)
    {
      _label.text = copy;
      _label.color = KeepBlinkingTheme.TextPrimary;
      _icon.color = accent;
    }

    private void Update()
    {
      if (_group != null)
      {
        _group.alpha = Mathf.MoveTowards(_group.alpha, _targetAlpha, Time.unscaledDeltaTime * 5f);
      }
    }
  }

  internal sealed class DryCorePromptIconGraphic : MaskableGraphic
  {
    private DryCoreBossPrompt _prompt;

    internal DryCoreBossPrompt Prompt
    {
      get => _prompt;
      set
      {
        _prompt = value;
        SetVerticesDirty();
      }
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
      vertexHelper.Clear();
      switch (_prompt)
      {
        case DryCoreBossPrompt.SoftBlink:
          AddEye(vertexHelper, 0.28f);
          break;
        case DryCoreBossPrompt.CloseEyes:
          AddEye(vertexHelper, 1f);
          AddArc(vertexHelper, Vector2.zero, 34f, 205f, 335f, 12, 1.8f);
          break;
        case DryCoreBossPrompt.WaitForTone:
          AddArc(vertexHelper, Vector2.zero, 16f, -45f, 45f, 8, 2f);
          AddArc(vertexHelper, Vector2.zero, 27f, -45f, 45f, 10, 1.8f);
          break;
        case DryCoreBossPrompt.Open:
          AddEye(vertexHelper, 0f);
          break;
        case DryCoreBossPrompt.PushAway:
          AddDevice(vertexHelper);
          break;
        case DryCoreBossPrompt.Complete:
          AddArc(vertexHelper, Vector2.zero, 26f, 0f, 360f, 24, 2.4f);
          AddArc(vertexHelper, Vector2.zero, 15f, 0f, 360f, 18, 1.8f);
          break;
      }
    }

    private void AddEye(VertexHelper vh, float closedAmount)
    {
      const int segments = 12;
      var height = Mathf.Lerp(15f, 1.5f, closedAmount);
      for (var i = 0; i < segments; i++)
      {
        var t0 = i / (float)segments;
        var t1 = (i + 1) / (float)segments;
        var x0 = Mathf.Lerp(-30f, 30f, t0);
        var x1 = Mathf.Lerp(-30f, 30f, t1);
        var arch0 = Mathf.Sin(t0 * Mathf.PI) * height;
        var arch1 = Mathf.Sin(t1 * Mathf.PI) * height;
        AddLine(vh, new Vector2(x0, arch0), new Vector2(x1, arch1), 2.4f);
        AddLine(vh, new Vector2(x0, -arch0), new Vector2(x1, -arch1), 2.4f);
      }
      if (closedAmount < 0.8f)
      {
        AddArc(vh, Vector2.zero, 7f, 0f, 360f, 14, 2f);
      }
    }

    private void AddDevice(VertexHelper vh)
    {
      AddLine(vh, new Vector2(-23f, -28f), new Vector2(13f, -28f), 2.5f);
      AddLine(vh, new Vector2(13f, -28f), new Vector2(13f, 28f), 2.5f);
      AddLine(vh, new Vector2(13f, 28f), new Vector2(-23f, 28f), 2.5f);
      AddLine(vh, new Vector2(-23f, 28f), new Vector2(-23f, -28f), 2.5f);
      AddLine(vh, new Vector2(22f, 0f), new Vector2(36f, 0f), 2.5f);
      AddLine(vh, new Vector2(29f, 7f), new Vector2(36f, 0f), 2.5f);
      AddLine(vh, new Vector2(29f, -7f), new Vector2(36f, 0f), 2.5f);
    }

    private void AddArc(VertexHelper vh, Vector2 center, float radius, float startDegrees, float endDegrees, int segments, float width)
    {
      var previous = center + Direction(startDegrees) * radius;
      for (var i = 1; i <= segments; i++)
      {
        var angle = Mathf.Lerp(startDegrees, endDegrees, i / (float)segments);
        var next = center + Direction(angle) * radius;
        AddLine(vh, previous, next, width);
        previous = next;
      }
    }

    private static Vector2 Direction(float degrees)
    {
      var radians = degrees * Mathf.Deg2Rad;
      return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
    }

    private void AddLine(VertexHelper vh, Vector2 from, Vector2 to, float width)
    {
      var direction = (to - from).normalized;
      var normal = new Vector2(-direction.y, direction.x) * width * 0.5f;
      var index = vh.currentVertCount;
      vh.AddVert(from - normal, color, Vector2.zero);
      vh.AddVert(from + normal, color, Vector2.up);
      vh.AddVert(to + normal, color, Vector2.one);
      vh.AddVert(to - normal, color, Vector2.right);
      vh.AddTriangle(index, index + 1, index + 2);
      vh.AddTriangle(index, index + 2, index + 3);
    }
  }
}
