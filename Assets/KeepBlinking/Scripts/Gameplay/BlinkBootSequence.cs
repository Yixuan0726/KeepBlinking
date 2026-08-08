using KeepBlinking.Input;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  /// <summary>
  /// Passive, non-blocking open-eye calibration. The opening flow decides when
  /// collection may begin; this component never asks the player to blink.
  /// </summary>
  public sealed class BlinkBootSequence : MonoBehaviour
  {
    private enum PassiveState
    {
      Idle,
      WaitingForFace,
      Collecting,
      Monitoring,
    }

    [SerializeField, Min(0.05f)] private float _minimumOpenEyeForBaseline = 0.2f;
    [SerializeField, Min(0.1f)] private float _stableOpenSeconds = 0.55f;
    [SerializeField] private bool _hideLegacyDebugOverlayOnBoot = true;

    private PassiveState _state = PassiveState.Idle;
    private float _openEyeStartedAt = -1f;
    private float _baselineLeftEyeOpen = -1f;
    private float _baselineRightEyeOpen = -1f;

    public static BlinkBootSequence Instance { get; private set; }
    public bool HasOpenEyeBaseline => _state == PassiveState.Monitoring;
    public bool IsCalibrationRunning => _state == PassiveState.WaitingForFace || _state == PassiveState.Collecting;
    public float BaselineLeftEyeOpen => _baselineLeftEyeOpen;
    public float BaselineRightEyeOpen => _baselineRightEyeOpen;

    public static BlinkBootSequence EnsureExists()
    {
      if (Instance == null) Instance = FindFirstObjectByType<BlinkBootSequence>();
      if (Instance != null) return Instance;

      var owner = new GameObject("Passive Blink Calibration");
      if (Application.isPlaying) DontDestroyOnLoad(owner);
      Instance = owner.AddComponent<BlinkBootSequence>();
      return Instance;
    }

    public static void BeginPassiveCalibration()
    {
      EnsureExists().BeginCalibration();
    }

    private void Awake()
    {
      if (Instance != null && Instance != this)
      {
        Destroy(gameObject);
        return;
      }
      Instance = this;
    }

    private void Start()
    {
      if (!_hideLegacyDebugOverlayOnBoot) return;
      var debugOverlay = FindFirstObjectByType<EyeInputDebugOverlay>();
      if (debugOverlay != null) debugOverlay.enabled = false;
    }

    private void BeginCalibration()
    {
      _state = PassiveState.WaitingForFace;
      _openEyeStartedAt = -1f;
      _baselineLeftEyeOpen = -1f;
      _baselineRightEyeOpen = -1f;
      Debug.Log("Passive Blink Calibration started after the fixed session distance baseline.", this);
    }

    private void Update()
    {
      if (_state == PassiveState.Idle || _state == PassiveState.Monitoring) return;

      var snapshot = EyeInputDebugState.Latest;
      if (!snapshot.FaceDetected)
      {
        _state = PassiveState.WaitingForFace;
        _openEyeStartedAt = -1f;
        return;
      }

      var eyesOpen = (snapshot.LeftEyeOpen + snapshot.RightEyeOpen) * 0.5f >= _minimumOpenEyeForBaseline &&
                     !snapshot.IsBlinking;
      if (!eyesOpen)
      {
        _state = PassiveState.Collecting;
        _openEyeStartedAt = -1f;
        return;
      }

      _state = PassiveState.Collecting;
      _baselineLeftEyeOpen = Mathf.Max(_baselineLeftEyeOpen, snapshot.LeftEyeOpen);
      _baselineRightEyeOpen = Mathf.Max(_baselineRightEyeOpen, snapshot.RightEyeOpen);
      if (_openEyeStartedAt < 0f) _openEyeStartedAt = Time.unscaledTime;
      if (Time.unscaledTime - _openEyeStartedAt < _stableOpenSeconds) return;

      _state = PassiveState.Monitoring;
      Debug.Log(
        $"Passive Blink Calibration ready. L={_baselineLeftEyeOpen:F3}, R={_baselineRightEyeOpen:F3}.",
        this);
    }

    private void OnDestroy()
    {
      if (Instance == this) Instance = null;
    }
  }
}
