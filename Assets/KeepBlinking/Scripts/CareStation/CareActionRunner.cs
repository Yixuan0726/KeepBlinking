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

    public void Reset(bool closeRequestAlreadyPlayed = false)
    {
      _closeRequestPlayed = closeRequestAlreadyPlayed;
      _readyToOpenPlayed = false;
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
    [Header("Screen Down")]
    [SerializeField, Min(0.1f)] private float _screenDownDemoSeconds = 1.2f;
    [SerializeField, Min(1f)] private float _screenDownSeconds = 20f;
    [SerializeField, Range(10f, 70f)] private float _groundAlignmentDegrees = 40f;
    [SerializeField, Range(5f, 45f)] private float _returnAngleDegrees = 20f;
    [SerializeField, Min(0.1f)] private float _screenDownHoldSeconds = 0.5f;
    [SerializeField, Min(0.1f)] private float _returnHoldSeconds = 0.4f;
    [SerializeField, Range(0.05f, 0.5f)] private float _accelerationTolerance = 0.2f;
    [SerializeField, Min(0.1f)] private float _maximumGyroRadiansPerSecond = 0.8f;

    [Header("Closed-Eye Rest")]
    [SerializeField, Range(45f, 60f)] private float _closedEyeRestSeconds = 45f;
    [SerializeField, Min(0.1f)] private float _closeStartHoldSeconds = 1.5f;
    [SerializeField, Min(0.1f)] private float _reopenHoldSeconds = 0.5f;

    [Header("Focus Shift")]
    [SerializeField, Range(0.25f, 0.4f)] private float _gestureReferenceCaptureSeconds = 0.3f;
    [SerializeField, Range(3, 15)] private int _gestureReferenceMinimumSamples = 5;
    [SerializeField, Min(0.1f)] private float _gestureScaleSmoothingSpeed = 12f;
    // Linear distance fractions (see FaceDistanceRatio): 0.22 means the step completes once
    // the player has moved to 1/1.22 = 82% of the reference distance, about 8 cm from 45 cm.
    [SerializeField, Range(0.01f, 0.12f)] private float _distanceDeadZone = 0.05f;
    [SerializeField, Range(0.08f, 0.4f)] private float _distanceCompleteThreshold = 0.22f;
    [SerializeField, Range(0.05f, 1f)] private float _distanceStepHoldSeconds = 0.25f;
    [SerializeField, Range(0.05f, 1f)] private float _distanceProgressFallSeconds = 0.25f;
    [SerializeField, Range(0f, 2f)] private float _focusStepTransitionSeconds = 0.4f;
    [SerializeField, Min(1f)] private float _distanceFallbackDelay = 8f;

    [Header("Guided Eye Circles")]
    [SerializeField, Min(1f)] private float _guidedPreviewSeconds = 4f;
    [SerializeField, Min(1f)] private float _guidedClockwiseSeconds = 8f;
    [SerializeField, Min(0.1f)] private float _guidedPauseSeconds = 2f;
    [SerializeField, Min(1f)] private float _guidedCounterClockwiseSeconds = 8f;
    [SerializeField, Min(0.1f)] private float _guidedRelaxSeconds = 5f;

    private CareActionRuntime _runtime;
    private EdgeOrbitHarvestMvp _gameplay;
    private CareStationView _view;
    private GravitySensor _gravitySensor;
    private Accelerometer _accelerometer;
    private UnityEngine.InputSystem.Gyroscope _gyroscope;
    private Vector3 _initialDeviceGravity = Vector3.back;
    private Vector3 _deviceGravity = Vector3.back;
    private Vector3 _previousDeviceGravity = Vector3.zero;
    private float _accelerationMagnitude = 1f;
    private float _angularSpeed;
    private bool _orientationCaptured;
    private bool _applicationActive = true;
    private bool _hasFocus = true;
    private CareActionInternalPhase _lastPresentedPhase;
    private CareActionStage _lastPresentedStage;
    private int _lastGuidedNote = -1;
    private bool _completionCuePlayed;
    private bool _restoredCompletionPending;
    private readonly CareActionCueGuard _cueGuard = new CareActionCueGuard();
    private CareDistanceReferenceSampler _focusReferenceSampler;
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
    private bool _simulateScreenDown;
    private bool _simulateReturn;
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
    public int CloseRequestCuePlayCount => _cueGuard.CloseRequestPlayCount;
    public int ReadyToOpenCuePlayCount => _cueGuard.ReadyToOpenPlayCount;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public bool CanSkipUnavailableScreenDown => IsRunning &&
      ActionType == CareActionType.ScreenDown &&
      PauseReason == CareActionPauseReason.SensorUnavailable;
#endif
    public CareActionSaveData SaveData => _runtime?.Data;
    public float GestureDistanceRatio => CurrentDistanceRatio;
    public float GestureReferenceScale => SaveData != null && SaveData.gestureReferenceValid ? SaveData.gestureReferenceScale : 0f;
    public bool GestureReferenceValid => SaveData != null && SaveData.gestureReferenceValid;
    public float CurrentGestureFaceScale => _hasSmoothedGestureFaceScale ? _smoothedGestureFaceScale : 0f;
    public float RawGestureFaceScale => _rawGestureFaceScale;
    public float FocusStableSeconds => SaveData != null ? SaveData.holdElapsedSeconds : 0f;
    public int FocusStep => SaveData != null ? SaveData.focusTargetStep : 0;
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
        if (InternalPhase == CareActionInternalPhase.FocusReference) return "CAPTURING REFERENCE";
        if (DistanceSensorUnavailable) return "SENSOR UNAVAILABLE";
        if (FocusStableSeconds > 0f) return "STABILIZING";
        if (DirectionProgress <= 0f) return "DEAD ZONE";
        return ExpectedDistanceDirection == CareDistanceDirection.Closer ? "MOVING CLOSER" : "MOVING AWAY";
      }
    }

    public void Bind(EdgeOrbitHarvestMvp gameplay, CareStationView view)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_view != null) _view.SkipCareStepSelected -= HandleSkipCareStepSelected;
