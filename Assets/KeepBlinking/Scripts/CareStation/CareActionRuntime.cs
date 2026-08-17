using System;
using UnityEngine;

namespace KeepBlinking.CareStation
{
  [Serializable]
  public struct CareActionConfiguration
  {
    public float screenDownDemoSeconds;
    public float screenDownDurationSeconds;
    public float screenDownHoldSeconds;
    public float screenReturnHoldSeconds;
    public float closedEyeDurationSeconds;
    public float closeStartHoldSeconds;
    public float reopenHoldSeconds;
    public float distanceDeadZone;
    public float distanceCompleteThreshold;
    public float distanceStepHoldSeconds;
    public float distanceProgressFallSeconds;
    public float focusStepTransitionSeconds;
    public float guidedPreviewSeconds;
    public float guidedClockwiseSeconds;
    public float guidedPauseSeconds;
    public float guidedCounterClockwiseSeconds;
    public float guidedRelaxSeconds;

    public static CareActionConfiguration Default => new CareActionConfiguration
    {
      screenDownDemoSeconds = 1.2f,
      screenDownDurationSeconds = 20f,
      screenDownHoldSeconds = 0.5f,
      screenReturnHoldSeconds = 0.4f,
      closedEyeDurationSeconds = 45f,
      closeStartHoldSeconds = 1.5f,
      reopenHoldSeconds = 0.5f,
      distanceDeadZone = 0.02f,
      distanceCompleteThreshold = 0.06f,
      distanceStepHoldSeconds = 0.25f,
      distanceProgressFallSeconds = 0.25f,
      focusStepTransitionSeconds = 0.4f,
      guidedPreviewSeconds = 4f,
      guidedClockwiseSeconds = 8f,
      guidedPauseSeconds = 2f,
      guidedCounterClockwiseSeconds = 8f,
      guidedRelaxSeconds = 5f,
    };

    public void Sanitize()
    {
      screenDownDemoSeconds = Mathf.Max(0.1f, screenDownDemoSeconds);
      screenDownDurationSeconds = Mathf.Max(1f, screenDownDurationSeconds);
      screenDownHoldSeconds = Mathf.Max(0.1f, screenDownHoldSeconds);
      screenReturnHoldSeconds = Mathf.Max(0.1f, screenReturnHoldSeconds);
      closedEyeDurationSeconds = Mathf.Max(1f, closedEyeDurationSeconds);
      closeStartHoldSeconds = Mathf.Max(0.1f, closeStartHoldSeconds);
      reopenHoldSeconds = Mathf.Max(0.1f, reopenHoldSeconds);
      distanceDeadZone = Mathf.Clamp(distanceDeadZone, 0.005f, 0.05f);
      distanceCompleteThreshold = Mathf.Clamp(distanceCompleteThreshold, distanceDeadZone + 0.005f, 0.15f);
      distanceStepHoldSeconds = Mathf.Clamp(distanceStepHoldSeconds, 0.05f, 1f);
      distanceProgressFallSeconds = Mathf.Clamp(distanceProgressFallSeconds, 0.05f, 1f);
      focusStepTransitionSeconds = Mathf.Clamp(focusStepTransitionSeconds, 0f, 2f);
      guidedPreviewSeconds = Mathf.Max(0.1f, guidedPreviewSeconds);
      guidedClockwiseSeconds = Mathf.Max(0.1f, guidedClockwiseSeconds);
      guidedPauseSeconds = Mathf.Max(0.1f, guidedPauseSeconds);
      guidedCounterClockwiseSeconds = Mathf.Max(0.1f, guidedCounterClockwiseSeconds);
      guidedRelaxSeconds = Mathf.Max(0.1f, guidedRelaxSeconds);
    }
  }

  /// <summary>
  /// Deterministic care-action state machine. It never creates bottles, changes
  /// station resources, or invokes collection. Unity inputs are supplied by the
  /// runner so the same rules can be restored and tested without a camera.
  /// </summary>
  public sealed class CareActionRuntime
  {
    private CareActionConfiguration _config;
    private CareActionSaveData _data;
    private bool _manualPause;
    private CareRelativeDistanceStep _focusDistanceStep;

