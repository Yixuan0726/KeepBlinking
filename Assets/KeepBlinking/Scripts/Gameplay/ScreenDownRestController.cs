using System;
using KeepBlinking.Input;
using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace KeepBlinking.Gameplay
{
  public enum ScreenDownRestState
  {
    Dormant,
    WaitingForCameraReady,
    CaptureNormalOrientation,
    RestSeedIntro,
    PromptScreenDown,
    WaitScreenDown,
    Resting,
    ReadyToReturn,
    WaitNormalOrientation,
    WaitFaceReturn,
    SeedBloom,
    SensorUnavailable,
    Completed,
    Skipped,
  }

  /// <summary>
  /// Orientation judgements for the screen-down rest.
  ///
  /// Everything here works on the gravity vector expressed in *device* space, never on
  /// <see cref="UnityEngine.InputSystem.AttitudeSensor"/>. The device frame is fixed by Unity
  /// (x right, y up along the screen, z out of the screen), so the screen normal is always
  /// <see cref="Vector3.forward"/> and a single dot product answers "is the screen facing the
  /// ground". Attitude, by contrast, is not a device-to-Unity-world rotation on Android/iOS --
  /// using it to derive a world gravity vector produced a reference that depended on the pose
  /// held while calibrating, which is why face-down was never detected on device.
  /// </summary>
  public static class ScreenDownRestMotionLogic
  {
    public const float StandardGravity = 9.80665f;

    public static int StoredGoldFragments(float validRestSeconds, float restDurationSeconds)
    {
      return Mathf.Clamp(
        Mathf.FloorToInt(Mathf.Max(0f, validRestSeconds)),
        0,
        Mathf.Max(0, Mathf.FloorToInt(restDurationSeconds)));
    }

    /// <summary>
    /// Accelerometer and gravity readings are expected in g units, but a platform reporting
    /// m/s^2 would otherwise make every stability check fail. Rescale the obvious case.
    /// </summary>
    public static float ToGravityUnits(float rawMagnitude)
    {
      return rawMagnitude > 4f ? rawMagnitude / StandardGravity : rawMagnitude;
    }

    /// <summary>
    /// Face-down means gravity points along the screen normal (+z) in device space:
    /// lying face up reads (0, 0, -1), lying face down reads (0, 0, +1).
    /// </summary>
    public static bool IsScreenDown(Vector3 deviceGravity, float groundAlignmentDegrees)
    {
      if (deviceGravity.sqrMagnitude <= 0.0001f) return false;
      return Vector3.Dot(deviceGravity.normalized, Vector3.forward) >=
             Mathf.Cos(Mathf.Clamp(groundAlignmentDegrees, 5f, 80f) * Mathf.Deg2Rad);
    }

    /// <summary>
    /// Comparing device-space gravity vectors ignores heading, so turning around while picking
    /// the phone back up no longer blocks the return.
    /// </summary>
    public static bool IsReturned(Vector3 initialDeviceGravity, Vector3 currentDeviceGravity, float maximumAngle)
    {
      if (initialDeviceGravity.sqrMagnitude <= 0.0001f || currentDeviceGravity.sqrMagnitude <= 0.0001f) return false;
      return Vector3.Angle(initialDeviceGravity, currentDeviceGravity) <= Mathf.Max(1f, maximumAngle);
    }

    /// <summary>
    /// A resting device measures exactly 1 g. The reference is the constant, not a sample taken
    /// while the player happened to be holding the phone.
    /// </summary>
    public static bool IsStable(
      float accelerationMagnitude,
      float accelerationTolerance,
      float angularSpeed,
      float maximumAngularSpeed)
    {
      return Mathf.Abs(ToGravityUnits(accelerationMagnitude) - 1f) <= Mathf.Max(0.01f, accelerationTolerance) &&
             angularSpeed <= Mathf.Max(0.01f, maximumAngularSpeed);
    }

    /// <summary>Radians per second implied by how fast the gravity direction is sweeping.</summary>
    public static float AngularSpeedFromGravity(Vector3 previousGravity, Vector3 currentGravity, float delta)
    {
      if (previousGravity.sqrMagnitude <= 0.0001f || currentGravity.sqrMagnitude <= 0.0001f) return 0f;
      return Vector3.Angle(previousGravity, currentGravity) * Mathf.Deg2Rad / Mathf.Max(0.0001f, delta);
    }
  }

  public sealed class ScreenDownRestController : MonoBehaviour
  {
    public const string ReportDisplayName = "Screen-Down Rest";

    [Header("Orientation")]
    [SerializeField, Range(10f, 70f)] private float _groundAlignmentDegrees = 40f;
    [SerializeField, Min(0.1f)] private float _screenDownHoldSeconds = 0.5f;
    [SerializeField, Range(5f, 45f)] private float _returnAngleDegrees = 20f;
    [SerializeField, Min(0.1f)] private float _returnHoldSeconds = 0.4f;

    [Header("Stability")]
    [SerializeField, Min(0.01f)] private float _accelerationTolerance = 0.18f;
    [SerializeField, Min(0.01f)] private float _maximumGyroRadiansPerSecond = 0.35f;
    [SerializeField, Min(1f)] private float _maximumFallbackDegreesPerSecond = 30f;

    [Header("Timing")]
    [SerializeField, Min(1f)] private float _restDuration = 8f;
    [SerializeField, Min(0.1f)] private float _restSeedIntroSeconds = 1.1f;
    [SerializeField, Min(0.1f)] private float _seedBloomSeconds = 1.15f;
    [SerializeField, Min(0.1f)] private float _faceReturnHoldSeconds = 0.5f;
    [SerializeField] private bool _preserveInterruptedRestProgress;
    [SerializeField, Min(0f)] private float _interruptedProgressDecayPerSecond = 2f;

    [Header("Face Return")]
    [SerializeField, Range(0.05f, 0.45f)] private float _faceCenterHorizontalTolerance = 0.22f;
    [SerializeField, Range(0.05f, 0.45f)] private float _faceCenterVerticalTolerance = 0.25f;
    [SerializeField, Range(0.05f, 0.9f)] private float _minimumEyeOpen = 0.35f;

    private EdgeOrbitHarvestMvp _gameplay;
    private ScreenDownRestView _view;
    private GravitySensor _gravitySensor;
    private Accelerometer _accelerometer;
    private UnityEngine.InputSystem.Gyroscope _gyroscope;
    private Vector3 _initialDeviceGravity = Vector3.back;
    private Vector3 _deviceGravity = Vector3.back;
    private Vector3 _previousDeviceGravity = Vector3.zero;
    private float _accelerationMagnitude = 1f;
    private float _angularSpeed;
    private float _sensorRetryAt;
    private float _stateStartedAt;
    private float _conditionHeldSeconds;
    private float _restElapsed;
    private float _faceReturnHeldSeconds;
    private bool _orientationCaptured;
    private bool _sessionNeutralConfigured;
    private bool _completionFeedbackPlayed;
    private bool _wasSkipped;
    private int _storedGoldFragments;
    private bool _simulateScreenDown;
    private bool _simulateReturn;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private float _developmentTimeMultiplier = 1f;
#endif
    private bool _stationUse;
    private float _configuredRestDuration;
    private AudioSource _audioSource;
    private AudioClip _completionClip;

    public static ScreenDownRestController Instance { get; private set; }
    public static event Action ScreenDownRestCompleted;
    public static event Action ScreenDownRestSkipped;
    public static event Action ScreenDownRestStarted;
    public static event Action<int> ScreenDownRestRewardsReady;

    public ScreenDownRestState State { get; private set; } = ScreenDownRestState.Dormant;
    public Vector3 InitialDeviceGravity => _initialDeviceGravity;
    public Vector3 DeviceGravity => _deviceGravity;
    public bool HasSessionNeutralOrientation => _sessionNeutralConfigured;
    public int StoredGoldFragments => _storedGoldFragments;
    public float RestProgress => Mathf.Clamp01(_restElapsed / Mathf.Max(0.1f, _restDuration));
    public bool IsActive => State != ScreenDownRestState.Dormant &&
                            State != ScreenDownRestState.Completed &&
                            State != ScreenDownRestState.Skipped;
    public bool SensorsAvailable => _gravitySensor != null || _accelerometer != null;

    public static ScreenDownRestController EnsureExists(EdgeOrbitHarvestMvp gameplay)
    {
      if (Instance == null) Instance = FindFirstObjectByType<ScreenDownRestController>();
      if (Instance == null)
      {
        var owner = new GameObject("Screen-Down Rest Controller");
        Instance = owner.AddComponent<ScreenDownRestController>();
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
      _configuredRestDuration = _restDuration;
      _view = gameObject.AddComponent<ScreenDownRestView>();
      CreateCompletionAudio();
    }

    private void Bind(EdgeOrbitHarvestMvp gameplay)
    {
      _gameplay = gameplay;
    }

    public void BeginOpeningRest()
    {
      StartRoundRest();
    }

    public bool StartRoundRest()
    {
      _stationUse = false;
      _restDuration = Mathf.Max(1f, _configuredRestDuration);
      return StartRestInternal();
    }

    public bool StartStationRest(float durationSeconds)
    {
      _stationUse = true;
      _restDuration = Mathf.Max(1f, durationSeconds);
      return StartRestInternal();
    }

    private bool StartRestInternal()
    {
      if (IsActive) return false;
      _wasSkipped = false;
      _restElapsed = 0f;
      _storedGoldFragments = 0;
      _completionFeedbackPlayed = false;
      _simulateScreenDown = false;
      _simulateReturn = false;
      _gameplay?.SetScreenDownRestActive(true);
      SoftFocusFieldController.Instance?.SetCareInteractionPaused(true);
      _gameplay?.SetFirstLevelModalPaused(true, true);
      ResolveAndEnableSensors();
      SetState(ScreenDownRestState.WaitingForCameraReady);
      if (!_stationUse) _view?.Show();
      ScreenDownRestStarted?.Invoke();
      return true;
    }

    public bool TryCaptureSessionNeutralOrientation()
    {
      ResolveAndEnableSensors();
      if (_sessionNeutralConfigured) return true;
      if (!SensorsAvailable) return false;
      if (!TryCaptureInitialOrientation(false)) return false;
      _sessionNeutralConfigured = true;
      return true;
    }

    public void Skip()
    {
      if (!IsActive) return;
      _wasSkipped = true;
      SetState(ScreenDownRestState.SeedBloom);
    }

    public void CancelStationRestForDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (!_stationUse || !IsActive) return;
      _gameplay?.SetScreenDownRestActive(false);
      _gameplay?.SetFirstLevelModalPaused(false, false);
      _view?.Hide();
      _stationUse = false;
      SetState(ScreenDownRestState.Dormant);
#endif
    }

    public void SetDevelopmentTimeMultiplier(float multiplier)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _developmentTimeMultiplier = Mathf.Clamp(multiplier, 1f, 20f);
#endif
    }

    public void ShowReturnNeutralPrompt(string prompt)
    {
      _view?.ShowReturnNeutral(prompt);
    }

    public void HideReturnNeutralPrompt()
    {
      _view?.Hide();
    }

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (Keyboard.current != null && Keyboard.current.f11Key.wasPressedThisFrame)
      {
        if (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed)
        {
          _simulateScreenDown = false;
          _simulateReturn = true;
          Debug.Log("Screen-Down Rest simulation: RETURN.", this);
        }
        else
        {
          _simulateScreenDown = true;
          _simulateReturn = false;
          Debug.Log("Screen-Down Rest simulation: SCREEN DOWN.", this);
        }
      }
#endif

      if (State == ScreenDownRestState.SensorUnavailable && (_simulateScreenDown || _simulateReturn))
      {
        SetState(ScreenDownRestState.CaptureNormalOrientation);
      }

      if (!IsActive) return;
      var delta = Time.unscaledDeltaTime;
      // Sample before the development time multiplier is applied: a scaled delta would
      // understate how fast the device is actually turning.
      SampleMotion(delta);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_stationUse) delta *= _developmentTimeMultiplier;
