using System;
using System.Collections.Generic;
using KeepBlinking.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.CareStation
{
  /// <summary>
  /// Safe-area-sized presentation shared by Pilot Eye Routine and the Guided
  /// Eye Movement that immediately follows it. This component is presentation
  /// only: it never advances an action, emits completion, or mutates economy.
  /// </summary>
  [DisallowMultipleComponent]
  internal sealed class EyeMovementGuidanceOverlay : MonoBehaviour
  {
    private const float DefaultGuideWidthRatio = 0.82f;
    private const float MinimumGuideWidthRatio = 0.76f;
    private const float DefaultGuideHeightRatio = 0.52f;
    private const float DefaultWorkerHeadWidthRatio = 0.37f;
    private const float DefaultWorkerShoulderWidthRatio = 0.55f;
    private const float DefaultEyeWidthRatio = 0.082f;
    private const float DefaultEndpointWidthRatio = 0.062f;
    private const float DefaultGuideDotWidthRatio = 0.052f;

    [SerializeField, Range(MinimumGuideWidthRatio, 0.9f)] private float _guideWidthRatio = DefaultGuideWidthRatio;
    [SerializeField, Range(0.32f, 0.42f)] private float _workerHeadWidthRatio = DefaultWorkerHeadWidthRatio;
    [SerializeField, Range(0.50f, 0.62f)] private float _workerShoulderWidthRatio = DefaultWorkerShoulderWidthRatio;
    [SerializeField, Range(0.07f, 0.11f)] private float _eyeWidthRatio = DefaultEyeWidthRatio;
    [SerializeField, Range(0.15f, 0.34f)] private float _pupilRangeRatio = 0.24f;

    private RectTransform _root;
    private CanvasGroup _overlayGroup;
    private Image _scrim;
    private TextMeshProUGUI _title;
    private TextMeshProUGUI _progress;
    private TextMeshProUGUI _prompt;
    private RectTransform _guideRoot;
    private CanvasGroup _workerGroup;
    private RectTransform _workerHead;
    private RectTransform _workerShoulders;
    private RectTransform _leftEye;
    private RectTransform _rightEye;
    private RectTransform _leftPupil;
    private RectTransform _rightPupil;
    private CanvasGroup _axesGroup;
    private readonly List<Image> _axes = new List<Image>(4);
    private readonly List<Image> _endpoints = new List<Image>(8);
    private Image _activeHalfAxis;
    private RectTransform _pilotGuideDot;
    private CanvasGroup _circleGroup;
    private Image _circleTrack;
    private RectTransform _guidedGuideDot;
    private Image _breathingRing;
    private RectTransform _safeAreaGuide;
    private readonly List<Image> _safeAreaLines = new List<Image>(4);
    private Vector2 _layoutSize;
    private float _guideSize;
    private float _headWidth;
    private float _eyeWidth;
    private float _targetAlpha;
    private bool _guidanceClosedPhase;
    private bool _showSafeArea;
    private string _externalPrompt = string.Empty;

    internal static EyeMovementGuidanceOverlay Create(RectTransform safeArea)
    {
      var root = FirstLevelUiFactory.CreateObject("EyeMovementGuidanceOverlay", safeArea).GetComponent<RectTransform>();
      FirstLevelUiFactory.Stretch(root);
      var overlay = root.gameObject.AddComponent<EyeMovementGuidanceOverlay>();
      overlay.Build(root);
      return overlay;
    }

    internal bool IsVisible => _root != null && _root.gameObject.activeSelf;
    internal float GuideSize => _guideSize;
    internal float SafeAreaWidth => _layoutSize.x;
    internal float SafeAreaHeight => _layoutSize.y;
    internal float GuideWidthRatio => _layoutSize.x > 0f ? _guideSize / _layoutSize.x : 0f;
    internal float WorkerHeadWidthRatio => _layoutSize.x > 0f ? _headWidth / _layoutSize.x : 0f;
    internal float EyeWidthRatio => _layoutSize.x > 0f ? _eyeWidth / _layoutSize.x : 0f;
    internal RectTransform Root => _root;
    internal RectTransform GuideRoot => _guideRoot;
    internal RectTransform PromptRect => _prompt != null ? _prompt.rectTransform : null;
    internal IReadOnlyList<Image> Endpoints => _endpoints;

    private void Build(RectTransform root)
    {
      _root = root;
      _overlayGroup = root.gameObject.AddComponent<CanvasGroup>();
      _overlayGroup.alpha = 0f;
      _overlayGroup.interactable = true;
      _overlayGroup.blocksRaycasts = true;

      _scrim = FirstLevelUiFactory.CreateImage("Guidance Input Shield", root,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BackgroundPrimary, 0.28f));
      FirstLevelUiFactory.Stretch(_scrim.rectTransform);
      // This is the sole raycast target in the guidance surface. It prevents
      // hidden Station controls from being activated while remaining inert.
      _scrim.raycastTarget = true;

      var topVeil = FirstLevelUiFactory.CreateImage("Hidden Station Header Veil", root,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BackgroundPrimary, 0.96f));
      FirstLevelUiFactory.SetRect(topVeil.rectTransform, new Vector2(0f, 0.82f), Vector2.one,
        new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var bottomVeil = FirstLevelUiFactory.CreateImage("Hidden Station Footer Veil", root,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BackgroundPrimary, 0.96f));
      FirstLevelUiFactory.SetRect(bottomVeil.rectTransform, Vector2.zero, new Vector2(1f, 0.19f),
        new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

      var ambientRing = FirstLevelUiFactory.CreateImage("Quiet Station Echo", root,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderSubtle, 0.12f), FirstLevelUiFactory.RingSprite);
      FirstLevelUiFactory.SetRect(ambientRing.rectTransform, new Vector2(0.5f, 0.47f), new Vector2(0.5f, 0.47f),
        new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 820f));
      ambientRing.raycastTarget = false;

      _title = FirstLevelUiFactory.CreateText("Guidance Action Title", root, "PILOT EYE ROUTINE", 42f,
        FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(_title.rectTransform, new Vector2(0.06f, 0.89f), new Vector2(0.94f, 0.95f),
        new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

      _progress = FirstLevelUiFactory.CreateText("Guidance Action Progress", root, "AXIS 1 / 4   ROUND 1 / 3", 25f,
        FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.AccentSoft);
      FirstLevelUiFactory.SetRect(_progress.rectTransform, new Vector2(0.06f, 0.84f), new Vector2(0.94f, 0.89f),
        new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

      _guideRoot = FirstLevelUiFactory.CreateObject("Fullscreen Eye Movement Guide", root).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_guideRoot, new Vector2(0.5f, 0.47f), new Vector2(0.5f, 0.47f),
        new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820f, 820f));

      BuildWorker();
      BuildPilotAxes();
      BuildGuidedCircle();

      _prompt = FirstLevelUiFactory.CreateText("Guidance Current Prompt", root, "LOOK UP AND DOWN", 34f,
        FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary, true);
      FirstLevelUiFactory.SetRect(_prompt.rectTransform, new Vector2(0.06f, 0.075f), new Vector2(0.94f, 0.15f),
        new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

      BuildSafeAreaGuide();
      LayoutForSize(new Vector2(FirstLevelUiFactory.ReferenceWidth, FirstLevelUiFactory.ReferenceHeight));
      root.gameObject.SetActive(false);
    }

    private void BuildWorker()
    {
      var workerRoot = FirstLevelUiFactory.CreateObject("Guidance Worker Closeup", _guideRoot).GetComponent<RectTransform>();
      FirstLevelUiFactory.Stretch(workerRoot);
      _workerGroup = workerRoot.gameObject.AddComponent<CanvasGroup>();

      _workerShoulders = FirstLevelUiFactory.CreateImage("Worker Shoulders", workerRoot,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentSoft, 0.78f), FirstLevelUiFactory.RoundedSprite).rectTransform;
      _workerHead = FirstLevelUiFactory.CreateImage("Worker Head Closeup", workerRoot,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextSecondary, 0.90f), FirstLevelUiFactory.CircleSprite).rectTransform;

      var leftEyeImage = FirstLevelUiFactory.CreateImage("Guidance Left Eye", _workerHead,
        KeepBlinkingTheme.TextPrimary, FirstLevelUiFactory.CircleSprite);
      _leftEye = leftEyeImage.rectTransform;
      var rightEyeImage = FirstLevelUiFactory.CreateImage("Guidance Right Eye", _workerHead,
        KeepBlinkingTheme.TextPrimary, FirstLevelUiFactory.CircleSprite);
      _rightEye = rightEyeImage.rectTransform;

      _leftPupil = FirstLevelUiFactory.CreateImage("Guidance Left Pupil", _leftEye,
        KeepBlinkingTheme.BackgroundTertiary, FirstLevelUiFactory.CircleSprite).rectTransform;
      _rightPupil = FirstLevelUiFactory.CreateImage("Guidance Right Pupil", _rightEye,
        KeepBlinkingTheme.BackgroundTertiary, FirstLevelUiFactory.CircleSprite).rectTransform;

      var chestMark = FirstLevelUiFactory.CreateImage("Worker Care Mark", _workerShoulders,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.74f), FirstLevelUiFactory.RingSprite);
      FirstLevelUiFactory.SetRect(chestMark.rectTransform, new Vector2(0.5f, 0.42f), new Vector2(0.5f, 0.42f),
        new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(54f, 54f));
    }

    private void BuildPilotAxes()
    {
      var axesRoot = FirstLevelUiFactory.CreateObject("Pilot Four Axes", _guideRoot).GetComponent<RectTransform>();
      FirstLevelUiFactory.Stretch(axesRoot);
      _axesGroup = axesRoot.gameObject.AddComponent<CanvasGroup>();
      var rotations = new[] { 90f, 0f, -45f, 45f };
      for (var index = 0; index < rotations.Length; index++)
      {
        var axis = FirstLevelUiFactory.CreateImage($"Fullscreen Pilot Axis {index + 1}", axesRoot,
          KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextSecondary, 0.16f), FirstLevelUiFactory.RoundedSprite);
        axis.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotations[index]);
        _axes.Add(axis);
      }

      for (var index = 0; index < 8; index++)
      {
        var endpoint = FirstLevelUiFactory.CreateImage($"Fullscreen Pilot Endpoint {index + 1}", axesRoot,
          KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextSecondary, 0.26f), FirstLevelUiFactory.RingSprite);
        _endpoints.Add(endpoint);
      }

      _activeHalfAxis = FirstLevelUiFactory.CreateImage("Active Pilot Half Axis", axesRoot,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.86f), FirstLevelUiFactory.RoundedSprite);
      _pilotGuideDot = FirstLevelUiFactory.CreateImage("Fullscreen Pilot Guide Dot", axesRoot,
        KeepBlinkingTheme.AccentPrimary, FirstLevelUiFactory.CircleSprite).rectTransform;
      var guideGlow = FirstLevelUiFactory.CreateImage("Pilot Guide Glow", _pilotGuideDot,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.22f), FirstLevelUiFactory.CircleSprite);
      FirstLevelUiFactory.Stretch(guideGlow.rectTransform, new Vector2(-12f, -12f), new Vector2(12f, 12f));
      guideGlow.transform.SetAsFirstSibling();
    }

    private void BuildGuidedCircle()
    {
      var circleRoot = FirstLevelUiFactory.CreateObject("Guided Circular Track", _guideRoot).GetComponent<RectTransform>();
      FirstLevelUiFactory.Stretch(circleRoot);
      _circleGroup = circleRoot.gameObject.AddComponent<CanvasGroup>();
      _circleTrack = FirstLevelUiFactory.CreateImage("Fullscreen Guided Circle", circleRoot,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.52f), FirstLevelUiFactory.RingSprite);
      _guidedGuideDot = FirstLevelUiFactory.CreateImage("Fullscreen Guided Guide Dot", circleRoot,
        KeepBlinkingTheme.AccentPrimary, FirstLevelUiFactory.CircleSprite).rectTransform;
      var guideGlow = FirstLevelUiFactory.CreateImage("Guided Guide Glow", _guidedGuideDot,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.22f), FirstLevelUiFactory.CircleSprite);
      FirstLevelUiFactory.Stretch(guideGlow.rectTransform, new Vector2(-12f, -12f), new Vector2(12f, 12f));
      guideGlow.transform.SetAsFirstSibling();

      _breathingRing = FirstLevelUiFactory.CreateImage("Guided Closed Rest Breathing Ring", circleRoot,
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.26f), FirstLevelUiFactory.RingSprite);
      _breathingRing.gameObject.SetActive(false);
      _circleGroup.alpha = 0f;
    }

    private void BuildSafeAreaGuide()
    {
      _safeAreaGuide = FirstLevelUiFactory.CreateObject("Guidance Safe Area Guide", _root).GetComponent<RectTransform>();
      FirstLevelUiFactory.Stretch(_safeAreaGuide, new Vector2(6f, 6f), new Vector2(-6f, -6f));
      for (var index = 0; index < 4; index++)
      {
        var line = FirstLevelUiFactory.CreateImage($"Safe Area Edge {index + 1}", _safeAreaGuide,
          KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentWarm, 0.72f), FirstLevelUiFactory.RoundedSprite);
        _safeAreaLines.Add(line);
      }
      FirstLevelUiFactory.SetRect(_safeAreaLines[0].rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 3f));
      FirstLevelUiFactory.SetRect(_safeAreaLines[1].rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(0f, 3f));
      FirstLevelUiFactory.SetRect(_safeAreaLines[2].rectTransform, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(3f, 0f));
      FirstLevelUiFactory.SetRect(_safeAreaLines[3].rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), Vector2.zero, new Vector2(3f, 0f));
      _safeAreaGuide.gameObject.SetActive(false);
    }

    internal void Present(CareActionType type, CareActionInternalPhase phase, string prompt)
    {
      if (type != CareActionType.PilotEyeRoutine && type != CareActionType.GuidedEyeCircles) return;
      if (!_root.gameObject.activeSelf)
      {
        _root.gameObject.SetActive(true);
        _root.SetAsLastSibling();
        _overlayGroup.alpha = 0f;
        _guideRoot.localScale = Vector3.one * 0.9f;
      }
      _targetAlpha = 1f;
      _overlayGroup.interactable = true;
      _overlayGroup.blocksRaycasts = true;
      _externalPrompt = prompt ?? string.Empty;
      _title.text = type == CareActionType.PilotEyeRoutine ? "PILOT EYE ROUTINE" : "GUIDED EYE MOVEMENT";
      if (type == CareActionType.PilotEyeRoutine)
      {
        _axesGroup.alpha = 1f;
        _circleGroup.alpha = 0f;
      }
      else
      {
        _axesGroup.alpha = 0f;
        _circleGroup.alpha = 1f;
      }
    }

    internal void PresentPilotToGuidedHold()
    {
      Present(CareActionType.PilotEyeRoutine, CareActionInternalPhase.PilotTransition,
        "AXES COMPLETE\nNEXT: SLOW CIRCLES");
      _progress.text = "AXES COMPLETE";
      _prompt.text = "NEXT: SLOW CIRCLES";
      _axesGroup.alpha = 0f;
      _circleGroup.alpha = 1f;
      MovePupils(Vector2.zero, true);
    }

    internal void Render(CareActionSaveData data)
    {
      if (data == null || !IsVisible) return;
      UpdateLayoutFromRect();
      if (data.actionType == CareActionType.PilotEyeRoutine)
        RenderPilot(data);
      else if (data.actionType == CareActionType.GuidedEyeCircles)
        RenderGuided(data);
    }

    internal void HideImmediate()
    {
      if (_root == null) return;
      _targetAlpha = 0f;
      _overlayGroup.alpha = 0f;
      _overlayGroup.interactable = false;
      _overlayGroup.blocksRaycasts = false;
      _root.gameObject.SetActive(false);
      _guidanceClosedPhase = false;
    }

    internal void HideAnimated()
    {
      if (_root == null || !_root.gameObject.activeSelf) return;
      _targetAlpha = 0f;
      _overlayGroup.interactable = false;
      // Keep blocking until the short fade completes so Station controls do
      // not become clickable through a still-visible guidance surface.
      _overlayGroup.blocksRaycasts = true;
    }

    internal void AdjustGuideSizeDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _guideWidthRatio += 0.02f;
      if (_guideWidthRatio > 0.9f) _guideWidthRatio = MinimumGuideWidthRatio;
      LayoutForSize(_layoutSize);
