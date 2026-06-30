// Attach this script to an Empty GameObject.
// The script automatically reads KeepBlinking's MediaPipe bridge when available.
// External eye-tracking SDKs can still write realGazeScreenPosition and call TriggerHardwareBlink().
// No mouse, keyboard, Rigidbody2D, prefab, UI, or external art asset is required.

using System.Collections;
using System.Collections.Generic;
using KeepBlinking.Input;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  public class EdgeOrbitHarvestMvp : MonoBehaviour
  {
    private enum BlockState
    {
      Orbiting,
      Crisis,
      FadingOut,
      Converted,
      Collecting,
    }

    private enum GameplayState
    {
      Orbiting,
      Crisis,
      EyesClosedFreeze,
      ModuleUpgrade,
      SessionReport,
    }

    [Header("Hardware Input Hooks")]
    public Vector2 realGazeScreenPosition;
    public bool isEyesClosed;
    public float faceDistance = 1.0f;

    [SerializeField] private bool _autoReadKeepBlinkingEyeInput = true;
    [SerializeField] private float _hardwareTimeoutSeconds = 1.2f;
    [SerializeField] private float _gazeSmoothSpeed = 9f;
    [SerializeField] private float _minimumOpenEyeForBlinkBaseline = 0.2f;
    [SerializeField] private float _relativeBlinkCloseRatio = 0.72f;
    [SerializeField] private float _blinkCooldownSeconds = 0.35f;
    [SerializeField] private float _preBlinkSampleWindowSeconds = 0.3f;
    [SerializeField] private float _preBlinkIgnoreRecentSeconds = 0.04f;

    [Header("Camera")]
    [SerializeField] private Camera _camera;
    [SerializeField] private bool _setupOrthographicCamera = true;
    [SerializeField] private bool _forcePortraitProjection = true;
    [SerializeField] private float _portraitAspect = 9f / 16f;
    [SerializeField] private float _orthographicSize = 8f;
    [SerializeField] private float _blockDepthFromCamera = 10f;

    [Header("Orbit Spawn")]
    [SerializeField] private float _minSpawnIntervalSeconds = 2f;
    [SerializeField] private float _maxSpawnIntervalSeconds = 3f;
    [SerializeField] private int _maxOrbitingBlocks = 4;
    [SerializeField] private float _edgeInsetViewport = 0.09f;
    [SerializeField] private Vector2 _blockWorldSizeRange = new Vector2(0.58f, 0.82f);
    [SerializeField] private Vector2 _orbitAngularSpeedRange = new Vector2(0.07f, 0.13f);

    [Header("Crisis & Eye Close Break")]
    [SerializeField] private float _minCrisisIntervalSeconds = 15f;
    [SerializeField] private float _maxCrisisIntervalSeconds = 20f;
    [SerializeField] private int _crisisSpawnCount = 8;
    [SerializeField] private float _crisisSpawnPaddingViewport = 0.16f;
    [SerializeField] private Vector2 _crisisBlockWorldSizeRange = new Vector2(0.55f, 0.78f);
    [SerializeField] private Vector2 _crisisMoveSpeedRange = new Vector2(0.75f, 1.1f);
    [SerializeField] private float _purificationRadiusGrowthSpeed = 3.1f;
    [SerializeField] private float _purificationVisualAlpha = 0.34f;
    [SerializeField] private int _purificationCircleTextureSize = 128;
    [SerializeField] private float _blackoutAlpha = 0.82f;
    [SerializeField] private float _closedEyeLostFaceGraceSeconds = 5f;
    [SerializeField] private float _openEyeReleaseThreshold = 0.55f;
    [SerializeField] private float _gentleCloseAbsoluteThreshold = 0.38f;
    [SerializeField] private float _gentleCloseRelativeRatio = 0.68f;
    [SerializeField] private float _gentleCloseMinimumDrop = 0.16f;
    [SerializeField] private float _relativeOpenReleaseRatio = 0.94f;
    [SerializeField] private float _closedEyeEnterHoldSeconds = 0.16f;
    [SerializeField] private bool _treatLostFaceAsClosedDuringCrisis = true;

    [Header("Gaze & Feedback")]
    [SerializeField] private float _gazePaddingPixels = 88f;
    [SerializeField] private float _hoverGraceSeconds = 0.45f;
    [SerializeField] private float _colorLerpSpeed = 8f;
    [SerializeField] private float _scaleLerpSpeed = 8f;
    [SerializeField] private float _fadeOutSeconds = 0.9f;
    [SerializeField] private float _harvestSeconds = 0.75f;
    [SerializeField] private float _harvestScaleRatio = 0.3f;
    [SerializeField] private float _gazeIndicatorWorldSize = 0.55f;
    [SerializeField] private bool _useEdgeDirectionSoftLock = true;
    [SerializeField] private float _softLockMaxAngleDegrees = 68f;
    [SerializeField] private float _softLockMinGazeDistancePixels = 55f;
    [SerializeField] private float _sideIntentDeadZonePixels = 70f;
    [SerializeField] private float _sideIntentScoreBonus = 26f;
    [SerializeField] private bool _preferSameSideWhenIntentIsClear = true;
    [SerializeField] private float _leftIntentDeadZonePixels = 52f;
    [SerializeField] private float _leftSoftLockExtraAngleDegrees = 8f;

    [Header("Startup Gaze Calibration")]
    [SerializeField] private bool _runStartupCalibration = true;
    [SerializeField] private float _calibrationTargetWorldSize = 0.62f;
    [SerializeField] private float _calibrationEdgePaddingViewport = 0.22f;
    [SerializeField] private float _calibrationMaxScale = 3.5f;
    [SerializeField] private float _calibrationMinScale = 0.25f;

    [Header("Freeze Test Feedback")]
    [SerializeField] private bool _playFreezeFeedbackAudio = true;
    [SerializeField] private float _freezeFeedbackVolume = 0.22f;

    [Header("Debug HUD")]
    [SerializeField] private bool _showDebugHud;

    [Header("MVP Presentation")]
    [SerializeField] private bool _showOpeningGuide = true;
    [SerializeField] private float _openingGuideSeconds = 30f;
    [SerializeField] private bool _enableSessionReportTimer = true;
    [SerializeField] private float _sessionDurationSeconds = 180f;
    [SerializeField] private bool _allowDemoKeyboardShortcuts = true;

    [Header("Sampling & Module Upgrade")]
    [SerializeField] private float _pushAwayDistanceThreshold = 0.6f;
    [SerializeField] private float _pushAwayRelativeThreshold = 0.72f;
    [SerializeField] private float _pushAwayAbsoluteDrop = 0.18f;
    [SerializeField] private float _pushAwayHoldSeconds = 0.25f;
    [SerializeField] private float _pushAwayReadyRelativeThreshold = 0.9f;
    [SerializeField] private float _pushAwayReadyHoldSeconds = 0.35f;
    [SerializeField] private float _faceDistanceSmoothSpeed = 8f;
    [SerializeField] private float _sampleCollectSpeed = 9f;
    [SerializeField] private float _sampleCollectDistance = 0.18f;
    [SerializeField] private int _samplesNeededForUpgrade = 10;
    [SerializeField] private float _progressBarWidthViewport = 0.72f;
    [SerializeField] private float _progressBarHeightWorld = 0.24f;
    [SerializeField] private float _progressBarBottomViewport = 0.06f;
    [SerializeField] private Vector2 _moduleCardSize = new Vector2(2.25f, 3.0f);
    [SerializeField] private float _moduleCardSpacing = 0.38f;

    [Header("Center Player Marker")]
    [SerializeField] private float _playerMarkerWorldSize = 0.72f;
    [SerializeField] private float _playerMarkerPulseSpeed = 1.4f;

    private readonly List<OrbitBlock> _blocks = new List<OrbitBlock>();
    private readonly List<ModuleCard> _moduleCards = new List<ModuleCard>();
    private readonly List<SpriteRenderer> _playerMarkerPieces = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> _gazeIndicatorPieces = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> _calibrationTargetPieces = new List<SpriteRenderer>();
    private readonly List<Vector2> _calibrationRawSamples = new List<Vector2>();
    private readonly List<Vector2> _calibrationTargetSamples = new List<Vector2>();
    private readonly List<TimedGazeSample> _recentRawGazeSamples = new List<TimedGazeSample>();
    private Sprite _squareSprite;
    private Sprite _circleSprite;
    private Texture2D _squareTexture;
    private Texture2D _circleTexture;
    private GameObject _gazeIndicatorRoot;
    private GameObject _playerMarkerRoot;
    private GameObject _calibrationTargetRoot;
    private GameObject _blackoutRoot;
    private SpriteRenderer _blackoutRenderer;
    private GameObject _purificationWaveRoot;
    private SpriteRenderer _purificationWaveRenderer;
    private GameObject _progressBarRoot;
    private SpriteRenderer _progressBarBackRenderer;
    private SpriteRenderer _progressBarFillRenderer;
    private AudioSource _feedbackAudioSource;
    private AudioClip _freezeStartedClip;
    private AudioClip _coverageCompleteClip;
    private AudioClip _freezeFailedClip;
    private AudioClip _freezeClearedClip;
    private Vector2[] _calibrationTargets;
    private Vector2 _rawGazeScreenPosition;
    private Vector2 _calibrationScale = Vector2.one;
    private Vector2 _calibrationOffset = Vector2.zero;
    private bool _calibrationActive;
    private bool _calibrationComplete;
    private int _calibrationIndex;
    private bool _blinkQueued;
    private bool _hardwareWarningLogged;
    private int _lastObservedBlinkCount = -1;
    private float _baselineLeftEyeOpen = -1f;
    private float _baselineRightEyeOpen = -1f;
    private float _lastBlinkAcceptedAt = -999f;
    private float _lastBlinkVisualAt = -999f;
    private bool _lastRelativeBlinking;
    private float _nextSpawnAt;
    private float _nextCrisisAt;
    private float _eyesClosedStartedAt = -1f;
    private float _lastFaceDetectedAt = -999f;
    private float _lastEyesClosedSignalAt = -999f;
    private float _eyesClosedCandidateStartedAt = -1f;
    private float _lastFreezeResultAt = -999f;
    private float _lastFreezeDuration;
    private float _purificationRadius;
    private string _lastFreezeResult = "none";
    private bool _wasEyesClosed;
    private bool _coverageCuePlayed;
    private GameplayState _gameplayState = GameplayState.Orbiting;
    private GameplayState _resumeStateAfterUpgrade = GameplayState.Orbiting;
    private int _spawnSerial;
    private int _harvestedCount;
    private int _collectedSampleCount;
    private float _sampleProgress;
    private float _smoothedFaceDistance = 1f;
    private float _faceDistanceBaseline = -1f;
    private float _pushAwayCandidateStartedAt = -1f;
    private float _pushAwayReadyCandidateStartedAt = -1f;
    private bool _pushAwayWasActive;
    private bool _pushAwayReady;
    private OrbitBlock _hoveredBlock;
    private OrbitBlock _lastHoveredBlock;
    private float _lastHoveredAt = -999f;
    private bool _hoverUsesSoftLock;
    private float _lastSoftLockAngle;
    private float _lastHorizontalIntent;
    private GUIStyle _hudStyle;
    private GUIStyle _instructionTitleStyle;
    private GUIStyle _instructionBodyStyle;
    private GUIStyle _warningTitleStyle;
    private GUIStyle _warningBodyStyle;
    private GUIStyle _tutorialTitleStyle;
    private GUIStyle _tutorialBodyStyle;
    private GUIStyle _reportTitleStyle;
    private GUIStyle _reportBodyStyle;
    private GUIStyle _reportMetricStyle;
    private float _sessionStartedAt = -1f;
    private float _openingGuideStartedAt = -1f;
    private bool _sessionEnded;
    private int _sessionBlinkCount;
    private int _blinkCaptureCount;
    private int _eyeRestBreakCount;
    private int _distanceSwitchCount;
    private int _moduleChoiceCount;
    private int _totalSamplesCollected;
    private int _crisisClearCount;

    private static readonly Color OrbitColor = new Color(0.58f, 0.08f, 0.06f, 1f);
    private static readonly Color HoverColor = new Color(1f, 0.48f, 0.16f, 1f);
    private static readonly Color ConvertedColor = new Color(0.05f, 0.32f, 0.16f, 0.72f);
    private static readonly Color GazeIdleColor = new Color(0.55f, 1f, 0.76f, 0.62f);
    private static readonly Color GazeHoverColor = new Color(1f, 0.74f, 0.28f, 0.88f);
    private static readonly Color CalibrationColor = new Color(0.78f, 1f, 0.66f, 0.9f);
    private static readonly Color CrisisColor = new Color(0.82f, 0.04f, 0.04f, 1f);
    private static readonly Color ProgressBackColor = new Color(0.02f, 0.12f, 0.09f, 0.78f);
    private static readonly Color ProgressFillColor = new Color(0.22f, 0.9f, 0.55f, 0.88f);
    private static readonly Color[] ModuleCardColors =
    {
      new Color(0.95f, 0.32f, 0.22f, 0.94f),
      new Color(0.18f, 0.58f, 1f, 0.94f),
      new Color(0.95f, 0.78f, 0.24f, 0.94f),
    };

    public static void EnsureExists()
    {
      if (FindFirstObjectByType<EdgeOrbitHarvestMvp>() != null)
      {
        return;
      }

      var observer = new GameObject("Edge Orbit Harvest MVP");
      observer.AddComponent<EdgeOrbitHarvestMvp>();
    }

    public void TriggerHardwareBlink()
    {
      _blinkQueued = true;
      _lastBlinkVisualAt = Time.time;
      _sessionBlinkCount++;
    }

    private void Start()
    {
      EnsureCamera();
      CreateRuntimeSprite();
      CreateRuntimeCircleSprite();
      CreatePlayerMarker();
      CreateCalibrationTarget();
      CreateBlackoutOverlay();
      CreatePurificationWave();
      CreateProgressBar();
      CreateFreezeFeedbackAudio();
      realGazeScreenPosition = GetSafeInitialGazePosition();
      _rawGazeScreenPosition = realGazeScreenPosition;
      SetupCalibration();
      WarnIfEyeHardwareMissing();
      ScheduleNextSpawn(0.15f);
      ScheduleNextCrisis();
      _sessionStartedAt = Time.time;
      _openingGuideStartedAt = Time.time;
    }

    private void Update()
    {
      if (UnityEngine.Input.GetKeyDown(KeyCode.F1))
      {
        _showDebugHud = !_showDebugHud;
      }

      if (_allowDemoKeyboardShortcuts && UnityEngine.Input.GetKeyDown(KeyCode.F2))
      {
        EndSessionAndShowReport();
      }

      UpdateEyeInputFromPlugin();
      if (_gameplayState == GameplayState.SessionReport)
      {
        UpdatePlayerMarker();
        UpdateBlackoutOverlay();
        return;
      }

      if (UpdateCalibration())
      {
        UpdatePlayerMarker();
        UpdateGazeIndicator();
        return;
      }

      RemoveDeadBlocks();
      UpdateFaceDistanceFromPlugin();
      UpdateProgressBarVisual();
      if (_gameplayState == GameplayState.ModuleUpgrade)
      {
        UpdateModuleUpgradeSelection();
        UpdatePlayerMarker();
        UpdateGazeIndicator();
        return;
      }

      UpdateGameplayState();
      UpdateHoverState();
      UpdateBlocksByGameplayState();
      UpdateSampleCollection();
      ConsumeBlinkForHarvest();
      UpdatePlayerMarker();
      UpdateGazeIndicator();
      UpdateBlackoutOverlay();
      UpdateSessionTimer();
    }

    private void OnGUI()
    {
      EnsureHudStyle();
      DrawHardwareWarningOverlay();
      DrawOpeningGuideOverlay();
      DrawInstructionOverlay();
      DrawSessionReportOverlay();

      if (!_showDebugHud)
      {
        return;
      }

      GUILayout.BeginArea(new Rect(18f, 18f, Mathf.Min(760f, Screen.width - 36f), 280f));
      GUILayout.Label("Edge Orbit & Harvest MVP // Hardware Eye Input", _hudStyle);
      GUILayout.Label($"Gaze marker: {realGazeScreenPosition.x:F0}, {realGazeScreenPosition.y:F0}   Raw: {_rawGazeScreenPosition.x:F0}, {_rawGazeScreenPosition.y:F0}", _hudStyle);
      GUILayout.Label(GetHardwareStatusLine(), _hudStyle);
      GUILayout.Label(GetEyeClosedStatusLine(), _hudStyle);
      if (_calibrationActive)
      {
        GUILayout.Label($"Calibration: {_calibrationIndex + 1} / {_calibrationTargets.Length}   Look at the soft target, then blink gently.", _hudStyle);
      }
      else
      {
        var lockMode = _hoveredBlock == null ? "none" : (_hoverUsesSoftLock ? $"soft angle {_lastSoftLockAngle:F0}" : "direct");
        GUILayout.Label(GetGameplayStatusLine(), _hudStyle);
        GUILayout.Label(GetFreezeResultStatusLine(), _hudStyle);
        var pushCandidate = _pushAwayCandidateStartedAt < 0f ? "--" : $"{Mathf.Max(0f, Time.time - _pushAwayCandidateStartedAt):F2}s";
        var readyCandidate = _pushAwayReadyCandidateStartedAt < 0f ? "--" : $"{Mathf.Max(0f, Time.time - _pushAwayReadyCandidateStartedAt):F2}s";
        var faceBase = _faceDistanceBaseline < 0f ? "--" : _faceDistanceBaseline.ToString("F2");
        GUILayout.Label($"Face distance: {_smoothedFaceDistance:F2} / base {faceBase}   Push ready {_pushAwayReady} ({readyCandidate})   Push candidate {pushCandidate}   Sample bar: {_collectedSampleCount}/{_samplesNeededForUpgrade} ({_sampleProgress:P0})", _hudStyle);
        GUILayout.Label($"Hover: {(_hoveredBlock == null ? "none" : _hoveredBlock.Name)}   Lock: {lockMode}   Intent: {FormatHorizontalIntent(_lastHorizontalIntent)}", _hudStyle);
        GUILayout.Label($"Orbiting: {CountState(BlockState.Orbiting)} / {_maxOrbitingBlocks}   Crisis: {CountState(BlockState.Crisis)}   Converted: {_harvestedCount}", _hudStyle);
        GUILayout.Label("Look roughly toward a red edge block. Once it turns orange, blink gently to convert it.", _hudStyle);
      }
      GUILayout.EndArea();
    }

    private void WarnIfEyeHardwareMissing()
    {
      if (!_autoReadKeepBlinkingEyeInput)
      {
        return;
      }

      var snapshot = EyeInputDebugState.Latest;
      if (!snapshot.FaceDetected)
      {
        Debug.LogWarning("Eye hardware not detected. Please check the MediaPipe connection.");
        _hardwareWarningLogged = true;
      }
    }

    private string GetHardwareStatusLine()
    {
      var snapshot = EyeInputDebugState.Latest;
      if (!_autoReadKeepBlinkingEyeInput)
      {
        return "Eye input: external SDK feed";
      }

      if (!snapshot.FaceDetected)
      {
        return "Eye input: waiting for MediaPipe face landmarks";
      }

      return $"Eye input: MediaPipe active  L {snapshot.LeftEyeOpen:F2}  R {snapshot.RightEyeOpen:F2}  Blinks {snapshot.BlinkCount}";
    }

    private void DrawInstructionOverlay()
    {
      if (_gameplayState == GameplayState.SessionReport)
      {
        return;
      }

      EnsureInstructionStyles();
      DrawModuleCardLabels();
      var title = GetInstructionTitle();
      var body = GetInstructionBody();

      var width = Mathf.Min(Screen.width - 56f, 760f);
      var height = 94f;
      var x = (Screen.width - width) * 0.5f;
      var y = Screen.height - height - 76f;
      var rect = new Rect(x, y, width, height);

      GUI.color = new Color(0f, 0f, 0f, 0.48f);
      GUI.Box(rect, GUIContent.none);
      GUI.color = Color.white;
      GUI.Label(new Rect(rect.x + 24f, rect.y + 14f, rect.width - 48f, 34f), title, _instructionTitleStyle);
      GUI.Label(new Rect(rect.x + 24f, rect.y + 50f, rect.width - 48f, 34f), body, _instructionBodyStyle);
    }

    private void DrawHardwareWarningOverlay()
    {
      if (!ShouldShowHardwareWarningOverlay() || _gameplayState == GameplayState.SessionReport)
      {
        return;
      }

      EnsurePresentationStyles();
      var width = Mathf.Min(Screen.width - 48f, 760f);
      var height = 88f;
      var rect = new Rect((Screen.width - width) * 0.5f, 24f, width, height);

      GUI.color = new Color(0.08f, 0.03f, 0.02f, 0.72f);
      GUI.Box(rect, GUIContent.none);
      GUI.color = Color.white;
      GUI.Label(new Rect(rect.x + 18f, rect.y + 10f, rect.width - 36f, 30f), "Face Not Detected", _warningTitleStyle);
      GUI.Label(new Rect(rect.x + 18f, rect.y + 44f, rect.width - 36f, 34f), "Please adjust lighting, remove obstruction, or restart calibration.", _warningBodyStyle);
    }

    private void DrawOpeningGuideOverlay()
    {
      if (!_showOpeningGuide ||
          _openingGuideStartedAt < 0f ||
          _gameplayState == GameplayState.SessionReport ||
          Time.time - _openingGuideStartedAt > _openingGuideSeconds)
      {
        return;
      }

      EnsurePresentationStyles();
      var elapsed = Mathf.Max(0f, Time.time - _openingGuideStartedAt);
      var step = GetOpeningGuideStep(elapsed);
      var width = Mathf.Min(Screen.width - 56f, 760f);
      var height = 118f;
      var y = ShouldShowHardwareWarningOverlay() ? 124f : 30f;
      var rect = new Rect((Screen.width - width) * 0.5f, y, width, height);

      GUI.color = new Color(0f, 0.08f, 0.055f, 0.62f);
      GUI.Box(rect, GUIContent.none);
      GUI.color = Color.white;
      GUI.Label(new Rect(rect.x + 22f, rect.y + 12f, rect.width - 44f, 34f), step.Title, _tutorialTitleStyle);
      GUI.Label(new Rect(rect.x + 22f, rect.y + 52f, rect.width - 44f, 54f), step.Body, _tutorialBodyStyle);
    }

    private void DrawSessionReportOverlay()
    {
      if (_gameplayState != GameplayState.SessionReport)
      {
        return;
      }

      EnsurePresentationStyles();
      var width = Mathf.Min(Screen.width - 56f, 820f);
      var height = Mathf.Min(Screen.height - 80f, 520f);
      var rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

      GUI.color = new Color(0f, 0.05f, 0.035f, 0.88f);
      GUI.Box(rect, GUIContent.none);
      GUI.color = Color.white;

      GUI.Label(new Rect(rect.x + 28f, rect.y + 24f, rect.width - 56f, 48f), "Daily Observation Report", _reportTitleStyle);
      GUI.Label(new Rect(rect.x + 32f, rect.y + 84f, rect.width - 64f, 52f), "Session complete. The prototype recorded eye-care actions instead of score pressure.", _reportBodyStyle);

      var metricY = rect.y + 158f;
      DrawReportMetric(rect.x + 48f, metricY, "Blink signals", _sessionBlinkCount.ToString());
      DrawReportMetric(rect.x + rect.width * 0.5f, metricY, "Blink captures", _blinkCaptureCount.ToString());
      DrawReportMetric(rect.x + 48f, metricY + 70f, "Eye-rest breaks", _eyeRestBreakCount.ToString());
      DrawReportMetric(rect.x + rect.width * 0.5f, metricY + 70f, "Distance switches", _distanceSwitchCount.ToString());
      DrawReportMetric(rect.x + 48f, metricY + 140f, "Samples collected", _totalSamplesCollected.ToString());
      DrawReportMetric(rect.x + rect.width * 0.5f, metricY + 140f, "Modules selected", _moduleChoiceCount.ToString());

      GUI.Label(new Rect(rect.x + 32f, rect.y + height - 92f, rect.width - 64f, 58f), "Design intent: build a gentle screen-blink and distance-switch habit through a short, non-punitive play loop.", _reportBodyStyle);
    }

    private void DrawReportMetric(float x, float y, string label, string value)
    {
      GUI.Label(new Rect(x, y, 210f, 28f), label, _reportBodyStyle);
      GUI.Label(new Rect(x, y + 26f, 210f, 36f), value, _reportMetricStyle);
    }

    private void DrawModuleCardLabels()
    {
      if (_gameplayState != GameplayState.ModuleUpgrade || _moduleCards.Count == 0 || _camera == null)
      {
        return;
      }

      for (var i = 0; i < _moduleCards.Count; i++)
      {
        var card = _moduleCards[i];
        if (card.GameObject == null)
        {
          continue;
        }

        var screen = _camera.WorldToScreenPoint(card.GameObject.transform.position);
        var rect = new Rect(screen.x - 90f, Screen.height - screen.y - 28f, 180f, 56f);
        GUI.Label(rect, $"Module {card.Index + 1}", _instructionTitleStyle);
      }
    }

    private string GetInstructionTitle()
    {
      if (_calibrationActive)
      {
        return "Calibrate Your Gaze";
      }

      switch (_gameplayState)
      {
        case GameplayState.ModuleUpgrade:
          return "Choose One Module";
        case GameplayState.EyesClosedFreeze:
          return _coverageCuePlayed ? "Open Your Eyes Gently" : "Keep Your Eyes Softly Closed";
        case GameplayState.Crisis:
          return "Inward Drift Detected";
      }

      if (CountState(BlockState.Collecting) > 0)
      {
        return "Collecting Samples";
      }

      if (HasCollectableSamples())
      {
        return _pushAwayReady ? "Move the Phone Away" : "Return to Normal Distance";
      }

      if (_hoveredBlock != null)
      {
        return "Blink to Capture";
      }

      return "Find an Edge Signal";
    }

    private string GetInstructionBody()
    {
      if (_calibrationActive)
      {
        return "Look at the soft target, then blink gently to confirm.";
      }

      switch (_gameplayState)
      {
        case GameplayState.ModuleUpgrade:
          return "The test is paused. Click or tap one colored card to continue.";
        case GameplayState.EyesClosedFreeze:
          return _coverageCuePlayed
            ? "The cleansing circle has covered the targets. Open your eyes to release it."
            : $"The cleansing circle is expanding: {_purificationRadius:F1}. Wait for the soft cue if you can.";
        case GameplayState.Crisis:
          return "Close your eyes softly to freeze the field and expand the cleansing circle.";
      }

      if (CountState(BlockState.Collecting) > 0)
      {
        return "Green samples are being drawn into the reflection bar.";
      }

      if (HasCollectableSamples())
      {
        return _pushAwayReady
          ? "Move the phone clearly away to collect the green samples."
          : "Bring the phone back to your usual distance, then move it away to collect.";
      }

      if (_hoveredBlock != null)
      {
        return "When the target turns orange, blink gently to convert it into a green sample.";
      }

      return "Let your gaze drift toward an edge block. Blink gently when it responds.";
    }

    private bool ShouldShowHardwareWarningOverlay()
    {
      if (!_autoReadKeepBlinkingEyeInput)
      {
        return false;
      }

      var snapshot = EyeInputDebugState.Latest;
      return !snapshot.FaceDetected && Time.timeSinceLevelLoad > 1.2f;
    }

    private TutorialStep GetOpeningGuideStep(float elapsedSeconds)
    {
      if (elapsedSeconds < 6f)
      {
        return new TutorialStep(
          "1 / 5  Look at red signals",
          "Let your gaze move gently toward the red edge block. No hands needed.");
      }

      if (elapsedSeconds < 12f)
      {
        return new TutorialStep(
          "2 / 5  Blink to capture",
          "When a signal turns orange, blink softly. It becomes a green sample.");
      }

      if (elapsedSeconds < 18f)
      {
        return new TutorialStep(
          "3 / 5  Close eyes during inward drift",
          "When red blocks move inward, close your eyes to freeze the field and grow a cleansing circle.");
      }

      if (elapsedSeconds < 24f)
      {
        return new TutorialStep(
          "4 / 5  Move phone away",
          "After green samples appear, return to normal distance, then move the phone away to collect them.");
      }

      return new TutorialStep(
        "5 / 5  Choose a module",
        "When the bottom bar fills, click or tap one module card. This MVP validates the core eye-care loop.");
    }

    private string GetEyeClosedStatusLine()
    {
      var snapshot = EyeInputDebugState.Latest;
      var averageOpen = (snapshot.LeftEyeOpen + snapshot.RightEyeOpen) * 0.5f;
      var baseline = GetBlinkBaselineAverage();
      var baselineText = baseline < 0f ? "--" : baseline.ToString("F2");
      var candidateText = _eyesClosedCandidateStartedAt < 0f ? "--" : $"{Mathf.Max(0f, Time.time - _eyesClosedCandidateStartedAt):F2}s";
      return $"Eyes closed: {isEyesClosed}   Candidate {candidateText}   Eye open avg {averageOpen:F2} / base {baselineText}   Last face: {Mathf.Max(0f, Time.time - _lastFaceDetectedAt):F1}s ago";
    }

    private string GetFreezeResultStatusLine()
    {
      if (_gameplayState == GameplayState.EyesClosedFreeze)
      {
        var closedSeconds = Mathf.Max(0f, Time.time - _eyesClosedStartedAt);
        var farthest = GetFarthestCrisisDistanceFromCenter();
        var ready = _coverageCuePlayed ? "COVERED" : "expanding";
        return $"Freeze check: ACTIVE {closedSeconds:F1}s radius {_purificationRadius:F1}/{farthest:F1} {ready}";
      }

      if (Time.time - _lastFreezeResultAt <= 8f)
      {
        return $"Freeze check: last {_lastFreezeResult} ({_lastFreezeDuration:F1}s)";
      }

      return "Freeze check: close eyes during inward crisis to expand the purification circle";
    }

    private string GetGameplayStatusLine()
    {
      switch (_gameplayState)
      {
        case GameplayState.Orbiting:
          return $"State: orbiting   Next crisis in {Mathf.Max(0f, _nextCrisisAt - Time.time):F1}s";
        case GameplayState.Crisis:
          return "State: inward crisis   Close eyes to freeze and expand the cleansing circle";
        case GameplayState.EyesClosedFreeze:
          return $"State: eyes closed freeze   Radius {_purificationRadius:F1}";
        case GameplayState.ModuleUpgrade:
          return "State: module upgrade   Click one card to resume";
        default:
          return $"State: {_gameplayState}";
      }
    }

    private void UpdateEyeInputFromPlugin()
    {
      if (!_autoReadKeepBlinkingEyeInput)
      {
        return;
      }

      var snapshot = EyeInputDebugState.Latest;
      if (!snapshot.FaceDetected)
      {
        if (!_hardwareWarningLogged)
        {
          Debug.LogWarning("Eye hardware not detected. Please check the MediaPipe connection.");
          _hardwareWarningLogged = true;
        }

        TryTreatLostFaceAsClosedDuringCrisis();
        MaintainClosedEyesDuringLostFace();
        return;
      }

      _lastFaceDetectedAt = Time.time;
      UpdateBlinkBaseline(snapshot);
      UpdateEyesClosedState(snapshot);

      if (snapshot.HasGazeScreenPosition)
      {
        _rawGazeScreenPosition = Vector2.Lerp(
          _rawGazeScreenPosition,
          snapshot.GazeScreenPosition,
          1f - Mathf.Exp(-_gazeSmoothSpeed * Time.deltaTime));
        AddRecentRawGazeSample(_rawGazeScreenPosition);
        realGazeScreenPosition = ApplyGazeCalibration(_rawGazeScreenPosition);
      }

      if (ConsumePluginBlink(snapshot))
      {
        TriggerHardwareBlink();
      }
    }

    private void UpdateFaceDistanceFromPlugin()
    {
      if (_autoReadKeepBlinkingEyeInput)
      {
        var snapshot = EyeInputDebugState.Latest;
        if (snapshot.FaceDetected && snapshot.SmoothedFaceArea > 0.0001f)
        {
          faceDistance = Mathf.Clamp01(Mathf.InverseLerp(0.015f, 0.13f, snapshot.SmoothedFaceArea));
        }
      }

      _smoothedFaceDistance = Mathf.Lerp(
        _smoothedFaceDistance,
        faceDistance,
        1f - Mathf.Exp(-_faceDistanceSmoothSpeed * Time.deltaTime));
      UpdateFaceDistanceBaseline();
    }

    private void UpdateFaceDistanceBaseline()
    {
      if (_gameplayState == GameplayState.ModuleUpgrade ||
          _smoothedFaceDistance <= 0.01f ||
          HasCollectableSamples())
      {
        return;
      }

      if (_faceDistanceBaseline < 0f)
      {
        _faceDistanceBaseline = _smoothedFaceDistance;
        return;
      }

      _faceDistanceBaseline = Mathf.Lerp(
        _faceDistanceBaseline,
        _smoothedFaceDistance,
        1f - Mathf.Exp(-0.7f * Time.deltaTime));
    }

    private void UpdateEyesClosedState(EyeInputDebugSnapshot snapshot)
    {
      var averageOpen = (snapshot.LeftEyeOpen + snapshot.RightEyeOpen) * 0.5f;
      var baselineAverage = GetBlinkBaselineAverage();
      var closedByAbsoluteThreshold = averageOpen <= _gentleCloseAbsoluteThreshold ||
                                      (snapshot.LeftEyeOpen <= BlinkClosedThreshold() && snapshot.RightEyeOpen <= BlinkClosedThreshold());
      var closedByRelativeThreshold = baselineAverage > 0f &&
                                      averageOpen <= baselineAverage * _gentleCloseRelativeRatio &&
                                      baselineAverage - averageOpen >= _gentleCloseMinimumDrop;
      var closedByThreshold = snapshot.IsBlinking || closedByAbsoluteThreshold || closedByRelativeThreshold;
      var openByAbsoluteThreshold = snapshot.LeftEyeOpen >= _openEyeReleaseThreshold &&
                                    snapshot.RightEyeOpen >= _openEyeReleaseThreshold;
      var openByRelativeThreshold = baselineAverage > 0f &&
                                    averageOpen >= baselineAverage * _relativeOpenReleaseRatio;
      var openByThreshold = openByAbsoluteThreshold || openByRelativeThreshold;

      if (closedByThreshold)
      {
        _lastEyesClosedSignalAt = Time.time;
        if (_eyesClosedCandidateStartedAt < 0f)
        {
          _eyesClosedCandidateStartedAt = Time.time;
        }

        if (Time.time - _eyesClosedCandidateStartedAt >= _closedEyeEnterHoldSeconds)
        {
          isEyesClosed = true;
        }
        return;
      }

      if (openByThreshold)
      {
        isEyesClosed = false;
        _eyesClosedCandidateStartedAt = -1f;
      }
    }

    private float GetBlinkBaselineAverage()
    {
      if (_baselineLeftEyeOpen <= 0f || _baselineRightEyeOpen <= 0f)
      {
        return -1f;
      }

      return (_baselineLeftEyeOpen + _baselineRightEyeOpen) * 0.5f;
    }

    private void MaintainClosedEyesDuringLostFace()
    {
      if (!isEyesClosed)
      {
        return;
      }

      if (Time.time - _lastEyesClosedSignalAt <= _closedEyeLostFaceGraceSeconds)
      {
        return;
      }

      // If tracking never returns, fail open instead of trapping the player forever.
      isEyesClosed = false;
    }

    private void TryTreatLostFaceAsClosedDuringCrisis()
    {
      if (!_treatLostFaceAsClosedDuringCrisis ||
          isEyesClosed ||
          (_gameplayState != GameplayState.Crisis && _gameplayState != GameplayState.EyesClosedFreeze))
      {
        return;
      }

      if (Time.time - _lastFaceDetectedAt > _hardwareTimeoutSeconds)
      {
        return;
      }

      if (_eyesClosedCandidateStartedAt < 0f &&
          Time.time - _lastEyesClosedSignalAt > 0.2f)
      {
        return;
      }

      isEyesClosed = true;
      _lastEyesClosedSignalAt = Time.time;
    }

    private float BlinkClosedThreshold()
    {
      return Mathf.Max(0.08f, EyeInputDebugState.BlinkOpenThreshold);
    }

    private bool ConsumePluginBlink(EyeInputDebugSnapshot snapshot)
    {
      if (Time.time - _lastBlinkAcceptedAt < _blinkCooldownSeconds)
      {
        return false;
      }

      UpdateBlinkBaseline(snapshot);

      if (_lastObservedBlinkCount < 0)
      {
        _lastObservedBlinkCount = snapshot.BlinkCount;
        return false;
      }

      if (snapshot.BlinkCount > _lastObservedBlinkCount)
      {
        _lastObservedBlinkCount = snapshot.BlinkCount;
        _lastBlinkAcceptedAt = Time.time;
        return true;
      }

      _lastObservedBlinkCount = snapshot.BlinkCount;
      if (ConsumeRelativeBlink(snapshot))
      {
        _lastBlinkAcceptedAt = Time.time;
        return true;
      }

      return false;
    }

    private void UpdateBlinkBaseline(EyeInputDebugSnapshot snapshot)
    {
      var averageOpen = (snapshot.LeftEyeOpen + snapshot.RightEyeOpen) * 0.5f;
      if (averageOpen < _minimumOpenEyeForBlinkBaseline)
      {
        return;
      }

      _baselineLeftEyeOpen = Mathf.Max(_baselineLeftEyeOpen, snapshot.LeftEyeOpen);
      _baselineRightEyeOpen = Mathf.Max(_baselineRightEyeOpen, snapshot.RightEyeOpen);
    }

    private bool ConsumeRelativeBlink(EyeInputDebugSnapshot snapshot)
    {
      if (_baselineLeftEyeOpen <= 0f || _baselineRightEyeOpen <= 0f)
      {
        return false;
      }

      var currentAverage = (snapshot.LeftEyeOpen + snapshot.RightEyeOpen) * 0.5f;
      var baselineAverage = (_baselineLeftEyeOpen + _baselineRightEyeOpen) * 0.5f;
      var relativeBlinking = currentAverage <= baselineAverage * _relativeBlinkCloseRatio;
      var blinkStarted = relativeBlinking && !_lastRelativeBlinking;
      _lastRelativeBlinking = relativeBlinking;
      return blinkStarted;
    }

    private void AddRecentRawGazeSample(Vector2 rawGazeScreenPosition)
    {
      _recentRawGazeSamples.Add(new TimedGazeSample(rawGazeScreenPosition, Time.time));

      var oldestAllowed = Time.time - Mathf.Max(0.5f, _preBlinkSampleWindowSeconds + 0.2f);
      for (var i = _recentRawGazeSamples.Count - 1; i >= 0; i--)
      {
        if (_recentRawGazeSamples[i].Time < oldestAllowed)
        {
          _recentRawGazeSamples.RemoveAt(i);
        }
      }
    }

    private Vector2 GetStableRawGazeBeforeBlink()
    {
      var sampleStart = Time.time - _preBlinkSampleWindowSeconds;
      var sampleEnd = Time.time - _preBlinkIgnoreRecentSeconds;
      var sum = Vector2.zero;
      var count = 0;

      for (var i = 0; i < _recentRawGazeSamples.Count; i++)
      {
        var sample = _recentRawGazeSamples[i];
        if (sample.Time < sampleStart || sample.Time > sampleEnd)
        {
          continue;
        }

        sum += sample.Position;
        count++;
      }

      return count == 0 ? _rawGazeScreenPosition : sum / count;
    }

    private void EnsureCamera()
    {
      if (_camera == null)
      {
        _camera = Camera.main;
      }

      if (_camera == null)
      {
        var cameraObject = new GameObject("Edge Orbit MVP Camera");
        _camera = cameraObject.AddComponent<Camera>();
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        cameraObject.transform.rotation = Quaternion.identity;
      }

      if (!_setupOrthographicCamera)
      {
        return;
      }

      _camera.orthographic = true;
      _camera.orthographicSize = _orthographicSize;
      _camera.clearFlags = CameraClearFlags.SolidColor;
      _camera.backgroundColor = new Color(0.015f, 0.035f, 0.03f, 1f);

      if (_forcePortraitProjection)
      {
        _camera.aspect = _portraitAspect;
      }
    }

    private void CreateRuntimeSprite()
    {
      _squareTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
      {
        name = "EdgeOrbitRuntimeSquare",
        filterMode = FilterMode.Point,
        wrapMode = TextureWrapMode.Clamp,
      };
      _squareTexture.SetPixel(0, 0, Color.white);
      _squareTexture.Apply();

      _squareSprite = Sprite.Create(
        _squareTexture,
        new Rect(0f, 0f, 1f, 1f),
        new Vector2(0.5f, 0.5f),
        1f);
      _squareSprite.name = "EdgeOrbitRuntimeSquareSprite";
    }

    private void CreateRuntimeCircleSprite()
    {
      var size = Mathf.Max(16, _purificationCircleTextureSize);
      _circleTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
      {
        name = "PurificationRuntimeCircle",
        filterMode = FilterMode.Bilinear,
        wrapMode = TextureWrapMode.Clamp,
      };

      var center = (size - 1) * 0.5f;
      var radius = Mathf.Max(1f, center);
      for (var y = 0; y < size; y++)
      {
        for (var x = 0; x < size; x++)
        {
          var dx = (x - center) / radius;
          var dy = (y - center) / radius;
          var distance = Mathf.Sqrt(dx * dx + dy * dy);
          var fill = 1f - Mathf.SmoothStep(0.78f, 1f, distance);
          var ring = Mathf.SmoothStep(0.64f, 0.76f, distance) * (1f - Mathf.SmoothStep(0.88f, 0.98f, distance));
          var alpha = Mathf.Clamp01(fill * 0.34f + ring * 0.5f);
          _circleTexture.SetPixel(x, y, new Color(0.42f, 1f, 0.78f, alpha));
        }
      }

      _circleTexture.Apply();
      _circleSprite = Sprite.Create(
        _circleTexture,
        new Rect(0f, 0f, size, size),
        new Vector2(0.5f, 0.5f),
        size);
      _circleSprite.name = "PurificationRuntimeCircleSprite";
    }

    private void CreateGazeIndicator()
    {
      _gazeIndicatorRoot = new GameObject("Soft Gaze Indicator");
      _gazeIndicatorRoot.transform.SetParent(transform, false);

      var size = _gazeIndicatorWorldSize;
      CreateIndicatorPiece("Gaze Center", Vector3.zero, new Vector3(size * 0.18f, size * 0.18f, 1f), GazeIdleColor, 100);
      CreateIndicatorPiece("Gaze Horizontal", Vector3.zero, new Vector3(size, size * 0.045f, 1f), GazeIdleColor, 99);
      CreateIndicatorPiece("Gaze Vertical", Vector3.zero, new Vector3(size * 0.045f, size, 1f), GazeIdleColor, 99);
      CreateIndicatorPiece("Gaze Soft Halo", Vector3.zero, new Vector3(size * 1.35f, size * 1.35f, 1f), new Color(GazeIdleColor.r, GazeIdleColor.g, GazeIdleColor.b, 0.12f), 98);
    }

    private void CreatePlayerMarker()
    {
      _playerMarkerRoot = new GameObject("Center Player Marker");
      _playerMarkerRoot.transform.SetParent(transform, false);

      var size = _playerMarkerWorldSize;
      CreatePlayerMarkerPiece("Player Halo", Vector3.zero, new Vector3(size * 1.15f, size * 1.15f, 1f), new Color(0.28f, 1f, 0.72f, 0.08f), 8);
      CreatePlayerMarkerPiece("Player Head", new Vector3(0f, size * 0.26f, 0f), new Vector3(size * 0.28f, size * 0.28f, 1f), new Color(0.72f, 1f, 0.86f, 0.88f), 11);
      CreatePlayerMarkerPiece("Player Body", new Vector3(0f, -size * 0.04f, 0f), new Vector3(size * 0.24f, size * 0.42f, 1f), new Color(0.18f, 0.62f, 0.42f, 0.9f), 10);
      CreatePlayerMarkerPiece("Player Arms", new Vector3(0f, size * 0.02f, 0f), new Vector3(size * 0.58f, size * 0.09f, 1f), new Color(0.58f, 1f, 0.75f, 0.86f), 9);
      CreatePlayerMarkerPiece("Player Left Leg", new Vector3(-size * 0.08f, -size * 0.34f, 0f), new Vector3(size * 0.1f, size * 0.28f, 1f), new Color(0.58f, 1f, 0.75f, 0.86f), 9);
      CreatePlayerMarkerPiece("Player Right Leg", new Vector3(size * 0.08f, -size * 0.34f, 0f), new Vector3(size * 0.1f, size * 0.28f, 1f), new Color(0.58f, 1f, 0.75f, 0.86f), 9);
      UpdatePlayerMarker();
    }

    private void CreatePlayerMarkerPiece(string pieceName, Vector3 localPosition, Vector3 localScale, Color color, int sortingOrder)
    {
      var piece = new GameObject(pieceName);
      piece.transform.SetParent(_playerMarkerRoot.transform, false);
      piece.transform.localPosition = localPosition;
      piece.transform.localScale = localScale;

      var renderer = piece.AddComponent<SpriteRenderer>();
      renderer.sprite = _squareSprite;
      renderer.color = color;
      renderer.sortingOrder = sortingOrder;
      _playerMarkerPieces.Add(renderer);
    }

    private void CreateIndicatorPiece(string pieceName, Vector3 localPosition, Vector3 localScale, Color color, int sortingOrder)
    {
      var piece = new GameObject(pieceName);
      piece.transform.SetParent(_gazeIndicatorRoot.transform, false);
      piece.transform.localPosition = localPosition;
      piece.transform.localScale = localScale;

      var renderer = piece.AddComponent<SpriteRenderer>();
      renderer.sprite = _squareSprite;
      renderer.color = color;
      renderer.sortingOrder = sortingOrder;
      _gazeIndicatorPieces.Add(renderer);
    }

    private void CreateCalibrationTarget()
    {
      _calibrationTargetRoot = new GameObject("Gaze Calibration Target");
      _calibrationTargetRoot.transform.SetParent(transform, false);

      var size = _calibrationTargetWorldSize;
      CreateCalibrationPiece("Calibration Center", Vector3.zero, new Vector3(size * 0.22f, size * 0.22f, 1f), CalibrationColor, 105);
      CreateCalibrationPiece("Calibration Horizontal", Vector3.zero, new Vector3(size, size * 0.055f, 1f), CalibrationColor, 104);
      CreateCalibrationPiece("Calibration Vertical", Vector3.zero, new Vector3(size * 0.055f, size, 1f), CalibrationColor, 104);
      CreateCalibrationPiece("Calibration Halo", Vector3.zero, new Vector3(size * 1.45f, size * 1.45f, 1f), new Color(CalibrationColor.r, CalibrationColor.g, CalibrationColor.b, 0.14f), 103);
      _calibrationTargetRoot.SetActive(false);
    }

    private void CreateCalibrationPiece(string pieceName, Vector3 localPosition, Vector3 localScale, Color color, int sortingOrder)
    {
      var piece = new GameObject(pieceName);
      piece.transform.SetParent(_calibrationTargetRoot.transform, false);
      piece.transform.localPosition = localPosition;
      piece.transform.localScale = localScale;

      var renderer = piece.AddComponent<SpriteRenderer>();
      renderer.sprite = _squareSprite;
      renderer.color = color;
      renderer.sortingOrder = sortingOrder;
      _calibrationTargetPieces.Add(renderer);
    }

    private void CreateBlackoutOverlay()
    {
      _blackoutRoot = new GameObject("Eyes Closed Blackout Overlay");
      _blackoutRoot.transform.SetParent(transform, false);

      _blackoutRenderer = _blackoutRoot.AddComponent<SpriteRenderer>();
      _blackoutRenderer.sprite = _squareSprite;
      _blackoutRenderer.color = new Color(0f, 0f, 0f, _blackoutAlpha);
      _blackoutRenderer.sortingOrder = 900;
      SetBlackoutVisible(false);
      ResizeBlackoutOverlay();
    }

    private void CreatePurificationWave()
    {
      _purificationWaveRoot = new GameObject("Purification Expanding Wave");
      _purificationWaveRoot.transform.SetParent(transform, false);
      _purificationWaveRenderer = _purificationWaveRoot.AddComponent<SpriteRenderer>();
      _purificationWaveRenderer.sprite = _circleSprite;
      _purificationWaveRenderer.color = new Color(0.42f, 1f, 0.78f, _purificationVisualAlpha);
      _purificationWaveRenderer.sortingOrder = 950;
      SetPurificationWaveVisible(false);
    }

    private void CreateProgressBar()
    {
      _progressBarRoot = new GameObject("Reflection Sample Progress Bar");
      _progressBarRoot.transform.SetParent(transform, false);

      var back = new GameObject("Progress Back");
      back.transform.SetParent(_progressBarRoot.transform, false);
      _progressBarBackRenderer = back.AddComponent<SpriteRenderer>();
      _progressBarBackRenderer.sprite = _squareSprite;
      _progressBarBackRenderer.color = ProgressBackColor;
      _progressBarBackRenderer.sortingOrder = 80;

      var fill = new GameObject("Progress Fill");
      fill.transform.SetParent(_progressBarRoot.transform, false);
      _progressBarFillRenderer = fill.AddComponent<SpriteRenderer>();
      _progressBarFillRenderer.sprite = _squareSprite;
      _progressBarFillRenderer.color = ProgressFillColor;
      _progressBarFillRenderer.sortingOrder = 81;

      UpdateProgressBarVisual();
    }

    private void CreateFreezeFeedbackAudio()
    {
      _feedbackAudioSource = gameObject.AddComponent<AudioSource>();
      _feedbackAudioSource.playOnAwake = false;
      _feedbackAudioSource.loop = false;
      _feedbackAudioSource.volume = _freezeFeedbackVolume;
      _feedbackAudioSource.spatialBlend = 0f;

      _freezeStartedClip = CreateToneClip("Freeze Started Tone", 440f, 0.1f);
      _coverageCompleteClip = CreateToneClip("Coverage Complete Tone", 660f, 0.16f);
      _freezeFailedClip = CreateToneClip("Freeze Failed Tone", 220f, 0.12f);
      _freezeClearedClip = CreateToneClip("Freeze Cleared Tone", 880f, 0.18f);
    }

    private AudioClip CreateToneClip(string clipName, float frequency, float duration)
    {
      const int sampleRate = 44100;
      var sampleCount = Mathf.Max(1, Mathf.CeilToInt(sampleRate * duration));
      var samples = new float[sampleCount];

      for (var i = 0; i < sampleCount; i++)
      {
        var t = i / (float)sampleRate;
        var envelope = Mathf.Sin(Mathf.PI * i / Mathf.Max(1, sampleCount - 1));
        samples[i] = Mathf.Sin(Mathf.PI * 2f * frequency * t) * envelope * 0.28f;
      }

      var clip = AudioClip.Create(clipName, sampleCount, 1, sampleRate, false);
      clip.SetData(samples, 0);
      return clip;
    }

    private void PlayFeedbackClip(AudioClip clip)
    {
      if (!_playFreezeFeedbackAudio || _feedbackAudioSource == null || clip == null)
      {
        return;
      }

      _feedbackAudioSource.PlayOneShot(clip, _freezeFeedbackVolume);
    }

    private void SetBlackoutVisible(bool visible)
    {
      if (_blackoutRoot != null && _blackoutRoot.activeSelf != visible)
      {
        _blackoutRoot.SetActive(visible);
      }
    }

    private void UpdateBlackoutOverlay()
    {
      if (_blackoutRoot == null || !_blackoutRoot.activeSelf)
      {
        return;
      }

      ResizeBlackoutOverlay();
    }

    private void SetPurificationWaveVisible(bool visible)
    {
      if (_purificationWaveRoot != null && _purificationWaveRoot.activeSelf != visible)
      {
        _purificationWaveRoot.SetActive(visible);
      }
    }

    private void UpdatePurificationWaveVisual()
    {
      if (_purificationWaveRoot == null || _camera == null)
      {
        return;
      }

      var center = _camera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, _blockDepthFromCamera));
      _purificationWaveRoot.transform.position = center;
      var diameter = Mathf.Max(0.001f, _purificationRadius * 2f);
      _purificationWaveRoot.transform.localScale = new Vector3(diameter, diameter, 1f);

      if (_purificationWaveRenderer != null)
      {
        var color = _purificationWaveRenderer.color;
        color.a = _purificationVisualAlpha;
        _purificationWaveRenderer.color = color;
      }
    }

    private void ResizeBlackoutOverlay()
    {
      if (_blackoutRoot == null || _camera == null)
      {
        return;
      }

      var center = _camera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, _blockDepthFromCamera));
      var bottomLeft = _camera.ViewportToWorldPoint(new Vector3(0f, 0f, _blockDepthFromCamera));
      var topRight = _camera.ViewportToWorldPoint(new Vector3(1f, 1f, _blockDepthFromCamera));
      _blackoutRoot.transform.position = center;
      _blackoutRoot.transform.localScale = new Vector3(
        Mathf.Abs(topRight.x - bottomLeft.x) * 1.2f,
        Mathf.Abs(topRight.y - bottomLeft.y) * 1.2f,
        1f);
    }

    private Vector2 GetSafeInitialGazePosition()
    {
      if (realGazeScreenPosition.sqrMagnitude > 0.01f)
      {
        return realGazeScreenPosition;
      }

      return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
    }

    private void SetupCalibration()
    {
      _calibrationTargets = new[]
      {
        ViewportToScreenPoint(new Vector2(0.5f, 0.5f)),
        ViewportToScreenPoint(new Vector2(_calibrationEdgePaddingViewport, 1f - _calibrationEdgePaddingViewport)),
        ViewportToScreenPoint(new Vector2(1f - _calibrationEdgePaddingViewport, 1f - _calibrationEdgePaddingViewport)),
        ViewportToScreenPoint(new Vector2(1f - _calibrationEdgePaddingViewport, _calibrationEdgePaddingViewport)),
        ViewportToScreenPoint(new Vector2(_calibrationEdgePaddingViewport, _calibrationEdgePaddingViewport)),
      };

      _calibrationActive = _runStartupCalibration;
      _calibrationComplete = !_runStartupCalibration;
      _calibrationIndex = 0;
      _calibrationRawSamples.Clear();
      _calibrationTargetSamples.Clear();

      if (_calibrationTargetRoot != null)
      {
        _calibrationTargetRoot.SetActive(_calibrationActive);
      }

      if (_calibrationActive)
      {
        MoveCalibrationTargetToCurrentPoint(true);
      }
    }

    private Vector2 ViewportToScreenPoint(Vector2 viewportPoint)
    {
      return new Vector2(viewportPoint.x * Screen.width, viewportPoint.y * Screen.height);
    }

    private bool UpdateCalibration()
    {
      if (!_calibrationActive)
      {
        return false;
      }

      MoveCalibrationTargetToCurrentPoint(false);

      if (_blinkQueued)
      {
        _blinkQueued = false;
        AcceptCalibrationSample();
      }

      return true;
    }

    private void MoveCalibrationTargetToCurrentPoint(bool snap)
    {
      if (_calibrationTargetRoot == null || _calibrationTargets == null || _calibrationTargets.Length == 0)
      {
        return;
      }

      var targetScreen = _calibrationTargets[Mathf.Clamp(_calibrationIndex, 0, _calibrationTargets.Length - 1)];
      var targetWorld = _camera.ScreenToWorldPoint(new Vector3(targetScreen.x, targetScreen.y, _blockDepthFromCamera));

      if (snap)
      {
        _calibrationTargetRoot.transform.position = targetWorld;
      }
      else
      {
        _calibrationTargetRoot.transform.position = Vector3.Lerp(
          _calibrationTargetRoot.transform.position,
          targetWorld,
          1f - Mathf.Exp(-10f * Time.deltaTime));
      }

      var pulse = 1f + Mathf.Sin(Time.time * 3.5f) * 0.08f;
      _calibrationTargetRoot.transform.localScale = Vector3.one * pulse;
    }

    private void AcceptCalibrationSample()
    {
      if (_calibrationTargets == null || _calibrationIndex >= _calibrationTargets.Length)
      {
        FinishCalibration();
        return;
      }

      _calibrationRawSamples.Add(GetStableRawGazeBeforeBlink());
      _calibrationTargetSamples.Add(_calibrationTargets[_calibrationIndex]);
      _calibrationIndex++;

      if (_calibrationIndex >= _calibrationTargets.Length)
      {
        FinishCalibration();
        return;
      }

      MoveCalibrationTargetToCurrentPoint(true);
    }

    private void FinishCalibration()
    {
      CalculateCalibrationTransform();
      _calibrationActive = false;
      _calibrationComplete = true;

      if (_calibrationTargetRoot != null)
      {
        _calibrationTargetRoot.SetActive(false);
      }

      realGazeScreenPosition = ApplyGazeCalibration(_rawGazeScreenPosition);
      ScheduleNextSpawn(0.35f);
      Debug.Log($"KeepBlinking gaze calibration complete. Scale={_calibrationScale}, Offset={_calibrationOffset}");
    }

    private void CalculateCalibrationTransform()
    {
      if (_calibrationRawSamples.Count < 2 || _calibrationTargetSamples.Count != _calibrationRawSamples.Count)
      {
        _calibrationScale = Vector2.one;
        _calibrationOffset = Vector2.zero;
        return;
      }

      var rawMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
      var rawMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
      var targetMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
      var targetMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

      for (var i = 0; i < _calibrationRawSamples.Count; i++)
      {
        rawMin = Vector2.Min(rawMin, _calibrationRawSamples[i]);
        rawMax = Vector2.Max(rawMax, _calibrationRawSamples[i]);
        targetMin = Vector2.Min(targetMin, _calibrationTargetSamples[i]);
        targetMax = Vector2.Max(targetMax, _calibrationTargetSamples[i]);
      }

      var rawRange = rawMax - rawMin;
      var targetRange = targetMax - targetMin;
      _calibrationScale = new Vector2(
        rawRange.x < 1f ? 1f : Mathf.Clamp(targetRange.x / rawRange.x, _calibrationMinScale, _calibrationMaxScale),
        rawRange.y < 1f ? 1f : Mathf.Clamp(targetRange.y / rawRange.y, _calibrationMinScale, _calibrationMaxScale));

      var rawCenter = (rawMin + rawMax) * 0.5f;
      var targetCenter = (targetMin + targetMax) * 0.5f;
      _calibrationOffset = targetCenter - Vector2.Scale(rawCenter, _calibrationScale);
    }

    private Vector2 ApplyGazeCalibration(Vector2 rawScreenPosition)
    {
      if (!_calibrationComplete)
      {
        return ClampScreenPosition(rawScreenPosition);
      }

      return ClampScreenPosition(Vector2.Scale(rawScreenPosition, _calibrationScale) + _calibrationOffset);
    }

    private Vector2 ClampScreenPosition(Vector2 screenPosition)
    {
      return new Vector2(
        Mathf.Clamp(screenPosition.x, 0f, Screen.width),
        Mathf.Clamp(screenPosition.y, 0f, Screen.height));
    }

    private void SpawnOnTimer()
    {
      if (Time.time < _nextSpawnAt)
      {
        return;
      }

      while (CountState(BlockState.Orbiting) >= _maxOrbitingBlocks)
      {
        if (!SoftFadeOldestOrbitingBlock())
        {
          break;
        }
      }

      SpawnOrbitBlock();
      ScheduleNextSpawn(Random.Range(_minSpawnIntervalSeconds, _maxSpawnIntervalSeconds));
    }

    private void UpdateGameplayState()
    {
      if (_gameplayState == GameplayState.SessionReport)
      {
        return;
      }

      switch (_gameplayState)
      {
        case GameplayState.Orbiting:
          if (Time.time >= _nextCrisisAt)
          {
            BeginCrisis();
            return;
          }

          SpawnOnTimer();
          break;
        case GameplayState.Crisis:
          if (CountState(BlockState.Crisis) == 0)
          {
            EndCrisisAndResumeOrbiting();
            return;
          }

          if (isEyesClosed)
          {
            BeginEyesClosedFreeze();
          }
          break;
        case GameplayState.EyesClosedFreeze:
          UpdateEyesClosedFreeze();
          break;
      }
    }

    private void BeginCrisis()
    {
      _gameplayState = GameplayState.Crisis;
      _hoveredBlock = null;
      _lastHoveredBlock = null;
      SpawnCrisisBlocks();
    }

    private void BeginEyesClosedFreeze()
    {
      _gameplayState = GameplayState.EyesClosedFreeze;
      _eyesClosedStartedAt = Time.time;
      _purificationRadius = 0f;
      _wasEyesClosed = true;
      _coverageCuePlayed = false;
      _eyeRestBreakCount++;
      SetBlackoutVisible(true);
      SetPurificationWaveVisible(true);
      UpdatePurificationWaveVisual();
      PlayFeedbackClip(_freezeStartedClip);
    }

    private void UpdateEyesClosedFreeze()
    {
      if (isEyesClosed)
      {
        _wasEyesClosed = true;
        UpdatePurificationExpansion();
        return;
      }

      if (!_wasEyesClosed)
      {
        return;
      }

      var closedSeconds = Time.time - _eyesClosedStartedAt;
      SetBlackoutVisible(false);
      _wasEyesClosed = false;
      _eyesClosedStartedAt = -1f;
      _lastFreezeDuration = closedSeconds;
      _lastFreezeResultAt = Time.time;
      var clearedCount = ClearCrisisWithinCurrentRadius();
      var remainingCount = CountState(BlockState.Crisis);
      SetPurificationWaveVisible(false);
      _purificationRadius = 0f;
      _coverageCuePlayed = false;

      if (clearedCount > 0)
      {
        _lastFreezeResult = $"CLEARED {clearedCount} by radius";
        PlayFeedbackClip(_freezeClearedClip);
      }
      else
      {
        _lastFreezeResult = "NO COVERAGE: opened too early";
        PlayFeedbackClip(_freezeFailedClip);
      }

      if (remainingCount == 0)
      {
        _crisisClearCount++;
        EndCrisisAndResumeOrbiting();
      }
      else
      {
        _gameplayState = GameplayState.Crisis;
      }
    }

    private void UpdatePurificationExpansion()
    {
      _purificationRadius = Mathf.Min(GetScreenMaxRadiusFromCenter(), _purificationRadius + _purificationRadiusGrowthSpeed * Time.deltaTime);
      UpdatePurificationWaveVisual();

      var farthestDistance = GetFarthestCrisisDistanceFromCenter();
      if (!_coverageCuePlayed &&
          (_purificationRadius >= farthestDistance || _purificationRadius >= GetScreenMaxRadiusFromCenter()))
      {
        _coverageCuePlayed = true;
        PlayFeedbackClip(_coverageCompleteClip);
      }
    }

    private int ClearCrisisWithinCurrentRadius()
    {
      var clearedCount = 0;
      for (var i = 0; i < _blocks.Count; i++)
      {
        var block = _blocks[i];
        if (block.State == BlockState.Crisis)
        {
          var distanceFromCenter = GetDistanceFromCenter(block.Transform.position);
          if (distanceFromCenter <= _purificationRadius)
          {
            clearedCount++;
            StartCoroutine(HarvestRoutine(block));
          }
        }
      }

      return clearedCount;
    }

    private void EndCrisisAndResumeOrbiting()
    {
      _gameplayState = GameplayState.Orbiting;
      SetBlackoutVisible(false);
      SetPurificationWaveVisible(false);
      _purificationRadius = 0f;
      _coverageCuePlayed = false;
      ScheduleNextSpawn(0.45f);
      ScheduleNextCrisis();
    }

    private float GetFarthestCrisisDistanceFromCenter()
    {
      var farthest = 0f;
      for (var i = 0; i < _blocks.Count; i++)
      {
        var block = _blocks[i];
        if (block.State != BlockState.Crisis)
        {
          continue;
        }

        farthest = Mathf.Max(farthest, GetDistanceFromCenter(block.Transform.position));
      }

      return farthest;
    }

    private float GetScreenMaxRadiusFromCenter()
    {
      if (_camera == null)
      {
        return Mathf.Max(1f, _orthographicSize);
      }

      var center = _camera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, _blockDepthFromCamera));
      var corners = new[]
      {
        _camera.ViewportToWorldPoint(new Vector3(0f, 0f, _blockDepthFromCamera)),
        _camera.ViewportToWorldPoint(new Vector3(0f, 1f, _blockDepthFromCamera)),
        _camera.ViewportToWorldPoint(new Vector3(1f, 0f, _blockDepthFromCamera)),
        _camera.ViewportToWorldPoint(new Vector3(1f, 1f, _blockDepthFromCamera)),
      };

      var maxRadius = 0f;
      for (var i = 0; i < corners.Length; i++)
      {
        maxRadius = Mathf.Max(maxRadius, Vector2.Distance(center, corners[i]));
      }

      return maxRadius;
    }

    private float GetDistanceFromCenter(Vector3 worldPosition)
    {
      if (_camera == null)
      {
        return new Vector2(worldPosition.x, worldPosition.y).magnitude;
      }

      var center = _camera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, _blockDepthFromCamera));
      return Vector2.Distance(new Vector2(center.x, center.y), new Vector2(worldPosition.x, worldPosition.y));
    }

    private void ScheduleNextCrisis()
    {
      _nextCrisisAt = Time.time + Random.Range(_minCrisisIntervalSeconds, _maxCrisisIntervalSeconds);
    }

    private void ScheduleNextSpawn(float delaySeconds)
    {
      _nextSpawnAt = Time.time + Mathf.Max(0.01f, delaySeconds);
    }

    private void UpdateSessionTimer()
    {
      if (!_enableSessionReportTimer || _sessionEnded || _sessionStartedAt < 0f || _calibrationActive)
      {
        return;
      }

      if (Time.time - _sessionStartedAt >= _sessionDurationSeconds)
      {
        EndSessionAndShowReport();
      }
    }

    private void EndSessionAndShowReport()
    {
      if (_sessionEnded)
      {
        return;
      }

      _sessionEnded = true;
      _gameplayState = GameplayState.SessionReport;
      _blinkQueued = false;
      _hoveredBlock = null;
      _lastHoveredBlock = null;
      _pushAwayWasActive = false;
      _pushAwayReady = false;
      _pushAwayReadyCandidateStartedAt = -1f;
      _pushAwayCandidateStartedAt = -1f;
      ClearModuleCards();
      SetBlackoutVisible(false);
      SetPurificationWaveVisible(false);
      Debug.Log("KeepBlinking MVP session report opened.");
    }

    private void SpawnOrbitBlock()
    {
      var blockObject = new GameObject($"Edge Orbit Block {_spawnSerial + 1}");
      var renderer = blockObject.AddComponent<SpriteRenderer>();
      renderer.sprite = _squareSprite;
      renderer.color = OrbitColor;
      renderer.sortingOrder = 20;

      var size = Random.Range(_blockWorldSizeRange.x, _blockWorldSizeRange.y);
      var baseScale = new Vector3(size, size, 1f);
      blockObject.transform.localScale = baseScale;
      blockObject.transform.rotation = Quaternion.identity;

      var phase = Random.Range(0f, Mathf.PI * 2f);
      var speed = Random.Range(_orbitAngularSpeedRange.x, _orbitAngularSpeedRange.y);
      var direction = Random.value < 0.5f ? -1f : 1f;

      var block = new OrbitBlock(
        blockObject,
        renderer,
        phase,
        speed * direction,
        _spawnSerial,
        Time.time,
        baseScale);

      blockObject.transform.position = EvaluateOrbitWorldPosition(block.Phase);
      _blocks.Add(block);
      _spawnSerial++;
    }

    private void SpawnCrisisBlocks()
    {
      for (var i = 0; i < _crisisSpawnCount; i++)
      {
        SpawnCrisisBlock();
      }
    }

    private void SpawnCrisisBlock()
    {
      var blockObject = new GameObject($"Crisis Inward Block {_spawnSerial + 1}");
      var renderer = blockObject.AddComponent<SpriteRenderer>();
      renderer.sprite = _squareSprite;
      renderer.color = CrisisColor;
      renderer.sortingOrder = 25;

      var size = Random.Range(_crisisBlockWorldSizeRange.x, _crisisBlockWorldSizeRange.y);
      var baseScale = new Vector3(size, size, 1f);
      blockObject.transform.localScale = baseScale;
      blockObject.transform.rotation = Quaternion.identity;

      var spawnViewport = GetRandomOffscreenViewportPoint();
      var worldPosition = _camera.ViewportToWorldPoint(new Vector3(spawnViewport.x, spawnViewport.y, _blockDepthFromCamera));
      blockObject.transform.position = worldPosition;

      var block = new OrbitBlock(
        blockObject,
        renderer,
        Random.Range(0f, Mathf.PI * 2f),
        0f,
        _spawnSerial,
        Time.time,
        baseScale)
      {
        State = BlockState.Crisis,
        CrisisMoveSpeed = Random.Range(_crisisMoveSpeedRange.x, _crisisMoveSpeedRange.y),
      };

      _blocks.Add(block);
      _spawnSerial++;
    }

    private Vector2 GetRandomOffscreenViewportPoint()
    {
      var side = Random.Range(0, 4);
      var p = Random.value;
      var pad = _crisisSpawnPaddingViewport;

      switch (side)
      {
        case 0:
          return new Vector2(-pad, p);
        case 1:
          return new Vector2(1f + pad, p);
        case 2:
          return new Vector2(p, 1f + pad);
        default:
          return new Vector2(p, -pad);
      }
    }

    private void UpdateBlocksByGameplayState()
    {
      if (_gameplayState == GameplayState.EyesClosedFreeze)
      {
        return;
      }

      UpdateOrbitingBlocks();
      UpdateCrisisBlocks();
      UpdateCollectingBlocks();
    }

    private void UpdateSampleCollection()
    {
      UpdatePushAwayReadyState();
      var pushAwayActive = IsPushAwayActive();
      if (pushAwayActive && !_pushAwayWasActive)
      {
        StartCollectingConvertedSamples();
        _pushAwayReady = false;
        _pushAwayReadyCandidateStartedAt = -1f;
        _pushAwayCandidateStartedAt = -1f;
      }

      _pushAwayWasActive = pushAwayActive;
    }

    private void UpdatePushAwayReadyState()
    {
      if (!HasCollectableSamples())
      {
        _pushAwayReady = false;
        _pushAwayReadyCandidateStartedAt = -1f;
        return;
      }

      var baseline = _faceDistanceBaseline > 0f ? _faceDistanceBaseline : 1f;
      var nearNormalDistance = _smoothedFaceDistance >= baseline * _pushAwayReadyRelativeThreshold;

      if (!nearNormalDistance)
      {
        _pushAwayReadyCandidateStartedAt = -1f;
        return;
      }

      if (_pushAwayReadyCandidateStartedAt < 0f)
      {
        _pushAwayReadyCandidateStartedAt = Time.time;
      }

      if (Time.time - _pushAwayReadyCandidateStartedAt >= _pushAwayReadyHoldSeconds)
      {
        _pushAwayReady = true;
      }
    }

    private bool IsPushAwayActive()
    {
      if (!_pushAwayReady || !HasCollectableSamples())
      {
        _pushAwayCandidateStartedAt = -1f;
        return false;
      }

      var baseline = _faceDistanceBaseline > 0f ? _faceDistanceBaseline : 1f;
      var pushedByAbsoluteThreshold = _smoothedFaceDistance < _pushAwayDistanceThreshold;
      var pushedByRelativeThreshold = _smoothedFaceDistance <= baseline * _pushAwayRelativeThreshold &&
                                      baseline - _smoothedFaceDistance >= _pushAwayAbsoluteDrop;
      var candidate = pushedByAbsoluteThreshold && pushedByRelativeThreshold;

      if (!candidate)
      {
        _pushAwayCandidateStartedAt = -1f;
        return false;
      }

      if (_pushAwayCandidateStartedAt < 0f)
      {
        _pushAwayCandidateStartedAt = Time.time;
      }

      return Time.time - _pushAwayCandidateStartedAt >= _pushAwayHoldSeconds;
    }

    private bool HasCollectableSamples()
    {
      for (var i = 0; i < _blocks.Count; i++)
      {
        if (_blocks[i].State == BlockState.Converted)
        {
          return true;
        }
      }

      return false;
    }

    private void StartCollectingConvertedSamples()
    {
      var startedCollecting = false;
      for (var i = 0; i < _blocks.Count; i++)
      {
        var block = _blocks[i];
        if (block.State != BlockState.Converted)
        {
          continue;
        }

        block.State = BlockState.Collecting;
        block.IsHovered = false;
        block.Renderer.sortingOrder = 70;
        startedCollecting = true;
      }

      if (startedCollecting)
      {
        _distanceSwitchCount++;
      }
    }

    private void UpdateCollectingBlocks()
    {
      var target = GetProgressBarFillWorldPosition();
      for (var i = 0; i < _blocks.Count; i++)
      {
        var block = _blocks[i];
        if (block.State != BlockState.Collecting)
        {
          continue;
        }

        block.Transform.position = Vector3.Lerp(
          block.Transform.position,
          target,
          1f - Mathf.Exp(-_sampleCollectSpeed * Time.deltaTime));
        block.Transform.localScale = Vector3.Lerp(
          block.Transform.localScale,
          block.BaseScale * 0.08f,
          Time.deltaTime * _sampleCollectSpeed);
        block.Renderer.color = Color.Lerp(
          block.Renderer.color,
          ProgressFillColor,
          Time.deltaTime * _sampleCollectSpeed);

        if (Vector2.Distance(block.Transform.position, target) <= _sampleCollectDistance)
        {
          CollectSampleBlock(block);
        }
      }
    }

    private void CollectSampleBlock(OrbitBlock block)
    {
      block.State = BlockState.FadingOut;
      block.IsHovered = false;

      if (block.GameObject != null)
      {
        Destroy(block.GameObject);
      }

      _collectedSampleCount++;
      _totalSamplesCollected++;
      _sampleProgress = Mathf.Clamp01(_collectedSampleCount / Mathf.Max(1f, _samplesNeededForUpgrade));
      UpdateProgressBarVisual();

      if (_sampleProgress >= 1f)
      {
        BeginModuleUpgrade();
      }
    }

    private void BeginModuleUpgrade()
    {
      if (_gameplayState == GameplayState.ModuleUpgrade)
      {
        return;
      }

      _resumeStateAfterUpgrade = _gameplayState == GameplayState.EyesClosedFreeze ? GameplayState.Crisis : _gameplayState;
      _gameplayState = GameplayState.ModuleUpgrade;
      _blinkQueued = false;
      _hoveredBlock = null;
      _lastHoveredBlock = null;
      SetBlackoutVisible(false);
      SetPurificationWaveVisible(false);
      CreateModuleCards();
    }

    private void CreateModuleCards()
    {
      ClearModuleCards();
      var center = _camera.ViewportToWorldPoint(new Vector3(0.5f, 0.52f, _blockDepthFromCamera));
      var totalWidth = _moduleCardSize.x * 3f + _moduleCardSpacing * 2f;
      var startX = center.x - totalWidth * 0.5f + _moduleCardSize.x * 0.5f;

      for (var i = 0; i < 3; i++)
      {
        var root = new GameObject($"Module Card {i + 1}");
        root.transform.SetParent(transform, false);
        root.transform.position = new Vector3(startX + i * (_moduleCardSize.x + _moduleCardSpacing), center.y, center.z);
        root.transform.localScale = new Vector3(_moduleCardSize.x, _moduleCardSize.y, 1f);

        var renderer = root.AddComponent<SpriteRenderer>();
        renderer.sprite = _squareSprite;
        renderer.color = ModuleCardColors[i % ModuleCardColors.Length];
        renderer.sortingOrder = 980;

        _moduleCards.Add(new ModuleCard(root, renderer, i));
      }
    }

    private void ClearModuleCards()
    {
      for (var i = 0; i < _moduleCards.Count; i++)
      {
        var card = _moduleCards[i];
        if (card.GameObject != null)
        {
          Destroy(card.GameObject);
        }
      }

      _moduleCards.Clear();
    }

    private void UpdateModuleUpgradeSelection()
    {
      if (!UnityEngine.Input.GetMouseButtonDown(0))
      {
        return;
      }

      var pointerWorld = _camera.ScreenToWorldPoint(new Vector3(UnityEngine.Input.mousePosition.x, UnityEngine.Input.mousePosition.y, _blockDepthFromCamera));
      for (var i = 0; i < _moduleCards.Count; i++)
      {
        var card = _moduleCards[i];
        if (IsPointInsideCard(pointerWorld, card))
        {
          ChooseModuleCard(card);
          return;
        }
      }
    }

    private bool IsPointInsideCard(Vector3 worldPoint, ModuleCard card)
    {
      if (card.GameObject == null)
      {
        return false;
      }

      var center = card.GameObject.transform.position;
      var halfWidth = _moduleCardSize.x * 0.5f;
      var halfHeight = _moduleCardSize.y * 0.5f;
      return worldPoint.x >= center.x - halfWidth &&
             worldPoint.x <= center.x + halfWidth &&
             worldPoint.y >= center.y - halfHeight &&
             worldPoint.y <= center.y + halfHeight;
    }

    private void ChooseModuleCard(ModuleCard card)
    {
      Debug.Log($"KeepBlinking module selected: card {card.Index + 1}");
      _moduleChoiceCount++;
      ClearModuleCards();
      _collectedSampleCount = 0;
      _sampleProgress = 0f;
      _pushAwayWasActive = false;
      _pushAwayReady = false;
      _pushAwayReadyCandidateStartedAt = -1f;
      _pushAwayCandidateStartedAt = -1f;
      UpdateProgressBarVisual();
      _gameplayState = _resumeStateAfterUpgrade;
      if (_gameplayState == GameplayState.Orbiting)
      {
        ScheduleNextSpawn(0.45f);
      }
    }

    private void UpdateOrbitingBlocks()
    {
      for (var i = 0; i < _blocks.Count; i++)
      {
        var block = _blocks[i];
        if (block.State != BlockState.Orbiting)
        {
          continue;
        }

        block.Phase += block.AngularSpeed * Time.deltaTime;
        var targetPosition = EvaluateOrbitWorldPosition(block.Phase);
        block.Transform.position = Vector3.Lerp(block.Transform.position, targetPosition, Time.deltaTime * 6f);

        var targetColor = block.IsHovered ? HoverColor : OrbitColor;
        block.Renderer.color = Color.Lerp(block.Renderer.color, targetColor, Time.deltaTime * _colorLerpSpeed);

        var targetScale = block.IsHovered ? block.BaseScale * 1.14f : block.BaseScale;
        block.Transform.localScale = Vector3.Lerp(block.Transform.localScale, targetScale, Time.deltaTime * _scaleLerpSpeed);

        // A tiny visual roll makes the block feel alive without adding pressure.
        block.Transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(block.Phase * 1.7f) * 5f);
      }
    }

    private void UpdateCrisisBlocks()
    {
      for (var i = 0; i < _blocks.Count; i++)
      {
        var block = _blocks[i];
        if (block.State != BlockState.Crisis)
        {
          continue;
        }

        var targetPosition = Vector3.zero;
        targetPosition.z = block.Transform.position.z;
        block.Transform.position = Vector3.MoveTowards(
          block.Transform.position,
          targetPosition,
          block.CrisisMoveSpeed * Time.deltaTime);

        var targetColor = block.IsHovered ? HoverColor : CrisisColor;
        block.Renderer.color = Color.Lerp(block.Renderer.color, targetColor, Time.deltaTime * _colorLerpSpeed);

        var targetScale = block.IsHovered ? block.BaseScale * 1.14f : block.BaseScale;
        block.Transform.localScale = Vector3.Lerp(block.Transform.localScale, targetScale, Time.deltaTime * _scaleLerpSpeed);
        block.Transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 4f + block.Serial) * 6f);
      }
    }

    private Vector3 EvaluateOrbitWorldPosition(float phase)
    {
      var x = 0.5f + Mathf.Cos(phase) * (0.5f - _edgeInsetViewport);
      var y = 0.5f + Mathf.Sin(phase) * (0.5f - _edgeInsetViewport);
      x = Mathf.Clamp01(x);
      y = Mathf.Clamp01(y);

      return _camera.ViewportToWorldPoint(new Vector3(x, y, _blockDepthFromCamera));
    }

    private void UpdatePlayerMarker()
    {
      if (_playerMarkerRoot == null || _camera == null)
      {
        return;
      }

      var center = _camera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, _blockDepthFromCamera));
      _playerMarkerRoot.transform.position = center;

      var pulse = 1f + Mathf.Sin(Time.time * _playerMarkerPulseSpeed) * 0.035f;
      if (_gameplayState == GameplayState.EyesClosedFreeze)
      {
        pulse *= 0.92f;
      }

      _playerMarkerRoot.transform.localScale = Vector3.one * pulse;
      var alphaMultiplier = _gameplayState == GameplayState.EyesClosedFreeze ? 0.45f : 1f;
      for (var i = 0; i < _playerMarkerPieces.Count; i++)
      {
        var piece = _playerMarkerPieces[i];
        if (piece == null)
        {
          continue;
        }

        var color = piece.color;
        if (piece.gameObject.name.Contains("Halo"))
        {
          color.a = 0.08f * alphaMultiplier;
        }
        else if (piece.gameObject.name.Contains("Head"))
        {
          color.a = 0.88f * alphaMultiplier;
        }
        else if (piece.gameObject.name.Contains("Body"))
        {
          color.a = 0.9f * alphaMultiplier;
        }
        else
        {
          color.a = 0.86f * alphaMultiplier;
        }

        piece.color = color;
      }
    }

    private Vector3 GetProgressBarCenterWorldPosition()
    {
      return _camera.ViewportToWorldPoint(new Vector3(0.5f, _progressBarBottomViewport, _blockDepthFromCamera));
    }

    private Vector3 GetProgressBarFillWorldPosition()
    {
      var center = GetProgressBarCenterWorldPosition();
      center.z = _camera.ViewportToWorldPoint(new Vector3(0.5f, 0.5f, _blockDepthFromCamera)).z;
      return center;
    }

    private void UpdateProgressBarVisual()
    {
      if (_progressBarRoot == null || _camera == null)
      {
        return;
      }

      var left = _camera.ViewportToWorldPoint(new Vector3(0.5f - _progressBarWidthViewport * 0.5f, _progressBarBottomViewport, _blockDepthFromCamera));
      var right = _camera.ViewportToWorldPoint(new Vector3(0.5f + _progressBarWidthViewport * 0.5f, _progressBarBottomViewport, _blockDepthFromCamera));
      var fullWidth = Mathf.Abs(right.x - left.x);
      var center = GetProgressBarCenterWorldPosition();
      _progressBarRoot.transform.position = center;

      if (_progressBarBackRenderer != null)
      {
        _progressBarBackRenderer.transform.localPosition = Vector3.zero;
        _progressBarBackRenderer.transform.localScale = new Vector3(fullWidth, _progressBarHeightWorld, 1f);
      }

      if (_progressBarFillRenderer != null)
      {
        var fillWidth = Mathf.Max(0.001f, fullWidth * Mathf.Clamp01(_sampleProgress));
        _progressBarFillRenderer.transform.localScale = new Vector3(fillWidth, _progressBarHeightWorld * 0.78f, 1f);
        _progressBarFillRenderer.transform.localPosition = new Vector3((fillWidth - fullWidth) * 0.5f, 0f, -0.01f);
      }
    }

    private void UpdateHoverState()
    {
      var previousHover = _hoveredBlock;
      _hoverUsesSoftLock = false;
      _lastSoftLockAngle = 999f;
      _hoveredBlock = FindHoveredOrbitingBlock(realGazeScreenPosition);

      if (previousHover != null && previousHover != _hoveredBlock)
      {
        previousHover.IsHovered = false;
      }

      if (_hoveredBlock != null)
      {
        _hoveredBlock.IsHovered = true;
        _lastHoveredBlock = _hoveredBlock;
        _lastHoveredAt = Time.time;
      }
    }

    private OrbitBlock FindHoveredOrbitingBlock(Vector2 gazeScreenPosition)
    {
      var directBlock = FindDirectHoveredOrbitingBlock(gazeScreenPosition);
      if (directBlock != null)
      {
        return directBlock;
      }

      return _useEdgeDirectionSoftLock ? FindSoftLockedOrbitingBlock(gazeScreenPosition) : null;
    }

    private OrbitBlock FindDirectHoveredOrbitingBlock(Vector2 gazeScreenPosition)
    {
      OrbitBlock bestBlock = null;
      var bestDistance = float.PositiveInfinity;

      for (var i = 0; i < _blocks.Count; i++)
      {
        var block = _blocks[i];
        if (!IsActiveTargetBlock(block))
        {
          continue;
        }

        if (!TryGetScreenRect(block, out var rect))
        {
          continue;
        }

        var paddedRect = PadRect(rect, _gazePaddingPixels);
        if (!paddedRect.Contains(gazeScreenPosition))
        {
          continue;
        }

        var distance = Vector2.Distance(gazeScreenPosition, rect.center);
        if (distance < bestDistance)
        {
          bestDistance = distance;
          bestBlock = block;
        }
      }

      return bestBlock;
    }

    private OrbitBlock FindSoftLockedOrbitingBlock(Vector2 gazeScreenPosition)
    {
      var screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
      var gazeDirection = gazeScreenPosition - screenCenter;
      if (gazeDirection.magnitude < _softLockMinGazeDistancePixels)
      {
        return null;
      }

      var intendedSide = GetHorizontalIntent(gazeScreenPosition);
      _lastHorizontalIntent = intendedSide;
      gazeDirection.Normalize();
      var preferredSideBlock = FindSoftLockedOrbitingBlockOnSide(screenCenter, gazeDirection, intendedSide, true);
      if (preferredSideBlock != null)
      {
        return preferredSideBlock;
      }

      return FindSoftLockedOrbitingBlockOnSide(screenCenter, gazeDirection, 0f, false);
    }

    private OrbitBlock FindSoftLockedOrbitingBlockOnSide(Vector2 screenCenter, Vector2 gazeDirection, float intendedSide, bool requireSameSide)
    {
      if (requireSameSide && (!_preferSameSideWhenIntentIsClear || intendedSide == 0f))
      {
        return null;
      }

      OrbitBlock bestBlock = null;
      var bestScore = float.PositiveInfinity;
      var bestAngle = 999f;

      for (var i = 0; i < _blocks.Count; i++)
      {
        var block = _blocks[i];
        if (!IsActiveTargetBlock(block))
        {
          continue;
        }

        if (!TryGetScreenRect(block, out var rect))
        {
          continue;
        }

        var blockDirection = rect.center - screenCenter;
        if (blockDirection.sqrMagnitude < 1f)
        {
          continue;
        }

        var blockDistance = blockDirection.magnitude;
        blockDirection.Normalize();
        var blockSide = Mathf.Sign(rect.center.x - screenCenter.x);
        if (requireSameSide && blockSide != intendedSide)
        {
          continue;
        }

        var angle = Vector2.Angle(gazeDirection, blockDirection);
        var maxAllowedAngle = blockSide < 0f
          ? _softLockMaxAngleDegrees + _leftSoftLockExtraAngleDegrees
          : _softLockMaxAngleDegrees;
        if (angle > maxAllowedAngle)
        {
          continue;
        }

        // Prefer direction first, then gently prefer closer edge blocks.
        var sideBonus = intendedSide != 0f && blockSide == intendedSide ? _sideIntentScoreBonus : 0f;
        var score = angle + blockDistance * 0.002f - sideBonus;
        if (score < bestScore)
        {
          bestScore = score;
          bestAngle = angle;
          bestBlock = block;
        }
      }

      if (bestBlock != null)
      {
        _hoverUsesSoftLock = true;
        _lastSoftLockAngle = bestAngle;
      }

      return bestBlock;
    }

    private bool IsActiveTargetBlock(OrbitBlock block)
    {
      return block != null && (block.State == BlockState.Orbiting || block.State == BlockState.Crisis);
    }

    private float GetHorizontalIntent(Vector2 gazeScreenPosition)
    {
      var deltaX = gazeScreenPosition.x - Screen.width * 0.5f;
      var deadZone = deltaX < 0f ? _leftIntentDeadZonePixels : _sideIntentDeadZonePixels;
      if (Mathf.Abs(deltaX) < deadZone)
      {
        return 0f;
      }

      return Mathf.Sign(deltaX);
    }

    private string FormatHorizontalIntent(float intent)
    {
      if (intent < 0f)
      {
        return "left";
      }

      if (intent > 0f)
      {
        return "right";
      }

      return "center";
    }

    private bool TryGetScreenRect(OrbitBlock block, out Rect rect)
    {
      var bounds = block.Renderer.bounds;
      var min = _camera.WorldToScreenPoint(bounds.min);
      var max = _camera.WorldToScreenPoint(bounds.max);

      if (min.z < 0f || max.z < 0f)
      {
        rect = default;
        return false;
      }

      var xMin = Mathf.Min(min.x, max.x);
      var xMax = Mathf.Max(min.x, max.x);
      var yMin = Mathf.Min(min.y, max.y);
      var yMax = Mathf.Max(min.y, max.y);
      rect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
      return true;
    }

    private Rect PadRect(Rect rect, float padding)
    {
      rect.xMin -= padding;
      rect.xMax += padding;
      rect.yMin -= padding;
      rect.yMax += padding;
      return rect;
    }

    private void ConsumeBlinkForHarvest()
    {
      if (_calibrationActive)
      {
        return;
      }

      if (_gameplayState == GameplayState.EyesClosedFreeze)
      {
        _blinkQueued = false;
        return;
      }

      if (!_blinkQueued)
      {
        return;
      }

      _blinkQueued = false;
      var block = GetBlinkHarvestTarget();

      if (block == null)
      {
        return;
      }

      if (_hoveredBlock == block)
      {
        _hoveredBlock = null;
      }

      if (_lastHoveredBlock == block)
      {
        _lastHoveredBlock = null;
      }

      StartCoroutine(HarvestRoutine(block));
      _blinkCaptureCount++;
    }

    private OrbitBlock GetBlinkHarvestTarget()
    {
      if (IsActiveTargetBlock(_hoveredBlock))
      {
        return _hoveredBlock;
      }

      if (_lastHoveredBlock != null &&
          IsActiveTargetBlock(_lastHoveredBlock) &&
          Time.time - _lastHoveredAt <= _hoverGraceSeconds)
      {
        return _lastHoveredBlock;
      }

      return null;
    }

    private bool SoftFadeOldestOrbitingBlock()
    {
      OrbitBlock oldest = null;
      for (var i = 0; i < _blocks.Count; i++)
      {
        var block = _blocks[i];
        if (block.State != BlockState.Orbiting)
        {
          continue;
        }

        if (oldest == null || block.CreatedAt < oldest.CreatedAt)
        {
          oldest = block;
        }
      }

      if (oldest == null)
      {
        return false;
      }

      StartCoroutine(FadeOutRoutine(oldest));
      return true;
    }

    private IEnumerator FadeOutRoutine(OrbitBlock block)
    {
      block.State = BlockState.FadingOut;
      block.IsHovered = false;

      if (_hoveredBlock == block)
      {
        _hoveredBlock = null;
      }

      var startColor = block.Renderer.color;
      var startScale = block.Transform.localScale;
      var elapsed = 0f;

      while (elapsed < _fadeOutSeconds && block.GameObject != null)
      {
        elapsed += Time.deltaTime;
        var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _fadeOutSeconds));
        var color = Color.Lerp(startColor, new Color(startColor.r, startColor.g, startColor.b, 0f), t);
        block.Renderer.color = color;
        block.Transform.localScale = Vector3.Lerp(startScale, startScale * 0.82f, t);
        yield return null;
      }

      if (block.GameObject != null)
      {
        Destroy(block.GameObject);
      }
    }

    private IEnumerator HarvestRoutine(OrbitBlock block)
    {
      block.State = BlockState.Converted;
      block.IsHovered = false;
      block.Renderer.sortingOrder = 12;
      _harvestedCount++;

      var startColor = block.Renderer.color;
      var startScale = block.Transform.localScale;
      var startRotation = block.Transform.rotation;
      var targetScale = block.BaseScale * _harvestScaleRatio;
      var elapsed = 0f;

      while (elapsed < _harvestSeconds && block.GameObject != null)
      {
        elapsed += Time.deltaTime;
        var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _harvestSeconds));
        block.Renderer.color = Color.Lerp(startColor, ConvertedColor, t);
        block.Transform.localScale = Vector3.Lerp(startScale, targetScale, t);
        block.Transform.rotation = Quaternion.Lerp(startRotation, Quaternion.identity, t);
        yield return null;
      }

      if (block.GameObject != null)
      {
        block.Renderer.color = ConvertedColor;
        block.Transform.localScale = targetScale;
        block.Transform.rotation = Quaternion.identity;
      }
    }

    private Vector2 GetEffectiveGazeScreenPositionForBlink()
    {
      return ApplyGazeCalibration(GetStableRawGazeBeforeBlink());
    }

    private void UpdateGazeIndicator()
    {
      if (_gazeIndicatorRoot == null)
      {
        return;
      }

      var hardwareActive = (!_autoReadKeepBlinkingEyeInput || EyeInputDebugState.Latest.FaceDetected) && !_calibrationActive;
      if (_gazeIndicatorRoot.activeSelf != hardwareActive)
      {
        _gazeIndicatorRoot.SetActive(hardwareActive);
      }

      if (!hardwareActive)
      {
        return;
      }

      var targetPosition = _camera.ScreenToWorldPoint(new Vector3(
        realGazeScreenPosition.x,
        realGazeScreenPosition.y,
        _blockDepthFromCamera));

      _gazeIndicatorRoot.transform.position = Vector3.Lerp(
        _gazeIndicatorRoot.transform.position,
        targetPosition,
        1f - Mathf.Exp(-18f * Time.deltaTime));

      var blinkPulse = Mathf.Clamp01(1f - (Time.time - _lastBlinkVisualAt) / 0.22f);
      var hoverPulse = _hoveredBlock == null ? 0f : (Mathf.Sin(Time.time * 8f) + 1f) * 0.5f;
      var targetScale = 1f + blinkPulse * 0.45f + hoverPulse * 0.08f;
      _gazeIndicatorRoot.transform.localScale = Vector3.Lerp(
        _gazeIndicatorRoot.transform.localScale,
        Vector3.one * targetScale,
        Time.deltaTime * 12f);

      var targetColor = _hoveredBlock == null ? GazeIdleColor : GazeHoverColor;
      for (var i = 0; i < _gazeIndicatorPieces.Count; i++)
      {
        var piece = _gazeIndicatorPieces[i];
        if (piece == null)
        {
          continue;
        }

        var color = targetColor;
        if (piece.gameObject.name.Contains("Halo"))
        {
          color.a *= 0.25f;
        }

        piece.color = Color.Lerp(piece.color, color, Time.deltaTime * 10f);
      }
    }

    private int CountState(BlockState state)
    {
      var count = 0;
      for (var i = 0; i < _blocks.Count; i++)
      {
        if (_blocks[i] != null && _blocks[i].State == state)
        {
          count++;
        }
      }

      return count;
    }

    private void RemoveDeadBlocks()
    {
      for (var i = _blocks.Count - 1; i >= 0; i--)
      {
        if (_blocks[i] == null || _blocks[i].GameObject == null)
        {
          _blocks.RemoveAt(i);
        }
      }

      if (_hoveredBlock != null && _hoveredBlock.GameObject == null)
      {
        _hoveredBlock = null;
      }

      if (_lastHoveredBlock != null && _lastHoveredBlock.GameObject == null)
      {
        _lastHoveredBlock = null;
      }

    }

    private void EnsureHudStyle()
    {
      if (_hudStyle != null)
      {
        return;
      }

      _hudStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = 18,
        normal = { textColor = new Color(0.68f, 1f, 0.78f, 1f) },
      };
    }

    private void EnsureInstructionStyles()
    {
      if (_instructionTitleStyle != null && _instructionBodyStyle != null)
      {
        return;
      }

      _instructionTitleStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = 28,
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.MiddleCenter,
        normal = { textColor = new Color(0.72f, 1f, 0.82f, 1f) },
      };

      _instructionBodyStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = 20,
        alignment = TextAnchor.MiddleCenter,
        wordWrap = true,
        normal = { textColor = new Color(0.86f, 1f, 0.92f, 0.95f) },
      };
    }

    private void EnsurePresentationStyles()
    {
      if (_warningTitleStyle != null &&
          _warningBodyStyle != null &&
          _tutorialTitleStyle != null &&
          _tutorialBodyStyle != null &&
          _reportTitleStyle != null &&
          _reportBodyStyle != null &&
          _reportMetricStyle != null)
      {
        return;
      }

      _warningTitleStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = 24,
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.MiddleCenter,
        normal = { textColor = new Color(1f, 0.72f, 0.58f, 1f) },
      };

      _warningBodyStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = 18,
        alignment = TextAnchor.MiddleCenter,
        wordWrap = true,
        normal = { textColor = new Color(1f, 0.9f, 0.82f, 0.96f) },
      };

      _tutorialTitleStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = 25,
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.MiddleCenter,
        normal = { textColor = new Color(0.72f, 1f, 0.82f, 1f) },
      };

      _tutorialBodyStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = 19,
        alignment = TextAnchor.MiddleCenter,
        wordWrap = true,
        normal = { textColor = new Color(0.9f, 1f, 0.94f, 0.96f) },
      };

      _reportTitleStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = 34,
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.MiddleCenter,
        normal = { textColor = new Color(0.72f, 1f, 0.82f, 1f) },
      };

      _reportBodyStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = 19,
        alignment = TextAnchor.MiddleCenter,
        wordWrap = true,
        normal = { textColor = new Color(0.88f, 1f, 0.93f, 0.95f) },
      };

      _reportMetricStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = 32,
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.MiddleLeft,
        normal = { textColor = new Color(0.3f, 1f, 0.62f, 1f) },
      };
    }

    private void OnDestroy()
    {
      if (_squareSprite != null)
      {
        Destroy(_squareSprite);
      }

      if (_squareTexture != null)
      {
        Destroy(_squareTexture);
      }

      if (_circleSprite != null)
      {
        Destroy(_circleSprite);
      }

      if (_circleTexture != null)
      {
        Destroy(_circleTexture);
      }

      DestroyRuntimeClip(_freezeStartedClip);
      DestroyRuntimeClip(_coverageCompleteClip);
      DestroyRuntimeClip(_freezeFailedClip);
      DestroyRuntimeClip(_freezeClearedClip);
      ClearModuleCards();
    }

    private void DestroyRuntimeClip(AudioClip clip)
    {
      if (clip != null)
      {
        Destroy(clip);
      }
    }

    private class OrbitBlock
    {
      public readonly GameObject GameObject;
      public readonly Transform Transform;
      public readonly SpriteRenderer Renderer;
      public readonly int Serial;
      public readonly float CreatedAt;
      public readonly string Name;
      public readonly Vector3 BaseScale;

      public BlockState State;
      public bool IsHovered;
      public float Phase;
      public float AngularSpeed;
      public float CrisisMoveSpeed;

      public OrbitBlock(GameObject gameObject, SpriteRenderer renderer, float phase, float angularSpeed, int serial, float createdAt, Vector3 baseScale)
      {
        GameObject = gameObject;
        Transform = gameObject.transform;
        Renderer = renderer;
        Phase = phase;
        AngularSpeed = angularSpeed;
        Serial = serial;
        CreatedAt = createdAt;
        Name = gameObject.name;
        BaseScale = baseScale;
        State = BlockState.Orbiting;
      }
    }

    private readonly struct ModuleCard
    {
      public readonly GameObject GameObject;
      public readonly SpriteRenderer Renderer;
      public readonly int Index;

      public ModuleCard(GameObject gameObject, SpriteRenderer renderer, int index)
      {
        GameObject = gameObject;
        Renderer = renderer;
        Index = index;
      }
    }

    private readonly struct TimedGazeSample
    {
      public readonly Vector2 Position;
      public readonly float Time;

      public TimedGazeSample(Vector2 position, float time)
      {
        Position = position;
        Time = time;
      }
    }

    private readonly struct TutorialStep
    {
      public readonly string Title;
      public readonly string Body;

      public TutorialStep(string title, string body)
      {
        Title = title;
        Body = body;
      }
    }
  }
}
