using System;
using UnityEngine;

namespace KeepBlinking.CareStation
{
  [Serializable]
  public struct CareActionConfiguration
  {
    public bool showIntro;
    public float actionIntroSeconds;
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
    public float focusNeutralMinimum;
    public float focusNeutralMaximum;
    public float focusCloserRatio;
    public float focusAwayRatio;
    public float focusTooCloseRatio;
    public float focusTargetHoldSeconds;
    public float focusMinimumLegSeconds;
    public float focusDirectionIntervalSeconds;
    public int focusCycleCount;
    public float guidedPreviewSeconds;
    public float guidedClockwiseSeconds;
    public float guidedPauseSeconds;
    public float guidedCounterClockwiseSeconds;
    public float guidedRelaxSeconds;
    public int guidedLapsPerDirection;
    public float pilotIntroSeconds;
    public float pilotRoundSeconds;
    public int pilotRoundsPerAxis;
    public float pilotTransitionSeconds;

    public static CareActionConfiguration Default => new CareActionConfiguration
    {
      showIntro = false,
      actionIntroSeconds = 2.5f,
      screenDownDemoSeconds = 3f,
      screenDownDurationSeconds = 20f,
      screenDownHoldSeconds = 0.5f,
      screenReturnHoldSeconds = 0.4f,
      closedEyeDurationSeconds = 45f,
      closeStartHoldSeconds = 1.5f,
      reopenHoldSeconds = 0.5f,
      // Linear distance fractions (see FaceDistanceRatio), not raw face-scale fractions.
      distanceDeadZone = 0.05f,
      distanceCompleteThreshold = 0.22f,
      distanceStepHoldSeconds = 0.7f,
      distanceProgressFallSeconds = 0.25f,
      focusStepTransitionSeconds = 1.2f,
      focusNeutralMinimum = 0.94f,
      focusNeutralMaximum = 1.06f,
      focusCloserRatio = 1.25f,
      focusAwayRatio = 0.78f,
      focusTooCloseRatio = 1.45f,
      focusTargetHoldSeconds = 0.7f,
      focusMinimumLegSeconds = 3f,
      focusDirectionIntervalSeconds = 1.2f,
      focusCycleCount = 6,
      guidedPreviewSeconds = 2.5f,
      guidedClockwiseSeconds = 5f,
      guidedPauseSeconds = 0.9f,
      guidedCounterClockwiseSeconds = 5f,
      guidedRelaxSeconds = 12f,
      guidedLapsPerDirection = 3,
      pilotIntroSeconds = 3f,
      pilotRoundSeconds = 3.5f,
      pilotRoundsPerAxis = 3,
      pilotTransitionSeconds = 1.25f,
    };