#endif
      _gameplay = gameplay;
      _view = view;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_view != null) _view.SkipCareStepSelected += HandleSkipCareStepSelected;
#endif
    }

    public void ConfigureStationDurations(
      float screenDownSeconds,
      float closedEyeSeconds,
      float closeStartHoldSeconds,
      float reopenHoldSeconds)
    {
      _screenDownSeconds = Mathf.Max(1f, screenDownSeconds);
      _closedEyeRestSeconds = Mathf.Clamp(closedEyeSeconds, 1f, 60f);
      _closeStartHoldSeconds = Mathf.Max(0.1f, closeStartHoldSeconds);
      _reopenHoldSeconds = Mathf.Max(0.1f, reopenHoldSeconds);
    }

    public bool StartAction(
      CareActionType type,
      CareActionSaveData restore = null,
      bool developmentTest = false,
      float closedEyeDurationOverride = 0f)
    {
      if (type == CareActionType.None || IsRunning) return false;
      IsDevelopmentTest = developmentTest;
      _runtime = new CareActionRuntime();
      _runtime.Begin(type, BuildConfiguration(closedEyeDurationOverride), restore);
      _lastPresentedPhase = CareActionInternalPhase.None;
      _lastPresentedStage = CareActionStage.Cancelled;
      _lastGuidedNote = -1;
      _completionCuePlayed = false;
      _cueGuard.Reset(restore != null && restore.closeRequestCuePlayed);
      _restoredCompletionPending = _runtime.Stage == CareActionStage.Completed &&
                                   !_runtime.Data.completionSignalEmitted;
      if (type == CareActionType.ScreenDown) CaptureNeutralOrientation();
      if (type == CareActionType.FocusShift) PrepareFocusReference(restore);
      if (type == CareActionType.FocusShift) _focusStepOpenedAt = Time.unscaledTime;
      if (type == CareActionType.FocusShift && InternalPhase != CareActionInternalPhase.FocusReference)
      {
        _focusStepOpenedAt = Time.unscaledTime;
        _focusFreshSamplesInStep = 0;
      }
      if (!developmentTest) _gameplay?.SetCareActionActive(true);
      Present(true);
      ExecuteCueCommand(_cueGuard.ObservePhase(ActionType, InternalPhase));
      return true;
    }

    public void CancelAction()
    {
      if (_runtime == null) return;
      _runtime.Cancel();
      if (!IsDevelopmentTest) _gameplay?.SetCareActionActive(false);
      CareAudioFeedbackController.EnsureExists().StopGuidedCue();
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public bool TrySkipUnavailableScreenDownForDevelopment()
    {
      if (_runtime == null || !CanSkipUnavailableScreenDown ||
          !_runtime.SkipUnavailableScreenDownForDevelopment()) return false;
      Present(true);
      EmitCompletionIfReady();
      return true;
    }

    private void HandleSkipCareStepSelected()
    {
      TrySkipUnavailableScreenDownForDevelopment();
    }
#endif

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

    public void SimulateScreenPose(bool screenDown)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _simulateScreenDown = screenDown;
      _simulateReturn = !screenDown;
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
      var directionDelta = Mathf.Lerp(_distanceDeadZone, _distanceCompleteThreshold, Mathf.Clamp01(progress));
      _simulatedDistanceRatio = ExpectedDistanceDirection == CareDistanceDirection.Closer
        ? 1f + directionDelta
        : 1f - directionDelta;
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
      if (previousPhase != InternalPhase)
        PlayPhaseTransition(previousPhase, InternalPhase, input.ApplicationActive, input.TrackingValid);
      Present(previousPhase != InternalPhase || previousStage != Stage);
      PlayGuidedNotesIfNeeded();
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
      if (ActionType == CareActionType.ScreenDown && !_orientationCaptured) CaptureNeutralOrientation();
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
      SampleMotion(delta);
      var sensorAvailable = SensorsAvailable && _orientationCaptured;
      var screenDown = IsScreenDownAndStable();
      var returned = IsReturnedAndStable();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      sensorAvailable |= _simulateScreenDown || _simulateReturn;
      screenDown |= _simulateScreenDown;
      returned |= _simulateReturn;
#endif
      return new CareActionInputFrame(
        _applicationActive && _hasFocus,
        tracking,
        eyesClosed,
        sensorAvailable,
        screenDown,
        returned,
        referenceValid,
        distanceRatio,
        distanceSampleFresh,
        distanceSampleDelta);
    }

    private void Present(bool force)
    {
      if (_runtime == null || _view == null) return;
      if (ActionType == CareActionType.FocusShift && Stage == CareActionStage.Preparing)
      {
        _view.HideAllModals();
        return;
      }
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
        FocusStep);
      _view.SetReturnFallbackAvailable(CanOfferDistanceFallback);
      _view.SetCareActionChangeAvailable(
        !IsDevelopmentTest && IsRunning && ActionType != CareActionType.ClosedEyeRest);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _view.SetCareActionSkipAvailable(CanSkipUnavailableScreenDown);
