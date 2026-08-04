// Attach this script to an Empty GameObject.
// The script automatically reads KeepBlinking's MediaPipe bridge when available.
// External eye-tracking SDKs can still write realGazeScreenPosition and call TriggerHardwareBlink().
// No mouse, keyboard, Rigidbody2D, prefab, UI, or external art asset is required.

using System;
using System.Collections;
using System.Collections.Generic;
using KeepBlinking.Input;
using UnityEngine;
using Random = UnityEngine.Random;

namespace KeepBlinking.Gameplay
{
  public enum TutorialFeedbackCue
  {
    Focus,
    BlinkLoop,
    Converted,
    PushAwayLoop,
    ExperienceComplete,
    CountdownBeat,
  }

  public enum BossFeedbackCue
  {
    CloseRequest,
    CoverageComplete,
    SuccessfulRelease,
  }

  public readonly struct ExperienceProgressSignal
  {
    public int CollectedCount { get; }
    public int RequiredCount { get; }
    public float NormalizedProgress { get; }

    public ExperienceProgressSignal(int collectedCount, int requiredCount, float normalizedProgress)
    {
      CollectedCount = collectedCount;
      RequiredCount = requiredCount;
      NormalizedProgress = normalizedProgress;
    }
  }

  public class EdgeOrbitHarvestMvp : MonoBehaviour
  {
    public const int NoTargetId = -1;

    public event Action<int> TargetLockChanged;
    public event Action<int> TargetConverted;
    public event Action PushAwayCollectionReady;
    public event Action PushAwayTriggered;
    public event Action<int> ConvertedCollectionStarted;
    public event Action<int> ExperienceReachedBar;
    public event Action<ExperienceProgressSignal> ExperienceProgressChanged;
    public event Action UpgradeOpened;
    public event Action<int> ModuleChoiceCompleted;
    public event Action<int> CrisisStarted;
    public event Action EyesClosedFreezeStarted;
    public event Action FullCoverageReached;
    public event Action CrisisReleaseInterrupted;
    public event Action<int> ReopenReleaseCompleted;
    public event Action<int> CrisisExperienceCollectionCompleted;
    public event Action<bool> TutorialReadinessChanged;
    public event Action<FirstLevelModuleId> FirstLevelModuleInstalled;
    public event Action<FirstLevelModuleId> FirstLevelModuleEffectActivated;
    public event Action<int> FutureBossCoreDamageRequested;
    public event Action FirstLevelUpgradeSequenceCompleted;
    public event Action FirstLevelBuildCompleted;
    public event Action BlinkInputAccepted;
    public event Action<int> SoftBlinkPerformed;
    public event Action<int, int> NormalBlinkConversionCompleted;
    public event Action<int, int> BossExperienceCollectionCompleted;

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

    private enum FullLoopStage
    {
      WaitingForBlink,
      WaitingForRest,
      WaitingForPushAway,
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
    [SerializeField, Min(0.05f)] private float _softBlinkStableOpenSeconds = 0.16f;
    [SerializeField, Min(0.2f)] private float _softBlinkMaximumClosedSeconds = 0.65f;

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
    [SerializeField] private Vector2 _blockWorldSizeRange = new Vector2(1.03f, 1.38f);
    [SerializeField] private Vector2 _orbitAngularSpeedRange = new Vector2(0.07f, 0.13f);
    [SerializeField, Range(0.02f, 0.3f)] private float _softFocusInsideSpeedMultiplier = 0.55f;
    [SerializeField, Range(0.02f, 0.2f)] private float _softFocusLaneSpacingNormalized = 0.075f;

    [Header("Crisis & Eye Close Break")]
    [SerializeField] private float _minCrisisIntervalSeconds = 15f;
    [SerializeField] private float _maxCrisisIntervalSeconds = 20f;
    [SerializeField] private int _crisisSpawnCount = 8;
    [SerializeField] private float _crisisSpawnPaddingViewport = 0.16f;
    [SerializeField] private Vector2 _crisisBlockWorldSizeRange = new Vector2(1.03f, 1.38f);
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
    [SerializeField] private float _calibrationTargetWorldSize = 1.05f;
    [SerializeField] private float _calibrationEdgePaddingViewport = 0.22f;
    [SerializeField] private float _calibrationMaxScale = 3.5f;
    [SerializeField] private float _calibrationMinScale = 0.25f;

    [Header("Freeze Test Feedback")]
    [SerializeField] private bool _playFreezeFeedbackAudio = true;
    [SerializeField] private float _freezeFeedbackVolume = 0.22f;

    [Header("Debug HUD")]
    [SerializeField] private bool _showDebugHud;

    [Header("Session Face Distance")]
    [SerializeField, Min(0.5f)] private float _distanceBaselineCaptureSeconds = 1.5f;
    [SerializeField, Min(5)] private int _distanceBaselineMinimumSamples = 20;
    [SerializeField, Range(0.01f, 0.5f)] private float _distanceBaselineMaximumRelativeSpread = 0.18f;
    [SerializeField, Min(0.1f)] private float _faceDistanceSmoothSpeed = 8f;
    [SerializeField, Range(0.5f, 1f)] private float _distanceNormalMinimumRatio = 0.92f;
    [SerializeField, Range(1f, 1.5f)] private float _distanceNormalMaximumRatio = 1.10f;
    [SerializeField, Range(0.4f, 1f)] private float _pushAwayTriggerRatio = 0.82f;
    [SerializeField, Min(0f)] private float _pushAwayHoldSeconds = 0.3f;
    [SerializeField, Range(0.5f, 1.2f)] private float _pushAwayRearmRatio = 0.92f;
    [SerializeField, Min(0f)] private float _pushAwayRearmHoldSeconds = 0.3f;
    [SerializeField, Range(1f, 1.6f)] private float _tooCloseEnterRatio = 1.18f;
    [SerializeField, Min(0f)] private float _tooCloseHoldSeconds = 0.5f;
    [SerializeField, Range(1f, 1.5f)] private float _tooCloseExitRatio = 1.10f;

    [Header("Sampling & Module Upgrade")]
    [SerializeField] private float _sampleCollectSpeed = 9f;
    [SerializeField] private float _sampleCollectDistance = 0.18f;
    [SerializeField] private int _samplesNeededForUpgrade = 10;
    [SerializeField, Min(1)] private int _upgradesRequiredBeforeBoss = 5;
    [SerializeField] private float _progressBarWidthViewport = 0.46f;
    [SerializeField] private float _progressBarHeightWorld = 0.14f;
    [SerializeField] private float _progressBarBottomViewport = 0.02f;
    [SerializeField] private Vector2 _moduleCardSize = new Vector2(2.25f, 3.0f);
    [SerializeField] private float _moduleCardSpacing = 0.38f;
    [SerializeField] private float _moduleInstallPresentationSeconds = 0.42f;
    [SerializeField] private float _chainConversionRadiusWorld = 3.1f;
    [SerializeField] private int _minimumUpgradeSampleRequirement = 3;
    [SerializeField] private bool _logUpgradeFlowTransitions;

    [Header("Center Player Marker")]
    [SerializeField] private float _playerMarkerWorldSize = 0.72f;
    [SerializeField] private float _playerMarkerPulseSpeed = 1.4f;

    private readonly List<OrbitBlock> _blocks = new List<OrbitBlock>();
    private readonly List<ModuleCard> _moduleCards = new List<ModuleCard>();
    private readonly List<FirstLevelModuleId> _currentModuleOffer = new List<FirstLevelModuleId>(3);
    private readonly List<FirstLevelModuleId> _installedModuleOrder = new List<FirstLevelModuleId>(4);
    private readonly HashSet<FirstLevelModuleId> _installedModules = new HashSet<FirstLevelModuleId>();
    private readonly Dictionary<FirstLevelModuleId, float> _moduleFlashUntil = new Dictionary<FirstLevelModuleId, float>();
    private readonly List<SpriteRenderer> _playerMarkerPieces = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> _gazeIndicatorPieces = new List<SpriteRenderer>();
    private readonly List<SpriteRenderer> _calibrationTargetPieces = new List<SpriteRenderer>();
    private readonly List<Vector2> _calibrationRawSamples = new List<Vector2>();
    private readonly List<Vector2> _calibrationTargetSamples = new List<Vector2>();
    private readonly List<TimedGazeSample> _recentRawGazeSamples = new List<TimedGazeSample>();
    private readonly Dictionary<int, int> _collectedCrisisExperienceByWave = new Dictionary<int, int>();
    private readonly Dictionary<int, int> _collectedBossExperienceByRound = new Dictionary<int, int>();
    private Sprite _squareSprite;
    private Sprite _circleSprite;
    private Sprite _roundedFillSprite;
    private Sprite _roundedBorderSprite;
    private Sprite _dataSeedSprite;
    private Sprite _backgroundSprite;
    private Texture2D _squareTexture;
    private Texture2D _circleTexture;
    private Texture2D _roundedFillTexture;
    private Texture2D _roundedBorderTexture;
    private Texture2D _dataSeedTexture;
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
    private FirstLevelUpgradeView _moduleUpgradeView;
    private AudioSource _feedbackAudioSource;
    private AudioSource _tutorialFeedbackAudioSource;
    private AudioClip _freezeStartedClip;
    private AudioClip _coverageCompleteClip;
    private AudioClip _freezeInterruptedClip;
    private AudioClip _freezeClearedClip;
    private AudioClip _tutorialFocusClip;
    private AudioClip _tutorialBlinkClip;
    private AudioClip _tutorialConvertedClip;
    private AudioClip _tutorialPushAwayClip;
    private AudioClip _tutorialExperienceCompleteClip;
    private AudioClip _tutorialCountdownClip;
    private AudioClip _moduleInstalledClip;
    private AudioClip _moduleActivatedClip;
    private AudioClip _bossCloseRequestClip;
    private AudioClip _bossSuccessfulReleaseClip;
    private Vector2[] _calibrationTargets;
    private Vector2 _rawGazeScreenPosition;
    private Vector2 _calibrationScale = Vector2.one;
    private Vector2 _calibrationOffset = Vector2.zero;
    private bool _calibrationActive;
    private bool _calibrationComplete;
    private int _calibrationIndex;
    private bool _blinkQueued;
    private int _suppressBlinkHarvestFrame = -1;
    private bool _hardwareWarningLogged;
    private int _lastObservedBlinkCount = -1;
    private float _baselineLeftEyeOpen = -1f;
    private float _baselineRightEyeOpen = -1f;
    private float _lastBlinkAcceptedAt = -999f;
    private float _lastBlinkVisualAt = -999f;
    private bool _lastRelativeBlinking;
    private float _softBlinkStableOpenStartedAt = -1f;
    private float _softBlinkCandidateStartedAt = -1f;
    private bool _softBlinkCandidateActive;
    private int _softBlinkSerial;
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
    private readonly SessionDistanceTracker _distanceTracker = new SessionDistanceTracker();
    private DistanceCameraFeedback _distanceCameraFeedback;
    private SoftFocusFieldController _softFocusField;
    private float _nextDistanceBaselineWarningAt = -1f;
    private bool _formalFlowInitialized;
    private bool _pushAwayTriggerPending;
    private bool _pushAwayReady;
    private bool _softFocusHiddenByPushAway;
    private bool _offScreenEyeBreakPending;
    private bool _tutorialMode;
    private bool _tutorialRandomSpawningPaused;
    private bool _tutorialRandomCrisisPaused;
    private bool _tutorialSessionTimerPaused;
    private bool _tutorialCollectionInputPaused;
    private float _tutorialSessionTimerPausedAt = -1f;
    private bool _formalCrisisTrackingPaused;
    private float _formalCrisisTrackingPausedAt = -1f;
    private bool _hasTutorialExperienceSnapshot;
    private int _tutorialStartingCollectedSampleCount;
    private int _tutorialStartingTotalSamplesCollected;
    private float _tutorialStartingSampleProgress;
    private OrbitBlock _hoveredBlock;
    private OrbitBlock _lastHoveredBlock;
    private float _lastHoveredAt = -999f;
    private float _lastSoftLockAngle;
    private float _lastHorizontalIntent;
    private GUIStyle _hudStyle;
    private GUIStyle _warningTitleStyle;
    private GUIStyle _warningBodyStyle;
    private GUIStyle _reportTitleStyle;
    private GUIStyle _reportBodyStyle;
    private GUIStyle _reportLabelStyle;
    private GUIStyle _reportMetricStyle;
    private GUIStyle _cardTagStyle;
    private GUIStyle _cardTitleStyle;
    private GUIStyle _cardBodyStyle;
    private GUIStyle _cardDeltaStyle;
    private GUIStyle _cardLevelStyle;
    private GUIStyle _moduleHeaderStyle;
    private GUIStyle _moduleInstructionStyle;
    private float _presentationStyleScale = -1f;
    private float _sessionStartedAt = -1f;
    private bool _gameFlowStarted;
    private bool _sessionEnded;
    private int _sessionBlinkCount;
    private int _blinkCaptureCount;
    private int _eyeRestBreakCount;
    private int _distanceSwitchCount;
    private int _moduleChoiceCount;
    private int _currentUpgradeSampleRequirement;
    private int _moduleHoveredCardIndex = -1;
    private int _selectedModuleCardIndex = -1;
    private bool _moduleChoicePending;
    private float _moduleInstallStartedAt = -1f;
    private bool _firstLevelUpgradeSequenceCompleted;
    private bool _firstLevelBuildCompleted;
    private bool _firstLevelModalPaused;
    private bool _firstLevelRandomFlowPaused;
    private bool _firstLevelBossTransitionActive;
    private bool _firstLevelBossMode;
    private bool _firstLevelPresentationHidden;
    private int _acceptedBlinkSerial;
    private float _normalSpawnPausedUntil = -1f;
    private float _quietFieldVisualUntil = -1f;
    private bool _wakeEchoRangePrimed;
    private bool _deepRecoveryNextLockPrimed;
    private int _deepRecoveryTargetId = NoTargetId;
    private int _lockHoldActiveTargetId = NoTargetId;
    private int _loopBonusPendingSamples;
    private bool _flashXpDiscountOnNextSample;
    private FullLoopStage _fullLoopStage = FullLoopStage.WaitingForBlink;
    private float _lastModuleFeedbackAt = -999f;
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
    private int _lastSignaledLockedTargetId = NoTargetId;
    private int _nextCrisisWaveId;
    private int _activeCrisisWaveId;
    private Vector3 _lastPurificationAnchorWorldPosition;
    private float _lastPurificationAnchorWorldRadius;
    private bool _hasLastPurificationAnchor;
    private bool _tutorialReady;
    private int _tutorialOrbitTargetId = NoTargetId;
    private float _tutorialProgressGlowUntil = -1f;

    private const string ProtocolDayPrefsKey = "KeepBlinking.ProtocolDay";
    private static readonly Vector2 CrisisArrayCenterViewport = new Vector2(0.5f, 0.48f);

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
      _lastBlinkVisualAt = Time.time;
      _sessionBlinkCount++;
      _acceptedBlinkSerial++;
      InvokeSignalSafely(BlinkInputAccepted, nameof(BlinkInputAccepted));
      if (!_tutorialMode)
      {
        _blinkQueued = false;
        return;
      }
      if (Time.frameCount == _suppressBlinkHarvestFrame ||
          _gameplayState == GameplayState.Crisis ||
          _gameplayState == GameplayState.EyesClosedFreeze)
      {
        _blinkQueued = false;
        return;
      }

      _blinkQueued = true;
    }

    public bool IsTutorialModeEnabled => _tutorialMode;
    public bool IsTutorialRandomSpawningPaused => _tutorialMode && _tutorialRandomSpawningPaused;
    public bool IsTutorialRandomCrisisPaused => _tutorialMode && _tutorialRandomCrisisPaused;
    public bool IsTutorialSessionTimerPaused => _tutorialMode && _tutorialSessionTimerPaused;
    public bool IsTutorialInputSuspended => _tutorialMode && !_tutorialReady;
    public bool IsTutorialReady => _tutorialReady;
    public bool IsCalibrationActive => _calibrationActive;
    public bool IsCrisisAwaitingClose => _gameplayState == GameplayState.Crisis;
    public bool IsEyesClosedFreezeActive => _gameplayState == GameplayState.EyesClosedFreeze;
    public bool IsPushAwayCollectionReady => _pushAwayReady;
    public float BaselineFaceScale => _distanceTracker.BaselineFaceScale;
    public float CurrentFaceScale => _distanceTracker.CurrentFaceScale;
    public float DistanceRatio => _distanceTracker.DistanceRatio;
    public bool IsTooClose => _distanceTracker.IsTooClose;
    public SessionDistanceState DistanceState => _distanceTracker.State;
    public int PendingConvertedExperienceCount => CountState(BlockState.Converted);
    public bool IsFaceInputAvailable => !_autoReadKeepBlinkingEyeInput || EyeInputDebugState.Latest.FaceDetected;
    public int CrisisSpawnCount => Mathf.Max(1, _crisisSpawnCount);
    public bool IsCalibrationInputReady
    {
      get
      {
        if (!_autoReadKeepBlinkingEyeInput)
        {
          return true;
        }

        var snapshot = EyeInputDebugState.Latest;
        return snapshot.FaceDetected && snapshot.HasGazeScreenPosition;
      }
    }
    public int LockedTargetId => _tutorialMode && IsActiveTargetBlock(_hoveredBlock) ? _hoveredBlock.Serial : NoTargetId;
    public float CameraNearAmount => _distanceCameraFeedback != null ? _distanceCameraFeedback.NearAmount : 0f;
    public bool IsExperienceCollectionInProgress => CountState(BlockState.Collecting) > 0;
    public bool HasUncollectedExperience => HasCollectableSamples() || IsExperienceCollectionInProgress;
    public bool IsSoftFocusNormalGameplayActive => !_tutorialMode &&
                                                    !_calibrationActive &&
                                                    !_sessionEnded &&
                                                    !_firstLevelModalPaused &&
                                                    !_firstLevelRandomFlowPaused &&
                                                    !_firstLevelBossTransitionActive &&
                                                    !_firstLevelBossMode &&
                                                    !_softFocusHiddenByPushAway &&
                                                    _gameplayState == GameplayState.Orbiting;
    public bool IsSoftFocusBlinkHealthActive => IsSoftFocusNormalGameplayActive;
    public bool ShouldShowSoftFocusField => !_tutorialMode &&
                                            !_calibrationActive &&
                                            !_sessionEnded &&
                                            !_firstLevelBossMode &&
                                            !_softFocusHiddenByPushAway &&
                                            _gameplayState != GameplayState.ModuleUpgrade &&
                                            _gameplayState != GameplayState.SessionReport;
    public bool CanStartOffScreenEyeBreak => !_tutorialMode &&
                                             !_calibrationActive &&
                                             !_sessionEnded &&
                                             !_firstLevelBossMode &&
                                             !_firstLevelBossTransitionActive &&
                                             !_firstLevelModalPaused &&
                                             !_distanceTracker.IsTooClose &&
                                             IsTrackingAvailable &&
                                             _gameplayState == GameplayState.Orbiting &&
                                             !_moduleChoicePending &&
                                             !HasUncollectedExperience;
    public float ExperienceProgress => _sampleProgress;
    public int CurrentUpgradeSampleRequirement => GetCurrentUpgradeSampleRequirement();
    public int InstalledFirstLevelModuleCount => _installedModuleOrder.Count;
    public bool IsFirstLevelUpgradeSequenceComplete => _firstLevelUpgradeSequenceCompleted;
    public bool IsModuleUpgradeOpen => _gameplayState == GameplayState.ModuleUpgrade;
    public bool IsModuleInstallationPending => _moduleChoicePending;
    public bool IsFirstLevelBuildComplete => _firstLevelBuildCompleted;
    public bool IsFirstLevelModalPaused => _firstLevelModalPaused;
    public bool IsFirstLevelBossTransitionActive => _firstLevelBossTransitionActive;
    public bool IsFirstLevelBossMode => _firstLevelBossMode;
    public int AcceptedBlinkSerial => _acceptedBlinkSerial;
    public int SoftBlinkSerial => _softBlinkSerial;
    public bool HasStableOpenEyesForSoftBlink =>
      IsTrackingAvailable &&
      !isEyesClosed &&
      _softBlinkStableOpenStartedAt >= 0f &&
      Time.time - _softBlinkStableOpenStartedAt >= Mathf.Max(0.05f, _softBlinkStableOpenSeconds);
    public bool IsTrackingAvailable => !_autoReadKeepBlinkingEyeInput || EyeInputDebugState.Latest.FaceDetected;
    public bool HasCurrentGazeInput => !_autoReadKeepBlinkingEyeInput ||
                                       (EyeInputDebugState.Latest.FaceDetected && EyeInputDebugState.Latest.HasGazeScreenPosition);
    public bool IsComfortGazeForBoss
    {
      get
      {
        if (!IsTrackingAvailable || !HasCurrentGazeInput || AreEyesClosed || Screen.width <= 0 || Screen.height <= 0)
        {
          return false;
        }
        var normalized = new Vector2(realGazeScreenPosition.x / Screen.width, realGazeScreenPosition.y / Screen.height);
        return Mathf.Abs(normalized.x - 0.5f) <= 0.33f && Mathf.Abs(normalized.y - 0.5f) <= 0.33f;
      }
    }
    public bool AreEyesClosed => isEyesClosed;
    public Vector2 CurrentGazeScreenPosition => realGazeScreenPosition;
    public int UpgradesRequiredBeforeBoss => _upgradesRequiredBeforeBoss > 0
      ? _upgradesRequiredBeforeBoss
      : 5;
    public bool HasUnsettledSamples => HasCollectableSamples() || CountState(BlockState.Collecting) > 0;
    public bool IsFirstLevelFieldSettled => !_moduleChoicePending &&
                                            _gameplayState != GameplayState.ModuleUpgrade &&
                                            !HasAnyLiveGameplayBlock();