#endif
    }

    internal void AdjustWorkerSizeDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _workerHeadWidthRatio += 0.02f;
      if (_workerHeadWidthRatio > 0.42f) _workerHeadWidthRatio = 0.32f;
      _workerShoulderWidthRatio = Mathf.Clamp(_workerHeadWidthRatio + 0.18f, 0.5f, 0.62f);
      LayoutForSize(_layoutSize);
#endif
    }

    internal void AdjustEyeSizeDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _eyeWidthRatio += 0.008f;
      if (_eyeWidthRatio > 0.11f) _eyeWidthRatio = 0.07f;
      LayoutForSize(_layoutSize);
#endif
    }

    internal void AdjustPupilRangeDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _pupilRangeRatio += 0.03f;
      if (_pupilRangeRatio > 0.34f) _pupilRangeRatio = 0.15f;
#endif
    }

    internal void ToggleSafeAreaDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _showSafeArea = !_showSafeArea;
      if (_safeAreaGuide != null) _safeAreaGuide.gameObject.SetActive(_showSafeArea);
#endif
    }

    private void RenderPilot(CareActionSaveData data)
    {
      _title.text = "PILOT EYE ROUTINE";
      _guidanceClosedPhase = false;
      _workerGroup.alpha = 1f;
      _breathingRing.gameObject.SetActive(false);
      if (data.internalPhase == CareActionInternalPhase.PilotTransition)
      {
        var transition = Mathf.Clamp01(data.phaseElapsedSeconds / 1.25f);
        _progress.text = "AXES COMPLETE";
        _prompt.text = "NEXT: SLOW CIRCLES";
        _axesGroup.alpha = 1f - transition;
        _circleGroup.alpha = transition;
        MovePupils(Vector2.zero, true);
        return;
      }

      _axesGroup.alpha = 1f;
      _circleGroup.alpha = 0f;
      var axis = Mathf.Clamp(data.pilotCurrentAxis, 0, 3);
      var round = Mathf.Clamp(data.pilotCurrentRound + 1, 1, 3);
      _progress.text = $"AXIS {axis + 1} / 4   ROUND {round} / 3";
      _prompt.text = SpecialPromptOr(PromptForAxis(axis));

      for (var index = 0; index < _axes.Count; index++)
        _axes[index].color = index < axis
          ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.34f)
          : index == axis
            ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.68f)
            : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextSecondary, 0.15f);

      var direction = PilotDirection(axis, data.pilotNormalizedMoveProgress);
      var position = direction * (_guideSize * 0.46f);
      _pilotGuideDot.anchoredPosition = position;
      UpdateActiveHalfAxis(position);
      UpdateEndpoints(axis, position);
      MovePupils(direction, false);
    }

    private void RenderGuided(CareActionSaveData data)
    {
      _title.text = "GUIDED EYE MOVEMENT";
      _axesGroup.alpha = 0f;
      _circleGroup.alpha = 1f;
      var closed = data.internalPhase == CareActionInternalPhase.GuidedPromptClose ||
                   data.internalPhase == CareActionInternalPhase.GuidedClosedRest ||
                   data.internalPhase == CareActionInternalPhase.GuidedWaitReopen;
      _guidanceClosedPhase = closed;
      _breathingRing.gameObject.SetActive(closed);
      _workerGroup.alpha = closed ? 0.24f : 1f;
      _circleTrack.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, closed ? 0.12f : 0.52f);
      _guidedGuideDot.gameObject.SetActive(!closed);

      if (closed)
      {
        _progress.text = data.internalPhase == CareActionInternalPhase.GuidedWaitReopen
          ? "READY TO OPEN"
          : "QUIET REST";
        _prompt.text = SpecialPromptOr(data.internalPhase == CareActionInternalPhase.GuidedWaitReopen
          ? "OPEN YOUR EYES"
          : "CLOSE YOUR EYES");
        MovePupils(Vector2.zero, true);
        return;
      }

      var counter = data.internalPhase == CareActionInternalPhase.GuidedCounterClockwise ||
                    data.internalPhase == CareActionInternalPhase.GuidedPreviewCounterClockwise;
      var turns = data.guidedLapCount + Mathf.Clamp01(data.guidedNormalizedProgress);
      var angle = (counter ? 1f : -1f) * turns * Mathf.PI * 2f + Mathf.PI * 0.5f;
      var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
      _guidedGuideDot.anchoredPosition = direction * (_guideSize * 0.42f);
      MovePupils(direction, false);
      var lap = Mathf.Clamp(data.guidedLapCount + 1, 1, 3);
      _progress.text = counter ? $"COUNTERCLOCKWISE   {lap} / 3" : $"CLOCKWISE   {lap} / 3";
      _prompt.text = SpecialPromptOr("FOLLOW THE SLOW CIRCLES");
    }

    private string SpecialPromptOr(string fallback)
    {
      return _externalPrompt == "TRACKING LOST" || _externalPrompt == "SENSOR UNAVAILABLE" ||
             _externalPrompt == "PAUSED"
        ? _externalPrompt
        : fallback;
    }

    private void UpdateActiveHalfAxis(Vector2 target)
    {
      var length = target.magnitude;
      _activeHalfAxis.gameObject.SetActive(length > 3f);
      if (length <= 3f) return;
      _activeHalfAxis.rectTransform.sizeDelta = new Vector2(length, Mathf.Max(7f, _guideSize * 0.008f));
      _activeHalfAxis.rectTransform.anchoredPosition = target * 0.5f;
      _activeHalfAxis.rectTransform.localRotation = Quaternion.Euler(0f, 0f,
        Mathf.Atan2(target.y, target.x) * Mathf.Rad2Deg);
    }

    private void UpdateEndpoints(int currentAxis, Vector2 currentPosition)
    {
      var directions = EndpointDirections;
      var targetIndex = -1;
      if (currentPosition.sqrMagnitude > 12f)
      {
        var normalized = currentPosition.normalized;
        var best = -2f;
        for (var index = 0; index < directions.Length; index++)
        {
          var score = Vector2.Dot(normalized, directions[index]);
          if (score <= best) continue;
          best = score;
          targetIndex = index;
        }
      }
      for (var index = 0; index < _endpoints.Count; index++)
      {
        var axis = AxisForEndpoint(index);
        _endpoints[index].color = index == targetIndex
          ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.94f)
          : axis < currentAxis
            ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.30f)
            : axis == currentAxis
              ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.58f)
              : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextSecondary, 0.20f);
      }
    }

    private void MovePupils(Vector2 normalizedDirection, bool immediate)
    {
      normalizedDirection = Vector2.ClampMagnitude(normalizedDirection, 1f);
      var pupilSize = _leftPupil.rect.width;
      var eyeSize = _leftEye.rect.size;
      var maxX = Mathf.Max(0f, (eyeSize.x - pupilSize) * 0.5f - 3f);
      var maxY = Mathf.Max(0f, (eyeSize.y - pupilSize) * 0.5f - 3f);
      var range = new Vector2(maxX, maxY) * _pupilRangeRatio / 0.24f;
      range.x = Mathf.Min(range.x, maxX);
      range.y = Mathf.Min(range.y, maxY);
      var target = Vector2.Scale(normalizedDirection, range);
      var blend = immediate ? 1f : 0.34f;
      _leftPupil.anchoredPosition = Vector2.Lerp(_leftPupil.anchoredPosition, target, blend);
      _rightPupil.anchoredPosition = Vector2.Lerp(_rightPupil.anchoredPosition, target, blend);
    }

    private static Vector2 PilotDirection(int axis, float progress)
    {
      var first = axis == 0 ? Vector2.up : axis == 1 ? Vector2.left : axis == 2
        ? new Vector2(-0.7071068f, 0.7071068f) : new Vector2(0.7071068f, 0.7071068f);
      var second = -first;
      progress = Mathf.Clamp01(progress);
      if (progress < 0.25f) return Vector2.Lerp(Vector2.zero, first, progress * 4f);
      if (progress < 0.5f) return Vector2.Lerp(first, Vector2.zero, (progress - 0.25f) * 4f);
      if (progress < 0.75f) return Vector2.Lerp(Vector2.zero, second, (progress - 0.5f) * 4f);
      return Vector2.Lerp(second, Vector2.zero, (progress - 0.75f) * 4f);
    }

    private static string PromptForAxis(int axis)
    {
      return axis == 0 ? "LOOK UP AND DOWN" : axis == 1 ? "LOOK LEFT AND RIGHT" : "FOLLOW THE DIAGONAL";
    }

    private static int AxisForEndpoint(int endpoint)
    {
      return endpoint < 2 ? 0 : endpoint < 4 ? 1 : endpoint < 6 ? 2 : 3;
    }

    private static Vector2[] EndpointDirections => new[]
    {
      Vector2.up, Vector2.down, Vector2.left, Vector2.right,
      new Vector2(-0.7071068f, 0.7071068f), new Vector2(0.7071068f, -0.7071068f),
      new Vector2(0.7071068f, 0.7071068f), new Vector2(-0.7071068f, -0.7071068f),
    };

    private void OnRectTransformDimensionsChange()
    {
      if (_root != null) UpdateLayoutFromRect();
    }

    private void UpdateLayoutFromRect()
    {
      var size = _root.rect.size;
      if (size.x < 100f || size.y < 100f) return;
      if ((size - _layoutSize).sqrMagnitude < 1f) return;
      LayoutForSize(size);
    }

    private void LayoutForSize(Vector2 safeSize)
    {
      safeSize.x = Mathf.Max(320f, safeSize.x);
      safeSize.y = Mathf.Max(560f, safeSize.y);
      _layoutSize = safeSize;
      _guideSize = Mathf.Min(safeSize.x * Mathf.Max(MinimumGuideWidthRatio, _guideWidthRatio),
        safeSize.y * DefaultGuideHeightRatio);
      _headWidth = safeSize.x * Mathf.Clamp(_workerHeadWidthRatio, 0.32f, 0.42f);
      _eyeWidth = safeSize.x * Mathf.Clamp(_eyeWidthRatio, 0.07f, 0.11f);
      _guideRoot.sizeDelta = new Vector2(_guideSize, _guideSize);

      var headHeight = _headWidth * 0.92f;
      _workerHead.sizeDelta = new Vector2(_headWidth, headHeight);
      _workerHead.anchoredPosition = new Vector2(0f, _guideSize * 0.035f);
      var shoulderWidth = safeSize.x * Mathf.Clamp(_workerShoulderWidthRatio, 0.50f, 0.62f);
      _workerShoulders.sizeDelta = new Vector2(shoulderWidth, _guideSize * 0.24f);
      _workerShoulders.anchoredPosition = new Vector2(0f, -_guideSize * 0.22f);

      var eyeHeight = _eyeWidth * 0.58f;
      var eyeOffset = _headWidth * 0.19f;
      FirstLevelUiFactory.SetRect(_leftEye, new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f),
        new Vector2(0.5f, 0.5f), new Vector2(-eyeOffset, 0f), new Vector2(_eyeWidth, eyeHeight));
      FirstLevelUiFactory.SetRect(_rightEye, new Vector2(0.5f, 0.55f), new Vector2(0.5f, 0.55f),
        new Vector2(0.5f, 0.5f), new Vector2(eyeOffset, 0f), new Vector2(_eyeWidth, eyeHeight));
      var pupilDiameter = eyeHeight * 0.41f;
      FirstLevelUiFactory.SetRect(_leftPupil, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(pupilDiameter, pupilDiameter));
      FirstLevelUiFactory.SetRect(_rightPupil, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(pupilDiameter, pupilDiameter));

      var axisLength = _guideSize * 0.92f;
      var axisWidth = Mathf.Max(6f, _guideSize * 0.007f);
      for (var index = 0; index < _axes.Count; index++)
        FirstLevelUiFactory.SetRect(_axes[index].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
          new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(axisLength, axisWidth));

      var directions = EndpointDirections;
      var endpointSize = safeSize.x * DefaultEndpointWidthRatio;
      for (var index = 0; index < _endpoints.Count; index++)
        FirstLevelUiFactory.SetRect(_endpoints[index].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
          new Vector2(0.5f, 0.5f), directions[index] * (_guideSize * 0.46f), new Vector2(endpointSize, endpointSize));

      var guideDotSize = safeSize.x * DefaultGuideDotWidthRatio;
      FirstLevelUiFactory.SetRect(_pilotGuideDot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f), _pilotGuideDot.anchoredPosition, new Vector2(guideDotSize, guideDotSize));
      FirstLevelUiFactory.SetRect(_circleTrack.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * (_guideSize * 0.84f));
      FirstLevelUiFactory.SetRect(_guidedGuideDot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f), _guidedGuideDot.anchoredPosition, new Vector2(guideDotSize, guideDotSize));
      FirstLevelUiFactory.SetRect(_breathingRing.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
        new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * (_guideSize * 0.54f));
    }

    private void Update()
    {
      UpdateLayoutFromRect();
      var speed = _targetAlpha > _overlayGroup.alpha ? 3.4f : 2.5f;
      _overlayGroup.alpha = Mathf.MoveTowards(_overlayGroup.alpha, _targetAlpha, Time.unscaledDeltaTime * speed);
      _guideRoot.localScale = Vector3.Lerp(_guideRoot.localScale, Vector3.one,
        1f - Mathf.Exp(-Time.unscaledDeltaTime * 8f));
      if (_guidanceClosedPhase && _breathingRing != null)
      {
        var breath = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 1.6f);
        _breathingRing.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.96f, 1.045f, breath);
        _breathingRing.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary,
          Mathf.Lerp(0.16f, 0.30f, breath));
      }
      if (_targetAlpha <= 0f && _overlayGroup.alpha <= 0.001f && _root.gameObject.activeSelf)
      {
        _overlayGroup.blocksRaycasts = false;
        _root.gameObject.SetActive(false);
      }
    }
  }
}