    public void Sanitize()
    {
      actionIntroSeconds = Mathf.Clamp(actionIntroSeconds, 2f, 4f);
      screenDownDemoSeconds = Mathf.Max(0.1f, screenDownDemoSeconds);
      screenDownDurationSeconds = Mathf.Max(1f, screenDownDurationSeconds);
      screenDownHoldSeconds = Mathf.Max(0.1f, screenDownHoldSeconds);
      screenReturnHoldSeconds = Mathf.Max(0.1f, screenReturnHoldSeconds);
      closedEyeDurationSeconds = Mathf.Max(1f, closedEyeDurationSeconds);
      closeStartHoldSeconds = Mathf.Max(0.1f, closeStartHoldSeconds);
      reopenHoldSeconds = Mathf.Max(0.1f, reopenHoldSeconds);
      distanceDeadZone = Mathf.Clamp(distanceDeadZone, 0.01f, 0.12f);
      distanceCompleteThreshold = Mathf.Clamp(distanceCompleteThreshold, distanceDeadZone + 0.01f, 0.4f);
      distanceStepHoldSeconds = Mathf.Clamp(distanceStepHoldSeconds, 0.05f, 1f);
      distanceProgressFallSeconds = Mathf.Clamp(distanceProgressFallSeconds, 0.05f, 1f);
      focusStepTransitionSeconds = Mathf.Clamp(focusStepTransitionSeconds, 0f, 2f);
      focusNeutralMinimum = Mathf.Clamp(focusNeutralMinimum, 0.75f, 1f);
      focusNeutralMaximum = Mathf.Clamp(focusNeutralMaximum, 1f, 1.2f);
      focusCloserRatio = Mathf.Clamp(focusCloserRatio, 1.10f, 1.5f);
      focusAwayRatio = Mathf.Clamp(focusAwayRatio, 0.55f, 0.9f);
      focusTooCloseRatio = Mathf.Max(focusCloserRatio + 0.05f, focusTooCloseRatio);
      focusTargetHoldSeconds = Mathf.Clamp(focusTargetHoldSeconds, 0.2f, 1.5f);
      focusMinimumLegSeconds = Mathf.Clamp(focusMinimumLegSeconds, 2.5f, 8f);
      focusDirectionIntervalSeconds = Mathf.Clamp(focusDirectionIntervalSeconds, 1.2f, 3f);
      focusCycleCount = Mathf.Clamp(focusCycleCount, 1, 8);
      guidedPreviewSeconds = Mathf.Max(0.1f, guidedPreviewSeconds);
      guidedClockwiseSeconds = Mathf.Max(0.1f, guidedClockwiseSeconds);
      guidedPauseSeconds = Mathf.Max(0.1f, guidedPauseSeconds);
      guidedCounterClockwiseSeconds = Mathf.Max(0.1f, guidedCounterClockwiseSeconds);
      guidedRelaxSeconds = Mathf.Max(0.1f, guidedRelaxSeconds);
      guidedLapsPerDirection = Mathf.Clamp(guidedLapsPerDirection, 1, 6);
      pilotIntroSeconds = Mathf.Clamp(pilotIntroSeconds, 2f, 4f);
      pilotRoundSeconds = Mathf.Clamp(pilotRoundSeconds, 2.5f, 5f);
      pilotRoundsPerAxis = Mathf.Clamp(pilotRoundsPerAxis, 1, 4);
      pilotTransitionSeconds = Mathf.Clamp(pilotTransitionSeconds, 1f, 1.5f);
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
    public bool RequiresDevicePose => false;
    public string DisplayName => DisplayNameFor(ActionType);
    public string Prompt => Stage == CareActionStage.Completed || Stage == CareActionStage.Cancelled
      ? string.Empty
      : PromptFor(Phase, PauseReason);
    public float Progress => CalculateProgress();
    public float RemainingSeconds => CalculateRemainingSeconds();
    public int RemainingSteps => ActionType == CareActionType.FocusShift
      ? Mathf.Max(0, _config.focusCycleCount * 2 - Mathf.Clamp(_data.focusTargetStep, 0, _config.focusCycleCount * 2))
      : ActionType == CareActionType.PilotEyeRoutine
        ? Mathf.Max(0, 4 * _config.pilotRoundsPerAxis -
          (_data.pilotCurrentAxis * _config.pilotRoundsPerAxis + _data.pilotCurrentRound))
        : ActionType == CareActionType.GuidedEyeCircles
          ? Mathf.Max(0, _config.guidedLapsPerDirection * 2 -
            (_data.guidedStage >= 3 ? _config.guidedLapsPerDirection : 0) - _data.guidedLapCount)
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
      if (CareActionLibrary.IsRetiredTask(type))
      {
        _data.Reset();
        _data.actionType = type;
        _data.stage = CareActionStage.Cancelled;
        return;
      }
      if (restore != null && restore.actionType == type && restore.internalPhase != CareActionInternalPhase.None)
      {
        SanitizeRestoredData();
        _manualPause = restore.stage == CareActionStage.Paused &&
                       restore.pauseReason == CareActionPauseReason.Manual;
        return;
      }

      _data.Reset();
      _data.actionType = type;
      _data.introWasRequested = _config.showIntro;
      switch (type)
      {
        case CareActionType.ClosedEyeRest:
          Enter(_config.showIntro
              ? CareActionInternalPhase.ClosedEyeIntro
              : CareActionInternalPhase.ClosedEyePrompt,
            _config.showIntro ? CareActionStage.Demonstrating : CareActionStage.WaitingForStart);
          break;
        case CareActionType.FocusShift:
          _data.focusTargetStep = 0;
          _data.focusCycleCount = 0;
          _data.focusRearmed = false;
          Enter(_config.showIntro
              ? CareActionInternalPhase.FocusIntro
              : CareActionInternalPhase.FocusReference,
            _config.showIntro ? CareActionStage.Demonstrating : CareActionStage.Preparing);
          break;
        case CareActionType.GuidedEyeCircles:
          _data.guidedStage = 0;
          Enter(_config.showIntro
              ? CareActionInternalPhase.GuidedPreviewClockwise
              : CareActionInternalPhase.GuidedClockwise,
            _config.showIntro ? CareActionStage.Demonstrating : CareActionStage.Active);
          break;
        case CareActionType.PilotEyeRoutine:
          _data.pilotCurrentAxis = 0;
          _data.pilotCurrentRound = 0;
          _data.pilotCurrentEndpoint = 0;
          Enter(_config.showIntro ? CareActionInternalPhase.PilotIntro : CareActionInternalPhase.PilotVertical,
            _config.showIntro ? CareActionStage.Demonstrating : CareActionStage.Active);
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
        case CareActionType.ClosedEyeRest:
          AdvanceClosedEye(delta, input);
          break;
        case CareActionType.FocusShift:
          AdvanceFocusShift(delta, input);
          break;
        case CareActionType.GuidedEyeCircles:
          AdvanceGuidedCircles(delta, input);
          break;
        case CareActionType.PilotEyeRoutine:
          AdvancePilotRoutine(delta);
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
        case CareActionType.ClosedEyeRest:
          if (Phase == CareActionInternalPhase.ClosedEyeIntro)
            Enter(CareActionInternalPhase.ClosedEyePrompt, CareActionStage.WaitingForStart);
          else if (Phase == CareActionInternalPhase.ClosedEyePrompt)
            Enter(CareActionInternalPhase.ClosedEyeActive, CareActionStage.Active);
          else if (Phase == CareActionInternalPhase.ClosedEyeActive)
          {
            _data.elapsedSeconds = _config.closedEyeDurationSeconds;
            Enter(CareActionInternalPhase.ClosedEyeWaitReopen, CareActionStage.WaitingForStart);
          }
          else Finish();
          break;
        case CareActionType.FocusShift:
          if (Phase == CareActionInternalPhase.FocusIntro)
            Enter(CareActionInternalPhase.FocusReference, CareActionStage.Preparing);
          else if (Phase == CareActionInternalPhase.FocusReference)
          {
            if (!_data.gestureReferenceValid)
            {
              _data.gestureReferenceScale = 1f;
              _data.gestureReferenceValid = true;
            }
            Enter(_data.focusTargetStep <= 0
              ? CareActionInternalPhase.FocusNeutralStart
              : FocusPhaseForStep(_data.focusTargetStep), CareActionStage.Active);
          }
          else if (Phase == CareActionInternalPhase.FocusNeutralFinish) Finish();
          else CompleteFocusTarget();
          break;
        case CareActionType.GuidedEyeCircles:
          AdvanceGuidedPhaseForDevelopment();
          break;
        case CareActionType.PilotEyeRoutine:
          AdvancePilotPhaseForDevelopment();
          break;
      }
    }

