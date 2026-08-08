using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.Gameplay
{
  public sealed class DirectionalPhoneMovementView : MonoBehaviour
  {
    private readonly List<Image> _trackNodes = new List<Image>(16);
    private CanvasGroup _group;
    private RectTransform _trackRoot;
    private RectTransform _phoneRoot;
    private RectTransform _arrow;
    private Image _arrowShaft;
    private Image _arrowHeadA;
    private Image _arrowHeadB;
    private Image _phoneProgress;
    private Image _centerRing;
    private TextMeshProUGUI _prompt;
    private TextMeshProUGUI _status;
    private DirectionalPhoneAxis _axis;
    private DirectionalPhoneMovementState _state;
    private int _nodeCount = 14;

    private void Awake()
    {
      Build();
      Hide();
    }

    public void Show()
    {
      _group.alpha = 1f;
      _group.interactable = false;
      _group.blocksRaycasts = false;
      _status.text = string.Empty;
    }

    public void Hide()
    {
      if (_group == null) return;
      _group.alpha = 0f;
      _group.interactable = false;
      _group.blocksRaycasts = false;
    }

    public void ConfigureAxis(DirectionalPhoneAxis axis, int nodeCount)
    {
      _axis = axis;
      _nodeCount = Mathf.Clamp(nodeCount, 12, Mathf.Min(16, _trackNodes.Count));
      _trackRoot.gameObject.SetActive(true);
      for (var i = 0; i < _trackNodes.Count; i++)
      {
        var node = _trackNodes[i];
        var visible = i < _nodeCount;
        node.gameObject.SetActive(visible);
        if (!visible) continue;
        var t = _nodeCount <= 1 ? 0.5f : i / (float)(_nodeCount - 1);
        node.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        node.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        node.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        node.rectTransform.anchoredPosition = axis == DirectionalPhoneAxis.Horizontal
          ? new Vector2(Mathf.Lerp(-270f, 270f, t), 0f)
          : new Vector2(0f, Mathf.Lerp(-235f, 235f, t));
        node.rectTransform.sizeDelta = new Vector2(31f, 31f);
        node.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.16f);
      }
      SetArrow(axis == DirectionalPhoneAxis.Horizontal ? 180f : -90f, true);
      RenderTrack(0f, false);
    }

    public void SetState(DirectionalPhoneMovementState state, DirectionalPhoneAxis axis)
    {
      _state = state;
      _axis = axis;
      _status.gameObject.SetActive(false);
      _centerRing.gameObject.SetActive(state == DirectionalPhoneMovementState.WaitNeutral ||
                                       state == DirectionalPhoneMovementState.ReturnCenter);
      _arrow.gameObject.SetActive(state == DirectionalPhoneMovementState.MoveToStart ||
                                  state == DirectionalPhoneMovementState.HoldStart ||
                                  state == DirectionalPhoneMovementState.Sweep ||
                                  state == DirectionalPhoneMovementState.HoldEnd);
      switch (state)
      {
        case DirectionalPhoneMovementState.Preparing:
        case DirectionalPhoneMovementState.WaitNeutral:
          _prompt.text = "HOLD STEADY";
          break;
        case DirectionalPhoneMovementState.MoveToStart:
        case DirectionalPhoneMovementState.HoldStart:
          _prompt.text = axis == DirectionalPhoneAxis.Horizontal
            ? "MOVE LEFT TO START"
            : "MOVE DOWN TO START";
          SetArrow(axis == DirectionalPhoneAxis.Horizontal ? 180f : -90f, true);
          break;
        case DirectionalPhoneMovementState.Sweep:
          _prompt.text = axis == DirectionalPhoneAxis.Horizontal ? "SWEEP RIGHT" : "SWEEP UP";
          SetArrow(axis == DirectionalPhoneAxis.Horizontal ? 0f : 90f, true);
          break;
        case DirectionalPhoneMovementState.HoldEnd:
          _prompt.text = "HOLD STEADY";
          _arrow.gameObject.SetActive(false);
          break;
        case DirectionalPhoneMovementState.ReturnCenter:
          _prompt.text = "RETURN TO CENTER";
          break;
        case DirectionalPhoneMovementState.PausedTracking:
          _prompt.text = "TRACKING LOST";
          break;
      }
    }

    public void SetStatus(string status)
    {
      if (_status == null || _prompt == null) return;
      status = status ?? string.Empty;

      // During baseline/neutral acquisition the blocker is the only useful
      // instruction. Put it in the main prompt so two labels cannot overlap.
      if (_state == DirectionalPhoneMovementState.Preparing ||
          _state == DirectionalPhoneMovementState.WaitNeutral ||
          _state == DirectionalPhoneMovementState.PausedTracking)
      {
        if (!string.IsNullOrEmpty(status)) _prompt.text = status;
        _status.text = string.Empty;
        _status.gameObject.SetActive(false);
        return;
      }

      _status.text = status;
      _status.gameObject.SetActive(!string.IsNullOrEmpty(status) && status != _prompt.text);
    }

    public void ShowTrackingLost(bool lost)
    {
      if (lost)
      {
        _prompt.text = "TRACKING LOST";
        _status.gameObject.SetActive(false);
      }
    }

    public void RenderPreparation(float progress)
    {
      _trackRoot.gameObject.SetActive(false);
      _phoneProgress.fillAmount = Mathf.Clamp01(progress);
      _phoneProgress.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.72f);
      PulsePhone(progress >= 0.98f);
    }

    public void RenderMoveToStart(float progress, bool holding, bool valid)
    {
      _trackRoot.gameObject.SetActive(true);
      RenderTrack(0f, false);
      _phoneProgress.fillAmount = Mathf.Clamp01(progress);
      var color = valid
        ? Color.Lerp(KeepBlinkingTheme.TextPrimary, KeepBlinkingTheme.AccentPrimary, progress)
        : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.5f);
      SetArrowColor(color);
      _phoneProgress.color = KeepBlinkingTheme.WithAlpha(color, 0.72f);
      PulsePhone(holding);
    }

    public void BeginSweep()
    {
      _trackRoot.gameObject.SetActive(true);
      _phoneProgress.fillAmount = 0f;
      RenderTrack(0f, false);
    }

    public void RenderSweep(float visualProgress, float maxProgress, bool holdingEnd, bool valid)
    {
      _trackRoot.gameObject.SetActive(true);
      RenderTrack(maxProgress, holdingEnd);
      _phoneProgress.fillAmount = Mathf.Clamp01(visualProgress);
      var color = valid
        ? Color.Lerp(KeepBlinkingTheme.TextPrimary, KeepBlinkingTheme.AccentPrimary, maxProgress)
        : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.5f);
      SetArrowColor(color);
      _phoneProgress.color = KeepBlinkingTheme.WithAlpha(color, 0.68f);
      PulsePhone(holdingEnd);
    }

    public void RenderCenter(bool inside, float progress)
    {
      _phoneProgress.fillAmount = Mathf.Clamp01(progress);
      var color = Color.Lerp(KeepBlinkingTheme.TextPrimary, KeepBlinkingTheme.AccentPrimary, progress);
      _phoneProgress.color = KeepBlinkingTheme.WithAlpha(color, 0.7f);
      _centerRing.color = KeepBlinkingTheme.WithAlpha(color, inside ? 0.88f : 0.42f);
      PulsePhone(inside);
    }

    private void RenderTrack(float maxProgress, bool endpointHolding)
    {
      for (var i = 0; i < _nodeCount; i++)
      {
        var threshold = (i + 1f) / _nodeCount;
        var reached = maxProgress + 0.0001f >= threshold;
        var current = !reached && i == Mathf.Clamp(Mathf.FloorToInt(maxProgress * _nodeCount), 0, _nodeCount - 1);
        var color = reached
          ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.92f)
          : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, current ? 0.34f : 0.16f);
        _trackNodes[i].color = color;
        var pulse = reached && endpointHolding && i == _nodeCount - 1
          ? 1f + Mathf.Sin(Time.unscaledTime * 3f) * 0.08f
          : 1f;
        _trackNodes[i].rectTransform.localScale = Vector3.one * pulse;
      }
    }

    private void PulsePhone(bool strong)
    {
      var amount = strong ? 0.035f : 0.012f;
      _phoneRoot.localScale = Vector3.one * (1f + Mathf.Sin(Time.unscaledTime * 2f) * amount);
    }

    private void SetArrow(float rotation, bool active)
    {
      _arrow.gameObject.SetActive(active);
      _arrow.localRotation = Quaternion.Euler(0f, 0f, rotation);
    }

    private void SetArrowColor(Color color)
    {
      _arrowShaft.color = color;
      _arrowHeadA.color = color;
      _arrowHeadB.color = color;
    }

    private void Build()
    {
      var safe = FirstLevelUiFactory.CreateCanvas(transform, "Directional Movement Canvas", 330, out _, out _group);

      _trackRoot = FirstLevelUiFactory.CreateObject("Continuous Experience Track", safe).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(
        _trackRoot,
        new Vector2(0.5f, 0.55f),
        new Vector2(0.5f, 0.55f),
        new Vector2(0.5f, 0.5f),
        Vector2.zero,
        new Vector2(620f, 540f));
      for (var i = 0; i < 16; i++)
      {
        var node = FirstLevelUiFactory.CreateImage(
          $"Track Node {i + 1}",
          _trackRoot,
          KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.16f),
          FirstLevelUiFactory.RingSprite);
        _trackNodes.Add(node);
      }

      var instructionPanel = FirstLevelUiFactory.CreateImage(
        "Movement Instruction Backdrop",
        safe,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceScrim, 0.18f),
        FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(
        instructionPanel.rectTransform,
        new Vector2(0.5f, 0.22f),
        new Vector2(0.5f, 0.22f),
        new Vector2(0.5f, 0.5f),
        Vector2.zero,
        new Vector2(380f, 300f));

      _phoneRoot = FirstLevelUiFactory.CreateObject("Small Phone Outline", instructionPanel.transform).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(
        _phoneRoot,
        new Vector2(0.5f, 0.58f),
        new Vector2(0.5f, 0.58f),
        new Vector2(0.5f, 0.5f),
        Vector2.zero,
        new Vector2(84f, 148f));
      var outer = FirstLevelUiFactory.CreateImage("Warm White Phone", _phoneRoot, KeepBlinkingTheme.TextPrimary, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(outer.rectTransform);
      var inner = FirstLevelUiFactory.CreateImage("Phone Screen", _phoneRoot, KeepBlinkingTheme.BackgroundSecondary, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(inner.rectTransform, new Vector2(7f, 9f), new Vector2(-7f, -9f));

      _phoneProgress = FirstLevelUiFactory.CreateImage(
        "Phone Movement Progress",
        instructionPanel.transform,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.7f),
        FirstLevelUiFactory.RingSprite);
      FirstLevelUiFactory.SetRect(
        _phoneProgress.rectTransform,
        new Vector2(0.5f, 0.58f),
        new Vector2(0.5f, 0.58f),
        new Vector2(0.5f, 0.5f),
        Vector2.zero,
        new Vector2(184f, 184f));
      _phoneProgress.type = Image.Type.Filled;
      _phoneProgress.fillMethod = Image.FillMethod.Radial360;
      _phoneProgress.fillOrigin = 2;
      _phoneProgress.fillAmount = 0f;

      _centerRing = FirstLevelUiFactory.CreateImage(
        "Return Center Ring",
        instructionPanel.transform,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.42f),
        FirstLevelUiFactory.RingSprite);
      FirstLevelUiFactory.SetRect(
        _centerRing.rectTransform,
        new Vector2(0.5f, 0.58f),
        new Vector2(0.5f, 0.58f),
        new Vector2(0.5f, 0.5f),
        Vector2.zero,
        new Vector2(155f, 155f));

      _arrow = FirstLevelUiFactory.CreateObject("Direction Arrow", instructionPanel.transform).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(
        _arrow,
        new Vector2(0.5f, 0.58f),
        new Vector2(0.5f, 0.58f),
        new Vector2(0f, 0.5f),
        new Vector2(66f, 0f),
        new Vector2(130f, 62f));
      _arrowShaft = FirstLevelUiFactory.CreateImage("Arrow Shaft", _arrow, KeepBlinkingTheme.TextPrimary, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(_arrowShaft.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(96f, 12f));
      _arrowHeadA = CreateArrowHead("Arrow Head A", _arrow, 34f);
      _arrowHeadB = CreateArrowHead("Arrow Head B", _arrow, -34f);

      _status = FirstLevelUiFactory.CreateText(
        "Recognition Status",
        instructionPanel.transform,
        string.Empty,
        19f,
        FontStyles.Normal,
        TextAlignmentOptions.Center,
        KeepBlinkingTheme.TextSecondary);
      FirstLevelUiFactory.SetRect(
        _status.rectTransform,
        new Vector2(0.5f, 0.34f),
        new Vector2(0.5f, 0.34f),
        new Vector2(0.5f, 0.5f),
        Vector2.zero,
        new Vector2(340f, 36f));

      _prompt = FirstLevelUiFactory.CreateText(
        "Movement Prompt",
        instructionPanel.transform,
        "HOLD STEADY",
        27f,
        FontStyles.Bold,
        TextAlignmentOptions.Center,
        KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(
        _prompt.rectTransform,
        new Vector2(0.5f, 0f),
        new Vector2(0.5f, 0f),
        new Vector2(0.5f, 0f),
        new Vector2(0f, 20f),
        new Vector2(360f, 54f));
    }

    private Image CreateArrowHead(string name, Transform parent, float angle)
    {
      var image = FirstLevelUiFactory.CreateImage(name, parent, KeepBlinkingTheme.TextPrimary, FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(image.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-3f, 0f), new Vector2(44f, 12f));
      image.rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
      return image;
    }
  }
}
