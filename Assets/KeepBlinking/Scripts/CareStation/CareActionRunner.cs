using System;
using KeepBlinking.Gameplay;
using KeepBlinking.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KeepBlinking.CareStation
{
  [Flags]
  public enum CareActionCueCommand
  {
    None = 0,
    CloseRequest = 1,
    ReadyToOpen = 2,
  }

  /// <summary>
  /// Session-local cue guard. It keeps audio/haptics separate from action
  /// completion so a resumed WaitReopen phase may replay its safety cue once
  /// without emitting CareActionCompleted again.
  /// </summary>
  public sealed class CareActionCueGuard
  {
    private bool _closeRequestPlayed;
    private bool _readyToOpenPlayed;
    private bool _readyToOpenPending;

    public int CloseRequestPlayCount { get; private set; }
    public int ReadyToOpenPlayCount { get; private set; }

    public void Reset(bool closeRequestAlreadyPlayed = false, bool readyToOpenAlreadyPlayed = false)
    {
      _closeRequestPlayed = closeRequestAlreadyPlayed;
      _readyToOpenPlayed = readyToOpenAlreadyPlayed;
      _readyToOpenPending = false;
      CloseRequestPlayCount = 0;
      ReadyToOpenPlayCount = 0;
    }

    public CareActionCueCommand ObservePhase(CareActionType action, CareActionInternalPhase phase)
    {
      if (action != CareActionType.ClosedEyeRest && action != CareActionType.GuidedEyeCircles)
        return CareActionCueCommand.None;
      if ((phase == CareActionInternalPhase.ClosedEyePrompt ||
           phase == CareActionInternalPhase.GuidedPromptClose) && !_closeRequestPlayed)
      {
        _closeRequestPlayed = true;
        CloseRequestPlayCount++;
        return CareActionCueCommand.CloseRequest;
      }
      if (phase == CareActionInternalPhase.ClosedEyeWaitReopen ||
          phase == CareActionInternalPhase.GuidedWaitReopen)
        _readyToOpenPending = true;
      return CareActionCueCommand.None;
    }

    public CareActionCueCommand PollReadyToOpen(bool applicationActive, bool trackingValid)
    {
      if (!_readyToOpenPending || _readyToOpenPlayed || !applicationActive || !trackingValid)
        return CareActionCueCommand.None;
      _readyToOpenPending = false;
      _readyToOpenPlayed = true;
      ReadyToOpenPlayCount++;
      return CareActionCueCommand.ReadyToOpen;
    }
  }

  /// <summary>
  /// Unity input adapter for the deterministic CareActionRuntime. Completion is
  /// the only gameplay signal; this component never produces or settles bottles.
  /// </summary>
  public sealed class CareActionRunner : MonoBehaviour, ICareActionExecution
  {
    private const int VoiceIntro = 1 << 0;
    private const int VoiceReturn = 1 << 1;
    private const int VoiceComplete = 1 << 2;
    private const int VoiceClockwise = 1 << 3;
    private const int VoiceCenter = 1 << 4;
    private const int VoiceCounterClockwise = 1 << 5;
    private const int VoiceClose = 1 << 6;
    private const int VoiceBenefit = 1 << 7;
    private const int VoiceMiddle = 1 << 8;
    private const int VoiceAlmost = 1 << 9;
    private const int VoiceReadyOpen = 1 << 10;
    [Header("Care Action Introduction")]
    [SerializeField, Range(2f, 4f)] private float _actionIntroSeconds = 2.5f;

    [Header("Closed-Eye Rest")]
    [SerializeField, Range(45f, 60f)] private float _closedEyeRestSeconds = 45f;
    [SerializeField] private bool _enableRestAlmostCompleteVoice = true;
    [SerializeField] private bool _enableCareVoice = true;
    [SerializeField, Min(0.1f)] private float _closeStartHoldSeconds = 1.5f;
    [SerializeField, Min(0.1f)] private float _reopenHoldSeconds = 0.5f;

    [Header("Focus Shift")]
    [SerializeField, Min(0.1f)] private float _gestureScaleSmoothingSpeed = 12f;
    // Linear distance fractions (see FaceDistanceRatio): 0.22 means the step completes once
    // the player has moved to 1/1.22 = 82% of the reference distance, about 8 cm from 45 cm.
    [SerializeField, Range(0.01f, 0.12f)] private float _distanceDeadZone = 0.05f;
    [SerializeField, Range(0.08f, 0.4f)] private float _distanceCompleteThreshold = 0.22f;
    [SerializeField, Range(0.05f, 1f)] private float _distanceStepHoldSeconds = 0.25f;
    [SerializeField, Range(0.05f, 1f)] private float _distanceProgressFallSeconds = 0.25f;
    [SerializeField, Range(0f, 2f)] private float _focusStepTransitionSeconds = 1.2f;
    [SerializeField, Range(0.85f, 1f)] private float _focusNeutralMinimum = 0.94f;
    [SerializeField, Range(1f, 1.15f)] private float _focusNeutralMaximum = 1.06f;
    [SerializeField, Range(1.15f, 1.5f)] private float _focusCloserRatio = 1.25f;
    [SerializeField, Range(0.55f, 0.9f)] private float _focusAwayRatio = 0.78f;
    [SerializeField, Range(1.3f, 1.8f)] private float _focusTooCloseRatio = 1.45f;
    [SerializeField, Range(0.2f, 1.5f)] private float _focusTargetHoldSeconds = 0.7f;
    [SerializeField, Range(2.5f, 8f)] private float _focusMinimumLegSeconds = 3f;
    [SerializeField, Range(1.2f, 3f)] private float _focusDirectionIntervalSeconds = 1.2f;
    [SerializeField, Range(1, 8)] private int _focusCycleCount = 6;
    [SerializeField, Min(1f)] private float _distanceFallbackDelay = 8f;

    [Header("Guided Eye Movement")]
    [SerializeField, Range(2f, 4f)] private float _guidedPreviewSeconds = 2.5f;
    [SerializeField, Range(4.5f, 5.5f)] private float _guidedClockwiseSeconds = 5f;
    [SerializeField, Range(0.8f, 1f)] private float _guidedPauseSeconds = 0.9f;
    [SerializeField, Range(4.5f, 5.5f)] private float _guidedCounterClockwiseSeconds = 5f;
    [SerializeField, Range(10f, 15f)] private float _guidedRelaxSeconds = 12f;
    [SerializeField, Range(1, 6)] private int _guidedLapsPerDirection = 3;

    [Header("Pilot Eye Routine")]
    [SerializeField, Range(2f, 4f)] private float _pilotIntroSeconds = 3f;
    [SerializeField, Range(2.5f, 5f)] private float _pilotRoundSeconds = 3.5f;
    [SerializeField, Range(1, 4)] private int _pilotRoundsPerAxis = 3;
    [SerializeField, Range(1f, 1.5f)] private float _pilotTransitionSeconds = 1.25f;

    private CareActionRuntime _runtime;
    private EdgeOrbitHarvestMvp _gameplay;
    private CareStationView _view;
    private bool _applicationActive = true;
    private bool _hasFocus = true;
    private CareActionInternalPhase _lastPresentedPhase;
    private CareActionStage _lastPresentedStage;
    private int _lastGuidedNote = -1;
    private int _lastGuidedLap = -1;
    private int _lastPilotAxis = -1;
    private int _lastPilotRound = -1;
    private int _lastPilotEndpoint = -1;
    private float _eyesOpenPausedSeconds;
    private float _nextEarlyOpenVoiceAt;
    private bool _completionCuePlayed;
    private bool _changeStepAllowed = true;
    private bool _restoredCompletionPending;
    private readonly CareActionCueGuard _cueGuard = new CareActionCueGuard();
    private long _lastGestureSampleSequence = long.MinValue;
    private float _smoothedGestureFaceScale;
    private bool _hasSmoothedGestureFaceScale;
    private float _currentGestureRatio = 1f;
    private float _lastGestureFreshSampleAt = -1f;
    private float _rawGestureFaceScale;
    private float _focusStepOpenedAt = -1f;
    private int _focusFreshSamplesInStep;
    private float _focusObservedMinimum = float.PositiveInfinity;
    private float _focusObservedMaximum = float.NegativeInfinity;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private bool? _simulatedEyesClosed;
    private bool? _simulatedTracking;
    private float? _simulatedDistanceRatio;
    private float _developmentTimeMultiplier = 1f;
#endif

    public event Action<CareActionType> CareActionCompleted;
    public CareActionType ActionType => _runtime?.ActionType ?? CareActionType.None;
    public CareActionStage Stage => _runtime?.Stage ?? CareActionStage.Cancelled;
    public string DisplayName => _runtime?.DisplayName ?? string.Empty;
    public CareActionInternalPhase InternalPhase => _runtime?.Phase ?? CareActionInternalPhase.None;
    public CareActionPauseReason PauseReason => _runtime?.PauseReason ?? CareActionPauseReason.None;
    public float Progress => _runtime?.Progress ?? 0f;
    public float RemainingSeconds => _runtime?.RemainingSeconds ?? 0f;
    public int RemainingSteps => _runtime?.RemainingSteps ?? 0;
    public bool RequiresCamera => _runtime != null && _runtime.RequiresCamera;
    public bool RequiresDevicePose => _runtime != null && _runtime.RequiresDevicePose;
    public bool IsRunning => _runtime != null && _runtime.IsRunning;
    public bool IsDevelopmentTest { get; private set; }
    public bool ChangeStepAllowed => _changeStepAllowed;
    public int CloseRequestCuePlayCount => _cueGuard.CloseRequestPlayCount;
    public int ReadyToOpenCuePlayCount => _cueGuard.ReadyToOpenPlayCount;
    public CareActionSaveData SaveData => _runtime?.Data;

    public void SetChangeStepAllowed(bool allowed)
    {
      _changeStepAllowed = allowed;
    }
    public float GestureDistanceRatio => CurrentDistanceRatio;
    public float GestureReferenceScale => SaveData != null && SaveData.gestureReferenceValid ? SaveData.gestureReferenceScale : 0f;
    public bool GestureReferenceValid => SaveData != null && SaveData.gestureReferenceValid;
    public float CurrentGestureFaceScale => _hasSmoothedGestureFaceScale ? _smoothedGestureFaceScale : 0f;
    public float RawGestureFaceScale => _rawGestureFaceScale;
    public float FocusStableSeconds => SaveData != null ? SaveData.holdElapsedSeconds : 0f;
    public float FocusTargetHoldSeconds => _runtime?.FocusTargetHoldSeconds ?? _focusTargetHoldSeconds;
    public float FocusLegElapsedSeconds => _runtime?.FocusLegElapsedSeconds ?? 0f;
    public float FocusMinimumLegSeconds => _runtime?.FocusMinimumLegSeconds ?? _focusMinimumLegSeconds;
    public float FocusConfirmationProgress => _runtime?.FocusConfirmationProgress ?? 0f;
    public bool FocusTargetReached => _runtime != null && _runtime.FocusTargetReached;
    public bool FocusPaceReady => _runtime != null && _runtime.FocusPaceReady;
    public bool FocusTooClose => PauseReason == CareActionPauseReason.TooClose;
    public int FocusStep => SaveData != null ? SaveData.focusTargetStep : 0;
    public int FocusCycle => SaveData != null ? SaveData.focusCycleCount : 0;
    public bool FocusRearmed => SaveData != null && SaveData.focusRearmed;
    public float FocusNearPeakRatio => GestureReferenceValid &&
                                       CareDistanceReferenceSampler.IsValidScale(_focusObservedMaximum)
      ? FaceDistanceRatio.FromFaceScale(_focusObservedMaximum, GestureReferenceScale)
      : 0f;
    public float FocusFarPeakRatio => GestureReferenceValid &&
                                      CareDistanceReferenceSampler.IsValidScale(_focusObservedMinimum)
      ? FaceDistanceRatio.FromFaceScale(_focusObservedMinimum, GestureReferenceScale)
      : 0f;
    public float DirectionProgress => _runtime?.DirectionProgress ?? 0f;
    public CareDistanceDirection ExpectedDistanceDirection => _runtime?.ExpectedDistanceDirection ?? CareDistanceDirection.None;
    public float DirectionDeltaPercent => ExpectedDistanceDirection == CareDistanceDirection.Closer
      ? (_currentGestureRatio - 1f) * 100f
      : (1f - _currentGestureRatio) * 100f;
    public bool CanOfferDistanceFallback => IsRunning && ActionType == CareActionType.FocusShift &&
      _focusStepOpenedAt >= 0f && Time.unscaledTime - _focusStepOpenedAt >= _distanceFallbackDelay;
    public bool DistanceSensorUnavailable => CanOfferDistanceFallback &&
      (_focusFreshSamplesInStep <= 1 || !CareDistanceReferenceSampler.HasMeaningfulScaleUpdates(
        _focusObservedMinimum,
        _focusObservedMaximum,
        GestureReferenceScale));
    public string CurrentDistanceState
    {
      get
      {
        if (ActionType != CareActionType.FocusShift || !IsRunning) return "INACTIVE";
        if (PauseReason == CareActionPauseReason.TrackingLost) return "TRACKING LOST";
        if (PauseReason == CareActionPauseReason.TooClose) return "TOO CLOSE";
        if (InternalPhase == CareActionInternalPhase.FocusReference) return "REARMING";
        if (DistanceSensorUnavailable) return "SENSOR UNAVAILABLE";
        if (FocusTargetReached && !FocusRearmed) return "WAITING FOR NEUTRAL";
        if (FocusTargetReached && FocusStableSeconds >= FocusTargetHoldSeconds && !FocusPaceReady)
          return "HOLD COMPLETE / PACING";
        if (FocusTargetReached) return "HOLDING";
        if (DirectionProgress <= 0f) return "DEAD ZONE";
        return ExpectedDistanceDirection == CareDistanceDirection.Closer ? "MOVING CLOSER" : "MOVING AWAY";
      }
    }

    public void Bind(EdgeOrbitHarvestMvp gameplay, CareStationView view)
    {
      _gameplay = gameplay;
      _view = view;
    }

    public void ConfigureStationDurations(
      float screenDownSeconds,
      float closedEyeSeconds,
      float closeStartHoldSeconds,
      float reopenHoldSeconds)
    {
      _closedEyeRestSeconds = Mathf.Clamp(closedEyeSeconds, 1f, 60f);
      _closeStartHoldSeconds = Mathf.Max(0.1f, closeStartHoldSeconds);
      _reopenHoldSeconds = Mathf.Max(0.1f, reopenHoldSeconds);
    }

    public bool StartAction(
      CareActionType type,
      CareActionSaveData restore = null,
      bool developmentTest = false,
      float closedEyeDurationOverride = 0f,
      bool showIntro = false,
      int focusCycleCountOverride = 0,
      int guidedLapsOverride = 0,
      int pilotRoundsOverride = 0)
    {
      if (type == CareActionType.None || CareActionLibrary.IsRetiredTask(type) || IsRunning) return false;
      IsDevelopmentTest = developmentTest;
      _runtime = new CareActionRuntime();
      _runtime.Begin(type, BuildConfiguration(
        closedEyeDurationOverride,
        showIntro,
        focusCycleCountOverride,
        guidedLapsOverride,
        pilotRoundsOverride), restore);
      _lastPresentedPhase = CareActionInternalPhase.None;
      _lastPresentedStage = CareActionStage.Cancelled;
      _lastGuidedNote = -1;
      _lastGuidedLap = restore != null ? restore.guidedLapCount : -1;
      _lastPilotAxis = restore != null ? restore.pilotCurrentAxis : -1;
      _lastPilotRound = restore != null ? restore.pilotCurrentRound : -1;
      _lastPilotEndpoint = restore != null ? restore.pilotCurrentEndpoint : -1;
      _eyesOpenPausedSeconds = 0f;
      _nextEarlyOpenVoiceAt = Time.unscaledTime +
                              Mathf.Max(0f, restore != null ? restore.restEarlyOpenVoiceCooldown : 0f);
      _completionCuePlayed = restore != null &&
                             (restore.completionSignalEmitted || restore.readyToOpenCuePlayed ||
                              (type == CareActionType.PilotEyeRoutine && restore.pilotCompletionConsumed));
      _cueGuard.Reset(
        restore != null && restore.closeRequestCuePlayed,
        restore != null && restore.readyToOpenCuePlayed);
      _restoredCompletionPending = _runtime.Stage == CareActionStage.Completed &&
                                   !_runtime.Data.completionSignalEmitted;
      if (type == CareActionType.FocusShift) PrepareFocusReference(restore);
      if (type == CareActionType.FocusShift) _focusStepOpenedAt = Time.unscaledTime;
      if (type == CareActionType.FocusShift && InternalPhase != CareActionInternalPhase.FocusReference)
      {
        _focusStepOpenedAt = Time.unscaledTime;
        _focusFreshSamplesInStep = 0;
      }
      if (!developmentTest) _gameplay?.SetCareActionActive(true);
      var careAudio = CareAudioFeedbackController.EnsureExists();
      var startsPaused = _runtime.Stage == CareActionStage.Paused;
      var voiceStartsPaused = startsPaused && IsVoicePauseReason(_runtime.PauseReason);
      careAudio.StartActionAmbience(type, startsPaused);
      CareVoiceService.EnsureExists().SetPaused(voiceStartsPaused);
      Present(true);
      ExecuteCueCommand(_cueGuard.ObservePhase(ActionType, InternalPhase));
      if (showIntro) PlayActionIntroNarration(type);
      else PlayPhaseNarration(CareActionInternalPhase.None, InternalPhase);
      return true;
    }

    public void PlayRoutineStepRewardHaptic()
    {
      if (isActiveAndEnabled) StartCoroutine(PlayLightHapticPulses(1));
    }

    public void CancelAction(bool stopRoutineMusic = true)
    {
      if (_runtime == null) return;
      _runtime.Cancel();
      if (!IsDevelopmentTest) _gameplay?.SetCareActionActive(false);
      CareAudioFeedbackController.EnsureExists().StopGuidedCue();
      if (stopRoutineMusic) CareAudioFeedbackController.EnsureExists().StopActionAmbience();
      CareVoiceService.EnsureExists().Stop();
      IsDevelopmentTest = false;
    }

    public void PauseAction()
    {
      _runtime?.PauseManually();
    }

    public void ResumeAction()
    {
      _runtime?.ResumeManually();
    }

    public void SuspendForApplication(bool background)
    {
      if (_runtime == null || !IsRunning) return;
      _lastGestureFreshSampleAt = -1f;
      _runtime.Suspend(background
        ? CareActionPauseReason.ApplicationBackground
        : CareActionPauseReason.ApplicationFocusLost);
      CareAudioFeedbackController.EnsureExists().SetActionAudioPaused(true);
      CareVoiceService.EnsureExists().SetPaused(true);
    }

    public void CompleteCurrentStepForDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_runtime == null) return;
      var previousPhase = InternalPhase;
      _runtime.CompleteCurrentStepForDevelopment();
      if (previousPhase != InternalPhase) PlayPhaseTransition(previousPhase, InternalPhase);
      Present(true);
      EmitCompletionIfReady();
#endif
    }

    public void SetDevelopmentTimeMultiplier(float multiplier)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _developmentTimeMultiplier = Mathf.Clamp(multiplier, 1f, 10f);