    public FirstLevelModuleId[] GetInstalledFirstLevelModules()
    {
      return _installedModuleOrder.ToArray();
    }

    public bool HasFirstLevelModule(FirstLevelModuleId moduleId)
    {
      return _installedModules.Contains(moduleId);
    }

    public void SetOffScreenEyeBreakPending(bool pending)
    {
      _offScreenEyeBreakPending = pending;
    }

    public void NotifySoftFocusModuleActivated(FirstLevelModuleId moduleId)
    {
      ActivateModuleEffect(moduleId);
    }

    public void SetFirstLevelModalPaused(bool paused, bool hidePresentation)
    {
      _firstLevelModalPaused = paused;
      _firstLevelPresentationHidden = paused && hidePresentation;
      _blinkQueued = false;
      if (paused)
      {
        ResetPushAwayInputState();
      }
      _hoveredBlock = null;
      _lastHoveredBlock = null;
      PublishTargetLockChangedIfNeeded();
      SetFirstLevelPresentationHidden(_firstLevelPresentationHidden);

      if (!paused && !_firstLevelBossMode && !_firstLevelRandomFlowPaused && CanScheduleFormalFlow())
      {
        ScheduleNextSpawn(0.45f);
        ScheduleNextCrisis();
      }
    }

    public void BeginFirstLevelBossTransition()
    {
      _firstLevelBossTransitionActive = true;
      _firstLevelRandomFlowPaused = true;
      _blinkQueued = false;
      ResetPushAwayInputState();
      _hoveredBlock = null;
      _lastHoveredBlock = null;
      PublishTargetLockChangedIfNeeded();
      if (_playerMarkerRoot != null)
      {
        _playerMarkerRoot.SetActive(false);
      }
      if (_gazeIndicatorRoot != null)
      {
        _gazeIndicatorRoot.SetActive(false);
      }

      if (_gameplayState == GameplayState.Crisis || _gameplayState == GameplayState.EyesClosedFreeze)
      {
        _gameplayState = GameplayState.Orbiting;
        _coverageCuePlayed = false;
        _purificationRadius = 0f;
        _eyesClosedStartedAt = -1f;
        SetBlackoutVisible(false);
        SetPurificationWaveVisible(false);
      }

      for (var i = _blocks.Count - 1; i >= 0; i--)
      {
        var block = _blocks[i];
        if (block == null || block.GameObject == null)
        {
          _blocks.RemoveAt(i);
          continue;
        }

        if (block.State != BlockState.Converted && block.State != BlockState.Collecting)
        {
          Destroy(block.GameObject);
          _blocks.RemoveAt(i);
        }
      }
    }

    public void BeginFirstLevelBossMode()
    {
      _firstLevelBossTransitionActive = false;
      _firstLevelRandomFlowPaused = true;
      _firstLevelBossMode = true;
      _firstLevelModalPaused = false;
      _firstLevelPresentationHidden = false;
      _gameplayState = GameplayState.Orbiting;
      _blinkQueued = false;
      ResetPushAwayInputState();
      SetProgressBarVisible(true);
      SetFirstLevelPresentationHidden(false);
      if (_playerMarkerRoot != null)
      {
        _playerMarkerRoot.SetActive(false);
      }
      if (_gazeIndicatorRoot != null)
      {
        _gazeIndicatorRoot.SetActive(false);
      }
    }

    public void CompleteFirstLevelFlow()
    {
      _sessionEnded = true;
      _firstLevelBossTransitionActive = false;
      _firstLevelRandomFlowPaused = true;
      _firstLevelBossMode = false;
      _firstLevelModalPaused = true;
      _firstLevelPresentationHidden = true;
      _blinkQueued = false;
      ResetPushAwayInputState();
      SetFirstLevelPresentationHidden(true);
      RefreshTutorialReadiness(false);
    }

    public void BeginFirstLevelSettlement()
    {
      _sessionEnded = true;
      _firstLevelBossTransitionActive = false;
      _firstLevelRandomFlowPaused = true;
      _firstLevelBossMode = false;
      _firstLevelModalPaused = true;
      _firstLevelPresentationHidden = false;
      _blinkQueued = false;
      _hoveredBlock = null;
      _lastHoveredBlock = null;
      _gameplayState = GameplayState.Orbiting;
      ResetPushAwayInputState();
      PublishTargetLockChangedIfNeeded();
    }

    public void ReleaseFirstLevelSessionPauses()
    {
      if (_sessionEnded)
      {
        return;
      }

      _firstLevelRandomFlowPaused = false;
      _firstLevelBossTransitionActive = false;
      _firstLevelBossMode = false;
      SetFirstLevelModalPaused(false, false);
    }

    public int GetPendingBossExperienceSampleCount(int bossRoundId)
    {
      if (bossRoundId <= 0)
      {
        return 0;
      }

      var count = 0;
      for (var i = 0; i < _blocks.Count; i++)
      {
        var block = _blocks[i];
        if (block == null ||
            block.GameObject == null ||
            block.BossRoundId != bossRoundId)
        {
          continue;
        }

        if (block.State == BlockState.Converted || block.State == BlockState.Collecting)
        {
          count++;
        }
      }

      return count;
    }

    public int[] SpawnBossExperienceSamples(int bossRoundId, int count, Vector2 anchorViewport, Color color)
    {
      if (!_firstLevelBossMode || bossRoundId <= 0 || count <= 0 || _camera == null)
      {
        return Array.Empty<int>();
      }

      var ids = new int[count];
      var anchor = _camera.ViewportToWorldPoint(
        new Vector3(Mathf.Clamp01(anchorViewport.x), Mathf.Clamp01(anchorViewport.y), _blockDepthFromCamera));
      for (var i = 0; i < count; i++)
      {
        var angle = i * Mathf.PI * 2f / Mathf.Max(1, count);
        var offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * (0.24f + i * 0.035f);
        ids[i] = SpawnConvertedModuleSample(anchor + offset, color, 0, false, bossRoundId).Serial;
      }

      return ids;
    }

    public float ApplySuccessfulBossRestModules(int bossRoundId, Vector2 anchorViewport)
    {
      if (!_firstLevelBossMode || bossRoundId <= 0 || _camera == null)
      {
        return 0f;
      }

      var anchor = _camera.ViewportToWorldPoint(
        new Vector3(Mathf.Clamp01(anchorViewport.x), Mathf.Clamp01(anchorViewport.y), _blockDepthFromCamera));
      return HandleSuccessfulRestModules(0, bossRoundId, anchor);
    }

    public int ApplyBossBlinkModules()
    {
      var fragmentCount = 0;
      if (_installedModules.Contains(FirstLevelModuleId.WideBlink))
      {
        fragmentCount = 2;
        ActivateModuleEffect(FirstLevelModuleId.WideBlink);
      }
      else if (_installedModules.Contains(FirstLevelModuleId.ChainBlink))
      {
        fragmentCount = 1;
        ActivateModuleEffect(FirstLevelModuleId.ChainBlink);
      }

      if (fragmentCount > 0 && _installedModules.Contains(FirstLevelModuleId.WideChain))
      {
        ActivateModuleEffect(FirstLevelModuleId.WideChain);
      }

      if (_wakeEchoRangePrimed)
      {
        _wakeEchoRangePrimed = false;
        ActivateModuleEffect(FirstLevelModuleId.WakeEcho);
      }

      if (_installedModules.Contains(FirstLevelModuleId.FullLoop))
      {
        _fullLoopStage = FullLoopStage.WaitingForRest;
      }

      return fragmentCount;
    }

    public void PlayBossFeedback(BossFeedbackCue cue)
    {
      switch (cue)
      {
        case BossFeedbackCue.CloseRequest:
          PlayFeedbackClip(_bossCloseRequestClip);
          break;
        case BossFeedbackCue.CoverageComplete:
          PlayFeedbackClip(_coverageCompleteClip);
          break;
        case BossFeedbackCue.SuccessfulRelease:
          PlayFeedbackClip(_bossSuccessfulReleaseClip);
          break;
      }
    }

    public bool IsCrisisTargetId(int targetId)
    {
      var block = FindBlockById(targetId);
      return block != null && block.State == BlockState.Crisis;
    }

    public bool TryGetTargetScreenPresentation(int targetId, out Vector2 screenPosition, out float screenRadius)
    {
      screenPosition = Vector2.zero;
      screenRadius = 0f;
      var block = FindBlockById(targetId);
      if (_camera == null || block == null || block.GameObject == null)
      {
        return false;
      }

      var center = _camera.WorldToScreenPoint(block.Transform.position);
      if (center.z <= 0f)
      {
        return false;
      }

      var worldRadius = Mathf.Max(block.Transform.lossyScale.x, block.Transform.lossyScale.y) * 0.58f;
      var edge = _camera.WorldToScreenPoint(block.Transform.position + _camera.transform.right * worldRadius);
      screenPosition = new Vector2(center.x, center.y);
      var edgePosition = new Vector2(edge.x, edge.y);
      ApplyDistanceFeedbackToScreenPresentation(ref screenPosition, ref edgePosition);
      screenRadius = Mathf.Max(18f, Vector2.Distance(screenPosition, edgePosition));
      return true;
    }

    public bool TryGetLastPurificationAnchorScreenPresentation(out Vector2 screenPosition, out float screenRadius)
    {
      screenPosition = Vector2.zero;
      screenRadius = 0f;
      if (_camera == null || !_hasLastPurificationAnchor)
      {
        return false;
      }

      var center = _camera.WorldToScreenPoint(_lastPurificationAnchorWorldPosition);
      if (center.z <= 0f)
      {
        return false;
      }

      var edge = _camera.WorldToScreenPoint(
        _lastPurificationAnchorWorldPosition + _camera.transform.right * _lastPurificationAnchorWorldRadius);
      screenPosition = new Vector2(center.x, center.y);
      var edgePosition = new Vector2(edge.x, edge.y);
      ApplyDistanceFeedbackToScreenPresentation(ref screenPosition, ref edgePosition);
      screenRadius = Mathf.Max(18f, Vector2.Distance(screenPosition, edgePosition));
      return true;
    }

    public bool TryGetCalibrationTargetScreenPresentation(out Vector2 screenPosition, out float screenRadius)
    {
      screenPosition = Vector2.zero;
      screenRadius = 0f;
      if (!_calibrationActive ||
          _camera == null ||
          _calibrationTargetRoot == null ||
          !_calibrationTargetRoot.activeInHierarchy)
      {
        return false;
      }

      var center = _camera.WorldToScreenPoint(_calibrationTargetRoot.transform.position);
      if (center.z <= 0f)
      {
        return false;
      }

      var pulseScale = Mathf.Max(
        _calibrationTargetRoot.transform.lossyScale.x,
        _calibrationTargetRoot.transform.lossyScale.y);
      var worldRadius = _calibrationTargetWorldSize * pulseScale * 0.52f;
      var edge = _camera.WorldToScreenPoint(
        _calibrationTargetRoot.transform.position + _camera.transform.right * worldRadius);
      screenPosition = new Vector2(center.x, center.y);
      var edgePosition = new Vector2(edge.x, edge.y);
      ApplyDistanceFeedbackToScreenPresentation(ref screenPosition, ref edgePosition);
      screenRadius = Mathf.Max(18f, Vector2.Distance(screenPosition, edgePosition));
      return true;
    }

    private void ApplyDistanceFeedbackToScreenPresentation(ref Vector2 center, ref Vector2 edge)
    {
      if (_distanceCameraFeedback == null)
      {
        return;
      }

      center = _distanceCameraFeedback.WorldSourceScreenToOutputScreen(center);
      edge = _distanceCameraFeedback.WorldSourceScreenToOutputScreen(edge);
    }

    public bool TryGetTutorialTargetScreenPresentation(out Vector2 screenPosition, out float screenRadius)
    {
      return TryGetTargetScreenPresentation(_tutorialOrbitTargetId, out screenPosition, out screenRadius);
    }

    public void PlayTutorialFeedback(TutorialFeedbackCue cue)
    {
      if (!_tutorialMode || IsTutorialInputSuspended)
      {
        return;
      }

      StopTutorialFeedback();
      var clip = GetTutorialFeedbackClip(cue);
      var volumeScale = GetTutorialFeedbackVolumeScale(cue);
      PlayFeedbackClip(_tutorialFeedbackAudioSource, clip, _freezeFeedbackVolume * volumeScale);

      if (cue == TutorialFeedbackCue.ExperienceComplete)
      {
        _tutorialProgressGlowUntil = Time.time + 0.9f;
      }
    }

    public void StopTutorialFeedback()
    {
      if (_tutorialFeedbackAudioSource != null)
      {
        _tutorialFeedbackAudioSource.Stop();
      }
    }

    public void SetTutorialMode(bool enabled)
    {
      if (enabled)
      {
        if (!_tutorialMode)
        {
          CaptureTutorialExperienceSnapshot();
        }
        _tutorialMode = true;
        return;
      }

      ResumeFormalGameFlow();
    }

    public void SetTutorialRandomSpawningPaused(bool paused)
    {
      if (!_tutorialMode && paused)
      {
        return;
      }

      var wasPaused = IsTutorialRandomSpawningPaused;
      _tutorialRandomSpawningPaused = paused;
      if (wasPaused && !IsTutorialRandomSpawningPaused && CanScheduleFormalFlow())
      {
        ScheduleNextSpawn(0.45f);
      }
    }

    public void SetTutorialRandomCrisisPaused(bool paused)
    {
      if (!_tutorialMode && paused)
      {
        return;
      }

      var wasPaused = IsTutorialRandomCrisisPaused;
      _tutorialRandomCrisisPaused = paused;
      if (wasPaused && !IsTutorialRandomCrisisPaused && CanScheduleFormalFlow())
      {
        ScheduleNextCrisis();
      }
    }

    public void SetTutorialSessionTimerPaused(bool paused)
    {
      if (!_tutorialMode && paused)
      {
        return;
      }

      if (paused == _tutorialSessionTimerPaused)
      {
        return;
      }

      if (paused)
      {
        _tutorialSessionTimerPaused = true;
        _tutorialSessionTimerPausedAt = Time.time;
        return;
      }

      ResumeTutorialSessionTimer();
    }

    public void SetTutorialCollectionInputPaused(bool paused)
    {
      if (!_tutorialMode && paused)
      {
        return;
      }

      _tutorialCollectionInputPaused = paused;
      ResetPushAwayInputState();
    }

    public int SpawnTutorialOrbitTarget()
    {
      if (!_tutorialMode || _gameplayState != GameplayState.Orbiting || _sessionEnded)
      {
        return NoTargetId;
      }

      _tutorialOrbitTargetId = SpawnOrbitBlock(
        0f,
        0f,
        (_blockWorldSizeRange.x + _blockWorldSizeRange.y) * 0.5f);
      return _tutorialOrbitTargetId;
    }

    public int SpawnTutorialCrisisTargets(int count)
    {
      if (!_tutorialMode || _gameplayState != GameplayState.Orbiting || _sessionEnded || count <= 0)
      {
        return 0;
      }

      BeginCrisis(count);
      return count;
    }

    public void ResumeFormalGameFlow()
    {
      var wasTutorialMode = _tutorialMode;
      var resumeRandomSpawning = IsTutorialRandomSpawningPaused;
      var resumeRandomCrisis = IsTutorialRandomCrisisPaused;
      ResumeTutorialSessionTimer();

      _tutorialRandomSpawningPaused = false;
      _tutorialRandomCrisisPaused = false;
      _tutorialCollectionInputPaused = false;
      _tutorialMode = false;
      _tutorialOrbitTargetId = NoTargetId;
      StopTutorialFeedback();
      if (wasTutorialMode)
      {
        RestoreTutorialExperienceSnapshot();
      }

      if (!CanScheduleFormalFlow())
      {
        return;
      }

      if (resumeRandomSpawning)
      {
        ScheduleNextSpawn(0.45f);
      }

      if (resumeRandomCrisis)
      {
        ScheduleNextCrisis();
      }
    }

    private void CaptureTutorialExperienceSnapshot()
    {
      _tutorialStartingCollectedSampleCount = _collectedSampleCount;
      _tutorialStartingTotalSamplesCollected = _totalSamplesCollected;
      _tutorialStartingSampleProgress = _sampleProgress;
      _hasTutorialExperienceSnapshot = true;
    }

    private void RestoreTutorialExperienceSnapshot()
    {
      if (!_hasTutorialExperienceSnapshot)
      {
        return;
      }

      _collectedSampleCount = _tutorialStartingCollectedSampleCount;
      _totalSamplesCollected = _tutorialStartingTotalSamplesCollected;
      _sampleProgress = _tutorialStartingSampleProgress;
      _tutorialProgressGlowUntil = -1f;
      _hasTutorialExperienceSnapshot = false;
      UpdateProgressBarVisual();
      InvokeSignalSafely(ExperienceProgressChanged, new ExperienceProgressSignal(
        _collectedSampleCount,
        GetCurrentUpgradeSampleRequirement(),
        _sampleProgress), nameof(ExperienceProgressChanged));
    }

