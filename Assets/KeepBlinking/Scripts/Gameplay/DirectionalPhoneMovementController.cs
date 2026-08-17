using System;
using System.Collections.Generic;
using KeepBlinking.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KeepBlinking.Gameplay
{
  public enum DirectionalPhoneRoutine
  {
    Horizontal,
    Vertical,
    Complete,
  }

  public enum DirectionalPhoneAxis
  {
    Horizontal,
    Vertical,
  }

  public enum DirectionalPhoneMovementState
  {
    Dormant,
    Preparing,
    WaitNeutral,
    MoveToStart,
    HoldStart,
    Sweep,
    HoldEnd,
    ReturnCenter,
    PausedTracking,
    Completed,
  }

  public static class DirectionalPhoneMovementLogic
  {
    public static float DirectionProgress(
      CareMovementDirection direction,
      Vector2 faceDelta,
      float requiredHorizontal,
      float requiredVertical,
      float phoneLeftFaceSign,
      float phoneUpFaceSign)
    {
      switch (direction)
      {
        case CareMovementDirection.Left:
          return Mathf.Clamp01(faceDelta.x * phoneLeftFaceSign / Mathf.Max(0.001f, requiredHorizontal));
        case CareMovementDirection.Right:
          return Mathf.Clamp01(-faceDelta.x * phoneLeftFaceSign / Mathf.Max(0.001f, requiredHorizontal));
        case CareMovementDirection.Up:
          return Mathf.Clamp01(faceDelta.y * phoneUpFaceSign / Mathf.Max(0.001f, requiredVertical));
        case CareMovementDirection.Down:
          return Mathf.Clamp01(-faceDelta.y * phoneUpFaceSign / Mathf.Max(0.001f, requiredVertical));
        default:
          return 0f;
      }
    }

    public static float SweepProgress(float signedDelta, float startMagnitude, float endMagnitude)
    {
      var span = Mathf.Max(0.001f, Mathf.Abs(startMagnitude) + Mathf.Abs(endMagnitude));
      return Mathf.Clamp01((Mathf.Abs(startMagnitude) - signedDelta) / span);
    }

    public static bool IsInsideCenterEllipse(Vector2 delta, float horizontalRadius, float verticalRadius)
    {
      var x = delta.x / Mathf.Max(0.001f, horizontalRadius);
      var y = delta.y / Mathf.Max(0.001f, verticalRadius);
      return x * x + y * y <= 1f;
    }

    public static bool TryResolveDirectionSign(
      float primaryDelta,
      float crossDelta,
      float minimumPrimaryDelta,
      float crossAxisTolerance,
      out float sign)
    {
      sign = 0f;
      if (Mathf.Abs(primaryDelta) < Mathf.Max(0.001f, minimumPrimaryDelta) ||
          Mathf.Abs(crossDelta) > Mathf.Max(0.001f, crossAxisTolerance))
      {
        return false;
      }

      sign = Mathf.Sign(primaryDelta);
      return Mathf.Abs(sign) > 0.5f;
    }

    public static bool ScaleIsValid(float currentScale, float baselineScale, float minimumRatio, float maximumRatio)
    {
      if (currentScale <= 0f || baselineScale <= 0f) return false;
      var ratio = currentScale / baselineScale;
      return ratio >= minimumRatio && ratio <= maximumRatio;
    }

    public static bool CrossAxisIsValid(
      float primaryDelta,
      float crossDelta,
      float fixedTolerance,
      float deliberateMovementAllowance)
    {
      var allowed = Mathf.Max(
        Mathf.Max(0.001f, fixedTolerance),
        Mathf.Abs(primaryDelta) * Mathf.Max(0f, deliberateMovementAllowance));
      return Mathf.Abs(crossDelta) <= allowed;
    }

    public static bool IsSweepEndZone(
      float progress,
      bool endpointAlreadyLatched,
      float enterProgress,
      float stayProgress)
    {
      return progress >= (endpointAlreadyLatched ? stayProgress : enterProgress);
    }
  }

  /// <summary>
  /// Allocation-free One Euro filter. Low motion is strongly smoothed, while
  /// deliberate movement raises the cutoff to avoid a heavy fixed delay.
  /// </summary>
  public sealed class CareOneEuroFilter
  {
    private bool _initialized;
    private float _value;
    private float _derivative;

    public float Value => _value;

    public void Clear()
    {
      _initialized = false;
      _value = 0f;
      _derivative = 0f;
    }

    public void Reset(float value)
    {
      _initialized = true;
      _value = value;
      _derivative = 0f;
    }

    public float Filter(float value, float deltaTime, float minimumCutoff, float beta, float derivativeCutoff)
    {
      deltaTime = Mathf.Clamp(deltaTime, 0.001f, 0.1f);
      if (!_initialized)
      {
        Reset(value);
        return value;
      }

      var rawDerivative = (value - _value) / deltaTime;
      _derivative = Mathf.Lerp(_derivative, rawDerivative, Alpha(derivativeCutoff, deltaTime));
      var cutoff = Mathf.Max(0.01f, minimumCutoff + Mathf.Max(0f, beta) * Mathf.Abs(_derivative));
      _value = Mathf.Lerp(_value, value, Alpha(cutoff, deltaTime));
      return _value;
    }

    private static float Alpha(float cutoff, float deltaTime)
    {
      var tau = 1f / (2f * Mathf.PI * Mathf.Max(0.01f, cutoff));
      return 1f / (1f + tau / deltaTime);
    }
  }

  public sealed class DirectionalPhoneMovementController : MonoBehaviour
  {
    [Header("Action Center Baseline")]
    [SerializeField, Min(0.4f)] private float _actionBaselineCaptureSeconds = 0.8f;
    [SerializeField, Min(8)] private int _minimumActionBaselineSamples = 16;
    [SerializeField, Range(0.005f, 0.05f)] private float _maximumActionBaselineSpread = 0.035f;
    [SerializeField, Range(0f, 1f)] private float _minimumFaceCenterConfidence = 0.45f;
    [SerializeField] private float _actionNeutralDistanceMin = 0.90f;
    [SerializeField] private float _actionNeutralDistanceMax = 1.10f;

    [Header("Center Ellipse and Hysteresis")]
    [SerializeField, Range(0.03f, 0.08f)] private float _centerHorizontalRadius = 0.045f;
    [SerializeField, Range(0.03f, 0.09f)] private float _centerVerticalRadius = 0.055f;
    [SerializeField, Range(0f, 0.025f)] private float _maximumJitterAllowance = 0.015f;
    [SerializeField, Range(0f, 0.03f)] private float _centerExitHysteresis = 0.012f;

    [Header("Comfort Movement Range")]
    [SerializeField, Range(0.045f, 0.12f)] private float _fallbackHorizontalThreshold = 0.08f;
    [SerializeField, Range(0.045f, 0.12f)] private float _fallbackVerticalThreshold = 0.08f;
    [SerializeField, Range(0.6f, 0.7f)] private float _formalComfortFraction = 0.65f;
    [SerializeField, Range(0.045f, 0.08f)] private float _minimumDirectionThreshold = 0.045f;
    [SerializeField, Range(0.08f, 0.12f)] private float _maximumDirectionThreshold = 0.12f;
    [SerializeField, Range(0.06f, 0.12f)] private float _horizontalCrossAxisTolerance = 0.09f;
    [SerializeField, Range(0.06f, 0.12f)] private float _verticalCrossAxisTolerance = 0.09f;
    [SerializeField, Range(0.5f, 1.5f)] private float _deliberateMovementCrossAxisAllowance = 1.20f;
    [SerializeField, Range(0.7f, 1f)] private float _minimumScaleRatio = 0.86f;
    [SerializeField, Range(1f, 1.3f)] private float _maximumScaleRatio = 1.14f;
    [SerializeField, Range(0.02f, 0.06f)] private float _signDiscoveryDelta = 0.035f;

    [Header("Continuous Sweep")]
    [SerializeField, Range(12, 16)] private int _sweepRewardNodes = 14;
    [SerializeField, Range(0f, 0.04f)] private float _minorReverseTolerance = 0.02f;
    [SerializeField, Range(0.04f, 0.12f)] private float _majorReverseTolerance = 0.08f;
    [SerializeField, Min(0.2f)] private float _startHoldSeconds = 0.5f;
    [SerializeField, Min(0.2f)] private float _endHoldSeconds = 0.5f;
    [SerializeField, Range(0.85f, 0.98f)] private float _endHoldStayProgress = 0.92f;
    [SerializeField, Range(0.05f, 0.4f)] private float _endInputGraceSeconds = 0.22f;
    [SerializeField, Min(0.2f)] private float _returnCenterHoldSeconds = 0.35f;
    [SerializeField, Min(0.1f)] private float _trackingRecoverySeconds = 0.2f;
    [SerializeField, Range(0.35f, 1.2f)] private float _inputFreshnessSeconds = 0.8f;

    [Header("Adaptive Input Filter")]
    [SerializeField, Range(0.2f, 4f)] private float _oneEuroMinimumCutoff = 1.25f;
    [SerializeField, Range(0f, 2f)] private float _oneEuroBeta = 0.75f;
    [SerializeField, Range(0.2f, 4f)] private float _oneEuroDerivativeCutoff = 1f;

    private readonly List<float> _baselineX = new List<float>(90);
    private readonly List<float> _baselineY = new List<float>(90);
    private readonly List<float> _baselineScale = new List<float>(90);
    private readonly CareOneEuroFilter _xFilter = new CareOneEuroFilter();
    private readonly CareOneEuroFilter _yFilter = new CareOneEuroFilter();
    private readonly CareOneEuroFilter _scaleFilter = new CareOneEuroFilter();
    private DirectionalPhoneMovementView _view;
    private CareExperienceRewardEmitter _emitter;
    private DirectionalPhoneRoutine _routine;
    private DirectionalPhoneAxis _axis;
    private DirectionalPhoneMovementState _resumeState;
    private Vector2 _fallbackCenter;
    private Vector2 _actionBaseline;
    private Vector2 _filteredCenter;
    private Vector2 _rawCenter;
    private float _sessionDistanceBaseline;
    private float _actionBaselineFaceScale = -1f;
    private float _filteredFaceScale;
    private float _baselineStartedAt = -1f;
    private float _holdStartedAt = -1f;
    private float _endInvalidStartedAt = -1f;
    private float _trackingRecoveredAt = -1f;
    private float _sweepStartMagnitude;
    private float _currentProgress;
    private float _maxSweepProgress;
    private float _highestProgressReached;
    private int _lastRewardSegment = -1;
    private ulong _rewardedSegmentMask;
    private float _userLeftSign;
    private float _userDownSign;
    private float _comfortableLeftDelta;
    private float _comfortableRightDelta;
    private float _comfortableDownDelta;
    private float _comfortableUpDelta;
    private float _dynamicCenterHorizontalRadius;
    private float _dynamicCenterVerticalRadius;
    private string _blockingReason = "HOLD STEADY";
    private string _lastLoggedState;
    private bool _showDiagnostics;
    private long _lastSampleSequence = -1;
    private float _lastFreshSampleAt = -1f;
    private float _lastInputTimestamp = -1f;
    private bool _centerLatched;
    private float _currentDistanceRatio = 1f;

    public static DirectionalPhoneMovementController Instance { get; private set; }
    public static event Action<DirectionalPhoneRoutine> DirectionalMovementStarted;
    public static event Action<CareMovementDirection, float> DirectionalProgressChanged;
    public static event Action<CareMovementDirection> DirectionalStepCompleted;
    public static event Action<DirectionalPhoneRoutine> DirectionalMovementCompleted;
    public static event Action<DirectionalPhoneRoutine> DirectionalMovementSkipped;

    public DirectionalPhoneMovementState State { get; private set; } = DirectionalPhoneMovementState.Dormant;
    public DirectionalPhoneAxis CurrentAxis => _axis;
    public Vector2 ActionBaseline => _actionBaseline;
    public Vector2 RawFaceCenter => _rawCenter;
    public Vector2 FilteredFaceCenter => _filteredCenter;
    public Vector2 FaceDelta => _filteredCenter - _actionBaseline;
    public float UserLeftSign => _userLeftSign;
    public float UserDownSign => _userDownSign;
    public float CurrentProgress => _currentProgress;
    public float MaxProgress => _maxSweepProgress;
    public float HighestProgressReached => _highestProgressReached;
    public int LastRewardSegment => _lastRewardSegment;
    public ulong RewardedSegmentMask => _rewardedSegmentMask;
    public float CurrentDirectionThreshold => GetStartThreshold();
    public float ActionBaselineFaceScale => _actionBaselineFaceScale;
    public string BlockingReason => _blockingReason;
    public bool IsActive => State != DirectionalPhoneMovementState.Dormant &&
                            State != DirectionalPhoneMovementState.Completed;
    public CareMovementDirection CurrentDirection
    {
      get
      {
        if (State == DirectionalPhoneMovementState.ReturnCenter ||
            State == DirectionalPhoneMovementState.Preparing ||
            State == DirectionalPhoneMovementState.WaitNeutral)
          return CareMovementDirection.Center;
        if (_axis == DirectionalPhoneAxis.Horizontal)
          return State == DirectionalPhoneMovementState.MoveToStart || State == DirectionalPhoneMovementState.HoldStart
            ? CareMovementDirection.Left
            : CareMovementDirection.Right;
        return State == DirectionalPhoneMovementState.MoveToStart || State == DirectionalPhoneMovementState.HoldStart
          ? CareMovementDirection.Down
          : CareMovementDirection.Up;
      }
    }

    public static DirectionalPhoneMovementController EnsureExists(EdgeOrbitHarvestMvp gameplay)
    {
      if (Instance == null) Instance = FindFirstObjectByType<DirectionalPhoneMovementController>();
      if (Instance == null)
      {
        var owner = new GameObject("Directional Phone Movement Controller");
        Instance = owner.AddComponent<DirectionalPhoneMovementController>();
      }
      Instance._emitter = CareExperienceRewardEmitter.EnsureExists(gameplay);
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
      _view = gameObject.AddComponent<DirectionalPhoneMovementView>();
    }

    public bool StartRoutine(
      DirectionalPhoneRoutine routine,
      float sessionBaselineX,
      float sessionBaselineY,
      float sessionBaselineFaceScale)
    {
      if (IsActive || sessionBaselineFaceScale <= 0f) return false;
      _routine = routine;
      _fallbackCenter = new Vector2(sessionBaselineX, sessionBaselineY);
      _sessionDistanceBaseline = sessionBaselineFaceScale;
      _actionBaseline = _fallbackCenter;
      _baselineX.Clear();
      _baselineY.Clear();
      _baselineScale.Clear();
      _actionBaselineFaceScale = -1f;
      _baselineStartedAt = -1f;
      _holdStartedAt = -1f;
      _endInvalidStartedAt = -1f;
      _trackingRecoveredAt = -1f;
      _lastSampleSequence = -1;
      _lastFreshSampleAt = Time.unscaledTime;
      _lastInputTimestamp = -1f;
      _centerLatched = false;
      ResetFilters();
      ResetSweepRewards();
      _emitter?.SetEmissionPaused(false);
      _view.Show();
      SetState(DirectionalPhoneMovementState.Preparing, "HOLD STEADY");
      DirectionalMovementStarted?.Invoke(routine);
      return true;
    }

    public void Skip()
    {
      if (!IsActive) return;
      var skippedRoutine = _routine;
      State = DirectionalPhoneMovementState.Completed;
      _emitter?.SetEmissionPaused(false);
      _view?.Hide();
      DirectionalMovementSkipped?.Invoke(skippedRoutine);
    }

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (Keyboard.current != null && Keyboard.current.f7Key.wasPressedThisFrame)
        _showDiagnostics = !_showDiagnostics;
#endif
      if (!IsActive) return;

      var snapshot = EyeInputDebugState.Latest;
      if (!snapshot.FaceDetected || !snapshot.HasFaceCenter || snapshot.FaceArea <= 0f ||
          snapshot.FaceCenterConfidence < _minimumFaceCenterConfidence)
      {
        PauseForTrackingLoss();
        return;
      }

      if (snapshot.SampleSequence == _lastSampleSequence)
      {
        if (_lastFreshSampleAt >= 0f && Time.unscaledTime - _lastFreshSampleAt > _inputFreshnessSeconds)
          PauseForTrackingLoss();
        return;
      }
      _lastSampleSequence = snapshot.SampleSequence;
      _lastFreshSampleAt = Time.unscaledTime;

      _rawCenter = snapshot.FaceCenterNormalized;
      var deltaTime = _lastInputTimestamp > 0f
        ? Mathf.Clamp(snapshot.LastUpdateSeconds - _lastInputTimestamp, 0.001f, 0.1f)
        : Mathf.Clamp(Time.unscaledDeltaTime, 0.001f, 0.1f);
      _lastInputTimestamp = snapshot.LastUpdateSeconds;
      if (State == DirectionalPhoneMovementState.PausedTracking)
      {
        ResetFilters(_rawCenter, snapshot.FaceArea);
        if (_trackingRecoveredAt < 0f) _trackingRecoveredAt = Time.unscaledTime;
        if (Time.unscaledTime - _trackingRecoveredAt < _trackingRecoverySeconds)
        {
          _view.ShowTrackingLost(false);
          return;
        }
        SetState(_resumeState, "HOLD STEADY");
      }

      _filteredCenter = new Vector2(
        _xFilter.Filter(_rawCenter.x, deltaTime, _oneEuroMinimumCutoff, _oneEuroBeta, _oneEuroDerivativeCutoff),
        _yFilter.Filter(_rawCenter.y, deltaTime, _oneEuroMinimumCutoff, _oneEuroBeta, _oneEuroDerivativeCutoff));
      _filteredFaceScale = _scaleFilter.Filter(
        snapshot.FaceArea,
        deltaTime,
        _oneEuroMinimumCutoff,
        _oneEuroBeta * 0.5f,
        _oneEuroDerivativeCutoff);
      _currentDistanceRatio = _filteredFaceScale / Mathf.Max(
        0.000001f,
        _actionBaselineFaceScale > 0f ? _actionBaselineFaceScale : _sessionDistanceBaseline);
      _view.ShowTrackingLost(false);

      switch (State)
      {
        case DirectionalPhoneMovementState.Preparing:
          UpdateActionBaselineCapture();
          break;
        case DirectionalPhoneMovementState.WaitNeutral:
          UpdateInitialNeutral();
          break;
        case DirectionalPhoneMovementState.MoveToStart:
        case DirectionalPhoneMovementState.HoldStart:
          UpdateMoveToStart();
          break;
        case DirectionalPhoneMovementState.Sweep:
        case DirectionalPhoneMovementState.HoldEnd:
          UpdateSweep();
          break;
        case DirectionalPhoneMovementState.ReturnCenter:
          UpdateReturnCenter();
          break;
      }
    }

    private void UpdateActionBaselineCapture()
    {
      var ratio = _filteredFaceScale / Mathf.Max(0.000001f, _sessionDistanceBaseline);
      _currentDistanceRatio = ratio;
      if (ratio < _actionNeutralDistanceMin || ratio > _actionNeutralDistanceMax)
      {
        RestartActionBaseline(ratio < _actionNeutralDistanceMin ? "MOVE CLOSER" : "MOVE AWAY");
        return;
      }

      if (_baselineStartedAt < 0f) _baselineStartedAt = Time.unscaledTime;
      _baselineX.Add(_filteredCenter.x);
      _baselineY.Add(_filteredCenter.y);
      _baselineScale.Add(_filteredFaceScale);
      if (_baselineX.Count > 90)
      {
        _baselineX.RemoveAt(0);
        _baselineY.RemoveAt(0);
        _baselineScale.RemoveAt(0);
      }

      _view.RenderPreparation(Mathf.Clamp01((Time.unscaledTime - _baselineStartedAt) / _actionBaselineCaptureSeconds));
      if (Time.unscaledTime - _baselineStartedAt < _actionBaselineCaptureSeconds ||
          _baselineX.Count < _minimumActionBaselineSamples)
      {
        SetBlockingReason("HOLD STEADY");
        return;
      }

      var xSpread = PercentileRange(_baselineX, 0.1f, 0.9f);
      var ySpread = PercentileRange(_baselineY, 0.1f, 0.9f);
      if (xSpread > _maximumActionBaselineSpread || ySpread > _maximumActionBaselineSpread)
      {
        RestartActionBaseline("HOLD STEADY");
        return;
      }

      _actionBaseline = new Vector2(Median(_baselineX), Median(_baselineY));
      _actionBaselineFaceScale = Median(_baselineScale);
      _dynamicCenterHorizontalRadius = Mathf.Min(
        _centerHorizontalRadius + xSpread,
        _centerHorizontalRadius + _maximumJitterAllowance);
      _dynamicCenterVerticalRadius = Mathf.Min(
        _centerVerticalRadius + ySpread,
        _centerVerticalRadius + _maximumJitterAllowance);
      _holdStartedAt = Time.unscaledTime;
      SetState(DirectionalPhoneMovementState.WaitNeutral, "HOLD STEADY");
      Debug.Log(
        $"Directional action baseline captured: X={_actionBaseline.x:F4} Y={_actionBaseline.y:F4} " +
        $"Scale={_actionBaselineFaceScale:F6} " +
        $"UserLeftSign={_userLeftSign:F0} UserDownSign={_userDownSign:F0} " +
        $"CameraMirrored={EyeInputDebugState.Latest.CameraMirrored} " +
        $"CameraRotation={EyeInputDebugState.Latest.CameraRotationDegrees} " +
        $"ScreenOrientation={EyeInputDebugState.Latest.ScreenOrientation}.",
        this);
    }

    private void UpdateInitialNeutral()
    {
      var inside = UpdateCenterLatch() && IsScaleValid();
      _view.RenderCenter(inside, 1f);
      if (!inside)
      {
        _holdStartedAt = -1f;
        SetBlockingReason("RETURN TO CENTER");
        return;
      }
      if (_holdStartedAt < 0f) _holdStartedAt = Time.unscaledTime;
      if (Time.unscaledTime - _holdStartedAt < 0.15f) return;

      BeginAxis(_routine == DirectionalPhoneRoutine.Vertical
        ? DirectionalPhoneAxis.Vertical
        : DirectionalPhoneAxis.Horizontal);
    }

    private void BeginAxis(DirectionalPhoneAxis axis)
    {
      _axis = axis;
      _holdStartedAt = -1f;
      _currentProgress = 0f;
      _view.ConfigureAxis(axis, Mathf.Clamp(_sweepRewardNodes, 12, 16));
      SetState(DirectionalPhoneMovementState.MoveToStart,
        axis == DirectionalPhoneAxis.Horizontal ? "MOVE LEFT TO START" : "MOVE DOWN TO START");
    }

    private void UpdateMoveToStart()
    {
      var delta = FaceDelta;
      var primary = _axis == DirectionalPhoneAxis.Horizontal ? delta.x : delta.y;
      var cross = _axis == DirectionalPhoneAxis.Horizontal ? delta.y : delta.x;
      var sign = _axis == DirectionalPhoneAxis.Horizontal ? _userLeftSign : _userDownSign;
      var crossTolerance = _axis == DirectionalPhoneAxis.Horizontal
        ? _horizontalCrossAxisTolerance
        : _verticalCrossAxisTolerance;

      if (Mathf.Abs(sign) < 0.5f &&
          DirectionalPhoneMovementLogic.TryResolveDirectionSign(
            primary,
            cross,
            _signDiscoveryDelta,
            crossTolerance,
            out var learnedSign))
      {
        sign = learnedSign;
        if (_axis == DirectionalPhoneAxis.Horizontal) _userLeftSign = sign;
        else _userDownSign = sign;
        Debug.Log(
          $"Directional sign learned: UserLeftSign={_userLeftSign:F0} UserDownSign={_userDownSign:F0} " +
          $"CameraMirrored={EyeInputDebugState.Latest.CameraMirrored} " +
          $"CameraRotation={EyeInputDebugState.Latest.CameraRotationDegrees} " +
          $"ScreenOrientation={EyeInputDebugState.Latest.ScreenOrientation}.",
          this);
      }

      var threshold = GetStartThreshold();
      var signedDelta = Mathf.Abs(sign) < 0.5f ? 0f : primary * sign;
      var progress = Mathf.Clamp01(signedDelta / Mathf.Max(0.001f, threshold));
      _currentProgress = progress;
      var valid = IsScaleValid() &&
                  DirectionalPhoneMovementLogic.CrossAxisIsValid(
                    primary,
                    cross,
                    crossTolerance,
                    _deliberateMovementCrossAxisAllowance) &&
                  Mathf.Abs(sign) > 0.5f;
      _view.RenderMoveToStart(progress, State == DirectionalPhoneMovementState.HoldStart, valid);
      DirectionalProgressChanged?.Invoke(CurrentDirection, progress);

      if (!valid || progress < 0.999f)
      {
        if (State == DirectionalPhoneMovementState.HoldStart)
          SetState(DirectionalPhoneMovementState.MoveToStart, "MOVE A LITTLE MORE");
        else
          SetBlockingReason(IsScaleValid() ? "MOVE A LITTLE MORE" : "HOLD STEADY");
        _holdStartedAt = -1f;
        return;
      }

      if (State != DirectionalPhoneMovementState.HoldStart)
      {
        _holdStartedAt = Time.unscaledTime;
        SetState(DirectionalPhoneMovementState.HoldStart, string.Empty);
      }
      if (Time.unscaledTime - _holdStartedAt < _startHoldSeconds) return;

      _sweepStartMagnitude = Mathf.Max(threshold, signedDelta);
      RecordComfortableStart(_sweepStartMagnitude);
      ResetSweepRewards();
      _view.BeginSweep();
      CareAudioFeedbackController.EnsureExists().PlaySweepStart();
      DirectionalStepCompleted?.Invoke(CurrentDirection);
      SetState(DirectionalPhoneMovementState.Sweep,
        _axis == DirectionalPhoneAxis.Horizontal ? "SWEEP RIGHT" : "SWEEP UP");
    }

    private void UpdateSweep()
    {
      var delta = FaceDelta;
      var primary = _axis == DirectionalPhoneAxis.Horizontal ? delta.x : delta.y;
      var cross = _axis == DirectionalPhoneAxis.Horizontal ? delta.y : delta.x;
      var sign = _axis == DirectionalPhoneAxis.Horizontal ? _userLeftSign : _userDownSign;
      var signedDelta = primary * (Mathf.Abs(sign) < 0.5f ? 1f : sign);
      var endThreshold = GetEndThreshold();
      var rawProgress = DirectionalPhoneMovementLogic.SweepProgress(signedDelta, _sweepStartMagnitude, endThreshold);
      var reverseAmount = Mathf.Max(0f, _maxSweepProgress - rawProgress);
      var visualProgress = reverseAmount <= _minorReverseTolerance ? _maxSweepProgress : rawProgress;
      var crossTolerance = _axis == DirectionalPhoneAxis.Horizontal
        ? _horizontalCrossAxisTolerance
        : _verticalCrossAxisTolerance;
      var scaleValid = IsScaleValid();
      var crossValid = DirectionalPhoneMovementLogic.CrossAxisIsValid(
        primary,
        cross,
        crossTolerance,
        _deliberateMovementCrossAxisAllowance);
      var inputValid = scaleValid && crossValid;
      var majorReverse = reverseAmount > _majorReverseTolerance;

      if (inputValid && !majorReverse && rawProgress > _maxSweepProgress)
      {
        RewardNewSweepProgress(rawProgress);
      }
      _currentProgress = visualProgress;
      _view.RenderSweep(visualProgress, _maxSweepProgress, State == DirectionalPhoneMovementState.HoldEnd, inputValid);
      DirectionalProgressChanged?.Invoke(CurrentDirection, visualProgress);

      if (!scaleValid)
        SetBlockingReason("HOLD DISTANCE");
      else if (!crossValid)
        SetBlockingReason("MOVE STRAIGHT");
      else if (majorReverse)
        SetBlockingReason(_axis == DirectionalPhoneAxis.Horizontal ? "SWEEP RIGHT" : "SWEEP UP");
      else
        SetBlockingReason(string.Empty);

      var endpointLatched = State == DirectionalPhoneMovementState.HoldEnd;
      var atEnd = inputValid && DirectionalPhoneMovementLogic.IsSweepEndZone(
        rawProgress,
        endpointLatched,
        0.999f,
        _endHoldStayProgress);
      if (!atEnd)
      {
        if (endpointLatched)
        {
          if (_endInvalidStartedAt < 0f) _endInvalidStartedAt = Time.unscaledTime;
          if (Time.unscaledTime - _endInvalidStartedAt <= _endInputGraceSeconds)
          {
            SetBlockingReason("HOLD STEADY");
            return;
          }
        }
        if (State == DirectionalPhoneMovementState.HoldEnd)
          SetState(DirectionalPhoneMovementState.Sweep,
            _axis == DirectionalPhoneAxis.Horizontal ? "SWEEP RIGHT" : "SWEEP UP");
        _holdStartedAt = -1f;
        _endInvalidStartedAt = -1f;
        return;
      }

      if (State != DirectionalPhoneMovementState.HoldEnd)
      {
        // The end-zone tolerance is intentionally slightly wider than a perfect
        // mathematical 1.0. Complete the final one-shot node here so a low-FPS
        // camera frame can never leave one real reward behind.
        RewardNewSweepProgress(1f);
        _holdStartedAt = Time.unscaledTime;
        _endInvalidStartedAt = -1f;
        SetState(DirectionalPhoneMovementState.HoldEnd, "HOLD STEADY");
      }
      else if (_endInvalidStartedAt >= 0f)
      {
        // Brief invalid input pauses the required hold instead of counting as
        // stable time or erasing a genuinely reached endpoint.
        _holdStartedAt += Mathf.Max(0f, Time.unscaledTime - _endInvalidStartedAt);
        _endInvalidStartedAt = -1f;
      }
      if (Time.unscaledTime - _holdStartedAt < _endHoldSeconds) return;

      RecordComfortableEnd(Mathf.Abs(signedDelta));
      CareAudioFeedbackController.EnsureExists().PlaySweepEnd();
      DirectionalStepCompleted?.Invoke(CurrentDirection);
      _holdStartedAt = -1f;
      SetState(DirectionalPhoneMovementState.ReturnCenter, "RETURN TO CENTER");
    }

    private void UpdateReturnCenter()
    {
      var inside = UpdateCenterLatch() && IsScaleValid();
      var normalized = CenterProgress();
      _currentProgress = normalized;
      _view.RenderCenter(inside, normalized);
      DirectionalProgressChanged?.Invoke(CareMovementDirection.Center, normalized);
      if (!inside)
      {
        _holdStartedAt = -1f;
        SetBlockingReason("RETURN TO CENTER");
        return;
      }

      if (_holdStartedAt < 0f) _holdStartedAt = Time.unscaledTime;
      if (Time.unscaledTime - _holdStartedAt < _returnCenterHoldSeconds) return;

      DirectionalStepCompleted?.Invoke(CareMovementDirection.Center);
      CareAudioFeedbackController.EnsureExists().PlayStepComplete();
      if (_routine == DirectionalPhoneRoutine.Complete && _axis == DirectionalPhoneAxis.Horizontal)
      {
        BeginAxis(DirectionalPhoneAxis.Vertical);
        return;
      }
      CompleteRoutine();
    }

    private void RewardNewSweepProgress(float progress)
    {
      var count = CareRewardSegmentLogic.CountNewSegments(
        _maxSweepProgress,
        progress,
        Mathf.Clamp(_sweepRewardNodes, 12, 16));
      _maxSweepProgress = Mathf.Max(_maxSweepProgress, progress);
      _highestProgressReached = _maxSweepProgress;
      if (count <= 0) return;
      for (var i = 0; i < count; i++)
      {
        var segment = _lastRewardSegment + 1;
        if (segment >= 0 && segment < 64) _rewardedSegmentMask |= 1UL << segment;
        _lastRewardSegment = segment;
        var nodeProgress = (segment + 1f) / Mathf.Clamp(_sweepRewardNodes, 12, 16);
        _emitter?.EnqueueFragments(1, false, CurrentDirection, nodeProgress);
      }
    }

    private void CompleteRoutine()
    {
      State = DirectionalPhoneMovementState.Completed;
      _emitter?.SetEmissionPaused(false);
      _view.Hide();
      DirectionalMovementCompleted?.Invoke(_routine);
    }

    private void PauseForTrackingLoss()
    {
      if (State != DirectionalPhoneMovementState.PausedTracking)
      {
        _resumeState = State;
        if (State == DirectionalPhoneMovementState.Preparing)
          RestartActionBaseline("TRACKING LOST");
        State = DirectionalPhoneMovementState.PausedTracking;
        _trackingRecoveredAt = -1f;
        _holdStartedAt = -1f;
        _emitter?.SetEmissionPaused(true);
        ResetFilters();
        SetBlockingReason("TRACKING LOST");
      }
      _view.ShowTrackingLost(true);
    }

    private void RestartActionBaseline(string reason)
    {
      _baselineX.Clear();
      _baselineY.Clear();
      _baselineScale.Clear();
      _baselineStartedAt = -1f;
      SetBlockingReason(reason);
      _view.RenderPreparation(0f);
    }

    private void ResetFilters()
    {
      _xFilter.Clear();
      _yFilter.Clear();
      _scaleFilter.Clear();
      _filteredCenter = _fallbackCenter;
      _filteredFaceScale = _sessionDistanceBaseline;
      _lastInputTimestamp = -1f;
    }

    private void ResetFilters(Vector2 center, float scale)
    {
      _xFilter.Reset(center.x);
      _yFilter.Reset(center.y);
      _scaleFilter.Reset(scale);
      _filteredCenter = center;
      _filteredFaceScale = scale;
    }

    private bool IsScaleValid()
    {
      return DirectionalPhoneMovementLogic.ScaleIsValid(
        _filteredFaceScale,
        _actionBaselineFaceScale > 0f ? _actionBaselineFaceScale : _sessionDistanceBaseline,
        _minimumScaleRatio,
        _maximumScaleRatio);
    }

    private bool IsInsideCenter(bool useExitHysteresis)
    {
      var extra = useExitHysteresis ? _centerExitHysteresis : 0f;
      return DirectionalPhoneMovementLogic.IsInsideCenterEllipse(
        FaceDelta,
        Mathf.Max(_centerHorizontalRadius, _dynamicCenterHorizontalRadius) + extra,
        Mathf.Max(_centerVerticalRadius, _dynamicCenterVerticalRadius) + extra);
    }

    private bool UpdateCenterLatch()
    {
      var inside = IsInsideCenter(_centerLatched);
      _centerLatched = inside;
      return inside;
    }

    private float CenterProgress()
    {
      var radiusX = Mathf.Max(_centerHorizontalRadius, _dynamicCenterHorizontalRadius);
      var radiusY = Mathf.Max(_centerVerticalRadius, _dynamicCenterVerticalRadius);
      var delta = FaceDelta;
      var normalizedDistance = Mathf.Sqrt(
        Mathf.Pow(delta.x / Mathf.Max(0.001f, radiusX), 2f) +
        Mathf.Pow(delta.y / Mathf.Max(0.001f, radiusY), 2f));
      return Mathf.Clamp01(1f - normalizedDistance);
    }

    private float GetStartThreshold()
    {
      var comfortable = _axis == DirectionalPhoneAxis.Horizontal
        ? _comfortableLeftDelta
        : _comfortableDownDelta;
      var fallback = _axis == DirectionalPhoneAxis.Horizontal
        ? _fallbackHorizontalThreshold
        : _fallbackVerticalThreshold;
      return CalibratedThreshold(comfortable, fallback);
    }

    private float GetEndThreshold()
    {
      var comfortable = _axis == DirectionalPhoneAxis.Horizontal
        ? _comfortableRightDelta
        : _comfortableUpDelta;
      var fallback = _axis == DirectionalPhoneAxis.Horizontal
        ? _fallbackHorizontalThreshold
        : _fallbackVerticalThreshold;
      return CalibratedThreshold(comfortable, fallback);
    }

    private float CalibratedThreshold(float comfortable, float fallback)
    {
      var value = comfortable > 0.001f ? comfortable * _formalComfortFraction : fallback;
      return Mathf.Clamp(value, _minimumDirectionThreshold, _maximumDirectionThreshold);
    }

    private void RecordComfortableStart(float magnitude)
    {
      if (_axis == DirectionalPhoneAxis.Horizontal)
        _comfortableLeftDelta = Mathf.Clamp(Mathf.Max(_comfortableLeftDelta, magnitude), _minimumDirectionThreshold, _maximumDirectionThreshold);
      else
        _comfortableDownDelta = Mathf.Clamp(Mathf.Max(_comfortableDownDelta, magnitude), _minimumDirectionThreshold, _maximumDirectionThreshold);
    }

    private void RecordComfortableEnd(float magnitude)
    {
      if (_axis == DirectionalPhoneAxis.Horizontal)
        _comfortableRightDelta = Mathf.Clamp(Mathf.Max(_comfortableRightDelta, magnitude), _minimumDirectionThreshold, _maximumDirectionThreshold);
      else
        _comfortableUpDelta = Mathf.Clamp(Mathf.Max(_comfortableUpDelta, magnitude), _minimumDirectionThreshold, _maximumDirectionThreshold);
    }

    private void ResetSweepRewards()
    {
      _currentProgress = 0f;
      _maxSweepProgress = 0f;
      _highestProgressReached = 0f;
      _lastRewardSegment = -1;
      _rewardedSegmentMask = 0;
    }

    private void SetState(DirectionalPhoneMovementState state, string reason)
    {
      State = state;
      if (state == DirectionalPhoneMovementState.WaitNeutral ||
          state == DirectionalPhoneMovementState.ReturnCenter)
      {
        _centerLatched = false;
      }
      _view.SetState(state, _axis);
      SetBlockingReason(reason);
      LogStateIfChanged();
    }

    private void SetBlockingReason(string reason)
    {
      reason = reason ?? string.Empty;
      if (_blockingReason == reason)
      {
        // SetState may have refreshed the view's normal prompt. Re-apply the
        // current blocker even when its diagnostic value did not change.
        _view.SetStatus(reason);
        return;
      }
      _blockingReason = reason;
      _view.SetStatus(reason);
      LogStateIfChanged();
    }

    private void LogStateIfChanged()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      var summary = $"{State}|{_blockingReason}";
      if (_lastLoggedState == summary) return;
      _lastLoggedState = summary;
      Debug.Log($"Directional Movement: State={State} BlockingReason={_blockingReason}", this);
#endif
    }

    private static float Median(List<float> values)
    {
      if (values == null || values.Count == 0) return 0f;
      var copy = values.ToArray();
      Array.Sort(copy);
      var middle = copy.Length / 2;
      return copy.Length % 2 == 0 ? (copy[middle - 1] + copy[middle]) * 0.5f : copy[middle];
    }

    private static float PercentileRange(List<float> values, float low, float high)
    {
      if (values == null || values.Count == 0) return float.PositiveInfinity;
      var copy = values.ToArray();
      Array.Sort(copy);
      var lowIndex = Mathf.Clamp(Mathf.RoundToInt((copy.Length - 1) * low), 0, copy.Length - 1);
      var highIndex = Mathf.Clamp(Mathf.RoundToInt((copy.Length - 1) * high), 0, copy.Length - 1);
      return Mathf.Max(0f, copy[highIndex] - copy[lowIndex]);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private void OnGUI()
    {
      if (!_showDiagnostics) return;
      var flow = FirstLevelCareFlowController.Instance;
      var focus = FocusShiftController.Instance;
      var style = new GUIStyle(GUI.skin.box)
      {
        alignment = TextAnchor.UpperLeft,
        fontSize = 13,
        normal = { textColor = Color.white },
      };
      var scaleRatio = _sessionDistanceBaseline > 0f ? _filteredFaceScale / _sessionDistanceBaseline : 0f;
      var focusState = focus != null ? focus.State.ToString() : "--";
      var focusRatio = focus != null ? focus.DistanceRatio : 0f;
      var focusRawRatio = focus != null ? focus.RawDistanceRatio : 0f;
      var focusGuidance = focus != null ? focus.CurrentGuidance.ToString() : "--";
      var focusHold = focus != null ? focus.CurrentHoldProgress : 0f;
      var text =
        $"DIRECTIONAL INPUT (F7)\n" +
        $"Current State: {State}\n" +
        $"Raw X / Y: {_rawCenter.x:F4} / {_rawCenter.y:F4}\n" +
        $"Filtered X / Y: {_filteredCenter.x:F4} / {_filteredCenter.y:F4}\n" +
        $"Action Baseline X / Y: {_actionBaseline.x:F4} / {_actionBaseline.y:F4}\n" +
        $"Delta X / Y: {FaceDelta.x:F4} / {FaceDelta.y:F4}\n" +
        $"User Left Sign: {_userLeftSign:F0}\n" +
        $"User Down Sign: {_userDownSign:F0}\n" +
        $"Current Progress: {_currentProgress:F3}\n" +
        $"Max Progress: {_maxSweepProgress:F3}\n" +
        $"Center Radius: {Mathf.Max(_centerHorizontalRadius, _dynamicCenterHorizontalRadius):F3} / {Mathf.Max(_centerVerticalRadius, _dynamicCenterVerticalRadius):F3}\n" +
        $"Direction Threshold: {CurrentDirectionThreshold:F3}\n" +
        $"Face Scale: {_filteredFaceScale:F6} ({scaleRatio:F3})\n" +
        $"Action Distance Ratio: {_currentDistanceRatio:F3}\n" +
        $"Baseline Samples: {_baselineX.Count}/{_minimumActionBaselineSamples}\n" +
        $"Session Distance Baseline: {_sessionDistanceBaseline:F6}\n" +
        $"Action Distance Baseline: {_actionBaselineFaceScale:F6}\n" +
        $"Local Focus Baseline: {(focus != null ? focus.LocalFocusBaseline : 0f):F6}\n" +
        $"Focus State: {focusState}\n" +
        $"Focus Ratio Raw / Filtered: {focusRawRatio:F3} / {focusRatio:F3}\n" +
        $"Focus Guidance / Hold: {focusGuidance} / {focusHold:F2}\n" +
        $"Tracking Confidence: {EyeInputDebugState.Latest.FaceCenterConfidence:F2}\n" +
        $"Camera Mirrored: {EyeInputDebugState.Latest.CameraMirrored}\n" +
        $"Camera Rotation: {EyeInputDebugState.Latest.CameraRotationDegrees}\n" +
        $"Screen Orientation: {EyeInputDebugState.Latest.ScreenOrientation}\n" +
        $"Blocking Reason: {_blockingReason}";
      GUI.Box(new Rect(16f, 58f, 420f, 520f), text, style);
    }
#endif

    private void OnDestroy()
    {
      _emitter?.SetEmissionPaused(false);
      if (Instance == this) Instance = null;
    }
  }
}
