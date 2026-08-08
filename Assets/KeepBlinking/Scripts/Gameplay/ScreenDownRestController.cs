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

  public static class ScreenDownRestMotionLogic
  {
    public static int StoredGoldFragments(float validRestSeconds, float restDurationSeconds)
    {
      return Mathf.Clamp(
        Mathf.FloorToInt(Mathf.Max(0f, validRestSeconds)),
        0,
        Mathf.Max(0, Mathf.FloorToInt(restDurationSeconds)));
    }

    public static Vector3 ScreenNormal(Quaternion attitude)
    {
      return (attitude * Vector3.forward).normalized;
    }

    public static bool IsScreenDown(
      Vector3 initialScreenNormal,
      Vector3 currentScreenNormal,
      Vector3 worldGravity,
      float relativeAngleThreshold,
      float groundAlignmentDegrees)
    {
      var relativeAngle = Vector3.Angle(initialScreenNormal, currentScreenNormal);
      var gravityDirection = worldGravity.sqrMagnitude > 0.0001f ? worldGravity.normalized : Vector3.down;
      var groundAligned = Vector3.Dot(currentScreenNormal.normalized, gravityDirection) >=
                          Mathf.Cos(Mathf.Clamp(groundAlignmentDegrees, 0f, 89f) * Mathf.Deg2Rad);
      return relativeAngle >= Mathf.Clamp(relativeAngleThreshold, 90f, 180f) || groundAligned;
    }

    public static bool IsReturned(Quaternion initialAttitude, Quaternion currentAttitude, float maximumAngle)
    {
      return Quaternion.Angle(initialAttitude, currentAttitude) <= Mathf.Max(1f, maximumAngle);
    }

    public static bool IsStable(
      float accelerationMagnitude,
      float stableAccelerationMagnitude,
      float accelerationTolerance,
      float angularSpeed,
      float maximumAngularSpeed)
    {
      return Mathf.Abs(accelerationMagnitude - stableAccelerationMagnitude) <= Mathf.Max(0.01f, accelerationTolerance) &&
             angularSpeed <= Mathf.Max(0.01f, maximumAngularSpeed);
    }
  }

  public sealed class ScreenDownRestController : MonoBehaviour
  {
    public const string ReportDisplayName = "Screen-Down Rest";

    [Header("Orientation")]
    [SerializeField, Range(90f, 180f)] private float _screenDownRelativeAngle = 150f;
    [SerializeField, Range(5f, 60f)] private float _groundAlignmentDegrees = 30f;
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
    private AttitudeSensor _attitudeSensor;
    private GravitySensor _gravitySensor;
    private Accelerometer _accelerometer;
    private UnityEngine.InputSystem.Gyroscope _gyroscope;
    private Quaternion _initialDeviceAttitude = Quaternion.identity;
    private Quaternion _previousAttitude = Quaternion.identity;
    private Vector3 _initialScreenNormal = Vector3.forward;
    private Vector3 _initialWorldGravity = Vector3.down;
    private float _stableAccelerationMagnitude = 1f;
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
    private AudioSource _audioSource;
    private AudioClip _completionClip;

    public static ScreenDownRestController Instance { get; private set; }
    public static event Action ScreenDownRestCompleted;
    public static event Action ScreenDownRestSkipped;
    public static event Action ScreenDownRestStarted;
    public static event Action<int> ScreenDownRestRewardsReady;

    public ScreenDownRestState State { get; private set; } = ScreenDownRestState.Dormant;
    public Quaternion InitialDeviceAttitude => _initialDeviceAttitude;
    public Vector3 InitialScreenNormal => _initialScreenNormal;
    public bool HasSessionNeutralOrientation => _sessionNeutralConfigured;
    public int StoredGoldFragments => _storedGoldFragments;
    public float RestProgress => Mathf.Clamp01(_restElapsed / Mathf.Max(0.1f, _restDuration));
    public bool IsActive => State != ScreenDownRestState.Dormant &&
                            State != ScreenDownRestState.Completed &&
                            State != ScreenDownRestState.Skipped;
    public bool SensorsAvailable => _attitudeSensor != null &&
                                    (_gravitySensor != null || _accelerometer != null);

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
      _view?.Show();
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
      UpdateState(delta);
      _view?.Render(State, RestProgress, _simulateScreenDown, _simulateReturn);
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
          SetState(ScreenDownRestState.RestSeedIntro);
          break;
        case ScreenDownRestState.RestSeedIntro:
          if (StateElapsed >= _restSeedIntroSeconds) SetState(ScreenDownRestState.PromptScreenDown);
          break;
        case ScreenDownRestState.PromptScreenDown:
          if (StateElapsed >= 0.7f) SetState(ScreenDownRestState.WaitScreenDown);
          break;
        case ScreenDownRestState.WaitScreenDown:
          if (IsScreenDownAndStable(delta))
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
          if (!IsScreenDownAndStable(delta))
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
          if (IsNormalOrientationAndStable(delta))
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
            if (_faceReturnHeldSeconds >= _faceReturnHoldSeconds) SetState(ScreenDownRestState.SeedBloom);
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
      _attitudeSensor = AttitudeSensor.current;
      _gravitySensor = GravitySensor.current;
      _accelerometer = Accelerometer.current;
      _gyroscope = UnityEngine.InputSystem.Gyroscope.current;
      EnableSensor(_attitudeSensor);
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
        _previousAttitude = _initialDeviceAttitude;
        _orientationCaptured = true;
        return true;
      }

      if (_simulateScreenDown || _simulateReturn)
      {
        _initialDeviceAttitude = Quaternion.identity;
        _previousAttitude = Quaternion.identity;
        _initialScreenNormal = Vector3.forward;
        _initialWorldGravity = Vector3.down;
        _stableAccelerationMagnitude = 1f;
        _orientationCaptured = true;
        return true;
      }

      var gravity = ReadGravity();
      var accelerationMagnitude = ReadAccelerationMagnitude();
      if (_attitudeSensor == null || gravity.sqrMagnitude <= 0.0001f || accelerationMagnitude <= 0.01f)
        return false;

      var attitude = _attitudeSensor.attitude.ReadValue();
      var attitudeMagnitude = attitude.x * attitude.x + attitude.y * attitude.y + attitude.z * attitude.z + attitude.w * attitude.w;
      if (attitudeMagnitude <= 0.25f) return false;

      _initialDeviceAttitude = attitude;
      _previousAttitude = _initialDeviceAttitude;
      _initialScreenNormal = ScreenDownRestMotionLogic.ScreenNormal(_initialDeviceAttitude);
      _initialWorldGravity = (_initialDeviceAttitude * gravity.normalized).normalized;
      _stableAccelerationMagnitude = accelerationMagnitude;
      _orientationCaptured = true;
      return true;
    }

    private bool IsScreenDownAndStable(float delta)
    {
      if (_simulateScreenDown) return true;
      if (!_orientationCaptured || !SensorsAvailable) return false;
      var attitude = _attitudeSensor.attitude.ReadValue();
      var currentNormal = ScreenDownRestMotionLogic.ScreenNormal(attitude);
      return ScreenDownRestMotionLogic.IsScreenDown(
               _initialScreenNormal,
               currentNormal,
               _initialWorldGravity,
               _screenDownRelativeAngle,
               _groundAlignmentDegrees) &&
             IsMovementStable(attitude, delta);
    }

    private bool IsNormalOrientationAndStable(float delta)
    {
      if (_simulateReturn) return true;
      if (!_orientationCaptured || !SensorsAvailable) return false;
      var attitude = _attitudeSensor.attitude.ReadValue();
      return ScreenDownRestMotionLogic.IsReturned(_initialDeviceAttitude, attitude, _returnAngleDegrees) &&
             IsMovementStable(attitude, delta);
    }

    private bool IsMovementStable(Quaternion attitude, float delta)
    {
      var angularSpeed = _gyroscope != null
        ? _gyroscope.angularVelocity.ReadValue().magnitude
        : Quaternion.Angle(_previousAttitude, attitude) / Mathf.Max(0.0001f, delta) * Mathf.Deg2Rad;
      var maximumAngularSpeed = _gyroscope != null
        ? _maximumGyroRadiansPerSecond
        : _maximumFallbackDegreesPerSecond * Mathf.Deg2Rad;
      _previousAttitude = attitude;
      return ScreenDownRestMotionLogic.IsStable(
        ReadAccelerationMagnitude(),
        _stableAccelerationMagnitude,
        _accelerationTolerance,
        angularSpeed,
        maximumAngularSpeed);
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
      _view?.Hide();
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