    private void AdvanceClosedEye(float delta, CareActionInputFrame input)
    {
      if (Phase == CareActionInternalPhase.ClosedEyeIntro)
      {
        _data.stage = CareActionStage.Demonstrating;
        _data.pauseReason = CareActionPauseReason.None;
        _data.phaseElapsedSeconds += delta;
        if (_data.phaseElapsedSeconds >= _config.actionIntroSeconds)
          Enter(CareActionInternalPhase.ClosedEyePrompt, CareActionStage.WaitingForStart);
        return;
      }
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
      if (Phase == CareActionInternalPhase.FocusIntro)
      {
        _data.stage = CareActionStage.Demonstrating;
        _data.pauseReason = CareActionPauseReason.None;
        _data.phaseElapsedSeconds += delta;
        if (_data.phaseElapsedSeconds >= _config.actionIntroSeconds)
          Enter(CareActionInternalPhase.FocusReference, CareActionStage.Preparing);
        return;
      }
      if (!input.TrackingValid)
      {
        _data.focusTrackingRecoveryGuard = true;
        _data.holdElapsedSeconds = 0f;
        _data.stage = CareActionStage.Paused;
        _data.pauseReason = CareActionPauseReason.TrackingLost;
        return;
      }
      if (!input.DistanceReferenceValid || !IsFinitePositive(input.DistanceRatio))
      {
        Pause(CareActionPauseReason.DistanceUnavailable);
        return;
      }
      if (_data.focusTrackingRecoveryGuard)
      {
        if (input.DistanceSampleFresh) _data.focusTrackingRecoveryGuard = false;
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
        var transitionDelay = _data.focusTargetStep == 0 ? 0f : _config.focusDirectionIntervalSeconds;
        if (_data.phaseElapsedSeconds < transitionDelay) return;
        _data.distanceDirectionProgress = 0f;
        Enter(_data.focusTargetStep == 0
          ? CareActionInternalPhase.FocusNeutralStart
          : FocusPhaseForStep(_data.focusTargetStep), CareActionStage.Active);
        return;
      }

      var sampleDelta = input.DistanceSampleDeltaSeconds > 0f
        ? Mathf.Clamp(input.DistanceSampleDeltaSeconds, 0f, 0.25f)
        : delta;
      if (!input.DistanceSampleFresh) return;
      var ratio = input.DistanceRatio;
      var neutral = ratio >= _config.focusNeutralMinimum && ratio <= _config.focusNeutralMaximum;
      if (Phase == CareActionInternalPhase.FocusNeutralStart ||
          Phase == CareActionInternalPhase.FocusNeutralFinish)
      {
        _data.stage = CareActionStage.Active;
        _data.pauseReason = CareActionPauseReason.None;
        _data.distanceDirectionProgress = neutral ? 1f : 0f;
        _data.holdElapsedSeconds = neutral ? _data.holdElapsedSeconds + sampleDelta : 0f;
        if (_data.holdElapsedSeconds < _config.focusTargetHoldSeconds) return;
        if (Phase == CareActionInternalPhase.FocusNeutralFinish)
        {
          Finish();
          return;
        }
        _data.focusRearmed = true;
        Enter(FocusPhaseForStep(0), CareActionStage.Active);
        return;
      }

      _data.stage = CareActionStage.Active;
      _data.pauseReason = CareActionPauseReason.None;
      _data.phaseElapsedSeconds += sampleDelta;
      _data.elapsedSeconds += delta;
      if (neutral) _data.focusRearmed = true;
      var direction = ExpectedDistanceDirection;
      if (direction == CareDistanceDirection.Closer && ratio >= _config.focusTooCloseRatio)
      {
        _data.stage = CareActionStage.Paused;
        _data.pauseReason = CareActionPauseReason.TooClose;
        _data.holdElapsedSeconds = 0f;
        return;
      }

      var targetReached = direction == CareDistanceDirection.Closer
        ? ratio >= _config.focusCloserRatio
        : ratio <= _config.focusAwayRatio;
      var progress = direction == CareDistanceDirection.Closer
        ? Mathf.InverseLerp(_config.focusNeutralMaximum, _config.focusCloserRatio, ratio)
        : Mathf.InverseLerp(_config.focusNeutralMinimum, _config.focusAwayRatio, ratio);
      _data.distanceDirectionProgress = Mathf.Clamp01(progress);
      var paceReady = _data.phaseElapsedSeconds >= _config.focusMinimumLegSeconds;
      _data.holdElapsedSeconds = targetReached && paceReady && _data.focusRearmed
        ? _data.holdElapsedSeconds + sampleDelta
        : 0f;
      if (_data.holdElapsedSeconds >= _config.focusTargetHoldSeconds)
        CompleteFocusTarget();
    }

