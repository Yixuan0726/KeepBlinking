using System;
using System.Runtime.InteropServices;
using KeepBlinking.Input;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  public sealed class OffScreenEyeBreakController : MonoBehaviour
  {
    public const string ReportDisplayName = "Off-Screen Eye Break";

    public enum MovementState
    {
      Dormant,
      Preparing,
      PreviewDirection,
      WaitOffScreen,
      DirectionConfirmed,
      WaitReturnCenter,
      NextDirection,
      Completed,
      Skipped,
      PausedByTracking,
      PausedByUser,
    }

    private static readonly OffScreenDirection[] DirectionSequence =
    {
      OffScreenDirection.Left,
      OffScreenDirection.Right,
      OffScreenDirection.Up,
      OffScreenDirection.Down,
    };

    private static readonly Color WarmWhite = new Color(0.96f, 0.93f, 0.84f, 1f);
    private static readonly Color MintGreen = new Color(0.43f, 0.91f, 0.72f, 1f);
    private static readonly Color SandGold = new Color(0.86f, 0.70f, 0.39f, 1f);

    [Header("First Level Trigger")]
    [SerializeField] private bool _triggerAfterSecondUpgrade = true;

    [Header("Timing")]
    [SerializeField, Min(0.2f)] private float _previewSeconds = 1f;
    [SerializeField, Min(1f)] private float _directionTimeoutSeconds = 8f;
    [SerializeField, Min(0.1f)] private float _confirmationSeconds = 0.28f;
    [SerializeField, Min(0.1f)] private float _nextDirectionSeconds = 0.18f;
    [SerializeField, Min(0.2f)] private float _completionSeconds = 0.6f;
    [SerializeField, Range(0f, 0.8f)] private float _backgroundDimAlpha = 0.3f;

    [Header("Detection")]
    [SerializeField] private OffScreenEyeMovementThresholds _thresholds = default;
    [SerializeField, Range(0.05f, 0.95f)] private float _minimumEyeOpen = 0.42f;

    [Header("Development")]
    [SerializeField] private bool _showDebugHud;

    private readonly OffScreenEyeMovementDetector _detector = new OffScreenEyeMovementDetector();
    private EdgeOrbitHarvestMvp _gameplay;
    private MovementState _state = MovementState.Dormant;
    private MovementState _stateBeforeTrackingPause;
    private MovementState _stateBeforeUserPause;
    private int _directionIndex;
    private int _consecutiveTimeouts;
    private double _stateStartedAt;
    private double _firstPromptUntil;
    private bool _automaticTriggerConsumed;
    private bool _skipAvailable;
    private bool _ownedGameplayPause;
    private float _previousTimeScale = 1f;
    private string _technicalStatus = string.Empty;
    private double _nextStatusLogAt;
    private OffScreenEyeMovementSample _latestSample;
    private bool _hasLatestSample;
    private Texture2D _discTexture;
    private AudioSource _audioSource;
    private AudioClip _confirmationClip;

    public static OffScreenEyeBreakController Instance { get; private set; }
    public static event Action OffScreenGazeBreakCompleted;

    public MovementState State => _state;
    public bool IsActive => _state != MovementState.Dormant;
    private bool IsPresentationActive => _state != MovementState.Dormant && _state != MovementState.Preparing;
    public OffScreenDirection CurrentDirection => DirectionSequence[Mathf.Clamp(_directionIndex, 0, DirectionSequence.Length - 1)];

    public static OffScreenEyeBreakController EnsureExists(EdgeOrbitHarvestMvp gameplay)
    {
      if (Instance != null)
      {
        Instance.Bind(gameplay);
        return Instance;
      }

      Instance = FindFirstObjectByType<OffScreenEyeBreakController>();
      if (Instance == null)
      {
        var owner = new GameObject("Off-Screen Eye Break Controller");
        Instance = owner.AddComponent<OffScreenEyeBreakController>();
      }

      Instance.Bind(gameplay);
      return Instance;
    }

    public void StartEyeMovementBreak()
    {
      if (IsActive || _gameplay == null)
      {
        return;
      }

      BeginActiveBreak();
    }

    private void BeginActiveBreak()
    {

      EnsureThresholdDefaults();
      _directionIndex = 0;
      _consecutiveTimeouts = 0;
      _skipAvailable = false;
      _technicalStatus = string.Empty;
      _detector.Reset();
      _firstPromptUntil = Time.unscaledTimeAsDouble + Mathf.Max(0.5f, _previewSeconds);
      PauseGameplay();
      SoftFocusFieldController.Instance?.SetEyeBreakPaused(true);
      _gameplay.SetOffScreenEyeBreakPending(false);
      GazeProviderComparisonController.SetOffScreenSamplingRequested(true);
      SetState(MovementState.PreviewDirection);
      Debug.Log("Off-Screen Eye Break started. L2CS raw direction and MediaPipe validity gates are active.");
    }

    private void Awake()
    {
      if (Instance != null && Instance != this)
      {
        Destroy(gameObject);
        return;
      }

      Instance = this;
      EnsureThresholdDefaults();
      CreateRuntimeAssets();
    }

    private void Bind(EdgeOrbitHarvestMvp gameplay)
    {
      if (_gameplay == gameplay)
      {
        return;
      }

      if (_gameplay != null)
      {
        _gameplay.FirstLevelModuleInstalled -= HandleModuleInstalled;
      }

      _gameplay = gameplay;
      if (_gameplay != null)
      {
        _gameplay.FirstLevelModuleInstalled += HandleModuleInstalled;
      }
    }

    private void HandleModuleInstalled(FirstLevelModuleId moduleId)
    {
      if (!_triggerAfterSecondUpgrade || _automaticTriggerConsumed || _gameplay == null ||
          _gameplay.InstalledFirstLevelModuleCount < 2)
      {
        return;
      }

      _automaticTriggerConsumed = true;
      _gameplay.SetOffScreenEyeBreakPending(true);
      SetState(MovementState.Preparing);
    }

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (UnityEngine.Input.GetKeyDown(KeyCode.F10))
      {
        if (IsActive) CompleteAndResume(false);
        else StartEyeMovementBreak();
      }

      if (UnityEngine.Input.GetKeyDown(KeyCode.F11))
      {
        _showDebugHud = !_showDebugHud;
      }

      if (IsActive && UnityEngine.Input.GetKeyDown(KeyCode.F12))
      {
        SkipCurrentStep();
      }
#endif

      if (_state == MovementState.Dormant && _gameplay != null && _gameplay.InstalledFirstLevelModuleCount == 0)
      {
        _automaticTriggerConsumed = false;
      }

      if (!IsActive)
      {
        return;
      }

      if (_state == MovementState.Preparing)
      {
        if (_gameplay != null && _gameplay.CanStartOffScreenEyeBreak)
        {
          BeginActiveBreak();
        }
        return;
      }

      if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
      {
        ToggleUserPause();
      }

      if (_state == MovementState.PausedByUser)
      {
        return;
      }


      var elapsed = (float)(Time.unscaledTimeAsDouble - _stateStartedAt);
      if (_state == MovementState.Completed)
      {
        if (elapsed >= _completionSeconds)
        {
          CompleteAndResume(true);
        }
        return;
      }
      if (_state == MovementState.Skipped)
      {
        if (elapsed >= 0.3f)
        {
          CompleteAndResume(false);
        }
        return;
      }

      var hasTechnicalSample = TryBuildSample(out _latestSample, out var failureReason);
      _hasLatestSample = hasTechnicalSample;
      if (!hasTechnicalSample)
      {
        EnterTrackingPause(failureReason);
        if (Time.unscaledTimeAsDouble - _stateStartedAt >= _directionTimeoutSeconds)
        {
          HandleTrackingTimeout();
        }
        return;
      }

      if (_state == MovementState.PausedByTracking)
      {
        _technicalStatus = string.Empty;
        SetState(_stateBeforeTrackingPause == MovementState.PausedByTracking
          ? MovementState.PreviewDirection
          : _stateBeforeTrackingPause);
      }

      var deltaTime = Time.unscaledDeltaTime;
      elapsed = (float)(Time.unscaledTimeAsDouble - _stateStartedAt);
      switch (_state)
      {
        case MovementState.PreviewDirection:
          if (elapsed >= _previewSeconds)
          {
            _detector.ResetDirectionHold();
            SetState(MovementState.WaitOffScreen);
          }
          break;

        case MovementState.WaitOffScreen:
          if (_detector.UpdateDirection(CurrentDirection, _latestSample, _thresholds, deltaTime))
          {
            _consecutiveTimeouts = 0;
            _skipAvailable = false;
            PlayDirectionConfirmation();
            SetState(MovementState.DirectionConfirmed);
          }
          else if (elapsed >= _directionTimeoutSeconds)
          {
            HandleTimeout();
          }
          break;

        case MovementState.DirectionConfirmed:
          if (elapsed >= _confirmationSeconds)
          {
            _detector.ResetReturnHold();
            SetState(MovementState.WaitReturnCenter);
          }
          break;

        case MovementState.WaitReturnCenter:
          if (_detector.UpdateReturnCenter(_latestSample, _thresholds, deltaTime))
          {
            _consecutiveTimeouts = 0;
            _skipAvailable = false;
            SetState(_directionIndex >= DirectionSequence.Length - 1
              ? MovementState.Completed
              : MovementState.NextDirection);
          }
          else if (elapsed >= _directionTimeoutSeconds)
          {
            HandleTimeout();
          }
          break;

        case MovementState.NextDirection:
          if (elapsed >= _nextDirectionSeconds)
          {
            _directionIndex = Mathf.Min(_directionIndex + 1, DirectionSequence.Length - 1);
            SetState(MovementState.PreviewDirection);
          }
          break;

      }
    }

    private bool TryBuildSample(out OffScreenEyeMovementSample sample, out string failureReason)
    {
      sample = default;
      var snapshot = EyeInputDebugState.Latest;
      if (!snapshot.FaceDetected)
      {
        failureReason = "TRACKING LOST";
        return false;
      }

      if (!snapshot.HasHeadPose)
      {
        failureReason = "HEAD POSE UNAVAILABLE";
        return false;
      }

      if (!GazeProviderComparisonController.TryGetOffScreenDirection(out var gaze, out failureReason))
      {
        return false;
      }

      var eyesOpen = snapshot.LeftEyeOpen >= _minimumEyeOpen && snapshot.RightEyeOpen >= _minimumEyeOpen;
      sample = new OffScreenEyeMovementSample(
        true,
        gaze.TrackingValid,
        eyesOpen,
        snapshot.IsBlinking,
        _gameplay != null && _gameplay.AreEyesClosed,
        snapshot.HasHeadPose,
        gaze.RawDirectionDegrees,
        gaze.CenteredDirectionDegrees,
        snapshot.HeadYawDegrees,
        snapshot.HeadPitchDegrees);
      failureReason = string.Empty;
      return true;
    }

    private void EnterTrackingPause(string reason)
    {
      if (_state != MovementState.PausedByTracking)
      {
        _stateBeforeTrackingPause = _state;
        SetState(MovementState.PausedByTracking);
        _detector.Reset();
      }

      _technicalStatus = string.IsNullOrWhiteSpace(reason) ? "GAZE UNAVAILABLE" : reason;
      if (Time.unscaledTimeAsDouble >= _nextStatusLogAt)
      {
        Debug.LogWarning($"Off-Screen Eye Break paused: {_technicalStatus}");
        _nextStatusLogAt = Time.unscaledTimeAsDouble + 2d;
      }
    }

    private void HandleTimeout()
    {
      _consecutiveTimeouts++;
      _skipAvailable = _consecutiveTimeouts >= 2;
      _detector.Reset();
      SetState(MovementState.PreviewDirection);
      Debug.Log($"Off-Screen Eye Break direction {CurrentDirection} timed out without penalty. Preview replayed.");
    }

    private void HandleTrackingTimeout()
    {
      _consecutiveTimeouts++;
      _skipAvailable = _consecutiveTimeouts >= 2;
      _stateStartedAt = Time.unscaledTimeAsDouble;
      Debug.Log($"Off-Screen Eye Break remained paused by tracking for {_directionTimeoutSeconds:0.#}s. No penalty was applied.");
    }

    private void SkipCurrentStep()
    {
      if (!IsActive)
      {
        return;
      }
      _consecutiveTimeouts = 0;
      _skipAvailable = false;
      _detector.Reset();
      SetState(MovementState.Skipped);
    }

    private void ToggleUserPause()
    {
      if (_state == MovementState.PausedByUser)
      {
        SetState(_stateBeforeUserPause == MovementState.PausedByUser
          ? MovementState.PreviewDirection
          : _stateBeforeUserPause);
        return;
      }

      _stateBeforeUserPause = _state;
      SetState(MovementState.PausedByUser);
      _detector.Reset();
    }

    private void SetState(MovementState state)
    {
      _state = state;
      _stateStartedAt = Time.unscaledTimeAsDouble;
    }

    private void PauseGameplay()
    {
      _previousTimeScale = Time.timeScale;
      Time.timeScale = 0f;
      if (_gameplay != null && !_gameplay.IsFirstLevelModalPaused)
      {
        _ownedGameplayPause = true;
        _gameplay.SetFirstLevelModalPaused(true, false);
      }
    }

    private void CompleteAndResume(bool completed)
    {
      GazeProviderComparisonController.SetOffScreenSamplingRequested(false);
      _gameplay?.SetOffScreenEyeBreakPending(false);
      SoftFocusFieldController.Instance?.SetEyeBreakPaused(false);
      if (_ownedGameplayPause && _gameplay != null)
      {
        _gameplay.SetFirstLevelModalPaused(false, false);
      }

      _ownedGameplayPause = false;
      Time.timeScale = Mathf.Max(0f, _previousTimeScale);
      _technicalStatus = string.Empty;
      _hasLatestSample = false;
      SetState(MovementState.Dormant);
      if (completed)
      {
        SoftFocusFieldController.Instance?.GrantFreshFocus();
        Debug.Log($"{ReportDisplayName} completed.");
        OffScreenGazeBreakCompleted?.Invoke();
      }
    }

    private void PlayDirectionConfirmation()
    {
      if (_audioSource != null && _confirmationClip != null)
      {
        _audioSource.PlayOneShot(_confirmationClip, 0.28f);
      }

      TriggerLightHaptic();
    }

    private void CreateRuntimeAssets()
    {
      _discTexture = CreateDiscTexture(64);
      _audioSource = gameObject.AddComponent<AudioSource>();
      _audioSource.playOnAwake = false;
      _audioSource.loop = false;
      _audioSource.spatialBlend = 0f;
      _confirmationClip = CreateConfirmationClip();
    }

    private void EnsureThresholdDefaults()
    {
      if (_thresholds.DirectionHoldSeconds <= 0f)
      {
        _thresholds = OffScreenEyeMovementThresholds.Default;
      }
    }

    private void OnGUI()
    {
      if (!IsPresentationActive)
      {
        return;
      }

      var previousColor = GUI.color;
      GUI.color = new Color(0f, 0f, 0f, _backgroundDimAlpha);
      GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
      GUI.color = previousColor;

      DrawMovementIcon();
      DrawPauseControl();
      DrawTechnicalStatus();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_showDebugHud)
      {
        DrawDebugHud();
      }

      if (GUI.Button(new Rect(Screen.width - 76f, 46f, 66f, 28f), "SKIP"))
      {
        SkipCurrentStep();
      }