#endif
    }

    public void SimulateEyesClosed(bool closed)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _simulatedEyesClosed = closed;
      _simulatedTracking = true;
#endif
    }

    public void SimulateTracking(bool valid)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _simulatedTracking = valid;
#endif
    }

    public void SimulateFocusRatio(float ratio)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _simulatedDistanceRatio = Mathf.Max(0.01f, ratio);
      _simulatedTracking = true;
#endif
    }

    public void SimulateCurrentDistanceProgress(float progress)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (ActionType != CareActionType.FocusShift || !IsRunning) return;
      EnsureSimulatedFocusReference();
      _simulatedDistanceRatio = ExpectedDistanceDirection == CareDistanceDirection.Closer
        ? Mathf.Lerp(1.06f, _focusCloserRatio, Mathf.Clamp01(progress))
        : Mathf.Lerp(0.94f, _focusAwayRatio, Mathf.Clamp01(progress));
      _simulatedTracking = true;
#endif
    }

    public bool CompleteCurrentDistanceStepForFallback(CareDistanceFallbackReason reason)
    {
      if (!CanOfferDistanceFallback || _runtime == null) return false;
      var previousPhase = InternalPhase;
      var completed = _runtime.CompleteFocusStepForFallback(reason);
      if (completed)
      {
        if (previousPhase != InternalPhase) PlayPhaseTransition(previousPhase, InternalPhase);
        Present(true);
        EmitCompletionIfReady();
      }
      return completed;
    }

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      PollDevelopmentKeys();
#endif
      if (_restoredCompletionPending)
      {
        _restoredCompletionPending = false;
        EmitCompletionIfReady();
        return;
      }
      if (!IsRunning) return;
      var delta = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.25f);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      delta *= _developmentTimeMultiplier;
