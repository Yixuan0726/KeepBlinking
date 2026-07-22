using KeepBlinking.Gameplay;
using UnityEngine;

namespace KeepBlinking.Tutorial
{
  public sealed class KeepBlinkingTutorialView : MonoBehaviour
  {
    private enum LoopPrompt
    {
      None,
      Blink,
      PushAway,
    }

    private const float BlinkLoopSeconds = 1.8f;
    private const float PushAwayLoopSeconds = 2.2f;
    private const float CrisisSuccessFeedbackSeconds = 0.8f;
    private const float CountdownStepSeconds = 0.7f;

    [SerializeField] private KeepBlinkingTutorialController _controller;
    [SerializeField] private EdgeOrbitHarvestMvp _gameplay;
    [SerializeField, Min(1)] private int _formalPushAwayPromptMinimumConvertedCount = 3;

    private LoopPrompt _loopPrompt;
    private float _loopStartedAt;
    private float _nextLoopCueAt;
    private bool _controllerSubscribed;
    private bool _gameplaySubscribed;
    private GUIStyle _calibrationHeaderStyle;
    private GUIStyle _countdownStyle;
    private bool _hasLockedRingPresentation;
    private float _crisisClosePromptStartedAt = -999f;
    private Vector2 _crisisSuccessCenter;
    private float _crisisSuccessRadius;
    private float _crisisSuccessStartedAt = -999f;
    private float _presentationSuspendedAt = -1f;
    private int _countdownNumber;
    private float _countdownNumberStartedAt = -999f;
    private bool _crisisCollectionPresentationPending;
    private bool _crisisPushReady;
    private bool _formalPushAwayPromptVisible;

    private static readonly Color MintColor = new Color(159f / 255f, 203f / 255f, 180f / 255f, 0.78f);
    private static readonly Color MintFaintColor = new Color(159f / 255f, 203f / 255f, 180f / 255f, 0.24f);
    private static readonly Color SandColor = new Color(203f / 255f, 191f / 255f, 155f / 255f, 0.92f);
    private static readonly Color SandFaintColor = new Color(203f / 255f, 191f / 255f, 155f / 255f, 0.54f);
    private static readonly Color WarmWhiteColor = new Color(242f / 255f, 244f / 255f, 234f / 255f, 0.94f);

    private void OnEnable()
    {
      ResolveDependencies();
      Subscribe();

      if (_controller != null && _controller.IsRunning && !_controller.IsInputSuspended)
      {
        ConfigureForState(_controller.State, false);
      }
    }

    private void OnDisable()
    {
      StopLoopPrompt();
      _formalPushAwayPromptVisible = false;
      _countdownNumber = 0;
      Unsubscribe();
    }

    public void SetController(KeepBlinkingTutorialController controller)
    {
      if (_controller == controller)
      {
        return;
      }

      Unsubscribe();
      _controller = controller;
      ResolveDependencies();
      if (isActiveAndEnabled)
      {
        Subscribe();
      }
    }

    private void Update()
    {
      if (_gameplay == null)
      {
        return;
      }

      UpdateFormalPushAwayPromptState();

      if (_controller == null ||
          !_controller.IsRunning ||
          _controller.IsInputSuspended)
      {
        return;
      }

      if (_loopPrompt == LoopPrompt.None)
      {
        if (ShouldStartCrisisPushAwayPrompt())
        {
          StartLoopPrompt(LoopPrompt.PushAway);
        }
        return;
      }

      if (_loopPrompt == LoopPrompt.Blink && _controller.State != KeepBlinkingTutorialState.WaitFirstConverted)
      {
        StopLoopPrompt();
        return;
      }

      if (_loopPrompt == LoopPrompt.PushAway &&
          (!IsPushAwayPromptState(_controller.State) || _controller.PushAwayTriggeredObserved))
      {
        StopLoopPrompt();
        return;
      }

      var now = Time.unscaledTime;
      if (now < _nextLoopCueAt)
      {
        return;
      }

      if (_loopPrompt == LoopPrompt.Blink)
      {
        _gameplay.PlayTutorialFeedback(TutorialFeedbackCue.BlinkLoop);
        _nextLoopCueAt = now + BlinkLoopSeconds;
      }
      else if (_loopPrompt == LoopPrompt.PushAway)
      {
        _gameplay.PlayTutorialFeedback(TutorialFeedbackCue.PushAwayLoop);
        _nextLoopCueAt = now + PushAwayLoopSeconds;
      }
    }