#endif
      RetrySensorsIfUnavailable();
      UpdateState(delta);
      _view?.Render(State, RestProgress, _simulateScreenDown, _simulateReturn);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _view?.SetDiagnostics(BuildDiagnostics());
#endif
    }

    /// <summary>
    /// Reads the motion sensors once per frame so every judgement in <see cref="UpdateState"/>
    /// sees the same sample, and so the no-gyroscope fallback always has a fresh previous
    /// sample instead of one left over from whenever a short-circuited check last ran.
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

    private void RetrySensorsIfUnavailable()
    {
      if (State != ScreenDownRestState.SensorUnavailable) return;
      if (Time.unscaledTime < _sensorRetryAt) return;
      _sensorRetryAt = Time.unscaledTime + 0.5f;
      ResolveAndEnableSensors();
      if (SensorsAvailable) SetState(ScreenDownRestState.CaptureNormalOrientation);
    }

    private void UpdateState(float delta)
    {
      switch (State)
      {
        case ScreenDownRestState.WaitingForCameraReady:
          if (EyeInputDebugState.Latest.FaceDetected || _simulateScreenDown || _simulateReturn)
          {
            SetState(ScreenDownRestState.CaptureNormalOrientation);
          }
          break;
        case ScreenDownRestState.CaptureNormalOrientation:
          if (!SensorsAvailable && !_simulateScreenDown && !_simulateReturn)
          {
            SetState(ScreenDownRestState.SensorUnavailable);
            break;
          }
          if (!TryCaptureInitialOrientation(true)) break;
          SetState(_stationUse ? ScreenDownRestState.PromptScreenDown : ScreenDownRestState.RestSeedIntro);
          break;
        case ScreenDownRestState.RestSeedIntro:
          if (StateElapsed >= _restSeedIntroSeconds) SetState(ScreenDownRestState.PromptScreenDown);
          break;
        case ScreenDownRestState.PromptScreenDown:
          if (StateElapsed >= (_stationUse ? 0.2f : 0.7f)) SetState(ScreenDownRestState.WaitScreenDown);
          break;
        case ScreenDownRestState.WaitScreenDown:
          if (IsScreenDownAndStable())
          {
            _conditionHeldSeconds += delta;
            if (_conditionHeldSeconds >= _screenDownHoldSeconds) SetState(ScreenDownRestState.Resting);
          }
          else
          {
            _conditionHeldSeconds = 0f;
            if (!_preserveInterruptedRestProgress)
              _restElapsed = Mathf.Max(0f, _restElapsed - _interruptedProgressDecayPerSecond * delta);
          }
          break;
        case ScreenDownRestState.Resting:
          if (!IsScreenDownAndStable())
          {
            if (!_preserveInterruptedRestProgress) _restElapsed = 0f;
            SetState(ScreenDownRestState.WaitScreenDown);
            break;
          }
          _restElapsed += delta;
          _storedGoldFragments = Mathf.Max(
            _storedGoldFragments,
            ScreenDownRestMotionLogic.StoredGoldFragments(_restElapsed, _restDuration));
          if (_restElapsed >= _restDuration)
          {
            PlayCompletionFeedbackOnce();
            SetState(ScreenDownRestState.ReadyToReturn);
          }
          break;
        case ScreenDownRestState.ReadyToReturn:
          if (StateElapsed >= 0.25f) SetState(ScreenDownRestState.WaitNormalOrientation);
          break;
        case ScreenDownRestState.WaitNormalOrientation:
          if (IsNormalOrientationAndStable())
          {
            _conditionHeldSeconds += delta;
            if (_conditionHeldSeconds >= _returnHoldSeconds) SetState(ScreenDownRestState.WaitFaceReturn);
          }
          else
          {
            _conditionHeldSeconds = 0f;
          }
          break;
        case ScreenDownRestState.WaitFaceReturn:
          if (IsFaceReady())
          {
            _faceReturnHeldSeconds += delta;
            if (_faceReturnHeldSeconds >= _faceReturnHoldSeconds)
            {
              if (_stationUse) FinishOpening();
              else SetState(ScreenDownRestState.SeedBloom);
            }
          }
          else
          {
            _faceReturnHeldSeconds = 0f;
          }
          break;
        case ScreenDownRestState.SeedBloom:
          if (StateElapsed >= _seedBloomSeconds) FinishOpening();
          break;
      }
    }

    private float StateElapsed => Time.unscaledTime - _stateStartedAt;

    private void SetState(ScreenDownRestState state)
    {
      if (State == state) return;
      State = state;
      _stateStartedAt = Time.unscaledTime;
      _conditionHeldSeconds = 0f;
      if (state == ScreenDownRestState.WaitFaceReturn) _faceReturnHeldSeconds = 0f;
      _view?.SetState(state);
      Debug.Log($"Screen-Down Rest state: {state}.", this);
    }

    private void ResolveAndEnableSensors()
    {
      _gravitySensor = GravitySensor.current;
      _accelerometer = Accelerometer.current;
      _gyroscope = UnityEngine.InputSystem.Gyroscope.current;
      EnableSensor(_gravitySensor);
      EnableSensor(_accelerometer);
      EnableSensor(_gyroscope);
    }

    private static void EnableSensor(InputDevice sensor)
    {
      if (sensor != null && !sensor.enabled) InputSystem.EnableDevice(sensor);
    }

    private bool TryCaptureInitialOrientation(bool allowConfiguredNeutral)
    {
      if (allowConfiguredNeutral && _sessionNeutralConfigured)
      {
        _orientationCaptured = true;
        return true;
      }

      if (_simulateScreenDown || _simulateReturn)
      {
        _initialDeviceGravity = Vector3.back;
        _deviceGravity = Vector3.back;
        _previousDeviceGravity = Vector3.back;
        _orientationCaptured = true;
        return true;
      }

      var gravity = ReadGravity();
      var accelerationMagnitude = ReadAccelerationMagnitude();
      if (gravity.sqrMagnitude <= 0.0001f || accelerationMagnitude <= 0.01f) return false;

      _initialDeviceGravity = gravity.normalized;
      _deviceGravity = _initialDeviceGravity;
      _previousDeviceGravity = _initialDeviceGravity;
      _orientationCaptured = true;
      return true;
    }

    private bool IsScreenDownAndStable()
    {
      if (_simulateScreenDown) return true;
      if (!_orientationCaptured || !SensorsAvailable) return false;
      return ScreenDownRestMotionLogic.IsScreenDown(_deviceGravity, _groundAlignmentDegrees) &&
             IsMovementStable();
    }

    private bool IsNormalOrientationAndStable()
    {
      if (_simulateReturn) return true;
      if (!_orientationCaptured || !SensorsAvailable) return false;
      return ScreenDownRestMotionLogic.IsReturned(_initialDeviceGravity, _deviceGravity, _returnAngleDegrees) &&
             IsMovementStable();
    }

    private bool IsMovementStable()
    {
      var maximumAngularSpeed = _gyroscope != null
        ? _maximumGyroRadiansPerSecond
        : _maximumFallbackDegreesPerSecond * Mathf.Deg2Rad;
      return ScreenDownRestMotionLogic.IsStable(
        _accelerationMagnitude,
        _accelerationTolerance,
        _angularSpeed,
        maximumAngularSpeed);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>
    /// On-device readout: without it a stuck rest looks identical whether the camera never
    /// reported a face, the sensors are missing, the phone is not flat enough, or the device
    /// is still judged to be moving.
    /// </summary>
    private string BuildDiagnostics()
    {
      var down = Vector3.Dot(_deviceGravity, Vector3.forward);
      var sensor = _gravitySensor != null ? "gravity" : _accelerometer != null ? "accel" : "none";
      return
        $"g·fwd {down:F2} (need {Mathf.Cos(Mathf.Clamp(_groundAlignmentDegrees, 5f, 80f) * Mathf.Deg2Rad):F2})  " +
        $"|a| {ScreenDownRestMotionLogic.ToGravityUnits(_accelerationMagnitude):F2}g  " +
        $"ω {_angularSpeed:F2}rad/s\n" +
        $"src {sensor}  gyro {(_gyroscope != null ? "y" : "n")}  " +
        $"face {(EyeInputDebugState.Latest.FaceDetected ? "y" : "n")}  " +
        $"hold {_conditionHeldSeconds:F1}s";
    }
#endif

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

    private bool IsFaceReady()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_simulateReturn) return true;