#endif
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
      _focusReferenceSampler = new CareDistanceReferenceSampler(
        _gestureReferenceCaptureSeconds,
        _gestureReferenceMinimumSamples);
      _lastGestureSampleSequence = long.MinValue;
      _hasSmoothedGestureFaceScale = false;
      _smoothedGestureFaceScale = 0f;
      _currentGestureRatio = 1f;
      _lastGestureFreshSampleAt = -1f;
      _rawGestureFaceScale = 0f;
      if (restore != null && restore.gestureReferenceValid &&
          _focusReferenceSampler.Restore(restore.gestureReferenceScale, true))
      {
        _runtime.Data.gestureReferenceScale = _focusReferenceSampler.ReferenceScale;
        _runtime.Data.gestureReferenceValid = true;
        _smoothedGestureFaceScale = _focusReferenceSampler.ReferenceScale;
        _hasSmoothedGestureFaceScale = true;
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
        if (!tracking)
        {
          _lastGestureFreshSampleAt = -1f;
          if (!referenceValid) _focusReferenceSampler?.Reset();
        }
        return;
      }

      _rawGestureFaceScale = scale;

      if (!referenceValid && _runtime != null && _runtime.Data.focusTargetStep > 0 &&
          _runtime.Data.phaseElapsedSeconds < _focusStepTransitionSeconds) return;

      if (!referenceValid)
      {
        if (_focusReferenceSampler == null) PrepareFocusReference(null);
        if (sequence != _lastGestureSampleSequence)
        {
          _lastGestureSampleSequence = sequence;
          _focusFreshSamplesInStep++;
          ObserveFocusScale(scale);
        }
        if (!_focusReferenceSampler.AddFreshSample(sequence, scale, Time.unscaledTime, tracking)) return;
        _runtime.Data.gestureReferenceScale = _focusReferenceSampler.ReferenceScale;
        _runtime.Data.gestureReferenceValid = true;
        _smoothedGestureFaceScale = _focusReferenceSampler.ReferenceScale;
        _hasSmoothedGestureFaceScale = true;
        _lastGestureSampleSequence = sequence;
        _lastGestureFreshSampleAt = Time.unscaledTime;
        _currentGestureRatio = 1f;
        referenceValid = true;
        ratio = 1f;
        sampleFresh = true;
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
      _focusReferenceSampler?.Restore(1f, true);
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
        PrepareFocusReference(null);
        _focusStepOpenedAt = Time.unscaledTime;
        _focusFreshSamplesInStep = 0;
        ResetFocusScaleObservation();
      }
      else if (ActionType == CareActionType.FocusShift &&
               (current == CareActionInternalPhase.FocusNearOne ||
                current == CareActionInternalPhase.FocusFarOne ||
                current == CareActionInternalPhase.FocusNearTwo ||
                current == CareActionInternalPhase.FocusFarTwo))
      {
        _focusStepOpenedAt = Time.unscaledTime;
        _focusFreshSamplesInStep = 0;
        ResetFocusScaleObservation();
      }
      if (previous == CareActionInternalPhase.ScreenDownRest && current == CareActionInternalPhase.ScreenDownReturn)
      {
        audio.PlayGuidedCompletion();
        _completionCuePlayed = true;
      }
      ExecuteCueCommand(_cueGuard.ObservePhase(ActionType, current));
      ExecuteCueCommand(_cueGuard.PollReadyToOpen(applicationActive, trackingValid));
      if (current == CareActionInternalPhase.GuidedPause)
        audio.PlayGuidedCenterPause();
      else if (ActionType == CareActionType.FocusShift && previous != CareActionInternalPhase.None)
        audio.PlayStepComplete();
      if (current == CareActionInternalPhase.GuidedClockwise || current == CareActionInternalPhase.GuidedCounterClockwise)
      {
        _lastGuidedNote = -1;
        StartCoroutine(PlayHapticPulses(current == CareActionInternalPhase.GuidedCounterClockwise ? 2 : 1));
      }
    }

    private void PlayGuidedNotesIfNeeded()
    {
      if (ActionType != CareActionType.GuidedEyeCircles || Stage != CareActionStage.Active) return;
      var duration = InternalPhase == CareActionInternalPhase.GuidedClockwise
        ? _guidedClockwiseSeconds
        : InternalPhase == CareActionInternalPhase.GuidedCounterClockwise ? _guidedCounterClockwiseSeconds : 0f;
      if (duration <= 0f) return;
      var note = Mathf.Clamp(Mathf.FloorToInt(SaveData.phaseElapsedSeconds / duration * 8f), 0, 7);
      if (note == _lastGuidedNote) return;
      _lastGuidedNote = note;
      var audio = CareAudioFeedbackController.EnsureExists();
      if (InternalPhase == CareActionInternalPhase.GuidedClockwise) audio.PlayGuidedClockwiseNote(note, 8);
      else audio.PlayGuidedCounterClockwiseNote(note, 8);
    }

    private void PlayCompletionCueOnce()
    {
      if (ActionType == CareActionType.ClosedEyeRest || ActionType == CareActionType.GuidedEyeCircles)
      {
        // Normally this was already played on entry to WaitReopen. This call
        // only covers a restored completed action whose signal was not yet
        // consumed; the guard prevents any duplicate cue.
        ExecuteCueCommand(_cueGuard.PollReadyToOpen(true, true));
        return;
      }
      if (_completionCuePlayed) return;
      _completionCuePlayed = true;
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
        CareAudioFeedbackController.EnsureExists().PlayGuidedCompletion();
        StartCoroutine(PlayLightHapticPulses(1));
      }
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

    private CareActionConfiguration BuildConfiguration(float closedEyeDurationOverride = 0f)
    {
      return new CareActionConfiguration
      {
        screenDownDemoSeconds = _screenDownDemoSeconds,
        screenDownDurationSeconds = _screenDownSeconds,
        screenDownHoldSeconds = _screenDownHoldSeconds,
        screenReturnHoldSeconds = _returnHoldSeconds,
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
        guidedPreviewSeconds = _guidedPreviewSeconds,
        guidedClockwiseSeconds = _guidedClockwiseSeconds,
        guidedPauseSeconds = _guidedPauseSeconds,
        guidedCounterClockwiseSeconds = _guidedCounterClockwiseSeconds,
        guidedRelaxSeconds = _guidedRelaxSeconds,
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

    private void CaptureNeutralOrientation()
    {
      ResolveSensors();
      _orientationCaptured = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_simulateScreenDown || _simulateReturn)
      {
        _initialDeviceGravity = Vector3.back;
        _deviceGravity = Vector3.back;
        _previousDeviceGravity = Vector3.back;
        _orientationCaptured = true;
        return;
      }
#endif
      if (!SensorsAvailable) return;
      var gravity = ReadGravity();
      var acceleration = ReadAccelerationMagnitude();
      if (gravity.sqrMagnitude <= 0.0001f || acceleration <= 0.01f) return;
      _initialDeviceGravity = gravity.normalized;
      _deviceGravity = _initialDeviceGravity;
      _previousDeviceGravity = _initialDeviceGravity;
      _orientationCaptured = true;
    }

    private void ResolveSensors()
    {
      _gravitySensor = GravitySensor.current;
      _accelerometer = Accelerometer.current;
      _gyroscope = UnityEngine.InputSystem.Gyroscope.current;
      Enable(_gravitySensor);
      Enable(_accelerometer);
      Enable(_gyroscope);
    }

    private bool SensorsAvailable => _gravitySensor != null || _accelerometer != null;

    private static void Enable(InputDevice sensor)
    {
      if (sensor != null && !sensor.enabled) InputSystem.EnableDevice(sensor);
    }

    /// <summary>
    /// Sampled once per frame so both judgements below share one reading and the no-gyroscope
    /// fallback always compares against the previous frame. See <see cref="ScreenDownRestMotionLogic"/>
    /// for why this works on device-space gravity instead of the attitude sensor.
    /// </summary>
    private void SampleMotion(float delta)
    {
      var gravity = ReadGravity();
      if (gravity.sqrMagnitude > 0.0001f)
      {
        _deviceGravity = gravity.normalized;
        _angularSpeed = _gyroscope != null
          ? _gyroscope.angularVelocity.ReadValue().magnitude
          : ScreenDownRestMotionLogic.AngularSpeedFromGravity(_previousDeviceGravity, _deviceGravity, delta);
        _previousDeviceGravity = _deviceGravity;
      }
      _accelerationMagnitude = ReadAccelerationMagnitude();
    }

    private bool IsScreenDownAndStable()
    {
      if (!_orientationCaptured || !SensorsAvailable) return false;
      return ScreenDownRestMotionLogic.IsScreenDown(_deviceGravity, _groundAlignmentDegrees) && IsStable();
    }

    private bool IsReturnedAndStable()
    {
      if (!_orientationCaptured || !SensorsAvailable) return false;
      return ScreenDownRestMotionLogic.IsReturned(_initialDeviceGravity, _deviceGravity, _returnAngleDegrees) &&
             IsStable();
    }

    private bool IsStable()
    {
      return ScreenDownRestMotionLogic.IsStable(
        _accelerationMagnitude,
        _accelerationTolerance,
        _angularSpeed,
        _maximumGyroRadiansPerSecond);
    }

    private Vector3 ReadGravity()
    {
      if (_gravitySensor != null) return _gravitySensor.gravity.ReadValue();
      if (_accelerometer != null) return _accelerometer.acceleration.ReadValue();
      return Vector3.zero;
    }

    private float ReadAccelerationMagnitude()
    {
      if (_accelerometer != null) return _accelerometer.acceleration.ReadValue().magnitude;
      if (_gravitySensor != null) return _gravitySensor.gravity.ReadValue().magnitude;
      return 0f;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void PollDevelopmentKeys()
    {
      var keyboard = Keyboard.current;
      if (keyboard == null) return;
      if (keyboard.f11Key.wasPressedThisFrame)
        SimulateScreenPose(!(keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed));
      if (keyboard.bKey.wasPressedThisFrame)
        SimulateEyesClosed(!(keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed));
      if (keyboard.sKey.wasPressedThisFrame)
        TrySkipUnavailableScreenDownForDevelopment();
    }
#endif

    private void OnDestroy()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_view != null) _view.SkipCareStepSelected -= HandleSkipCareStepSelected;
#endif
    }

    private void OnApplicationPause(bool paused)
    {
      _applicationActive = !paused;
      if (paused && IsRunning)
        _runtime.Suspend(CareActionPauseReason.ApplicationBackground);
    }

    private void OnApplicationFocus(bool focused)
    {
      _hasFocus = focused;
      if (!focused && IsRunning)
        _runtime.Suspend(CareActionPauseReason.ApplicationFocusLost);
    }
  }
}