    private void AdvanceGuidedCircles(float delta, CareActionInputFrame input)
    {
      if (Phase == CareActionInternalPhase.GuidedPreviewClockwise)
      {
        _data.stage = CareActionStage.Demonstrating;
        _data.pauseReason = CareActionPauseReason.None;
        _data.phaseElapsedSeconds += delta;
        _data.guidedNormalizedProgress = Mathf.Clamp01(_data.phaseElapsedSeconds / _config.guidedPreviewSeconds);
        if (_data.phaseElapsedSeconds >= _config.guidedPreviewSeconds)
        {
          _data.guidedStage = 1;
          _data.guidedLapCount = 0;
          Enter(CareActionInternalPhase.GuidedClockwise, CareActionStage.Active);
        }
        return;
      }

      if (Phase == CareActionInternalPhase.GuidedClockwise ||
          Phase == CareActionInternalPhase.GuidedPause ||
          Phase == CareActionInternalPhase.GuidedCounterClockwise)
      {
        ResumeActive();
        _data.phaseElapsedSeconds += delta;
        _data.elapsedSeconds += delta;
        var perLap = Phase == CareActionInternalPhase.GuidedCounterClockwise
          ? _config.guidedCounterClockwiseSeconds
          : _config.guidedClockwiseSeconds;
        if (Phase == CareActionInternalPhase.GuidedPause)
        {
          _data.guidedNormalizedProgress = 0f;
          if (_data.phaseElapsedSeconds >= _config.guidedPauseSeconds)
          {
            _data.guidedStage = 3;
            _data.guidedLapCount = 0;
            Enter(CareActionInternalPhase.GuidedCounterClockwise, CareActionStage.Active);
          }
          return;
        }

        var total = perLap * _config.guidedLapsPerDirection;
        _data.guidedLapCount = Mathf.Clamp(Mathf.FloorToInt(_data.phaseElapsedSeconds / perLap), 0,
          _config.guidedLapsPerDirection);
        _data.guidedNormalizedProgress = Mathf.Repeat(_data.phaseElapsedSeconds, perLap) / perLap;
        if (_data.phaseElapsedSeconds < total) return;
        _data.guidedLapCount = _config.guidedLapsPerDirection;
        _data.guidedNormalizedProgress = 1f;
        if (Phase == CareActionInternalPhase.GuidedClockwise)
        {
          _data.guidedStage = 2;
          Enter(CareActionInternalPhase.GuidedPause, CareActionStage.Active);
        }
        else
        {
          _data.guidedStage = 4;
          _data.guidedClosedPhase = true;
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
          _data.guidedStage = 5;
          Enter(CareActionInternalPhase.GuidedClosedRest, CareActionStage.Active);
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
      if (Phase == CareActionInternalPhase.GuidedClosedRest &&
          _data.phaseElapsedSeconds >= _config.guidedRelaxSeconds)
      {
        _data.guidedStage = 6;
        Enter(CareActionInternalPhase.GuidedWaitReopen, CareActionStage.WaitingForStart);
      }
    }

    private void AdvancePilotRoutine(float delta)
    {
      if (Phase == CareActionInternalPhase.PilotIntro)
      {
        _data.stage = CareActionStage.Demonstrating;
        _data.pauseReason = CareActionPauseReason.None;
        _data.phaseElapsedSeconds += delta;
        if (_data.phaseElapsedSeconds >= _config.pilotIntroSeconds)
          Enter(PilotPhaseForAxis(0), CareActionStage.Active);
        return;
      }

      if (Phase == CareActionInternalPhase.PilotTransition)
      {
        _data.stage = CareActionStage.Demonstrating;
        _data.phaseElapsedSeconds += delta;
        if (_data.phaseElapsedSeconds >= _config.pilotTransitionSeconds) Finish();
        return;
      }

      ResumeActive();
      _data.phaseElapsedSeconds += delta;
      _data.elapsedSeconds += delta;
      _data.pilotNormalizedMoveProgress = Mathf.Clamp01(_data.phaseElapsedSeconds / _config.pilotRoundSeconds);
      _data.pilotCurrentEndpoint = Mathf.Clamp(
        Mathf.FloorToInt(_data.pilotNormalizedMoveProgress * 4f), 0, 4);
      if (_data.phaseElapsedSeconds < _config.pilotRoundSeconds) return;

      _data.pilotCurrentRound++;
      if (_data.pilotCurrentRound < _config.pilotRoundsPerAxis)
      {
        Enter(PilotPhaseForAxis(_data.pilotCurrentAxis), CareActionStage.Active);
        return;
      }

      _data.pilotCurrentAxis++;
      _data.pilotCurrentRound = 0;
      _data.pilotCurrentEndpoint = 0;
      _data.pilotNormalizedMoveProgress = 0f;
      if (_data.pilotCurrentAxis >= 4)
      {
        _data.pilotCompletionConsumed = true;
        Enter(CareActionInternalPhase.PilotTransition, CareActionStage.Demonstrating);
      }
      else
      {
        Enter(PilotPhaseForAxis(_data.pilotCurrentAxis), CareActionStage.Active);
      }
    }

    private void CompleteFocusTarget()
    {
      var completedDirection = DirectionForFocusStep(_data.focusTargetStep);
      _data.focusTargetStep++;
      if (completedDirection == CareDistanceDirection.Away)
        _data.focusCycleCount = Mathf.Min(_config.focusCycleCount, _data.focusCycleCount + 1);
      _data.distanceDirectionProgress = 0f;
      _data.holdElapsedSeconds = 0f;
      _data.focusRearmed = false;
      if (_data.focusTargetStep >= _config.focusCycleCount * 2)
      {
        Enter(CareActionInternalPhase.FocusNeutralFinish, CareActionStage.Active);
        return;
      }
      // The immutable Session baseline remains valid for every one of the six
      // cycles. FocusReference is only a paced transition/rearm gate.
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
          Enter(CareActionInternalPhase.GuidedClockwise, CareActionStage.Active);
          break;
        case CareActionInternalPhase.GuidedClockwise:
          _data.guidedLapCount = _config.guidedLapsPerDirection;
          Enter(CareActionInternalPhase.GuidedPause, CareActionStage.Active);
          break;
        case CareActionInternalPhase.GuidedPause:
          Enter(CareActionInternalPhase.GuidedCounterClockwise, CareActionStage.Active);
          break;
        case CareActionInternalPhase.GuidedCounterClockwise:
          _data.guidedLapCount = _config.guidedLapsPerDirection;
          Enter(CareActionInternalPhase.GuidedPromptClose, CareActionStage.WaitingForStart);
          break;
        case CareActionInternalPhase.GuidedPromptClose:
          Enter(CareActionInternalPhase.GuidedClosedRest, CareActionStage.Active);
          break;
        case CareActionInternalPhase.GuidedClosedRest:
          Enter(CareActionInternalPhase.GuidedWaitReopen, CareActionStage.WaitingForStart);
          break;
        default:
          Finish();
          break;
      }
    }

    private void AdvancePilotPhaseForDevelopment()
    {
      if (Phase == CareActionInternalPhase.PilotIntro)
      {
        Enter(CareActionInternalPhase.PilotVertical, CareActionStage.Active);
        return;
      }
      if (Phase == CareActionInternalPhase.PilotTransition)
      {
        Finish();
        return;
      }
      _data.pilotCurrentRound = 0;
      _data.pilotCurrentAxis++;
      if (_data.pilotCurrentAxis >= 4)
        Enter(CareActionInternalPhase.PilotTransition, CareActionStage.Demonstrating);
      else
        Enter(PilotPhaseForAxis(_data.pilotCurrentAxis), CareActionStage.Active);
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

    private float CalculateProgress()
    {
      if (_data == null) return 0f;
      if (Stage == CareActionStage.Completed) return 1f;
      switch (ActionType)
      {
        case CareActionType.ClosedEyeRest:
          return Mathf.Clamp01(_data.elapsedSeconds / _config.closedEyeDurationSeconds);
        case CareActionType.FocusShift:
          if (Phase == CareActionInternalPhase.FocusNeutralFinish) return 0.99f;
          return Mathf.Clamp01((_data.focusTargetStep +
                                Mathf.Clamp01(_data.distanceDirectionProgress)) /
                               Mathf.Max(1f, _config.focusCycleCount * 2f));
        case CareActionType.GuidedEyeCircles:
          var guidedTotal = (_config.guidedClockwiseSeconds + _config.guidedCounterClockwiseSeconds) *
                            _config.guidedLapsPerDirection + _config.guidedPauseSeconds +
                            _config.guidedRelaxSeconds;
          return Phase == CareActionInternalPhase.GuidedPreviewClockwise
            ? 0.05f * Mathf.Clamp01(_data.phaseElapsedSeconds / _config.guidedPreviewSeconds)
            : 0.05f + 0.95f * Mathf.Clamp01(_data.elapsedSeconds / Mathf.Max(0.1f, guidedTotal));
        case CareActionType.PilotEyeRoutine:
          if (Phase == CareActionInternalPhase.PilotTransition) return 0.99f;
          return Mathf.Clamp01((_data.pilotCurrentAxis * _config.pilotRoundsPerAxis +
                                _data.pilotCurrentRound + _data.pilotNormalizedMoveProgress) /
                               Mathf.Max(1f, 4f * _config.pilotRoundsPerAxis));
        default:
          return 0f;
      }
    }

    private float CalculateRemainingSeconds()
    {
      if (_data == null) return 0f;
      switch (ActionType)
      {
        case CareActionType.ClosedEyeRest:
          return Mathf.Max(0f, _config.closedEyeDurationSeconds - _data.elapsedSeconds);
        case CareActionType.GuidedEyeCircles:
          var total = (_config.guidedClockwiseSeconds + _config.guidedCounterClockwiseSeconds) *
                      _config.guidedLapsPerDirection + _config.guidedPauseSeconds + _config.guidedRelaxSeconds;
          return Mathf.Max(0f, total - _data.elapsedSeconds);
        case CareActionType.PilotEyeRoutine:
          return Mathf.Max(0f, 4f * _config.pilotRoundsPerAxis * _config.pilotRoundSeconds -
            _data.elapsedSeconds);
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
      _data.focusTargetStep = Mathf.Clamp(_data.focusTargetStep, 0, _config.focusCycleCount * 2);
      _data.focusCycleCount = Mathf.Clamp(_data.focusCycleCount, 0, _config.focusCycleCount);
      _data.distanceDirectionProgress = Mathf.Clamp01(_data.distanceDirectionProgress);
      if (!Enum.IsDefined(typeof(CareDistanceFallbackReason), _data.distanceFallbackReason))
        _data.distanceFallbackReason = CareDistanceFallbackReason.None;
      _data.guidedStage = Mathf.Clamp(_data.guidedStage, 0, 7);
      _data.guidedLapCount = Mathf.Clamp(_data.guidedLapCount, 0, _config.guidedLapsPerDirection);
      _data.guidedNormalizedProgress = Mathf.Clamp01(_data.guidedNormalizedProgress);
      _data.pilotCurrentAxis = Mathf.Clamp(_data.pilotCurrentAxis, 0, 4);
      _data.pilotCurrentRound = Mathf.Clamp(_data.pilotCurrentRound, 0, _config.pilotRoundsPerAxis);
      _data.pilotCurrentEndpoint = Mathf.Clamp(_data.pilotCurrentEndpoint, 0, 4);
      _data.pilotNormalizedMoveProgress = Mathf.Clamp01(_data.pilotNormalizedMoveProgress);
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
      return (step & 1) == 0
        ? CareActionInternalPhase.FocusNearOne
        : CareActionInternalPhase.FocusFarOne;
    }

    private static CareDistanceDirection DirectionForFocusStep(int step)
    {
      return (step & 1) == 0 ? CareDistanceDirection.Closer : CareDistanceDirection.Away;
    }

    private static CareActionStage StageForPhase(CareActionInternalPhase phase)
    {
      switch (phase)
      {
        case CareActionInternalPhase.FocusIntro:
        case CareActionInternalPhase.ClosedEyeIntro:
        case CareActionInternalPhase.ScreenDownDemo:
        case CareActionInternalPhase.GuidedPreviewClockwise:
        case CareActionInternalPhase.GuidedPreviewCounterClockwise:
        case CareActionInternalPhase.PilotIntro:
        case CareActionInternalPhase.PilotTransition:
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
      return CareActionLibrary.DisplayName(type);
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
        case CareActionInternalPhase.FocusIntro: return "FOCUS SHIFT";
        case CareActionInternalPhase.ClosedEyeIntro: return "CLOSED-EYE REST";
        case CareActionInternalPhase.FocusReference:
          return string.Empty;
        case CareActionInternalPhase.ScreenDownDemo:
        case CareActionInternalPhase.ScreenDownWait:
        case CareActionInternalPhase.ScreenDownRest:
        case CareActionInternalPhase.ScreenDownReturn: return string.Empty;
        case CareActionInternalPhase.ClosedEyePrompt:
        case CareActionInternalPhase.ClosedEyeActive: return "CLOSE YOUR EYES";
        case CareActionInternalPhase.ClosedEyeWaitReopen: return "OPEN YOUR EYES";
        case CareActionInternalPhase.FocusNeutralStart: return "RETURN TO CENTER";
        case CareActionInternalPhase.FocusNeutralFinish: return "RETURN TO CENTER";
        case CareActionInternalPhase.FocusNearOne:
        case CareActionInternalPhase.FocusNearTwo: return "MOVE CLOSER";
        case CareActionInternalPhase.FocusFarOne:
        case CareActionInternalPhase.FocusFarTwo: return "MOVE AWAY";
        case CareActionInternalPhase.GuidedPreviewClockwise:
        case CareActionInternalPhase.GuidedPreviewCounterClockwise: return "GUIDED EYE MOVEMENT";
        case CareActionInternalPhase.GuidedClockwise: return "CLOCKWISE";
        case CareActionInternalPhase.GuidedCounterClockwise: return "COUNTERCLOCKWISE";
        case CareActionInternalPhase.GuidedPause: return "RETURN TO CENTER";
        case CareActionInternalPhase.GuidedPromptClose: return "CLOSE YOUR EYES";
        case CareActionInternalPhase.GuidedClosedRest: return "CLOSE YOUR EYES";
        case CareActionInternalPhase.GuidedWaitReopen: return "OPEN YOUR EYES";
        case CareActionInternalPhase.GuidedRelax: return "RELAX";
        case CareActionInternalPhase.PilotIntro: return "PILOT EYE ROUTINE";
        case CareActionInternalPhase.PilotVertical: return "LOOK UP AND DOWN";
        case CareActionInternalPhase.PilotHorizontal: return "LOOK LEFT AND RIGHT";
        case CareActionInternalPhase.PilotDiagonalA:
        case CareActionInternalPhase.PilotDiagonalB: return "FOLLOW THE DIAGONAL";
        case CareActionInternalPhase.PilotTransition: return "AXES COMPLETE\nNEXT: SLOW CIRCLES";
        default: return string.Empty;
      }
    }

    private static CareActionInternalPhase PilotPhaseForAxis(int axis)
    {
      switch (Mathf.Clamp(axis, 0, 3))
      {
        case 0: return CareActionInternalPhase.PilotVertical;
        case 1: return CareActionInternalPhase.PilotHorizontal;
        case 2: return CareActionInternalPhase.PilotDiagonalA;
        default: return CareActionInternalPhase.PilotDiagonalB;
      }
    }

    private static bool IsFinitePositive(float value)
    {
      return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
  }
}