    private bool CanScheduleFormalFlow()
    {
      return _gameFlowStarted &&
             !_calibrationActive &&
             _distanceTracker.HasBaseline &&
             !_distanceTracker.IsTooClose &&
             !_sessionEnded &&
             !_firstLevelModalPaused &&
             !_firstLevelRandomFlowPaused &&
             _gameplayState == GameplayState.Orbiting;
    }

    private bool HasAnyLiveGameplayBlock()
    {
      for (var i = 0; i < _blocks.Count; i++)
      {
        if (_blocks[i] != null && _blocks[i].GameObject != null)
        {
          return true;
        }
      }

      return false;
    }

    private void ResumeTutorialSessionTimer()
    {
      if (!_tutorialSessionTimerPaused)
      {
        return;
      }

      if (_sessionStartedAt >= 0f && _tutorialSessionTimerPausedAt >= 0f)
      {
        var effectivePauseStart = Mathf.Max(_sessionStartedAt, _tutorialSessionTimerPausedAt);
        _sessionStartedAt += Mathf.Max(0f, Time.time - effectivePauseStart);
      }

      _tutorialSessionTimerPaused = false;
      _tutorialSessionTimerPausedAt = -1f;
    }

    private void Start()
    {
      _protocolDay = Mathf.Clamp(PlayerPrefs.GetInt(ProtocolDayPrefsKey, 1), 1, 14);
      _currentUpgradeSampleRequirement = GetBaseUpgradeSampleRequirement();
      EnsureCamera();
      CreateRuntimeSprite();
      CreateRuntimeCircleSprite();
      CreateRuntimeUiSprites();
      CreateBackgroundVisual();
      EnsureDistanceCameraFeedback();
      _softFocusField = SoftFocusFieldController.EnsureExists(this);
      CreateGazeIndicator();
      CreatePlayerMarker();
      CreateCalibrationTarget();
      CreateBlackoutOverlay();
      CreatePurificationWave();
      CreateProgressBar();
      _distanceCameraFeedback?.RegisterHudRoot(_progressBarRoot);
      CreateFreezeFeedbackAudio();
      EnsureModuleUpgradeView();
      realGazeScreenPosition = GetSafeInitialGazePosition();
      _rawGazeScreenPosition = realGazeScreenPosition;
      WarnIfEyeHardwareMissing();
      OffScreenEyeBreakController.EnsureExists(this);
      BeginGameFlow();
    }

    private void Update()
    {
      if (UnityEngine.Input.GetKeyDown(KeyCode.F1))
      {
        _showDebugHud = !_showDebugHud;
      }

      UpdateEyeInputFromPlugin();
      UpdateFaceDistanceFromPlugin();
      UpdateDistanceSessionState();
      UpdateDistanceCameraFeedback();

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

      if (!_distanceTracker.HasBaseline)
      {
        UpdatePlayerMarker();
        UpdateGazeIndicator();
        UpdateBlackoutOverlay();
        UpdateObservationMetrics();
        return;
      }

      if (_firstLevelModalPaused)
      {
        _blinkQueued = false;
        UpdatePlayerMarker();
        UpdateGazeIndicator();
        UpdateBlackoutOverlay();
        return;
      }

      if (IsTutorialInputSuspended)
      {
        SuspendTutorialInputForTrackingLoss();
        UpdateProgressBarVisual();
        UpdatePlayerMarker();
        UpdateGazeIndicator();
        UpdateBlackoutOverlay();
        UpdateModuleCardVisuals();
        UpdateObservationMetrics();
        return;
      }

      if (HandleFormalCrisisTrackingPause())
      {
        UpdateProgressBarVisual();
        UpdatePlayerMarker();
        UpdateGazeIndicator();
        UpdateBlackoutOverlay();
        UpdateObservationMetrics();
        return;
      }

      RemoveDeadBlocks();
      UpdateProgressBarVisual();
      if (_gameplayState == GameplayState.ModuleUpgrade)
      {
        SetGameplayPresentationForUpgrade(true);
        UpdateModuleUpgradeSelection();
        UpdatePlayerMarker();
        UpdateGazeIndicator();
        UpdateObservationMetrics();
        return;
      }

      UpdateGameplayState();
      if (_tutorialMode)
      {
        UpdateHoverState();
      }
      else
      {
        DisableNormalTargetLock();
      }
      UpdateBlocksByGameplayState();
      UpdateSoftFocusPurification();
      UpdateSampleCollection();
      ConsumeBlinkForHarvest();
      UpdatePlayerMarker();
      UpdateGazeIndicator();
      UpdateBlackoutOverlay();
      UpdateModuleCardVisuals();
      UpdateObservationMetrics();
    }

    private void OnGUI()
    {
      EnsureHudStyle();
      if (_gameplayState == GameplayState.ModuleUpgrade)
      {
        return;
      }

      DrawHardwareWarningOverlay();
      DrawSessionReportOverlay();

      if (!_showDebugHud)
      {
        return;
      }

      GUILayout.BeginArea(new Rect(18f, 18f, Mathf.Min(820f, Screen.width - 36f), 390f));
      GUILayout.Label("Edge Orbit & Harvest MVP // Hardware Eye Input", _hudStyle);
      GUILayout.Label($"Gaze sensor: {realGazeScreenPosition.x:F0}, {realGazeScreenPosition.y:F0}   Raw: {_rawGazeScreenPosition.x:F0}, {_rawGazeScreenPosition.y:F0}", _hudStyle);
      GUILayout.Label(GetHardwareStatusLine(), _hudStyle);
      GUILayout.Label(GetEyeClosedStatusLine(), _hudStyle);
      if (_calibrationActive)
      {
        GUILayout.Label($"Calibration: {_calibrationIndex + 1} / {_calibrationTargets.Length}   Look at the soft target, then blink gently.", _hudStyle);
      }
      else
      {
        GUILayout.Label(GetGameplayStatusLine(), _hudStyle);
        GUILayout.Label(GetFreezeResultStatusLine(), _hudStyle);
        var faceBase = _distanceTracker.HasBaseline ? _distanceTracker.BaselineFaceScale.ToString("F5") : "--";
        var currentFace = _distanceTracker.HasValidSample ? _distanceTracker.CurrentFaceScale.ToString("F5") : "--";
        var ratio = _distanceTracker.HasBaseline && _distanceTracker.HasValidSample ? _distanceTracker.DistanceRatio.ToString("F3") : "--";
        var cameraAmount = _distanceCameraFeedback != null ? _distanceCameraFeedback.CameraFeedbackAmount : 0f;
        var nearAmount = _distanceCameraFeedback != null ? _distanceCameraFeedback.NearAmount : 0f;
        var distortion = _distanceCameraFeedback != null ? _distanceCameraFeedback.CurrentDistortionStrength : 0f;
        var overscan = _distanceCameraFeedback != null ? _distanceCameraFeedback.CurrentOverscanScale : 1f;
        GUILayout.Label($"baselineFaceScale: {faceBase}   currentFaceScale: {currentFace}   distanceRatio: {ratio}", _hudStyle);
        GUILayout.Label($"Distance State: {SessionDistanceTracker.GetStateLabel(_distanceTracker.State)}   Baseline samples: {_distanceTracker.BaselineSampleCount}   Spread: {_distanceTracker.BaselineRelativeSpread:P1}", _hudStyle);
        GUILayout.Label($"Push Away Ready: {_pushAwayReady}   Armed: {_distanceTracker.IsPushAwayArmed}   Push Away Triggered: {_distanceTracker.PushAwayTriggeredSinceRearm}   Too Close: {_distanceTracker.IsTooClose}", _hudStyle);
        GUILayout.Label($"Camera Feedback Amount: {cameraAmount:F3}   nearAmount: {nearAmount:F3}   Distortion: {distortion:F3}   Overscan: {overscan:F3}   Sample bar: {_collectedSampleCount}/{GetCurrentUpgradeSampleRequirement()} ({_sampleProgress:P0})", _hudStyle);
        if (_softFocusField != null)
        {
          GUILayout.Label($"Soft Focus: {_softFocusField.GazeState}   Scale: {_softFocusField.FieldScale:F2}   Blink Health: {_softFocusField.SecondsSinceLastBlink:F1}s   Capacity: {_softFocusField.ConcurrentCapacity}", _hudStyle);
        }
        GUILayout.Label($"Orbiting: {CountState(BlockState.Orbiting)} / {_maxOrbitingBlocks}   Crisis: {CountState(BlockState.Crisis)}   Converted: {_harvestedCount}", _hudStyle);
        GUILayout.Label("Keep a comfortable central gaze. Targets purify automatically inside the field.", _hudStyle);
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

    private void BeginGameFlow()
    {
      if (_gameFlowStarted)
      {
        return;
      }

      _gameFlowStarted = true;
      SetProgressBarVisible(true);
      SetupCalibration();
      _nextSpawnAt = float.PositiveInfinity;
      _nextCrisisAt = float.PositiveInfinity;
      RefreshTutorialReadiness();
    }

    private void RefreshTutorialReadiness(bool? inputReadyOverride = null)
    {
      var snapshot = EyeInputDebugState.Latest;
      var inputReady = !_autoReadKeepBlinkingEyeInput ||
                       (inputReadyOverride ?? (snapshot.FaceDetected && snapshot.HasGazeScreenPosition));
      var ready = inputReady && _calibrationComplete && _distanceTracker.HasBaseline && !_sessionEnded;
      if (_tutorialReady == ready)
      {
        return;
      }

      _tutorialReady = ready;
      InvokeSignalSafely(TutorialReadinessChanged, ready, nameof(TutorialReadinessChanged));
    }

    private void SuspendTutorialInputForTrackingLoss()
    {
      _blinkQueued = false;
      _hoveredBlock = null;
      _lastHoveredBlock = null;
      ResetPushAwayInputState();
      PublishTargetLockChangedIfNeeded();
    }

    private void SetProgressBarVisible(bool visible)
    {
      if (_progressBarRoot != null && _progressBarRoot.activeSelf != visible)
      {
        _progressBarRoot.SetActive(visible);
      }
    }

    private void EnsureModuleUpgradeView()
    {
      if (_moduleUpgradeView == null)
      {
        _moduleUpgradeView = GetComponent<FirstLevelUpgradeView>();
      }

      if (_moduleUpgradeView == null)
      {
        _moduleUpgradeView = gameObject.AddComponent<FirstLevelUpgradeView>();
      }

      _moduleUpgradeView.EnsureCreated();
      _moduleUpgradeView.SetInstalledModules(_installedModuleOrder);
    }

    private void SetGameplayPresentationForUpgrade(bool upgradeVisible)
    {
      for (var i = 0; i < _blocks.Count; i++)
      {
        var block = _blocks[i];
        if (block == null || block.GameObject == null)
        {
          continue;
        }

        if (block.Renderer != null)
        {
          block.Renderer.enabled = !upgradeVisible;
        }
        if (block.GlowRenderer != null)
        {
          block.GlowRenderer.enabled = !upgradeVisible;
        }
      }

      if (_playerMarkerRoot != null)
      {
        _playerMarkerRoot.SetActive(!upgradeVisible && _tutorialMode);
      }
      if (_gazeIndicatorRoot != null)
      {
        _gazeIndicatorRoot.SetActive(false);
      }
      if (_calibrationTargetRoot != null)
      {
        _calibrationTargetRoot.SetActive(!upgradeVisible && _calibrationActive);
      }
    }

    private void SetFirstLevelPresentationHidden(bool hidden)
    {
      for (var i = 0; i < _blocks.Count; i++)
      {
        var block = _blocks[i];
        if (block == null || block.GameObject == null)
        {
          continue;
        }

        if (block.Renderer != null)
        {
          block.Renderer.enabled = !hidden;
        }
        if (block.GlowRenderer != null)
        {
          block.GlowRenderer.enabled = !hidden;
        }
      }

      if (_playerMarkerRoot != null)
      {
        _playerMarkerRoot.SetActive(!hidden && _tutorialMode);
      }
      if (_gazeIndicatorRoot != null && hidden)
      {
        _gazeIndicatorRoot.SetActive(false);
      }

      SetProgressBarVisible(!hidden);
      _moduleUpgradeView?.SetHudVisible(!hidden);
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

    private FirstLevelModuleDefinition GetModuleDefinitionForCard(int cardIndex)
    {
      if (cardIndex >= 0 && cardIndex < _currentModuleOffer.Count)
      {
        return FirstLevelUpgradeCatalog.Get(_currentModuleOffer[cardIndex]);
      }

      return FirstLevelUpgradeCatalog.Get(FirstLevelModuleId.ChainBlink);
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
      var calibrationHeaderClearance = _calibrationActive ? 38f * scale : 0f;
      var rect = new Rect(
        safeRect.center.x - width * 0.5f,
        safeRect.yMin + 6f * scale + calibrationHeaderClearance,
        width,
        height);

      DrawRoundedPanel(rect, KeepBlinkingTheme.SurfaceOverlay, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.WarningSoft, 0.72f), KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.WarningSoft, 0.12f), 6f * scale);
      GUI.Label(new Rect(rect.x + 20f * scale, rect.y + 14f * scale, rect.width - 40f * scale, 30f * scale), "Observation Signal Not Ready", _warningTitleStyle);
      GUI.Label(
        new Rect(rect.x + 20f * scale, rect.y + 46f * scale, rect.width - 40f * scale, rect.height - 56f * scale),
        "Adjust the lighting, clear any obstruction, and keep a relaxed face. Observation resumes automatically once your face is detected.",
        _warningBodyStyle);
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
        "You completed a gentle screen-gaze interruption ritual today. The system recorded your natural blinks, rest closures, and distance resets.",
        _reportBodyStyle);

      var singleColumn = rect.width < 460f * scale;
      var columns = singleColumn ? 1 : 2;
      var cellGap = 12f * scale;
      var cellWidth = (rect.width - 60f * scale - cellGap * (columns - 1)) / columns;
      var cellHeight = (singleColumn ? 64f : 76f) * scale;
      var metrics = new (string Label, string Value)[]
      {
        ("Natural blink count", _sessionBlinkCount.ToString()),
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
      var uiScale = GetMobileUiScale();
      var safeRect = GetSafeAreaScreenRect(12f * uiScale);
      GUI.Label(new Rect(safeRect.x, safeRect.y + 8f * uiScale, safeRect.width, 34f * uiScale), "CHOOSE A MODULE", _moduleHeaderStyle);
      var instruction = _moduleChoicePending
        ? "INSTALLED"
        : "CLICK TO INSTALL";
      GUI.Label(new Rect(safeRect.x, safeRect.y + 40f * uiScale, safeRect.width, 24f * uiScale), instruction, _moduleInstructionStyle);

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

        var definition = GetModuleDefinitionForCard(card.Index);
        var padX = 18f * uiScale;
        var previewWidth = Mathf.Clamp(rect.width * 0.28f, 68f * uiScale, 98f * uiScale);
        var textWidth = rect.width - padX * 3f - previewWidth;
        var tagWidth = Mathf.Min(108f * uiScale, textWidth * 0.62f);
        var tagRect = new Rect(rect.x + padX, rect.y + 13f * uiScale, tagWidth, 24f * uiScale);
        var titleRect = new Rect(rect.x + padX, rect.y + 43f * uiScale, textWidth, 27f * uiScale);
        var bodyRect = new Rect(rect.x + padX, rect.y + 70f * uiScale, textWidth, 38f * uiScale);
        var deltaRect = new Rect(rect.x + padX, rect.yMax - 34f * uiScale, textWidth, 24f * uiScale);
        var previewRect = new Rect(rect.xMax - padX - previewWidth, rect.center.y - previewWidth * 0.42f, previewWidth, previewWidth * 0.84f);

        DrawRoundedPanel(tagRect, KeepBlinkingTheme.WithAlpha(definition.AccentColor, 0.16f), KeepBlinkingTheme.WithAlpha(definition.AccentColor, 0.66f), KeepBlinkingTheme.WithAlpha(definition.AccentColor, 0.04f), 0f);
        var contentAlpha = _moduleChoicePending && card.Index != _selectedModuleCardIndex ? 0.12f : 1f;
        GUI.color = new Color(1f, 1f, 1f, contentAlpha);
        GUI.Label(tagRect, definition.CategoryLabel, _cardTagStyle);
        GUI.Label(titleRect, definition.Title, _cardTitleStyle);
        GUI.Label(bodyRect, definition.Description, _cardBodyStyle);
        GUI.Label(deltaRect, definition.Delta, _cardDeltaStyle);
        GUI.color = Color.white;
        if (contentAlpha > 0.5f)
        {
          DrawModulePreview(definition, previewRect, card.Index == _selectedModuleCardIndex);
        }
      }
    }

    private void DrawModulePreview(FirstLevelModuleDefinition definition, Rect rect, bool selected)
    {
      var phase = Mathf.Repeat(Time.unscaledTime * 0.62f + (int)definition.Id * 0.07f, 1f);
      var center = rect.center;
      var unit = Mathf.Min(rect.width, rect.height);
      var pulse = 1f + Mathf.Sin(Time.unscaledTime * 3f) * (selected ? 0.06f : 0.025f);
      DrawModuleIcon(definition.Category, center, unit * 0.34f * pulse, definition.AccentColor, phase, definition.Tier);
    }

    private void DrawInstalledModuleSlots()
    {
      if (_installedModuleOrder.Count == 0 || _gameplayState == GameplayState.SessionReport)
      {
        return;
      }

      EnsurePresentationStyles();
      var scale = GetMobileUiScale();
      var safeRect = GetSafeAreaScreenRect(10f * scale);
      var slotSize = 42f * scale;
      var gap = 8f * scale;
      var totalWidth = _installedModuleOrder.Count * slotSize + Mathf.Max(0, _installedModuleOrder.Count - 1) * gap;
      var startX = safeRect.center.x - totalWidth * 0.5f;
      var y = safeRect.yMax - slotSize - 38f * scale;

      for (var i = 0; i < _installedModuleOrder.Count; i++)
      {
        var id = _installedModuleOrder[i];
        var definition = FirstLevelUpgradeCatalog.Get(id);
        var rect = new Rect(startX + i * (slotSize + gap), y, slotSize, slotSize);
        _moduleFlashUntil.TryGetValue(id, out var flashUntil);
        var flash = Mathf.Clamp01((flashUntil - Time.unscaledTime) / 0.72f);
        var glow = KeepBlinkingTheme.WithAlpha(definition.AccentColor, Mathf.Lerp(0.05f, 0.48f, flash));
        DrawRoundedPanel(
          flash > 0f ? ExpandRect(rect, 3f * flash * scale) : rect,
          KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceElevated, 0.92f),
          KeepBlinkingTheme.WithAlpha(definition.AccentColor, Mathf.Lerp(0.38f, 0.92f, flash)),
          glow,
          2f * scale);

        var categoryLevel = CountInstalledModulesInCategory(definition.Category);
        DrawModuleIcon(definition.Category, rect.center, slotSize * 0.24f, definition.AccentColor, Time.unscaledTime * 0.3f, categoryLevel);
      }
    }