    private void OnGUI()
    {
      if (_gameplay != null && _gameplay.IsModuleUpgradeOpen)
      {
        return;
      }

      if (_gameplay != null && _gameplay.IsCalibrationActive)
      {
        DrawCalibrationHeader();
        if (_gameplay.IsCalibrationInputReady)
        {
          DrawCalibrationPrompt();
        }
        return;
      }

      if (_gameplay == null ||
          (_controller != null && _controller.IsRunning && _controller.IsInputSuspended))
      {
        return;
      }

      if (_gameplay.IsCrisisAwaitingClose &&
          !_gameplay.isEyesClosed)
      {
        DrawGlobalCrisisClosePrompt();
        return;
      }

      if (IsCrisisSuccessFeedbackActive())
      {
        DrawCrisisSuccessFeedback();
        return;
      }

      if (_crisisCollectionPresentationPending && _crisisPushReady)
      {
        DrawPushAwayPrompt();
        return;
      }

      if (_formalPushAwayPromptVisible)
      {
        DrawPushAwayPrompt();
        return;
      }

      if (_controller == null || !_controller.IsRunning)
      {
        return;
      }

      if (_controller.State == KeepBlinkingTutorialState.WaitFirstConverted)
      {
        DrawBlinkPrompt();
      }
      else if (_controller.State == KeepBlinkingTutorialState.WaitFirstCollected &&
               !_controller.PushAwayTriggeredObserved)
      {
        DrawPushAwayPrompt();
      }
      else if (_controller.State == KeepBlinkingTutorialState.WaitCrisisCollected)
      {
        if (_controller.PushAwayReadyObserved && !_controller.PushAwayTriggeredObserved)
        {
          DrawPushAwayPrompt();
        }
      }
      else if (_controller.State == KeepBlinkingTutorialState.Countdown)
      {
        DrawCountdown();
      }
    }

    private void ResolveDependencies()
    {
      if (_controller == null)
      {
        _controller = FindFirstObjectByType<KeepBlinkingTutorialController>();
      }

      if (_gameplay == null)
      {
        _gameplay = FindFirstObjectByType<EdgeOrbitHarvestMvp>();
      }
    }

    private void Subscribe()
    {
      if (!_controllerSubscribed && _controller != null)
      {
        _controller.StateChanged += HandleStateChanged;
        _controller.InputSuspensionChanged += HandleInputSuspensionChanged;
        _controller.CountdownValueChanged += HandleCountdownValueChanged;
        _controllerSubscribed = true;
      }

      if (!_gameplaySubscribed && _gameplay != null)
      {
        _gameplay.PushAwayCollectionReady += HandlePushAwayCollectionReady;
        _gameplay.PushAwayTriggered += HandlePushAwayTriggered;
        _gameplay.CrisisStarted += HandleCrisisStarted;
        _gameplay.CrisisReleaseInterrupted += HandleCrisisReleaseInterrupted;
        _gameplay.ReopenReleaseCompleted += HandleReopenReleaseCompleted;
        _gameplay.CrisisExperienceCollectionCompleted += HandleCrisisExperienceCollectionCompleted;
        _gameplaySubscribed = true;
      }
    }

    private void Unsubscribe()
    {
      if (_controllerSubscribed && _controller != null)
      {
        _controller.StateChanged -= HandleStateChanged;
        _controller.InputSuspensionChanged -= HandleInputSuspensionChanged;
        _controller.CountdownValueChanged -= HandleCountdownValueChanged;
      }

      if (_gameplaySubscribed && _gameplay != null)
      {
        _gameplay.PushAwayCollectionReady -= HandlePushAwayCollectionReady;
        _gameplay.PushAwayTriggered -= HandlePushAwayTriggered;
        _gameplay.CrisisStarted -= HandleCrisisStarted;
        _gameplay.CrisisReleaseInterrupted -= HandleCrisisReleaseInterrupted;
        _gameplay.ReopenReleaseCompleted -= HandleReopenReleaseCompleted;
        _gameplay.CrisisExperienceCollectionCompleted -= HandleCrisisExperienceCollectionCompleted;
      }

      _controllerSubscribed = false;
      _gameplaySubscribed = false;
    }