#endif

      if (_skipAvailable && GUI.Button(new Rect(Screen.width * 0.5f - 25f, Screen.height - 64f, 50f, 34f), ">>"))
      {
        SkipCurrentStep();
      }
    }

    private void DrawMovementIcon()
    {
      var center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
      var shortSide = Mathf.Min(Screen.width, Screen.height);
      var width = Mathf.Clamp(shortSide * 0.42f, 180f, 320f);
      var height = width * 0.52f;
      var alpha = GetIconAlpha();
      var direction = DirectionVector(CurrentDirection);
      var previewProgress = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((float)(Time.unscaledTimeAsDouble - _stateStartedAt) / Mathf.Max(0.1f, _previewSeconds)));

      if (_state == MovementState.Completed)
      {
        direction = Vector2.zero;
        previewProgress = 0f;
        DrawCompletionRipple(center, width);
      }
      else if (_state == MovementState.WaitReturnCenter || _state == MovementState.NextDirection)
      {
        direction = Vector2.zero;
        previewProgress = 0f;
      }

      var closure = 0f;
      if (_state == MovementState.Completed)
      {
        var t = Mathf.Clamp01((float)(Time.unscaledTimeAsDouble - _stateStartedAt) / Mathf.Max(0.1f, _completionSeconds));
        closure = Mathf.Sin(t * Mathf.PI);
      }

      var eyeHeight = Mathf.Lerp(height, 4f, closure);
      DrawEyeOutline(center, width, eyeHeight, new Color(WarmWhite.r, WarmWhite.g, WarmWhite.b, alpha), 3f);

      var pupilOffset = direction * new Vector2(width * 0.24f, height * 0.22f) * previewProgress;
      var pupilSize = Mathf.Clamp(width * 0.11f, 22f, 36f) * Mathf.Lerp(1f, 0.2f, closure);
      GUI.color = new Color(MintGreen.r, MintGreen.g, MintGreen.b, alpha);
      GUI.DrawTexture(new Rect(center.x + pupilOffset.x - pupilSize * 0.5f, center.y - pupilOffset.y - pupilSize * 0.5f, pupilSize, pupilSize), _discTexture);
      GUI.color = Color.white;

      if (_state == MovementState.PreviewDirection || _state == MovementState.WaitOffScreen)
      {
        DrawDirectionArc(center, width, height, CurrentDirection, new Color(SandGold.r, SandGold.g, SandGold.b, alpha));
      }

      if (Time.unscaledTimeAsDouble <= _firstPromptUntil)
      {
        var style = new GUIStyle(GUI.skin.label)
        {
          alignment = TextAnchor.MiddleCenter,
          fontSize = Mathf.Clamp(Mathf.RoundToInt(shortSide * 0.032f), 17, 26),
          normal = { textColor = WarmWhite },
        };
        GUI.Label(new Rect(center.x - width, center.y - height - 66f, width * 2f, 42f), "LOOK AWAY", style);
      }
    }

    private float GetIconAlpha()
    {
      if (_state == MovementState.WaitOffScreen)
      {
        var elapsed = (float)(Time.unscaledTimeAsDouble - _stateStartedAt);
        return Mathf.Lerp(0.82f, 0.08f, Mathf.Clamp01(elapsed / 0.55f));
      }

      if (_state == MovementState.PausedByTracking || _state == MovementState.PausedByUser)
      {
        return 0.22f;
      }

      return 0.96f;
    }

    private void DrawPauseControl()
    {
      var label = _state == MovementState.PausedByUser ? ">" : "II";
      if (GUI.Button(new Rect(Screen.width - 42f, 10f, 32f, 28f), label))
      {
        ToggleUserPause();
      }
    }

    private void DrawTechnicalStatus()
    {
      var status = _state == MovementState.PausedByUser ? "PAUSED" : _technicalStatus;
      if (string.IsNullOrWhiteSpace(status))
      {
        return;
      }

      var style = new GUIStyle(GUI.skin.label)
      {
        alignment = TextAnchor.MiddleCenter,
        fontSize = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(Screen.width, Screen.height) * 0.024f), 14, 22),
        normal = { textColor = WarmWhite },
      };
      GUI.Label(new Rect(12f, Screen.height * 0.72f, Screen.width - 24f, 40f), status, style);
    }

    private void DrawDebugHud()
    {
      var snapshot = EyeInputDebugState.Latest;
      var raw = _hasLatestSample ? _latestSample.RawGazeDegrees : Vector2.zero;
      var centered = _hasLatestSample ? _latestSample.CenteredGazeDegrees : Vector2.zero;
      var content =
        $"OFF-SCREEN EYE BREAK\n" +
        $"Eye Break State: {_state}\n" +
        $"Current Direction: {CurrentDirection}\n" +
        $"Raw Gaze Yaw/Pitch: {raw.x:0.0} / {raw.y:0.0}\n" +
        $"Centered Gaze H/V: {centered.x:0.0} / {centered.y:0.0}\n" +
        $"Head Yaw/Pitch: {snapshot.HeadYawDegrees:0.0} / {snapshot.HeadPitchDegrees:0.0}\n" +
        $"Face Tracked: {snapshot.FaceDetected}\n" +
        $"Gaze Valid: {_hasLatestSample}\n" +
        $"Direction Hold Progress: {_detector.DirectionHoldProgress:0.00}\n" +
        $"Return Center Progress: {_detector.ReturnCenterProgress:0.00}\n" +
        $"Tracking Lost: {_state == MovementState.PausedByTracking}\n" +
        $"Timeout Count: {_consecutiveTimeouts}\n" +
        $"Skip Available: {_skipAvailable}\n" +
        $"Blink Health Paused: {SoftFocusFieldController.Instance != null && SoftFocusFieldController.Instance.IsBlinkHealthPaused}";
      var style = new GUIStyle(GUI.skin.box)
      {
        alignment = TextAnchor.UpperLeft,
        fontSize = 12,
        normal = { textColor = WarmWhite },
      };
      GUI.Box(new Rect(8f, 8f, Mathf.Min(320f, Screen.width - 16f), 258f), content, style);
    }

    private void DrawCompletionRipple(Vector2 center, float eyeWidth)
    {
      var t = Mathf.Clamp01((float)(Time.unscaledTimeAsDouble - _stateStartedAt) / Mathf.Max(0.1f, _completionSeconds));
      var radius = Mathf.Lerp(eyeWidth * 0.18f, eyeWidth * 0.75f, t);
      DrawCircle(center, radius, new Color(MintGreen.r, MintGreen.g, MintGreen.b, (1f - t) * 0.72f), 3f, 48);
    }

    private static void DrawEyeOutline(Vector2 center, float width, float height, Color color, float thickness)
    {
      const int segments = 30;
      var left = center + Vector2.left * width * 0.5f;
      var previousTop = left;
      var previousBottom = left;
      for (var index = 1; index <= segments; index++)
      {
        var t = index / (float)segments;
        var x = Mathf.Lerp(-width * 0.5f, width * 0.5f, t);
        var arch = Mathf.Sin(t * Mathf.PI) * height * 0.5f;
        var top = center + new Vector2(x, -arch);
        var bottom = center + new Vector2(x, arch);
        DrawLine(previousTop, top, color, thickness);
        DrawLine(previousBottom, bottom, color, thickness);
        previousTop = top;
        previousBottom = bottom;
      }
    }

    private static void DrawDirectionArc(Vector2 center, float width, float height, OffScreenDirection direction, Color color)
    {
      var logicalDirection = DirectionVector(direction);
      var directionVector = new Vector2(logicalDirection.x, -logicalDirection.y);
      var perpendicular = new Vector2(-directionVector.y, directionVector.x);
      var anchor = center + directionVector * (width * 0.58f);
      var pulse = 0.92f + Mathf.Sin(Time.unscaledTime * 2.2f) * 0.08f;
      var span = Mathf.Min(width, height * 2f) * 0.20f * pulse;
      var start = anchor - perpendicular * span;
      var mid = anchor + directionVector * span * 0.38f;
      var end = anchor + perpendicular * span;
      DrawLine(start, mid, color, 3f);
      DrawLine(mid, end, color, 3f);
      DrawLine(mid, mid - directionVector * 10f + perpendicular * 7f, color, 3f);
      DrawLine(mid, mid - directionVector * 10f - perpendicular * 7f, color, 3f);
    }

    private static Vector2 DirectionVector(OffScreenDirection direction)
    {
      switch (direction)
      {
        case OffScreenDirection.Left: return Vector2.left;
        case OffScreenDirection.Right: return Vector2.right;
        case OffScreenDirection.Up: return Vector2.up;
        case OffScreenDirection.Down: return Vector2.down;
        default: return Vector2.zero;
      }
    }

    private static void DrawCircle(Vector2 center, float radius, Color color, float thickness, int segments)
    {
      var previous = center + Vector2.right * radius;
      for (var index = 1; index <= segments; index++)
      {
        var angle = index / (float)segments * Mathf.PI * 2f;
        var next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        DrawLine(previous, next, color, thickness);
        previous = next;
      }
    }

    private static void DrawLine(Vector2 start, Vector2 end, Color color, float thickness)
    {
      var previousMatrix = GUI.matrix;
      var delta = end - start;
      GUI.color = color;
      GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
      GUI.DrawTexture(new Rect(start.x, start.y - thickness * 0.5f, delta.magnitude, thickness), Texture2D.whiteTexture);
      GUI.matrix = previousMatrix;
      GUI.color = Color.white;
    }

    private static Texture2D CreateDiscTexture(int size)
    {
      var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
      {
        name = "Off-Screen Eye Pupil",
        wrapMode = TextureWrapMode.Clamp,
        filterMode = FilterMode.Bilinear,
        hideFlags = HideFlags.HideAndDontSave,
      };
      var pixels = new Color32[size * size];
      var center = (size - 1) * 0.5f;
      var radius = center - 1f;
      for (var y = 0; y < size; y++)
      {
        for (var x = 0; x < size; x++)
        {
          var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
          pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(radius - distance + 1f) * 255f));
        }
      }
      texture.SetPixels32(pixels);
      texture.Apply(false, true);
      return texture;
    }

    private static AudioClip CreateConfirmationClip()
    {
      const int sampleRate = 44100;
      const float duration = 0.16f;
      var samples = Mathf.CeilToInt(sampleRate * duration);
      var data = new float[samples];
      for (var index = 0; index < samples; index++)
      {
        var t = index / (float)sampleRate;
        var progress = t / duration;
        var envelope = Mathf.Sin(progress * Mathf.PI) * Mathf.Exp(-progress * 1.6f);
        var frequency = Mathf.Lerp(720f, 900f, progress);
        data[index] = Mathf.Sin(t * frequency * Mathf.PI * 2f) * envelope * 0.22f;
      }

      var clip = AudioClip.Create("Off-Screen Direction Confirm", samples, 1, sampleRate, false);
      clip.SetData(data, 0);
      return clip;
    }

    private static void TriggerLightHaptic()
    {
#if UNITY_IOS && !UNITY_EDITOR
      AudioServicesPlaySystemSound(1519);
#endif
    }

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void AudioServicesPlaySystemSound(uint systemSoundId);
#endif

    private void OnDestroy()
    {
      if (_gameplay != null)
      {
        _gameplay.FirstLevelModuleInstalled -= HandleModuleInstalled;
      }

      if (IsActive)
      {
        CompleteAndResume(false);
      }

      if (_discTexture != null) Destroy(_discTexture);
      if (_confirmationClip != null) Destroy(_confirmationClip);
      if (Instance == this) Instance = null;
    }
  }
}