    public CareActionSaveData Data => _data;
    public CareActionType ActionType => _data != null ? _data.actionType : CareActionType.None;
    public CareActionStage Stage => _data != null ? _data.stage : CareActionStage.Cancelled;
    public CareActionInternalPhase Phase => _data != null ? _data.internalPhase : CareActionInternalPhase.None;
    public CareActionPauseReason PauseReason => _data != null ? _data.pauseReason : CareActionPauseReason.None;
    public bool IsRunning => _data != null && ActionType != CareActionType.None &&
                             Stage != CareActionStage.Completed && Stage != CareActionStage.Cancelled;
    public bool RequiresCamera => ActionType == CareActionType.ClosedEyeRest ||
                                  ActionType == CareActionType.FocusShift ||
                                  ActionType == CareActionType.GuidedEyeCircles;
    public bool RequiresDevicePose => ActionType == CareActionType.ScreenDown;
    public string DisplayName => DisplayNameFor(ActionType);
    public string Prompt => Stage == CareActionStage.Completed || Stage == CareActionStage.Cancelled
      ? string.Empty
      : PromptFor(Phase, PauseReason);
    public float Progress => CalculateProgress();
    public float RemainingSeconds => CalculateRemainingSeconds();
    public int RemainingSteps => ActionType == CareActionType.FocusShift
      ? Mathf.Max(0, 4 - Mathf.Clamp(_data.focusTargetStep, 0, 4))
      : 0;
    public float DirectionProgress => ActionType == CareActionType.FocusShift && _data != null
      ? Mathf.Clamp01(_data.distanceDirectionProgress)
      : 0f;
    public CareDistanceDirection ExpectedDistanceDirection => ActionType == CareActionType.FocusShift
      ? DirectionForFocusStep(_data != null ? _data.focusTargetStep : 0)
      : CareDistanceDirection.None;

    public void Begin(CareActionType type, CareActionConfiguration configuration, CareActionSaveData restore = null)
    {
      _config = configuration;
      _config.Sanitize();
      _data = restore ?? new CareActionSaveData();
      _manualPause = false;
      _focusDistanceStep = null;
      if (restore != null && restore.actionType == type && restore.internalPhase != CareActionInternalPhase.None)
      {
        SanitizeRestoredData();
        _manualPause = restore.stage == CareActionStage.Paused &&
                       restore.pauseReason == CareActionPauseReason.Manual;
        return;
      }

      _data.Reset();
      _data.actionType = type;
      switch (type)
      {
        case CareActionType.ScreenDown:
          Enter(CareActionInternalPhase.ScreenDownDemo, CareActionStage.Demonstrating);
          break;
        case CareActionType.ClosedEyeRest:
          Enter(CareActionInternalPhase.ClosedEyePrompt, CareActionStage.WaitingForStart);
          break;
        case CareActionType.FocusShift:
          _data.focusTargetStep = 0;
          Enter(CareActionInternalPhase.FocusReference, CareActionStage.Preparing);
          break;
        case CareActionType.GuidedEyeCircles:
          _data.guidedStage = 0;
          Enter(CareActionInternalPhase.GuidedPreviewClockwise, CareActionStage.Demonstrating);
          break;
        default:
          _data.stage = CareActionStage.Cancelled;
          break;
      }
    }

    public void Advance(float unscaledDeltaSeconds, CareActionInputFrame input)
    {
      if (!IsRunning) return;
      var delta = Mathf.Clamp(unscaledDeltaSeconds, 0f, 1f);
      if (_manualPause)
      {
        Pause(CareActionPauseReason.Manual);
        return;
      }
      if (!input.ApplicationActive)
      {
        Pause(CareActionPauseReason.ApplicationBackground);
        return;
      }

      switch (ActionType)
      {
        case CareActionType.ScreenDown:
          AdvanceScreenDown(delta, input);
          break;
        case CareActionType.ClosedEyeRest:
          AdvanceClosedEye(delta, input);
          break;
        case CareActionType.FocusShift:
          AdvanceFocusShift(delta, input);
          break;
        case CareActionType.GuidedEyeCircles:
          AdvanceGuidedCircles(delta, input);
          break;
      }
    }

    public void PauseManually()
    {
      if (!IsRunning) return;
      _manualPause = true;
      Pause(CareActionPauseReason.Manual);
    }

    public void Suspend(CareActionPauseReason reason)
    {
      if (!IsRunning) return;
      Pause(reason == CareActionPauseReason.None ? CareActionPauseReason.ApplicationBackground : reason);
    }