    private void HandleStateChanged(KeepBlinkingTutorialState previousState, KeepBlinkingTutorialState nextState)
    {
      StopLoopPrompt();
      if (nextState != KeepBlinkingTutorialState.WaitEyesClosed)
      {
        ResetCrisisClosePromptPresentation();
      }
      if (nextState != KeepBlinkingTutorialState.Countdown)
      {
        _countdownNumber = 0;
      }
      if (_controller == null ||
          !_controller.IsRunning ||
          _controller.IsInputSuspended ||
          _gameplay == null)
      {
        return;
      }

      ConfigureForState(nextState, true);
    }

    private void HandleInputSuspensionChanged(bool suspended)
    {
      StopLoopPrompt();
      if (suspended)
      {
        _presentationSuspendedAt = Time.unscaledTime;
        return;
      }

      if (_presentationSuspendedAt >= 0f && _crisisSuccessStartedAt > -900f)
      {
        _crisisSuccessStartedAt += Mathf.Max(0f, Time.unscaledTime - _presentationSuspendedAt);
      }
      _presentationSuspendedAt = -1f;
      if (!suspended && _controller != null && _controller.IsRunning)
      {
        ConfigureForState(_controller.State, false);
      }
    }

    private void HandleCountdownValueChanged(int number)
    {
      _countdownNumber = Mathf.Clamp(number, 0, 3);
      _countdownNumberStartedAt = Time.unscaledTime;
      if (_countdownNumber > 0 &&
          _controller != null &&
          _controller.IsRunning &&
          !_controller.IsInputSuspended)
      {
        _gameplay?.PlayTutorialFeedback(TutorialFeedbackCue.CountdownBeat);
      }
    }

    private void HandlePushAwayTriggered()
    {
      _formalPushAwayPromptVisible = false;

      if (_crisisCollectionPresentationPending)
      {
        _crisisPushReady = false;
      }

      if (_loopPrompt == LoopPrompt.PushAway)
      {
        StopLoopPrompt();
      }
    }

    private void HandlePushAwayCollectionReady()
    {
      if (!_crisisCollectionPresentationPending)
      {
        return;
      }

      _crisisPushReady = true;
      _loopStartedAt = Time.unscaledTime;
    }

    private void HandleCrisisReleaseInterrupted()
    {
      ResetCrisisClosePromptPresentation();
    }

    private void HandleCrisisStarted(int spawnCount)
    {
      if (spawnCount > 0)
      {
        _formalPushAwayPromptVisible = false;
        ResetCrisisClosePromptPresentation();
      }
    }

    private void UpdateFormalPushAwayPromptState()
    {
      var isBossTransitionSettlement = _gameplay.IsFirstLevelBossTransitionActive &&
                                       _gameplay.PendingConvertedExperienceCount > 0;
      var shouldShow = !_gameplay.IsTutorialModeEnabled &&
                       (_controller == null || !_controller.IsRunning) &&
                       _gameplay.IsFaceInputAvailable &&
                       !_gameplay.IsCrisisAwaitingClose &&
                       !_gameplay.IsEyesClosedFreezeActive &&
                       _gameplay.IsPushAwayCollectionReady &&
                       (isBossTransitionSettlement ||
                        _gameplay.PendingConvertedExperienceCount >=
                        Mathf.Max(1, _formalPushAwayPromptMinimumConvertedCount));

      if (shouldShow == _formalPushAwayPromptVisible)
      {
        return;
      }

      _formalPushAwayPromptVisible = shouldShow;
      if (shouldShow)
      {
        _loopStartedAt = Time.unscaledTime;
      }
    }

