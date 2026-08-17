using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.Gameplay
{
  public sealed class FocusShiftView : MonoBehaviour
  {
    private CanvasGroup _group;
    private RectTransform _sampleRoot;
    private Image _sample;
    private Image _progress;
    private Image _dampingRing;
    private RectTransform _trackRoot;
    private RectTransform _nearArrow;
    private RectTransform _farArrow;
    private TextMeshProUGUI _prompt;
    private readonly List<Image> _trackNodes = new List<Image>(10);
    private int _activeTrackNodes = 10;

    [Header("Distance Visual Feedback")]
    [SerializeField, Range(0.3f, 0.8f)] private float _farVisualScale = 0.42f;
    [SerializeField, Range(1.3f, 1.9f)] private float _nearVisualScale = 1.72f;
    [SerializeField, Range(1.6f, 2.2f)] private float _tooCloseVisualScale = 1.95f;
    // Endpoints of the visual depth feedback, in the same linear ratio the controller judges
    // with. They track the Far band floor, the middle of the Near band and Too Close, so the
    // sample keeps growing across the whole movement instead of saturating early.
    [SerializeField, Range(0.6f, 0.95f)] private float _visualFarRatio = 0.69f;
    [SerializeField, Range(1.12f, 1.5f)] private float _visualNearRatio = 1.34f;
    [SerializeField, Range(1.3f, 1.8f)] private float _visualCapRatio = 1.60f;
    [SerializeField, Range(3f, 12f)] private float _visualResponse = 6.5f;

    public static float EvaluateDistanceVisualScale(
      float distanceRatio,
      float farVisualScale,
      float nearVisualScale,
      float tooCloseVisualScale,
      float visualFarRatio,
      float visualNearRatio,
      float visualCapRatio)
    {
      if (float.IsNaN(distanceRatio) || float.IsInfinity(distanceRatio)) return 1f;
      if (distanceRatio <= 1f)
      {
        var farT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(visualFarRatio, 1f, distanceRatio));
        return Mathf.Lerp(farVisualScale, 1f, farT);
      }

      if (distanceRatio <= visualNearRatio)
      {
        var nearT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, visualNearRatio, distanceRatio));
        return Mathf.Lerp(1f, nearVisualScale, nearT);
      }

      var closeT = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(visualNearRatio, visualCapRatio, distanceRatio));
      return Mathf.Lerp(nearVisualScale, tooCloseVisualScale, closeT);
    }

    private void Awake()
    {
      Build();
      Hide();
    }

    public void Bind(EdgeOrbitHarvestMvp gameplay)
    {
      if (_sample != null && gameplay != null && gameplay.CareExperienceSprite != null)
      {
        _sample.sprite = gameplay.CareExperienceSprite;
      }
    }

    public void Show()
    {
      _group.alpha = 1f;
      _group.interactable = false;
      _group.blocksRaycasts = false;
      _sampleRoot.localScale = Vector3.one;
      _progress.rectTransform.localScale = Vector3.one;
      if (_trackRoot != null) _trackRoot.localScale = Vector3.one;
    }

    public void Hide()
    {
      if (_group == null) return;
      _group.alpha = 0f;
      _group.interactable = false;
      _group.blocksRaycasts = false;
    }

    public void SetDirection(CareMovementDirection direction, int rewardNodes)
    {
      _prompt.text = direction == CareMovementDirection.Near
        ? "MOVE CLOSER"
        : direction == CareMovementDirection.Far
          ? "MOVE AWAY"
          : "RETURN TO CENTER";
      _nearArrow.gameObject.SetActive(direction == CareMovementDirection.Near);
      _farArrow.gameObject.SetActive(direction == CareMovementDirection.Far);
      _activeTrackNodes = Mathf.Clamp(rewardNodes, 1, _trackNodes.Count);
      for (var i = 0; i < _trackNodes.Count; i++)
      {
        _trackNodes[i].gameObject.SetActive(i < _activeTrackNodes);
        _trackNodes[i].color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.16f);
      }
    }

    public void SetCalibrating(float progress, bool tooClose, float distanceRatio = 1f)
    {
      var moveCloser = !tooClose && distanceRatio < 0.95f;
      var moveAway = tooClose || distanceRatio > 1.05f;
      _prompt.text = moveCloser
        ? "MOVE CLOSER"
        : moveAway
          ? "MOVE AWAY"
          : progress > 0f
            ? "HOLD STEADY"
            : "RETURN TO CENTER";
      _nearArrow.gameObject.SetActive(moveCloser);
      _farArrow.gameObject.SetActive(moveAway);
      _progress.fillAmount = Mathf.Clamp01(progress);
      for (var i = 0; i < _trackNodes.Count; i++) _trackNodes[i].gameObject.SetActive(false);
      _dampingRing.gameObject.SetActive(tooClose);
      _sample.color = tooClose
        ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentWarm, 0.74f)
        : Color.Lerp(KeepBlinkingTheme.TextPrimary, KeepBlinkingTheme.AccentPrimary, Mathf.Clamp01(progress));
      ApplyDistanceVisual(distanceRatio, tooClose);
    }

    public void SetTrackingLost()
    {
      _prompt.text = "TRACKING LOST";
      _nearArrow.gameObject.SetActive(false);
      _farArrow.gameObject.SetActive(false);
      _dampingRing.gameObject.SetActive(false);
    }

    public void Render(
      CareMovementDirection direction,
      float progress,
      float distanceRatio,
      bool tooClose,
      FocusShiftGuidance guidance,
      float holdProgress)
    {
      _prompt.text = guidance == FocusShiftGuidance.MoveCloser
        ? "MOVE CLOSER"
        : guidance == FocusShiftGuidance.MoveAway
          ? "MOVE AWAY"
          : "HOLD STEADY";
      _nearArrow.gameObject.SetActive(guidance == FocusShiftGuidance.MoveCloser);
      _farArrow.gameObject.SetActive(guidance == FocusShiftGuidance.MoveAway);
      ApplyDistanceVisual(distanceRatio, tooClose);
      _progress.fillAmount = guidance == FocusShiftGuidance.HoldSteady
        ? Mathf.Clamp01(holdProgress)
        : Mathf.Clamp01(progress);
      _progress.color = guidance == FocusShiftGuidance.HoldSteady
        ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.72f)
        : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.28f);
      RenderTrack(progress);
      _sample.color = tooClose
        ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentWarm, 0.74f)
        : guidance == FocusShiftGuidance.HoldSteady
          ? KeepBlinkingTheme.AccentPrimary
          : Color.Lerp(KeepBlinkingTheme.TextPrimary, KeepBlinkingTheme.AccentPrimary, Mathf.Clamp01(progress));
      if (guidance == FocusShiftGuidance.HoldSteady && !tooClose)
      {
        var pulse = 1f + Mathf.Sin(Time.unscaledTime * 2.4f) * 0.025f;
        _sample.rectTransform.localScale = Vector3.one * pulse;
      }
      else _sample.rectTransform.localScale = Vector3.one;
      _dampingRing.gameObject.SetActive(tooClose);
      if (tooClose)
      {
        var closeAmount = Mathf.InverseLerp(_visualNearRatio, _visualCapRatio, distanceRatio);
        var pulse = 1f + Mathf.Sin(Time.unscaledTime * 1.4f) * 0.035f;
        _dampingRing.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 1.18f, closeAmount) * pulse;
      }
    }

    private void ApplyDistanceVisual(float distanceRatio, bool tooClose)
    {
      var targetScale = EvaluateDistanceVisualScale(
        distanceRatio,
        _farVisualScale,
        _nearVisualScale,
        _tooCloseVisualScale,
        _visualFarRatio,
        _visualNearRatio,
        _visualCapRatio);
      var response = 1f - Mathf.Exp(-_visualResponse * Time.unscaledDeltaTime);
      _sampleRoot.localScale = Vector3.Lerp(_sampleRoot.localScale, Vector3.one * targetScale, response);

      // The surrounding reference rings move less than the sample. This makes
      // the real depth change easy to read without moving the target itself.
      var ringT = Mathf.InverseLerp(_visualFarRatio, _visualCapRatio, distanceRatio);
      var ringScale = Mathf.Lerp(0.86f, 1.16f, ringT);
      _progress.rectTransform.localScale = Vector3.Lerp(
        _progress.rectTransform.localScale,
        Vector3.one * ringScale,
        response);
      if (_trackRoot != null)
      {
        var trackScale = Mathf.Lerp(0.92f, 1.10f, ringT);
        _trackRoot.localScale = Vector3.Lerp(_trackRoot.localScale, Vector3.one * trackScale, response);
      }

      if (!tooClose) _dampingRing.rectTransform.localScale = Vector3.one;
    }

    private void Build()
    {
      var safe = FirstLevelUiFactory.CreateCanvas(transform, "Focus Shift Canvas", 332, out _, out _group);
      var panel = FirstLevelUiFactory.CreateImage("Focus Shift Backdrop", safe, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceScrim, 0.3f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(720f, 720f));

      _sampleRoot = FirstLevelUiFactory.CreateObject("Large Experience Reference", panel.transform).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_sampleRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(210f, 210f));
      _sample = FirstLevelUiFactory.CreateImage("Experience Sample", _sampleRoot, KeepBlinkingTheme.AccentPrimary, FirstLevelUiFactory.CircleSprite);
      FirstLevelUiFactory.Stretch(_sample.rectTransform);

      _progress = FirstLevelUiFactory.CreateImage("Shift Progress", panel.transform, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.18f), FirstLevelUiFactory.RingSprite);
      FirstLevelUiFactory.SetRect(_progress.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(420f, 420f));
      _progress.type = Image.Type.Filled;
      _progress.fillMethod = Image.FillMethod.Radial360;
      _progress.fillOrigin = 2;
      _progress.fillAmount = 0f;
      _progress.transform.SetAsFirstSibling();

      _trackRoot = FirstLevelUiFactory.CreateObject("Focus Shift Experience Track", panel.transform).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_trackRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 620f));
      for (var i = 0; i < 10; i++)
      {
        var angle = Mathf.Lerp(210f, -30f, i / 9f) * Mathf.Deg2Rad;
        var node = FirstLevelUiFactory.CreateImage(
          $"Focus Track Node {i + 1}",
          _trackRoot,
          KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.16f),
          FirstLevelUiFactory.RingSprite);
        FirstLevelUiFactory.SetRect(
          node.rectTransform,
          new Vector2(0.5f, 0.5f),
          new Vector2(0.5f, 0.5f),
          new Vector2(0.5f, 0.5f),
          new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 255f,
          new Vector2(32f, 32f));
        _trackNodes.Add(node);
      }

      _dampingRing = FirstLevelUiFactory.CreateImage("Too Close Damping Ring", panel.transform, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentWarm, 0.5f), FirstLevelUiFactory.RingSprite);
      FirstLevelUiFactory.SetRect(_dampingRing.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(480f, 480f));
      _dampingRing.gameObject.SetActive(false);

      _nearArrow = CreateDistanceArrow(panel.transform, "Near Arrows", true);
      _farArrow = CreateDistanceArrow(panel.transform, "Far Arrows", false);

      _prompt = FirstLevelUiFactory.CreateText("Focus Shift Prompt", panel.transform, "RETURN TO CENTER", 36f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(_prompt.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 42f), new Vector2(620f, 70f));
    }

    private void RenderTrack(float progress)
    {
      if (_trackNodes.Count == 0) return;
      for (var i = 0; i < _trackNodes.Count; i++)
      {
        if (i >= _activeTrackNodes) continue;
        var reached = progress + 0.0001f >= (i + 1f) / _activeTrackNodes;
        _trackNodes[i].color = reached
          ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.92f)
          : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.16f);
      }
    }

    private RectTransform CreateDistanceArrow(Transform parent, string name, bool inward)
    {
      var root = FirstLevelUiFactory.CreateObject(name, parent).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(root, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 160f));
      for (var side = -1; side <= 1; side += 2)
      {
        var shaft = FirstLevelUiFactory.CreateImage($"Arrow {side}", root, KeepBlinkingTheme.TextPrimary, FirstLevelUiFactory.RoundedSprite);
        var x = side * 185f;
        var rotation = inward ? (side < 0 ? 0f : 180f) : (side < 0 ? 180f : 0f);
        FirstLevelUiFactory.SetRect(shaft.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x, 0f), new Vector2(105f, 16f));
        shaft.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotation);
      }
      return root;
    }
  }
}