    private void DrawModuleIcon(
      FirstLevelModuleCategory category,
      Vector2 center,
      float radius,
      Color accent,
      float phase,
      int evolutionLevel)
    {
      if (_circleTexture == null)
      {
        return;
      }

      var haloRadius = radius * (1.35f + 0.08f * Mathf.Sin(phase * Mathf.PI * 2f));
      GUI.color = KeepBlinkingTheme.WithAlpha(accent, 0.12f);
      GUI.DrawTexture(new Rect(center.x - haloRadius, center.y - haloRadius, haloRadius * 2f, haloRadius * 2f), _circleTexture);

      switch (category)
      {
        case FirstLevelModuleCategory.Blink:
        {
          var satelliteCount = Mathf.Clamp(evolutionLevel, 1, 3);
          DrawModuleDot(center, radius * 0.46f, accent, 0.88f);
          for (var i = 0; i < satelliteCount; i++)
          {
            var angle = phase * Mathf.PI * 2f + i * Mathf.PI * 2f / satelliteCount;
            DrawModuleDot(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, radius * 0.23f, accent, 0.62f);
          }
          break;
        }
        case FirstLevelModuleCategory.Rest:
        {
          var closure = Mathf.SmoothStep(0f, 1f, Mathf.PingPong(phase * 1.4f, 1f));
          DrawRect(new Rect(center.x - radius, center.y - radius * (0.55f - closure * 0.44f), radius * 2f, 2f), KeepBlinkingTheme.WithAlpha(accent, 0.86f));
          DrawRect(new Rect(center.x - radius, center.y + radius * (0.55f - closure * 0.44f), radius * 2f, 2f), KeepBlinkingTheme.WithAlpha(accent, 0.52f));
          break;
        }
        case FirstLevelModuleCategory.Distance:
        {
          var travel = Mathf.SmoothStep(0f, 1f, Mathf.PingPong(phase * 1.3f, 1f));
          DrawModuleDot(center - new Vector2(radius * 0.72f, 0f), radius * 0.5f, accent, 0.68f);
          var phoneCenter = center + new Vector2(Mathf.Lerp(radius * 0.22f, radius * 1.05f, travel), 0f);
          DrawRect(new Rect(phoneCenter.x - radius * 0.22f, phoneCenter.y - radius * 0.58f, radius * 0.44f, radius * 1.16f), KeepBlinkingTheme.WithAlpha(accent, 0.72f));
          break;
        }
        default:
        {
          var colors = new Color[] { KeepBlinkingTheme.AccentPrimary, new Color32(0x91, 0xB8, 0xD0, 0xFF), KeepBlinkingTheme.AccentWarm };
          for (var i = 0; i < 3; i++)
          {
            var angle = phase * Mathf.PI * 2f + i * Mathf.PI * 2f / 3f;
            DrawModuleDot(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * 0.86f, radius * 0.28f, colors[i], 0.8f);
          }
          break;
        }
      }

      var evolutionPips = Mathf.Clamp(evolutionLevel, 1, 3);
      var pipSpacing = radius * 0.34f;
      for (var i = 0; i < evolutionPips; i++)
      {
        var pipX = center.x + (i - (evolutionPips - 1) * 0.5f) * pipSpacing;
        DrawModuleDot(new Vector2(pipX, center.y + radius * 1.22f), radius * 0.09f, accent, 0.74f);
      }

      GUI.color = Color.white;
    }