    private void HandleReopenReleaseCompleted(int convertedCount)
    {
      if (convertedCount <= 0)
      {
        return;
      }

      _crisisCollectionPresentationPending = true;
      _crisisPushReady = false;
      BeginCrisisSuccessFeedback();
    }

    private void HandleCrisisExperienceCollectionCompleted(int collectedCount)
    {
      if (collectedCount <= 0)
      {
        return;
      }

      _crisisCollectionPresentationPending = false;
      _crisisPushReady = false;
    }

    private void ConfigureForState(KeepBlinkingTutorialState state, bool playEntryCue)
    {
      switch (state)
      {
        case KeepBlinkingTutorialState.WaitFirstConverted:
          if (playEntryCue)
          {
            _gameplay.PlayTutorialFeedback(TutorialFeedbackCue.Focus);
          }
          StartLoopPrompt(LoopPrompt.Blink);
          break;
        case KeepBlinkingTutorialState.WaitFirstPushAway:
          if (playEntryCue)
          {
            _gameplay.PlayTutorialFeedback(TutorialFeedbackCue.Converted);
          }
          break;
        case KeepBlinkingTutorialState.WaitFirstCollected:
          if (!_controller.PushAwayTriggeredObserved)
          {
            StartLoopPrompt(LoopPrompt.PushAway);
          }
          break;
        case KeepBlinkingTutorialState.WaitEyesClosed:
          if (playEntryCue)
          {
            _gameplay.PlayTutorialFeedback(TutorialFeedbackCue.ExperienceComplete);
          }
          break;
        case KeepBlinkingTutorialState.WaitCrisisCollected:
          if (ShouldStartCrisisPushAwayPrompt())
          {
            StartLoopPrompt(LoopPrompt.PushAway);
          }
          break;
        case KeepBlinkingTutorialState.Countdown:
          if (playEntryCue)
          {
            _gameplay.PlayTutorialFeedback(TutorialFeedbackCue.ExperienceComplete);
          }
          break;
      }
    }

    private bool ShouldStartCrisisPushAwayPrompt()
    {
      return _controller != null &&
             _controller.State == KeepBlinkingTutorialState.WaitCrisisCollected &&
             _controller.PushAwayReadyObserved &&
             !_controller.PushAwayTriggeredObserved &&
             !IsCrisisSuccessFeedbackActive();
    }

    private static bool IsPushAwayPromptState(KeepBlinkingTutorialState state)
    {
      return state == KeepBlinkingTutorialState.WaitFirstCollected ||
             state == KeepBlinkingTutorialState.WaitCrisisCollected;
    }

    private bool IsCrisisSuccessFeedbackActive()
    {
      return Time.unscaledTime - _crisisSuccessStartedAt < CrisisSuccessFeedbackSeconds;
    }

    private void BeginCrisisSuccessFeedback()
    {
      _crisisSuccessStartedAt = Time.unscaledTime;
      if (!TryGetLastPurificationAnchorGuiPresentation(out _crisisSuccessCenter, out _crisisSuccessRadius))
      {
        _crisisSuccessCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.46f);
        _crisisSuccessRadius = 42f;
      }
    }

    private void StartLoopPrompt(LoopPrompt prompt)
    {
      _loopPrompt = prompt;
      _loopStartedAt = Time.unscaledTime;
      _nextLoopCueAt = prompt == LoopPrompt.Blink
        ? _loopStartedAt + BlinkLoopSeconds * 0.72f
        : _loopStartedAt + 0.12f;
    }

    private void StopLoopPrompt()
    {
      _loopPrompt = LoopPrompt.None;
      _nextLoopCueAt = float.PositiveInfinity;
      _gameplay?.StopTutorialFeedback();
    }

    private void DrawBlinkPrompt()
    {
      if (!TryGetTutorialTargetGuiPresentation(out var targetCenter, out var targetRadius))
      {
        return;
      }

      var phase = Mathf.Repeat((Time.unscaledTime - _loopStartedAt) / BlinkLoopSeconds, 1f);
      var closure = GetBlinkClosure(phase);

      var ringRadius = Mathf.Clamp(targetRadius * 1.55f, 42f, 78f);
      DrawCircle(targetCenter, ringRadius, 64, MintFaintColor, 1.25f);
      for (var i = 0; i < 4; i++)
      {
        DrawArc(targetCenter, ringRadius, 17f + i * 90f, 56f, 14, MintColor, 1.8f);
      }

      DrawBlinkEyeBesideTarget(targetCenter, targetRadius, ringRadius, closure);
    }