    public void ResumeManually()
    {
      if (_data == null || Stage != CareActionStage.Paused || PauseReason != CareActionPauseReason.Manual) return;
      _manualPause = false;
      _data.pauseReason = CareActionPauseReason.None;
      _data.stage = StageForPhase(Phase);
    }

    public void Cancel()
    {
      if (_data == null) return;
      _manualPause = false;
      _data.pauseReason = CareActionPauseReason.None;
      _data.stage = CareActionStage.Cancelled;
    }

    public bool TryConsumeCompletionSignal()
    {
      if (_data == null || Stage != CareActionStage.Completed || _data.completionSignalEmitted) return false;
      _data.completionSignalEmitted = true;
      return true;
    }

    public void CompleteCurrentStepForDevelopment()
    {
      if (!IsRunning) return;
      _manualPause = false;
      _data.pauseReason = CareActionPauseReason.None;
      switch (ActionType)
      {
        case CareActionType.ScreenDown:
          if (Phase == CareActionInternalPhase.ScreenDownDemo)
            Enter(CareActionInternalPhase.ScreenDownWait, CareActionStage.WaitingForStart);
          else if (Phase == CareActionInternalPhase.ScreenDownWait)
            Enter(CareActionInternalPhase.ScreenDownRest, CareActionStage.Active);
          else if (Phase == CareActionInternalPhase.ScreenDownRest)
          {
            _data.elapsedSeconds = _config.screenDownDurationSeconds;
            Enter(CareActionInternalPhase.ScreenDownReturn, CareActionStage.WaitingForStart);
          }
          else Finish();
          break;
        case CareActionType.ClosedEyeRest:
          if (Phase == CareActionInternalPhase.ClosedEyePrompt)
            Enter(CareActionInternalPhase.ClosedEyeActive, CareActionStage.Active);
          else if (Phase == CareActionInternalPhase.ClosedEyeActive)
          {
            _data.elapsedSeconds = _config.closedEyeDurationSeconds;
            Enter(CareActionInternalPhase.ClosedEyeWaitReopen, CareActionStage.WaitingForStart);
          }
          else Finish();
          break;
        case CareActionType.FocusShift:
          if (Phase == CareActionInternalPhase.FocusReference)
          {
            _data.gestureReferenceScale = 1f;
            _data.gestureReferenceValid = true;
            Enter(FocusPhaseForStep(_data.focusTargetStep), CareActionStage.Active);
          }
          else CompleteFocusTarget();
          break;
        case CareActionType.GuidedEyeCircles:
          AdvanceGuidedPhaseForDevelopment();
          break;
      }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public bool SkipUnavailableScreenDownForDevelopment()
    {
      if (!IsRunning || ActionType != CareActionType.ScreenDown ||
          PauseReason != CareActionPauseReason.SensorUnavailable) return false;
      Finish(CareActionCompletionSource.DeveloperSkipped);
      return true;
    }
#endif

    private void AdvanceScreenDown(float delta, CareActionInputFrame input)
    {
      if (Phase == CareActionInternalPhase.ScreenDownDemo)
      {
        _data.stage = CareActionStage.Demonstrating;
        _data.pauseReason = CareActionPauseReason.None;
        _data.phaseElapsedSeconds += delta;
        if (_data.phaseElapsedSeconds >= _config.screenDownDemoSeconds)
          Enter(CareActionInternalPhase.ScreenDownWait, CareActionStage.WaitingForStart);
        return;
      }
      if (!input.DeviceSensorAvailable)
      {
        Wait(CareActionPauseReason.SensorUnavailable);
        return;
      }
      if (Phase == CareActionInternalPhase.ScreenDownReturn)
      {
        _data.stage = CareActionStage.WaitingForStart;
        _data.pauseReason = CareActionPauseReason.None;
        _data.holdElapsedSeconds = input.ScreenReturned ? _data.holdElapsedSeconds + delta : 0f;
        if (_data.holdElapsedSeconds >= _config.screenReturnHoldSeconds) Finish();
        return;
      }
      if (Phase == CareActionInternalPhase.ScreenDownWait)
      {
        _data.stage = CareActionStage.WaitingForStart;
        _data.pauseReason = CareActionPauseReason.None;
        _data.holdElapsedSeconds = input.ScreenDown ? _data.holdElapsedSeconds + delta : 0f;
        if (_data.holdElapsedSeconds >= _config.screenDownHoldSeconds)
          Enter(CareActionInternalPhase.ScreenDownRest, CareActionStage.Active);
        return;
      }
      if (!input.ScreenDown)
      {
        Pause(CareActionPauseReason.ScreenReturned);
        return;
      }
      if (Stage == CareActionStage.Paused)
      {
        _data.holdElapsedSeconds += delta;
        if (_data.holdElapsedSeconds < _config.screenDownHoldSeconds) return;
        _data.holdElapsedSeconds = 0f;
      }
      ResumeActive();
      _data.elapsedSeconds = Mathf.Min(_config.screenDownDurationSeconds, _data.elapsedSeconds + delta);
      if (_data.elapsedSeconds >= _config.screenDownDurationSeconds)
        Enter(CareActionInternalPhase.ScreenDownReturn, CareActionStage.WaitingForStart);
    }

    private void AdvanceClosedEye(float delta, CareActionInputFrame input)
    {
      if (!input.TrackingValid)
      {
        Pause(CareActionPauseReason.TrackingLost);
        return;
      }
      if (Phase == CareActionInternalPhase.ClosedEyePrompt)
      {
        _data.stage = CareActionStage.WaitingForStart;
        _data.pauseReason = CareActionPauseReason.None;
        _data.holdElapsedSeconds = input.EyesClosed ? _data.holdElapsedSeconds + delta : 0f;
        if (_data.holdElapsedSeconds >= _config.closeStartHoldSeconds)
          Enter(CareActionInternalPhase.ClosedEyeActive, CareActionStage.Active);
        return;
      }
      if (Phase == CareActionInternalPhase.ClosedEyeWaitReopen)
      {
        _data.stage = CareActionStage.WaitingForStart;
        _data.pauseReason = CareActionPauseReason.None;
        _data.holdElapsedSeconds = !input.EyesClosed ? _data.holdElapsedSeconds + delta : 0f;
        if (_data.holdElapsedSeconds >= _config.reopenHoldSeconds) Finish();
        return;
      }
      var wasPaused = Stage == CareActionStage.Paused;
      if (!input.EyesClosed)
      {
        Pause(CareActionPauseReason.EyesOpen);
        return;
      }
      if (wasPaused)
      {
        _data.holdElapsedSeconds += delta;
        if (_data.holdElapsedSeconds < _config.closeStartHoldSeconds) return;
        _data.holdElapsedSeconds = 0f;
      }
      ResumeActive();
      _data.elapsedSeconds = Mathf.Min(_config.closedEyeDurationSeconds, _data.elapsedSeconds + delta);
      if (_data.elapsedSeconds >= _config.closedEyeDurationSeconds)
        Enter(CareActionInternalPhase.ClosedEyeWaitReopen, CareActionStage.WaitingForStart);
    }

    private void AdvanceFocusShift(float delta, CareActionInputFrame input)
    {
      if (!input.TrackingValid)
      {
        _focusDistanceStep?.FreezeForTrackingLoss();
        _data.holdElapsedSeconds = 0f;
        _data.stage = CareActionStage.Paused;
        _data.pauseReason = CareActionPauseReason.TrackingLost;
        return;
      }
      if (Phase == CareActionInternalPhase.FocusReference)
      {
        _data.stage = CareActionStage.Preparing;
        _data.pauseReason = CareActionPauseReason.None;
        _data.holdElapsedSeconds = 0f;
        _data.phaseElapsedSeconds += delta;
        // Both sides must confirm the new step reference. Input can still carry
        // the previous frame's valid flag immediately after a target completes;
        // the saved runtime flag is explicitly cleared by CompleteFocusTarget.
        // Requiring both prevents one movement from skipping reference capture
        // and advancing the following direction automatically.
        if (!_data.gestureReferenceValid || !input.DistanceReferenceValid ||
            !IsFinitePositive(input.DistanceRatio)) return;
        var transitionDelay = _data.focusTargetStep == 0 ? 0f : _config.focusStepTransitionSeconds;
        if (_data.phaseElapsedSeconds < transitionDelay) return;
        _data.distanceDirectionProgress = 0f;
        _focusDistanceStep = null;
        Enter(FocusPhaseForStep(_data.focusTargetStep), CareActionStage.Active);
        return;
      }
      if (!input.DistanceReferenceValid || !IsFinitePositive(input.DistanceRatio))
      {
        Pause(CareActionPauseReason.DistanceUnavailable);
        return;
      }
      _data.stage = CareActionStage.Active;
      _data.pauseReason = CareActionPauseReason.None;
      _data.phaseElapsedSeconds += delta;
      _data.elapsedSeconds += delta;
      EnsureFocusDistanceStep();
      var sampleDelta = input.DistanceSampleDeltaSeconds > 0f
        ? Mathf.Clamp(input.DistanceSampleDeltaSeconds, 0f, 0.25f)
        : delta;
      var completed = _focusDistanceStep.Advance(
        input.DistanceRatio,
        1f,
        sampleDelta,
        true,
        input.DistanceSampleFresh);
      _data.distanceDirectionProgress = _focusDistanceStep.Progress;
      _data.holdElapsedSeconds = _focusDistanceStep.StableSeconds;
      if (completed) CompleteFocusTarget();
    }

    private void AdvanceGuidedCircles(float delta, CareActionInputFrame input)
    {
      if (Phase == CareActionInternalPhase.GuidedPreviewClockwise ||
          Phase == CareActionInternalPhase.GuidedPreviewCounterClockwise)
      {
        _data.stage = CareActionStage.Demonstrating;
        _data.pauseReason = CareActionPauseReason.None;
        _data.phaseElapsedSeconds += delta;
        var halfPreview = _config.guidedPreviewSeconds * 0.5f;
        if (Phase == CareActionInternalPhase.GuidedPreviewClockwise && _data.phaseElapsedSeconds >= halfPreview)
        {
          _data.guidedStage = 1;
          Enter(CareActionInternalPhase.GuidedPreviewCounterClockwise, CareActionStage.Demonstrating);
        }
        else if (Phase == CareActionInternalPhase.GuidedPreviewCounterClockwise && _data.phaseElapsedSeconds >= halfPreview)
        {
          _data.guidedStage = 2;
          Enter(CareActionInternalPhase.GuidedPromptClose, CareActionStage.WaitingForStart);
        }
        return;
      }
      if (!input.TrackingValid)
      {
        Pause(CareActionPauseReason.TrackingLost);
        return;
      }
      if (Phase == CareActionInternalPhase.GuidedPromptClose)
      {
        _data.stage = CareActionStage.WaitingForStart;
        _data.pauseReason = CareActionPauseReason.None;
        _data.holdElapsedSeconds = input.EyesClosed ? _data.holdElapsedSeconds + delta : 0f;
        if (_data.holdElapsedSeconds >= _config.closeStartHoldSeconds)
        {
          _data.guidedStage = 3;
          Enter(CareActionInternalPhase.GuidedClockwise, CareActionStage.Active);
        }
        return;
      }
      if (Phase == CareActionInternalPhase.GuidedWaitReopen)
      {
        _data.stage = CareActionStage.WaitingForStart;
        _data.pauseReason = CareActionPauseReason.None;
        _data.holdElapsedSeconds = !input.EyesClosed ? _data.holdElapsedSeconds + delta : 0f;
        if (_data.holdElapsedSeconds >= _config.reopenHoldSeconds) Finish();
        return;
      }
      var wasPaused = Stage == CareActionStage.Paused;
      if (!input.EyesClosed)
      {
        Pause(CareActionPauseReason.EyesOpen);
        return;
      }

      if (wasPaused)
      {
        _data.holdElapsedSeconds += delta;
        if (_data.holdElapsedSeconds < _config.closeStartHoldSeconds) return;
        _data.holdElapsedSeconds = 0f;
      }

      ResumeActive();
      _data.phaseElapsedSeconds += delta;
      _data.elapsedSeconds += delta;
      var duration = GuidedPhaseDuration(Phase);
      if (_data.phaseElapsedSeconds < duration) return;
      switch (Phase)
      {
        case CareActionInternalPhase.GuidedClockwise:
          _data.guidedStage = 4;
          Enter(CareActionInternalPhase.GuidedPause, CareActionStage.Active);
          break;
        case CareActionInternalPhase.GuidedPause:
          _data.guidedStage = 5;
          Enter(CareActionInternalPhase.GuidedCounterClockwise, CareActionStage.Active);
          break;
        case CareActionInternalPhase.GuidedCounterClockwise:
          _data.guidedStage = 6;
          Enter(CareActionInternalPhase.GuidedRelax, CareActionStage.Active);
          break;
        case CareActionInternalPhase.GuidedRelax:
          _data.guidedStage = 7;
          Enter(CareActionInternalPhase.GuidedWaitReopen, CareActionStage.WaitingForStart);
          break;
      }
    }

    private void CompleteFocusTarget()
    {
      _data.focusTargetStep++;
      _data.distanceDirectionProgress = 0f;
      _data.holdElapsedSeconds = 0f;
      _focusDistanceStep = null;
      if (_data.focusTargetStep >= 4)
      {
        Finish();
        return;
      }
      _data.gestureReferenceScale = 0f;
      _data.gestureReferenceValid = false;
      Enter(CareActionInternalPhase.FocusReference, CareActionStage.Preparing);
    }

    public bool CompleteFocusStepForFallback(CareDistanceFallbackReason reason)
    {
      if (!IsRunning || ActionType != CareActionType.FocusShift ||
          reason == CareDistanceFallbackReason.None) return false;
      _data.distanceFallbackReason = reason;
      CompleteFocusTarget();
      return true;
    }

    private void AdvanceGuidedPhaseForDevelopment()
    {
      switch (Phase)
      {
        case CareActionInternalPhase.GuidedPreviewClockwise:
          Enter(CareActionInternalPhase.GuidedPreviewCounterClockwise, CareActionStage.Demonstrating);
          break;
        case CareActionInternalPhase.GuidedPreviewCounterClockwise:
          Enter(CareActionInternalPhase.GuidedPromptClose, CareActionStage.WaitingForStart);
          break;
        case CareActionInternalPhase.GuidedPromptClose:
          Enter(CareActionInternalPhase.GuidedClockwise, CareActionStage.Active);
          break;
        case CareActionInternalPhase.GuidedClockwise:
          Enter(CareActionInternalPhase.GuidedPause, CareActionStage.Active);
          break;
        case CareActionInternalPhase.GuidedPause:
          Enter(CareActionInternalPhase.GuidedCounterClockwise, CareActionStage.Active);
          break;
        case CareActionInternalPhase.GuidedCounterClockwise:
          Enter(CareActionInternalPhase.GuidedRelax, CareActionStage.Active);
          break;
        case CareActionInternalPhase.GuidedRelax:
          Enter(CareActionInternalPhase.GuidedWaitReopen, CareActionStage.WaitingForStart);
          break;
        default:
          Finish();
          break;
      }
    }

    private void Enter(CareActionInternalPhase phase, CareActionStage stage)
    {
      _data.internalPhase = phase;
      _data.stage = stage;
      _data.pauseReason = CareActionPauseReason.None;
      _data.phaseElapsedSeconds = 0f;
      _data.holdElapsedSeconds = 0f;
    }

    private void Finish(CareActionCompletionSource source = CareActionCompletionSource.SensorCompleted)
    {
      if (_data.stage == CareActionStage.Completed) return;
      _data.stage = CareActionStage.Completed;
      _data.pauseReason = CareActionPauseReason.None;
      _data.holdElapsedSeconds = 0f;
      _data.completionSource = source;
      if (_data.actionType == CareActionType.FocusShift)
      {
        _data.gestureReferenceScale = 0f;
        _data.gestureReferenceValid = false;
      }
    }

    private void Pause(CareActionPauseReason reason)
    {
      _data.stage = CareActionStage.Paused;
      _data.pauseReason = reason;
      _data.holdElapsedSeconds = 0f;
    }

    private void Wait(CareActionPauseReason reason)
    {
      _data.stage = CareActionStage.WaitingForStart;
      _data.pauseReason = reason;
      _data.holdElapsedSeconds = 0f;
    }

    private void ResumeActive()
    {
      _data.stage = CareActionStage.Active;
      _data.pauseReason = CareActionPauseReason.None;
    }

    private void EnsureFocusDistanceStep()
    {
      if (_focusDistanceStep != null && _focusDistanceStep.Direction == ExpectedDistanceDirection) return;
      _focusDistanceStep = new CareRelativeDistanceStep(
        ExpectedDistanceDirection,
        _config.distanceDeadZone,
        _config.distanceCompleteThreshold,
        _config.distanceStepHoldSeconds,
        _config.distanceProgressFallSeconds,
        _data.distanceDirectionProgress,
        _data.holdElapsedSeconds);
    }

    private float CalculateProgress()
    {
      if (_data == null) return 0f;
      if (Stage == CareActionStage.Completed) return 1f;
      switch (ActionType)
      {
        case CareActionType.ScreenDown:
          return Mathf.Clamp01(_data.elapsedSeconds / _config.screenDownDurationSeconds);
        case CareActionType.ClosedEyeRest:
          return Mathf.Clamp01(_data.elapsedSeconds / _config.closedEyeDurationSeconds);
        case CareActionType.FocusShift:
          if (Phase == CareActionInternalPhase.FocusReference) return 0f;
          return Mathf.Clamp01((_data.focusTargetStep +
                                Mathf.Clamp01(_data.distanceDirectionProgress)) / 4f);
        case CareActionType.GuidedEyeCircles:
          if (Phase == CareActionInternalPhase.GuidedPreviewClockwise || Phase == CareActionInternalPhase.GuidedPreviewCounterClockwise)
          {
            var previewOffset = Phase == CareActionInternalPhase.GuidedPreviewCounterClockwise ? _config.guidedPreviewSeconds * 0.5f : 0f;
            return 0.15f * Mathf.Clamp01((previewOffset + _data.phaseElapsedSeconds) / _config.guidedPreviewSeconds);
          }
          var total = _config.guidedClockwiseSeconds + _config.guidedPauseSeconds +
                      _config.guidedCounterClockwiseSeconds + _config.guidedRelaxSeconds;
          return 0.15f + 0.85f * Mathf.Clamp01(_data.elapsedSeconds / Mathf.Max(0.1f, total));
        default:
          return 0f;
      }
    }

    private float CalculateRemainingSeconds()
    {
      if (_data == null) return 0f;
      switch (ActionType)
      {
        case CareActionType.ScreenDown:
          return Mathf.Max(0f, _config.screenDownDurationSeconds - _data.elapsedSeconds);
        case CareActionType.ClosedEyeRest:
          return Mathf.Max(0f, _config.closedEyeDurationSeconds - _data.elapsedSeconds);
        case CareActionType.GuidedEyeCircles:
          var total = _config.guidedClockwiseSeconds + _config.guidedPauseSeconds +
                      _config.guidedCounterClockwiseSeconds + _config.guidedRelaxSeconds;
          return Mathf.Max(0f, total - _data.elapsedSeconds);
        default:
          return 0f;
      }
    }

    private float GuidedPhaseDuration(CareActionInternalPhase phase)
    {
      switch (phase)
      {
        case CareActionInternalPhase.GuidedClockwise: return _config.guidedClockwiseSeconds;
        case CareActionInternalPhase.GuidedPause: return _config.guidedPauseSeconds;
        case CareActionInternalPhase.GuidedCounterClockwise: return _config.guidedCounterClockwiseSeconds;
        case CareActionInternalPhase.GuidedRelax: return _config.guidedRelaxSeconds;
        default: return 0f;
      }
    }

    private void SanitizeRestoredData()
    {
      _data.elapsedSeconds = Mathf.Max(0f, _data.elapsedSeconds);
      _data.phaseElapsedSeconds = Mathf.Max(0f, _data.phaseElapsedSeconds);
      _data.holdElapsedSeconds = Mathf.Max(0f, _data.holdElapsedSeconds);
      _data.focusTargetStep = Mathf.Clamp(_data.focusTargetStep, 0, 4);
      _data.distanceDirectionProgress = Mathf.Clamp01(_data.distanceDirectionProgress);
      if (!Enum.IsDefined(typeof(CareDistanceFallbackReason), _data.distanceFallbackReason))
        _data.distanceFallbackReason = CareDistanceFallbackReason.None;
      _data.guidedStage = Mathf.Clamp(_data.guidedStage, 0, 7);
      if (!CareDistanceReferenceSampler.IsValidScale(_data.gestureReferenceScale))
      {
        _data.gestureReferenceScale = 0f;
        _data.gestureReferenceValid = false;
      }
      if (_data.stage == CareActionStage.Completed || _data.stage == CareActionStage.Cancelled) return;
      if (_data.internalPhase == CareActionInternalPhase.None)
        _data.stage = CareActionStage.Cancelled;
    }

    private static CareActionInternalPhase FocusPhaseForStep(int step)
    {
      switch (step)
      {
        case 0: return CareActionInternalPhase.FocusNearOne;
        case 1: return CareActionInternalPhase.FocusFarOne;
        case 2: return CareActionInternalPhase.FocusNearTwo;
        case 3: return CareActionInternalPhase.FocusFarTwo;
        default: return CareActionInternalPhase.None;
      }
    }

    private static CareDistanceDirection DirectionForFocusStep(int step)
    {
      return (step & 1) == 0 ? CareDistanceDirection.Closer : CareDistanceDirection.Away;
    }

    private static CareActionStage StageForPhase(CareActionInternalPhase phase)
    {
      switch (phase)
      {
        case CareActionInternalPhase.ScreenDownDemo:
        case CareActionInternalPhase.GuidedPreviewClockwise:
        case CareActionInternalPhase.GuidedPreviewCounterClockwise:
          return CareActionStage.Demonstrating;
        case CareActionInternalPhase.FocusReference:
          return CareActionStage.Preparing;
        case CareActionInternalPhase.ScreenDownWait:
        case CareActionInternalPhase.ScreenDownReturn:
        case CareActionInternalPhase.ClosedEyePrompt:
        case CareActionInternalPhase.ClosedEyeWaitReopen:
        case CareActionInternalPhase.GuidedPromptClose:
        case CareActionInternalPhase.GuidedWaitReopen:
          return CareActionStage.WaitingForStart;
        default:
          return CareActionStage.Active;
      }
    }

    public static string DisplayNameFor(CareActionType type)
    {
      switch (type)
      {
        case CareActionType.ScreenDown: return "SCREEN DOWN";
        case CareActionType.ClosedEyeRest: return "CLOSED-EYE REST";
        case CareActionType.FocusShift: return "FOCUS SHIFT";
        case CareActionType.GuidedEyeCircles: return "GUIDED EYE CIRCLES";
        default: return string.Empty;
      }
    }

    private static string PromptFor(CareActionInternalPhase phase, CareActionPauseReason reason)
    {
      if (reason == CareActionPauseReason.TrackingLost) return "TRACKING LOST";
      if (reason == CareActionPauseReason.SensorUnavailable || reason == CareActionPauseReason.DistanceUnavailable)
        return "SENSOR UNAVAILABLE";
      if (reason == CareActionPauseReason.TooClose) return "MOVE AWAY";
      if (reason == CareActionPauseReason.ApplicationBackground || reason == CareActionPauseReason.ApplicationFocusLost ||
          reason == CareActionPauseReason.Manual) return "PAUSED";
      switch (phase)
      {
        case CareActionInternalPhase.FocusReference: return string.Empty;
        case CareActionInternalPhase.ScreenDownDemo: return string.Empty;
        case CareActionInternalPhase.ScreenDownWait:
        case CareActionInternalPhase.ScreenDownRest: return "SCREEN DOWN";
        case CareActionInternalPhase.ScreenDownReturn: return "RETURN";
        case CareActionInternalPhase.ClosedEyePrompt:
        case CareActionInternalPhase.ClosedEyeActive: return "CLOSE YOUR EYES";
        case CareActionInternalPhase.ClosedEyeWaitReopen: return "OPEN YOUR EYES";
        case CareActionInternalPhase.FocusNeutralStart:
        case CareActionInternalPhase.FocusNeutralFinish: return string.Empty;
        case CareActionInternalPhase.FocusNearOne:
        case CareActionInternalPhase.FocusNearTwo: return "MOVE CLOSER";
        case CareActionInternalPhase.FocusFarOne:
        case CareActionInternalPhase.FocusFarTwo: return "MOVE AWAY";
        case CareActionInternalPhase.GuidedPreviewClockwise:
        case CareActionInternalPhase.GuidedPreviewCounterClockwise: return "FOLLOW THE CIRCLE";
        case CareActionInternalPhase.GuidedPromptClose: return "CLOSE YOUR EYES";
        case CareActionInternalPhase.GuidedWaitReopen: return "OPEN YOUR EYES";
        case CareActionInternalPhase.GuidedRelax: return "RELAX";
        case CareActionInternalPhase.GuidedClockwise:
        case CareActionInternalPhase.GuidedPause:
        case CareActionInternalPhase.GuidedCounterClockwise: return "FOLLOW THE RHYTHM";
        default: return string.Empty;
      }
    }

    private static bool IsFinitePositive(float value)
    {
      return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
  }
}