    private void DrawModuleDot(Vector2 center, float radius, Color color, float alpha)
    {
      GUI.color = KeepBlinkingTheme.WithAlpha(color, alpha);
      GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), _circleTexture);
      GUI.color = Color.white;
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
          return "State: module upgrade   Click a card to install";
        default:
          return $"State: {_gameplayState}";
      }
    }

    private void UpdateEyeInputFromPlugin()
    {
      if (!_autoReadKeepBlinkingEyeInput)
      {
        RefreshTutorialReadiness();
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

        RefreshTutorialReadiness(false);
        ResetSoftBlinkDetection();
        TryTreatLostFaceAsClosedDuringCrisis();
        MaintainClosedEyesDuringLostFace();
        return;
      }

      _lastFaceDetectedAt = Time.time;
      UpdateBlinkBaseline(snapshot);
      UpdateEyesClosedState(snapshot);
      UpdateSoftBlinkDetection(snapshot);

      if (snapshot.HasGazeScreenPosition)
      {
        var providerGazeScreenPosition = snapshot.GazeScreenPosition;
        var usingExperimentalProvider = false;
        if (GazeProviderComparisonController.TryGetGameplayGazeScreenPosition(out var experimentalGazeScreenPosition))
        {
          providerGazeScreenPosition = experimentalGazeScreenPosition;
          usingExperimentalProvider = true;
        }

        _rawGazeScreenPosition = Vector2.Lerp(
          _rawGazeScreenPosition,
          providerGazeScreenPosition,
          1f - Mathf.Exp(-_gazeSmoothSpeed * Time.deltaTime));
        AddRecentRawGazeSample(_rawGazeScreenPosition);
        realGazeScreenPosition = usingExperimentalProvider
          ? _rawGazeScreenPosition
          : ApplyGazeCalibration(_rawGazeScreenPosition);
      }

      if (ConsumePluginBlink(snapshot))
      {
        TriggerHardwareBlink();
      }

      var crisisEyeInputReady = _gameplayState == GameplayState.Crisis ||
                                _gameplayState == GameplayState.EyesClosedFreeze;
      RefreshTutorialReadiness(snapshot.FaceDetected &&
                               (snapshot.HasGazeScreenPosition || crisisEyeInputReady));
    }

    private void UpdateSoftBlinkDetection(EyeInputDebugSnapshot snapshot)
    {
      if (!snapshot.FaceDetected)
      {
        ResetSoftBlinkDetection();
        return;
      }

      var now = Time.time;
      var averageOpen = (snapshot.LeftEyeOpen + snapshot.RightEyeOpen) * 0.5f;
      var baselineAverage = GetBlinkBaselineAverage();
      var closedByExistingThreshold = snapshot.IsBlinking ||
                                      (baselineAverage > 0f &&
                                       averageOpen <= baselineAverage * _relativeBlinkCloseRatio);
      var openByExistingThreshold = !snapshot.IsBlinking &&
                                    snapshot.LeftEyeOpen >= _openEyeReleaseThreshold &&
                                    snapshot.RightEyeOpen >= _openEyeReleaseThreshold;

      if (_softBlinkCandidateActive)
      {
        if (now - _softBlinkCandidateStartedAt > Mathf.Max(0.2f, _softBlinkMaximumClosedSeconds))
        {
          _softBlinkCandidateActive = false;
          _softBlinkCandidateStartedAt = -1f;
          _softBlinkStableOpenStartedAt = openByExistingThreshold ? now : -1f;
          return;
        }

        if (closedByExistingThreshold)
        {
          _softBlinkStableOpenStartedAt = -1f;
          return;
        }

        if (!openByExistingThreshold)
        {
          _softBlinkStableOpenStartedAt = -1f;
          return;
        }

        _softBlinkCandidateActive = false;
        _softBlinkCandidateStartedAt = -1f;
        _softBlinkStableOpenStartedAt = now;
        _softBlinkSerial++;
        InvokeSignalSafely(SoftBlinkPerformed, _softBlinkSerial, nameof(SoftBlinkPerformed));
        return;
      }

      if (openByExistingThreshold)
      {
        if (_softBlinkStableOpenStartedAt < 0f)
        {
          _softBlinkStableOpenStartedAt = now;
        }
        return;
      }

      if (closedByExistingThreshold &&
          _softBlinkStableOpenStartedAt >= 0f &&
          now - _softBlinkStableOpenStartedAt >= Mathf.Max(0.05f, _softBlinkStableOpenSeconds))
      {
        _softBlinkCandidateActive = true;
        _softBlinkCandidateStartedAt = now;
      }

      _softBlinkStableOpenStartedAt = -1f;
    }

    private void ResetSoftBlinkDetection()
    {
      _softBlinkCandidateActive = false;
      _softBlinkCandidateStartedAt = -1f;
      _softBlinkStableOpenStartedAt = -1f;
    }

    private void UpdateFaceDistanceFromPlugin()
    {
      if (_autoReadKeepBlinkingEyeInput)
      {
        var snapshot = EyeInputDebugState.Latest;
        if (snapshot.FaceDetected && snapshot.SmoothedFaceArea > 0.0001f)
        {
          faceDistance = snapshot.SmoothedFaceArea;
        }
      }
    }

    private void UpdateDistanceSessionState()
    {
      var snapshot = EyeInputDebugState.Latest;
      var sampleValid = _autoReadKeepBlinkingEyeInput
        ? snapshot.FaceDetected && IsValidFaceScale(snapshot.SmoothedFaceArea)
        : IsValidFaceScale(faceDistance);
      var currentFaceScale = _autoReadKeepBlinkingEyeInput ? snapshot.SmoothedFaceArea : faceDistance;
      var now = Time.unscaledTime;
      var update = _distanceTracker.Update(
        currentFaceScale,
        sampleValid,
        _calibrationComplete,
        CanUpdateDistanceState(sampleValid),
        HasCollectableSamples(),
        now,
        Time.unscaledDeltaTime,
        BuildDistanceSettings());

      _pushAwayReady = _distanceTracker.IsPushAwayReady;
      _pushAwayTriggerPending |= update.PushAwayTriggered;
      if (_softFocusHiddenByPushAway &&
          _distanceTracker.HasValidSample &&
          _distanceTracker.DistanceRatio >= _pushAwayRearmRatio)
      {
        _softFocusHiddenByPushAway = false;
      }

      if (update.BaselineCaptured)
      {
        Debug.Log(
          $"KeepBlinking fixed session distance baseline captured once: {_distanceTracker.BaselineFaceScale:F6} " +
          $"from {_distanceTracker.BaselineSampleCount} samples (P10-P90 spread {_distanceTracker.BaselineRelativeSpread:P1}).",
          this);
        InitializeFormalFlowAfterDistanceBaseline();
      }
      else if (update.BaselineRejected)
      {
        Debug.LogWarning(
          $"KeepBlinking rejected an unstable distance baseline window (relative spread {_distanceTracker.BaselineRelativeSpread:P1}). " +
          "Hold a comfortable, steady distance; baseline capture will retry without using an invalid value.",
          this);
      }
      else if (_calibrationComplete &&
               !_distanceTracker.HasBaseline &&
               _distanceTracker.BaselineCaptureElapsed(now) >= _distanceBaselineCaptureSeconds &&
               _distanceTracker.BaselineSampleCount < _distanceBaselineMinimumSamples &&
               now >= _nextDistanceBaselineWarningAt)
      {
        _nextDistanceBaselineWarningAt = now + 2f;
        Debug.LogWarning(
          $"KeepBlinking is still waiting for enough valid distance baseline samples " +
          $"({_distanceTracker.BaselineSampleCount}/{_distanceBaselineMinimumSamples}). No fallback baseline was applied.",
          this);
      }

      if (update.PushAwayReady)
      {
        InvokeSignalSafely(PushAwayCollectionReady, nameof(PushAwayCollectionReady));
      }
    }

    private SessionDistanceSettings BuildDistanceSettings()
    {
      return new SessionDistanceSettings(
        _distanceBaselineCaptureSeconds,
        _distanceBaselineMinimumSamples,
        _distanceBaselineMaximumRelativeSpread,
        _faceDistanceSmoothSpeed,
        _distanceNormalMinimumRatio,
        _distanceNormalMaximumRatio,
        _pushAwayTriggerRatio,
        _pushAwayHoldSeconds,
        _pushAwayRearmRatio,
        _pushAwayRearmHoldSeconds,
        _tooCloseEnterRatio,
        _tooCloseHoldSeconds,
        _tooCloseExitRatio);
    }

    private bool CanUpdateDistanceState(bool sampleValid)
    {
      return sampleValid &&
             Time.timeScale > 0f &&
             !_calibrationActive &&
             !_sessionEnded &&
             !_firstLevelModalPaused &&
             !_firstLevelRandomFlowPaused &&
             !_firstLevelBossTransitionActive &&
             !_firstLevelBossMode &&
             _gameplayState == GameplayState.Orbiting &&
             !(_tutorialMode && _tutorialCollectionInputPaused) &&
             !ShouldShowHardwareWarningOverlay();
    }

    private void InitializeFormalFlowAfterDistanceBaseline()
    {
      if (_formalFlowInitialized || !_distanceTracker.HasBaseline)
      {
        return;
      }

      _formalFlowInitialized = true;
      _sessionStartedAt = Time.time;
      ScheduleNextSpawn(0.35f);
      ScheduleNextCrisis();
      FirstLevelSessionController.EnsureExists(this);
      RefreshTutorialReadiness();
    }

    private void EnsureDistanceCameraFeedback()
    {
      if (_distanceCameraFeedback == null)
      {
        _distanceCameraFeedback = GetComponent<DistanceCameraFeedback>();
      }
      if (_distanceCameraFeedback == null)
      {
        _distanceCameraFeedback = gameObject.AddComponent<DistanceCameraFeedback>();
      }
      _distanceCameraFeedback.Configure(_camera, _backgroundRenderer);
    }

    private void UpdateDistanceCameraFeedback()
    {
      if (_distanceCameraFeedback == null)
      {
        return;
      }

      var feedbackAllowed = _distanceTracker.HasBaseline &&
                            CanUpdateDistanceState(_distanceTracker.HasValidSample);
      _distanceCameraFeedback.SetInput(
        _distanceTracker.DistanceRatio,
        _distanceTracker.HasValidSample,
        feedbackAllowed,
        feedbackAllowed && _distanceTracker.IsTooClose);
      _distanceCameraFeedback.Tick(Time.unscaledDeltaTime);
    }

    private static bool IsValidFaceScale(float value)
    {
      return value > 0.000001f && !float.IsNaN(value) && !float.IsInfinity(value);
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

      if (_tutorialMode &&
          (_gameplayState == GameplayState.Crisis || _gameplayState == GameplayState.EyesClosedFreeze))
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
      _dataSeedTexture = CreateDataSeedTexture(textureSize);
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

      _dataSeedSprite = Sprite.Create(
        _dataSeedTexture,
        new Rect(0f, 0f, textureSize, textureSize),
        new Vector2(0.5f, 0.5f),
        textureSize);
      _dataSeedSprite.name = "LowPolyDataSeedSprite";
      ApplyDataSeedGeometry(_dataSeedSprite);

      _backgroundSprite = Sprite.Create(
        _backgroundTexture,
        new Rect(0f, 0f, _backgroundTexture.width, _backgroundTexture.height),
        new Vector2(0.5f, 0.5f),
        _backgroundTexture.width);
      _backgroundSprite.name = "ObservationBackgroundSprite";
    }

    private Texture2D CreateDataSeedTexture(int size)
    {
      var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
      {
        name = "LowPolyDataSeedTexture",
        filterMode = FilterMode.Bilinear,
        wrapMode = TextureWrapMode.Clamp,
      };

      for (var y = 0; y < size; y++)
      {
        for (var x = 0; x < size; x++)
        {
          var u = x / Mathf.Max(1f, size - 1f);
          var v = y / Mathf.Max(1f, size - 1f);
          var directionalLight = Mathf.Clamp01((1f - u) * 0.42f + v * 0.58f);
          var value = Mathf.Lerp(0.84f, 1f, directionalLight);
          texture.SetPixel(x, y, new Color(value, value, value, 1f));
        }
      }

      texture.Apply();
      return texture;
    }

    private static void ApplyDataSeedGeometry(Sprite sprite)
    {
      const int sides = 11;
      var vertices = new Vector2[sides + 1];
      var triangles = new ushort[sides * 3];
      var center = sprite.rect.center;
      var baseRadius = Mathf.Min(sprite.rect.width, sprite.rect.height);
      vertices[0] = center;
      for (var i = 0; i < sides; i++)
      {
        var angle = Mathf.PI * 2f * i / sides + Mathf.PI * 0.5f;
        var radiusScale = i % 3 == 0 ? 0.48f : (i % 3 == 1 ? 0.455f : 0.47f);
        vertices[i + 1] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * (baseRadius * radiusScale);

        var triangleIndex = i * 3;
        triangles[triangleIndex] = 0;
        triangles[triangleIndex + 1] = (ushort)(i + 1);
        triangles[triangleIndex + 2] = (ushort)((i + 1) % sides + 1);
      }

      sprite.OverrideGeometry(vertices, triangles);
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

      for (var y = 0; y < height; y++)
      {
        var v = y / Mathf.Max(1f, height - 1f);
        for (var x = 0; x < width; x++)
        {
          var u = x / Mathf.Max(1f, width - 1f);
          var verticalTone = Mathf.SmoothStep(0f, 1f, v) * 0.2f;
          var baseColor = Color.Lerp(KeepBlinkingTheme.BackgroundPrimary, KeepBlinkingTheme.BackgroundSecondary, verticalTone);

          var edgeDistance = Mathf.Max(Mathf.Abs(u - 0.5f) * 1.72f, Mathf.Abs(v - 0.5f) * 1.12f);
          var vignette = Mathf.SmoothStep(0.5f, 1f, edgeDistance);
          baseColor = Color.Lerp(baseColor, KeepBlinkingTheme.BackgroundTertiary, vignette * 0.42f);

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

          baseColor = Color.Lerp(baseColor, KeepBlinkingTheme.GridTint, Mathf.Clamp01(gridLine) * KeepBlinkingTheme.GridTint.a);

          var grainSeed = Mathf.Sin((x + 17f) * 12.9898f + (y + 31f) * 78.233f) * 43758.5453f;
          var grain = (grainSeed - Mathf.Floor(grainSeed) - 0.5f) * 0.018f;
          baseColor.r = Mathf.Clamp01(baseColor.r + grain);
          baseColor.g = Mathf.Clamp01(baseColor.g + grain);
          baseColor.b = Mathf.Clamp01(baseColor.b + grain);

          var dustSeed = Mathf.Abs(Mathf.Sin((x + 11f) * 0.043f + (y + 7f) * 0.019f) * Mathf.Cos((x + 3f) * 0.013f - (y + 17f) * 0.031f));
          if (dustSeed > 0.9984f)
          {
            baseColor = Color.Lerp(baseColor, KeepBlinkingTheme.DustTint, 0.34f);
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
      var size = _gazeIndicatorWorldSize;
      CreateIndicatorPiece(
        "Gaze Halo",
        Vector3.zero,
        new Vector3(size, size, 1f),
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.11f),
        88,
        _circleSprite);
      CreateIndicatorPiece(
        "Gaze Core",
        Vector3.zero,
        new Vector3(size * 0.16f, size * 0.16f, 1f),
        KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentSoft, 0.5f),
        89,
        _circleSprite);
      _gazeIndicatorRoot.SetActive(false);
    }

    private void CreatePlayerMarker()
    {
      _playerMarkerRoot = new GameObject("Center Player Marker");
      _playerMarkerRoot.transform.SetParent(transform, false);

      var size = _playerMarkerWorldSize;
      CreatePlayerMarkerPiece("Player Halo", Vector3.zero, new Vector3(size * 1.05f, size * 1.05f, 1f), KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.045f), 8, _circleSprite);
      CreatePlayerMarkerPiece("Observation Core", Vector3.zero, new Vector3(size * 0.11f, size * 0.11f, 1f), KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentSoft, 0.38f), 13, _circleSprite);
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
      CreateCalibrationPiece("Calibration Soft Halo", Vector3.zero, new Vector3(size * 1.56f, size * 1.56f, 1f), KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentWarm, 0.11f), 102, _circleSprite);
      CreateCalibrationPiece("Calibration Data Seed", Vector3.zero, new Vector3(size, size, 1f), KeepBlinkingTheme.OrbitSignal, 104, _dataSeedSprite);
      CreateCalibrationPiece("Calibration Core Light", new Vector3(-size * 0.1f, size * 0.12f, 0f), new Vector3(size * 0.2f, size * 0.2f, 1f), KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentWarm, 0.34f), 105, _circleSprite);
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

      UpdateProgressBarVisual();
    }

    private void CreateFreezeFeedbackAudio()
    {
      _feedbackAudioSource = gameObject.AddComponent<AudioSource>();
      _feedbackAudioSource.playOnAwake = false;
      _feedbackAudioSource.loop = false;
      _feedbackAudioSource.volume = _freezeFeedbackVolume;
      _feedbackAudioSource.spatialBlend = 0f;

      _tutorialFeedbackAudioSource = gameObject.AddComponent<AudioSource>();
      _tutorialFeedbackAudioSource.playOnAwake = false;
      _tutorialFeedbackAudioSource.loop = false;
      _tutorialFeedbackAudioSource.volume = 1f;
      _tutorialFeedbackAudioSource.spatialBlend = 0f;

      _freezeStartedClip = CreateToneClip("Freeze Started Tone", 440f, 0.1f);
      _coverageCompleteClip = CreateToneClip("Coverage Complete Tone", 660f, 0.16f);
      _freezeInterruptedClip = CreateToneClip("Freeze Interrupted Tone", 220f, 0.12f);
      _freezeClearedClip = CreateToneClip("Freeze Cleared Tone", 880f, 0.18f);
      _tutorialFocusClip = CreateToneSequenceClip("Tutorial Focus Cue", new[] { 294f, 392f }, 0.11f, 0.015f, 0.19f);
      _tutorialBlinkClip = CreateToneSequenceClip("Tutorial Blink Cue", new[] { 277f, 349f }, 0.075f, 0.018f, 0.14f);
      _tutorialConvertedClip = CreateToneSequenceClip("Tutorial Converted Cue", new[] { 330f, 392f }, 0.09f, 0.012f, 0.17f);
      _tutorialPushAwayClip = CreateToneSequenceClip("Tutorial Push Away Cue", new[] { 247f, 330f }, 0.13f, 0.025f, 0.15f);
      _tutorialExperienceCompleteClip = CreateToneSequenceClip("Tutorial Experience Complete Cue", new[] { 294f, 370f, 440f }, 0.115f, 0.018f, 0.2f);
      _tutorialCountdownClip = CreateToneSequenceClip("Tutorial Countdown Beat", new[] { 220f, 294f }, 0.065f, 0.012f, 0.1f);
      _moduleInstalledClip = CreateToneSequenceClip("Module Installed Cue", new[] { 262f, 330f, 392f }, 0.075f, 0.014f, 0.13f);
      _moduleActivatedClip = CreateToneSequenceClip("Module Activated Cue", new[] { 294f, 370f }, 0.065f, 0.012f, 0.1f);
      _bossCloseRequestClip = CreateToneSequenceClip("Boss Close Request Cue", new[] { 196f, 247f }, 0.13f, 0.045f, 0.12f);
      _bossSuccessfulReleaseClip = CreateToneSequenceClip("Boss Successful Release Cue", new[] { 294f, 370f, 440f }, 0.1f, 0.018f, 0.16f);
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

    private AudioClip CreateToneSequenceClip(
      string clipName,
      float[] frequencies,
      float noteDuration,
      float gapDuration,
      float amplitude)
    {
      const int sampleRate = 44100;
      var safeFrequencies = frequencies ?? Array.Empty<float>();
      var noteSampleCount = Mathf.Max(1, Mathf.CeilToInt(sampleRate * Mathf.Max(0.02f, noteDuration)));
      var gapSampleCount = Mathf.Max(0, Mathf.CeilToInt(sampleRate * Mathf.Max(0f, gapDuration)));
      var totalSampleCount = Mathf.Max(1, safeFrequencies.Length * noteSampleCount + Mathf.Max(0, safeFrequencies.Length - 1) * gapSampleCount);
      var samples = new float[totalSampleCount];
      var writeOffset = 0;

      for (var noteIndex = 0; noteIndex < safeFrequencies.Length; noteIndex++)
      {
        var frequency = Mathf.Clamp(safeFrequencies[noteIndex], 80f, 720f);
        for (var i = 0; i < noteSampleCount && writeOffset + i < samples.Length; i++)
        {
          var normalized = i / (float)Mathf.Max(1, noteSampleCount - 1);
          var envelope = Mathf.Sin(Mathf.PI * normalized);
          envelope *= envelope;
          var t = i / (float)sampleRate;
          samples[writeOffset + i] = Mathf.Sin(Mathf.PI * 2f * frequency * t) * envelope * amplitude;
        }

        writeOffset += noteSampleCount + gapSampleCount;
      }

      var clip = AudioClip.Create(clipName, totalSampleCount, 1, sampleRate, false);
      clip.SetData(samples, 0);
      return clip;
    }

    private AudioClip GetTutorialFeedbackClip(TutorialFeedbackCue cue)
    {
      switch (cue)
      {
        case TutorialFeedbackCue.Focus:
          return _tutorialFocusClip;
        case TutorialFeedbackCue.BlinkLoop:
          return _tutorialBlinkClip;
        case TutorialFeedbackCue.Converted:
          return _tutorialConvertedClip;
        case TutorialFeedbackCue.PushAwayLoop:
          return _tutorialPushAwayClip;
        case TutorialFeedbackCue.ExperienceComplete:
          return _tutorialExperienceCompleteClip;
        case TutorialFeedbackCue.CountdownBeat:
          return _tutorialCountdownClip;
        default:
          return null;
      }
    }

    private static float GetTutorialFeedbackVolumeScale(TutorialFeedbackCue cue)
    {
      switch (cue)
      {
        case TutorialFeedbackCue.BlinkLoop:
          return 0.42f;
        case TutorialFeedbackCue.PushAwayLoop:
          return 0.46f;
        case TutorialFeedbackCue.ExperienceComplete:
          return 0.72f;
        case TutorialFeedbackCue.CountdownBeat:
          return 0.4f;
        default:
          return 0.56f;
      }
    }

    private void PlayFeedbackClip(AudioClip clip)
    {
      PlayFeedbackClip(_feedbackAudioSource, clip, _freezeFeedbackVolume);
    }

    private void PlayFeedbackClip(AudioSource source, AudioClip clip, float volume)
    {
      if (!_playFreezeFeedbackAudio || source == null || clip == null)
      {
        return;
      }

      source.PlayOneShot(clip, Mathf.Clamp01(volume));
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

      var center = GetPurificationCenterWorldPosition();
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
      _distanceTracker.ResetSession();
      _pushAwayReady = false;
      _softFocusHiddenByPushAway = false;
      _formalFlowInitialized = false;
      _nextDistanceBaselineWarningAt = -1f;

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
      RefreshTutorialReadiness();
      Debug.Log($"KeepBlinking gaze calibration complete. Scale={_calibrationScale}, Offset={_calibrationOffset}. Capturing the fixed session distance baseline next.");
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
      if (IsTutorialRandomSpawningPaused ||
          _firstLevelRandomFlowPaused ||
          _firstLevelModalPaused ||
          _offScreenEyeBreakPending ||
          _distanceTracker.IsTooClose)
      {
        return;
      }

      if (Time.time < _normalSpawnPausedUntil)
      {
        return;
      }

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
          if (!_firstLevelRandomFlowPaused &&
              !_firstLevelModalPaused &&
              !_distanceTracker.IsTooClose &&
              !IsTutorialRandomCrisisPaused &&
              Time.time >= _nextCrisisAt)
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
      BeginCrisis(_crisisSpawnCount);
    }

    private void BeginCrisis(int spawnCount)
    {
      _gameplayState = GameplayState.Crisis;
      _blinkQueued = false;
      _hasLastPurificationAnchor = false;
      _hoveredBlock = null;
      _lastHoveredBlock = null;
      PublishTargetLockChangedIfNeeded();
      _activeCrisisWaveId = ++_nextCrisisWaveId;
      var spawnedCount = SpawnCrisisBlocks(Mathf.Max(0, spawnCount));
      InvokeSignalSafely(CrisisStarted, spawnedCount, nameof(CrisisStarted));
    }

    private bool HandleFormalCrisisTrackingPause()
    {
      if (_tutorialMode ||
          (_gameplayState != GameplayState.Crisis && _gameplayState != GameplayState.EyesClosedFreeze))
      {
        _formalCrisisTrackingPaused = false;
        _formalCrisisTrackingPausedAt = -1f;
        return false;
      }

      if (!IsTrackingAvailable)
      {
        if (!_formalCrisisTrackingPaused)
        {
          _formalCrisisTrackingPaused = true;
          _formalCrisisTrackingPausedAt = Time.time;
        }
        _blinkQueued = false;
        return true;
      }

      if (!_formalCrisisTrackingPaused)
      {
        return false;
      }

      var pausedSeconds = _formalCrisisTrackingPausedAt >= 0f
        ? Mathf.Max(0f, Time.time - _formalCrisisTrackingPausedAt)
        : 0f;
      _formalCrisisTrackingPaused = false;
      _formalCrisisTrackingPausedAt = -1f;

      if (_gameplayState == GameplayState.EyesClosedFreeze && _eyesClosedStartedAt >= 0f)
      {
        _eyesClosedStartedAt += pausedSeconds;
        if (!isEyesClosed)
        {
          ResetCrisisCoverageAfterTrackingInterruption();
        }
      }

      return false;
    }

    private void ResetCrisisCoverageAfterTrackingInterruption()
    {
      _blinkQueued = false;
      _suppressBlinkHarvestFrame = Time.frameCount;
      _wasEyesClosed = false;
      _eyesClosedStartedAt = -1f;
      _purificationRadius = 0f;
      _coverageCuePlayed = false;
      _reopenWaveReleaseUntil = -1f;
      SetBlackoutVisible(false);
      SetPurificationWaveVisible(false);
      _gameplayState = GameplayState.Crisis;
      _lastFreezeResult = "TRACKING PAUSE: coverage reset";
      _lastFreezeResultAt = Time.time;
    }

    private void BeginEyesClosedFreeze()
    {
      if (CountState(BlockState.Crisis) <= 0)
      {
        return;
      }

      _gameplayState = GameplayState.EyesClosedFreeze;
      _lastPurificationAnchorWorldPosition = GetCrisisArrayCenterWorldPosition();
      _lastPurificationAnchorWorldRadius = Mathf.Max(
        0.05f,
        (_crisisBlockWorldSizeRange.x + _crisisBlockWorldSizeRange.y) * 0.25f);
      _hasLastPurificationAnchor = true;
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
      InvokeSignalSafely(EyesClosedFreezeStarted, nameof(EyesClosedFreezeStarted));
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

      _blinkQueued = false;
      _suppressBlinkHarvestFrame = Time.frameCount;
      var fullCoverageReached = _coverageCuePlayed;
      var closedSeconds = Time.time - _eyesClosedStartedAt;
      _totalClosedEyeRestSeconds += closedSeconds;
      SetBlackoutVisible(false);
      _wasEyesClosed = false;
      _eyesClosedStartedAt = -1f;
      _lastFreezeDuration = closedSeconds;
      _lastFreezeResultAt = Time.time;
      var clearedCount = fullCoverageReached ? ClearCrisisWithinCurrentRadius() : 0;
      if (fullCoverageReached && clearedCount > 0)
      {
        HandleSuccessfulRestModules();
      }
      var remainingCount = CountState(BlockState.Crisis);
      _purificationRadius = 0f;
      _coverageCuePlayed = false;

      if (!fullCoverageReached)
      {
        _reopenWaveReleaseUntil = -1f;
        SetPurificationWaveVisible(false);
        _lastFreezeResult = "RETRY: coverage incomplete";
        _gameplayState = GameplayState.Crisis;
        InvokeSignalSafely(CrisisReleaseInterrupted, nameof(CrisisReleaseInterrupted));
        return;
      }

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

      InvokeSignalSafely(ReopenReleaseCompleted, clearedCount, nameof(ReopenReleaseCompleted));
    }

    private void UpdatePurificationExpansion()
    {
      _purificationRadius += _purificationRadiusGrowthSpeed * Time.deltaTime;
      UpdatePurificationWaveVisual();

      if (!_coverageCuePlayed && AreAllActiveCrisisTargetsFullyCovered())
      {
        _coverageCuePlayed = true;
        PlayFeedbackClip(_coverageCompleteClip);
        InvokeSignalSafely(FullCoverageReached, nameof(FullCoverageReached));
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
          var requiredRadius = GetDistanceFromCenter(block.Transform.position) + GetBlockWorldRadius(block);
          if (requiredRadius <= _purificationRadius)
          {
            clearedCount++;
            StartCoroutine(HarvestRoutine(block));
          }
        }
      }

      return clearedCount;
    }

    private bool AreAllActiveCrisisTargetsFullyCovered()
    {
      var activeTargetCount = 0;
      for (var i = 0; i < _blocks.Count; i++)
      {
        var block = _blocks[i];
        if (block == null || block.GameObject == null || block.State != BlockState.Crisis)
        {
          continue;
        }

        activeTargetCount++;
        var requiredRadius = GetDistanceFromCenter(block.Transform.position) + GetBlockWorldRadius(block);
        if (requiredRadius > _purificationRadius)
        {
          return false;
        }
      }

      return activeTargetCount > 0;
    }

    private static float GetBlockWorldRadius(OrbitBlock block)
    {
      if (block == null || block.Renderer == null)
      {
        return 0f;
      }

      var extents = block.Renderer.bounds.extents;
      return Mathf.Max(extents.x, extents.y);
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
      ScheduleNextSpawn(Mathf.Max(0.45f, _normalSpawnPausedUntil - Time.time));
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

        farthest = Mathf.Max(
          farthest,
          GetDistanceFromCenter(block.Transform.position) + GetBlockWorldRadius(block));
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
      var center = GetPurificationCenterWorldPosition();
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

      var center = GetPurificationCenterWorldPosition();
      return Vector2.Distance(new Vector2(center.x, center.y), new Vector2(worldPosition.x, worldPosition.y));
    }

    private Vector3 GetPurificationCenterWorldPosition()
    {
      if (_hasLastPurificationAnchor && _reopenWaveReleaseUntil > 0f)
      {
        return _lastPurificationAnchorWorldPosition;
      }

      return GetCrisisArrayCenterWorldPosition();
    }

    private Vector3 GetCrisisArrayCenterWorldPosition()
    {
      if (_camera == null)
      {
        return Vector3.zero;
      }

      return _camera.ViewportToWorldPoint(
        new Vector3(CrisisArrayCenterViewport.x, CrisisArrayCenterViewport.y, _blockDepthFromCamera));
    }

    private void ScheduleNextCrisis()
    {
      _nextCrisisAt = Time.time + Random.Range(_minCrisisIntervalSeconds, _maxCrisisIntervalSeconds);
    }

    private void ScheduleNextSpawn(float delaySeconds)
    {
      _nextSpawnAt = Time.time + Mathf.Max(0.01f, delaySeconds);
    }

    private void UpdateObservationMetrics()
    {
      ResizeBackgroundVisual();
      UpdateQuietFieldBackgroundVisual();
      if (_purificationWaveRoot != null && _purificationWaveRoot.activeSelf)
      {
        UpdatePurificationWaveVisual();
      }

      var isRestState = _calibrationActive ||
                        IsTutorialInputSuspended ||
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
      RefreshTutorialReadiness();
      _gameplayState = GameplayState.SessionReport;
      _blinkQueued = false;
      _hoveredBlock = null;
      _lastHoveredBlock = null;
      PublishTargetLockChangedIfNeeded();
      ResetPushAwayInputState();
      AdvanceProtocolDayAfterSession();
      ClearModuleCards();
      _moduleUpgradeView?.Hide();
      SetGameplayPresentationForUpgrade(false);
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

    private int SpawnOrbitBlock()
    {
      return SpawnOrbitBlock(null, null, null);
    }

    private int SpawnOrbitBlock(float? forcedPhase, float? forcedAngularSpeed, float? forcedSize)
    {
      var blockObject = new GameObject($"Edge Orbit Block {_spawnSerial + 1}");
      var renderer = blockObject.AddComponent<SpriteRenderer>();
      renderer.sprite = _dataSeedSprite;
      renderer.color = OrbitColor;
      renderer.sortingOrder = 20;
      var glowRenderer = CreateTargetGlow(blockObject, KeepBlinkingTheme.WithAlpha(OrbitColor, 0.12f), 19);

      var size = forcedSize ?? Random.Range(_blockWorldSizeRange.x, _blockWorldSizeRange.y);
      var baseScale = new Vector3(size, size, 1f);
      blockObject.transform.localScale = baseScale;
      blockObject.transform.rotation = Quaternion.identity;

      var phase = forcedPhase ?? Random.Range(0f, Mathf.PI * 2f);
      var angularSpeed = forcedAngularSpeed ??
                         Random.Range(_orbitAngularSpeedRange.x, _orbitAngularSpeedRange.y) *
                         (Random.value < 0.5f ? -1f : 1f);

      var block = new OrbitBlock(
        blockObject,
        renderer,
        glowRenderer,
        phase,
        angularSpeed,
        _spawnSerial,
        Time.time,
        baseScale);
      block.SoftFocusLaneOffset = ((_spawnSerial % 4) - 1.5f) * _softFocusLaneSpacingNormalized;
      block.SoftFocusProgressSegments = CreateSoftFocusProgressRing(blockObject);

      if (_deepRecoveryNextLockPrimed && !_tutorialMode)
      {
        _deepRecoveryNextLockPrimed = false;
        _deepRecoveryTargetId = block.Serial;
        block.StartsHalfLocked = true;
      }

      blockObject.transform.position = _tutorialMode
        ? EvaluateOrbitWorldPosition(block.Phase)
        : EvaluateSoftFocusPathWorldPosition(block.Phase, block.SoftFocusLaneOffset);
      _blocks.Add(block);
      _spawnSerial++;
      return block.Serial;
    }

    private int SpawnCrisisBlocks(int count)
    {
      for (var i = 0; i < count; i++)
      {
        SpawnCrisisBlock();
      }

      return count;
    }

    private int SpawnCrisisBlock()
    {
      var blockObject = new GameObject($"Crisis Inward Block {_spawnSerial + 1}");
      var renderer = blockObject.AddComponent<SpriteRenderer>();
      renderer.sprite = _dataSeedSprite;
      renderer.color = CrisisColor;
      renderer.sortingOrder = 25;
      var glowRenderer = CreateTargetGlow(blockObject, KeepBlinkingTheme.WithAlpha(CrisisColor, 0.1f), 24);

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
        glowRenderer,
        Random.Range(0f, Mathf.PI * 2f),
        0f,
        _spawnSerial,
        Time.time,
        baseScale)
      {
        State = BlockState.Crisis,
        CrisisMoveSpeed = Random.Range(_crisisMoveSpeedRange.x, _crisisMoveSpeedRange.y),
        CrisisWaveId = _activeCrisisWaveId,
      };

      _blocks.Add(block);
      _spawnSerial++;
      return block.Serial;
    }

    private SpriteRenderer CreateTargetGlow(GameObject parent, Color color, int sortingOrder)
    {
      var glowObject = new GameObject("Seed Soft Glow");
      glowObject.transform.SetParent(parent.transform, false);
      glowObject.transform.localPosition = new Vector3(0f, 0f, 0.02f);
      glowObject.transform.localScale = new Vector3(1.55f, 1.55f, 1f);
      var glowRenderer = glowObject.AddComponent<SpriteRenderer>();
      glowRenderer.sprite = _circleSprite;
      glowRenderer.color = color;
      glowRenderer.sortingOrder = sortingOrder;
      return glowRenderer;
    }

    private SpriteRenderer[] CreateSoftFocusProgressRing(GameObject parent)
    {
      const int segmentCount = 16;
      var segments = new SpriteRenderer[segmentCount];
      for (var index = 0; index < segmentCount; index++)
      {
        var angle = index * Mathf.PI * 2f / segmentCount;
        var segmentObject = new GameObject($"Soft Focus Progress {index + 1}");
        segmentObject.transform.SetParent(parent.transform, false);
        segmentObject.transform.localPosition = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), -0.03f) * 0.72f;
        segmentObject.transform.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg + 90f);
        segmentObject.transform.localScale = new Vector3(0.22f, 0.045f, 1f);
        var renderer = segmentObject.AddComponent<SpriteRenderer>();
        renderer.sprite = _squareSprite;
        renderer.color = KeepBlinkingTheme.WithAlpha(ConvertedColor, 0f);
        renderer.sortingOrder = 23;
        segments[index] = renderer;
      }
      return segments;
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
      UpdateConvertedBlocks();
      UpdateCollectingBlocks();
    }

    private void UpdateSampleCollection()
    {
      if (!CanUpdateDistanceState(_distanceTracker.HasValidSample))
      {
        ResetPushAwayInputState();
        return;
      }

      if (!_pushAwayTriggerPending)
      {
        return;
      }

      _pushAwayTriggerPending = false;
      if (!HasCollectableSamples())
      {
        return;
      }

      InvokeSignalSafely(PushAwayTriggered, nameof(PushAwayTriggered));
      _softFocusHiddenByPushAway = true;
      PreparePushAwayModuleSamples();
      HandleSuccessfulPushAwayModules();
      StartCollectingConvertedSamples();
      _pushAwayReady = false;
    }

    private void ResetPushAwayInputState()
    {
      _pushAwayTriggerPending = false;
      _pushAwayReady = false;
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

    private int StartCollectingConvertedSamples()
    {
      var startedCollectingCount = 0;
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
        if (block.GlowRenderer != null)
        {
          block.GlowRenderer.sortingOrder = 69;
        }
        startedCollectingCount++;
      }

      if (startedCollectingCount > 0)
      {
        _distanceSwitchCount++;
        InvokeSignalSafely(ConvertedCollectionStarted, startedCollectingCount, nameof(ConvertedCollectionStarted));
      }

      return startedCollectingCount;
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
        if (block.GlowRenderer != null)
        {
          block.GlowRenderer.color = Color.Lerp(
            block.GlowRenderer.color,
            KeepBlinkingTheme.WithAlpha(ProgressFillColor, 0.16f),
            Time.deltaTime * _sampleCollectSpeed);
        }

        if (Vector2.Distance(block.Transform.position, target) <= _sampleCollectDistance)
        {
          CollectSampleBlock(block);
        }
      }
    }

    private void CollectSampleBlock(OrbitBlock block)
    {
      var targetId = block.Serial;
      var crisisWaveId = block.CrisisWaveId;
      var bossRoundId = block.BossRoundId;
      block.State = BlockState.FadingOut;
      block.IsHovered = false;

      if (block.GameObject != null)
      {
        Destroy(block.GameObject);
      }

      _collectedSampleCount++;
      _totalSamplesCollected++;
      _sampleProgress = Mathf.Clamp01(_collectedSampleCount / (float)GetCurrentUpgradeSampleRequirement());
      UpdateProgressBarVisual();
      InvokeSignalSafely(ExperienceReachedBar, targetId, nameof(ExperienceReachedBar));
      InvokeSignalSafely(ExperienceProgressChanged, new ExperienceProgressSignal(
        _collectedSampleCount,
        GetCurrentUpgradeSampleRequirement(),
        _sampleProgress), nameof(ExperienceProgressChanged));
      TrackCrisisExperienceCollected(crisisWaveId);
      TrackBossExperienceCollected(bossRoundId);

      if (_flashXpDiscountOnNextSample)
      {
        _flashXpDiscountOnNextSample = false;
        ActivateModuleEffect(FirstLevelModuleId.XpDiscount);
      }

      if (_sampleProgress >= 1f)
      {
        BeginModuleUpgrade();
      }
    }

    private void TrackCrisisExperienceCollected(int crisisWaveId)
    {
      if (crisisWaveId <= 0)
      {
        return;
      }

      _collectedCrisisExperienceByWave.TryGetValue(crisisWaveId, out var collectedCount);
      collectedCount++;
      _collectedCrisisExperienceByWave[crisisWaveId] = collectedCount;

      if (HasPendingCrisisExperienceForWave(crisisWaveId))
      {
        return;
      }

      _collectedCrisisExperienceByWave.Remove(crisisWaveId);
      InvokeSignalSafely(
        CrisisExperienceCollectionCompleted,
        collectedCount,
        nameof(CrisisExperienceCollectionCompleted));
    }

    private bool HasPendingCrisisExperienceForWave(int crisisWaveId)
    {
      for (var i = 0; i < _blocks.Count; i++)
      {
        var block = _blocks[i];
        if (block == null || block.CrisisWaveId != crisisWaveId)
        {
          continue;
        }

        if (block.State == BlockState.Crisis ||
            block.State == BlockState.Converted ||
            block.State == BlockState.Collecting)
        {
          return true;
        }
      }

      return false;
    }

    private void TrackBossExperienceCollected(int bossRoundId)
    {
      if (bossRoundId <= 0)
      {
        return;
      }

      _collectedBossExperienceByRound.TryGetValue(bossRoundId, out var collectedCount);
      collectedCount++;
      _collectedBossExperienceByRound[bossRoundId] = collectedCount;
      if (HasPendingBossExperienceForRound(bossRoundId))
      {
        return;
      }

      _collectedBossExperienceByRound.Remove(bossRoundId);
      InvokeSignalSafely(
        BossExperienceCollectionCompleted,
        bossRoundId,
        collectedCount,
        nameof(BossExperienceCollectionCompleted));
    }

    private bool HasPendingBossExperienceForRound(int bossRoundId)
    {
      return GetPendingBossExperienceSampleCount(bossRoundId) > 0;
    }

    private void HandleSuccessfulRestModules()
    {
      HandleSuccessfulRestModules(_activeCrisisWaveId, 0, GetCrisisArrayCenterWorldPosition());
    }

    private float HandleSuccessfulRestModules(int crisisWaveId, int bossRoundId, Vector3 sampleAnchor)
    {
      var spawnPauseSeconds = 0f;
      var pauseModule = FirstLevelModuleId.None;
      if (_installedModules.Contains(FirstLevelModuleId.QuietField))
      {
        spawnPauseSeconds = 4f;
        pauseModule = FirstLevelModuleId.QuietField;
        _quietFieldVisualUntil = Mathf.Max(_quietFieldVisualUntil, Time.time + spawnPauseSeconds);
        _softFocusField?.GrantQuietField(spawnPauseSeconds);
      }
      else if (_installedModules.Contains(FirstLevelModuleId.QuietWake))
      {
        spawnPauseSeconds = 2f;
        pauseModule = FirstLevelModuleId.QuietWake;
      }

      if (spawnPauseSeconds > 0f)
      {
        _normalSpawnPausedUntil = Mathf.Max(_normalSpawnPausedUntil, Time.time + spawnPauseSeconds);
        ActivateModuleEffect(pauseModule);
      }

      if (_installedModules.Contains(FirstLevelModuleId.CoreEcho))
      {
        InvokeSignalSafely(FutureBossCoreDamageRequested, 1, nameof(FutureBossCoreDamageRequested));
        ActivateModuleEffect(FirstLevelModuleId.CoreEcho);
      }

      if (_installedModules.Contains(FirstLevelModuleId.DeepRecovery))
      {
        PrimeDeepRecoveryTarget();
      }

      if (_installedModules.Contains(FirstLevelModuleId.WakeEcho))
      {
        _wakeEchoRangePrimed = true;
        _softFocusField?.GrantQuietField(4f);
      }

      if (_installedModules.Contains(FirstLevelModuleId.RestCache))
      {
        SpawnConvertedModuleSample(
          sampleAnchor,
          new Color32(0x91, 0xB8, 0xD0, 0xFF),
          crisisWaveId,
          false,
          bossRoundId);
        ActivateModuleEffect(FirstLevelModuleId.RestCache);
      }

      if (_installedModules.Contains(FirstLevelModuleId.FullLoop) &&
          _fullLoopStage == FullLoopStage.WaitingForRest)
      {
        _fullLoopStage = FullLoopStage.WaitingForPushAway;
      }

      return spawnPauseSeconds;
    }

    private void PreparePushAwayModuleSamples()
    {
      var anchor = GetConvertedSampleAnchorWorldPosition();
      var crisisWaveId = GetConvertedCrisisWaveId();
      var bossRoundId = GetConvertedBossRoundId();
      if (_installedModules.Contains(FirstLevelModuleId.BonusSample))
      {
        SpawnConvertedModuleSample(anchor, KeepBlinkingTheme.AccentWarm, crisisWaveId, false, bossRoundId);
        ActivateModuleEffect(FirstLevelModuleId.BonusSample);
      }

      if (_loopBonusPendingSamples > 0)
      {
        var pendingCount = _loopBonusPendingSamples;
        _loopBonusPendingSamples = 0;
        for (var i = 0; i < pendingCount; i++)
        {
          var angle = i * Mathf.PI * 2f / Mathf.Max(1, pendingCount);
          var offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * 0.32f;
          SpawnConvertedModuleSample(anchor + offset, KeepBlinkingTheme.AccentWarm, crisisWaveId, false, bossRoundId);
        }
        ActivateModuleEffect(FirstLevelModuleId.LoopBonus);
      }
    }

    private void PrimeDeepRecoveryTarget()
    {
      _deepRecoveryTargetId = NoTargetId;
      for (var i = 0; i < _blocks.Count; i++)
      {
        var block = _blocks[i];
        if (block.State != BlockState.Orbiting)
        {
          continue;
        }

        block.StartsHalfLocked = true;
        _deepRecoveryTargetId = block.Serial;
        _deepRecoveryNextLockPrimed = false;
        return;
      }

      _deepRecoveryNextLockPrimed = true;
    }

    private void HandleSuccessfulPushAwayModules()
    {
      if (!_installedModules.Contains(FirstLevelModuleId.FullLoop) ||
          _fullLoopStage != FullLoopStage.WaitingForPushAway)
      {
        return;
      }

      _fullLoopStage = FullLoopStage.WaitingForBlink;
      SpawnConvertedModuleSample(
        GetProgressBarFillWorldPosition() + Vector3.up * 1.25f,
        KeepBlinkingTheme.AccentWarm,
        GetConvertedCrisisWaveId(),
        true,
        GetConvertedBossRoundId());
      ActivateModuleEffect(FirstLevelModuleId.FullLoop);
    }

    private OrbitBlock SpawnConvertedModuleSample(
      Vector3 worldPosition,
      Color color,
      int crisisWaveId,
      bool isGold,
      int bossRoundId = 0)
    {
      var sampleObject = new GameObject(isGold
        ? $"Gold Loop Sample {_spawnSerial + 1}"
        : $"Module Experience Sample {_spawnSerial + 1}");
      sampleObject.transform.SetParent(transform, false);
      var renderer = sampleObject.AddComponent<SpriteRenderer>();
      renderer.sprite = _dataSeedSprite;
      renderer.color = color;
      renderer.sortingOrder = 14;
      var glowRenderer = CreateTargetGlow(sampleObject, KeepBlinkingTheme.WithAlpha(color, isGold ? 0.2f : 0.13f), 13);
      var size = isGold ? 0.7f : 0.58f;
      var baseScale = new Vector3(size, size, 1f);
      sampleObject.transform.position = worldPosition;
      sampleObject.transform.localScale = baseScale * _harvestScaleRatio;

      var sample = new OrbitBlock(
        sampleObject,
        renderer,
        glowRenderer,
        0f,
        0f,
        _spawnSerial,
        Time.time,
        baseScale)
      {
        State = BlockState.Converted,
        CrisisWaveId = crisisWaveId,
        ConvertedAt = Time.time - _harvestSeconds,
        IsModuleSample = true,
        ModuleSampleColor = color,
        BossRoundId = bossRoundId,
      };

      _blocks.Add(sample);
      _spawnSerial++;
      return sample;
    }

    private Vector3 GetConvertedSampleAnchorWorldPosition()
    {
      var sum = Vector3.zero;
      var count = 0;
      for (var i = 0; i < _blocks.Count; i++)
      {
        if (_blocks[i].State == BlockState.Converted)
        {
          sum += _blocks[i].Transform.position;
          count++;
        }
      }

      return count > 0 ? sum / count : GetCrisisArrayCenterWorldPosition();
    }

    private int GetConvertedCrisisWaveId()
    {
      for (var i = 0; i < _blocks.Count; i++)
      {
        if (_blocks[i].State == BlockState.Converted && _blocks[i].CrisisWaveId > 0)
        {
          return _blocks[i].CrisisWaveId;
        }
      }

      return 0;
    }

    private int GetConvertedBossRoundId()
    {
      for (var i = 0; i < _blocks.Count; i++)
      {
        if (_blocks[i].State == BlockState.Converted && _blocks[i].BossRoundId > 0)
        {
          return _blocks[i].BossRoundId;
        }
      }

      return 0;
    }

    private int GetBaseUpgradeSampleRequirement()
    {
      return Mathf.Max(_minimumUpgradeSampleRequirement, _samplesNeededForUpgrade);
    }

    private int GetCurrentUpgradeSampleRequirement()
    {
      return _currentUpgradeSampleRequirement > 0
        ? Mathf.Max(_minimumUpgradeSampleRequirement, _currentUpgradeSampleRequirement)
        : GetBaseUpgradeSampleRequirement();
    }

    private int CountInstalledModulesInCategory(FirstLevelModuleCategory category)
    {
      var count = 0;
      for (var i = 0; i < _installedModuleOrder.Count; i++)
      {
        if (FirstLevelUpgradeCatalog.Get(_installedModuleOrder[i]).Category == category)
        {
          count++;
        }
      }

      return Mathf.Max(1, count);
    }

    private void ActivateModuleEffect(FirstLevelModuleId moduleId)
    {
      if (moduleId == FirstLevelModuleId.None || !_installedModules.Contains(moduleId))
      {
        return;
      }

      _moduleFlashUntil[moduleId] = Time.unscaledTime + 0.78f;
      _moduleUpgradeView?.FlashModule(moduleId);
      InvokeSignalSafely(FirstLevelModuleEffectActivated, moduleId, nameof(FirstLevelModuleEffectActivated));
      if (Time.unscaledTime - _lastModuleFeedbackAt >= 0.12f)
      {
        _lastModuleFeedbackAt = Time.unscaledTime;
        PlayFeedbackClip(_moduleActivatedClip);
      }
    }

    private void UpdateQuietFieldBackgroundVisual()
    {
      if (_backgroundRenderer == null)
      {
        return;
      }

      var dimmed = Time.time < _quietFieldVisualUntil;
      var targetColor = dimmed
        ? new Color(0.76f, 0.82f, 0.79f, 1f)
        : Color.white;
      _backgroundRenderer.color = Color.Lerp(
        _backgroundRenderer.color,
        targetColor,
        1f - Mathf.Exp(-2.4f * Time.deltaTime));
    }

    private void BeginModuleUpgrade()
    {
      if (_gameplayState == GameplayState.ModuleUpgrade ||
          _firstLevelUpgradeSequenceCompleted ||
          _moduleChoiceCount >= UpgradesRequiredBeforeBoss)
      {
        return;
      }

      _resumeStateAfterUpgrade = _gameplayState == GameplayState.EyesClosedFreeze ? GameplayState.Crisis : _gameplayState;
      _gameplayState = GameplayState.ModuleUpgrade;
      _blinkQueued = false;
      _hoveredBlock = null;
      _lastHoveredBlock = null;
      PublishTargetLockChangedIfNeeded();
      SetBlackoutVisible(false);
      _reopenWaveReleaseUntil = -1f;
      SetPurificationWaveVisible(false);
      SetProgressBarVisible(false);
      _currentModuleOffer.Clear();
      _currentModuleOffer.AddRange(FirstLevelUpgradeCatalog.BuildOffer(_moduleChoiceCount + 1, _installedModules));
      if (_currentModuleOffer.Count == 0)
      {
        Debug.LogError("KeepBlinking upgrade pool returned no legal cards. Gameplay will resume without consuming the upgrade.", this);
        _gameplayState = ResolveGameplayStateAfterUpgrade();
        SetProgressBarVisible(true);
        SetGameplayPresentationForUpgrade(false);
        if (_gameplayState == GameplayState.Orbiting)
        {
          ScheduleNextSpawn(0.45f);
        }
        return;
      }

      _moduleHoveredCardIndex = -1;
      _selectedModuleCardIndex = -1;
      _moduleChoicePending = false;
      _moduleInstallStartedAt = -1f;
      SetGameplayPresentationForUpgrade(true);
      EnsureModuleUpgradeView();
      _moduleUpgradeView.Show(_currentModuleOffer);
      TraceUpgradeFlow("UpgradeOpened", $"offer={_currentModuleOffer.Count}, choice={_moduleChoiceCount + 1}/{UpgradesRequiredBeforeBoss}");
      InvokeSignalSafely(UpgradeOpened, nameof(UpgradeOpened));
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

      for (var i = 0; i < _currentModuleOffer.Count; i++)
      {
        var definition = GetModuleDefinitionForCard(i);
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

        var glow = CreateModuleCardPiece(root.transform, "Glow", Vector3.zero, new Vector3(1.08f, 1.08f, 1f), _roundedFillSprite, KeepBlinkingTheme.WithAlpha(definition.AccentColor, 0.08f), 979);
        var border = CreateModuleCardPiece(root.transform, "Border", Vector3.zero, Vector3.one, _roundedBorderSprite, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.BorderReadable, 0.96f), 981);
        var accentPosition = isVertical ? new Vector3(-0.47f, 0f, 0f) : new Vector3(0f, 0.33f, 0f);
        var accentScale = isVertical ? new Vector3(0.035f, 0.72f, 1f) : new Vector3(0.58f, 0.12f, 1f);
        var accent = CreateModuleCardPiece(root.transform, "Accent", accentPosition, accentScale, _roundedFillSprite, KeepBlinkingTheme.WithAlpha(definition.AccentColor, 0.22f), 982);
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
      if (_moduleChoicePending)
      {
        var installProgress = Mathf.Clamp01(
          (Time.unscaledTime - _moduleInstallStartedAt) /
          Mathf.Max(0.05f, _moduleInstallPresentationSeconds));
        _moduleUpgradeView?.SetInteractionState(
          _moduleHoveredCardIndex,
          _selectedModuleCardIndex,
          1f,
          true,
          installProgress);
        _blinkQueued = false;
        if (Time.unscaledTime - _moduleInstallStartedAt >= _moduleInstallPresentationSeconds)
        {
          FinalizeModuleInstallation();
        }
        return;
      }

      var hoveredCardIndex = -1;
      if (_moduleUpgradeView != null)
      {
        _moduleUpgradeView.TryGetCardAtScreenPosition(UnityEngine.Input.mousePosition, out hoveredCardIndex);
      }

      var previousHoveredCardIndex = _moduleHoveredCardIndex;
      _moduleHoveredCardIndex = hoveredCardIndex;
      if (hoveredCardIndex >= 0 && hoveredCardIndex != previousHoveredCardIndex)
      {
        TraceUpgradeFlow("CardPreviewStarted", $"card={hoveredCardIndex}");
      }
      _selectedModuleCardIndex = -1;
      _blinkQueued = false;
      _moduleUpgradeView?.SetInteractionState(
        hoveredCardIndex,
        -1,
        0f,
        false,
        0f);

      if (hoveredCardIndex < 0 || !UnityEngine.Input.GetMouseButtonDown(0))
      {
        return;
      }

      _selectedModuleCardIndex = hoveredCardIndex;
      BeginModuleInstallation();
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

    private void BeginModuleInstallation()
    {
      if (_moduleChoicePending ||
          _selectedModuleCardIndex < 0 ||
          _selectedModuleCardIndex >= _currentModuleOffer.Count)
      {
        return;
      }

      var moduleId = _currentModuleOffer[_selectedModuleCardIndex];
      if (_installedModules.Contains(moduleId))
      {
        Debug.LogError($"KeepBlinking rejected duplicate module request: {moduleId}.", this);
        _selectedModuleCardIndex = -1;
        return;
      }

      _moduleChoicePending = true;
      _moduleInstallStartedAt = Time.unscaledTime;
      TraceUpgradeFlow("ModuleChoiceRequested", $"card={_selectedModuleCardIndex}, module={moduleId}");
      _moduleUpgradeView?.BeginInstallAnimation(
        _selectedModuleCardIndex,
        moduleId,
        _installedModuleOrder.Count);
      _moduleUpgradeView?.SetInteractionState(
        _moduleHoveredCardIndex,
        _selectedModuleCardIndex,
        1f,
        true,
        0f);
      PlayFeedbackClip(_moduleInstalledClip);
    }

    private void FinalizeModuleInstallation()
    {
      if (!_moduleChoicePending)
      {
        return;
      }

      if (_selectedModuleCardIndex < 0 || _selectedModuleCardIndex >= _currentModuleOffer.Count)
      {
        Debug.LogError("KeepBlinking module installation lost its selected card. The upgrade UI is being safely released.", this);
        RecoverFromInvalidModuleInstallation();
        return;
      }

      var selectedCardIndex = _selectedModuleCardIndex;
      var moduleId = _currentModuleOffer[selectedCardIndex];
      Debug.Log($"KeepBlinking first-level module installed: {moduleId}");
      _moduleChoicePending = false;
      _moduleInstallStartedAt = -1f;
      _moduleChoiceCount++;
      if (_installedModules.Add(moduleId))
      {
        _installedModuleOrder.Add(moduleId);
      }
      TraceUpgradeFlow("ModuleApplied", $"module={moduleId}, installed={_moduleChoiceCount}/{UpgradesRequiredBeforeBoss}");

      _moduleFlashUntil[moduleId] = Time.unscaledTime + 0.9f;
      _currentUpgradeSampleRequirement = moduleId == FirstLevelModuleId.XpDiscount
        ? Mathf.Max(_minimumUpgradeSampleRequirement, GetBaseUpgradeSampleRequirement() - 1)
        : GetBaseUpgradeSampleRequirement();
      _flashXpDiscountOnNextSample = moduleId == FirstLevelModuleId.XpDiscount;

      if (_installedModules.Contains(FirstLevelModuleId.LoopBonus))
      {
        _loopBonusPendingSamples += 2;
      }

      var reserveCount = moduleId == FirstLevelModuleId.XpReserve
        ? Mathf.FloorToInt(GetCurrentUpgradeSampleRequirement() * 0.3f)
        : 0;
      ClearModuleCards();
      _moduleUpgradeView?.SetInstalledModules(_installedModuleOrder);
      _moduleUpgradeView?.Hide();
      TraceUpgradeFlow("UpgradeClosed", $"module={moduleId}");
      _currentModuleOffer.Clear();
      _moduleHoveredCardIndex = -1;
      _selectedModuleCardIndex = -1;
      _collectedSampleCount = reserveCount;
      _sampleProgress = Mathf.Clamp01(_collectedSampleCount / (float)GetCurrentUpgradeSampleRequirement());
      ResetPushAwayInputState();
      UpdateProgressBarVisual();
      InvokeSignalSafely(ExperienceProgressChanged, new ExperienceProgressSignal(
        _collectedSampleCount,
        GetCurrentUpgradeSampleRequirement(),
        _sampleProgress), nameof(ExperienceProgressChanged));
      SetProgressBarVisible(true);
      SetGameplayPresentationForUpgrade(false);
      _gameplayState = ResolveGameplayStateAfterUpgrade();

      var upgradeSequenceJustCompleted = _moduleChoiceCount >= UpgradesRequiredBeforeBoss &&
                                         !_firstLevelUpgradeSequenceCompleted;
      var buildJustCompleted = _moduleChoiceCount >= UpgradesRequiredBeforeBoss &&
                               !_firstLevelBuildCompleted;
      if (upgradeSequenceJustCompleted)
      {
        _firstLevelUpgradeSequenceCompleted = true;
      }
      if (buildJustCompleted)
      {
        _firstLevelBuildCompleted = true;
      }

      if (!buildJustCompleted && _gameplayState == GameplayState.Orbiting)
      {
        ScheduleNextSpawn(Mathf.Max(0.45f, _normalSpawnPausedUntil - Time.time));
      }
      TraceUpgradeFlow("NextGameplayState", $"state={_gameplayState}, buildComplete={_firstLevelBuildCompleted}");

      InvokeSignalSafely(FirstLevelModuleInstalled, moduleId, nameof(FirstLevelModuleInstalled));
      if (moduleId == FirstLevelModuleId.XpReserve)
      {
        ActivateModuleEffect(moduleId);
      }
      InvokeSignalSafely(ModuleChoiceCompleted, selectedCardIndex, nameof(ModuleChoiceCompleted));
      TraceUpgradeFlow("ModuleChoiceCompleted", $"card={selectedCardIndex}, module={moduleId}");

      if (upgradeSequenceJustCompleted)
      {
        InvokeSignalSafely(FirstLevelUpgradeSequenceCompleted, nameof(FirstLevelUpgradeSequenceCompleted));
      }

      if (buildJustCompleted)
      {
        InvokeSignalSafely(FirstLevelBuildCompleted, nameof(FirstLevelBuildCompleted));
      }
    }

    private GameplayState ResolveGameplayStateAfterUpgrade()
    {
      return _resumeStateAfterUpgrade == GameplayState.Crisis && CountState(BlockState.Crisis) > 0
        ? GameplayState.Crisis
        : GameplayState.Orbiting;
    }

    private void RecoverFromInvalidModuleInstallation()
    {
      _moduleChoicePending = false;
      _moduleInstallStartedAt = -1f;
      _moduleHoveredCardIndex = -1;
      _selectedModuleCardIndex = -1;
      ClearModuleCards();
      _moduleUpgradeView?.Hide();
      _currentModuleOffer.Clear();
      SetProgressBarVisible(true);
      SetGameplayPresentationForUpgrade(false);
      _gameplayState = ResolveGameplayStateAfterUpgrade();
      if (_gameplayState == GameplayState.Orbiting && !_firstLevelBuildCompleted)
      {
        ScheduleNextSpawn(0.45f);
      }
      TraceUpgradeFlow("NextGameplayState", $"state={_gameplayState}, recoveredInvalidSelection=true");
    }

    private void TraceUpgradeFlow(string marker, string details = null)
    {
      if (!_logUpgradeFlowTransitions)
      {
        return;
      }

      Debug.Log(string.IsNullOrEmpty(details)
        ? $"[UpgradeFlow] {marker}"
        : $"[UpgradeFlow] {marker} | {details}", this);
    }

    private void InvokeSignalSafely(Action signal, string signalName)
    {
      if (signal == null)
      {
        return;
      }

      var handlers = signal.GetInvocationList();
      for (var i = 0; i < handlers.Length; i++)
      {
        try
        {
          ((Action)handlers[i]).Invoke();
        }
        catch (Exception exception)
        {
          Debug.LogError($"KeepBlinking gameplay signal subscriber failed: {signalName}.", this);
          Debug.LogException(exception, this);
        }
      }
    }

    private void InvokeSignalSafely<T>(Action<T> signal, T value, string signalName)
    {
      if (signal == null)
      {
        return;
      }

      var handlers = signal.GetInvocationList();
      for (var i = 0; i < handlers.Length; i++)
      {
        try
        {
          ((Action<T>)handlers[i]).Invoke(value);
        }
        catch (Exception exception)
        {
          Debug.LogError($"KeepBlinking gameplay signal subscriber failed: {signalName}.", this);
          Debug.LogException(exception, this);
        }
      }
    }

    private void InvokeSignalSafely<TFirst, TSecond>(
      Action<TFirst, TSecond> signal,
      TFirst first,
      TSecond second,
      string signalName)
    {
      if (signal == null)
      {
        return;
      }

      var handlers = signal.GetInvocationList();
      for (var i = 0; i < handlers.Length; i++)
      {
        try
        {
          ((Action<TFirst, TSecond>)handlers[i]).Invoke(first, second);
        }
        catch (Exception exception)
        {
          Debug.LogError($"KeepBlinking gameplay signal subscriber failed: {signalName}.", this);
          Debug.LogException(exception, this);
        }
      }
    }

    private void UpdateModuleCardVisuals()
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

        var definition = GetModuleDefinitionForCard(card.Index);
        var pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 1.18f + card.Index * 0.9f);
        var isFocused = card.Index == _moduleHoveredCardIndex;
        var isSelected = card.Index == _selectedModuleCardIndex;
        var isFading = _moduleChoicePending && !isSelected;
        var targetAlpha = isFading ? 0.08f : 0.98f;
        var glowAlpha = isSelected ? 0.22f : isFocused ? 0.14f : 0.06f + pulse * 0.03f;
        var borderAlpha = isFading ? 0.08f : isSelected ? 1f : 0.92f;
        var targetScale = isSelected ? (_moduleChoicePending ? 1.08f : 1.045f) : isFading ? 0.94f : 1f + pulse * 0.008f;

        card.GameObject.transform.localScale = Vector3.Lerp(
          card.GameObject.transform.localScale,
          new Vector3(GetCurrentModuleCardWorldSize().x, GetCurrentModuleCardWorldSize().y, 1f) * targetScale,
          Time.deltaTime * 4f);
        if (card.Renderer != null)
        {
          card.Renderer.color = Color.Lerp(card.Renderer.color, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceElevated, targetAlpha), Time.deltaTime * 7f);
        }

        if (card.BorderRenderer != null)
        {
          card.BorderRenderer.color = Color.Lerp(card.BorderRenderer.color, KeepBlinkingTheme.WithAlpha(isSelected || isFocused ? definition.AccentColor : KeepBlinkingTheme.BorderReadable, borderAlpha), Time.deltaTime * 7f);
        }

        if (card.GlowRenderer != null)
        {
          card.GlowRenderer.color = Color.Lerp(card.GlowRenderer.color, KeepBlinkingTheme.WithAlpha(definition.AccentColor, isFading ? 0f : glowAlpha), Time.deltaTime * 7f);
        }

        if (card.AccentRenderer != null)
        {
          card.AccentRenderer.color = Color.Lerp(card.AccentRenderer.color, KeepBlinkingTheme.WithAlpha(definition.AccentColor, isFading ? 0.02f : 0.16f + pulse * 0.08f), Time.deltaTime * 7f);
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

        var movementMultiplier = !_tutorialMode && block.IsInsideSoftFocusField
          ? _softFocusInsideSpeedMultiplier
          : 1f;
        block.Phase += block.AngularSpeed * movementMultiplier * Time.deltaTime;
        var targetPosition = _tutorialMode
          ? EvaluateOrbitWorldPosition(block.Phase)
          : EvaluateSoftFocusPathWorldPosition(block.Phase, block.SoftFocusLaneOffset);
        block.Transform.position = Vector3.Lerp(block.Transform.position, targetPosition, Time.deltaTime * 2.6f);

        var isTutorialTarget = _tutorialMode && block.Serial == _tutorialOrbitTargetId;
        var tutorialHighlight = 0.5f + Mathf.Sin(Time.time * 2.1f) * 0.12f;
        var targetColor = block.IsSoftFocused
          ? Color.Lerp(OrbitColor, ConvertedColor, 0.72f)
          : block.IsHovered
          ? HoverColor
          : isTutorialTarget
            ? Color.Lerp(OrbitColor, HoverColor, tutorialHighlight)
            : block.StartsHalfLocked
              ? Color.Lerp(OrbitColor, HoverColor, 0.5f)
              : OrbitColor;
        block.Renderer.color = Color.Lerp(block.Renderer.color, targetColor, Time.deltaTime * _colorLerpSpeed);

        var driftPulse = 1f + Mathf.Sin(Time.time * 1.45f + block.Serial * 0.71f) * 0.05f;
        var tutorialPulse = 1.08f + Mathf.Sin(Time.time * 2.1f) * 0.045f;
        var targetScale = block.IsSoftFocused
          ? block.BaseScale * 1.10f
          : block.IsHovered
          ? block.BaseScale * 1.18f
          : block.BaseScale * (isTutorialTarget ? tutorialPulse : block.StartsHalfLocked ? 1.09f : driftPulse);
        block.Transform.localScale = Vector3.Lerp(block.Transform.localScale, targetScale, Time.deltaTime * _scaleLerpSpeed);

        if (block.GlowRenderer != null)
        {
          var glowAlpha = block.IsSoftFocused ? 0.22f : block.IsHovered ? 0.2f : isTutorialTarget ? 0.14f : block.StartsHalfLocked ? 0.13f : 0.075f;
          var glowColor = block.IsSoftFocused ? ConvertedColor : block.IsHovered || block.StartsHalfLocked ? HoverColor : OrbitColor;
          block.GlowRenderer.color = Color.Lerp(
            block.GlowRenderer.color,
            KeepBlinkingTheme.WithAlpha(glowColor, glowAlpha),
            Time.deltaTime * 5f);
          var glowScale = block.IsSoftFocused ? 1.72f : isTutorialTarget || block.IsHovered ? 1.64f : 1.48f;
          block.GlowRenderer.transform.localScale = Vector3.Lerp(
            block.GlowRenderer.transform.localScale,
            new Vector3(glowScale, glowScale, 1f),
            Time.deltaTime * 4f);
        }

        block.Transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Sin(block.Phase * 0.85f) * 2f);
        UpdateSoftFocusProgressVisual(block);
      }
    }

    private void UpdateConvertedBlocks()
    {
      for (var i = 0; i < _blocks.Count; i++)
      {
        var block = _blocks[i];
        var usesConvertedBreathing =
          (_tutorialMode && block.Serial == _tutorialOrbitTargetId) || block.CrisisWaveId > 0 || block.IsModuleSample;
        if (block.State != BlockState.Converted ||
            !usesConvertedBreathing ||
            Time.time - block.ConvertedAt < _harvestSeconds)
        {
          continue;
        }

        var pulse01 = 0.5f + Mathf.Sin(Time.time * 2.35f) * 0.5f;
        var targetScale = block.BaseScale * _harvestScaleRatio * Mathf.Lerp(0.985f, 1.045f, pulse01);
        var convertedColor = block.IsModuleSample ? block.ModuleSampleColor : ConvertedColor;
        var targetColor = Color.Lerp(convertedColor, Color.white, pulse01 * 0.08f);
        block.Transform.localScale = Vector3.Lerp(block.Transform.localScale, targetScale, Time.deltaTime * 5f);
        block.Renderer.color = Color.Lerp(block.Renderer.color, targetColor, Time.deltaTime * 4f);
        if (block.GlowRenderer != null)
        {
          block.GlowRenderer.color = Color.Lerp(
            block.GlowRenderer.color,
            KeepBlinkingTheme.WithAlpha(convertedColor, Mathf.Lerp(0.1f, 0.18f, pulse01)),
            Time.deltaTime * 4f);
          var glowScale = Mathf.Lerp(1.5f, 1.68f, pulse01);
          block.GlowRenderer.transform.localScale = Vector3.Lerp(
            block.GlowRenderer.transform.localScale,
            new Vector3(glowScale, glowScale, 1f),
            Time.deltaTime * 4f);
        }
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

        var targetPosition = GetCrisisArrayCenterWorldPosition();
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
        if (block.GlowRenderer != null)
        {
          block.GlowRenderer.color = Color.Lerp(
            block.GlowRenderer.color,
            KeepBlinkingTheme.WithAlpha(block.IsHovered ? HoverColor : CrisisColor, block.IsHovered ? 0.18f : 0.08f),
            Time.deltaTime * 5f);
        }
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

    private Vector3 EvaluateSoftFocusPathWorldPosition(float phase, float laneOffset)
    {
      var safeViewport = GetGameplayViewportRect(_edgeInsetViewport, _edgeInsetViewport + 0.03f);
      var travel = Mathf.Sin(phase);
      var x = safeViewport.center.x + travel * safeViewport.width * 0.49f;
      var verticalDrift = Mathf.Sin(phase * 0.53f + laneOffset * 11f) * safeViewport.height * 0.055f;
      var y = safeViewport.center.y + 0.04f + laneOffset + verticalDrift;
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

      var shouldShow = _tutorialMode && !_firstLevelPresentationHidden;
      if (_playerMarkerRoot.activeSelf != shouldShow)
      {
        _playerMarkerRoot.SetActive(shouldShow);
      }
      if (!shouldShow)
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
          color.a = 0.045f * alphaMultiplier;
        }
        else if (piece.gameObject.name.Contains("Zone"))
        {
          color.a = 0.16f * alphaMultiplier;
        }
        else if (piece.gameObject.name.Contains("Core"))
        {
          color.a = 0.38f * alphaMultiplier;
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
      var hudWorldScale = _distanceCameraFeedback != null
        ? Mathf.Max(0.01f, _distanceCameraFeedback.HudWorldScaleCompensation)
        : 1f;
      var progressBarHeight = _progressBarHeightWorld * hudWorldScale;
      _progressBarRoot.transform.position = center;

      if (_progressBarBackRenderer != null)
      {
        _progressBarBackRenderer.transform.localPosition = Vector3.zero;
        _progressBarBackRenderer.transform.localScale = new Vector3(fullWidth, progressBarHeight * 0.62f, 1f);
      }

      if (_progressBarGlowRenderer != null)
      {
        var remainingGlow = Mathf.Max(0f, _tutorialProgressGlowUntil - Time.time);
        var glowAmount = remainingGlow > 0f
          ? Mathf.Clamp01(remainingGlow / 0.9f) * (0.72f + Mathf.Sin(Time.time * 8f) * 0.18f)
          : 0f;
        _progressBarGlowRenderer.transform.localPosition = new Vector3(0f, 0f, 0.01f);
        _progressBarGlowRenderer.transform.localScale = new Vector3(
          fullWidth + (0.08f + glowAmount * 0.2f) * hudWorldScale,
          progressBarHeight + (0.06f + glowAmount * 0.12f) * hudWorldScale,
          1f);
        _progressBarGlowRenderer.color = Color.Lerp(
          KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.ProgressGlow, 0.08f),
          KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.46f),
          glowAmount);
      }

      if (_progressBarFillRenderer != null)
      {
        var progress = Mathf.Clamp01(_sampleProgress);
        var minimumVisibleFill = progress > 0f ? progressBarHeight * 0.7f : 0.001f;
        var fillWidth = Mathf.Max(minimumVisibleFill, fullWidth * progress);
        _progressBarFillRenderer.transform.localScale = new Vector3(fillWidth, progressBarHeight * 0.7f, 1f);
        _progressBarFillRenderer.transform.localPosition = new Vector3((fillWidth - fullWidth) * 0.5f, 0f, -0.01f);
      }

      if (_progressBarBorderRenderer != null)
      {
        _progressBarBorderRenderer.transform.localPosition = new Vector3(0f, 0f, -0.02f);
        _progressBarBorderRenderer.transform.localScale = new Vector3(
          fullWidth + 0.04f * hudWorldScale,
          progressBarHeight + 0.04f * hudWorldScale,
          1f);
      }
    }

    private void UpdateHoverState()
    {
      if (_distanceTracker.IsTooClose ||
          _gameplayState == GameplayState.Crisis ||
          _gameplayState == GameplayState.EyesClosedFreeze)
      {
        if (_hoveredBlock != null)
        {
          _hoveredBlock.IsHovered = false;
          _hoveredBlock = null;
        }
        _lastHoveredBlock = null;
        PublishTargetLockChangedIfNeeded();
        return;
      }

      var previousHover = _hoveredBlock;
      _lastSoftLockAngle = 999f;
      var worldSourceGaze = _distanceCameraFeedback != null
        ? _distanceCameraFeedback.OutputScreenToWorldSourceScreen(realGazeScreenPosition)
        : realGazeScreenPosition;
      _hoveredBlock = FindHoveredOrbitingBlock(worldSourceGaze);

      var lockHoldUsed = false;
      if (_hoveredBlock == null &&
          _installedModules.Contains(FirstLevelModuleId.LockHold) &&
          previousHover != null &&
          previousHover.State == BlockState.Orbiting &&
          Time.time - _lastHoveredAt <= 0.4f)
      {
        _hoveredBlock = previousHover;
        lockHoldUsed = true;
        if (_lockHoldActiveTargetId != previousHover.Serial)
        {
          _lockHoldActiveTargetId = previousHover.Serial;
          ActivateModuleEffect(FirstLevelModuleId.LockHold);
        }
      }
      else
      {
        _lockHoldActiveTargetId = NoTargetId;
      }

      if (previousHover != null && previousHover != _hoveredBlock)
      {
        previousHover.IsHovered = false;
      }

      if (_hoveredBlock != null)
      {
        _hoveredBlock.IsHovered = true;
        _lastHoveredBlock = _hoveredBlock;
        if (!lockHoldUsed)
        {
          _lastHoveredAt = Time.time;
        }

        if (_hoveredBlock.StartsHalfLocked)
        {
          _hoveredBlock.StartsHalfLocked = false;
          _deepRecoveryTargetId = NoTargetId;
          ActivateModuleEffect(FirstLevelModuleId.DeepRecovery);
        }
      }

      PublishTargetLockChangedIfNeeded();
    }

    private void DisableNormalTargetLock()
    {
      if (_hoveredBlock != null)
      {
        _hoveredBlock.IsHovered = false;
      }
      if (_lastHoveredBlock != null)
      {
        _lastHoveredBlock.IsHovered = false;
      }
      _hoveredBlock = null;
      _lastHoveredBlock = null;
      _lockHoldActiveTargetId = NoTargetId;
      PublishTargetLockChangedIfNeeded();
    }

    private void UpdateSoftFocusPurification()
    {
      if (_tutorialMode || _softFocusField == null)
      {
        return;
      }

      var canAccumulate = _softFocusField.CanAccumulate;
      var capacity = _softFocusField.ConcurrentCapacity;
      var activeCount = 0;
      var convertedThisFrame = 0;
      var firstConvertedPosition = Vector3.zero;
      for (var i = 0; i < _blocks.Count; i++)
      {
        var block = _blocks[i];
        if (block == null || block.GameObject == null || block.State != BlockState.Orbiting)
        {
          continue;
        }

        var sourceScreen = _camera.WorldToScreenPoint(block.Transform.position);
        var outputScreen = new Vector2(sourceScreen.x, sourceScreen.y);
        if (_distanceCameraFeedback != null)
        {
          outputScreen = _distanceCameraFeedback.WorldSourceScreenToOutputScreen(outputScreen);
        }
        var outputViewport = new Vector2(
          Screen.width > 0 ? outputScreen.x / Screen.width : 0.5f,
          Screen.height > 0 ? outputScreen.y / Screen.height : 0.5f);
        block.IsInsideSoftFocusField = sourceScreen.z > 0f &&
                                       _softFocusField.ContainsViewportPoint(outputViewport);
        block.IsSoftFocused = block.IsInsideSoftFocusField && canAccumulate && activeCount < capacity;
        if (!block.IsSoftFocused)
        {
          UpdateSoftFocusProgressVisual(block);
          continue;
        }

        activeCount++;
        if (block.StartsHalfLocked && block.SoftFocusProgress < 0.5f)
        {
          block.SoftFocusProgress = 0.5f;
          block.StartsHalfLocked = false;
          _deepRecoveryTargetId = NoTargetId;
          ActivateModuleEffect(FirstLevelModuleId.DeepRecovery);
        }

        block.SoftFocusProgress = SoftFocusFieldLogic.AdvancePurification(
          block.SoftFocusProgress,
          Time.deltaTime,
          _softFocusField.PurificationSeconds,
          _softFocusField.PurificationSpeedMultiplier,
          true,
          _softFocusField.CanComplete);

        UpdateSoftFocusProgressVisual(block);
        if (block.SoftFocusProgress < 1f || !_softFocusField.CanComplete)
        {
          continue;
        }

        block.SoftFocusProgress = 1f;
        block.IsSoftFocused = false;
        if (convertedThisFrame == 0)
        {
          firstConvertedPosition = block.Transform.position;
        }
        convertedThisFrame++;
        StartCoroutine(HarvestRoutine(block));
      }

      if (convertedThisFrame <= 0)
      {
        return;
      }

      if (_installedModules.Contains(FirstLevelModuleId.FullLoop))
      {
        _fullLoopStage = FullLoopStage.WaitingForRest;
      }
      if (activeCount > _softFocusField.BaseConcurrentCapacity && _installedModules.Contains(FirstLevelModuleId.ChainBlink))
      {
        ActivateModuleEffect(FirstLevelModuleId.ChainBlink);
      }
      if (activeCount > _softFocusField.BaseConcurrentCapacity + 1 && _installedModules.Contains(FirstLevelModuleId.WideChain))
      {
        ActivateModuleEffect(FirstLevelModuleId.WideChain);
      }
      if (convertedThisFrame >= 3 && _installedModules.Contains(FirstLevelModuleId.PreciseHarvest))
      {
        SpawnConvertedModuleSample(firstConvertedPosition, KeepBlinkingTheme.AccentPrimary, 0, false);
        ActivateModuleEffect(FirstLevelModuleId.PreciseHarvest);
      }
    }

    private void UpdateSoftFocusProgressVisual(OrbitBlock block)
    {
      var segments = block.SoftFocusProgressSegments;
      if (segments == null)
      {
        return;
      }

      var visibleProgress = block.State == BlockState.Orbiting &&
                            _gameplayState != GameplayState.ModuleUpgrade &&
                            !_firstLevelPresentationHidden
        ? Mathf.Clamp01(block.SoftFocusProgress)
        : 0f;
      for (var index = 0; index < segments.Length; index++)
      {
        var segment = segments[index];
        if (segment == null)
        {
          continue;
        }

        var segmentStart = index / (float)segments.Length;
        var alpha = visibleProgress > segmentStart
          ? block.IsSoftFocused ? 0.78f : 0.34f
          : 0f;
        segment.color = Color.Lerp(
          segment.color,
          KeepBlinkingTheme.WithAlpha(ConvertedColor, alpha),
          1f - Mathf.Exp(-8f * Time.deltaTime));
      }
    }

    private void PublishTargetLockChangedIfNeeded()
    {
      var targetId = LockedTargetId;
      if (targetId == _lastSignaledLockedTargetId)
      {
        return;
      }

      _lastSignaledLockedTargetId = targetId;
      InvokeSignalSafely(TargetLockChanged, targetId, nameof(TargetLockChanged));
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
        _lastSoftLockAngle = bestAngle;
      }

      return bestBlock;
    }

    private bool IsActiveTargetBlock(OrbitBlock block)
    {
      return block != null && block.State == BlockState.Orbiting;
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

    public bool TryGetDevelopmentTargetRegionHalfSizeNormalized(out Vector2 halfSize)
    {
      halfSize = default;
      if (Screen.width <= 0 || Screen.height <= 0)
      {
        return false;
      }

      var total = Vector2.zero;
      var count = 0;
      for (var i = 0; i < _blocks.Count; i++)
      {
        var block = _blocks[i];
        if (!IsActiveTargetBlock(block) || !TryGetScreenRect(block, out var rect))
        {
          continue;
        }

        var padded = PadRect(rect, _gazePaddingPixels);
        total += new Vector2(padded.width / (2f * Screen.width), padded.height / (2f * Screen.height));
        count++;
      }

      if (count == 0)
      {
        return false;
      }

      halfSize = total / count;
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
      if (!_tutorialMode)
      {
        _blinkQueued = false;
        return;
      }

      if (_distanceTracker.IsTooClose)
      {
        _blinkQueued = false;
        return;
      }

      if (Time.frameCount == _suppressBlinkHarvestFrame)
      {
        _blinkQueued = false;
        return;
      }

      if (_calibrationActive)
      {
        return;
      }

      if (_gameplayState == GameplayState.Crisis ||
          _gameplayState == GameplayState.EyesClosedFreeze)
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

      if (block == null || block.State != BlockState.Orbiting)
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

      PublishTargetLockChangedIfNeeded();

      StartCoroutine(HarvestRoutine(block));
      var chainConvertedCount = ConvertNearbyTargetsFromBlink(block);
      HandleBlinkModuleSequence(block, chainConvertedCount);
      InvokeSignalSafely(
        NormalBlinkConversionCompleted,
        block.Serial,
        1 + chainConvertedCount,
        nameof(NormalBlinkConversionCompleted));
      _blinkCaptureCount++;
    }

    private int ConvertNearbyTargetsFromBlink(OrbitBlock primaryBlock)
    {
      var nearbyTargetLimit = _installedModules.Contains(FirstLevelModuleId.WideBlink)
        ? 2
        : _installedModules.Contains(FirstLevelModuleId.ChainBlink)
          ? 1
          : 0;
      if (nearbyTargetLimit <= 0)
      {
        return 0;
      }

      var rangeMultiplier = _installedModules.Contains(FirstLevelModuleId.WideChain) ? 1.25f : 1f;
      if (_wakeEchoRangePrimed)
      {
        rangeMultiplier *= 1.5f;
      }

      var range = Mathf.Max(0.1f, _chainConversionRadiusWorld * rangeMultiplier);
      var selectedTargets = new List<OrbitBlock>(nearbyTargetLimit);
      for (var selectionIndex = 0; selectionIndex < nearbyTargetLimit; selectionIndex++)
      {
        OrbitBlock nearest = null;
        var nearestDistance = float.PositiveInfinity;
        for (var i = 0; i < _blocks.Count; i++)
        {
          var candidate = _blocks[i];
          if (candidate == primaryBlock ||
              candidate.State != BlockState.Orbiting ||
              selectedTargets.Contains(candidate))
          {
            continue;
          }

          var distance = Vector2.Distance(primaryBlock.Transform.position, candidate.Transform.position);
          if (distance <= range && distance < nearestDistance)
          {
            nearest = candidate;
            nearestDistance = distance;
          }
        }

        if (nearest == null)
        {
          break;
        }

        selectedTargets.Add(nearest);
      }

      for (var i = 0; i < selectedTargets.Count; i++)
      {
        StartCoroutine(HarvestRoutine(selectedTargets[i]));
      }

      if (selectedTargets.Count > 0)
      {
        ActivateModuleEffect(_installedModules.Contains(FirstLevelModuleId.WideBlink)
          ? FirstLevelModuleId.WideBlink
          : FirstLevelModuleId.ChainBlink);
        if (_installedModules.Contains(FirstLevelModuleId.WideChain))
        {
          ActivateModuleEffect(FirstLevelModuleId.WideChain);
        }

        if (_wakeEchoRangePrimed)
        {
          _wakeEchoRangePrimed = false;
          ActivateModuleEffect(FirstLevelModuleId.WakeEcho);
        }
      }

      return selectedTargets.Count;
    }

    private void HandleBlinkModuleSequence(OrbitBlock primaryBlock, int chainConvertedCount)
    {
      var totalConverted = 1 + chainConvertedCount;
      if (_installedModules.Contains(FirstLevelModuleId.PreciseHarvest) && totalConverted >= 3)
      {
        SpawnConvertedModuleSample(primaryBlock.Transform.position, KeepBlinkingTheme.AccentPrimary, 0, false);
        ActivateModuleEffect(FirstLevelModuleId.PreciseHarvest);
      }

      if (_installedModules.Contains(FirstLevelModuleId.FullLoop))
      {
        _fullLoopStage = FullLoopStage.WaitingForRest;
      }
    }

    private OrbitBlock GetBlinkHarvestTarget()
    {
      if (_hoveredBlock != null && _hoveredBlock.State == BlockState.Orbiting)
      {
        return _hoveredBlock;
      }

      if (_lastHoveredBlock != null &&
          _lastHoveredBlock.State == BlockState.Orbiting &&
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
      var isCrisisConversion = block.CrisisWaveId > 0;
      block.State = BlockState.Converted;
      block.ConvertedAt = Time.time;
      block.IsHovered = false;
      block.Renderer.sortingOrder = 12;
      if (block.GlowRenderer != null)
      {
        block.GlowRenderer.sortingOrder = 11;
      }
      _harvestedCount++;
      InvokeSignalSafely(TargetConverted, block.Serial, nameof(TargetConverted));
      PublishTargetLockChangedIfNeeded();

      var startColor = isCrisisConversion
        ? Color.Lerp(block.Renderer.color, KeepBlinkingTheme.OrbitSignal, 0.9f)
        : block.Renderer.color;
      block.Renderer.color = startColor;
      var startScale = block.Transform.localScale;
      var startRotation = block.Transform.rotation;
      var targetScale = block.BaseScale * _harvestScaleRatio;
      var elapsed = 0f;

      while (elapsed < _harvestSeconds && block.GameObject != null)
      {
        elapsed += Time.deltaTime;
        var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / _harvestSeconds));
        block.Renderer.color = Color.Lerp(startColor, ConvertedColor, t);
        if (block.GlowRenderer != null)
        {
          block.GlowRenderer.color = Color.Lerp(
            block.GlowRenderer.color,
            KeepBlinkingTheme.WithAlpha(ConvertedColor, 0.12f),
            Time.deltaTime * 5f);
        }
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

      var shouldShow = _tutorialMode &&
                       _gameplayState == GameplayState.Crisis &&
                       !isEyesClosed &&
                       !IsTutorialInputSuspended &&
                       IsCalibrationInputReady &&
                       _camera != null;
      if (!shouldShow)
      {
        if (_gazeIndicatorRoot.activeSelf)
        {
          _gazeIndicatorRoot.SetActive(false);
        }
        return;
      }

      if (!_gazeIndicatorRoot.activeSelf)
      {
        _gazeIndicatorRoot.SetActive(true);
        _gazeIndicatorRoot.transform.position = _camera.ScreenToWorldPoint(
          new Vector3(realGazeScreenPosition.x, realGazeScreenPosition.y, _blockDepthFromCamera));
      }

      var targetPosition = _camera.ScreenToWorldPoint(
        new Vector3(realGazeScreenPosition.x, realGazeScreenPosition.y, _blockDepthFromCamera));
      _gazeIndicatorRoot.transform.position = Vector3.Lerp(
        _gazeIndicatorRoot.transform.position,
        targetPosition,
        1f - Mathf.Exp(-14f * Time.deltaTime));
      var pulse = 1f + Mathf.Sin(Time.time * 2.2f) * 0.045f;
      _gazeIndicatorRoot.transform.localScale = Vector3.one * pulse;

      for (var i = 0; i < _gazeIndicatorPieces.Count; i++)
      {
        var piece = _gazeIndicatorPieces[i];
        if (piece == null)
        {
          continue;
        }

        var color = piece.color;
        color.a = piece.gameObject.name.Contains("Core") ? 0.5f : 0.11f;
        piece.color = color;
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

    private OrbitBlock FindBlockById(int targetId)
    {
      if (targetId == NoTargetId)
      {
        return null;
      }

      for (var i = 0; i < _blocks.Count; i++)
      {
        var block = _blocks[i];
        if (block != null && block.GameObject != null && block.Serial == targetId)
        {
          return block;
        }
      }

      return null;
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

      PublishTargetLockChangedIfNeeded();

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

    private void EnsurePresentationStyles()
    {
      var scale = GetMobileUiScale();
      if (_warningTitleStyle != null &&
          _warningBodyStyle != null &&
          _reportTitleStyle != null &&
          _reportBodyStyle != null &&
          _reportLabelStyle != null &&
          _reportMetricStyle != null &&
          _cardTagStyle != null &&
          _cardTitleStyle != null &&
          _cardBodyStyle != null &&
          _cardDeltaStyle != null &&
          _cardLevelStyle != null &&
          _moduleHeaderStyle != null &&
          _moduleInstructionStyle != null &&
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

      _moduleHeaderStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = ScaledFontSize(24),
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.MiddleCenter,
        wordWrap = false,
        normal = { textColor = KeepBlinkingTheme.TextPrimary },
      };

      _moduleInstructionStyle = new GUIStyle(GUI.skin.label)
      {
        fontSize = ScaledFontSize(12),
        fontStyle = FontStyle.Bold,
        alignment = TextAnchor.MiddleCenter,
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

      if (_dataSeedSprite != null)
      {
        Destroy(_dataSeedSprite);
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

      if (_dataSeedTexture != null)
      {
        Destroy(_dataSeedTexture);
      }

      if (_backgroundTexture != null)
      {
        Destroy(_backgroundTexture);
      }

      DestroyRuntimeClip(_freezeStartedClip);
      DestroyRuntimeClip(_coverageCompleteClip);
      DestroyRuntimeClip(_freezeInterruptedClip);
      DestroyRuntimeClip(_freezeClearedClip);
      DestroyRuntimeClip(_tutorialFocusClip);
      DestroyRuntimeClip(_tutorialBlinkClip);
      DestroyRuntimeClip(_tutorialConvertedClip);
      DestroyRuntimeClip(_tutorialPushAwayClip);
      DestroyRuntimeClip(_tutorialExperienceCompleteClip);
      DestroyRuntimeClip(_tutorialCountdownClip);
      DestroyRuntimeClip(_moduleInstalledClip);
      DestroyRuntimeClip(_moduleActivatedClip);
      DestroyRuntimeClip(_bossCloseRequestClip);
      DestroyRuntimeClip(_bossSuccessfulReleaseClip);
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
      public readonly SpriteRenderer GlowRenderer;
      public readonly int Serial;
      public readonly float CreatedAt;
      public readonly string Name;
      public readonly Vector3 BaseScale;

      public BlockState State;
      public bool IsHovered;
      public bool IsInsideSoftFocusField;
      public bool IsSoftFocused;
      public float SoftFocusProgress;
      public float SoftFocusLaneOffset;
      public SpriteRenderer[] SoftFocusProgressSegments;
      public float Phase;
      public float AngularSpeed;
      public float CrisisMoveSpeed;
      public int CrisisWaveId;
      public int BossRoundId;
      public float ConvertedAt = -1f;
      public bool StartsHalfLocked;
      public bool IsModuleSample;
      public Color ModuleSampleColor;

      public OrbitBlock(
        GameObject gameObject,
        SpriteRenderer renderer,
        SpriteRenderer glowRenderer,
        float phase,
        float angularSpeed,
        int serial,
        float createdAt,
        Vector3 baseScale)
      {
        GameObject = gameObject;
        Transform = gameObject.transform;
        Renderer = renderer;
        GlowRenderer = glowRenderer;
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

  }
}