    private void DrawGlobalCrisisClosePrompt()
    {
      if (!_hasLockedRingPresentation)
      {
        _hasLockedRingPresentation = true;
        _crisisClosePromptStartedAt = Time.unscaledTime;
      }

      var iconCenter = new Vector2(Screen.width * 0.5f, Screen.height * (1f - 0.48f));
      var iconHalfWidth = Mathf.Clamp(Mathf.Min(Screen.width, Screen.height) * 0.045f, 24f, 42f);
      var elapsed = Mathf.Max(0f, Time.unscaledTime - _crisisClosePromptStartedAt);
      var closure = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 1.25f));
      var holdPulse = closure * (0.5f + Mathf.Sin(Time.unscaledTime * 2.1f) * 0.5f);
      DrawEyeAtCenter(iconCenter, iconHalfWidth, closure, SandColor, SandFaintColor);
      var holdRadius = iconHalfWidth * 1.42f;
      var holdProgress = Mathf.Clamp01(elapsed / 2.8f);
      DrawArc(iconCenter, holdRadius, -90f, 360f * holdProgress, 40, MintColor, 1.45f);
      if (closure >= 0.99f)
      {
        DrawCircle(
          iconCenter,
          holdRadius + Mathf.Lerp(1f, 4f, holdPulse),
          36,
          new Color(MintColor.r, MintColor.g, MintColor.b, Mathf.Lerp(0.12f, 0.32f, holdPulse)),
          1.2f);
      }
    }

    private void ResetCrisisClosePromptPresentation()
    {
      _hasLockedRingPresentation = false;
      _crisisClosePromptStartedAt = -999f;
    }

    private void DrawCrisisSuccessFeedback()
    {
      var progress = Mathf.Clamp01(
        (Time.unscaledTime - _crisisSuccessStartedAt) / CrisisSuccessFeedbackSeconds);
      var baseRadius = Mathf.Clamp(_crisisSuccessRadius, 30f, 58f);
      var rippleRadius = Mathf.Lerp(baseRadius * 0.75f, baseRadius * 3.3f, Mathf.SmoothStep(0f, 1f, progress));
      var rippleAlpha = (1f - Mathf.SmoothStep(0.15f, 1f, progress)) * 0.62f;
      DrawCircle(
        _crisisSuccessCenter,
        rippleRadius,
        72,
        new Color(MintColor.r, MintColor.g, MintColor.b, rippleAlpha),
        2f);

      var collapse = Mathf.Clamp01(progress / 0.42f);
      var ringRadius = Mathf.Lerp(baseRadius * 1.62f, baseRadius * 0.34f, Mathf.SmoothStep(0f, 1f, collapse));
      DrawIrisRing(
        _crisisSuccessCenter,
        ringRadius,
        new Color(MintColor.r, MintColor.g, MintColor.b, (1f - collapse) * 0.86f),
        new Color(MintFaintColor.r, MintFaintColor.g, MintFaintColor.b, (1f - collapse) * 0.5f),
        0.75f + collapse * 0.25f);

      var iconAlpha = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.14f, progress)) *
                      (1f - Mathf.SmoothStep(0.46f, 1f, progress));
      DrawLightPetalIcon(_crisisSuccessCenter, baseRadius * 0.6f, iconAlpha);
    }

    private void DrawCountdown()
    {
      if (_countdownNumber <= 0)
      {
        return;
      }

      var progress = Mathf.Clamp01(
        (Time.unscaledTime - _countdownNumberStartedAt) / CountdownStepSeconds);
      var scale = Mathf.Lerp(0.78f, 1.08f, Mathf.SmoothStep(0f, 1f, Mathf.Min(1f, progress / 0.34f)));
      var alpha = 1f - Mathf.SmoothStep(0.55f, 1f, progress);
      var fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.width * 0.18f * scale, 64f, 150f));

      if (_countdownStyle == null)
      {
        _countdownStyle = new GUIStyle(GUI.skin.label)
        {
          alignment = TextAnchor.MiddleCenter,
          fontStyle = FontStyle.Normal,
        };
      }

      _countdownStyle.fontSize = fontSize;
      _countdownStyle.normal.textColor = new Color(
        WarmWhiteColor.r,
        WarmWhiteColor.g,
        WarmWhiteColor.b,
        alpha);
      var rectSize = Mathf.Max(180f, fontSize * 1.7f);
      GUI.Label(
        new Rect(
          Screen.width * 0.5f - rectSize * 0.5f,
          Screen.height * 0.5f - rectSize * 0.5f,
          rectSize,
          rectSize),
        _countdownNumber.ToString(),
        _countdownStyle);
    }

    private static void DrawIrisRing(
      Vector2 center,
      float radius,
      Color arcColor,
      Color circleColor,
      float scale)
    {
      var safeRadius = Mathf.Max(8f, radius * scale);
      DrawCircle(center, safeRadius, 64, circleColor, 1.1f);
      for (var i = 0; i < 4; i++)
      {
        DrawArc(center, safeRadius, 17f + i * 90f, 56f, 14, arcColor, 1.75f);
      }
    }

    private static void DrawLightPetalIcon(Vector2 center, float radius, float alpha)
    {
      if (alpha <= 0.001f)
      {
        return;
      }

      var color = new Color(WarmWhiteColor.r, WarmWhiteColor.g, WarmWhiteColor.b, alpha * 0.86f);
      var mint = new Color(MintColor.r, MintColor.g, MintColor.b, alpha * 0.72f);
      DrawCircle(center, Mathf.Max(2f, radius * 0.1f), 18, color, 2f);
      for (var i = 0; i < 6; i++)
      {
        var angle = (i * 60f - 90f) * Mathf.Deg2Rad;
        var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
        var tangent = new Vector2(-direction.y, direction.x);
        var inner = center + direction * radius * 0.28f;
        var tip = center + direction * radius;
        var shoulder = center + direction * radius * 0.58f;
        DrawLine(inner, shoulder + tangent * radius * 0.2f, 1.7f, mint);
        DrawLine(shoulder + tangent * radius * 0.2f, tip, 1.7f, color);
        DrawLine(tip, shoulder - tangent * radius * 0.2f, 1.7f, color);
        DrawLine(shoulder - tangent * radius * 0.2f, inner, 1.7f, mint);
      }
    }

    private void DrawCalibrationPrompt()
    {
      if (!TryGetCalibrationTargetGuiPresentation(out var targetCenter, out var targetRadius))
      {
        return;
      }

      var phase = Mathf.Repeat(Time.unscaledTime / BlinkLoopSeconds, 1f);
      var closure = GetBlinkClosure(phase);
      var anchorRadius = Mathf.Clamp(targetRadius * 1.25f, 32f, 58f);
      DrawBlinkEyeBesideTarget(targetCenter, targetRadius, anchorRadius, closure);
    }

    private void DrawCalibrationHeader()
    {
      if (_calibrationHeaderStyle == null)
      {
        _calibrationHeaderStyle = new GUIStyle(GUI.skin.label)
        {
          alignment = TextAnchor.UpperCenter,
          fontSize = Mathf.RoundToInt(Mathf.Clamp(Screen.width / 390f * 15f, 14f, 19f)),
          fontStyle = FontStyle.Bold,
          normal = { textColor = new Color(159f / 255f, 203f / 255f, 180f / 255f, 0.72f) },
        };
      }

      var safeArea = Screen.safeArea;
      var safeTop = Screen.height - safeArea.yMax;
      GUI.Label(new Rect(safeArea.xMin, safeTop + 10f, safeArea.width, 28f), "CALIBRATION", _calibrationHeaderStyle);
    }

    private static float GetBlinkClosure(float phase)
    {
      return phase < 0.58f
        ? 0f
        : phase < 0.72f
          ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 0.72f, phase))
          : phase < 0.86f
            ? Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.72f, 0.86f, phase))
            : 0f;
    }

    private static void DrawBlinkEyeBesideTarget(
      Vector2 targetCenter,
      float targetRadius,
      float anchorRadius,
      float closure)
    {
      var iconHalfWidth = Mathf.Clamp(targetRadius * 0.48f, 18f, 27f);
      var iconCenter = GetEyeIconCenter(targetCenter, targetRadius, anchorRadius);
      DrawEyeAtCenter(iconCenter, iconHalfWidth, closure, SandColor, SandFaintColor);
    }

    private static void DrawEyeAtCenter(
      Vector2 iconCenter,
      float iconHalfWidth,
      float closure,
      Color outlineColor,
      Color irisColor)
    {
      var halfHeight = Mathf.Lerp(iconHalfWidth * 0.48f, 1.8f, closure);
      var left = new Vector2(iconCenter.x - iconHalfWidth, iconCenter.y);
      var right = new Vector2(iconCenter.x + iconHalfWidth, iconCenter.y);
      var upper = new Vector2(iconCenter.x, iconCenter.y - halfHeight);
      var lower = new Vector2(iconCenter.x, iconCenter.y + halfHeight);

      DrawLine(left, upper, 2.4f, outlineColor);
      DrawLine(upper, right, 2.4f, outlineColor);
      DrawLine(left, lower, 2.4f, outlineColor);
      DrawLine(lower, right, 2.4f, outlineColor);

      if (closure < 0.72f)
      {
        DrawCircle(iconCenter, Mathf.Lerp(iconHalfWidth * 0.22f, 2f, closure), 18, irisColor, 2f);
      }
    }

    private static Vector2 GetEyeIconCenter(Vector2 targetCenter, float targetRadius, float anchorRadius)
    {
      var iconHalfWidth = Mathf.Clamp(targetRadius * 0.48f, 18f, 27f);
      var iconCenter = targetCenter.x + anchorRadius + iconHalfWidth + 14f < Screen.width - 12f
        ? targetCenter + new Vector2(anchorRadius + iconHalfWidth + 14f, 0f)
        : targetCenter - new Vector2(anchorRadius + iconHalfWidth + 14f, 0f);
      iconCenter.y = Mathf.Clamp(iconCenter.y, 24f, Screen.height - 24f);
      return iconCenter;
    }

    private void DrawPushAwayPrompt()
    {
      Vector2 targetCenter;
      float targetRadius;
      var hasAnchor = _crisisCollectionPresentationPending ||
                      (_controller != null &&
                       _controller.State == KeepBlinkingTutorialState.WaitCrisisCollected)
        ? TryGetLastPurificationAnchorGuiPresentation(out targetCenter, out targetRadius)
        : TryGetTutorialTargetGuiPresentation(out targetCenter, out targetRadius);
      if (!hasAnchor)
      {
        targetCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.42f);
        targetRadius = 36f;
      }

      var phase = Mathf.Repeat((Time.unscaledTime - _loopStartedAt) / PushAwayLoopSeconds, 1f);
      var travel = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phase / 0.76f));
      var guideDirection = targetCenter.x > Screen.width * 0.55f ? -1f : 1f;
      var guideCenter = targetCenter + new Vector2(guideDirection * (targetRadius + 78f), 0f);
      guideCenter.x = Mathf.Clamp(guideCenter.x, 74f, Screen.width - 74f);
      guideCenter.y = Mathf.Clamp(guideCenter.y, 58f, Screen.height - 58f);
      var faceCenter = guideCenter - new Vector2(guideDirection * 36f, 0f);
      var phoneCenter = guideCenter + new Vector2(guideDirection * Mathf.Lerp(22f, 54f, travel), 0f);

      DrawCircle(faceCenter, 19f, 24, MintColor, 2.2f);
      DrawLine(faceCenter + new Vector2(-7f, -4f), faceCenter + new Vector2(-3f, -4f), 2f, MintFaintColor);
      DrawLine(faceCenter + new Vector2(3f, -4f), faceCenter + new Vector2(7f, -4f), 2f, MintFaintColor);
      DrawLine(faceCenter + new Vector2(-5f, 7f), faceCenter + new Vector2(5f, 7f), 1.8f, MintFaintColor);

      var phoneRect = new Rect(phoneCenter.x - 11f, phoneCenter.y - 23f, 22f, 46f);
      DrawRectOutline(phoneRect, MintColor, 2.2f);
      DrawLine(new Vector2(phoneRect.x + 7f, phoneRect.yMax - 5f), new Vector2(phoneRect.xMax - 7f, phoneRect.yMax - 5f), 1.5f, MintFaintColor);

      var arrowStart = faceCenter + new Vector2(guideDirection * 24f, 0f);
      var arrowEnd = phoneCenter - new Vector2(guideDirection * 18f, 0f);
      DrawLine(arrowStart, arrowEnd, 1.8f, MintFaintColor);
      DrawLine(arrowEnd, arrowEnd + new Vector2(-guideDirection * 6f, -5f), 1.8f, MintFaintColor);
      DrawLine(arrowEnd, arrowEnd + new Vector2(-guideDirection * 6f, 5f), 1.8f, MintFaintColor);
    }

    private bool TryGetTutorialTargetGuiPresentation(out Vector2 guiPosition, out float screenRadius)
    {
      guiPosition = Vector2.zero;
      screenRadius = 0f;
      if (_gameplay == null || !_gameplay.TryGetTutorialTargetScreenPresentation(out var screenPosition, out screenRadius))
      {
        return false;
      }

      guiPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
      return true;
    }

    private bool TryGetLastPurificationAnchorGuiPresentation(out Vector2 guiPosition, out float screenRadius)
    {
      guiPosition = Vector2.zero;
      screenRadius = 0f;
      if (_gameplay == null ||
          !_gameplay.TryGetLastPurificationAnchorScreenPresentation(out var screenPosition, out screenRadius))
      {
        return false;
      }

      guiPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
      return true;
    }

    private bool TryGetCalibrationTargetGuiPresentation(out Vector2 guiPosition, out float screenRadius)
    {
      guiPosition = Vector2.zero;
      screenRadius = 0f;
      if (_gameplay == null || !_gameplay.TryGetCalibrationTargetScreenPresentation(out var screenPosition, out screenRadius))
      {
        return false;
      }

      guiPosition = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
      return true;
    }

    private static void DrawArc(
      Vector2 center,
      float radius,
      float startDegrees,
      float sweepDegrees,
      int segments,
      Color color,
      float thickness)
    {
      var startRadians = startDegrees * Mathf.Deg2Rad;
      var previous = center + new Vector2(Mathf.Cos(startRadians), Mathf.Sin(startRadians)) * radius;
      for (var i = 1; i <= segments; i++)
      {
        var angle = (startDegrees + sweepDegrees * i / segments) * Mathf.Deg2Rad;
        var next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        DrawLine(previous, next, thickness, color);
        previous = next;
      }
    }

    private static void DrawRectOutline(Rect rect, Color color, float thickness)
    {
      DrawLine(new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), thickness, color);
      DrawLine(new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), thickness, color);
      DrawLine(new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax), thickness, color);
      DrawLine(new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMin, rect.yMin), thickness, color);
    }

    private static void DrawCircle(Vector2 center, float radius, int segments, Color color, float thickness)
    {
      var previous = center + Vector2.right * radius;
      for (var i = 1; i <= segments; i++)
      {
        var angle = Mathf.PI * 2f * i / segments;
        var next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        DrawLine(previous, next, thickness, color);
        previous = next;
      }
    }

    private static void DrawLine(Vector2 start, Vector2 end, float thickness, Color color)
    {
      var delta = end - start;
      var length = delta.magnitude;
      if (length <= 0.01f)
      {
        return;
      }

      var previousMatrix = GUI.matrix;
      var previousColor = GUI.color;
      var pivot = (start + end) * 0.5f;
      GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, pivot);
      GUI.color = color;
      GUI.DrawTexture(new Rect(pivot.x - length * 0.5f, pivot.y - thickness * 0.5f, length, thickness), Texture2D.whiteTexture);
      GUI.matrix = previousMatrix;
      GUI.color = previousColor;
    }
  }
}
