using KeepBlinking.Input;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  public class BlinkBootSequence : MonoBehaviour
  {
    private enum BootState
    {
      WaitingForFace,
      WaitingForOpenEyes,
      WaitingForBlink,
      Connected,
    }

    [SerializeField] private float _minimumOpenEyeForBaseline = 0.2f;
    [SerializeField] private float _stableOpenSeconds = 0.55f;
    [SerializeField] private float _relativeBlinkCloseRatio = 0.68f;
    [SerializeField] private float _singleEyeBlinkCloseRatio = 0.55f;
    [SerializeField] private float _connectedMessageSeconds = 2.5f;
    [SerializeField] private bool _hideLegacyDebugOverlayOnBoot = true;

    private BootState _state = BootState.WaitingForFace;
    private GUIStyle _terminalStyle;
    private GUIStyle _titleStyle;
    private GUIStyle _mutedStyle;
    private GUIStyle _successStyle;
    private int _observedBlinkCount;
    private float _openEyeStartedAt = -1f;
    private float _connectedAt = -1f;
    private bool _bootOverlayVisible = true;
    private float _baselineLeftEyeOpen = -1f;
    private float _baselineRightEyeOpen = -1f;

    public static void EnsureExists()
    {
      if (FindFirstObjectByType<BlinkBootSequence>() != null)
      {
        return;
      }

      var boot = new GameObject("KeepBlinking Blink Boot Sequence");
      DontDestroyOnLoad(boot);
      boot.AddComponent<BlinkBootSequence>();
    }

    private void Start()
    {
      if (_hideLegacyDebugOverlayOnBoot)
      {
        var debugOverlay = FindFirstObjectByType<EyeInputDebugOverlay>();
        if (debugOverlay != null)
        {
          debugOverlay.enabled = false;
        }
      }
    }

    private void Update()
    {
      var snapshot = EyeInputDebugState.Latest;
      switch (_state)
      {
        case BootState.WaitingForFace:
          if (snapshot.FaceDetected)
          {
            _state = BootState.WaitingForOpenEyes;
            ResetOpenEyeCalibration();
            _observedBlinkCount = snapshot.BlinkCount;
          }
          break;
        case BootState.WaitingForOpenEyes:
          if (!snapshot.FaceDetected)
          {
            _state = BootState.WaitingForFace;
            ResetOpenEyeCalibration();
            break;
          }

          if (EyesAreOpen(snapshot))
          {
            UpdateOpenEyeBaseline(snapshot);

            if (_openEyeStartedAt < 0f)
            {
              _openEyeStartedAt = Time.time;
            }

            if (Time.time - _openEyeStartedAt >= _stableOpenSeconds)
            {
              _state = BootState.WaitingForBlink;
              _observedBlinkCount = snapshot.BlinkCount;
            }
          }
          else
          {
            ResetOpenEyeCalibration();
          }
          break;
        case BootState.WaitingForBlink:
          if (!snapshot.FaceDetected)
          {
            _state = BootState.WaitingForFace;
            ResetOpenEyeCalibration();
            break;
          }

          if (snapshot.BlinkCount > _observedBlinkCount || IsRelativeBlink(snapshot))
          {
            _state = BootState.Connected;
            _connectedAt = Time.time;
          }
          break;
      }
    }

    private void OnGUI()
    {
      if (!_bootOverlayVisible)
      {
        return;
      }

      EnsureStyles();

      var snapshot = EyeInputDebugState.Latest;
      var width = Mathf.Min(760f, Screen.width - 48f);
      var height = 280f;
      var rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

      DrawBackdrop();
      GUILayout.BeginArea(rect, GUI.skin.box);
      GUILayout.Label("CLINICAL OBSERVATION PROTOCOL // DAY 01 / 21", _mutedStyle);
      GUILayout.Space(12f);
      GUILayout.Label("KEEP BLINKING", _titleStyle);
      GUILayout.Space(18f);

      switch (_state)
      {
        case BootState.WaitingForFace:
          GUILayout.Label("> OBSERVATION SYSTEM READY", _terminalStyle);
          GUILayout.Label("> FACE INPUT: NOT DETECTED", _terminalStyle);
          GUILayout.Space(10f);
          GUILayout.Label("Please sit comfortably and keep your full face in the camera frame.", _mutedStyle);
          break;
        case BootState.WaitingForOpenEyes:
          GUILayout.Label("> FACE INPUT: DETECTED", _terminalStyle);
          GUILayout.Label("> CALIBRATING YOUR OPEN-EYE BASELINE...", _terminalStyle);
          GUILayout.Space(10f);
          GUILayout.Label($"Glasses are okay. Keep eyes naturally open. L {snapshot.LeftEyeOpen:F2} / R {snapshot.RightEyeOpen:F2}", _mutedStyle);
          break;
        case BootState.WaitingForBlink:
          GUILayout.Label("> OBSERVATION SYSTEM READY", _terminalStyle);
          GUILayout.Label("> Please blink gently to establish connection.", _terminalStyle);
          GUILayout.Space(10f);
          GUILayout.Label("This trains the first reflex: blink before entering a screen session.", _mutedStyle);
          GUILayout.Label($"Eye signal L {snapshot.LeftEyeOpen:F2}/{_baselineLeftEyeOpen:F2}  R {snapshot.RightEyeOpen:F2}/{_baselineRightEyeOpen:F2}", _mutedStyle);
          break;
        case BootState.Connected:
          GUILayout.Label("> CONNECTION ESTABLISHED", _successStyle);
          GUILayout.Label("> Blink reflex sample accepted.", _terminalStyle);
          GUILayout.Space(10f);
          GUILayout.Label("System boot complete. Next prototype step: basic clean loop.", _mutedStyle);
          if (Time.time - _connectedAt >= _connectedMessageSeconds)
          {
            _bootOverlayVisible = false;
          }
          break;
      }

      GUILayout.Space(18f);
      DrawProgressLine();
      GUILayout.EndArea();
    }

    private bool EyesAreOpen(EyeInputDebugSnapshot snapshot)
    {
      return AverageEyeOpen(snapshot) >= _minimumOpenEyeForBaseline;
    }

    private void ResetOpenEyeCalibration()
    {
      _openEyeStartedAt = -1f;
      _baselineLeftEyeOpen = -1f;
      _baselineRightEyeOpen = -1f;
    }

    private void UpdateOpenEyeBaseline(EyeInputDebugSnapshot snapshot)
    {
      _baselineLeftEyeOpen = Mathf.Max(_baselineLeftEyeOpen, snapshot.LeftEyeOpen);
      _baselineRightEyeOpen = Mathf.Max(_baselineRightEyeOpen, snapshot.RightEyeOpen);
    }

    private bool IsRelativeBlink(EyeInputDebugSnapshot snapshot)
    {
      if (_baselineLeftEyeOpen <= 0f || _baselineRightEyeOpen <= 0f)
      {
        return false;
      }

      var currentAverage = AverageEyeOpen(snapshot);
      var baselineAverage = (_baselineLeftEyeOpen + _baselineRightEyeOpen) * 0.5f;
      var averageClosedEnough = currentAverage <= baselineAverage * _relativeBlinkCloseRatio;
      var leftClosedEnough = snapshot.LeftEyeOpen <= _baselineLeftEyeOpen * _singleEyeBlinkCloseRatio;
      var rightClosedEnough = snapshot.RightEyeOpen <= _baselineRightEyeOpen * _singleEyeBlinkCloseRatio;
      return averageClosedEnough || (leftClosedEnough && rightClosedEnough);
    }

    private float AverageEyeOpen(EyeInputDebugSnapshot snapshot)
    {
      return (snapshot.LeftEyeOpen + snapshot.RightEyeOpen) * 0.5f;
    }

    private void DrawBackdrop()
    {
      var previousColor = GUI.color;
      GUI.color = new Color(0.02f, 0.05f, 0.04f, 0.92f);
      GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
      GUI.color = previousColor;
    }

    private void DrawProgressLine()
    {
      var connected = _state == BootState.Connected;
      var elapsed = connected ? Mathf.Clamp01((Time.time - _connectedAt) / _connectedMessageSeconds) : 0f;
      var bars = connected ? Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(1f, 21f, elapsed))) : 1;
      var filled = new string('#', bars);
      var empty = new string('-', 21 - bars);
      GUILayout.Label($"[{filled}{empty}] SYSTEM BOOT", connected ? _successStyle : _mutedStyle);
    }

    private void EnsureStyles()
    {
      if (_terminalStyle != null)
      {
        return;
      }

      _terminalStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = 24,
        fontStyle = FontStyle.Normal,
        wordWrap = true,
        normal = { textColor = new Color(0.58f, 1f, 0.72f) },
      };

      _titleStyle = new GUIStyle(_terminalStyle)
      {
        fontSize = 42,
        fontStyle = FontStyle.Bold,
        normal = { textColor = new Color(0.78f, 1f, 0.84f) },
      };

      _mutedStyle = new GUIStyle(_terminalStyle)
      {
        fontSize = 18,
        normal = { textColor = new Color(0.42f, 0.72f, 0.52f) },
      };

      _successStyle = new GUIStyle(_terminalStyle)
      {
        fontSize = 26,
        fontStyle = FontStyle.Bold,
        normal = { textColor = new Color(0.82f, 1f, 0.62f) },
      };
    }
  }
}