#endif
      var snapshot = EyeInputDebugState.Latest;
      if (!snapshot.FaceDetected || snapshot.IsBlinking) return false;
      var center = snapshot.FaceRect.center;
      var centered = Mathf.Abs(center.x - 0.5f) <= _faceCenterHorizontalTolerance &&
                     Mathf.Abs(center.y - 0.5f) <= _faceCenterVerticalTolerance;
      var eyesOpen = (snapshot.LeftEyeOpen + snapshot.RightEyeOpen) * 0.5f >= _minimumEyeOpen;
      return centered && eyesOpen;
    }

    private void PlayCompletionFeedbackOnce()
    {
      if (_completionFeedbackPlayed) return;
      _completionFeedbackPlayed = true;
      if (_audioSource != null && _completionClip != null) _audioSource.PlayOneShot(_completionClip, 0.34f);
#if UNITY_IOS && !UNITY_EDITOR
      AudioServicesPlaySystemSound(1519);
#endif
    }

    private void CreateCompletionAudio()
    {
      _audioSource = gameObject.AddComponent<AudioSource>();
      _audioSource.playOnAwake = false;
      _audioSource.spatialBlend = 0f;
      const int sampleRate = 24000;
      const float duration = 0.34f;
      var samples = Mathf.RoundToInt(sampleRate * duration);
      var data = new float[samples];
      for (var i = 0; i < samples; i++)
      {
        var t = i / (float)sampleRate;
        var envelope = Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI);
        data[i] = Mathf.Sin(t * Mathf.PI * 2f * 523.25f) * envelope * 0.16f;
      }
      _completionClip = AudioClip.Create("Screen-Down Rest Complete", samples, 1, sampleRate, false);
      _completionClip.SetData(data, 0);
    }

    private void FinishOpening()
    {
      _gameplay?.SetScreenDownRestActive(false);
      SoftFocusFieldController.Instance?.SetCareInteractionPaused(false);
      _gameplay?.SetFirstLevelModalPaused(false, false);
      if (!_stationUse) _view?.Hide();
      if (_wasSkipped)
      {
        SetState(ScreenDownRestState.Skipped);
        ScreenDownRestSkipped?.Invoke();
      }
      else
      {
        SetState(ScreenDownRestState.Completed);
        ScreenDownRestRewardsReady?.Invoke(_storedGoldFragments);
        ScreenDownRestCompleted?.Invoke();
      }
    }

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void AudioServicesPlaySystemSound(uint soundId);
#endif

    private void OnDestroy()
    {
      if (Instance == this) Instance = null;
    }
  }
}