#endif
      var previousPhase = InternalPhase;
      var previousStage = Stage;
      var input = ReadInputFrame(delta);
      ExecuteCueCommand(_cueGuard.PollReadyToOpen(input.ApplicationActive, input.TrackingValid));
      _runtime.Advance(delta, input);
      UpdateAudioSuspension(input);
      if (previousPhase != InternalPhase)
        PlayPhaseTransition(previousPhase, InternalPhase, input.ApplicationActive, input.TrackingValid);
      Present(previousPhase != InternalPhase || previousStage != Stage);
      PlayGuidedNotesIfNeeded();
      PlayPilotProgressIfNeeded();
      UpdateClosedEyeAudio(delta, input);
      EmitCompletionIfReady();
    }

    private void EmitCompletionIfReady()
    {
      if (_runtime != null && _runtime.TryConsumeCompletionSignal())
      {
        PlayCompletionCueOnce();
        if (!IsDevelopmentTest) _gameplay?.SetCareActionActive(false);
        CareActionCompleted?.Invoke(ActionType);
        if (IsDevelopmentTest)
        {
          IsDevelopmentTest = false;
          _view?.HideAllModals();
        }
      }
    }

    private CareActionInputFrame ReadInputFrame(float delta)
    {
      var tracking = _gameplay != null && _gameplay.IsTrackingAvailable;
      var eyesClosed = _gameplay != null && _gameplay.AreEyesClosed;
      var referenceValid = false;
      var distanceRatio = 0f;
      var distanceSampleFresh = false;
      var distanceSampleDelta = 0f;
      if (ActionType == CareActionType.FocusShift)
        UpdateFocusReferenceAndRatio(
          delta,
          tracking,
          out referenceValid,
          out distanceRatio,
          out distanceSampleFresh,
          out distanceSampleDelta);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_simulatedTracking.HasValue) tracking = _simulatedTracking.Value;
      if (_simulatedEyesClosed.HasValue) eyesClosed = _simulatedEyesClosed.Value;
      if (_simulatedDistanceRatio.HasValue)
      {
        EnsureSimulatedFocusReference();
        referenceValid = true;
        distanceRatio = _simulatedDistanceRatio.Value;
        _currentGestureRatio = distanceRatio;
        _rawGestureFaceScale = FaceDistanceRatio.ToFaceScale(_runtime.Data.gestureReferenceScale, distanceRatio);
        ObserveFocusScale(_rawGestureFaceScale);
        _smoothedGestureFaceScale = _rawGestureFaceScale;
        _hasSmoothedGestureFaceScale = true;
        _focusFreshSamplesInStep++;
        distanceSampleFresh = true;
        distanceSampleDelta = delta;
      }
