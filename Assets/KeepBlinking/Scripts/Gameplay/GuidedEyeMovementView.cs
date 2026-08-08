using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.Gameplay
{
  public sealed class GuidedEyeMovementView : MonoBehaviour
  {
    private const int LidSegments = 9;
    private readonly RectTransform[] _upperLid = new RectTransform[LidSegments];
    private readonly RectTransform[] _lowerLid = new RectTransform[LidSegments];
    private CanvasGroup _canvasGroup;
    private Image _dim;
    private RectTransform _eyeRoot;
    private CanvasGroup _eyeGroup;
    private Image _track;
    private RectTransform _mintDot;
    private RectTransform _directionArrow;
    private Image _ripple;
    private TextMeshProUGUI _title;
    private TextMeshProUGUI _prompt;
    private Button _skipButton;
    private Button _watchButton;
    private float _targetDimAlpha = 0.5f;

    private void Awake()
    {
      Build();
      Hide();
    }

    public void Show()
    {
      if (_canvasGroup == null) Build();
      _canvasGroup.alpha = 1f;
      _canvasGroup.interactable = true;
      _canvasGroup.blocksRaycasts = true;
    }

    public void Hide()
    {
      if (_canvasGroup == null) return;
      _canvasGroup.alpha = 0f;
      _canvasGroup.interactable = false;
      _canvasGroup.blocksRaycasts = false;
    }

    public void SetPrompt(string prompt)
    {
      if (_prompt != null) _prompt.text = prompt ?? string.Empty;
    }

    public void SetState(GuidedEyeMovementState state, bool canReplayPreview)
    {
      if (_title == null) return;
      _title.text = IsPreview(state) ? "WATCH" : string.Empty;
      _prompt.text = PromptFor(state);
      _watchButton.gameObject.SetActive(
        canReplayPreview &&
        (state == GuidedEyeMovementState.PromptClose || state == GuidedEyeMovementState.WaitEyesClosed));
      _skipButton.gameObject.SetActive(
        state != GuidedEyeMovementState.ReopenFeedback &&
        state != GuidedEyeMovementState.Completed &&
        state != GuidedEyeMovementState.Skipped);
    }

    public void ShowReturnNeutral(string prompt)
    {
      Show();
      _canvasGroup.interactable = false;
      _canvasGroup.blocksRaycasts = false;
      _title.text = string.Empty;
      _prompt.text = prompt;
      _skipButton.gameObject.SetActive(false);
      _watchButton.gameObject.SetActive(false);
      _track.gameObject.SetActive(false);
      _mintDot.gameObject.SetActive(false);
      _directionArrow.gameObject.SetActive(false);
      _ripple.color = Color.clear;
      _targetDimAlpha = 0.18f;
      _eyeGroup.alpha = 1f;
      SetEyeOpenness(1f);
      _eyeRoot.localScale = Vector3.one * 0.62f;
    }

    public void Render(GuidedEyeMovementState state, float phaseProgress, float validGuidanceProgress)
    {
      if (_eyeRoot == null) return;
      _targetDimAlpha = DimAlphaFor(state);
      _dim.color = KeepBlinkingTheme.WithAlpha(
        KeepBlinkingTheme.BackgroundTertiary,
        Mathf.Lerp(_dim.color.a, _targetDimAlpha, 1f - Mathf.Exp(-4.2f * Time.unscaledDeltaTime)));

      var previewClockwise = state == GuidedEyeMovementState.PreviewClockwise;
      var previewPause = state == GuidedEyeMovementState.PreviewPause;
      var previewCounter = state == GuidedEyeMovementState.PreviewCounterClockwise;
      var preview = previewClockwise || previewPause || previewCounter;
      _track.gameObject.SetActive(preview);
      _mintDot.gameObject.SetActive(preview);
      _directionArrow.gameObject.SetActive(preview && !previewPause);
      var targetEyeAlpha = EyeAlphaFor(state);
      _eyeGroup.alpha = Mathf.Lerp(
        _eyeGroup.alpha,
        targetEyeAlpha,
        1f - Mathf.Exp(-5.2f * Time.unscaledDeltaTime));

      var openness = 1f;
      if (previewClockwise) openness = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phaseProgress / 0.18f));
      else if (previewPause) openness = 0.04f;
      else if (previewCounter)
        openness = phaseProgress < 0.82f ? 0.04f : Mathf.SmoothStep(0.04f, 1f, (phaseProgress - 0.82f) / 0.18f);
      else if (state == GuidedEyeMovementState.GuidedClockwise ||
               state == GuidedEyeMovementState.GuidedPause ||
               state == GuidedEyeMovementState.GuidedCounterClockwise ||
               state == GuidedEyeMovementState.PausedTracking ||
               state == GuidedEyeMovementState.CompletionCue ||
               state == GuidedEyeMovementState.WaitReopen)
        openness = 0.04f;
      else if (state == GuidedEyeMovementState.ReopenFeedback)
        openness = Mathf.SmoothStep(0.04f, 1f, phaseProgress);
      SetEyeOpenness(openness);

      if (preview)
      {
        var angle = previewPause
          ? 0f
          : (previewClockwise
            ? Mathf.PI * 0.5f - phaseProgress * Mathf.PI * 2f
            : Mathf.PI * 0.5f + phaseProgress * Mathf.PI * 2f);
        var radius = previewPause ? 0f : 116f;
        var point = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        _mintDot.anchoredPosition = point;
        _directionArrow.anchoredPosition = point;
        _directionArrow.localRotation = Quaternion.Euler(
          0f,
          0f,
          (angle * Mathf.Rad2Deg) + (previewClockwise ? -90f : 90f));
      }

      var reopen = state == GuidedEyeMovementState.ReopenFeedback ? phaseProgress : 0f;
      _ripple.color = KeepBlinkingTheme.WithAlpha(
        KeepBlinkingTheme.AccentPrimary,
        reopen * (1f - reopen) * 1.7f);
      _ripple.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.35f, 3.2f, reopen);
      var breath = 1f + Mathf.Sin(Time.unscaledTime * 1.15f) * 0.018f;
      _eyeRoot.localScale = Vector3.one * (breath + validGuidanceProgress * 0.02f);
    }

    private void SetEyeOpenness(float openness)
    {
      openness = Mathf.Clamp01(openness);
      for (var i = 0; i < LidSegments; i++)
      {
        var normalizedX = Mathf.Lerp(-1f, 1f, i / (float)(LidSegments - 1));
        var x = normalizedX * 172f;
        var arc = (1f - normalizedX * normalizedX) * 76f * openness;
        _upperLid[i].anchoredPosition = new Vector2(x, arc);
        _lowerLid[i].anchoredPosition = new Vector2(x, -arc);
        _upperLid[i].localRotation = Quaternion.Euler(0f, 0f, -normalizedX * openness * 28f);
        _lowerLid[i].localRotation = Quaternion.Euler(0f, 0f, normalizedX * openness * 28f);
      }
    }

    private void Build()
    {
      var safe = FirstLevelUiFactory.CreateCanvas(transform, "Guided Eye Movement Canvas", 425, out _, out _canvasGroup);
      _dim = FirstLevelUiFactory.CreateImage(
        "Visual Dimming",
        safe,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BackgroundTertiary, 0.52f));
      FirstLevelUiFactory.Stretch(_dim.rectTransform);

      _eyeRoot = FirstLevelUiFactory.CreateObject("Minimal Closed-Eye Guide", safe).GetComponent<RectTransform>();
      _eyeGroup = _eyeRoot.gameObject.AddComponent<CanvasGroup>();
      FirstLevelUiFactory.SetRect(
        _eyeRoot,
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f),
        new Vector2(0f, 95f),
        new Vector2(520f, 420f));

      _ripple = FirstLevelUiFactory.CreateImage("Mint Reopen Ripple", _eyeRoot, Color.clear, FirstLevelUiFactory.RingSprite);
      FirstLevelUiFactory.SetRect(
        _ripple.rectTransform,
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f),
        Vector2.zero,
        new Vector2(310f, 310f));

      _track = FirstLevelUiFactory.CreateImage(
        "Low-Contrast Circular Guide",
        _eyeRoot,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentWarm, 0.25f),
        FirstLevelUiFactory.RingSprite);
      FirstLevelUiFactory.SetRect(
        _track.rectTransform,
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f),
        Vector2.zero,
        new Vector2(278f, 278f));

      for (var i = 0; i < LidSegments; i++)
      {
        var upper = FirstLevelUiFactory.CreateImage(
          $"Warm White Upper Lid {i + 1}",
          _eyeRoot,
          KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.95f),
          FirstLevelUiFactory.RoundedSprite);
        FirstLevelUiFactory.SetRect(
          upper.rectTransform,
          new Vector2(0.5f, 0.5f),
          new Vector2(0.5f, 0.5f),
          new Vector2(0.5f, 0.5f),
          Vector2.zero,
          new Vector2(48f, 9f));
        _upperLid[i] = upper.rectTransform;

        var lower = FirstLevelUiFactory.CreateImage(
          $"Soft Lower Lid {i + 1}",
          _eyeRoot,
          KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.52f),
          FirstLevelUiFactory.RoundedSprite);
        FirstLevelUiFactory.SetRect(
          lower.rectTransform,
          new Vector2(0.5f, 0.5f),
          new Vector2(0.5f, 0.5f),
          new Vector2(0.5f, 0.5f),
          Vector2.zero,
          new Vector2(48f, 7f));
        _lowerLid[i] = lower.rectTransform;
      }

      var dotImage = FirstLevelUiFactory.CreateImage(
        "Muted Mint Guide Point",
        _eyeRoot,
        KeepBlinkingTheme.AccentPrimary,
        FirstLevelUiFactory.CircleSprite);
      _mintDot = dotImage.rectTransform;
      FirstLevelUiFactory.SetRect(
        _mintDot,
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f),
        Vector2.zero,
        new Vector2(32f, 32f));

      _directionArrow = FirstLevelUiFactory.CreateObject("Gentle Direction Arrow", _eyeRoot).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(
        _directionArrow,
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f),
        Vector2.zero,
        new Vector2(72f, 44f));
      var shaft = FirstLevelUiFactory.CreateImage(
        "Arrow Shaft",
        _directionArrow,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentWarm, 0.82f),
        FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(
        shaft.rectTransform,
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f),
        new Vector2(14f, 0f),
        new Vector2(46f, 6f));
      CreateArrowHead(_directionArrow, 23f);
      CreateArrowHead(_directionArrow, -23f);

      _title = FirstLevelUiFactory.CreateText(
        "Title",
        safe,
        string.Empty,
        30f,
        FontStyles.Bold,
        TextAlignmentOptions.Center,
        KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(
        _title.rectTransform,
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f),
        new Vector2(0f, 390f),
        new Vector2(720f, 60f));

      _prompt = FirstLevelUiFactory.CreateText(
        "Prompt",
        safe,
        string.Empty,
        34f,
        FontStyles.Bold,
        TextAlignmentOptions.Center,
        KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(
        _prompt.rectTransform,
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f),
        new Vector2(0f, -250f),
        new Vector2(820f, 80f));

      _watchButton = FirstLevelUiFactory.CreateButton("Watch Again", safe, "WATCH", KeepBlinkingTheme.AccentPrimary);
      FirstLevelUiFactory.SetRect(
        _watchButton.GetComponent<RectTransform>(),
        new Vector2(0f, 1f),
        new Vector2(0f, 1f),
        new Vector2(0f, 1f),
        new Vector2(36f, -42f),
        new Vector2(170f, 66f));
      _watchButton.onClick.AddListener(() => GuidedEyeMovementController.Instance?.ReplayPreview());

      _skipButton = FirstLevelUiFactory.CreateButton("Skip", safe, "SKIP", KeepBlinkingTheme.AccentWarm);
      FirstLevelUiFactory.SetRect(
        _skipButton.GetComponent<RectTransform>(),
        Vector2.one,
        Vector2.one,
        Vector2.one,
        new Vector2(-36f, -42f),
        new Vector2(170f, 66f));
      _skipButton.onClick.AddListener(() => GuidedEyeMovementController.Instance?.Skip());
      SetEyeOpenness(1f);
    }

    private static void CreateArrowHead(Transform parent, float rotation)
    {
      var head = FirstLevelUiFactory.CreateImage(
        "Arrow Head",
        parent,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentWarm, 0.82f),
        FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(
        head.rectTransform,
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f),
        new Vector2(34f, 0f),
        new Vector2(24f, 5f));
      head.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotation);
    }

    private static bool IsPreview(GuidedEyeMovementState state)
    {
      return state == GuidedEyeMovementState.Preparing ||
             state == GuidedEyeMovementState.PreviewClockwise ||
             state == GuidedEyeMovementState.PreviewPause ||
             state == GuidedEyeMovementState.PreviewCounterClockwise;
    }

    private static string PromptFor(GuidedEyeMovementState state)
    {
      if (IsPreview(state)) return "FOLLOW THE RHYTHM";
      switch (state)
      {
        case GuidedEyeMovementState.PromptClose:
        case GuidedEyeMovementState.WaitEyesClosed: return "CLOSE YOUR EYES";
        case GuidedEyeMovementState.CompletionCue:
        case GuidedEyeMovementState.WaitReopen: return "OPEN YOUR EYES";
        case GuidedEyeMovementState.PausedTracking: return "FOLLOW THE RHYTHM";
        default: return string.Empty;
      }
    }

    private static float DimAlphaFor(GuidedEyeMovementState state)
    {
      if (IsPreview(state)) return 0.56f;
      switch (state)
      {
        case GuidedEyeMovementState.PromptClose:
        case GuidedEyeMovementState.WaitEyesClosed: return 0.68f;
        case GuidedEyeMovementState.GuidedClockwise:
        case GuidedEyeMovementState.GuidedPause:
        case GuidedEyeMovementState.GuidedCounterClockwise: return 0.90f;
        case GuidedEyeMovementState.PausedTracking: return 0.93f;
        case GuidedEyeMovementState.CompletionCue:
        case GuidedEyeMovementState.WaitReopen: return 0.82f;
        case GuidedEyeMovementState.ReopenFeedback: return 0.48f;
        default: return 0.56f;
      }
    }

    private static float EyeAlphaFor(GuidedEyeMovementState state)
    {
      if (IsPreview(state) ||
          state == GuidedEyeMovementState.PromptClose ||
          state == GuidedEyeMovementState.WaitEyesClosed)
        return 1f;
      switch (state)
      {
        case GuidedEyeMovementState.GuidedClockwise:
        case GuidedEyeMovementState.GuidedPause:
        case GuidedEyeMovementState.GuidedCounterClockwise: return 0.10f;
        case GuidedEyeMovementState.PausedTracking: return 0.06f;
        case GuidedEyeMovementState.CompletionCue:
        case GuidedEyeMovementState.WaitReopen: return 0.45f;
        case GuidedEyeMovementState.ReopenFeedback: return 1f;
        default: return 0.7f;
      }
    }
  }
}
