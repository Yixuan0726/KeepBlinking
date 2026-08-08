using System;
using System.Collections.Generic;
using KeepBlinking.Input;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  public enum FocusShiftState
  {
    Dormant,
    CalibratingNeutral,
    Neutral,
    Near,
    Far,
    PausedTracking,
    TooClose,
    Completed,
  }

  public enum FocusShiftGuidance
  {
    MoveCloser,
    MoveAway,
    HoldSteady,
  }

  public sealed class FocusShiftController : MonoBehaviour
  {
    private sealed class OneEuroScalarFilter
    {
      private bool _hasValue;
      private float _filteredValue;
      private float _filteredDerivative;
      private float _lastRawValue;
      private float _lastTimestamp;

      public void Clear()
      {
        _hasValue = false;
        _filteredValue = 0f;
        _filteredDerivative = 0f;
        _lastRawValue = 0f;
        _lastTimestamp = 0f;
      }

      public float Reset(float value, float timestamp)
      {
        _hasValue = true;
        _filteredValue = value;
        _filteredDerivative = 0f;
        _lastRawValue = value;
        _lastTimestamp = timestamp;
        return value;
      }

      public float Filter(float value, float timestamp, float minimumCutoff, float beta, float derivativeCutoff)
      {
        if (!_hasValue) return Reset(value, timestamp);
        var deltaTime = Mathf.Clamp(timestamp - _lastTimestamp, 1f / 240f, 0.25f);
        var rawDerivative = (value - _lastRawValue) / deltaTime;
        var derivativeAlpha = Alpha(Mathf.Max(0.01f, derivativeCutoff), deltaTime);
        _filteredDerivative = Mathf.Lerp(_filteredDerivative, rawDerivative, derivativeAlpha);
        var cutoff = Mathf.Max(0.01f, minimumCutoff) + Mathf.Max(0f, beta) * Mathf.Abs(_filteredDerivative);
        _filteredValue = Mathf.Lerp(_filteredValue, value, Alpha(cutoff, deltaTime));
        _lastRawValue = value;
        _lastTimestamp = timestamp;
        return _filteredValue;
      }

      private static float Alpha(float cutoff, float deltaTime)
      {
        var timeConstant = 1f / (2f * Mathf.PI * cutoff);
        return 1f / (1f + timeConstant / Mathf.Max(0.0001f, deltaTime));
      }
    }

    private readonly struct ShiftStep
    {
      public ShiftStep(CareMovementDirection direction, int rewards)
      {
        Direction = direction;
        Rewards = rewards;
      }
      public CareMovementDirection Direction { get; }
      public int Rewards { get; }
    }

    [Header("Required Ranges")]
    [SerializeField, Range(1, 4)] private int _focusShiftCycles = 2;
    [SerializeField] private float _neutralMin = 0.95f;
    [SerializeField] private float _neutralMax = 1.05f;
    [SerializeField] private float _nearMin = 1.10f;
    [SerializeField] private float _nearMax = 1.14f;
    [SerializeField] private float _farMin = 0.84f;
    [SerializeField] private float _farMax = 0.88f;
    [SerializeField] private float _tooCloseRatio = 1.18f;

    [Header("Timing")]
    [SerializeField, Min(0.2f)] private float _neutralHoldSeconds = 0.4f;
    [SerializeField, Min(0.2f)] private float _nearFarHoldSeconds = 0.5f;
    [SerializeField, Min(0.5f)] private float _minimumTransitionSeconds = 1f;
    [SerializeField, Min(0.3f)] private float _localBaselineCaptureSeconds = 0.5f;
    [SerializeField, Min(5)] private int _minimumLocalBaselineSamples = 8;
    [SerializeField, Range(0.01f, 0.08f)] private float _maximumLocalBaselineSpread = 0.035f;
    [SerializeField, Range(0.15f, 0.75f)] private float _sampleFreshnessSeconds = 0.35f;

    [Header("Dynamic Filtering")]
    [SerializeField, Range(0.2f, 5f)] private float _minimumCutoff = 1.2f;
    [SerializeField, Range(0f, 2f)] private float _speedCoefficient = 0.75f;
    [SerializeField, Range(0.2f, 5f)] private float _derivativeCutoff = 1f;

    private readonly List<ShiftStep> _steps = new List<ShiftStep>(6);
    private readonly List<float> _localBaselineSamples = new List<float>(32);
    private readonly OneEuroScalarFilter _ratioFilter = new OneEuroScalarFilter();
    private EdgeOrbitHarvestMvp _gameplay;
    private CareExperienceRewardEmitter _emitter;
    private FocusShiftView _view;
    private float _sessionBaselineFaceScale;
    private float _localFocusBaseline = -1f;
    private float _rawSessionRatio = 1f;
    private float _smoothedRatio = 1f;
    private int _stepIndex;
    private float _stepOriginRatio = 1f;
    private float _stepStartedAt;
    private float _holdStartedAt = -1f;
    private float _highestProgressReached;
    private int _lastRewardSegment = -1;
    private ulong _rewardedSegmentMask;
    private long _lastSampleSequence = long.MinValue;
    private float _lastFreshSampleAt = -1f;
    private float _localBaselineStartedAt = -1f;
    private float _trackingPausedAt = -1f;
    private float _focusStartedAt = -1f;
    private bool _latestSampleValid;
    private bool _trackingPaused;
    private bool _receivedSampleSinceStart;

    public static FocusShiftController Instance { get; private set; }
    public static event Action FocusShiftStarted;
    public static event Action<CareMovementDirection, float, float> FocusShiftProgressChanged;
    public static event Action<CareMovementDirection> FocusShiftStepCompleted;
    public static event Action FocusShiftCompleted;

    public FocusShiftState State { get; private set; } = FocusShiftState.Dormant;
    public float DistanceRatio => _smoothedRatio;
    public float RawDistanceRatio => _rawSessionRatio;
    public float SessionBaselineFaceScale => _sessionBaselineFaceScale;
    public float LocalFocusBaseline => _localFocusBaseline;
    public bool HasLocalFocusBaseline => IsFinitePositive(_localFocusBaseline);
    public bool HasFreshSample => _latestSampleValid &&
                                  _lastFreshSampleAt >= 0f &&
                                  Time.unscaledTime - _lastFreshSampleAt <= _sampleFreshnessSeconds;
    public int FocusShiftCycles => Mathf.Max(2, _focusShiftCycles);
    public int CurrentStepIndex => _stepIndex;
    public CareMovementDirection CurrentDirection =>
      _stepIndex >= 0 && _stepIndex < _steps.Count ? _steps[_stepIndex].Direction : CareMovementDirection.Center;
    public float HighestProgressReached => _highestProgressReached;
    public FocusShiftGuidance CurrentGuidance { get; private set; } = FocusShiftGuidance.HoldSteady;
    public float CurrentHoldProgress { get; private set; }
    public int LastRewardSegment => _lastRewardSegment;
    public ulong RewardedSegmentMask => _rewardedSegmentMask;
    public bool IsActive => State != FocusShiftState.Dormant && State != FocusShiftState.Completed;

    public static FocusShiftController EnsureExists(EdgeOrbitHarvestMvp gameplay)
    {
      if (Instance == null) Instance = FindFirstObjectByType<FocusShiftController>();
      if (Instance == null)
      {
        var owner = new GameObject("Focus Shift Controller");
        Instance = owner.AddComponent<FocusShiftController>();
      }
      Instance.Bind(gameplay);
      return Instance;
    }

    private void Awake()
    {
      if (Instance != null && Instance != this)
      {
        Destroy(gameObject);
        return;
      }
      Instance = this;
      _view = gameObject.AddComponent<FocusShiftView>();
    }

    private void Bind(EdgeOrbitHarvestMvp gameplay)
    {
      _gameplay = gameplay;
      _emitter = CareExperienceRewardEmitter.EnsureExists(gameplay);
      _view?.Bind(gameplay);
    }

    public bool StartFocusShift(float sessionBaselineFaceScale)
    {
      if (IsActive || sessionBaselineFaceScale <= 0f) return false;
      _sessionBaselineFaceScale = sessionBaselineFaceScale;
      _localFocusBaseline = -1f;
      _steps.Clear();
      // Health invariant: always two complete Near -> Far cycles.
      for (var cycle = 0; cycle < Mathf.Max(2, _focusShiftCycles); cycle++)
      {
        _steps.Add(new ShiftStep(
          CareMovementDirection.Near,
          cycle == 0
            ? FirstLevelCareRewardPlan.NeutralToNearFragments
            : FirstLevelCareRewardPlan.ShiftSpanFragments));
        _steps.Add(new ShiftStep(CareMovementDirection.Far, FirstLevelCareRewardPlan.ShiftSpanFragments));
      }
      _steps.Add(new ShiftStep(CareMovementDirection.Center, FirstLevelCareRewardPlan.FinalNeutralFragments));
      _stepIndex = -1;
      _rawSessionRatio = 1f;
      _smoothedRatio = 1f;
      _ratioFilter.Clear();
      _localBaselineSamples.Clear();
      _localBaselineStartedAt = -1f;
      _lastFreshSampleAt = -1f;
      _latestSampleValid = false;
      _trackingPaused = false;
      _trackingPausedAt = -1f;
      _focusStartedAt = Time.unscaledTime;
      _receivedSampleSinceStart = false;
      CurrentGuidance = FocusShiftGuidance.HoldSteady;
      CurrentHoldProgress = 0f;
      // Do not accept a frame captured before this Focus Shift began.
      _lastSampleSequence = EyeInputDebugState.Latest.SampleSequence;
      State = FocusShiftState.CalibratingNeutral;
      _emitter?.SetEmissionPaused(false);
      _gameplay?.SetFocusShiftActive(true);
      _view?.Show();
      _view?.SetCalibrating(0f, false, 1f);
      FocusShiftStarted?.Invoke();
      return true;
    }

    private void Update()
    {
      if (!IsActive) return;
      var now = Time.unscaledTime;
      var snapshot = EyeInputDebugState.Latest;
      var hasNewSample = snapshot.SampleSequence != _lastSampleSequence;
      if (hasNewSample)
      {
        _receivedSampleSinceStart = true;
        _lastSampleSequence = snapshot.SampleSequence;
        var faceScale = GetFreshFaceScale(snapshot);
        _latestSampleValid = snapshot.FaceDetected && IsFinitePositive(faceScale);
        if (_latestSampleValid)
        {
          _lastFreshSampleAt = now;
          _rawSessionRatio = faceScale / _sessionBaselineFaceScale;
          if (_trackingPaused)
          {
            ResumeTracking(now, _rawSessionRatio);
          }
          else
          {
            _smoothedRatio = _ratioFilter.Filter(
              _rawSessionRatio,
              now,
              _minimumCutoff,
              _speedCoefficient,
              _derivativeCutoff);
          }
        }
      }

      if (!_latestSampleValid || _lastFreshSampleAt < 0f || now - _lastFreshSampleAt > _sampleFreshnessSeconds)
      {
        // Allow one camera-frame grace period at startup while still requiring
        // the baseline itself to contain only post-start samples.
        if (!_receivedSampleSinceStart && now - _focusStartedAt <= _sampleFreshnessSeconds)
          return;
        PauseForTracking(now);
        return;
      }

      if (!HasLocalFocusBaseline)
      {
        UpdateLocalBaselineCapture(snapshot, hasNewSample, now);
        return;
      }

      UpdateCurrentStep(now);
    }

    private void UpdateLocalBaselineCapture(EyeInputDebugSnapshot snapshot, bool hasNewSample, float now)
    {
      var tooClose = _smoothedRatio >= _tooCloseRatio;
      if (tooClose)
      {
        State = FocusShiftState.TooClose;
        ResetLocalBaselineCapture();
        _emitter?.SetEmissionPaused(true);
        _view?.SetCalibrating(0f, true, _smoothedRatio);
        return;
      }

      State = FocusShiftState.CalibratingNeutral;
      _emitter?.SetEmissionPaused(false);
      if (_smoothedRatio < _neutralMin || _smoothedRatio > _neutralMax)
      {
        ResetLocalBaselineCapture();
        _view?.SetCalibrating(0f, false, _smoothedRatio);
        return;
      }

      if (hasNewSample)
      {
        var faceScale = GetFreshFaceScale(snapshot);
        if (_localBaselineStartedAt < 0f) _localBaselineStartedAt = now;
        _localBaselineSamples.Add(faceScale);
        if (_localBaselineSamples.Count > 48) _localBaselineSamples.RemoveAt(0);
        if (!LocalBaselineSpreadIsStable())
        {
          _localBaselineSamples.Clear();
          _localBaselineSamples.Add(faceScale);
          _localBaselineStartedAt = now;
        }
      }

      var elapsed = _localBaselineStartedAt < 0f ? 0f : now - _localBaselineStartedAt;
      var calibrationProgress = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, _localBaselineCaptureSeconds));
      _view?.SetCalibrating(calibrationProgress, false, _smoothedRatio);
      if (_localBaselineSamples.Count < Mathf.Max(5, _minimumLocalBaselineSamples) ||
          elapsed < _localBaselineCaptureSeconds)
        return;

      var median = Median(_localBaselineSamples);
      var medianSessionRatio = median / _sessionBaselineFaceScale;
      if (!IsFinitePositive(median) || medianSessionRatio < _neutralMin || medianSessionRatio > _neutralMax)
      {
        ResetLocalBaselineCapture();
        return;
      }

      _localFocusBaseline = median;
      _rawSessionRatio = GetFreshFaceScale(snapshot) / _sessionBaselineFaceScale;
      _smoothedRatio = _ratioFilter.Reset(_rawSessionRatio, now);
      _stepIndex = 0;
      BeginStep(medianSessionRatio);
    }

    private void UpdateCurrentStep(float now)
    {
      var step = _steps[_stepIndex];
      var tooClose = _smoothedRatio >= _tooCloseRatio;
      _emitter?.SetEmissionPaused(tooClose);
      var progress = CalculateProgress(step.Direction);
      var inZone = !tooClose && IsInZone(step.Direction, _smoothedRatio);
      var transitionReady = now - _stepStartedAt >= _minimumTransitionSeconds;
      CurrentGuidance = ResolveGuidance(
        step.Direction,
        _smoothedRatio,
        _neutralMin,
        _neutralMax,
        _nearMin,
        _nearMax,
        _farMin,
        _farMax,
        _tooCloseRatio);
      State = tooClose
        ? FocusShiftState.TooClose
        : step.Direction == CareMovementDirection.Near
          ? FocusShiftState.Near
          : step.Direction == CareMovementDirection.Far
            ? FocusShiftState.Far
            : FocusShiftState.Neutral;

      if (!tooClose) RewardNewProgress(step, progress);
      FocusShiftProgressChanged?.Invoke(step.Direction, progress, _smoothedRatio);

      if (!inZone || !transitionReady)
      {
        _holdStartedAt = -1f;
        CurrentHoldProgress = 0f;
        _view?.Render(step.Direction, progress, _smoothedRatio, tooClose, CurrentGuidance, CurrentHoldProgress);
        return;
      }
      if (_holdStartedAt < 0f) _holdStartedAt = now;
      var hold = step.Direction == CareMovementDirection.Center ? _neutralHoldSeconds : _nearFarHoldSeconds;
      CurrentHoldProgress = Mathf.Clamp01((now - _holdStartedAt) / Mathf.Max(0.05f, hold));
      _view?.Render(step.Direction, progress, _smoothedRatio, tooClose, CurrentGuidance, CurrentHoldProgress);
      if (CurrentHoldProgress < 1f) return;

      FocusShiftStepCompleted?.Invoke(step.Direction);
      CareAudioFeedbackController.EnsureExists().PlayStepComplete();
      _stepIndex++;
      if (_stepIndex >= _steps.Count)
      {
        State = FocusShiftState.Completed;
        _emitter?.SetEmissionPaused(false);
        _gameplay?.SetFocusShiftActive(false);
        _view?.Hide();
        FocusShiftCompleted?.Invoke();
        return;
      }
      BeginStep(_smoothedRatio);
    }

    private void PauseForTracking(float now)
    {
      if (!_trackingPaused)
      {
        _trackingPaused = true;
        _trackingPausedAt = now;
        _holdStartedAt = -1f;
        if (!HasLocalFocusBaseline) ResetLocalBaselineCapture();
      }
      State = FocusShiftState.PausedTracking;
      _emitter?.SetEmissionPaused(true);
      _view?.SetTrackingLost();
    }

    private void ResumeTracking(float now, float rawSessionRatio)
    {
      if (_trackingPaused && HasLocalFocusBaseline && _trackingPausedAt >= 0f)
      {
        // Pause the minimum-transition clock while input is unavailable. The
        // current step, max progress and reward mask remain untouched.
        _stepStartedAt += Mathf.Max(0f, now - _trackingPausedAt);
      }
      _trackingPaused = false;
      _trackingPausedAt = -1f;
      _holdStartedAt = -1f;
      _smoothedRatio = _ratioFilter.Reset(rawSessionRatio, now);
      if (HasLocalFocusBaseline && _stepIndex >= 0 && _stepIndex < _steps.Count)
        _view?.SetDirection(CurrentDirection, _steps[_stepIndex].Rewards);
      else _view?.SetCalibrating(0f, false, _smoothedRatio);
    }

    private void BeginStep(float originRatio)
    {
      _stepOriginRatio = originRatio;
      _stepStartedAt = Time.unscaledTime;
      _holdStartedAt = -1f;
      _highestProgressReached = 0f;
      CurrentHoldProgress = 0f;
      _lastRewardSegment = -1;
      _rewardedSegmentMask = 0;
      _view?.SetDirection(_steps[_stepIndex].Direction, _steps[_stepIndex].Rewards);
    }

    private bool LocalBaselineSpreadIsStable()
    {
      if (_localBaselineSamples.Count < 2) return true;
      var minimum = float.PositiveInfinity;
      var maximum = float.NegativeInfinity;
      for (var i = 0; i < _localBaselineSamples.Count; i++)
      {
        minimum = Mathf.Min(minimum, _localBaselineSamples[i]);
        maximum = Mathf.Max(maximum, _localBaselineSamples[i]);
      }
      var median = Median(_localBaselineSamples);
      return IsFinitePositive(median) && (maximum - minimum) / median <= _maximumLocalBaselineSpread;
    }

    private void ResetLocalBaselineCapture()
    {
      _localBaselineSamples.Clear();
      _localBaselineStartedAt = -1f;
    }

    private static float GetFreshFaceScale(EyeInputDebugSnapshot snapshot)
    {
      if (IsFinitePositive(snapshot.RobustFaceScale)) return snapshot.RobustFaceScale;
      return IsFinitePositive(snapshot.FaceArea) ? snapshot.FaceArea : -1f;
    }

    internal static float Median(IReadOnlyList<float> values)
    {
      if (values == null || values.Count == 0) return -1f;
      var sorted = new List<float>(values.Count);
      for (var i = 0; i < values.Count; i++)
      {
        if (IsFinitePositive(values[i])) sorted.Add(values[i]);
      }
      if (sorted.Count == 0) return -1f;
      sorted.Sort();
      var middle = sorted.Count / 2;
      return sorted.Count % 2 == 0
        ? (sorted[middle - 1] + sorted[middle]) * 0.5f
        : sorted[middle];
    }

    private static bool IsFinitePositive(float value)
    {
      return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private float CalculateProgress(CareMovementDirection direction)
    {
      switch (direction)
      {
        case CareMovementDirection.Near:
          return Mathf.Clamp01((_smoothedRatio - _stepOriginRatio) / Mathf.Max(0.001f, _nearMin - _stepOriginRatio));
        case CareMovementDirection.Far:
          return Mathf.Clamp01((_stepOriginRatio - _smoothedRatio) / Mathf.Max(0.001f, _stepOriginRatio - _farMax));
        default:
          if (_steps[_stepIndex].Rewards == 0)
            return Mathf.Clamp01(1f - Mathf.Abs(_smoothedRatio - 1f) / Mathf.Max(0.001f, _neutralMax - 1f));
          return Mathf.Clamp01((_smoothedRatio - _stepOriginRatio) / Mathf.Max(0.001f, _neutralMin - _stepOriginRatio));
      }
    }

    private bool IsInZone(CareMovementDirection direction, float ratio)
    {
      switch (direction)
      {
        case CareMovementDirection.Near: return ratio >= _nearMin && ratio <= _nearMax;
        case CareMovementDirection.Far: return ratio >= _farMin && ratio <= _farMax;
        default: return ratio >= _neutralMin && ratio <= _neutralMax;
      }
    }

    public static FocusShiftGuidance ResolveGuidance(
      CareMovementDirection direction,
      float ratio,
      float neutralMin,
      float neutralMax,
      float nearMin,
      float nearMax,
      float farMin,
      float farMax,
      float tooCloseRatio)
    {
      if (ratio >= tooCloseRatio) return FocusShiftGuidance.MoveAway;
      switch (direction)
      {
        case CareMovementDirection.Near:
          if (ratio < nearMin) return FocusShiftGuidance.MoveCloser;
          if (ratio > nearMax) return FocusShiftGuidance.MoveAway;
          return FocusShiftGuidance.HoldSteady;
        case CareMovementDirection.Far:
          if (ratio > farMax) return FocusShiftGuidance.MoveAway;
          if (ratio < farMin) return FocusShiftGuidance.MoveCloser;
          return FocusShiftGuidance.HoldSteady;
        default:
          if (ratio < neutralMin) return FocusShiftGuidance.MoveCloser;
          if (ratio > neutralMax) return FocusShiftGuidance.MoveAway;
          return FocusShiftGuidance.HoldSteady;
      }
    }

    private void RewardNewProgress(ShiftStep step, float progress)
    {
      if (step.Rewards <= 0 || progress <= _highestProgressReached) return;
      var count = CareRewardSegmentLogic.CountNewSegments(_highestProgressReached, progress, step.Rewards);
      _highestProgressReached = Mathf.Max(_highestProgressReached, progress);
      if (count <= 0) return;
      for (var i = 0; i < count; i++)
      {
        var segment = _lastRewardSegment + 1;
        if (segment >= 0 && segment < 64) _rewardedSegmentMask |= 1UL << segment;
        _lastRewardSegment = segment;
        var nodeProgress = (segment + 1f) / Mathf.Max(1, step.Rewards);
        _emitter?.EnqueueFragments(1, false, step.Direction, nodeProgress);
      }
    }

    private void OnDestroy()
    {
      _emitter?.SetEmissionPaused(false);
      if (_gameplay != null) _gameplay.SetFocusShiftActive(false);
      if (Instance == this) Instance = null;
    }
  }
}