#endif
      return new CareActionInputFrame(
        _applicationActive && _hasFocus,
        tracking,
        eyesClosed,
        false,
        false,
        false,
        referenceValid,
        distanceRatio,
        distanceSampleFresh,
        distanceSampleDelta);
    }

    private void Present(bool force)
    {
      if (_runtime == null || _view == null) return;
      if (!force && _lastPresentedPhase == InternalPhase && _lastPresentedStage == Stage)
      {
        RenderCurrentAction();
        return;
      }
      _lastPresentedPhase = InternalPhase;
      _lastPresentedStage = Stage;
      RenderCurrentAction();
    }

    private void RenderCurrentAction()
    {
      var prompt = _runtime.Prompt;
      if (DistanceSensorUnavailable) prompt = "SENSOR UNAVAILABLE";
      _view.RenderCareAction(
        ActionType,
        InternalPhase,
        prompt,
        Progress,
        CurrentDistanceRatio,
        DirectionProgress,
        ExpectedDistanceDirection,
        FocusCycle,
        SaveData != null && SaveData.introWasRequested);
      _view.RenderCareActionMotionData(SaveData);
      // Formal Focus Shift is completed by the Session-baseline sensor path or
      // replaced through CHANGE STEP. The old generic CONTINUE fallback stays
      // hidden; F6 can still advance a development-only preview explicitly.
      _view.SetReturnFallbackAvailable(false);
      _view.SetCareActionChangeAvailable(
        !IsDevelopmentTest && IsRunning && _changeStepAllowed &&
        ActionType != CareActionType.ClosedEyeRest);
    }

    private float CurrentDistanceRatio
    {
      get
      {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_simulatedDistanceRatio.HasValue) return _simulatedDistanceRatio.Value;
#endif
        return ActionType == CareActionType.FocusShift ? _currentGestureRatio : 1f;
      }
    }

    private void PrepareFocusReference(CareActionSaveData restore)
    {
      _lastGestureSampleSequence = long.MinValue;
      _hasSmoothedGestureFaceScale = false;
      _smoothedGestureFaceScale = 0f;
      _currentGestureRatio = 1f;
      _lastGestureFreshSampleAt = -1f;
      _rawGestureFaceScale = 0f;
      var restoredScale = restore != null && restore.gestureReferenceValid
        ? restore.gestureReferenceScale
        : _gameplay != null && _gameplay.BaselineFaceScale > 0f
          ? _gameplay.BaselineFaceScale
          : 0f;
      if (CareDistanceReferenceSampler.IsValidScale(restoredScale))
      {
        _runtime.Data.gestureReferenceScale = restoredScale;
        _runtime.Data.gestureReferenceValid = true;
        _smoothedGestureFaceScale = restoredScale;
        _hasSmoothedGestureFaceScale = true;
      }
      else
      {
        _runtime.Data.gestureReferenceScale = 0f;
        _runtime.Data.gestureReferenceValid = false;
      }
    }

    private void UpdateFocusReferenceAndRatio(
      float delta,
      bool tracking,
      out bool referenceValid,
      out float ratio,
      out bool sampleFresh,
      out float sampleDelta)
    {
      sampleFresh = false;
      sampleDelta = 0f;
      referenceValid = _runtime != null && _runtime.Data.gestureReferenceValid &&
                       CareDistanceReferenceSampler.IsValidScale(_runtime.Data.gestureReferenceScale);
      ratio = referenceValid ? _currentGestureRatio : 0f;
      if (!TryReadFreshFaceScale(out var scale, out var sequence))
      {
        if (!tracking) _lastGestureFreshSampleAt = -1f;
        return;
      }

      _rawGestureFaceScale = scale;
      if (!referenceValid)
      {
        // Focus Shift is deliberately not allowed to create an action-local
        // origin. It waits for the Session baseline established by gameplay.
        return;
      }

      if (sequence == _lastGestureSampleSequence) return;
      _lastGestureSampleSequence = sequence;
      sampleFresh = true;
      if (InternalPhase != CareActionInternalPhase.FocusReference)
      {
        _focusFreshSamplesInStep++;
        ObserveFocusScale(scale);
      }
      var now = Time.unscaledTime;
      sampleDelta = _lastGestureFreshSampleAt >= 0f
        ? Mathf.Clamp(now - _lastGestureFreshSampleAt, 0f, 0.25f)
        : 0f;
      _lastGestureFreshSampleAt = now;
      if (!_hasSmoothedGestureFaceScale)
      {
        _smoothedGestureFaceScale = scale;
        _hasSmoothedGestureFaceScale = true;
      }
      else
      {
        var smoothingDelta = sampleDelta > 0f ? sampleDelta : delta;
        var smoothing = 1f - Mathf.Exp(-_gestureScaleSmoothingSpeed * Mathf.Max(0f, smoothingDelta));
        _smoothedGestureFaceScale = Mathf.Lerp(_smoothedGestureFaceScale, scale, smoothing);
      }
      _currentGestureRatio = FaceDistanceRatio.FromFaceScale(
        _smoothedGestureFaceScale,
        _runtime.Data.gestureReferenceScale);
      ratio = _currentGestureRatio;
    }

    private bool TryReadFreshFaceScale(out float scale, out long sequence)
    {
      scale = 0f;
      sequence = long.MinValue;
      var snapshot = EyeInputDebugState.Latest;
      if (snapshot.FaceDetected && CareDistanceReferenceSampler.IsValidScale(snapshot.RobustFaceScale))
      {
        scale = snapshot.RobustFaceScale;
        sequence = snapshot.SampleSequence;
        return true;
      }
      if (_gameplay == null || !_gameplay.HasValidDistanceSample ||
          !CareDistanceReferenceSampler.IsValidScale(_gameplay.CurrentFaceScale)) return false;
      scale = _gameplay.CurrentFaceScale;
      sequence = Time.frameCount;
      return true;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void EnsureSimulatedFocusReference()
    {
      if (_runtime == null || _runtime.Data.gestureReferenceValid) return;
      _runtime.Data.gestureReferenceScale = 1f;
      _runtime.Data.gestureReferenceValid = true;
    }
#endif

    private void PlayPhaseTransition(
      CareActionInternalPhase previous,
      CareActionInternalPhase current,
      bool applicationActive = true,
      bool trackingValid = true)
    {
      var audio = CareAudioFeedbackController.EnsureExists();
      if (ActionType == CareActionType.FocusShift && current == CareActionInternalPhase.FocusReference)
      {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        _simulatedDistanceRatio = null;
#endif
        _focusStepOpenedAt = Time.unscaledTime;
        _focusFreshSamplesInStep = 0;
        ResetFocusScaleObservation();
      }
      else if (ActionType == CareActionType.FocusShift &&
               (current == CareActionInternalPhase.FocusNearOne ||
                current == CareActionInternalPhase.FocusFarOne))
      {
        _focusStepOpenedAt = Time.unscaledTime;
        _focusFreshSamplesInStep = 0;
        ResetFocusScaleObservation();
      }
      ExecuteCueCommand(_cueGuard.ObservePhase(ActionType, current));
      ExecuteCueCommand(_cueGuard.PollReadyToOpen(applicationActive, trackingValid));
      PlayPhaseNarration(previous, current);
      if (current == CareActionInternalPhase.GuidedPause)
        audio.PlayGuidedCenterPause();
      else if (ActionType == CareActionType.FocusShift && previous != CareActionInternalPhase.None &&
               (current == CareActionInternalPhase.FocusReference ||
                current == CareActionInternalPhase.FocusNeutralFinish))
      {
        var completedDirection = ExpectedDistanceDirection == CareDistanceDirection.Closer
          ? CareDistanceDirection.Away
          : CareDistanceDirection.Closer;
        _view?.PlayFocusLegFeedback(completedDirection);
        if (completedDirection == CareDistanceDirection.Closer) audio.PlayFocusCloser();
        else
        {
          audio.PlayFocusAway();
          audio.PlayFocusCycle();
        }
        StartCoroutine(PlayLightHapticPulses(1));
      }
      if (current == CareActionInternalPhase.GuidedClockwise || current == CareActionInternalPhase.GuidedCounterClockwise)
      {
        _lastGuidedNote = -1;
        StartCoroutine(PlayHapticPulses(current == CareActionInternalPhase.GuidedCounterClockwise ? 2 : 1));
      }
      if (ActionType == CareActionType.PilotEyeRoutine && current == CareActionInternalPhase.PilotTransition &&
          !_completionCuePlayed)
      {
        _completionCuePlayed = true;
        audio.PlayPilotCompletion();
      }
    }

    private void PlayGuidedNotesIfNeeded()
    {
      if (ActionType != CareActionType.GuidedEyeCircles || Stage != CareActionStage.Active) return;
      var duration = InternalPhase == CareActionInternalPhase.GuidedClockwise
        ? _guidedClockwiseSeconds
        : InternalPhase == CareActionInternalPhase.GuidedCounterClockwise ? _guidedCounterClockwiseSeconds : 0f;
      if (duration <= 0f) return;
      var audio = CareAudioFeedbackController.EnsureExists();
      if (SaveData.guidedLapCount > _lastGuidedLap)
      {
        _lastGuidedLap = SaveData.guidedLapCount;
        if (_lastGuidedLap > 0) audio.PlayGuidedLap();
      }
      var note = Mathf.Clamp(Mathf.FloorToInt(Mathf.Repeat(SaveData.phaseElapsedSeconds, duration) /
        duration * 8f), 0, 7);
      if (note == _lastGuidedNote) return;
      _lastGuidedNote = note;
      if (InternalPhase == CareActionInternalPhase.GuidedClockwise) audio.PlayGuidedClockwiseNote(note, 8);
      else audio.PlayGuidedCounterClockwiseNote(note, 8);
    }

    private void PlayPilotProgressIfNeeded()
    {
      if (ActionType != CareActionType.PilotEyeRoutine || SaveData == null ||
          Stage != CareActionStage.Active || !IsPilotAxisPhase(InternalPhase)) return;
      if (SaveData.pilotCurrentAxis != _lastPilotAxis || SaveData.pilotCurrentRound != _lastPilotRound)
      {
        if (_lastPilotAxis >= 0 && SaveData.pilotCurrentAxis > _lastPilotAxis)
          CareAudioFeedbackController.EnsureExists().PlayPilotAxis();
        _lastPilotAxis = SaveData.pilotCurrentAxis;
        _lastPilotRound = SaveData.pilotCurrentRound;
        _lastPilotEndpoint = -1;
        if (SaveData.pilotCurrentRound == 0 && SaveData.pilotCurrentAxis >= 0 &&
            SaveData.pilotCurrentAxis < 4)
        {
          var axisLines = new[]
          {
            "LOOK UP AND DOWN.",
            "LOOK LEFT AND RIGHT.",
            "LOOK UPPER LEFT AND LOWER RIGHT.",
            "LOOK LOWER LEFT AND UPPER RIGHT.",
          };
          SpeakEventOnce(2000 + SaveData.pilotCurrentAxis,
            $"pilot-axis-{SaveData.pilotCurrentAxis}", axisLines[SaveData.pilotCurrentAxis], 3.2f);
        }
      }
      if (SaveData.pilotCurrentEndpoint == _lastPilotEndpoint) return;
      _lastPilotEndpoint = SaveData.pilotCurrentEndpoint;
      if (_lastPilotEndpoint <= 0 || _lastPilotEndpoint == 2 || _lastPilotEndpoint == 4)
      {
        CareAudioFeedbackController.EnsureExists().PlayPilotCenter();
      }
      else
      {
        var secondHalf = _lastPilotEndpoint >= 3;
        var directionIndex = SaveData.pilotCurrentAxis * 2 + (secondHalf ? 1 : 0);
        CareAudioFeedbackController.EnsureExists().PlayPilotDirection(
          Mathf.Clamp(directionIndex, 0, 7));
        var directionLines = new[]
        {
          "UP.", "DOWN.", "LEFT.", "RIGHT.",
          "UPPER LEFT.", "LOWER RIGHT.", "LOWER LEFT.", "UPPER RIGHT.",
        };
        var eventId = 3000 + SaveData.pilotCurrentAxis * 100 + SaveData.pilotCurrentRound * 10 +
                      _lastPilotEndpoint;
        SpeakSynchronizedDirectionEventOnce(
          eventId,
          $"pilot-direction-{directionIndex}",
          directionLines[directionIndex]);
      }
    }

    private static bool IsPilotAxisPhase(CareActionInternalPhase phase)
    {
      return phase == CareActionInternalPhase.PilotVertical ||
             phase == CareActionInternalPhase.PilotHorizontal ||
             phase == CareActionInternalPhase.PilotDiagonalA ||
             phase == CareActionInternalPhase.PilotDiagonalB;
    }

    private void PlayCompletionCueOnce()
    {
      if (ActionType == CareActionType.ClosedEyeRest || ActionType == CareActionType.GuidedEyeCircles)
      {
        // Normally this was already played on entry to WaitReopen. This call
        // only covers a restored completed action whose signal was not yet
        // consumed; the guard prevents any duplicate cue.
        ExecuteCueCommand(_cueGuard.PollReadyToOpen(true, true));
        if (ActionType == CareActionType.GuidedEyeCircles)
          SpeakOnce(VoiceComplete, "guided-complete", "GUIDED MOVEMENT COMPLETE.", 2.5f,
            CareVoicePriority.Completion);
        return;
      }
      if (_completionCuePlayed) return;
      _completionCuePlayed = true;
      if (ActionType == CareActionType.FocusShift)
      {
        CareAudioFeedbackController.EnsureExists().PlayFocusCompletion();
        SpeakOnce(VoiceComplete, "focus-complete", "FOCUS SHIFT COMPLETE.", 2.5f,
          CareVoicePriority.Completion);
      }
      else if (ActionType == CareActionType.PilotEyeRoutine)
        CareAudioFeedbackController.EnsureExists().PlayPilotCompletion();
      else
        CareAudioFeedbackController.EnsureExists().PlayStepComplete();
    }

    private void ExecuteCueCommand(CareActionCueCommand command)
    {
      if ((command & CareActionCueCommand.CloseRequest) != 0)
      {
        if (_runtime?.Data != null) _runtime.Data.closeRequestCuePlayed = true;
        CareAudioFeedbackController.EnsureExists().PlayGuidedCloseRequest();
        StartCoroutine(PlayLightHapticPulses(2));
      }
      if ((command & CareActionCueCommand.ReadyToOpen) != 0)
      {
        _completionCuePlayed = true;
        if (_runtime?.Data != null)
        {
          _runtime.Data.readyToOpenCuePlayed = true;
          if (ActionType == CareActionType.GuidedEyeCircles) _runtime.Data.guidedOpenCuePlayed = true;
        }
        if (ActionType == CareActionType.GuidedEyeCircles)
        {
          CareAudioFeedbackController.EnsureExists().PlayGuidedOpen();
          SpeakOnce(VoiceReadyOpen, "guided-ready-open",
            "REST COMPLETE. GENTLY OPEN YOUR EYES.", 3.4f, CareVoicePriority.Completion);
        }
        else
        {
          CareAudioFeedbackController.EnsureExists().PlayRestOpen();
          if (_runtime?.Data != null && !_runtime.Data.restCompletionVoicePlayed)
          {
            _runtime.Data.restCompletionVoicePlayed = true;
            SpeakOnce(VoiceReadyOpen, "rest-ready-open",
              "REST COMPLETE. GENTLY OPEN YOUR EYES.", 3.4f, CareVoicePriority.Completion);
          }
        }
        StartCoroutine(PlayLightHapticPulses(1));
      }
    }

    private void PlayActionIntroNarration(CareActionType type)
    {
      switch (type)
      {
        case CareActionType.FocusShift:
          SpeakOnce(VoiceIntro, "focus-intro",
            "KEEP YOUR HEAD STILL. MOVE THE PHONE, NOT YOUR HEAD.", 4.2f);
          break;
        case CareActionType.GuidedEyeCircles:
          SpeakOnce(VoiceIntro, "guided-intro",
            "KEEP YOUR HEAD STILL. FOLLOW THE DOT WITH YOUR EYES.", 4.2f);
          break;
        case CareActionType.PilotEyeRoutine:
          SpeakOnce(VoiceIntro, "pilot-intro",
            "KEEP YOUR HEAD STILL. MOVE ONLY YOUR EYES.", 3.8f);
          break;
        case CareActionType.ClosedEyeRest:
          SpeakOnce(VoiceClose, "rest-intro", "GENTLY CLOSE YOUR EYES.", 2.5f);
          break;
      }
    }

    private void PlayPhaseNarration(CareActionInternalPhase previous, CareActionInternalPhase current)
    {
      if (!_enableCareVoice || SaveData == null || previous == current) return;
      switch (current)
      {
        case CareActionInternalPhase.FocusNearOne:
        case CareActionInternalPhase.FocusFarOne:
          var direction = ExpectedDistanceDirection;
          var firstCycle = SaveData.focusTargetStep < 2;
          var focusText = direction == CareDistanceDirection.Closer
            ? firstCycle ? "SLOWLY MOVE THE PHONE CLOSER." : "MOVE CLOSER."
            : firstCycle ? "SLOWLY MOVE THE PHONE AWAY." : "MOVE AWAY.";
          SpeakEventOnce(1000 + SaveData.focusTargetStep,
            direction == CareDistanceDirection.Closer ? "focus-closer" : "focus-away",
            focusText, firstCycle ? 2.9f : 1.5f);
          break;
        case CareActionInternalPhase.FocusNeutralFinish:
          SpeakOnce(VoiceReturn, "focus-return", "RETURN TO A COMFORTABLE DISTANCE.", 3f);
          break;
        case CareActionInternalPhase.GuidedClockwise:
          SpeakOnce(VoiceClockwise, "guided-clockwise", "FOLLOW CLOCKWISE. THREE SLOW CIRCLES.", 3.6f);
          break;
        case CareActionInternalPhase.GuidedPause:
          SpeakOnce(VoiceCenter, "guided-center", "RETURN TO CENTER.", 2f);
          break;
        case CareActionInternalPhase.GuidedCounterClockwise:
          SpeakOnce(VoiceCounterClockwise, "guided-counterclockwise",
            "NOW COUNTERCLOCKWISE. THREE SLOW CIRCLES.", 3.8f);
          break;
        case CareActionInternalPhase.GuidedPromptClose:
          SpeakOnce(VoiceClose, "guided-close", "GENTLY CLOSE YOUR EYES AND RELAX.", 3.2f);
          break;
        case CareActionInternalPhase.ClosedEyePrompt:
          SpeakOnce(VoiceClose, "rest-close", "GENTLY CLOSE YOUR EYES.", 2.5f);
          break;
        case CareActionInternalPhase.PilotTransition:
          SpeakOnce(VoiceComplete, "pilot-complete",
            "PILOT ROUTINE COMPLETE. NOW FOLLOW THE CIRCULAR GUIDE.", 4.6f,
            CareVoicePriority.Completion);
          break;
      }
    }

    private void SpeakOnce(
      int mask,
      string cueKey,
      string text,
      float estimatedSeconds,
      CareVoicePriority priority = CareVoicePriority.Instruction)
    {
      if (!_enableCareVoice || SaveData == null || (SaveData.consumedVoiceCueMask & mask) != 0) return;
      SaveData.consumedVoiceCueMask |= mask;
      CareVoiceService.EnsureExists().Speak(cueKey, text, estimatedSeconds, priority);
    }

    private void SpeakEventOnce(
      int eventId,
      string cueKey,
      string text,
      float estimatedSeconds,
      CareVoicePriority priority = CareVoicePriority.Instruction)
    {
      if (!_enableCareVoice || SaveData == null || SaveData.lastVoiceEventId == eventId) return;
      SaveData.lastVoiceEventId = eventId;
      CareVoiceService.EnsureExists().Speak(cueKey, text, estimatedSeconds, priority);
    }

    private void SpeakSynchronizedDirectionEventOnce(int eventId, string cueKey, string text)
    {
      if (!_enableCareVoice || SaveData == null || SaveData.lastVoiceEventId == eventId) return;
      SaveData.lastVoiceEventId = eventId;
      CareVoiceService.EnsureExists().SpeakSynchronizedDirection(cueKey, text, 0.55f);
    }

    private static bool IsVoicePauseReason(CareActionPauseReason reason)
    {
      return reason == CareActionPauseReason.ApplicationBackground ||
             reason == CareActionPauseReason.ApplicationFocusLost ||
             reason == CareActionPauseReason.Manual ||
             reason == CareActionPauseReason.TrackingLost;
    }

    private void UpdateAudioSuspension(CareActionInputFrame input)
    {
      var voicePaused = !input.ApplicationActive || IsVoicePauseReason(PauseReason);
      var ambiencePaused = voicePaused || Stage == CareActionStage.Paused;
      CareAudioFeedbackController.EnsureExists().SetActionAudioPaused(ambiencePaused);
      // Eyes-open pauses keep narration available for the gentle continue
      // reminder; tracking and application pauses silence every channel.
      CareVoiceService.EnsureExists().SetPaused(voicePaused);
    }

    private void UpdateClosedEyeAudio(float delta, CareActionInputFrame input)
    {
      if (ActionType != CareActionType.ClosedEyeRest || SaveData == null) return;
      SaveData.restEarlyOpenVoiceCooldown = Mathf.Max(0f, _nextEarlyOpenVoiceAt - Time.unscaledTime);
      var audio = CareAudioFeedbackController.EnsureExists();
      if (InternalPhase == CareActionInternalPhase.ClosedEyeActive && Stage == CareActionStage.Active)
      {
        audio.StartClosedEyeMusic();
        _eyesOpenPausedSeconds = 0f;
        if (!SaveData.restBenefitVoicePlayed && SaveData.elapsedSeconds >= 2f)
        {
          SaveData.restBenefitVoicePlayed = true;
          SpeakOnce(VoiceBenefit, "rest-benefit", "LET YOUR EYES REST FROM THE SCREEN.", 3.2f);
        }
        var configuredDuration = SaveData.elapsedSeconds + RemainingSeconds;
        if ((SaveData.consumedVoiceCueMask & VoiceMiddle) == 0 && configuredDuration > 0f &&
            SaveData.elapsedSeconds >= configuredDuration * 0.5f)
        {
          SpeakOnce(VoiceMiddle, "rest-middle",
            "KEEP YOUR EYES GENTLY CLOSED. RELAX YOUR FOREHEAD AND SHOULDERS.", 4.8f);
        }
        if (_enableRestAlmostCompleteVoice && !SaveData.restAlmostCompleteVoicePlayed && RemainingSeconds <= 10f)
        {
          SaveData.restAlmostCompleteVoicePlayed = true;
          SpeakOnce(VoiceAlmost, "rest-almost", "YOU ARE ALMOST DONE.", 2.4f);
        }
        return;
      }
      if (Stage == CareActionStage.Paused && PauseReason == CareActionPauseReason.EyesOpen &&
          input.TrackingValid)
      {
        _eyesOpenPausedSeconds += delta;
        if (_eyesOpenPausedSeconds >= 1.5f && Time.unscaledTime >= _nextEarlyOpenVoiceAt)
        {
          _nextEarlyOpenVoiceAt = Time.unscaledTime + 8f;
          SaveData.restEarlyOpenVoiceCooldown = 8f;
          _eyesOpenPausedSeconds = 0f;
          CareVoiceService.EnsureExists().Speak("rest-continue", "CLOSE YOUR EYES TO CONTINUE.", 2.5f);
        }
      }
      else if (PauseReason == CareActionPauseReason.TrackingLost)
      {
        _eyesOpenPausedSeconds = 0f;
      }
      // Closed-Eye Rest is still part of the same Routine. The Routine owner
      // performs the single fade-out after it consumes the final completion.
    }

    private System.Collections.IEnumerator PlayHapticPulses(int count)
    {
#if UNITY_IOS && !UNITY_EDITOR
      for (var i = 0; i < Mathf.Clamp(count, 1, 2); i++)
      {
        Handheld.Vibrate();
        if (i + 1 < count) yield return new WaitForSecondsRealtime(0.16f);
      }
#else
      yield break;
#endif
    }

    private System.Collections.IEnumerator PlayLightHapticPulses(int count)
    {
#if UNITY_IOS && !UNITY_EDITOR
      for (var i = 0; i < Mathf.Clamp(count, 1, 2); i++)
      {
        CareAudioFeedbackController.PulseLight();
        if (i + 1 < count) yield return new WaitForSecondsRealtime(0.22f);
      }
#else
      yield break;
#endif
    }

    private CareActionConfiguration BuildConfiguration(
      float closedEyeDurationOverride = 0f,
      bool showIntro = false,
      int focusCycleCountOverride = 0,
      int guidedLapsOverride = 0,
      int pilotRoundsOverride = 0)
    {
      return new CareActionConfiguration
      {
        showIntro = showIntro,
        actionIntroSeconds = _actionIntroSeconds,
        closedEyeDurationSeconds = closedEyeDurationOverride > 0f
          ? Mathf.Clamp(closedEyeDurationOverride, 1f, 180f)
          : _closedEyeRestSeconds,
        closeStartHoldSeconds = _closeStartHoldSeconds,
        reopenHoldSeconds = _reopenHoldSeconds,
        distanceDeadZone = _distanceDeadZone,
        distanceCompleteThreshold = _distanceCompleteThreshold,
        distanceStepHoldSeconds = _distanceStepHoldSeconds,
        distanceProgressFallSeconds = _distanceProgressFallSeconds,
        focusStepTransitionSeconds = _focusStepTransitionSeconds,
        focusNeutralMinimum = _focusNeutralMinimum,
        focusNeutralMaximum = _focusNeutralMaximum,
        focusCloserRatio = _focusCloserRatio,
        focusAwayRatio = _focusAwayRatio,
        focusTooCloseRatio = _focusTooCloseRatio,
        focusTargetHoldSeconds = _focusTargetHoldSeconds,
        focusMinimumLegSeconds = _focusMinimumLegSeconds,
        focusDirectionIntervalSeconds = _focusDirectionIntervalSeconds,
        focusCycleCount = focusCycleCountOverride > 0 ? focusCycleCountOverride : _focusCycleCount,
        guidedPreviewSeconds = _guidedPreviewSeconds,
        guidedClockwiseSeconds = _guidedClockwiseSeconds,
        guidedPauseSeconds = _guidedPauseSeconds,
        guidedCounterClockwiseSeconds = _guidedCounterClockwiseSeconds,
        guidedRelaxSeconds = _guidedRelaxSeconds,
        guidedLapsPerDirection = guidedLapsOverride > 0 ? guidedLapsOverride : _guidedLapsPerDirection,
        pilotIntroSeconds = _pilotIntroSeconds,
        pilotRoundSeconds = _pilotRoundSeconds,
        pilotRoundsPerAxis = pilotRoundsOverride > 0 ? pilotRoundsOverride : _pilotRoundsPerAxis,
        pilotTransitionSeconds = _pilotTransitionSeconds,
      };
    }

    private void ObserveFocusScale(float scale)
    {
      if (!CareDistanceReferenceSampler.IsValidScale(scale)) return;
      _focusObservedMinimum = Mathf.Min(_focusObservedMinimum, scale);
      _focusObservedMaximum = Mathf.Max(_focusObservedMaximum, scale);
    }

    private void ResetFocusScaleObservation()
    {
      _focusObservedMinimum = float.PositiveInfinity;
      _focusObservedMaximum = float.NegativeInfinity;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void PollDevelopmentKeys()
    {
      var keyboard = Keyboard.current;
      if (keyboard == null) return;
      if (keyboard.bKey.wasPressedThisFrame)
        SimulateEyesClosed(!(keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed));
    }
#endif

    private void OnApplicationPause(bool paused)
    {
      _applicationActive = !paused;
      if (paused && IsRunning)
      {
        CareAudioFeedbackController.EnsureExists().SetActionAudioPaused(true);
        CareVoiceService.EnsureExists().SetPaused(true);
        _runtime.Suspend(CareActionPauseReason.ApplicationBackground);
      }
      // Do not unpause here. The next action update must first validate the
      // current tracking/eye state and then release audio through
      // UpdateAudioSuspension; otherwise foregrounding can leak one frame.
    }

    private void OnApplicationFocus(bool focused)
    {
      _hasFocus = focused;
      if (!focused && IsRunning)
      {
        CareAudioFeedbackController.EnsureExists().SetActionAudioPaused(true);
        CareVoiceService.EnsureExists().SetPaused(true);
        _runtime.Suspend(CareActionPauseReason.ApplicationFocusLost);
      }
      // As with application resume, the first valid action frame owns the
      // audio resume decision.
    }
  }
}
