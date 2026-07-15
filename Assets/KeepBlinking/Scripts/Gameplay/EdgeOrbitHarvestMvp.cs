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
    [SerializeField] private float _portraitAspect = 1170f / 2532f;
    [SerializeField] private float _orthographicSize = 8f;
    [SerializeField] private float _blockDepthFromCamera = 10f;

    [Header("Orbit Spawn")]
    [SerializeField] private float _minSpawnIntervalSeconds = 2f;
    [SerializeField] private float _maxSpawnIntervalSeconds = 3f;
    [SerializeField] private int _maxOrbitingBlocks = 4;
    [SerializeField] private float _edgeInsetViewport = 0.09f;
    [SerializeField] private Vector2 _blockWorldSizeRange = new Vector2(0.76f, 1.02f);
    [SerializeField] private Vector2 _orbitAngularSpeedRange = new Vector2(0.07f, 0.13f);

    [Header("Crisis & Eye Close Break")]
    [SerializeField] private float _minCrisisIntervalSeconds = 15f;
    [SerializeField] private float _maxCrisisIntervalSeconds = 20f;
    [SerializeField] private int _crisisSpawnCount = 8;
    [SerializeField] private float _crisisSpawnPaddingViewport = 0.16f;
    [SerializeField] private Vector2 _crisisBlockWorldSizeRange = new Vector2(0.76f, 1.02f);
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
    [SerializeField] private float _harvestScaleRatio = 0.52f;
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
    [SerializeField] private float _calibrationTargetWorldSize = 0.78f;
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
    [SerializeField] private float _openingGuideSeconds = 25f;
    [SerializeField] private bool _pauseGameplayDuringOpeningGuide = true;
    [SerializeField] private bool _showAmbientInstructionOverlay;
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
    [SerializeField] private float _progressBarHeightWorld = 0.42f;
    [SerializeField] private float _progressBarBottomViewport = 0.16f;
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
    private Sprite _roundedFillSprite;
    private Sprite _roundedBorderSprite;
    private Sprite _backgroundSprite;
    private Texture2D _squareTexture;
    private Texture2D _circleTexture;
    private Texture2D _roundedFillTexture;
    private Texture2D _roundedBorderTexture;
    private Texture2D _backgroundTexture;
    private GameObject _gazeIndicatorRoot;
    private GameObject _playerMarkerRoot;
    private GameObject _calibrationTargetRoot;
    private GameObject _backgroundRoot;
    private GameObject _blackoutRoot;
    private SpriteRenderer _blackoutRenderer;
    private SpriteRenderer _backgroundRenderer;
    private GameObject _purificationWaveRoot;
    private SpriteRenderer _purificationWaveRenderer;
    private GameObject _progressBarRoot;
    private SpriteRenderer _progressBarGlowRenderer;
    private SpriteRenderer _progressBarBackRenderer;
    private SpriteRenderer _progressBarFillRenderer;
    private SpriteRenderer _progressBarBorderRenderer;
    private AudioSource _feedbackAudioSource;
    private AudioClip _freezeStartedClip;
    private AudioClip _coverageCompleteClip;
    private AudioClip _freezeInterruptedClip;
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
    private GUIStyle _reportLabelStyle;
    private GUIStyle _reportMetricStyle;
    private GUIStyle _cardTagStyle;
    private GUIStyle _cardTitleStyle;
    private GUIStyle _cardBodyStyle;
    private GUIStyle _cardDeltaStyle;
    private GUIStyle _cardLevelStyle;
    private float _instructionStyleScale = -1f;
    private float _presentationStyleScale = -1f;
    private float _sessionStartedAt = -1f;
    private float _openingGuideStartedAt = -1f;
    private bool _openingGuideComplete;
    private bool _gameFlowStarted;
    private bool _sessionEnded;
    private int _sessionBlinkCount;
    private int _blinkCaptureCount;
    private int _eyeRestBreakCount;
    private int _distanceSwitchCount;
    private int _moduleChoiceCount;
    private int _protocolDay = 1;
    private int _totalSamplesCollected;
    private int _crisisClearCount;
    private float _totalClosedEyeRestSeconds;
    private float _longestContinuousObservationSeconds;
    private float _continuousObservationStartedAt = -1f;
    private float _blackoutVisualAlpha;
    private float _blackoutTargetAlpha;
    private float _blackoutFadeReleaseUntil = -1f;
    private float _reopenWaveReleaseUntil = -1f;

    private const string ProtocolDayPrefsKey = "KeepBlinking.ProtocolDay";

    private static readonly Color OrbitColor = KeepBlinkingTheme.OrbitSignal;
    private static readonly Color HoverColor = KeepBlinkingTheme.OrbitSignalHover;
    private static readonly Color ConvertedColor = KeepBlinkingTheme.ConvertedSignal;
    private static readonly Color GazeIdleColor = KeepBlinkingTheme.GazeIdle;
    private static readonly Color GazeHoverColor = KeepBlinkingTheme.GazeHover;
    private static readonly Color CalibrationColor = KeepBlinkingTheme.CalibrationSignal;
    private static readonly Color CrisisColor = KeepBlinkingTheme.CrisisSignal;
    private static readonly Color ProgressBackColor = KeepBlinkingTheme.ProgressBack;
    private static readonly Color ProgressFillColor = KeepBlinkingTheme.ProgressFill;

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
      _protocolDay = Mathf.Clamp(PlayerPrefs.GetInt(ProtocolDayPrefsKey, 1), 1, 14);
      EnsureCamera();
      CreateRuntimeSprite();
      CreateRuntimeCircleSprite();
      CreateRuntimeUiSprites();
      CreateBackgroundVisual();
      CreateGazeIndicator();
      CreatePlayerMarker();
      CreateCalibrationTarget();
      CreateBlackoutOverlay();
      CreatePurificationWave();
      CreateProgressBar();
      CreateFreezeFeedbackAudio();
      realGazeScreenPosition = GetSafeInitialGazePosition();
      _rawGazeScreenPosition = realGazeScreenPosition;
      WarnIfEyeHardwareMissing();

      if (_showOpeningGuide)
      {
        BeginOpeningGuide();
      }
      else
      {
        BeginPlayAfterOpeningGuide();
      }
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
      UpdateOpeningGuideLifecycle();
      if (IsOpeningGuideActive() && _pauseGameplayDuringOpeningGuide)
      {
        UpdatePlayerMarker();
        UpdateGazeIndicator();
        UpdateBlackoutOverlay();
        UpdateObservationMetrics();
        return;
      }

      if (_gameplayState == GameplayState.SessionReport)
      {
        UpdatePlayerMarker();
        UpdateBlackoutOverlay();
        UpdateObservationMetrics();
        return;
      }

      if (UpdateCalibration())
      {
        UpdatePlayerMarker();
        UpdateGazeIndicator();
        UpdateObservationMetrics();
        return;
      }

      RemoveDeadBlocks();
      UpdateFaceDistanceFromPlugin();
      UpdateProgressBarVisual();
      if (_gameplayState == GameplayState.ModuleUpgrade)
      {
        UpdateModuleUpgradeSelection();
        UpdateModuleCardVisuals();
        UpdatePlayerMarker();
        UpdateGazeIndicator();
        UpdateObservationMetrics();
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
      UpdateModuleCardVisuals();
      UpdateSessionTimer();
      UpdateObservationMetrics();
    }

    private void OnGUI()
    {
      EnsureHudStyle();
      DrawHardwareWarningOverlay();
      DrawOpeningGuideOverlay();
      DrawModuleCardLabels();
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

    private void BeginOpeningGuide()
    {
      _openingGuideStartedAt = Time.time;
      _openingGuideComplete = false;
      _sessionStartedAt = -1f;
      SetProgressBarVisible(false);

      if (!_pauseGameplayDuringOpeningGuide)
      {
        BeginGameFlow();
      }
    }

    private void BeginPlayAfterOpeningGuide()
    {
      _openingGuideComplete = true;
      _openingGuideStartedAt = -1f;
      BeginGameFlow();
    }

    private void BeginGameFlow()
    {
      if (_gameFlowStarted)
      {
        return;
      }

      _gameFlowStarted = true;
      SetProgressBarVisible(true);
      SetupCalibration();

      if (_calibrationActive)
      {
        _nextSpawnAt = float.PositiveInfinity;
        _nextCrisisAt = float.PositiveInfinity;
        return;
      }

      _sessionStartedAt = Time.time;
      ScheduleNextSpawn(0.35f);
      ScheduleNextCrisis();
    }

    private void UpdateOpeningGuideLifecycle()
    {
      if (!_showOpeningGuide || _openingGuideComplete || _openingGuideStartedAt < 0f)
      {
        return;
      }

      if (Time.time - _openingGuideStartedAt >= _openingGuideSeconds)
      {
        BeginPlayAfterOpeningGuide();
      }
    }

    private bool IsOpeningGuideActive()
    {
      return _showOpeningGuide &&
             !_openingGuideComplete &&
             _openingGuideStartedAt >= 0f &&
             Time.time - _openingGuideStartedAt < _openingGuideSeconds;
    }

    private void SetProgressBarVisible(bool visible)
    {
      if (_progressBarRoot != null && _progressBarRoot.activeSelf != visible)
      {
        _progressBarRoot.SetActive(visible);
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

    private void DrawRoundedPanel(Rect rect, Color fillColor, Color borderColor, Color glowColor, float shadowOffset)
    {
      if (_roundedFillTexture == null || _roundedBorderTexture == null)
      {
        return;
      }

      var shadowRect = new Rect(rect.x, rect.y + shadowOffset, rect.width, rect.height);
      GUI.color = KeepBlinkingTheme.SurfaceShadow;
      GUI.DrawTexture(shadowRect, _roundedFillTexture, ScaleMode.StretchToFill, true);

      var glowRect = ExpandRect(rect, 8f);
      GUI.color = glowColor;
      GUI.DrawTexture(glowRect, _roundedFillTexture, ScaleMode.StretchToFill, true);

      GUI.color = fillColor;
      GUI.DrawTexture(rect, _roundedFillTexture, ScaleMode.StretchToFill, true);

      GUI.color = borderColor;
      GUI.DrawTexture(rect, _roundedBorderTexture, ScaleMode.StretchToFill, true);
      GUI.color = Color.white;
    }

    private void DrawBracketFrame(Rect rect, Color color, float thickness, float bracketLength)
    {
      DrawRect(new Rect(rect.xMin, rect.yMin, bracketLength, thickness), color);
      DrawRect(new Rect(rect.xMin, rect.yMin, thickness, bracketLength), color);
      DrawRect(new Rect(rect.xMax - bracketLength, rect.yMin, bracketLength, thickness), color);
      DrawRect(new Rect(rect.xMax - thickness, rect.yMin, thickness, bracketLength), color);
      DrawRect(new Rect(rect.xMin, rect.yMax - thickness, bracketLength, thickness), color);
      DrawRect(new Rect(rect.xMin, rect.yMax - bracketLength, thickness, bracketLength), color);
      DrawRect(new Rect(rect.xMax - bracketLength, rect.yMax - thickness, bracketLength, thickness), color);
      DrawRect(new Rect(rect.xMax - thickness, rect.yMax - bracketLength, thickness, bracketLength), color);
    }

    private void DrawRect(Rect rect, Color color)
    {
      if (_squareTexture == null)
      {
        return;
      }

      GUI.color = color;
      GUI.DrawTexture(rect, _squareTexture, ScaleMode.StretchToFill, true);
      GUI.color = Color.white;
    }

    private Rect ExpandRect(Rect rect, float amount)
    {
      return new Rect(rect.x - amount, rect.y - amount, rect.width + amount * 2f, rect.height + amount * 2f);
    }

    private bool IsNarrowPortraitLayout()
    {
      return Screen.width / Mathf.Max(1f, (float)Screen.height) < 0.58f;
    }

    private Vector2 GetCurrentModuleCardWorldSize()
    {
      return IsNarrowPortraitLayout()
        ? new Vector2(4.75f, 2.05f)
        : _moduleCardSize;
    }

    private bool TryGetModuleCardScreenRect(ModuleCard card, out Rect rect)
    {
      rect = default;
      if (_camera == null || card.GameObject == null)
      {
        return false;
      }

      var cardSize = new Vector2(card.GameObject.transform.localScale.x, card.GameObject.transform.localScale.y);
      var center = card.GameObject.transform.position;
      var min = _camera.WorldToScreenPoint(new Vector3(center.x - cardSize.x * 0.5f, center.y - cardSize.y * 0.5f, center.z));
      var max = _camera.WorldToScreenPoint(new Vector3(center.x + cardSize.x * 0.5f, center.y + cardSize.y * 0.5f, center.z));
      if (min.z < 0f || max.z < 0f)
      {
        return false;
      }

      var xMin = Mathf.Min(min.x, max.x);
      var xMax = Mathf.Max(min.x, max.x);
      var yMin = Screen.height - Mathf.Max(min.y, max.y);
      var yMax = Screen.height - Mathf.Min(min.y, max.y);
      rect = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
      return true;
    }

    private KeepBlinkingTheme.ModuleProtocol GetModuleProtocolForCard(int cardIndex)
    {
      var protocols = KeepBlinkingTheme.ModuleProtocols;
      if (protocols == null || protocols.Length == 0)
      {
        return default;
      }

      var unlockDay = GetCurrentModuleUnlockDay();
      var unlockedCount = CountUnlockedModuleProtocols(protocols, unlockDay);
      if (unlockedCount <= 0)
      {
        return protocols[Mathf.Abs(cardIndex) % protocols.Length];
      }

      var targetIndex = PositiveModulo(_moduleChoiceCount * 3 + cardIndex, unlockedCount);
      var seen = 0;
      for (var i = 0; i < protocols.Length; i++)
      {
        if (protocols[i].UnlockDay > unlockDay)
        {
          continue;
        }

        if (seen == targetIndex)
        {
          return protocols[i];
        }

        seen++;
      }

      return protocols[0];
    }

    private int GetCurrentModuleUnlockDay()
    {
      var sessionPreviewBoost = Mathf.FloorToInt(_moduleChoiceCount / 2f);
      return Mathf.Clamp(_protocolDay + sessionPreviewBoost, 1, 14);
    }

    private int CountUnlockedModuleProtocols(KeepBlinkingTheme.ModuleProtocol[] protocols, int unlockDay)
    {
      var count = 0;
      for (var i = 0; i < protocols.Length; i++)
      {
        if (protocols[i].UnlockDay <= unlockDay)
        {
          count++;
        }
      }

      return count;
    }

    private int PositiveModulo(int value, int modulo)
    {
      if (modulo <= 0)
      {
        return 0;
      }

      var result = value % modulo;
      return result < 0 ? result + modulo : result;
    }

    private string FormatDuration(float seconds)
    {
      seconds = Mathf.Max(0f, seconds);
      if (seconds < 60f)
      {
        return $"{seconds:F1}s";
      }

      var minutes = Mathf.FloorToInt(seconds / 60f);
      var remainder = seconds - minutes * 60f;
      return $"{minutes}m {remainder:F0}s";
    }

    private void DrawDistanceSamplingFrame()
    {
      if (!HasCollectableSamples() && CountState(BlockState.Collecting) == 0)
      {
        return;
      }

      var safeRect = GetSafeAreaScreenRect(18f * GetMobileUiScale());
      var frameWidth = safeRect.width * (IsNarrowPortraitLayout() ? 0.74f : 0.62f);
      var frameHeight = safeRect.height * (IsNarrowPortraitLayout() ? 0.34f : 0.42f);
      var centerY = safeRect.y + safeRect.height * 0.4f;
      var rect = new Rect(
        safeRect.center.x - frameWidth * 0.5f,
        centerY - frameHeight * 0.5f,
        frameWidth,
        frameHeight);

      var pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 1.7f);
      var bracketColor = _pushAwayReady
        ? KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentWarm, 0.56f + pulse * 0.16f)
        : KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderReadable, 0.42f + pulse * 0.08f);
      DrawBracketFrame(rect, bracketColor, 3f * GetMobileUiScale(), 28f * GetMobileUiScale());
    }

    private void DrawInstructionOverlay()
    {
      if (_gameplayState == GameplayState.SessionReport ||
          IsOpeningGuideActive() ||
          !ShouldShowInstructionOverlay())
      {
        return;
      }

      EnsureInstructionStyles();
      DrawDistanceSamplingFrame();
      var title = GetInstructionTitle();
      var body = GetInstructionBody();
      if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(body))
      {
        return;
      }

      var scale = GetMobileUiScale();
      var safeRect = GetSafeAreaScreenRect(14f * scale);
      var width = Mathf.Min(safeRect.width * 0.88f, 620f * scale);
      var height = _gameplayState == GameplayState.ModuleUpgrade
        ? 152f * scale
        : (HasCollectableSamples() || _gameplayState == GameplayState.EyesClosedFreeze ? 124f * scale : 112f * scale);
      var rect = new Rect(
        safeRect.center.x - width * 0.5f,
        safeRect.yMax - height - 24f * scale,
        width,
        height);
      var padX = 22f * scale;
      var accent = _gameplayState == GameplayState.Crisis
        ? KeepBlinkingTheme.WarningSoft
        : (_gameplayState == GameplayState.EyesClosedFreeze
          ? KeepBlinkingTheme.AccentSoft
          : (_pushAwayReady ? KeepBlinkingTheme.AccentWarm : KeepBlinkingTheme.AccentPrimary));

      DrawRoundedPanel(rect, KeepBlinkingTheme.SurfaceOverlay, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderReadable, 0.9f), KeepBlinkingTheme.WithAlpha(accent, 0.14f), 7f * scale);
      DrawRect(new Rect(rect.x + padX, rect.y + 16f * scale, rect.width - padX * 2f, 3f * scale), KeepBlinkingTheme.WithAlpha(accent, 0.66f));
      GUI.Label(new Rect(rect.x + padX, rect.y + 30f * scale, rect.width - padX * 2f, 28f * scale), title, _instructionTitleStyle);
      GUI.Label(new Rect(rect.x + padX, rect.y + 60f * scale, rect.width - padX * 2f, rect.height - 76f * scale), body, _instructionBodyStyle);
    }

    private void DrawHardwareWarningOverlay()
    {
      if (!ShouldShowHardwareWarningOverlay() || _gameplayState == GameplayState.SessionReport)
      {
        return;
      }

      EnsurePresentationStyles();
      var scale = GetMobileUiScale();
      var safeRect = GetSafeAreaScreenRect(14f * scale);
      var width = Mathf.Min(safeRect.width * 0.94f, 680f * scale);
      var height = 108f * scale;
      var rect = new Rect(safeRect.center.x - width * 0.5f, safeRect.yMin + 6f * scale, width, height);

      DrawRoundedPanel(rect, KeepBlinkingTheme.SurfaceOverlay, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.WarningSoft, 0.72f), KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.WarningSoft, 0.12f), 6f * scale);
      GUI.Label(new Rect(rect.x + 20f * scale, rect.y + 14f * scale, rect.width - 40f * scale, 30f * scale), "Observation Signal Not Ready", _warningTitleStyle);
      GUI.Label(
        new Rect(rect.x + 20f * scale, rect.y + 46f * scale, rect.width - 40f * scale, rect.height - 56f * scale),
        "Adjust the lighting, clear any obstruction, and keep a relaxed face. Observation resumes automatically once your face is detected.",
        _warningBodyStyle);
    }

    private void DrawOpeningGuideOverlay()
    {
      if (!IsOpeningGuideActive() || _gameplayState == GameplayState.SessionReport)
      {
        return;
      }

      EnsurePresentationStyles();
      var elapsed = Mathf.Max(0f, Time.time - _openingGuideStartedAt);
      var step = GetOpeningGuideStep(elapsed);
      var scale = GetMobileUiScale();
      var progress = Mathf.Clamp01(elapsed / Mathf.Max(1f, _openingGuideSeconds));
      var safeRect = GetSafeAreaScreenRect(18f * scale);
      var width = Mathf.Min(safeRect.width * 0.88f, 620f * scale);
      var height = Mathf.Min(safeRect.height * 0.42f, 330f * scale);
      var y = safeRect.y + safeRect.height * 0.23f;
      var rect = new Rect(safeRect.center.x - width * 0.5f, y, width, height);
      var padX = 24f * scale;
      var progressTrack = new Rect(rect.x + padX, rect.y + rect.height - 34f * scale, rect.width - padX * 2f, 5f * scale);

      DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), KeepBlinkingTheme.SurfaceScrim);
      DrawRoundedPanel(rect, KeepBlinkingTheme.SurfaceOverlay, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderReadable, 0.88f), KeepBlinkingTheme.PanelGlow, 8f * scale);
      GUI.Label(new Rect(rect.x + padX, rect.y + 48f * scale, rect.width - padX * 2f, 38f * scale), step.Title, _tutorialTitleStyle);
      GUI.Label(new Rect(rect.x + padX, rect.y + 98f * scale, rect.width - padX * 2f, 112f * scale), step.Body, _tutorialBodyStyle);
      DrawRoundedPanel(ExpandRect(progressTrack, 2f * scale), KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceBase, 0.92f), KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderSubtle, 0.8f), KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.05f), 0f);
      DrawRect(new Rect(progressTrack.x, progressTrack.y, progressTrack.width * progress, progressTrack.height), KeepBlinkingTheme.AccentPrimary);
    }

    private void DrawSessionReportOverlay()
    {
      if (_gameplayState != GameplayState.SessionReport)
      {
        return;
      }

      EnsurePresentationStyles();
      var scale = GetMobileUiScale();
      var safeRect = GetSafeAreaScreenRect(16f * scale);
      var width = Mathf.Min(safeRect.width, 700f * scale);
      var height = Mathf.Min(safeRect.height, 720f * scale);
      var rect = new Rect(safeRect.center.x - width * 0.5f, safeRect.center.y - height * 0.5f, width, height);

      DrawRect(new Rect(0f, 0f, Screen.width, Screen.height), KeepBlinkingTheme.SurfaceScrim);
      DrawRoundedPanel(rect, KeepBlinkingTheme.SurfaceOverlay, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderReadable, 0.92f), KeepBlinkingTheme.PanelGlow, 8f * scale);

      GUI.Label(new Rect(rect.x + 28f * scale, rect.y + 22f * scale, rect.width - 56f * scale, 24f * scale), "Recorded today", _reportLabelStyle);
      GUI.Label(new Rect(rect.x + 28f * scale, rect.y + 48f * scale, rect.width - 56f * scale, 42f * scale), "Daily Observation Report", _reportTitleStyle);
      GUI.Label(
        new Rect(rect.x + 30f * scale, rect.y + 100f * scale, rect.width - 60f * scale, 54f * scale),
        "You completed a gentle screen-gaze interruption ritual today. The system recorded your soft blinks, rest closures, and distance resets.",
        _reportBodyStyle);

      var singleColumn = rect.width < 460f * scale;
      var columns = singleColumn ? 1 : 2;
      var cellGap = 12f * scale;
      var cellWidth = (rect.width - 60f * scale - cellGap * (columns - 1)) / columns;
      var cellHeight = (singleColumn ? 64f : 76f) * scale;
      var metrics = new (string Label, string Value)[]
      {
        ("Soft blink count", _sessionBlinkCount.ToString()),
        ("Closed-eye rest", FormatDuration(_totalClosedEyeRestSeconds)),
        ("Protective interruptions", _crisisClearCount.ToString()),
        ("Pull-away samplings", _distanceSwitchCount.ToString()),
        ("Longest continuous gaze", FormatDuration(_longestContinuousObservationSeconds)),
        ("Samples recorded", _totalSamplesCollected.ToString()),
      };

      var metricStartY = rect.y + 170f * scale;
      for (var i = 0; i < metrics.Length; i++)
      {
        var column = i % columns;
        var row = i / columns;
        var cellX = rect.x + 30f * scale + column * (cellWidth + cellGap);
        var cellY = metricStartY + row * (cellHeight + cellGap);
        DrawReportMetric(new Rect(cellX, cellY, cellWidth, cellHeight), metrics[i].Label, metrics[i].Value);
      }

      GUI.Label(
        new Rect(rect.x + 30f * scale, rect.yMax - 74f * scale, rect.width - 60f * scale, 46f * scale),
        "Hold onto this relaxed state for a moment before jumping back into bright or highly stimulating content.",
        _reportBodyStyle);
    }

    private void DrawReportMetric(Rect rect, string label, string value)
    {
      var scale = GetMobileUiScale();
      DrawRoundedPanel(rect, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceBase, 0.94f), KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderSubtle, 0.88f), KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.06f), 4f);
      GUI.Label(new Rect(rect.x + 16f * scale, rect.y + 12f * scale, rect.width - 32f * scale, 22f * scale), label, _reportLabelStyle);
      GUI.Label(new Rect(rect.x + 16f * scale, rect.y + 30f * scale, rect.width - 32f * scale, rect.height - 34f * scale), value, _reportMetricStyle);
    }

    private void DrawModuleCardLabels()
    {
      if (_gameplayState != GameplayState.ModuleUpgrade || _moduleCards.Count == 0 || _camera == null)
      {
        return;
      }

      EnsurePresentationStyles();
      for (var i = 0; i < _moduleCards.Count; i++)
      {
        var card = _moduleCards[i];
        if (card.GameObject == null)
        {
          continue;
        }

        if (!TryGetModuleCardScreenRect(card, out var rect))
        {
          continue;
        }

        var protocol = GetModuleProtocolForCard(card.Index);
        var scale = GetMobileUiScale();
        var padX = 22f * scale;
        var tagWidth = Mathf.Min(104f * scale, rect.width * 0.34f);
        var title = protocol.TitleEn;
        var tag = protocol.TagEn;
        var delta = protocol.DeltaEn;
        var level = protocol.Rarity;
        var levelWidth = Mathf.Min(82f * scale, rect.width * 0.28f);
        var deltaRect = new Rect(rect.x + padX, rect.y + 80f * scale, rect.width - padX * 2f, 24f * scale);

        DrawRoundedPanel(new Rect(rect.x + padX, rect.y + 16f * scale, tagWidth, 26f * scale), KeepBlinkingTheme.WithAlpha(protocol.AccentColor, 0.16f), KeepBlinkingTheme.WithAlpha(protocol.AccentColor, 0.68f), KeepBlinkingTheme.WithAlpha(protocol.AccentColor, 0.04f), 0f);
        GUI.Label(new Rect(rect.x + padX + 2f * scale, rect.y + 18f * scale, tagWidth - 4f * scale, 20f * scale), tag, _cardTagStyle);
        GUI.Label(new Rect(rect.x + rect.width - levelWidth - padX, rect.y + 18f * scale, levelWidth, 20f * scale), level, _cardLevelStyle);
        GUI.Label(new Rect(rect.x + padX, rect.y + 52f * scale, rect.width - padX * 2f, 28f * scale), title, _cardTitleStyle);
        GUI.Label(deltaRect, delta, _cardDeltaStyle);
      }
    }

    private bool ShouldShowInstructionOverlay()
    {
      if (_calibrationActive ||
          _gameplayState == GameplayState.EyesClosedFreeze ||
          _gameplayState == GameplayState.Crisis ||
          HasCollectableSamples())
      {
        return true;
      }

      if (_showAmbientInstructionOverlay)
      {
        return true;
      }

      return false;
    }

    private string GetInstructionTitle()
    {
      if (_calibrationActive)
      {
        return "Soft Blink";
      }

      switch (_gameplayState)
      {
        case GameplayState.ModuleUpgrade:
          return string.Empty;
        case GameplayState.EyesClosedFreeze:
          return _coverageCuePlayed ? "Ready to Reopen" : "Eyes Closed. Rest Softly";
        case GameplayState.Crisis:
          return "Protective Pause";
      }

      if (CountState(BlockState.Collecting) > 0)
      {
        return string.Empty;
      }

      if (HasCollectableSamples())
      {
        return _pushAwayReady ? "Slowly Move Away" : "Return to Baseline Distance";
      }

      if (_hoveredBlock != null)
      {
        return "Soft Blink";
      }

      return "Observe the Field";
    }

    private string GetInstructionBody()
    {
      if (_calibrationActive)
      {
        return "Look at the node, then blink.";
      }

      switch (_gameplayState)
      {
        case GameplayState.ModuleUpgrade:
          return string.Empty;
        case GameplayState.EyesClosedFreeze:
          return _coverageCuePlayed ? "Open softly when ready." : "No effort needed.";
        case GameplayState.Crisis:
          return "Close your eyes softly for a short rest.";
      }

      if (CountState(BlockState.Collecting) > 0)
      {
        return string.Empty;
      }

      if (HasCollectableSamples())
      {
        return _pushAwayReady ? "Move away gently." : "Return to baseline first.";
      }

      if (_hoveredBlock != null)
      {
        return "Blink once.";
      }

      return "Let your gaze drift.";
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
      var stepSeconds = Mathf.Max(1f, _openingGuideSeconds / 5f);
      var stepIndex = Mathf.Clamp(Mathf.FloorToInt(elapsedSeconds / stepSeconds), 0, 4);
      switch (stepIndex)
      {
        case 0:
          return new TutorialStep(
            "1/5 Gentle Entry",
            "Let your eyes settle. Drift toward a soft edge signal when you are ready.");
        case 1:
          return new TutorialStep(
            "2/5 Soft Blink",
            "When the signal warms, blink once. No force is needed.");
        case 2:
          return new TutorialStep(
            "3/5 Eye Rest",
            "If samples drift inward, close your eyes softly and let the field settle.");
        case 3:
          return new TutorialStep(
            "4/5 Distance Reset",
            "Return to your usual viewing distance first. Then move the device away slowly.");
        default:
          return new TutorialStep(
            "5/5 Begin",
            "Take your time. The guide will finish on its own before calibration starts.");
      }
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
      _camera.backgroundColor = KeepBlinkingTheme.BackgroundPrimary;

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
          var alpha = Mathf.Clamp01(fill * 0.72f + ring * 0.42f);
          _circleTexture.SetPixel(x, y, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, alpha));
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

    private void CreateRuntimeUiSprites()
    {
      const int textureSize = 128;
      _roundedFillTexture = CreateRoundedRectTexture("ObservationRoundedFill", textureSize, textureSize, textureSize * 0.2f, 0f, false);
      _roundedBorderTexture = CreateRoundedRectTexture("ObservationRoundedBorder", textureSize, textureSize, textureSize * 0.2f, textureSize * 0.05f, true);
      _backgroundTexture = CreateBackgroundTexture(384, 768);

      _roundedFillSprite = Sprite.Create(
        _roundedFillTexture,
        new Rect(0f, 0f, textureSize, textureSize),
        new Vector2(0.5f, 0.5f),
        textureSize);
      _roundedFillSprite.name = "ObservationRoundedFillSprite";

      _roundedBorderSprite = Sprite.Create(
        _roundedBorderTexture,
        new Rect(0f, 0f, textureSize, textureSize),
        new Vector2(0.5f, 0.5f),
        textureSize);
      _roundedBorderSprite.name = "ObservationRoundedBorderSprite";

      _backgroundSprite = Sprite.Create(
        _backgroundTexture,
        new Rect(0f, 0f, _backgroundTexture.width, _backgroundTexture.height),
        new Vector2(0.5f, 0.5f),
        _backgroundTexture.width);
      _backgroundSprite.name = "ObservationBackgroundSprite";
    }

    private void CreateBackgroundVisual()
    {
      _backgroundRoot = new GameObject("Observation Background");
      _backgroundRoot.transform.SetParent(transform, false);
      _backgroundRenderer = _backgroundRoot.AddComponent<SpriteRenderer>();
      _backgroundRenderer.sprite = _backgroundSprite;
      _backgroundRenderer.color = Color.white;
      _backgroundRenderer.sortingOrder = -500;
      ResizeBackgroundVisual();
    }

    private void ResizeBackgroundVisual()
    {
      if (_backgroundRoot == null || _backgroundRenderer == null || _camera == null)
      {
        return;
      }

      var safeViewport = GetSafeViewportRect(0f, 0f);
      var center = _camera.ViewportToWorldPoint(new Vector3(safeViewport.center.x, safeViewport.center.y, _blockDepthFromCamera));
      var bottomLeft = _camera.ViewportToWorldPoint(new Vector3(0f, 0f, _blockDepthFromCamera));
      var topRight = _camera.ViewportToWorldPoint(new Vector3(1f, 1f, _blockDepthFromCamera));
      var worldWidth = Mathf.Abs(topRight.x - bottomLeft.x) * 1.08f;
      var worldHeight = Mathf.Abs(topRight.y - bottomLeft.y) * 1.08f;
      var bounds = _backgroundRenderer.sprite.bounds.size;

      _backgroundRoot.transform.position = new Vector3(center.x, center.y, center.z + 1.5f);
      _backgroundRoot.transform.localScale = new Vector3(
        worldWidth / Mathf.Max(0.0001f, bounds.x),
        worldHeight / Mathf.Max(0.0001f, bounds.y),
        1f);
    }

    private Texture2D CreateBackgroundTexture(int width, int height)
    {
      var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
      {
        name = "ObservationBackgroundTexture",
        filterMode = FilterMode.Bilinear,
        wrapMode = TextureWrapMode.Clamp,
      };

      var center = new Vector2(0.5f, 0.57f);
      for (var y = 0; y < height; y++)
      {
        var v = y / Mathf.Max(1f, height - 1f);
        for (var x = 0; x < width; x++)
        {
          var u = x / Mathf.Max(1f, width - 1f);
          var baseColor = Color.Lerp(
            Color.Lerp(KeepBlinkingTheme.BackgroundPrimary, KeepBlinkingTheme.BackgroundSecondary, Mathf.Pow(v, 0.82f)),
            KeepBlinkingTheme.BackgroundTertiary,
            Mathf.Pow(1f - v, 2.1f));

          var vignette = Mathf.SmoothStep(0.1f, 1f, Mathf.Max(Mathf.Abs(u - 0.5f) * 1.6f, Mathf.Abs(v - 0.52f) * 1.25f));
          baseColor = Color.Lerp(baseColor, KeepBlinkingTheme.BackgroundPrimary, vignette * 0.32f);

          var distance = Vector2.Distance(new Vector2(u, v), center);
          var ringA = 1f - Mathf.SmoothStep(0f, 0.018f, Mathf.Abs(distance - 0.22f));
          var ringB = 1f - Mathf.SmoothStep(0f, 0.02f, Mathf.Abs(distance - 0.36f));
          var ringC = 1f - Mathf.SmoothStep(0f, 0.022f, Mathf.Abs(distance - 0.51f));
          var ringMix = Mathf.Clamp01(ringA * 0.7f + ringB * 0.5f + ringC * 0.35f);
          baseColor = Color.Lerp(baseColor, KeepBlinkingTheme.AccentSoft, ringMix * KeepBlinkingTheme.RingTint.a);

          var gridStepX = width / 7f;
          var gridStepY = height / 11f;
          var gridLine = 0f;
          var xMod = Mathf.Abs((x % gridStepX) / gridStepX - 0.5f);
          var yMod = Mathf.Abs((y % gridStepY) / gridStepY - 0.5f);
          if (xMod > 0.47f)
          {
            gridLine += 1f;
          }

          if (yMod > 0.475f)
          {
            gridLine += 1f;
          }

          baseColor = Color.Lerp(baseColor, KeepBlinkingTheme.AccentSoft, Mathf.Clamp01(gridLine) * KeepBlinkingTheme.GridTint.a);

          var dustSeed = Mathf.Abs(Mathf.Sin((x + 11f) * 0.043f + (y + 7f) * 0.019f) * Mathf.Cos((x + 3f) * 0.013f - (y + 17f) * 0.031f));
          if (dustSeed > 0.9965f)
          {
            baseColor = Color.Lerp(baseColor, KeepBlinkingTheme.TextPrimary, 0.1f);
          }

          texture.SetPixel(x, y, baseColor);
        }
      }

      texture.Apply();
      return texture;
    }

    private Texture2D CreateRoundedRectTexture(string textureName, int width, int height, float radius, float borderWidth, bool borderOnly)
    {
      var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
      {
        name = textureName,
        filterMode = FilterMode.Bilinear,
        wrapMode = TextureWrapMode.Clamp,
      };

      var halfWidth = width * 0.5f;
      var halfHeight = height * 0.5f;
      var innerRadius = Mathf.Max(0f, radius - borderWidth);
      for (var y = 0; y < height; y++)
      {
        for (var x = 0; x < width; x++)
        {
          var px = x + 0.5f - halfWidth;
          var py = y + 0.5f - halfHeight;
          var outerDistance = SignedDistanceToRoundedRect(px, py, halfWidth - 1f, halfHeight - 1f, radius);
          var outerAlpha = 1f - Mathf.SmoothStep(-1f, 1.6f, outerDistance);
          var alpha = outerAlpha;

          if (borderOnly)
          {
            var innerDistance = SignedDistanceToRoundedRect(
              px,
              py,
              Mathf.Max(1f, halfWidth - 1f - borderWidth),
              Mathf.Max(1f, halfHeight - 1f - borderWidth),
              innerRadius);
            var innerAlpha = 1f - Mathf.SmoothStep(-1f, 1.6f, innerDistance);
            alpha = Mathf.Clamp01(outerAlpha - innerAlpha);
          }

          texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
        }
      }

      texture.Apply();
      return texture;
    }

    private static float SignedDistanceToRoundedRect(float x, float y, float halfWidth, float halfHeight, float radius)
    {
      var qx = Mathf.Abs(x) - (halfWidth - radius);
      var qy = Mathf.Abs(y) - (halfHeight - radius);
      var ox = Mathf.Max(qx, 0f);
      var oy = Mathf.Max(qy, 0f);
      return Mathf.Sqrt(ox * ox + oy * oy) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
    }

    private void CreateGazeIndicator()
    {
      _gazeIndicatorRoot = new GameObject("Soft Gaze Indicator");
      _gazeIndicatorRoot.transform.SetParent(transform, false);
      _gazeIndicatorRoot.SetActive(false);
    }

    private void CreatePlayerMarker()
    {
      _playerMarkerRoot = new GameObject("Center Player Marker");
      _playerMarkerRoot.transform.SetParent(transform, false);

      var size = _playerMarkerWorldSize;
      CreatePlayerMarkerPiece("Player Halo", Vector3.zero, new Vector3(size * 1.48f, size * 1.48f, 1f), KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.1f), 8, _circleSprite);
      CreatePlayerMarkerPiece("Observation Zone", Vector3.zero, new Vector3(size * 0.94f, size * 0.94f, 1f), KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentSoft, 0.16f), 9, _circleSprite);
      CreatePlayerMarkerPiece("Observation Core", Vector3.zero, new Vector3(size * 0.18f, size * 0.18f, 1f), KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.TextPrimary, 0.92f), 13, _circleSprite);
      CreatePlayerMarkerPiece("Frame Top", new Vector3(0f, size * 0.47f, 0f), new Vector3(size * 0.72f, size * 0.048f, 1f), KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentSoft, 0.74f), 11);
      CreatePlayerMarkerPiece("Frame Bottom", new Vector3(0f, -size * 0.47f, 0f), new Vector3(size * 0.72f, size * 0.048f, 1f), KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentSoft, 0.74f), 11);
      CreatePlayerMarkerPiece("Frame Left", new Vector3(-size * 0.47f, 0f, 0f), new Vector3(size * 0.048f, size * 0.72f, 1f), KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentSoft, 0.74f), 11);
      CreatePlayerMarkerPiece("Frame Right", new Vector3(size * 0.47f, 0f, 0f), new Vector3(size * 0.048f, size * 0.72f, 1f), KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentSoft, 0.74f), 11);
      UpdatePlayerMarker();
    }

    private void CreatePlayerMarkerPiece(string pieceName, Vector3 localPosition, Vector3 localScale, Color color, int sortingOrder, Sprite sprite = null)
    {
      var piece = new GameObject(pieceName);
      piece.transform.SetParent(_playerMarkerRoot.transform, false);
      piece.transform.localPosition = localPosition;
      piece.transform.localScale = localScale;

      var renderer = piece.AddComponent<SpriteRenderer>();
      renderer.sprite = sprite ?? _squareSprite;
      renderer.color = color;
      renderer.sortingOrder = sortingOrder;
      _playerMarkerPieces.Add(renderer);
    }

    private void CreateIndicatorPiece(string pieceName, Vector3 localPosition, Vector3 localScale, Color color, int sortingOrder, Sprite sprite = null)
    {
      var piece = new GameObject(pieceName);
      piece.transform.SetParent(_gazeIndicatorRoot.transform, false);
      piece.transform.localPosition = localPosition;
      piece.transform.localScale = localScale;

      var renderer = piece.AddComponent<SpriteRenderer>();
      renderer.sprite = sprite ?? _squareSprite;
      renderer.color = color;
      renderer.sortingOrder = sortingOrder;
      _gazeIndicatorPieces.Add(renderer);
    }

    private void CreateCalibrationTarget()
    {
      _calibrationTargetRoot = new GameObject("Gaze Calibration Target");
      _calibrationTargetRoot.transform.SetParent(transform, false);

      var size = _calibrationTargetWorldSize;
      CreateCalibrationPiece("Calibration Backplate", Vector3.zero, new Vector3(size * 1.18f, size * 1.18f, 1f), KeepBlinkingTheme.CalibrationBackplate, 102, _roundedFillSprite);
      CreateCalibrationPiece("Calibration Warm Halo", Vector3.zero, new Vector3(size * 1.72f, size * 1.72f, 1f), KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.CalibrationOuter, 0.30f), 103, _circleSprite);
      CreateCalibrationPiece("Calibration Warm Ring", Vector3.zero, new Vector3(size * 1.06f, size * 1.06f, 1f), KeepBlinkingTheme.CalibrationOuter, 104, _circleSprite);
      CreateCalibrationPiece("Calibration Center", Vector3.zero, new Vector3(size * 0.46f, size * 0.46f, 1f), KeepBlinkingTheme.CalibrationCore, 105, _roundedFillSprite);
      CreateCalibrationPiece("Calibration Core Dot", Vector3.zero, new Vector3(size * 0.18f, size * 0.18f, 1f), KeepBlinkingTheme.CalibrationOuter, 106, _circleSprite);
      _calibrationTargetRoot.SetActive(false);
    }

    private void CreateCalibrationPiece(string pieceName, Vector3 localPosition, Vector3 localScale, Color color, int sortingOrder, Sprite sprite = null)
    {
      var piece = new GameObject(pieceName);
      piece.transform.SetParent(_calibrationTargetRoot.transform, false);
      piece.transform.localPosition = localPosition;
      piece.transform.localScale = localScale;

      var renderer = piece.AddComponent<SpriteRenderer>();
      renderer.sprite = sprite ?? _squareSprite;
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
      _blackoutVisualAlpha = 0f;
      _blackoutTargetAlpha = _blackoutAlpha;
      _blackoutRenderer.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BackdropClosedEye, 0f);
      _blackoutRenderer.sortingOrder = 900;
      ResizeBlackoutOverlay();
      _blackoutRoot.SetActive(false);
    }

    private void CreatePurificationWave()
    {
      _purificationWaveRoot = new GameObject("Purification Expanding Wave");
      _purificationWaveRoot.transform.SetParent(transform, false);
      _purificationWaveRenderer = _purificationWaveRoot.AddComponent<SpriteRenderer>();
      _purificationWaveRenderer.sprite = _circleSprite;
      _purificationWaveRenderer.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, _purificationVisualAlpha);
      _purificationWaveRenderer.sortingOrder = 950;
      SetPurificationWaveVisible(false);
    }

    private void CreateProgressBar()
    {
      _progressBarRoot = new GameObject("Reflection Sample Progress Bar");
      _progressBarRoot.transform.SetParent(transform, false);

      var glow = new GameObject("Progress Soft Glow");
      glow.transform.SetParent(_progressBarRoot.transform, false);
      _progressBarGlowRenderer = glow.AddComponent<SpriteRenderer>();
      _progressBarGlowRenderer.sprite = _roundedFillSprite;
      _progressBarGlowRenderer.color = KeepBlinkingTheme.ProgressGlow;
      _progressBarGlowRenderer.sortingOrder = 79;

      var back = new GameObject("Progress Back");
      back.transform.SetParent(_progressBarRoot.transform, false);
      _progressBarBackRenderer = back.AddComponent<SpriteRenderer>();
      _progressBarBackRenderer.sprite = _roundedFillSprite;
      _progressBarBackRenderer.color = ProgressBackColor;
      _progressBarBackRenderer.sortingOrder = 80;

      var fill = new GameObject("Progress Fill");
      fill.transform.SetParent(_progressBarRoot.transform, false);
      _progressBarFillRenderer = fill.AddComponent<SpriteRenderer>();
      _progressBarFillRenderer.sprite = _roundedFillSprite;
      _progressBarFillRenderer.color = ProgressFillColor;
      _progressBarFillRenderer.sortingOrder = 81;

      var border = new GameObject("Progress Readable Border");
      border.transform.SetParent(_progressBarRoot.transform, false);
      _progressBarBorderRenderer = border.AddComponent<SpriteRenderer>();
      _progressBarBorderRenderer.sprite = _roundedBorderSprite;
      _progressBarBorderRenderer.color = KeepBlinkingTheme.AccentSoft;
      _progressBarBorderRenderer.sortingOrder = 82;

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
      _freezeInterruptedClip = CreateToneClip("Freeze Interrupted Tone", 220f, 0.12f);
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
      if (_blackoutRoot == null)
      {
        return;
      }

      if (visible)
      {
        _blackoutTargetAlpha = _blackoutAlpha;
        _blackoutFadeReleaseUntil = -1f;
      }
      else
      {
        if (!_blackoutRoot.activeSelf && _blackoutVisualAlpha <= 0.001f)
        {
          return;
        }

        _blackoutTargetAlpha = 0f;
        _blackoutFadeReleaseUntil = Time.time + 1.45f;
        visible = true;
      }

      if (_blackoutRoot.activeSelf != visible)
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

      ResizeBackgroundVisual();
      ResizeBlackoutOverlay();

      var fadeSpeed = _gameplayState == GameplayState.EyesClosedFreeze ? 4.6f : 1.9f;
      _blackoutVisualAlpha = Mathf.Lerp(_blackoutVisualAlpha, _blackoutTargetAlpha, 1f - Mathf.Exp(-fadeSpeed * Time.deltaTime));
      _blackoutRenderer.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BackdropClosedEye, _blackoutVisualAlpha);

      if (_blackoutTargetAlpha <= 0.001f &&
          _blackoutVisualAlpha <= 0.02f &&
          _blackoutFadeReleaseUntil > 0f &&
          Time.time >= _blackoutFadeReleaseUntil)
      {
        _blackoutRoot.SetActive(false);
        _blackoutFadeReleaseUntil = -1f;
      }
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

      var safeViewport = GetSafeViewportRect(0.04f, 0.06f);
      var center = _camera.ViewportToWorldPoint(new Vector3(safeViewport.center.x, safeViewport.center.y, _blockDepthFromCamera));
      _purificationWaveRoot.transform.position = center;
      var diameter = Mathf.Max(0.001f, _purificationRadius * 2f);
      _purificationWaveRoot.transform.localScale = new Vector3(diameter, diameter, 1f);

      if (_purificationWaveRenderer != null)
      {
        var targetAlpha = _purificationVisualAlpha;
        if (_reopenWaveReleaseUntil > 0f)
        {
          var reopenT = Mathf.Clamp01(1f - (_reopenWaveReleaseUntil - Time.time) / 1.65f);
          diameter = Mathf.Max(diameter, Mathf.Lerp(diameter, GetScreenMaxRadiusFromCenter() * 2.15f, reopenT));
          _purificationWaveRoot.transform.localScale = new Vector3(diameter, diameter, 1f);
          targetAlpha = Mathf.Lerp(_purificationVisualAlpha * 0.92f, 0f, reopenT);
          if (reopenT >= 0.999f)
          {
            _reopenWaveReleaseUntil = -1f;
            SetPurificationWaveVisible(false);
          }
        }

        _purificationWaveRenderer.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, targetAlpha);
      }
    }

    private void ResizeBlackoutOverlay()
    {
      if (_blackoutRoot == null || _camera == null)
      {
        return;
      }

      var safeViewport = GetSafeViewportRect(0f, 0f);
      var center = _camera.ViewportToWorldPoint(new Vector3(safeViewport.center.x, safeViewport.center.y, _blockDepthFromCamera));
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

      var safe = GetSafeAreaScreenRect(0f);
      return new Vector2(safe.center.x, safe.center.y);
    }

    private Rect GetSafeAreaScreenRect(float margin)
    {
      var safeArea = Screen.safeArea;
      return new Rect(
        safeArea.xMin + margin,
        safeArea.yMin + margin,
        Mathf.Max(0f, safeArea.width - margin * 2f),
        Mathf.Max(0f, safeArea.height - margin * 2f));
    }

    private Rect GetSafeViewportRect(float horizontalPaddingViewport, float verticalPaddingViewport)
    {
      var safeArea = Screen.safeArea;
      var xMin = safeArea.xMin / Mathf.Max(1f, Screen.width);
      var xMax = safeArea.xMax / Mathf.Max(1f, Screen.width);
      var yMin = safeArea.yMin / Mathf.Max(1f, Screen.height);
      var yMax = safeArea.yMax / Mathf.Max(1f, Screen.height);

      xMin = Mathf.Clamp01(xMin + horizontalPaddingViewport);
      xMax = Mathf.Clamp01(xMax - horizontalPaddingViewport);
      yMin = Mathf.Clamp01(yMin + verticalPaddingViewport);
      yMax = Mathf.Clamp01(yMax - verticalPaddingViewport);
      return Rect.MinMaxRect(xMin, yMin, Mathf.Max(xMin + 0.02f, xMax), Mathf.Max(yMin + 0.02f, yMax));
    }

    private Rect GetGameplayViewportRect(float horizontalPaddingViewport, float verticalPaddingViewport)
    {
      var safeViewport = GetSafeViewportRect(horizontalPaddingViewport, verticalPaddingViewport);
      var reservedBottom = IsNarrowPortraitLayout() ? 0.31f : 0.24f;
      var yMin = Mathf.Min(safeViewport.yMax - 0.12f, Mathf.Max(safeViewport.yMin, reservedBottom));
      return Rect.MinMaxRect(safeViewport.xMin, yMin, safeViewport.xMax, safeViewport.yMax);
    }

    private Vector2 SafeViewportToScreenPoint(Vector2 viewportPoint)
    {
      var safeViewport = GetSafeViewportRect(0f, 0f);
      return new Vector2(
        Mathf.Lerp(safeViewport.xMin, safeViewport.xMax, viewportPoint.x) * Screen.width,
        Mathf.Lerp(safeViewport.yMin, safeViewport.yMax, viewportPoint.y) * Screen.height);
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
      return SafeViewportToScreenPoint(viewportPoint);
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

      var pulse = 1f + Mathf.Sin(Time.time * 2.0f) * 0.05f;
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
      ScheduleNextCrisis();
      _sessionStartedAt = Time.time;
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
      _continuousObservationStartedAt = -1f;
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
      _totalClosedEyeRestSeconds += closedSeconds;
      SetBlackoutVisible(false);
      _wasEyesClosed = false;
      _eyesClosedStartedAt = -1f;
      _lastFreezeDuration = closedSeconds;
      _lastFreezeResultAt = Time.time;
      var clearedCount = ClearCrisisWithinCurrentRadius();
      var remainingCount = CountState(BlockState.Crisis);
      _purificationRadius = 0f;
      _coverageCuePlayed = false;
      _reopenWaveReleaseUntil = Time.time + 1.65f;

      if (clearedCount > 0)
      {
        _lastFreezeResult = $"CLEARED {clearedCount} by radius";
        PlayFeedbackClip(_freezeClearedClip);
      }
      else
      {
        _lastFreezeResult = "NO COVERAGE: opened too early";
        PlayFeedbackClip(_freezeInterruptedClip);
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
      if (_reopenWaveReleaseUntil <= 0f)
      {
        SetPurificationWaveVisible(false);
      }
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

      var safeViewport = GetSafeViewportRect(0.04f, 0.06f);
      var center = _camera.ViewportToWorldPoint(new Vector3(safeViewport.center.x, safeViewport.center.y, _blockDepthFromCamera));
      var corners = new[]
      {
        _camera.ViewportToWorldPoint(new Vector3(safeViewport.xMin, safeViewport.yMin, _blockDepthFromCamera)),
        _camera.ViewportToWorldPoint(new Vector3(safeViewport.xMin, safeViewport.yMax, _blockDepthFromCamera)),
        _camera.ViewportToWorldPoint(new Vector3(safeViewport.xMax, safeViewport.yMin, _blockDepthFromCamera)),
        _camera.ViewportToWorldPoint(new Vector3(safeViewport.xMax, safeViewport.yMax, _blockDepthFromCamera)),
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

      var safeViewport = GetSafeViewportRect(0.04f, 0.06f);
      var center = _camera.ViewportToWorldPoint(new Vector3(safeViewport.center.x, safeViewport.center.y, _blockDepthFromCamera));
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

    private void UpdateObservationMetrics()
    {
      ResizeBackgroundVisual();
      if (_purificationWaveRoot != null && _purificationWaveRoot.activeSelf)
      {
        UpdatePurificationWaveVisual();
      }

      var isRestState = IsOpeningGuideActive() ||
                        _calibrationActive ||
                        _gameplayState == GameplayState.EyesClosedFreeze ||
                        _gameplayState == GameplayState.ModuleUpgrade ||
                        _gameplayState == GameplayState.SessionReport ||
                        CountState(BlockState.Collecting) > 0;
      if (isRestState)
      {
        _continuousObservationStartedAt = -1f;
        return;
      }

      if (_continuousObservationStartedAt < 0f)
      {
        _continuousObservationStartedAt = Time.time;
      }

      _longestContinuousObservationSeconds = Mathf.Max(
        _longestContinuousObservationSeconds,
        Time.time - _continuousObservationStartedAt);
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
      AdvanceProtocolDayAfterSession();
      ClearModuleCards();
      SetBlackoutVisible(false);
      _reopenWaveReleaseUntil = -1f;
      SetPurificationWaveVisible(false);
      Debug.Log("KeepBlinking MVP session report opened.");
    }

    private void AdvanceProtocolDayAfterSession()
    {
      var nextDay = Mathf.Clamp(_protocolDay + 1, 1, 14);
      if (nextDay <= _protocolDay)
      {
        return;
      }

      _protocolDay = nextDay;
      PlayerPrefs.SetInt(ProtocolDayPrefsKey, _protocolDay);
      PlayerPrefs.Save();
    }

    private void SpawnOrbitBlock()
    {
      var blockObject = new GameObject($"Edge Orbit Block {_spawnSerial + 1}");
      var renderer = blockObject.AddComponent<SpriteRenderer>();
      renderer.sprite = _roundedFillSprite;
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
      renderer.sprite = _roundedFillSprite;
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
      var side = Random.Range(0, 3);
      var playViewport = GetGameplayViewportRect(0.04f, 0.08f);
      var x = Random.Range(playViewport.xMin, playViewport.xMax);
      var y = Random.Range(playViewport.yMin, playViewport.yMax);
      var pad = _crisisSpawnPaddingViewport;

      switch (side)
      {
        case 0:
          return new Vector2(playViewport.xMin - pad, y);
        case 1:
          return new Vector2(playViewport.xMax + pad, y);
        default:
          return new Vector2(x, playViewport.yMax + pad);
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
      _reopenWaveReleaseUntil = -1f;
      SetPurificationWaveVisible(false);
      SetProgressBarVisible(false);
      CreateModuleCards();
    }

    private void CreateModuleCards()
    {
      ClearModuleCards();
      var safeViewport = GetSafeViewportRect(0.1f, 0.12f);
      var center = _camera.ViewportToWorldPoint(new Vector3(safeViewport.center.x, safeViewport.center.y + 0.08f, _blockDepthFromCamera));
      var cardSize = GetCurrentModuleCardWorldSize();
      var isVertical = IsNarrowPortraitLayout();
      var spacing = isVertical ? 0.48f : _moduleCardSpacing;
      var totalWidth = cardSize.x * 3f + spacing * 2f;
      var totalHeight = cardSize.y * 3f + spacing * 2f;
      var startX = center.x - totalWidth * 0.5f + cardSize.x * 0.5f;
      var startY = center.y + totalHeight * 0.5f - cardSize.y * 0.5f;

      for (var i = 0; i < 3; i++)
      {
        var protocol = GetModuleProtocolForCard(i);
        var root = new GameObject($"Module Card {i + 1}");
        root.transform.SetParent(transform, false);
        root.transform.position = isVertical
          ? new Vector3(center.x, startY - i * (cardSize.y + spacing), center.z)
          : new Vector3(startX + i * (cardSize.x + spacing), center.y, center.z);
        root.transform.localScale = new Vector3(cardSize.x, cardSize.y, 1f);

        var renderer = root.AddComponent<SpriteRenderer>();
        renderer.sprite = _roundedFillSprite;
        renderer.color = KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceElevated, 0.98f);
        renderer.sortingOrder = 980;

        var glow = CreateModuleCardPiece(root.transform, "Glow", Vector3.zero, new Vector3(1.08f, 1.08f, 1f), _roundedFillSprite, KeepBlinkingTheme.WithAlpha(protocol.AccentColor, 0.08f), 979);
        var border = CreateModuleCardPiece(root.transform, "Border", Vector3.zero, Vector3.one, _roundedBorderSprite, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderReadable, 0.96f), 981);
        var accentPosition = isVertical ? new Vector3(-0.47f, 0f, 0f) : new Vector3(0f, 0.33f, 0f);
        var accentScale = isVertical ? new Vector3(0.035f, 0.72f, 1f) : new Vector3(0.58f, 0.12f, 1f);
        var accent = CreateModuleCardPiece(root.transform, "Accent", accentPosition, accentScale, _roundedFillSprite, KeepBlinkingTheme.WithAlpha(protocol.AccentColor, 0.22f), 982);
        _moduleCards.Add(new ModuleCard(root, renderer, border, glow, accent, i));
      }
    }

    private SpriteRenderer CreateModuleCardPiece(Transform parent, string pieceName, Vector3 localPosition, Vector3 localScale, Sprite sprite, Color color, int sortingOrder)
    {
      var child = new GameObject(pieceName);
      child.transform.SetParent(parent, false);
      child.transform.localPosition = localPosition;
      child.transform.localScale = localScale;

      var renderer = child.AddComponent<SpriteRenderer>();
      renderer.sprite = sprite;
      renderer.color = color;
      renderer.sortingOrder = sortingOrder;
      return renderer;
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
      var cardSize = new Vector2(card.GameObject.transform.localScale.x, card.GameObject.transform.localScale.y);
      var halfWidth = cardSize.x * 0.5f;
      var halfHeight = cardSize.y * 0.5f;
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
      SetProgressBarVisible(true);
      _gameplayState = _resumeStateAfterUpgrade;
      if (_gameplayState == GameplayState.Orbiting)
      {
        ScheduleNextSpawn(0.45f);
      }
    }

    private void UpdateModuleCardVisuals()
    {
      if (_gameplayState != GameplayState.ModuleUpgrade || _moduleCards.Count == 0 || _camera == null)
      {
        return;
      }

      var pointerWorld = _camera.ScreenToWorldPoint(new Vector3(UnityEngine.Input.mousePosition.x, UnityEngine.Input.mousePosition.y, _blockDepthFromCamera));
      for (var i = 0; i < _moduleCards.Count; i++)
      {
        var card = _moduleCards[i];
        if (card.GameObject == null)
        {
          continue;
        }

        var protocol = GetModuleProtocolForCard(card.Index);
        var pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 1.18f + card.Index * 0.9f);
        var isFocused = IsPointInsideCard(pointerWorld, card);
        var glowAlpha = isFocused ? 0.14f : 0.06f + pulse * 0.03f;
        var borderAlpha = isFocused ? 1f : 0.92f;

        card.GameObject.transform.localScale = Vector3.Lerp(
          card.GameObject.transform.localScale,
          new Vector3(GetCurrentModuleCardWorldSize().x, GetCurrentModuleCardWorldSize().y, 1f) * (isFocused ? 1.02f : 1f + pulse * 0.008f),
          Time.deltaTime * 4f);
        if (card.Renderer != null)
        {
          card.Renderer.color = Color.Lerp(card.Renderer.color, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceElevated, 0.98f), Time.deltaTime * 7f);
        }

        if (card.BorderRenderer != null)
        {
          card.BorderRenderer.color = Color.Lerp(card.BorderRenderer.color, KeepBlinkingTheme.WithAlpha(isFocused ? protocol.AccentColor : KeepBlinkingTheme.BorderReadable, borderAlpha), Time.deltaTime * 7f);
        }

        if (card.GlowRenderer != null)
        {
          card.GlowRenderer.color = Color.Lerp(card.GlowRenderer.color, KeepBlinkingTheme.WithAlpha(protocol.AccentColor, glowAlpha), Time.deltaTime * 7f);
        }

        if (card.AccentRenderer != null)
        {
          card.AccentRenderer.color = Color.Lerp(card.AccentRenderer.color, KeepBlinkingTheme.WithAlpha(protocol.AccentColor, 0.16f + pulse * 0.08f), Time.deltaTime * 7f);
        }
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

        var driftPulse = 1f + Mathf.Sin(Time.time * 1.45f + block.Serial * 0.71f) * 0.05f;
        var targetScale = block.IsHovered ? block.BaseScale * 1.18f : block.BaseScale * driftPulse;
        block.Transform.localScale = Vector3.Lerp(block.Transform.localScale, targetScale, Time.deltaTime * _scaleLerpSpeed);

        block.Transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(block.Phase * 0.85f) * 2f);
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

        var driftPulse = 1f + Mathf.Sin(Time.time * 1.7f + block.Serial * 0.53f) * 0.06f;
        var targetScale = block.IsHovered ? block.BaseScale * 1.16f : block.BaseScale * driftPulse;
        block.Transform.localScale = Vector3.Lerp(block.Transform.localScale, targetScale, Time.deltaTime * _scaleLerpSpeed);
        block.Transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(Time.time * 2.2f + block.Serial) * 3f);
      }
    }

    private Vector3 EvaluateOrbitWorldPosition(float phase)
    {
      var safeViewport = GetGameplayViewportRect(_edgeInsetViewport, _edgeInsetViewport + 0.03f);
      var centerX = safeViewport.center.x;
      var centerY = safeViewport.center.y;
      var radiusX = safeViewport.width * 0.5f;
      var radiusY = safeViewport.height * 0.5f;
      var x = centerX + Mathf.Cos(phase) * radiusX;
      var y = centerY + Mathf.Sin(phase) * radiusY;
      x = Mathf.Clamp(x, safeViewport.xMin, safeViewport.xMax);
      y = Mathf.Clamp(y, safeViewport.yMin, safeViewport.yMax);

      return _camera.ViewportToWorldPoint(new Vector3(x, y, _blockDepthFromCamera));
    }

    private void UpdatePlayerMarker()
    {
      if (_playerMarkerRoot == null || _camera == null)
      {
        return;
      }

      var safeViewport = GetSafeViewportRect(0.04f, 0.06f);
      var center = _camera.ViewportToWorldPoint(new Vector3(safeViewport.center.x, safeViewport.center.y, _blockDepthFromCamera));
      _playerMarkerRoot.transform.position = center;

      var pulse = 1f + Mathf.Sin(Time.time * _playerMarkerPulseSpeed) * 0.03f;
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
          color.a = 0.1f * alphaMultiplier;
        }
        else if (piece.gameObject.name.Contains("Zone"))
        {
          color.a = 0.16f * alphaMultiplier;
        }
        else if (piece.gameObject.name.Contains("Core"))
        {
          color.a = 0.92f * alphaMultiplier;
        }
        else
        {
          color.a = 0.74f * alphaMultiplier;
        }

        piece.color = color;
      }
    }

    private Vector3 GetProgressBarCenterWorldPosition()
    {
      var safeViewport = GetSafeViewportRect(0.03f, 0.03f);
      var viewportY = Mathf.Lerp(safeViewport.yMin, safeViewport.yMax, _progressBarBottomViewport + 0.04f);
      return _camera.ViewportToWorldPoint(new Vector3(safeViewport.center.x, viewportY, _blockDepthFromCamera));
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

      var safeViewport = GetSafeViewportRect(0.03f, 0.03f);
      var viewportY = Mathf.Lerp(safeViewport.yMin, safeViewport.yMax, _progressBarBottomViewport + 0.04f);
      var halfWidthViewport = Mathf.Min(_progressBarWidthViewport * 0.5f, safeViewport.width * 0.48f);
      var left = _camera.ViewportToWorldPoint(new Vector3(safeViewport.center.x - halfWidthViewport, viewportY, _blockDepthFromCamera));
      var right = _camera.ViewportToWorldPoint(new Vector3(safeViewport.center.x + halfWidthViewport, viewportY, _blockDepthFromCamera));
      var fullWidth = Mathf.Abs(right.x - left.x);
      var center = GetProgressBarCenterWorldPosition();
      _progressBarRoot.transform.position = center;

      if (_progressBarBackRenderer != null)
      {
        _progressBarBackRenderer.transform.localPosition = Vector3.zero;
        _progressBarBackRenderer.transform.localScale = new Vector3(fullWidth, _progressBarHeightWorld, 1f);
      }

      if (_progressBarGlowRenderer != null)
      {
        _progressBarGlowRenderer.transform.localPosition = new Vector3(0f, 0f, 0.01f);
        _progressBarGlowRenderer.transform.localScale = new Vector3(fullWidth + 0.22f, _progressBarHeightWorld + 0.2f, 1f);
      }

      if (_progressBarFillRenderer != null)
      {
        var progress = Mathf.Clamp01(_sampleProgress);
        var minimumVisibleFill = progress > 0f ? _progressBarHeightWorld * 0.95f : 0.001f;
        var fillWidth = Mathf.Max(minimumVisibleFill, fullWidth * progress);
        _progressBarFillRenderer.transform.localScale = new Vector3(fillWidth, _progressBarHeightWorld * 0.92f, 1f);
        _progressBarFillRenderer.transform.localPosition = new Vector3((fillWidth - fullWidth) * 0.5f, 0f, -0.01f);
      }

      if (_progressBarBorderRenderer != null)
      {
        _progressBarBorderRenderer.transform.localPosition = new Vector3(0f, 0f, -0.02f);
        _progressBarBorderRenderer.transform.localScale = new Vector3(fullWidth + 0.04f, _progressBarHeightWorld + 0.04f, 1f);
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

      if (_gazeIndicatorRoot.activeSelf)
      {
        _gazeIndicatorRoot.SetActive(false);
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
        normal = { textColor = KeepBlinkingTheme.AccentSoft },
      };
    }

    private float GetMobileUiScale()
    {
      var widthScale = Screen.width / 390f;
      var heightScale = Screen.height / 844f;
      return Mathf.Clamp(Mathf.Min(widthScale, heightScale), 1f, 1.18f);
    }

    private int ScaledFontSize(int baseFontSize)
    {
      return Mathf.RoundToInt(baseFontSize * GetMobileUiScale());
    }

    private void EnsureInstructionStyles()
    {
      var scale = GetMobileUiScale();
      if (_instructionTitleStyle != null &&
          _instructionBodyStyle != null &&
          Mathf.Abs(_instructionStyleScale - scale) < 0.01f)
      {
        return;
      }

      _instructionStyleScale = scale;
      _instructionTitleStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = ScaledFontSize(20),
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.UpperLeft,
        wordWrap = true,
        normal = { textColor = KeepBlinkingTheme.TextPrimary },
      };

      _instructionBodyStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = ScaledFontSize(14),
        alignment = TextAnchor.UpperLeft,
        wordWrap = true,
        normal = { textColor = KeepBlinkingTheme.AccentSoft },
      };
    }

    private void EnsurePresentationStyles()
    {
      var scale = GetMobileUiScale();
      if (_warningTitleStyle != null &&
          _warningBodyStyle != null &&
          _tutorialTitleStyle != null &&
          _tutorialBodyStyle != null &&
          _reportTitleStyle != null &&
          _reportBodyStyle != null &&
          _reportLabelStyle != null &&
          _reportMetricStyle != null &&
          _cardTagStyle != null &&
          _cardTitleStyle != null &&
          _cardBodyStyle != null &&
          _cardDeltaStyle != null &&
          _cardLevelStyle != null &&
          Mathf.Abs(_presentationStyleScale - scale) < 0.01f)
      {
        return;
      }

      _presentationStyleScale = scale;
      _warningTitleStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = ScaledFontSize(22),
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.UpperLeft,
        wordWrap = true,
        normal = { textColor = KeepBlinkingTheme.TextPrimary },
      };

      _warningBodyStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = ScaledFontSize(16),
        alignment = TextAnchor.UpperLeft,
        wordWrap = true,
        normal = { textColor = KeepBlinkingTheme.TextSecondary },
      };

      _tutorialTitleStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = ScaledFontSize(23),
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.UpperLeft,
        wordWrap = true,
        normal = { textColor = KeepBlinkingTheme.TextPrimary },
      };

      _tutorialBodyStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = ScaledFontSize(15),
        alignment = TextAnchor.UpperLeft,
        wordWrap = true,
        normal = { textColor = KeepBlinkingTheme.TextSecondary },
      };

      _reportTitleStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = ScaledFontSize(30),
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.UpperLeft,
        wordWrap = true,
        normal = { textColor = KeepBlinkingTheme.TextPrimary },
      };

      _reportBodyStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = ScaledFontSize(16),
        alignment = TextAnchor.UpperLeft,
        wordWrap = true,
        normal = { textColor = KeepBlinkingTheme.TextSecondary },
      };

      _reportLabelStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = ScaledFontSize(14),
        alignment = TextAnchor.UpperLeft,
        wordWrap = true,
        normal = { textColor = KeepBlinkingTheme.TextMuted },
      };

      _reportMetricStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = ScaledFontSize(24),
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.MiddleLeft,
        normal = { textColor = KeepBlinkingTheme.TextPrimary },
      };

      _cardTagStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = ScaledFontSize(12),
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.MiddleCenter,
        normal = { textColor = KeepBlinkingTheme.TextPrimary },
      };

      _cardTitleStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = ScaledFontSize(18),
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.UpperLeft,
        wordWrap = false,
        normal = { textColor = KeepBlinkingTheme.TextPrimary },
      };

      _cardBodyStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = ScaledFontSize(14),
        alignment = TextAnchor.UpperLeft,
        wordWrap = true,
        normal = { textColor = KeepBlinkingTheme.TextSecondary },
      };

      _cardDeltaStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = ScaledFontSize(13),
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.MiddleLeft,
        wordWrap = false,
        normal = { textColor = KeepBlinkingTheme.TextPrimary },
      };

      _cardLevelStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = ScaledFontSize(11),
        alignment = TextAnchor.MiddleRight,
        wordWrap = false,
        normal = { textColor = KeepBlinkingTheme.TextMuted },
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

      if (_roundedFillSprite != null)
      {
        Destroy(_roundedFillSprite);
      }

      if (_roundedBorderSprite != null)
      {
        Destroy(_roundedBorderSprite);
      }

      if (_backgroundSprite != null)
      {
        Destroy(_backgroundSprite);
      }

      if (_circleTexture != null)
      {
        Destroy(_circleTexture);
      }

      if (_roundedFillTexture != null)
      {
        Destroy(_roundedFillTexture);
      }

      if (_roundedBorderTexture != null)
      {
        Destroy(_roundedBorderTexture);
      }

      if (_backgroundTexture != null)
      {
        Destroy(_backgroundTexture);
      }

      DestroyRuntimeClip(_freezeStartedClip);
      DestroyRuntimeClip(_coverageCompleteClip);
      DestroyRuntimeClip(_freezeInterruptedClip);
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
      public readonly SpriteRenderer BorderRenderer;
      public readonly SpriteRenderer GlowRenderer;
      public readonly SpriteRenderer AccentRenderer;
      public readonly int Index;

      public ModuleCard(
        GameObject gameObject,
        SpriteRenderer renderer,
        SpriteRenderer borderRenderer,
        SpriteRenderer glowRenderer,
        SpriteRenderer accentRenderer,
        int index)
      {
        GameObject = gameObject;
        Renderer = renderer;
        BorderRenderer = borderRenderer;
        GlowRenderer = glowRenderer;
        AccentRenderer = accentRenderer;
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
