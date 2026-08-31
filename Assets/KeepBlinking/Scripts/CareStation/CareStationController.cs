using System;
using System.Collections.Generic;
using KeepBlinking.Gameplay;
using KeepBlinking.Input;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace KeepBlinking.CareStation
{
  public sealed class CareStationController : MonoBehaviour
  {
    [Header("Offline Production")]
    [SerializeField, Min(1f)] private float _minimumOfflineMinutes = 30f;
    [SerializeField, Min(1f)] private float _offlineXpPerHour = 18f;

    [Header("Care Actions")]
    [SerializeField, Min(1f)] private float _screenDownSeconds = 20f;
    [SerializeField, Min(1f)] private float _drySpotRestSeconds = 45f;
    [SerializeField, Min(0.2f)] private float _closedEyeStartHoldSeconds = 1.5f;
    [SerializeField, Min(0.1f)] private float _reopenHoldSeconds = 0.5f;
    [SerializeField, Range(0.25f, 0.4f)] private float _gestureReferenceCaptureSeconds = 0.3f;
    [SerializeField, Range(3, 15)] private int _gestureReferenceMinimumSamples = 5;
    [SerializeField, Min(0.1f)] private float _gestureScaleSmoothingSpeed = 12f;
    // Linear distance fractions (see FaceDistanceRatio): 0.22 means the step completes once
    // the player has moved to 1/1.22 = 82% of the reference distance, about 8 cm from 45 cm.
    [SerializeField, Range(0.01f, 0.12f)] private float _distanceDeadZone = 0.05f;
    [SerializeField, Range(0.08f, 0.4f)] private float _distanceCompleteThreshold = 0.22f;
    [SerializeField, Range(0.05f, 1f)] private float _distanceStepHoldSeconds = 0.25f;
    [SerializeField, Range(0.05f, 1f)] private float _distanceProgressFallSeconds = 0.25f;
    [SerializeField, Range(0f, 2f)] private float _distanceStepTransitionSeconds = 0.4f;
    [SerializeField, Min(1f)] private float _distanceFallbackDelay = 8f;
    [SerializeField, Min(0.5f)] private float _tooClosePromptDelay = 2f;
    [SerializeField, Min(0.1f)] private float _distanceSafetyRecoverySeconds = 1f;
    [SerializeField, Range(0.1f, 0.8f)] private float _unverifiedExtremeFaceOccupancy = 0.32f;

    [Header("Care Recipes")]
    [SerializeField, Range(0f, 1f)] private float _singleRecipeWeight = 0.25f;
    [SerializeField, Range(0f, 1f)] private float _doubleRecipeWeight = 0.55f;
    [SerializeField, Range(0f, 1f)] private float _tripleRecipeWeight = 0.20f;
    [SerializeField, Range(1, 128)] private int _recipeGenerationMaximumAttempts = 32;
    [SerializeField, Range(0.1f, 2f)] private float _recipeStepFeedbackSeconds = 0.65f;
    [SerializeField, Range(2f, 4f)] private float _routineIntroSeconds = CareActionLibrary.RoutineIntroSeconds;
    [SerializeField, Range(1f, 3f)] private float _recipeCompletionFeedbackSeconds = CareActionLibrary.RecipeCompletionFeedbackSeconds;

    [Header("Station Upgrades")]
    [SerializeField] private CareStationUpgradeConfiguration _upgradeConfiguration = new CareStationUpgradeConfiguration();
    [SerializeField] private CareEconomyConfiguration _economyConfiguration = new CareEconomyConfiguration();

    [Header("Production Line")]
    [SerializeField] private CareProductionConfiguration _productionConfiguration = new CareProductionConfiguration();

    [Header("Research")]
    [SerializeField] private bool _researchMode;

    [Header("Presentation")]
    [SerializeField, Min(0.1f)] private float _repairRevealSeconds = 1.4f;
    [SerializeField, Range(8, 30)] private int _maximumXpBundleVisuals = 24;

    private EdgeOrbitHarvestMvp _gameplay;
    private CareActionRunner _careActions;
    private CareStationView _view;
    private CareStationAudio _stationAudio;
    private readonly CareStationProductionController _production = new CareStationProductionController();
    private CareStationSaveService _saveService;
    private CareStationSaveData _save;
    private CareResearchSessionRecorder _researchRecorder;
    private CareRecipeRuntime _recipe;
    private CareStationOfflineResult _lastOfflineResult;
    private readonly CareStationExperienceLedger _ledger = new CareStationExperienceLedger();
    private readonly HashSet<int> _arrivedCollectionTargetIds = new HashSet<int>();
    private float _stateStartedAt;
    private float _nextActionSaveAt;
    private float _nextProductionSaveAt;
    private bool _xpBundlesSpawned;
    private int _collectionSpawnedBundleCount;
    private string _collectionPausedReason = "NONE";
    private float _lastCollectionRecoveryAttemptAt = -1f;
    private bool _subscribed;
    private bool _resumingFromPause;
    private bool _resumingFromFocus;
    private float _tooCloseHeld;
    private float _distanceSafetyNeutralHeld;
    private float _pushAwayRecognitionStartedAt = -1f;
    private float _developmentActionStartedAt = -1f;
    private CareDistanceReferenceSampler _pushReferenceSampler;
    private CareRelativeDistanceStep _pushDistanceStep;
    private CareDistanceDirection _pushStepDirection = CareDistanceDirection.None;
    private long _lastPushScaleSequence = long.MinValue;
    private float _smoothedPushFaceScale;
    private bool _hasSmoothedPushFaceScale;
    private float _currentPushRatio = 1f;
    private float _currentPushFaceScale;
    private float _rawPushFaceScale;
    private float _pushObservedMinimum = float.PositiveInfinity;
    private float _pushObservedMaximum = float.NegativeInfinity;
    private float _lastPushFreshSampleAt = -1f;
    private float _returnRecognitionStartedAt = -1f;
    private float _distanceStepOpenedAt = -1f;
    private int _pushFreshSamplesInStep;
    private CareStationState _resumeStateBeforeWelcome = CareStationState.Dormant;
    private int _pendingStepFeedbackEnergy;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private float _clearResearchConfirmationUntil = -1f;
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    private bool? _developmentEyesClosed;
    private float _developmentNeutralUntil;
    private float _developmentCareTimeMultiplier = 1f;
    private float? _developmentDistanceRatio;
    private CareRecipeRuntime _developmentRecipe;
    private bool _developmentRecipeAdvancePending;
    private float _developmentRecipeAdvanceAt;
    private CareStationUiInputDiagnostics _uiInputDiagnostics;
#endif

    public static CareStationController Instance { get; private set; }
    public CareStationState State { get; private set; } = CareStationState.Dormant;
    public CareStationSaveData SaveData => _save;
    public event Action<CareStationState> StateChanged;
    public event Action<int, CareActionType> RecipeStepCompleted;
    public event Action RecipeCompleted;
    public event Action FirstStationInspectionCompleted;

    public static CareStationController EnsureExists(EdgeOrbitHarvestMvp gameplay)
    {
      if (!string.Equals(SceneManager.GetActiveScene().name, "SampleScene", StringComparison.Ordinal)) return null;
      if (Instance == null) Instance = FindFirstObjectByType<CareStationController>();
      if (Instance == null)
      {
        var owner = new GameObject("Eye Care Station");
        Instance = owner.AddComponent<CareStationController>();
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
      var researchSetting = Resources.Load<TextAsset>("CareStation/ResearchMode");
      if (researchSetting != null && bool.TryParse(researchSetting.text.Trim(), out var configuredResearchMode))
        _researchMode |= configuredResearchMode;
      _view = gameObject.AddComponent<CareStationView>();
      _view.Build();
      _stationAudio = gameObject.AddComponent<CareStationAudio>();
      _stationAudio.Build();
      _careActions = gameObject.AddComponent<CareActionRunner>();
      _careActions.CareActionCompleted += HandleUnifiedCareActionCompleted;
      _view.StartCareSelected += HandleStartCareSelected;
      _view.ContinueSelected += HandleWelcomeContinue;
      _view.FallbackCollectSelected += HandleFallbackCollect;
      _view.ReturnFallbackSelected += HandleReturnFallback;
      _view.UpgradeSelected += HandleUpgradeSelected;
      _view.NavigationSelected += HandleNavigationSelected;
      _view.UpgradeBackSelected += HandleUpgradeBackSelected;
      _view.ChangeStepSelected += HandleChangeStepRequested;
      _view.UseRestSelected += HandleUseRestSelected;
      _view.KeepStepSelected += HandleKeepStepSelected;
      _view.EndShiftSelected += HandleEndShiftSelected;
      _view.SubjectiveScoresChanged += HandleSubjectiveScoresChanged;
      _view.SubjectiveScoresSubmitted += HandleSubjectiveScoresSubmitted;
      _view.SubjectiveScoresSkipped += HandleSubjectiveScoresSkipped;
      _view.CareReportDoneSelected += HandleCareReportDone;
      _saveService = new CareStationSaveService();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _uiInputDiagnostics = gameObject.AddComponent<CareStationUiInputDiagnostics>();
      _uiInputDiagnostics.Bind(this);
      gameObject.AddComponent<CareStationDevelopmentOverlay>().Bind(this);
#endif
    }

    private void Bind(EdgeOrbitHarvestMvp gameplay)
    {
      if (_gameplay == gameplay && _save != null) return;
      Unsubscribe();
      _gameplay = gameplay;
      if (_gameplay == null) return;
      _gameplay.SetCareStationMode(true);
      _careActions.Bind(_gameplay, _view);
      _careActions.ConfigureStationDurations(
        _screenDownSeconds,
        _drySpotRestSeconds,
        _closedEyeStartHoldSeconds,
        _reopenHoldSeconds);
      Subscribe();
      InitializeStation();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _uiInputDiagnostics?.DumpCurrentPointer("POST LOAD UI INPUT SNAPSHOT");
#endif
    }

    private void InitializeStation()
    {
      SetState(CareStationState.LoadingSave);
      var now = DateTime.UtcNow;
      _save = _saveService.Load(now);
      _researchRecorder = new CareResearchSessionRecorder(_researchMode);
      if (_save.currentState != CareStationState.AutoShift || !_save.endShiftConsumed)
        _researchRecorder.BeginOrResume(_save);
      CareStationShiftRules.SynchronizeUpgradeValues(_save, _upgradeConfiguration);
      RestoreRecipeRuntime();
      SettleOffline(now, true);
      _view.ApplyStation(_save);
      _view.SetPendingXp(CurrentRemainingBottleValue, CurrentGoldBottleCount);
      ResumeSavedFlow();
      _view.RebindInputHandlers();
      _view.SynchronizeUiInputOwnership(IsGuidanceInputExpected());
    }

    private void ResumeSavedFlow()
    {
      if (_lastOfflineResult.HasAnything && IsSessionEntryState(_save.currentState))
      {
        _resumeStateBeforeWelcome = _save.currentState;
        SetState(CareStationState.WelcomeBack);
        _view.ShowWelcome(_lastOfflineResult);
        return;
      }

      switch (_save.currentState)
      {
        case CareStationState.PresentOfflineBottles:
        case CareStationState.WaitOfflinePushAway:
        case CareStationState.CollectingOfflineBottles:
        case CareStationState.WaitOfflineBottlesStored:
          BeginDistanceReset();
          break;
        case CareStationState.OfflineProductionSummary:
          SetState(CareStationState.WelcomeBack);
          _view.ShowWelcome(_lastOfflineResult);
          break;
        case CareStationState.WaitDistanceResetMoveAway:
        case CareStationState.WaitDistanceResetReturn:
          RestoreDistanceReset();
          break;
        case CareStationState.WaitReturnToNeutral:
          EnterWaitReturnToNeutral();
          break;
        case CareStationState.PresentIncident:
        case CareStationState.WaitIncidentSelection:
          // Legacy UI states are presentation-only. Preserve the recipe and
          // return to the normal station without rebuilding an Incident card.
          EnterStationWorking();
          break;
        case CareStationState.StationWorking:
          SetState(CareStationState.StationWorking);
          _view.ShowStationWorking();
          break;
        case CareStationState.PromptCareAction:
          EnsureCurrentRecipe();
          if (_save.currentRecipe != null && !_save.currentRecipe.routineIntroCompleted)
            _view.ShowCareRoutineIntro(_save.currentRecipe);
          else
            RestoreCareAction();
          break;
        case CareStationState.CareActionInProgress:
        case CareStationState.CareActionPaused:
        case CareStationState.WaitCareActionStart:
          RestoreCareAction();
          break;
        case CareStationState.CareActionCompleted:
          EnterCareActionCompleted();
          break;
        case CareStationState.RepairReveal:
          EnterRepairReveal();
          break;
        case CareStationState.ProduceBottles:
          ResumeProductionLine();
          break;
        case CareStationState.PresentCareBottles:
        case CareStationState.WaitCarePushAway:
        case CareStationState.WaitPushAwayReady:
        case CareStationState.WaitPushAway:
          EnterProduceBottles();
          break;
        case CareStationState.CollectingExperience:
        case CareStationState.WaitExperienceCollected:
        case CareStationState.CollectingCareBottles:
        case CareStationState.WaitCareBottlesStored:
          _save.activeCollectionPhase = CareStationCollectionPhase.Care;
          ResumeCollectionAfterReload();
          break;
        case CareStationState.WaitStorageSpace:
          RestoreStorageFullGate();
          break;
        case CareStationState.InspectionPreparing:
          EnterInspectionPreparing(false);
          break;
        case CareStationState.InspectionPassed:
          EnterInspectionPassed(false);
          break;
        case CareStationState.PreCareCheck:
          EnterPreCareCheck();
          break;
        case CareStationState.PostCareCheck:
          EnterPostCareCheck();
          break;
        case CareStationState.CareReport:
          EnterCareReport();
          break;
        case CareStationState.AutoShift:
          EnterAutoShift();
          break;
        case CareStationState.UpgradeSelection:
          EnterUpgradeSelection();
          break;
        case CareStationState.ShiftComplete:
          EnterShiftCompletePresentation();
          break;
        default:
          BeginSessionCollectionFlow();
          break;
      }
    }

    private void Update()
    {
      if (_gameplay == null || _save == null) return;
      _view?.SynchronizeUiInputOwnership(IsGuidanceInputExpected());
#if UNITY_EDITOR || UNITY_STANDALONE
      if (_view.IsUpgradeVisible && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
      {
        HandleUpgradeBackSelected();
        return;
      }
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_developmentRecipe != null && !_developmentRecipe.Data.recipeCompleted)
      {
        if (_developmentRecipeAdvancePending && Time.unscaledTime >= _developmentRecipeAdvanceAt)
        {
          _developmentRecipeAdvancePending = false;
          StartDevelopmentRecipeCurrentAction();
        }
        return;
      }
#endif
      if (_careActions != null && _careActions.IsDevelopmentTest) return;
      var delta = Time.unscaledDeltaTime;
      if (_careActions != null && !_careActions.IsDevelopmentTest && _careActions.SaveData != null)
        _save.careAction = _careActions.SaveData;
      _researchRecorder?.ObserveAction(_save, delta);
      UpdateDistanceSafety(delta);
      switch (State)
      {
        case CareStationState.PresentOfflineBottles:
        case CareStationState.WaitOfflinePushAway:
          BeginDistanceReset();
          break;
        case CareStationState.WaitDistanceResetMoveAway:
        case CareStationState.WaitDistanceResetReturn:
          UpdateDistanceReset(delta);
          break;
        case CareStationState.WaitCarePushAway:
          UpdateWaitForPushAway(delta);
          break;
        case CareStationState.CollectingOfflineBottles:
        case CareStationState.WaitOfflineBottlesStored:
        case CareStationState.CollectingCareBottles:
        case CareStationState.WaitCareBottlesStored:
          if (MaintainCurrentCollection()) _view.ShowCollecting(CurrentRemainingBottleValue);
          break;
        case CareStationState.WaitReturnToNeutral:
          UpdateReturnToNeutral(delta);
          break;
        case CareStationState.PromptCareAction:
          EnsureCurrentRecipe();
          if (_save.currentRecipe == null) break;
          _save.currentRecipe.routineIntroElapsedSeconds += delta;
          if (_save.currentRecipe.routineIntroElapsedSeconds < _routineIntroSeconds) break;
          _save.currentRecipe.routineIntroCompleted = true;
          _save.currentRecipe.routineIntroElapsedSeconds = _routineIntroSeconds;
          StartStationCareAction(_recipe.CurrentAction, false);
          break;
        case CareStationState.StationWorking:
          // Foreground waiting is intentionally economy-neutral. Care begins
          // only from the explicit START CARE action.
          break;
        case CareStationState.WaitCareActionStart:
        case CareStationState.CareActionInProgress:
        case CareStationState.CareActionPaused:
          UpdateUnifiedCareAction();
          break;
        case CareStationState.CareActionCompleted:
          UpdateCareActionCompleted(delta);
          break;
        case CareStationState.RepairReveal:
          if (StateElapsed >= _repairRevealSeconds) EnterProduceBottles();
          break;
        case CareStationState.ProduceBottles:
          UpdateProductionLine(delta);
          break;
        case CareStationState.PresentCareBottles:
          // v22 migration redirects this legacy presentation into ProduceBottles.
          EnterProduceBottles();
          break;
        case CareStationState.WaitPushAwayReady:
          UpdateWaitForPushAway(delta);
          break;
        case CareStationState.WaitPushAway:
          UpdateWaitForPushAway(delta);
          break;
        case CareStationState.CollectingExperience:
        case CareStationState.WaitExperienceCollected:
          if (MaintainCurrentCollection()) _view.ShowCollecting(CurrentRemainingBottleValue);
          break;
        case CareStationState.WaitStorageSpace:
          // The player may close the upgrade page and continue viewing the
          // station or reports. Do not reopen the modal every frame.
          break;
        case CareStationState.InspectionPreparing:
          if (StateElapsed >= 1.2f) StartInspectionCurrentAction(false);
          break;
        case CareStationState.InspectionPassed:
          if (StateElapsed >= _repairRevealSeconds) EnterProduceBottles();
          break;
        case CareStationState.ShiftComplete:
        case CareStationState.AutoShift:
        case CareStationState.PreCareCheck:
        case CareStationState.PostCareCheck:
        case CareStationState.CareReport:
          // Daily end states are explicit gates. They never generate another
          // event while the player remains in the foreground.
          break;
      }
    }

    public void NotifyDistanceBaselineReady()
    {
      // Retained for the legacy prototype. Focus Shift reads the immutable
      // Session baseline directly from gameplay when its runner starts; this
      // notification is not allowed to overwrite that baseline mid-routine.
    }

    public void SimulateOffline(TimeSpan duration)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_save == null) return;
      var now = DateTime.UtcNow;
      _save.lastClaimedUtc = now.Subtract(duration).ToString("O");
      _save.lastActiveUtc = now.Subtract(duration).ToString("O");
      SettleOffline(now, false);
      _view.ApplyStation(_save);
      _view.SetPendingXp(RemainingOfflineBottleValue);
      if (_lastOfflineResult.HasAnything && IsSessionEntryState(_save.currentState))
      {
        _resumeStateBeforeWelcome = _save.currentState;
        SetState(CareStationState.WelcomeBack);
        _view.ShowWelcome(_lastOfflineResult);
      }
#endif
    }

    public void StartNextShiftDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_save == null || State != CareStationState.AutoShift ||
          !PrepareNextShift(false, true)) return;
      _lastOfflineResult = default;
      _view.ApplyStation(_save);
      BeginSessionCollectionFlow();
#endif
    }

    public void FillStorageDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_save == null) return;
      _save.storedFullBottles = CareStationStorageRules.Capacity(_save);
      _save.storedGoldBottles = 0;
      _save.offlineProductionPausedByFullStorage = true;
      CareStationShiftRules.SynchronizeUpgradeValues(_save, _upgradeConfiguration);
      _view.ApplyStation(_save);
      _view.ShowStorageFull(_save, _upgradeConfiguration);
      SaveNow();
#endif
    }

    public void FreeOneStorageSlotDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_save == null) return;
      if (_save.storedFullBottles > 0) _save.storedFullBottles--;
      _save.offlineProductionPausedByFullStorage = CareStationStorageRules.Remaining(_save) <= 0;
      CareStationShiftRules.SynchronizeUpgradeValues(_save, _upgradeConfiguration);
      _view.ApplyStation(_save);
      if (State == CareStationState.WaitStorageSpace) ResumeAfterStorageSpaceAvailable();
      SaveNow();
#endif
    }

    public void SimulateOfflineFullDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      FillStorageDevelopment();
      SimulateOffline(TimeSpan.FromHours(4));
#endif
    }

    public void JumpToShift(int shift)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_save == null) return;
      _careActions?.CancelAction();
      _stationAudio?.StopWork();
      _gameplay?.ClearPendingCareExperienceForDevelopment();
      _save.currentShift = Mathf.Max(1, shift);
      _save.careShiftId = Mathf.Max(_save.careShiftId + 1, _save.currentShift);
      _save.currentState = CareStationState.Dormant;
      _save.selectedIncident = CareStationIncidentType.None;
      _save.pendingOfflineXP = 0;
      _save.pendingIncidentXP = 0;
      _save.careActionElapsed = 0f;
      _save.careActionCompleted = false;
      _save.careAction?.Reset();
      _save.currentRecipe?.Reset();
      _recipe = null;
      _save.careActionGestureReferenceScale = 0f;
      _save.careActionReferenceValid = false;
      _save.offlinePushReferenceScale = 0f;
      _save.offlinePushReferenceValid = false;
      _save.carePushReferenceScale = 0f;
      _save.carePushReferenceValid = false;
      _save.pendingReturnPhase = CareStationCollectionPhase.None;
      _save.pushAwayCompleted = false;
      _save.pushAwayCompletion = CareStationPushAwayCompletion.None;
      _save.collectedExperienceCount = 0;
      _save.collectedOfflineBottleValue = 0;
      _save.collectedCareBottleValue = 0;
      _save.careCollectionReleased = false;
      _save.offlineCollectionResolved = true;
      _save.returnedNeutralAfterOffline = false;
      _save.shiftSupplyGeneratedForShiftId = 0;
      _save.offlineRewardReason = CareStationPushAwayCompletion.None;
      _save.offlinePushAwayCompletion = CareStationPushAwayCompletion.None;
      _save.carePushAwayCompletion = CareStationPushAwayCompletion.None;
      _save.offlineReturnCompletion = CareStationReturnCompletion.None;
      _save.careReturnCompletion = CareStationReturnCompletion.None;
      _save.activeCollectionPhase = CareStationCollectionPhase.None;
      _save.shiftIncidentGenerated = false;
      _save.pendingGoldBottleCount = 0;
      _save.careShiftCompleted = false;
      _save.autoShiftEntered = false;
      _save.shiftCompleteRewardsShown = false;
      _save.endShiftConsumed = false;
      _save.nextShiftPrepared = false;
      _save.shiftStoredFullBottles = 0;
      _save.shiftStoredGoldBottles = 0;
      _save.careStepChangePending = false;
      _save.careStepWasReplaced = false;
      _save.replacedOriginalAction = CareActionType.None;
      _save.replacedWithAction = CareActionType.None;
      _save.replacementPauseReason = CareActionPauseReason.None;
      _xpBundlesSpawned = false;
      SaveNow();
      BeginSessionCollectionFlow();
#endif
    }

    public void ToggleDevelopmentCareSpeed()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _developmentCareTimeMultiplier = _developmentCareTimeMultiplier > 1f ? 1f : 10f;
      _careActions?.SetDevelopmentTimeMultiplier(_developmentCareTimeMultiplier);
#endif
    }

    public void SimulateEyesClosedForDevelopment(bool closed)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _developmentEyesClosed = closed;
      _careActions?.SimulateEyesClosed(closed);
#endif
    }

    public void AddOneGoldDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_save == null) return;
      _save.pendingPremiumShipment++;
      _view.ApplyStation(_save);
      SaveNow();
#endif
    }

    public void FreeFourStorageSlotsDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_save == null) return;
      _save.storedFullBottles = Mathf.Max(0, _save.storedFullBottles - 4);
      _save.offlineProductionPausedByFullStorage = CareStationStorageRules.Remaining(_save) <= 0;
      _view.ApplyStation(_save);
      if (State == CareStationState.WaitStorageSpace) ResumeAfterStorageSpaceAvailable();
      SaveNow();
#endif
    }

    public void ForceUpgradeCheckDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_save == null) return;
      _save.upgradeOffered = true;
      EnterUpgradeSelection();
#endif
    }

    public void TestNoAffordableUpgradeDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_save == null) return;
      var probe = new CareStationSaveData
      {
        workerLevel = _save.workerLevel,
        storageLevel = _save.storageLevel,
        cartLevel = _save.cartLevel,
        storageHours = _save.storageHours,
      };
      Debug.Log($"No-affordable-upgrade guard: {!CareStationShiftRules.CanPurchaseAnyUpgrade(probe, _upgradeConfiguration)}", this);
#endif
    }

    public void SimulateNeutralForDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _developmentNeutralUntil = Time.unscaledTime + 3f;
      if (State == CareStationState.WaitDistanceResetReturn)
        _developmentDistanceRatio = 1f;
#endif
    }

    public void SimulatePushAwayForDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (State == CareStationState.WaitDistanceResetMoveAway)
      {
        _developmentDistanceRatio = 1f - _distanceCompleteThreshold;
        return;
      }
      if (State != CareStationState.WaitOfflinePushAway && State != CareStationState.WaitCarePushAway &&
          State != CareStationState.WaitPushAwayReady && State != CareStationState.WaitPushAway) return;
      if (_save.activeCollectionPhase == CareStationCollectionPhase.None)
        _save.activeCollectionPhase = _save.careActionCompleted ? CareStationCollectionPhase.Care : CareStationCollectionPhase.Offline;
      EnsureXpBundles();
      _gameplay.SetCareCollectionArmed(true);
      if (!_gameplay.StartCareCollectionFromSkip()) return;
      var reference = CurrentPushReferenceValid ? CurrentPushReferenceScale : 1f;
      SetCurrentPushReference(FaceDistanceRatio.ToFaceScale(reference, 1f - _distanceCompleteThreshold), true);
      RecordPushAwayCompletion(CareStationPushAwayCompletion.FallbackCompleted);
      BeginCollectionState(_save.activeCollectionPhase);
#endif
    }

    public void SimulateCurrentDistanceProgressForDevelopment(float progress)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      progress = Mathf.Clamp01(progress);
      if (_careActions != null && _careActions.IsRunning &&
          _careActions.ActionType == CareActionType.FocusShift)
      {
        _careActions.SimulateCurrentDistanceProgress(progress);
        return;
      }
      if (_pushStepDirection == CareDistanceDirection.None) return;
      var directionDelta = Mathf.Lerp(_distanceDeadZone, _distanceCompleteThreshold, progress);
      _developmentDistanceRatio = _pushStepDirection == CareDistanceDirection.Closer
        ? 1f + directionDelta
        : 1f - directionDelta;
#endif
    }

    public bool StartCareActionDevelopmentTest(CareActionType type)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_careActions == null || type == CareActionType.None) return false;
      if (State == CareStationState.PromptCareAction ||
          State == CareStationState.WaitCareActionStart ||
          State == CareStationState.CareActionInProgress ||
          State == CareStationState.CareActionPaused ||
          State == CareStationState.CareActionCompleted) return false;
      if (_careActions.IsRunning)
      {
        if (_careActions.IsDevelopmentTest) FinishDevelopmentActionFreeze();
        _careActions.CancelAction();
      }
      _developmentRecipe = null;
      _developmentRecipeAdvancePending = false;
      _stationAudio?.StopWork();
      // Deliberately do not bind this isolated preview to SaveData, shift state,
      // pending bottles, upgrades, or either Push Away phase.
      var started = _careActions.StartAction(type, null, true);
      if (started) _developmentActionStartedAt = Time.unscaledTime;
      return started;
#else
      return false;
#endif
    }

    public bool StartRecipeDevelopmentTest(CareRecipeType type, int trainingIndex = -1)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_careActions == null || IsFormalCareActionState(State)) return false;
      if (_careActions.IsRunning) _careActions.CancelAction();
      var seed = 46000 + (int)type * 101 + Mathf.Max(0, trainingIndex);
      var recipe = type == CareRecipeType.Training
        ? CareRecipeGenerator.CreateRoutine(CareRoutineId.FocusFlow, 999, seed)
        : type == CareRecipeType.Full
          ? CareRecipeGenerator.CreateRoutine(CareRoutineId.FullCare, 999, seed)
          : CareRecipeGenerator.CreateFormal(type, 999, seed, Array.Empty<string>(), 0, 0, 32);
      _developmentRecipe = new CareRecipeRuntime(recipe);
      _developmentRecipeAdvancePending = false;
      _view.RestoreRecipePipeline(recipe);
      _view.ConfigureRecipe(recipe);
      _stationAudio?.StopWork();
      var started = StartDevelopmentRecipeCurrentAction();
      if (started) _developmentActionStartedAt = Time.unscaledTime;
      else
      {
        _developmentRecipe = null;
        _developmentRecipeAdvancePending = false;
      }
      return started;
#else
      return false;
#endif
    }

    public bool StartRoutineDevelopmentTest(CareRoutineId routineId)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_careActions == null || IsFormalCareActionState(State)) return false;
      if (_careActions.IsRunning) _careActions.CancelAction();
      var seed = 48000 + (int)routineId * 101;
      var recipe = CareRecipeGenerator.CreateRoutine(routineId, 999, seed);
      _developmentRecipe = new CareRecipeRuntime(recipe);
      _developmentRecipeAdvancePending = false;
      _view.RestoreRecipePipeline(recipe);
      _view.ConfigureRecipe(recipe);
      _stationAudio?.StopWork();
      var started = StartDevelopmentRecipeCurrentAction();
      if (started) _developmentActionStartedAt = Time.unscaledTime;
      else
      {
        _developmentRecipe = null;
        _developmentRecipeAdvancePending = false;
      }
      return started;
#else
      return false;
#endif
    }

    private bool StartDevelopmentRecipeCurrentAction()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_careActions == null || _developmentRecipe == null ||
          _developmentRecipe.CurrentAction == CareActionType.None) return false;
      var recipe = _developmentRecipe.Data;
      return _careActions.StartAction(
        _developmentRecipe.CurrentAction,
        null,
        true,
        recipe.closedEyeRestSeconds,
        false,
        recipe.focusCycleCount,
        recipe.guidedLapsPerDirection,
        recipe.pilotRoundsPerAxis);
#else
      return false;
#endif
    }

    public void AdvanceRecipeStepDevelopmentTest()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_developmentRecipe == null || _careActions == null || !_careActions.IsDevelopmentTest) return;
      var index = _developmentRecipe.Data.currentActionIndex;
      for (var guard = 0; guard < 48 && _developmentRecipe.Data.currentActionIndex == index; guard++)
        _careActions.CompleteCurrentStepForDevelopment();
#endif
    }

    public void ResetRecipeDevelopmentTest()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_careActions != null && _careActions.IsDevelopmentTest) _careActions.CancelAction();
      _developmentRecipe = null;
      _developmentRecipeAdvancePending = false;
      _developmentRecipeAdvancePending = false;
      FinishDevelopmentActionFreeze();
      RestoreCurrentPresentationAfterDevelopmentAction();
#endif
    }

    public void ResetTrainingProgressDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_save == null || IsFormalCareActionState(State)) return;
      _save.trainingProgress = 0;
      _save.completedTrainingActionMask = 0;
      _save.formalRecipesCreated = 0;
      _save.careRoutinesCreated = 0;
      _save.lastCompletedRoutineId = CareRoutineId.None;
      _save.recentRecipeHistory = Array.Empty<string>();
      _save.focusShiftCooldownUntilShiftId = 0;
      _save.guidedEyeCirclesCooldownUntilShiftId = 0;
      _save.currentRecipe?.Reset();
      _recipe = null;
      SaveNow();
#endif
    }

    public void ResetCareIntrosDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_save == null) return;
      _save.hasSeenFocusShiftIntro = false;
      _save.hasSeenClosedEyeRestIntro = false;
      _save.hasSeenGuidedMovementIntro = false;
      _save.hasSeenPilotEyeRoutineIntro = false;
      SaveNow();
#endif
    }

    private static bool IsFormalCareActionState(CareStationState state)
    {
      return state == CareStationState.PromptCareAction ||
             state == CareStationState.WaitCareActionStart ||
             state == CareStationState.CareActionInProgress ||
             state == CareStationState.CareActionPaused ||
             state == CareStationState.CareActionCompleted;
    }

    public void PauseCareActionDevelopmentTest()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_careActions != null && _careActions.IsDevelopmentTest) _careActions.PauseAction();
#endif
    }

    public void ResumeCareActionDevelopmentTest()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_careActions != null && _careActions.IsDevelopmentTest) _careActions.ResumeAction();
#endif
    }

    public void CompleteCareActionStepDevelopmentTest()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_careActions != null && _careActions.IsDevelopmentTest) _careActions.CompleteCurrentStepForDevelopment();
#endif
    }

    public void ResetCareActionDevelopmentTest()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_careActions != null && _careActions.IsDevelopmentTest) _careActions.CancelAction();
      _developmentRecipe = null;
      FinishDevelopmentActionFreeze();
      RestoreCurrentPresentationAfterDevelopmentAction();
#endif
    }

    public void TestCloseCueDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      // Audio preview only: never touches the action runner, recipe, save, or
      // station resources.
      CareAudioFeedbackController.EnsureExists().PlayGuidedCloseRequest();
#endif
    }

    public void TestOpenCueDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      // Audio preview only: never touches the action runner, recipe, save, or
      // station resources.
      CareAudioFeedbackController.EnsureExists().PlayRestOpen();
#endif
    }

    public void TestGuidedOpenCueDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      CareAudioFeedbackController.EnsureExists().PlayGuidedOpen();
#endif
    }

    public void TestPilotVoiceDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      CareVoiceService.EnsureExists().Speak("pilot-intro",
        "KEEP YOUR HEAD STILL. MOVE ONLY YOUR EYES.", 3.8f);
#endif
    }

    public void TestPilotCompletionDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      CareAudioFeedbackController.EnsureExists().PlayPilotCompletion();
#endif
    }

    public void TestVoiceDuckingDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      CareAudioFeedbackController.EnsureExists().StartActionAmbience(CareActionType.ClosedEyeRest);
      CareVoiceService.EnsureExists().Speak("voice-ducking-preview", "VOICE DUCKING PREVIEW.", 2.2f);
#endif
    }

    public void PreviewGuidedDirectionDevelopment(bool counterClockwise)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (!StartCareActionDevelopmentTest(CareActionType.GuidedEyeCircles)) return;
      if (!counterClockwise) return;
      _careActions.CompleteCurrentStepForDevelopment();
      _careActions.CompleteCurrentStepForDevelopment();
#endif
    }

    public void PreviewPilotAxisDevelopment(int axis)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _view?.PreviewFullscreenPilotDevelopment(axis);
#endif
    }

    public void PreviewFullscreenPilotDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _view?.PreviewFullscreenPilotDevelopment();
#endif
    }

    public void PreviewPilotToGuidedTransitionDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _view?.PreviewPilotToGuidedTransitionDevelopment();
#endif
    }

    public void ToggleStationHudDuringGuidanceDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _view?.ToggleStationHudDuringGuidanceDevelopment();
#endif
    }

    public void AdjustGuidanceWorkerSizeDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _view?.AdjustGuidanceWorkerSizeDevelopment();
#endif
    }

    public void AdjustGuidanceEyeSizeDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _view?.AdjustGuidanceEyeSizeDevelopment();
#endif
    }

    public void ToggleGuidanceSafeAreaDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _view?.ToggleGuidanceSafeAreaDevelopment();
#endif
    }

    public void CapturePilotLayoutDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _view?.CapturePilotLayoutDevelopment();
#endif
    }

    public void AdjustPilotPupilRangeDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _view?.AdjustPilotPupilRangeDevelopment();
#endif
    }

    public void AdjustPilotAxisRangeDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _view?.AdjustPilotAxisRangeDevelopment();
#endif
    }

    public void TestRestMusicDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      TestActionAmbienceDevelopment(CareActionType.ClosedEyeRest);
#endif
    }

    public void TestActionAmbienceDevelopment(CareActionType action)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (action != CareActionType.FocusShift && action != CareActionType.PilotEyeRoutine &&
          action != CareActionType.GuidedEyeCircles && action != CareActionType.ClosedEyeRest) return;
      Debug.Log($"[CareAudio] Previewing formal {CareActionLibrary.DisplayName(action)} ambience.");
      CareAudioFeedbackController.EnsureExists().StartActionAmbience(action);
#endif
    }

    public void TestBenefitVoiceDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      CareVoiceService.EnsureExists().Speak("rest-benefit",
        "LET YOUR EYES REST FROM THE SCREEN.", 3.2f);
#endif
    }

    public void TestAlmostCompleteVoiceDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      CareVoiceService.EnsureExists().Speak("rest-almost", "YOU ARE ALMOST DONE.", 2.4f);
#endif
    }

    public void StopAllCareAudioDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      CareAudioFeedbackController.EnsureExists().StopGuidedCue();
      CareAudioFeedbackController.EnsureExists().StopActionAmbience(true);
      CareVoiceService.EnsureExists().Stop();
#endif
    }

    private void FinishDevelopmentActionFreeze()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_developmentActionStartedAt < 0f) return;
      _stateStartedAt += Mathf.Max(0f, Time.unscaledTime - _developmentActionStartedAt);
      _developmentActionStartedAt = -1f;
#endif
    }

    private void RestoreCurrentPresentationAfterDevelopmentAction()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_view == null || _save == null) return;
      switch (State)
      {
        case CareStationState.WaitIncidentSelection:
        case CareStationState.PresentIncident:
          _view.ShowStationWorking();
          break;
        case CareStationState.StationWorking:
        case CareStationState.AutoShift:
          _view.ShowStationWorking();
          break;
        case CareStationState.WaitOfflinePushAway:
        case CareStationState.WaitCarePushAway:
        case CareStationState.WaitPushAwayReady:
        case CareStationState.WaitPushAway:
          _view.ShowSendXp(CurrentRemainingBottleValue, false);
          break;
        case CareStationState.ShiftComplete:
          _view.ShowShiftComplete(_save);
          break;
        default:
          _view.HideAllModals();
          break;
      }
#endif
    }

    public string DevelopmentCareActionStatus
    {
      get
      {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_careActions == null || !_careActions.IsRunning) return "NO ACTION";
        return $"{_careActions.DisplayName}  {_careActions.Stage}  {_careActions.InternalPhase}  {_careActions.Progress:P0}";
#else
        return string.Empty;
#endif
      }
    }

    public string DevelopmentRecipeStatus
    {
      get
      {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var recipe = _developmentRecipe?.Data ?? _save?.currentRecipe;
        if (recipe == null || recipe.ActionCount <= 0) return "RECIPE: NONE";
        var history = _save?.recentRecipeHistory == null ? string.Empty : string.Join(" | ", _save.recentRecipeHistory);
        return $"RECIPE: {recipe.recipeType}  {recipe.currentActionIndex}/{recipe.ActionCount}\n" +
               $"{CareRecipeGenerator.Signature(recipe.actionList)}\nHISTORY: {history}";
#else
        return string.Empty;
#endif
      }
    }

    public void ShowRecipeHistoryDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      Debug.Log(DevelopmentRecipeStatus, this);
#endif
    }

    public void SetPreScoresDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_save == null) return;
      var scores = DevelopmentScores();
      _save.preCareScores = scores;
      _researchRecorder?.RecordScores("Pre", scores);
      if (State == CareStationState.PreCareCheck) EnterStationWorking();
      else SaveNow();
#endif
    }

    public void SetPostScoresDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_save == null) return;
      var scores = DevelopmentScores();
      scores.comfort = 8;
      scores.dryness = 1;
      scores.eyeStrain = 1;
      scores.focusDifficulty = 1;
      _save.postCareScores = scores;
      _researchRecorder?.RecordScores("Post", scores);
      if (State == CareStationState.PostCareCheck) EnterCareReport();
      else SaveNow();
#endif
    }

    public void SkipPreCheckDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_save == null) return;
      var missing = new CareSubjectiveScores { skipped = true };
      _save.preCareScores = missing;
      _researchRecorder?.RecordScores("Pre", missing);
      if (State == CareStationState.PreCareCheck) EnterStationWorking();
      else SaveNow();
#endif
    }

    public void SkipPostCheckDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_save == null) return;
      var missing = new CareSubjectiveScores { skipped = true };
      _save.postCareScores = missing;
      _researchRecorder?.RecordScores("Post", missing);
      if (State == CareStationState.PostCareCheck) EnterCareReport();
      else SaveNow();
#endif
    }

    public void CompleteReportDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (State == CareStationState.CareReport) HandleCareReportDone();
#endif
    }

    public void ExportResearchDataDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      EnsureResearchSession();
      var exported = _researchRecorder != null && _researchRecorder.Persist(_save, _save != null && _save.endShiftConsumed);
      Debug.Log(exported ? "Research data exported." : "Research Mode is off; no files were written.", this);
#endif
    }

    public void OpenResearchFolderDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      EnsureResearchSession();
      if (_researchRecorder == null) return;
      Application.OpenURL("file:///" + _researchRecorder.DirectoryPath.Replace('\\', '/'));
#endif
    }

    public void ClearResearchDataDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_researchRecorder == null) return;
      if (Time.unscaledTime > _clearResearchConfirmationUntil)
      {
        _clearResearchConfirmationUntil = Time.unscaledTime + 5f;
        Debug.LogWarning("Press CLEAR RESEARCH DATA again within 5 seconds to confirm.", this);
        return;
      }
      _clearResearchConfirmationUntil = -1f;
      _researchRecorder.ClearAll();
      Debug.Log("Research data cleared.", this);
#endif
    }

    private static CareSubjectiveScores DevelopmentScores()
    {
      return new CareSubjectiveScores
      {
        comfort = 5,
        dryness = 2,
        eyeStrain = 2,
        focusDifficulty = 2,
        submitted = true,
      };
    }

    public string DevelopmentReturnDiagnostics
    {
      get
      {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var focusReturn = _careActions != null && _careActions.IsRunning &&
                          _careActions.ActionType == CareActionType.FocusShift;
        var returnPhase = _save != null ? _save.pendingReturnPhase : CareStationCollectionPhase.None;
        var activePushPhase = _save != null ? _save.activeCollectionPhase : CareStationCollectionPhase.None;
        var diagnosticPhase = returnPhase != CareStationCollectionPhase.None ? returnPhase : activePushPhase;
        var source = focusReturn
          ? "FOCUS SHIFT"
          : diagnosticPhase == CareStationCollectionPhase.Offline
            ? "OFFLINE PUSH"
            : diagnosticPhase == CareStationCollectionPhase.Care ? "CARE PUSH" : "NONE";
        var referenceValid = focusReturn
          ? _careActions.GestureReferenceValid
          : ReturnReferenceValid(diagnosticPhase);
        var referenceScale = focusReturn
          ? _careActions.GestureReferenceScale
          : ReturnReferenceScale(diagnosticPhase);
        var rawScale = focusReturn ? _careActions.RawGestureFaceScale : _rawPushFaceScale;
        var currentScale = focusReturn ? _careActions.CurrentGestureFaceScale : _currentPushFaceScale;
        var ratio = focusReturn ? _careActions.GestureDistanceRatio : _currentPushRatio;
        var stable = focusReturn ? _careActions.FocusStableSeconds : (_pushDistanceStep?.StableSeconds ?? 0f);
        var expected = focusReturn ? _careActions.ExpectedDistanceDirection : _pushStepDirection;
        var progress = focusReturn ? _careActions.DirectionProgress : (_pushDistanceStep?.Progress ?? 0f);
        var deltaPercent = focusReturn
          ? _careActions.DirectionDeltaPercent
          : expected == CareDistanceDirection.Closer
            ? (ratio - 1f) * 100f
            : (1f - ratio) * 100f;
        var distanceState = focusReturn ? _careActions.CurrentDistanceState : CurrentPushDistanceState;
        var focusStep = _careActions != null ? _careActions.FocusStep : 0;
        var focusCycle = _careActions != null ? _careActions.FocusCycle : 0;
        var focusRearmed = _careActions != null && _careActions.FocusRearmed;
        var focusHoldTarget = focusReturn ? _careActions.FocusTargetHoldSeconds : 0f;
        var focusLegElapsed = focusReturn ? _careActions.FocusLegElapsedSeconds : 0f;
        var focusMinimumLeg = focusReturn ? _careActions.FocusMinimumLegSeconds : 0f;
        var focusConfirmation = focusReturn ? _careActions.FocusConfirmationProgress : 0f;
        var focusTooClose = focusReturn && _careActions.FocusTooClose;
        var sessionBaseline = focusReturn && _careActions.GestureReferenceValid
          ? _careActions.GestureReferenceScale
          : _gameplay != null ? _gameplay.BaselineFaceScale : 0f;
        return
          $"Current State: {State}\n" +
          $"Tracking Valid: {EffectiveTracking}\n" +
          $"Session Baseline: {sessionBaseline:F6}\n" +
          $"Raw Face Scale: {rawScale:F6}\n" +
          $"Smoothed Face Scale: {currentScale:F6}\n" +
          $"Reference Scale: {(referenceValid ? referenceScale : 0f):F6}\n" +
          $"Distance Ratio: {ratio:F3}\n" +
          $"Delta Percent: {deltaPercent:+0.0;-0.0;0.0}%\n" +
          $"Expected Direction: {expected}\n" +
          $"Direction Progress: {progress:P0}\n" +
          $"Hold Time: {stable:F2} / {focusHoldTarget:F2}\n" +
          $"Minimum Leg Time: {focusLegElapsed:F2} / {focusMinimumLeg:F2}\n" +
          $"Confirmation Progress: {focusConfirmation:P0}\n" +
          $"Near Peak: {(focusReturn ? _careActions.FocusNearPeakRatio : 0f):F3}\n" +
          $"Far Peak: {(focusReturn ? _careActions.FocusFarPeakRatio : 0f):F3}\n" +
          $"Current Cycle: {focusCycle} / 6\n" +
          $"Neutral/Rearm: {focusRearmed}\n" +
          $"Detection State: {distanceState}\n" +
          $"Too Close: {focusTooClose}\n" +
          "Timing Source: UNSCALED\n" +
          $"Return Source: {source}\n" +
          $"Push Armed: {(_gameplay != null && _gameplay.IsCareCollectionArmed)}\n" +
          $"Focus Step: {focusStep}";
#else
        return string.Empty;
#endif
      }
    }

    public string DevelopmentCollectionDiagnostics
    {
      get
      {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        var phase = _save != null ? _save.activeCollectionPhase : CareStationCollectionPhase.None;
        var expectedCare = _save != null ? PendingCareBottleValue : 0;
        var arrivedCare = _save != null ? Mathf.Clamp(_save.collectedCareBottleValue, 0, expectedCare) : 0;
        var remaining = _save != null ? CurrentRemainingBottleValue : 0;
        var available = _save != null ? CareStationStorageRules.Remaining(_save) : 0;
        var inFlight = _gameplay != null ? _gameplay.PendingCollectingExperienceCount : 0;
        return
          $"Collection Phase: {phase}\n" +
          $"Expected Care Bottles: {expectedCare}\n" +
          $"Spawned Bundles: {_collectionSpawnedBundleCount}\n" +
          $"In Flight: {inFlight}\n" +
          $"Arrived Value: {arrivedCare}\n" +
          $"Remaining Value: {remaining}\n" +
          $"Available Storage: {available}\n" +
          $"Collection Paused Reason: {_collectionPausedReason}";
#else
        return string.Empty;
#endif
      }
    }

    public void DumpUiInputDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _uiInputDiagnostics?.DumpCurrentPointer();
#endif
    }

    internal string UiInputLockDescription =>
      $"{(_view != null ? _view.UiInputLockDescription : "owner=NO_VIEW")} expectedGuidance={IsGuidanceInputExpected()} state={State}";

    public void ClearStaleUiLockDevelopment()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_view == null) return;
      var legitimateGuidanceLock = IsGuidanceInputExpected();
      if (!_view.ClearStaleUiInputLock(legitimateGuidanceLock))
      {
        Debug.Log("[UI INPUT] CLEAR STALE UI LOCK skipped: a visible care guidance surface still owns input.", this);
        _uiInputDiagnostics?.DumpCurrentPointer("CLEAR STALE UI LOCK SKIPPED");
        return;
      }
      RestoreCurrentPresentation();
      _uiInputDiagnostics?.DumpCurrentPointer("CLEAR STALE UI LOCK COMPLETE");
#endif
    }

    public void ClearStationSave()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _careActions?.CancelAction();
      _stationAudio?.StopWork();
      _gameplay?.ClearPendingCareExperienceForDevelopment();
      _saveService.Delete();
      _gameplay?.ResetCareStationDistanceSession();
      InitializeStation();
#endif
    }

    private void SettleOffline(DateTime now, bool initialLoad)
    {
      var lastClaimed = _save.ReadLastClaimedUtc(now);
      var lastActive = _save.ReadLastActiveUtc(lastClaimed);
      var offlineStart = lastActive > lastClaimed ? lastActive : lastClaimed;
      var produced = CareStationOfflineCalculator.Calculate(
        offlineStart,
        now,
        _minimumOfflineMinutes,
        _save.storageHours,
        _offlineXpPerHour * CareStationShiftRules.ProductionRateMultiplier(_save),
        true);
      var validOfflineInterval = produced.CreditedDuration > TimeSpan.Zero;
      if (_save.currentState == CareStationState.AutoShift && validOfflineInterval)
        PrepareNextShift(true, false);
      var settlementId = validOfflineInterval
        ? $"{offlineStart.ToUniversalTime().Ticks}:{now.ToUniversalTime().Ticks}"
        : string.Empty;
      var settlement = validOfflineInterval
        ? _production.SettleCart(_save, produced.ExperienceMade, settlementId, _economyConfiguration)
        : default;
      var resumedProduction = CareProductionRules.AdvanceForegroundCycle(
        _save,
        0f,
        _productionConfiguration);
      if (resumedProduction.BottleStored && _save.currentState == CareStationState.ProduceBottles)
        _save.currentState = CareStationState.PostCareCheck;
      _lastOfflineResult = new CareStationOfflineResult(
        produced.CreditedDuration,
        settlement.CoinsEarned + settlement.BottlesProduced,
        produced.BuildCompleteCount,
        0);
      _save.lastOfflineStoredFullBottles = settlement.BottlesProduced;
      _save.lastOfflineStoredGoldBottles = 0;
      _save.lastOfflineWorkedSeconds = (float)produced.CreditedDuration.TotalSeconds;
      _save.offlineSummaryConsumed = !validOfflineInterval;
      _save.pendingOfflineXP = 0;
      _save.collectedOfflineBottleValue = 0;
      _save.offlineCollectionResolved = true;
      _save.returnedNeutralAfterOffline = true;
      _save.stationConstructionState += produced.BuildCompleteCount;
      if (validOfflineInterval) _save.StampClaimed(now);
      if (!initialLoad || validOfflineInterval || _lastOfflineResult.HasAnything) SaveNow();
    }

    private void PresentCurrentIncident()
    {
      if (!_save.preCareScores.IsResolved)
      {
        EnterPreCareCheck();
        return;
      }
      if (_save.inspectionActive)
      {
        EnterInspectionPreparing(true);
        return;
      }
      _save.selectedIncident = CareStationIncidentType.None;
      _save.shiftIncidentGenerated = false;
      EnsureCurrentRecipe();
      _save.careActionCompleted = _save.currentRecipe.recipeCompleted;
      if (!_save.currentRecipe.recipeCompleted) _save.careAction?.Reset();
      _save.careActionGestureReferenceScale = 0f;
      _save.careActionReferenceValid = false;
      _save.pushAwayCompleted = false;
      _save.pushAwayCompletion = CareStationPushAwayCompletion.None;
      _save.carePushAwayCompletion = CareStationPushAwayCompletion.None;
      _save.careReturnCompletion = CareStationReturnCompletion.None;
      _save.activeCollectionPhase = CareStationCollectionPhase.None;
      _xpBundlesSpawned = false;
      if (_save.currentRecipe.recipeCompleted)
      {
        EnterCareActionCompleted();
        return;
      }
      SetState(CareStationState.PromptCareAction);
      _view.SetCrewState(CareCrewState.Rest);
      if (!_save.currentRecipe.routineIntroCompleted)
      {
        _save.currentRecipe.routineIntroElapsedSeconds = 0f;
        _view.ShowCareRoutineIntro(_save.currentRecipe);
        CareAudioFeedbackController.EnsureExists().PlayStepComplete();
        SaveNow();
        return;
      }
      StartStationCareAction(_recipe.CurrentAction, false);
    }

    private void EnsureResearchSession()
    {
      if (_save == null) return;
      if (_researchRecorder == null) _researchRecorder = new CareResearchSessionRecorder(_researchMode);
      _researchRecorder.BeginOrResume(_save);
    }

    private void EnterPreCareCheck()
    {
      if (_save == null) return;
      EnsureResearchSession();
      if (_save.preCareScores.IsResolved)
      {
        EnterStationWorking();
        return;
      }
      _gameplay.SetCareActionActive(true);
      SetState(CareStationState.PreCareCheck);
      _view.ShowSubjectiveCheck(false, _save.preCareScores);
      _view.SetCrewState(CareCrewState.Rest);
      SaveNow();
    }

    private void EnterPostCareCheck()
    {
      if (_save == null) return;
      EnsureResearchSession();
      if (_save.postCareScores.IsResolved)
      {
        EnterCareReport();
        return;
      }
      _gameplay.SetCareActionActive(true);
      SetState(CareStationState.PostCareCheck);
      _view.ShowSubjectiveCheck(true, _save.postCareScores);
      _view.SetCrewState(CareCrewState.Rest);
      SaveNow();
    }

    private void HandleSubjectiveScoresChanged(bool post, CareSubjectiveScores scores)
    {
      if (_save == null || scores == null) return;
      if ((!post && State != CareStationState.PreCareCheck) ||
          (post && State != CareStationState.PostCareCheck)) return;
      if (post) _save.postCareScores = scores.Clone();
      else _save.preCareScores = scores.Clone();
      SaveNow();
    }

    private void HandleSubjectiveScoresSubmitted(bool post, CareSubjectiveScores scores)
    {
      if (_save == null || scores == null || !scores.HasAllResponses) return;
      if ((!post && State != CareStationState.PreCareCheck) ||
          (post && State != CareStationState.PostCareCheck)) return;
      var submitted = scores.Clone();
      submitted.submitted = true;
      submitted.skipped = false;
      if (post) _save.postCareScores = submitted;
      else _save.preCareScores = submitted;
      _researchRecorder?.RecordScores(post ? "Post" : "Pre", submitted);
      if (post) EnterCareReport();
      else EnterStationWorking();
    }

    private void HandleSubjectiveScoresSkipped(bool post)
    {
      if (_save == null) return;
      if ((!post && State != CareStationState.PreCareCheck) ||
          (post && State != CareStationState.PostCareCheck)) return;
      var missing = new CareSubjectiveScores { skipped = true };
      if (post) _save.postCareScores = missing;
      else _save.preCareScores = missing;
      _researchRecorder?.RecordScores(post ? "Post" : "Pre", missing);
      if (post) EnterCareReport();
      else EnterStationWorking();
    }

    private void EnterCareReport()
    {
      if (_save == null) return;
      if (_save.careReportConsumed)
      {
        ContinueAfterCareReport();
        return;
      }
      _save.careReportShown = true;
      SetState(CareStationState.CareReport);
      _view.ShowCareReport(_save);
      _view.SetCrewState(CareCrewState.Rest);
      _researchRecorder?.Persist(_save, false);
      SaveNow();
    }

    private void HandleCareReportDone()
    {
      if (_save == null || State != CareStationState.CareReport || _save.careReportConsumed) return;
      _save.careReportConsumed = true;
      ContinueAfterCareReport();
    }

    private void EnterStationWorking()
    {
      if (!_save.preCareScores.IsResolved)
      {
        EnterPreCareCheck();
        return;
      }
      if (!CareStationStateRules.CanPresentIncident(_save.offlineCollectionResolved, _save.returnedNeutralAfterOffline))
      {
        EnterWaitReturnToNeutral();
        return;
      }
      SetState(CareStationState.StationWorking);
      _view.ShowStationWorking();
      SaveNow();
    }

    private void HandleWelcomeContinue()
    {
      if (State != CareStationState.WelcomeBack) return;
      _save.offlineSummaryConsumed = true;
      ResumeSavedFlowAfterWelcome();
    }

    private void ResumeSavedFlowAfterWelcome()
    {
      if (_resumeStateBeforeWelcome == CareStationState.Dormant)
        BeginSessionCollectionFlow();
      else
        ResumeSavedFlowWithoutWelcome();
    }

    private void ResumeSavedFlowWithoutWelcome()
    {
      var saved = _resumeStateBeforeWelcome;
      _lastOfflineResult = default;
      _save.currentState = saved;
      ResumeSavedFlow();
    }

    private void HandleIncidentSelected()
    {
      // Legacy callback funnels into the same direct Recipe entry.
      PresentCurrentIncident();
    }

    private void HandleStartCareSelected()
    {
      if (_save == null) return;
      if (State == CareStationState.WaitIncidentSelection || State == CareStationState.PresentIncident)
        SetState(CareStationState.StationWorking);
      if (State == CareStationState.WaitStorageSpace)
      {
        if (HasPendingStorageReward(_save)) return;
        _save.activeCollectionPhase = CareStationCollectionPhase.None;
        _save.pendingReturnPhase = CareStationCollectionPhase.None;
        _save.offlineProductionPausedByFullStorage = CareStationStorageRules.Remaining(_save) <= 0;
        EnterStationWorking();
      }
      if (State != CareStationState.StationWorking) return;
      if (!_save.preCareScores.IsResolved)
      {
        EnterPreCareCheck();
        return;
      }
      if (!CareStationStateRules.CanPresentIncident(_save.offlineCollectionResolved, _save.returnedNeutralAfterOffline))
        return;
      PresentCurrentIncident();
    }

    private void HandleChangeStepRequested()
    {
      if (_save == null || _careActions == null || _careActions.IsDevelopmentTest ||
          (State != CareStationState.WaitCareActionStart &&
           State != CareStationState.CareActionInProgress &&
           State != CareStationState.CareActionPaused)) return;
      EnsureCurrentRecipe();
      var original = _recipe?.CurrentAction ?? CareActionType.None;
      if (original == CareActionType.None || original == CareActionType.ClosedEyeRest ||
          _save.careStepChangePending || !_careActions.ChangeStepAllowed) return;

      var reason = _careActions.PauseReason;
      _save.careStepChangePending = true;
      _save.replacedOriginalAction = original;
      _save.replacedWithAction = CareActionType.ClosedEyeRest;
      _save.replacementPauseReason = reason;
      CareStationEventLog.Append(
        _save,
        CareStationEventType.CareStepChangeRequested,
        DateTime.UtcNow,
        original,
        CareActionType.ClosedEyeRest,
        reason);
      _researchRecorder?.RecordStepChangeRequested(original, reason);
      _careActions.PauseAction();
      _save.careAction = _careActions.SaveData;
      SetState(CareStationState.CareActionPaused);
      _view.ShowCareStepChangeConfirmation();
      SaveNow();
    }

    private void HandleKeepStepSelected()
    {
      if (_save == null || !_save.careStepChangePending) return;
      _save.careStepChangePending = false;
      _save.replacedOriginalAction = CareActionType.None;
      _save.replacedWithAction = CareActionType.None;
      _save.replacementPauseReason = CareActionPauseReason.None;
      _view.HideCareStepChangeConfirmation();
      _careActions?.ResumeAction();
      SetState(StateForActionStage(_careActions != null ? _careActions.Stage : CareActionStage.WaitingForStart));
      SaveNow();
    }

    private void HandleUseRestSelected()
    {
      if (_save == null || !_save.careStepChangePending) return;
      EnsureCurrentRecipe();
      if (_recipe == null) return;
      var original = _save.replacedOriginalAction;
      var reason = _save.replacementPauseReason;
      var replacement = _recipe.ReplaceCurrentWithClosedEyeRest();
      if (!replacement.Accepted) return;

      _save.careStepChangePending = false;
      _save.careStepWasReplaced = true;
      _save.replacedOriginalAction = original;
      _save.replacedWithAction = CareActionType.ClosedEyeRest;
      _save.replacementPauseReason = reason;
      CareStationEventLog.Append(
        _save,
        CareStationEventType.CareStepReplaced,
        DateTime.UtcNow,
        original,
        CareActionType.ClosedEyeRest,
        reason);
      _researchRecorder?.RecordStepReplacement(original, CareActionType.ClosedEyeRest, reason);
      _view.HideCareStepChangeConfirmation();
      _view.ConfigureRecipe(_save.currentRecipe);
      _careActions?.CancelAction();
      _save.careAction?.Reset();

      if (replacement.SatisfiedByCompletedRest)
      {
        CareEconomyRules.TryGrantAllCompletedRecipeSteps(_save, out _pendingStepFeedbackEnergy);
        _save.careActionCompleted = replacement.RecipeCompleted;
        if (replacement.RecipeCompleted)
        {
          CareRecipeGenerator.ApplyCompletionToProgress(_save, _save.currentRecipe);
          if (_recipe.TryConsumeCompletionSignal()) RecipeCompleted?.Invoke();
        }
        EnterCareActionCompleted();
      }
      else
      {
        StartStationCareAction(CareActionType.ClosedEyeRest, false);
      }
      SaveNow();
    }

    private void RestoreCareAction()
    {
      _gameplay.SetCareActionActive(true);
      EnsureCurrentRecipe();
      var type = _recipe != null && _recipe.CurrentAction != CareActionType.None
        ? _recipe.CurrentAction
        : _save.careAction != null && _save.careAction.actionType != CareActionType.None
          ? _save.careAction.actionType
          : CareActionType.None;
      StartStationCareAction(type, true);
    }

    private bool StartStationCareAction(CareActionType type, bool restore)
    {
      if (_careActions == null || type == CareActionType.None) return false;
      if (_careActions.IsRunning) _careActions.CancelAction();
      var canRestore = restore && _save.careAction != null &&
                       _save.careAction.actionType == type &&
                       _save.careAction.internalPhase != CareActionInternalPhase.None;
      var restored = canRestore ? _save.careAction : null;
      if (!canRestore && _save.currentRecipe?.deferredActionSnapshot != null &&
          _save.currentRecipe.deferredActionSnapshot.actionType == type &&
          _save.currentRecipe.deferredActionSnapshot.internalPhase != CareActionInternalPhase.None)
      {
        restored = _save.currentRecipe.deferredActionSnapshot;
        canRestore = true;
        _save.currentRecipe.deferredActionSnapshot = new CareActionSaveData();
      }
      if (type == CareActionType.FocusShift && restored != null)
      {
        restored.gestureReferenceScale = _save.careActionGestureReferenceScale;
        restored.gestureReferenceValid = _save.careActionReferenceValid;
      }
      var recipeParameters = _save.currentRecipe;
      var restSeconds = type == CareActionType.ClosedEyeRest && recipeParameters != null
        ? recipeParameters.closedEyeRestSeconds
        : 0f;
      var showIntro = !canRestore && !HasSeenCareActionIntro(type);
      if (!_careActions.StartAction(
            type,
            restored,
            false,
            restSeconds,
            showIntro,
            recipeParameters?.focusCycleCount ?? 0,
            recipeParameters?.guidedLapsPerDirection ?? 0,
            recipeParameters?.pilotRoundsPerAxis ?? 0)) return false;
      var guidedBoundToPilot = type == CareActionType.GuidedEyeCircles &&
                               _save.currentRecipe != null &&
                               _save.currentRecipe.currentActionIndex > 0 &&
                               _save.currentRecipe.actionList != null &&
                               _save.currentRecipe.currentActionIndex < _save.currentRecipe.actionList.Length &&
                               _save.currentRecipe.actionList[_save.currentRecipe.currentActionIndex - 1] ==
                               CareActionType.PilotEyeRoutine;
      _careActions.SetChangeStepAllowed(!guidedBoundToPilot);
      if (showIntro) MarkCareActionIntroSeen(type);
      _save.careAction = _careActions.SaveData;
      SyncCareActionReferenceToSave();
      _save.careActionElapsed = _save.careAction.elapsedSeconds;
      SetState(StateForActionStage(_careActions.Stage));
      _view.ConfigureRecipe(_save.currentRecipe);
      _view.RestoreRecipePipeline(_save.currentRecipe);
      if (_save.inspectionActive) _view.ConfigureInspection(_save);
      _stationAudio.StartWork(CurrentIncident);
      if (_save.careStepChangePending)
      {
        _careActions.PauseAction();
        _save.careAction = _careActions.SaveData;
        SetState(CareStationState.CareActionPaused);
        _view.ShowCareStepChangeConfirmation();
      }
      SaveNow();
      return true;
    }

    private void UpdateUnifiedCareAction()
    {
      if (_careActions == null || _save == null) return;
      if (!_careActions.IsRunning && _careActions.Stage != CareActionStage.Completed) return;
      _save.careAction = _careActions.SaveData;
      SyncCareActionReferenceToSave();
      _save.careActionElapsed = _save.careAction != null ? _save.careAction.elapsedSeconds : 0f;
      var stationState = StateForActionStage(_careActions.Stage);
      if (stationState != State && stationState != CareStationState.CareActionCompleted) SetState(stationState);
      if (_careActions.Stage == CareActionStage.Active) _stationAudio.StartWork(CurrentIncident);
      else _stationAudio.StopWork();
      if (Time.unscaledTime >= _nextActionSaveAt)
      {
        _nextActionSaveAt = Time.unscaledTime + 2f;
        SaveNow();
      }
    }

    private void HandleUnifiedCareActionCompleted(CareActionType type)
    {
      if (_careActions == null) return;
      if (_careActions.IsDevelopmentTest)
      {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_developmentRecipe != null)
        {
          var developmentResult = _developmentRecipe.CompleteCurrentAction(type);
          if (!developmentResult.Accepted) return;
          _view.PlayRecipePipelineStep(developmentResult.CompletedStepIndex, _developmentRecipe.Data.ActionCount);
          _view.ConfigureRecipe(_developmentRecipe.Data);
          if (!developmentResult.RecipeCompleted)
          {
            _developmentRecipeAdvancePending = true;
            _developmentRecipeAdvanceAt = Time.unscaledTime + _recipeStepFeedbackSeconds;
            return;
          }
          _developmentRecipe = null;
          _developmentRecipeAdvancePending = false;
        }
        FinishDevelopmentActionFreeze();
        RestoreCurrentPresentationAfterDevelopmentAction();
#endif
        return;
      }
      if (State != CareStationState.WaitCareActionStart &&
          State != CareStationState.CareActionInProgress &&
          State != CareStationState.CareActionPaused) return;
      _save.careAction = _careActions.SaveData;
      SyncCareActionReferenceToSave();
      _save.careActionElapsed = RequiredCareSeconds;
      EnsureCurrentRecipe();
      var result = _recipe?.CompleteCurrentAction(type) ?? default;
      if (!result.Accepted) return;
      CareEconomyRules.TryGrantCompletedRecipeStep(_save, result.CompletedStepIndex, out _pendingStepFeedbackEnergy);
      _careActions.PlayRoutineStepRewardHaptic();
      if (_save.inspectionActive)
      {
        _save.inspectionCurrentCheck = _save.currentRecipe.currentActionIndex;
        _save.inspectionCompletedMask = CareStationInspectionRules.CompletedCheckMask(
          result.CompletedStepIndex,
          result.RecipeCompleted);
        _view.ConfigureInspection(_save);
      }
      if (_save.careAction != null &&
          _save.careAction.completionSource == CareActionCompletionSource.DeveloperSkipped)
        _save.currentRecipe.developerSkippedActionMask |= 1 << result.CompletedStepIndex;
      if (type == CareActionType.FocusShift)
        _save.sessionFocusShiftCompletions++;
      _save.careActionCompleted = result.RecipeCompleted;
      if (!_save.inspectionActive)
        _view.PlayRecipePipelineStep(result.CompletedStepIndex, _save.currentRecipe.ActionCount, result.ActionType);
      _view.ConfigureRecipe(_save.currentRecipe);
      if (_save.inspectionActive) _view.ConfigureInspection(_save);
      RecipeStepCompleted?.Invoke(result.CompletedStepIndex, result.ActionType);
      if (result.RecipeCompleted)
      {
        if (!_save.inspectionActive)
          CareRecipeGenerator.ApplyCompletionToProgress(_save, _save.currentRecipe);
        if (_recipe.TryConsumeCompletionSignal()) RecipeCompleted?.Invoke();
      }
      // Persist action completion and its planned-slot reward as one state
      // transition before any presentation or navigation can interrupt it.
      SaveNow();
      EnterCareActionCompleted();
    }

    private void SyncCareActionReferenceToSave()
    {
      if (_save == null || _save.careAction == null) return;
      _save.careActionGestureReferenceScale = _save.careAction.gestureReferenceScale;
      _save.careActionReferenceValid = _save.careAction.gestureReferenceValid;
    }

    private bool HasSeenCareActionIntro(CareActionType type)
    {
      if (_save == null) return false;
      switch (type)
      {
        case CareActionType.FocusShift: return _save.hasSeenFocusShiftIntro;
        case CareActionType.ClosedEyeRest: return _save.hasSeenClosedEyeRestIntro;
        case CareActionType.GuidedEyeCircles: return _save.hasSeenGuidedMovementIntro;
        case CareActionType.PilotEyeRoutine: return _save.hasSeenPilotEyeRoutineIntro;
        default: return true;
      }
    }

    private void MarkCareActionIntroSeen(CareActionType type)
    {
      if (_save == null) return;
      switch (type)
      {
        case CareActionType.FocusShift: _save.hasSeenFocusShiftIntro = true; break;
        case CareActionType.ClosedEyeRest: _save.hasSeenClosedEyeRestIntro = true; break;
        case CareActionType.GuidedEyeCircles: _save.hasSeenGuidedMovementIntro = true; break;
        case CareActionType.PilotEyeRoutine: _save.hasSeenPilotEyeRoutineIntro = true; break;
      }
    }

    private static CareActionType ActionForIncident(CareStationIncidentType incident)
    {
      return incident == CareStationIncidentType.DrySpot
        ? CareActionType.ClosedEyeRest
        : CareActionType.FocusShift;
    }

    private void RestoreRecipeRuntime()
    {
      if (_save == null) return;
      if (_save.currentRecipe == null) _save.currentRecipe = new CareRecipeSaveData();
      CareRecipeGenerator.SanitizeRecipe(_save.currentRecipe);
      _recipe = _save.currentRecipe.ActionCount > 0
        ? new CareRecipeRuntime(_save.currentRecipe)
        : null;
    }

    private void EnsureCurrentRecipe()
    {
      if (_save == null) return;
      if (_save.currentRecipe == null) _save.currentRecipe = new CareRecipeSaveData();
      if (_save.inspectionActive)
      {
        if (_save.currentRecipe.createdShiftId != _save.careShiftId ||
            string.IsNullOrEmpty(_save.currentRecipe.recipeId) ||
            !_save.currentRecipe.recipeId.StartsWith("station_inspection_", StringComparison.Ordinal) ||
            !IsPilotFlowInspection(_save.currentRecipe))
          _save.currentRecipe = CareStationInspectionRules.CreateRecipe(_save.careShiftId);
        CareRecipeGenerator.SanitizeRecipe(_save.currentRecipe);
        if (_recipe == null || !ReferenceEquals(_recipe.Data, _save.currentRecipe))
          _recipe = new CareRecipeRuntime(_save.currentRecipe);
        _save.inspectionCurrentCheck = _save.currentRecipe.currentActionIndex;
        _view?.ConfigureInspection(_save);
        SaveNow();
        return;
      }
      CareRecipeGenerator.SanitizeRecipe(_save.currentRecipe);
      if (_save.currentRecipe.ActionCount > 0 &&
          _save.currentRecipe.createdShiftId == _save.careShiftId)
      {
        if (_recipe == null || !ReferenceEquals(_recipe.Data, _save.currentRecipe))
          _recipe = new CareRecipeRuntime(_save.currentRecipe);
        return;
      }

      var seed = unchecked((_save.careShiftId * 73856093) ^
                           (_save.currentShift * 19349663) ^
                           ((_save.careRoutinesCreated + 1) * 83492791));
      var settings = new CareRecipeGenerationSettings(
        _singleRecipeWeight,
        _doubleRecipeWeight,
        _tripleRecipeWeight,
        _recipeGenerationMaximumAttempts);
      _save.currentRecipe = CareRecipeGenerator.CreateForShift(_save, seed, settings);
      _recipe = new CareRecipeRuntime(_save.currentRecipe);
      _researchRecorder?.RecordRecipe(_save);
      _save.careActionCompleted = false;
      _save.careAction?.Reset();
      _view?.ConfigureRecipe(_save.currentRecipe);
      _view?.RestoreRecipePipeline(_save.currentRecipe);
      // Recipe identity and action order are persisted at creation time so a
      // reload, foreground transition, or tracking recovery cannot reroll it.
      SaveNow();
    }

    private static bool IsPilotFlowInspection(CareRecipeSaveData recipe)
    {
      return recipe != null && recipe.ActionCount == 3 && recipe.actionList != null &&
             recipe.actionList[0] == CareActionType.PilotEyeRoutine &&
             recipe.actionList[1] == CareActionType.GuidedEyeCircles &&
             recipe.actionList[2] == CareActionType.ClosedEyeRest;
    }

    private static CareStationState StateForActionStage(CareActionStage stage)
    {
      switch (stage)
      {
        case CareActionStage.Active: return CareStationState.CareActionInProgress;
        case CareActionStage.Paused: return CareStationState.CareActionPaused;
        case CareActionStage.Completed: return CareStationState.CareActionCompleted;
        default: return CareStationState.WaitCareActionStart;
      }
    }

    private void EnterCareActionCompleted()
    {
      _stationAudio.StopWork();
      EnsureCurrentRecipe();
      _save.careActionCompleted = _save.currentRecipe != null && _save.currentRecipe.recipeCompleted;
      if (_save.careActionCompleted)
      {
        if (!_save.inspectionActive)
          CareRecipeGenerator.ApplyCompletionToProgress(_save, _save.currentRecipe);
        CareEconomyRules.TryGrantRecipeCareEnergy(_save, _economyConfiguration, out var reconciled);
        _pendingStepFeedbackEnergy += reconciled;
        if (_recipe.TryConsumeCompletionSignal()) RecipeCompleted?.Invoke();
      }
      SetState(CareStationState.CareActionCompleted);
      _view.ConfigureRecipe(_save.currentRecipe);
      _view.RestoreRecipePipeline(_save.currentRecipe);
      var completedIndex = Mathf.Clamp(_save.currentRecipe.currentActionIndex - 1, 0,
        Mathf.Max(0, _save.currentRecipe.ActionCount - 1));
      var completedType = _save.currentRecipe.ActionCount > 0
        ? _save.currentRecipe.actionList[completedIndex]
        : CareActionType.None;
      _view.ShowRecipeStepFeedback(_save.currentRecipe, completedType, _pendingStepFeedbackEnergy);
      _pendingStepFeedbackEnergy = 0;
      if (_save.currentRecipe.recipeCompleted && !_save.currentRecipe.completionFeedbackPlayed)
      {
        _save.currentRecipe.completionFeedbackPlayed = true;
        CareAudioFeedbackController.EnsureExists().PlayCareComplete();
      }
      SaveNow();
    }

    private void UpdateCareActionCompleted(float delta)
    {
      var feedbackSeconds = _save.currentRecipe != null && _save.currentRecipe.recipeCompleted
        ? _recipeCompletionFeedbackSeconds
        : _recipeStepFeedbackSeconds;
      if (StateElapsed < feedbackSeconds) return;
      EnsureCurrentRecipe();
      if (_recipe != null && !_recipe.Data.recipeCompleted && _recipe.CurrentAction != CareActionType.None)
      {
        _save.careActionElapsed = 0f;
        _save.careAction?.Reset();
        _save.careActionGestureReferenceScale = 0f;
        _save.careActionReferenceValid = false;
        StartStationCareAction(_recipe.CurrentAction, false);
        return;
      }
      EnterRepairReveal();
    }

    private void EnterInspectionPreparing(bool createIfNeeded)
    {
      _save.inspectionActive = true;
      _save.inspectionTriggered = true;
      if (createIfNeeded || _save.currentRecipe == null || _save.currentRecipe.ActionCount <= 0)
        _save.currentRecipe = CareStationInspectionRules.CreateRecipe(_save.careShiftId);
      _recipe = new CareRecipeRuntime(_save.currentRecipe);
      _save.inspectionCurrentCheck = _save.currentRecipe.currentActionIndex;
      SetState(CareStationState.InspectionPreparing);
      _gameplay.SetCareActionActive(true);
      _view.ShowInspectionIntro(_save);
      _view.SetCrewState(CareCrewState.Rest);
      SaveNow();
    }

    private void StartInspectionCurrentAction(bool restore)
    {
      EnsureCurrentRecipe();
      if (_recipe == null || _recipe.CurrentAction == CareActionType.None)
      {
        if (_save.currentRecipe != null && _save.currentRecipe.recipeCompleted)
          EnterInspectionPassed(true);
        else
          EnterInspectionPreparing(true);
        return;
      }
      StartStationCareAction(_recipe.CurrentAction, restore);
      _view.ConfigureInspection(_save);
    }

    private void EnterInspectionPassed(bool produceReward)
    {
      if (_save?.currentRecipe == null || !_save.currentRecipe.recipeCompleted)
      {
        EnterInspectionPreparing(true);
        return;
      }
      _stationAudio.StopWork();
      _gameplay.SetCareActionActive(true);
      _save.inspectionActive = true;
      _save.inspectionTriggered = true;
      _save.inspectionCompletedMask = CareStationInspectionRules.AllChecks;
      _save.inspectionCurrentCheck = 4;
      _save.stationLevel = 2;
      var conveyorUnlocked = CareProductionTransportRules.TryConsumeBasicConveyorUnlock(_save);
      _save.careActionCompleted = true;
      CareEconomyRules.TryGrantRecipeCareEnergy(_save, _economyConfiguration, out _);
      _save.inspectionRewardProduced = true;
      SetState(CareStationState.InspectionPassed);
      _view.ShowInspectionPassed(_save);
      if (conveyorUnlocked) _view.ShowTransportUpgradeUnlocked();
      _view.SetCrewState(CareCrewState.Cheer);
      CareAudioFeedbackController.EnsureExists().PlayStepComplete();
      SaveNow();
    }

    private void EnterRepairReveal()
    {
      EnsureCurrentRecipe();
      if (_recipe == null || !_recipe.Data.recipeCompleted) return;
      if (!_recipe.Data.completionConsumed) _recipe.TryConsumeForProduction();
      CareEconomyRules.TryGrantRecipeCareEnergy(_save, _economyConfiguration, out _);
      if (_save.inspectionActive)
      {
        EnterInspectionPassed(true);
        return;
      }
      _gameplay.SetCareActionActive(true);
      SetState(CareStationState.RepairReveal);
      _view.ShowRepairReveal();
      _view.SetPendingXp(PendingCareBottleValue, 0);
      _view.SetCrewState(CareCrewState.Cheer);
      CareAudioFeedbackController.EnsureExists().PlayStepComplete();
      SaveNow();
    }

    private void EnterProduceBottles()
    {
      var recipeId = _save.currentRecipe?.recipeId ?? string.Empty;
      if (_save.productionStage == CareProductionStage.None &&
          !CareProductionRules.TryBeginForegroundCycle(_save, recipeId))
      {
        // Full Storage or an already represented Recipe never blocks reports.
        // Unspent Care Energy remains available to a later Auto Shift.
        _save.offlineProductionPausedByFullStorage =
          _save.careEnergy > 0 && CareStationStorageRules.RemainingForAutomaticOfflineSettlement(_save) <= 0;
        EnterPostCareCheck();
        return;
      }
      ResumeProductionLine();
      SaveNow();
    }

    private void ResumeProductionLine()
    {
      if (_save == null || _save.productionStage == CareProductionStage.None)
      {
        EnterPostCareCheck();
        return;
      }
      SetState(CareStationState.ProduceBottles);
      _gameplay.SetCareCollectionArmed(false);
      _gameplay.SetCareActionActive(true);
      _view.ShowProductionStage(
        _save.productionStage,
        CareProductionRules.StageProgress(_save, _productionConfiguration),
        _save);
      _view.SetCrewState(CareCrewState.Work);
    }

    private void UpdateProductionLine(float unscaledDeltaSeconds)
    {
      if (_save == null || _save.productionStage == CareProductionStage.None)
      {
        EnterPostCareCheck();
        return;
      }
      var result = CareProductionRules.AdvanceForegroundCycle(
        _save,
        unscaledDeltaSeconds,
        _productionConfiguration);
      _view.ShowProductionStage(
        _save.productionStage,
        CareProductionRules.StageProgress(_save, _productionConfiguration),
        _save);
      if (result.BottleStored)
      {
        _view.ApplyStation(_save);
        SaveNow();
        EnterPostCareCheck();
        return;
      }
      if (result.WaitingForStorage)
      {
        SaveNow();
        EnterPostCareCheck();
        return;
      }
      if (result.StageChanged || Time.unscaledTime >= _nextProductionSaveAt)
      {
        _nextProductionSaveAt = Time.unscaledTime + 1f;
        SaveNow();
      }
    }

    private void BeginSessionCollectionFlow()
    {
      if (_save.careShiftCompleted || _save.endShiftConsumed ||
          _save.currentState == CareStationState.AutoShift ||
          _save.currentState == CareStationState.ShiftComplete)
      {
        if (_save.currentState == CareStationState.ShiftComplete) EnterShiftCompletePresentation();
        else EnterAutoShift();
        return;
      }
      EnsureResearchSession();
      _save.pendingOfflineXP = 0;
      _save.collectedOfflineBottleValue = 0;
      _save.offlineCollectionResolved = true;
      _save.returnedNeutralAfterOffline = true;
      EnterStationWorking();
    }

    private bool IsDistanceResetState =>
      State == CareStationState.WaitDistanceResetMoveAway ||
      State == CareStationState.WaitDistanceResetReturn;

    private void BeginDistanceReset()
    {
      _save.activeCollectionPhase = CareStationCollectionPhase.None;
      _save.offlineCollectionResolved = true;
      _save.returnedNeutralAfterOffline = false;
      _gameplay.SetCareCollectionArmed(false);
      _gameplay.SetCareActionActive(false);
      var returning = _save.distanceResetAwayCompleted &&
                      CareDistanceReferenceSampler.IsValidScale(_save.distanceResetAwayScale);
      SetState(returning
        ? CareStationState.WaitDistanceResetReturn
        : CareStationState.WaitDistanceResetMoveAway);
      PreparePushReference(CareStationCollectionPhase.None);
      if (returning)
      {
        BeginPushDistanceStep(CareDistanceDirection.Closer);
      }
      else
      {
        BeginPushDistanceStep(CareDistanceDirection.Away);
      }
      _view.HideAllModals();
      SaveNow();
    }

    private void RestoreDistanceReset()
    {
      BeginDistanceReset();
    }

    private void UpdateDistanceReset(float delta)
    {
      if (!_save.distanceResetReferenceValid)
      {
        if (!CapturePushReference(delta))
        {
          _view.ShowDistanceCollection(
            0,
            CareDistanceDirection.Away,
            0f,
            "SENSOR UNAVAILABLE",
            false);
          return;
        }
        SaveNow();
      }

      var direction = State == CareStationState.WaitDistanceResetReturn
        ? CareDistanceDirection.Closer
        : CareDistanceDirection.Away;
      EnsurePushDistanceStep(direction);
      if (!TryUpdatePushRatio(delta, out _, out var sampleDelta, out var sampleFresh))
      {
        _pushDistanceStep?.FreezeForTrackingLoss();
        _view.ShowDistanceCollection(
          0,
          direction,
          _pushDistanceStep?.Progress ?? 0f,
          EffectiveTracking ? "SENSOR UNAVAILABLE" : "TRACKING LOST",
          false,
          direction == CareDistanceDirection.Closer ? "RETURN" : null);
        return;
      }

      var stepReference = direction == CareDistanceDirection.Closer
        ? _save.distanceResetAwayScale
        : _save.distanceResetReferenceScale;
      if (!CareDistanceReferenceSampler.IsValidScale(stepReference)) return;
      var complete = Time.unscaledTime - _distanceStepOpenedAt >= _distanceStepTransitionSeconds &&
                     _pushDistanceStep.Advance(
                       _smoothedPushFaceScale,
                       stepReference,
                       sampleDelta,
                       EffectiveTracking,
                       sampleFresh);
      _view.ShowDistanceCollection(
        0,
        direction,
        _pushDistanceStep.Progress,
        CurrentPushDistanceState,
        false,
        direction == CareDistanceDirection.Closer ? "RETURN" : null);
      if (!complete) return;

      if (direction == CareDistanceDirection.Away)
      {
        _save.distanceResetAwayScale = _smoothedPushFaceScale;
        _save.distanceResetAwayCompleted = true;
        SetState(CareStationState.WaitDistanceResetReturn);
        BeginPushDistanceStep(CareDistanceDirection.Closer);
        SaveNow();
        return;
      }

      _save.distanceResetCompleted = true;
      _save.returnedNeutralAfterOffline = true;
      _pushDistanceStep = null;
      _pushStepDirection = CareDistanceDirection.None;
      _view.HideAllModals();
      _gameplay.SetCareActionActive(true);
      EnterStationWorking();
    }

    private void BeginOfflineBottleCollection()
    {
      _save.activeCollectionPhase = CareStationCollectionPhase.Offline;
      _save.offlineCollectionResolved = false;
      _save.returnedNeutralAfterOffline = false;
      _save.offlinePushAwayCompletion = CareStationPushAwayCompletion.None;
      ResetCollectionRuntimeTracking();
      if (RemainingOfflineBottleValue > 0 && CareStationStorageRules.Remaining(_save) <= 0)
      {
        EnterStorageFullGate(CareStationCollectionPhase.Offline);
        return;
      }
      SetState(CareStationState.PresentOfflineBottles);
      _view.ShowOfflineBottles(RemainingOfflineBottleValue, IsCurrentShiftSupply);
      SaveNow();
    }

    private void BeginCareBottleCollection()
    {
      _save.activeCollectionPhase = CareStationCollectionPhase.Care;
      ResetCollectionRuntimeTracking();
      if (RemainingCareBottleValue > 0 && !CanCollectAnyCareBottleNow())
      {
        EnterStorageFullGate(CareStationCollectionPhase.Care);
        return;
      }
      SetState(CareStationState.PresentCareBottles);
      _view.ShowBottleProduction(RemainingCareBottleValue, 0);
      SaveNow();
    }

    private void EnterWaitForPushAway(CareStationCollectionPhase phase)
    {
      _save.activeCollectionPhase = phase;
      if (CurrentRemainingBottleValue > 0 &&
          (phase != CareStationCollectionPhase.Care || !CanCollectAnyCareBottleNow()) &&
          CareStationStorageRules.Remaining(_save) <= 0)
      {
        EnterStorageFullGate(phase);
        return;
      }
      EnsureXpBundles();
      _pushAwayRecognitionStartedAt = -1f;
      PreparePushReference(phase);
      BeginPushDistanceStep(CareDistanceDirection.Away);
      _gameplay.SetCareCollectionArmed(false);
      _gameplay.SetCareActionActive(false);
      SetState(phase == CareStationCollectionPhase.Offline
        ? CareStationState.WaitOfflinePushAway
        : CareStationState.WaitCarePushAway);
      _researchRecorder?.RecordPushStarted(phase);
      if (CurrentPushReferenceValid)
      {
        _pushAwayRecognitionStartedAt = Time.unscaledTime;
        _view.ShowDistanceCollection(
          CurrentRemainingBottleValue,
          CareDistanceDirection.Away,
          0f,
          "DEAD ZONE",
          false);
      }
      else _view.HideAllModals();
      SaveNow();
    }

    private void UpdateWaitForPushAway(float delta)
    {
      if (!CareStationStateRules.CanArmCollection(
            _save.activeCollectionPhase,
            _save.careActionCompleted,
            _save.returnedNeutralAfterOffline)) return;
      if (!CurrentPushReferenceValid)
      {
        if (!CapturePushReference(delta))
        {
          if (CanOfferPushStepFallback)
            _view.ShowDistanceCollection(
              CurrentRemainingBottleValue,
              CareDistanceDirection.Away,
              0f,
              "SENSOR UNAVAILABLE",
              true);
          else
            _view.HideAllModals();
          return;
        }
        _pushAwayRecognitionStartedAt = Time.unscaledTime;
        _view.ShowDistanceCollection(
          CurrentRemainingBottleValue,
          CareDistanceDirection.Away,
          0f,
          "DEAD ZONE",
          false);
        SaveNow();
      }

      EnsurePushDistanceStep(CareDistanceDirection.Away);
      if (!TryUpdatePushRatio(delta, out _, out var sampleDelta, out var sampleFresh))
      {
        _pushDistanceStep?.FreezeForTrackingLoss();
        _view.ShowDistanceCollection(
          CurrentRemainingBottleValue,
          CareDistanceDirection.Away,
          _pushDistanceStep?.Progress ?? 0f,
          !EffectiveTracking
            ? "TRACKING LOST"
            : CanOfferPushStepFallback ? "SENSOR UNAVAILABLE" : "DEAD ZONE",
          CanOfferPushStepFallback);
        return;
      }

      var stepCompleted = Time.unscaledTime - _distanceStepOpenedAt >= _distanceStepTransitionSeconds &&
                          _pushDistanceStep.Advance(
                            _smoothedPushFaceScale,
                            CurrentPushReferenceScale,
                            sampleDelta,
                            EffectiveTracking,
                            sampleFresh);
      _view.ShowDistanceCollection(
        CurrentRemainingBottleValue,
        CareDistanceDirection.Away,
        _pushDistanceStep.Progress,
        CurrentPushDistanceState,
        CanOfferPushStepFallback);
      if (!stepCompleted) return;

      _gameplay.SetCareCollectionArmed(true);
      if (!_gameplay.StartCareCollectionFromGestureReference())
      {
        _gameplay.SetCareCollectionArmed(false);
        return;
      }
      // The completed Away position is the independent Closer-step origin.
      // It is persisted through bottle flight and never inferred from Center.
      SetCurrentPushReference(_smoothedPushFaceScale, true);
      RecordPushAwayCompletion(CareStationPushAwayCompletion.SensorCompleted);
      BeginCollectionState(_save.activeCollectionPhase);
    }

    private void PreparePushReference(CareStationCollectionPhase phase)
    {
      _pushReferenceSampler = new CareDistanceReferenceSampler(
        _gestureReferenceCaptureSeconds,
        _gestureReferenceMinimumSamples);
      _pushDistanceStep = null;
      _pushStepDirection = CareDistanceDirection.None;
      _lastPushScaleSequence = long.MinValue;
      _lastPushFreshSampleAt = -1f;
      _hasSmoothedPushFaceScale = false;
      _smoothedPushFaceScale = 0f;
      _currentPushFaceScale = 0f;
      _rawPushFaceScale = 0f;
      _currentPushRatio = 1f;
      _pushFreshSamplesInStep = 0;
      _distanceStepOpenedAt = -1f;
      var restoredScale = IsDistanceResetState
        ? _save.distanceResetReferenceScale
        : phase == CareStationCollectionPhase.Offline
          ? _save.offlinePushReferenceScale
          : _save.carePushReferenceScale;
      var restoredValid = IsDistanceResetState
        ? _save.distanceResetReferenceValid
        : phase == CareStationCollectionPhase.Offline
          ? _save.offlinePushReferenceValid
          : _save.carePushReferenceValid;
      if (_pushReferenceSampler.Restore(restoredScale, restoredValid))
      {
        _smoothedPushFaceScale = restoredScale;
        _currentPushFaceScale = restoredScale;
        _hasSmoothedPushFaceScale = true;
      }
    }

    private bool CapturePushReference(float delta)
    {
      if (_pushReferenceSampler == null) PreparePushReference(_save.activeCollectionPhase);
      if (!TryReadFreshFaceScale(out var scale, out var sequence))
      {
        if (!EffectiveTracking) _pushReferenceSampler.Reset();
        return false;
      }
      _rawPushFaceScale = scale;
      if (!_pushReferenceSampler.AddFreshSample(sequence, scale, Time.unscaledTime, EffectiveTracking)) return false;
      SetCurrentPushReference(_pushReferenceSampler.ReferenceScale, true);
      _smoothedPushFaceScale = _pushReferenceSampler.ReferenceScale;
      _currentPushFaceScale = _pushReferenceSampler.ReferenceScale;
      _hasSmoothedPushFaceScale = true;
      _lastPushScaleSequence = sequence;
      _lastPushFreshSampleAt = Time.unscaledTime;
      _currentPushRatio = 1f;
      _rawPushFaceScale = _pushReferenceSampler.ReferenceScale;
      return true;
    }

    private bool TryUpdatePushRatio(float delta, out float ratio, out float sampleDelta, out bool sampleFresh)
    {
      ratio = _currentPushRatio;
      sampleDelta = 0f;
      sampleFresh = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      if (_developmentDistanceRatio.HasValue && CurrentPushReferenceValid)
      {
        ratio = _developmentDistanceRatio.Value;
        sampleDelta = Mathf.Clamp(delta, 0f, 0.25f);
        sampleFresh = true;
        _currentPushRatio = ratio;
        _rawPushFaceScale = FaceDistanceRatio.ToFaceScale(CurrentPushReferenceScale, ratio);
        ObservePushScale(_rawPushFaceScale);
        _smoothedPushFaceScale = _rawPushFaceScale;
        _currentPushFaceScale = _smoothedPushFaceScale;
        _hasSmoothedPushFaceScale = true;
        _pushFreshSamplesInStep++;
        return true;
      }
#endif
      if (!EffectiveTracking || !CurrentPushReferenceValid)
      {
        _lastPushFreshSampleAt = -1f;
        return false;
      }
      if (!TryReadFreshFaceScale(out var scale, out var sequence)) return false;
      if (sequence == _lastPushScaleSequence) return true;
      _lastPushScaleSequence = sequence;
      sampleFresh = true;
      _pushFreshSamplesInStep++;
      _rawPushFaceScale = scale;
      ObservePushScale(scale);
      var now = Time.unscaledTime;
      sampleDelta = _lastPushFreshSampleAt >= 0f
        ? Mathf.Clamp(now - _lastPushFreshSampleAt, 0f, 0.25f)
        : 0f;
      _lastPushFreshSampleAt = now;
      if (!_hasSmoothedPushFaceScale)
      {
        _smoothedPushFaceScale = scale;
        _hasSmoothedPushFaceScale = true;
      }
      else
      {
        var smoothingDelta = sampleDelta > 0f ? sampleDelta : delta;
        var smoothing = 1f - Mathf.Exp(-_gestureScaleSmoothingSpeed * Mathf.Max(0f, smoothingDelta));
        _smoothedPushFaceScale = Mathf.Lerp(_smoothedPushFaceScale, scale, smoothing);
      }
      _currentPushFaceScale = _smoothedPushFaceScale;
      _currentPushRatio = FaceDistanceRatio.FromFaceScale(_smoothedPushFaceScale, CurrentPushReferenceScale);
      ratio = _currentPushRatio;
      return CareDistanceReferenceSampler.IsValidScale(ratio);
    }

    private bool TryReadFreshFaceScale(out float scale, out long sequence)
    {
      scale = 0f;
      sequence = long.MinValue;
      var snapshot = EyeInputDebugState.Latest;
      if (snapshot.FaceDetected && CareDistanceReferenceSampler.IsValidScale(snapshot.RobustFaceScale))
      {
        scale = snapshot.RobustFaceScale;
        sequence = snapshot.SampleSequence;
        return true;
      }
      if (_gameplay == null || !_gameplay.HasValidDistanceSample ||
          !CareDistanceReferenceSampler.IsValidScale(_gameplay.CurrentFaceScale)) return false;
      scale = _gameplay.CurrentFaceScale;
      sequence = Time.frameCount;
      return true;
    }

    private float CurrentPushReferenceScale => IsDistanceResetState
      ? _save.distanceResetReferenceScale
      : _save.activeCollectionPhase == CareStationCollectionPhase.Offline
        ? _save.offlinePushReferenceScale
        : _save.carePushReferenceScale;

    private bool CurrentPushReferenceValid => _save != null &&
      (IsDistanceResetState
        ? _save.distanceResetReferenceValid
        : _save.activeCollectionPhase == CareStationCollectionPhase.Offline
          ? _save.offlinePushReferenceValid
          : _save.activeCollectionPhase == CareStationCollectionPhase.Care && _save.carePushReferenceValid) &&
      CareDistanceReferenceSampler.IsValidScale(CurrentPushReferenceScale);

    private void SetCurrentPushReference(float scale, bool valid)
    {
      valid = valid && CareDistanceReferenceSampler.IsValidScale(scale);
      if (IsDistanceResetState)
      {
        _save.distanceResetReferenceScale = valid ? scale : 0f;
        _save.distanceResetReferenceValid = valid;
      }
      else if (_save.activeCollectionPhase == CareStationCollectionPhase.Offline)
      {
        _save.offlinePushReferenceScale = valid ? scale : 0f;
        _save.offlinePushReferenceValid = valid;
      }
      else if (_save.activeCollectionPhase == CareStationCollectionPhase.Care)
      {
        _save.carePushReferenceScale = valid ? scale : 0f;
        _save.carePushReferenceValid = valid;
      }
    }

    private void HandleFallbackCollect()
    {
      HandleDistanceStepFallback();
    }

    private void RecordPushAwayCompletion(CareStationPushAwayCompletion completion)
    {
      _save.pushAwayCompleted = true;
      _save.pushAwayCompletion = completion;
      if (_save.activeCollectionPhase == CareStationCollectionPhase.Offline)
        _save.offlinePushAwayCompletion = completion;
      else if (_save.activeCollectionPhase == CareStationCollectionPhase.Care)
        _save.carePushAwayCompletion = completion;
      _researchRecorder?.RecordPushCompleted(_save.activeCollectionPhase, completion);
    }

    private void BeginCollectionState(CareStationCollectionPhase phase)
    {
      if (phase == CareStationCollectionPhase.Care) _save.careCollectionReleased = true;
      _gameplay.SetCareActionActive(false);
      SetState(phase == CareStationCollectionPhase.Offline
        ? CareStationState.CollectingOfflineBottles
        : CareStationState.CollectingCareBottles);
      _view.ShowCollecting(CurrentRemainingBottleValue);
      _view.SetCrewState(CareCrewState.Carry);
      CareAudioFeedbackController.EnsureExists().PlayPushAway();
      SaveNow();
    }

    private void ResumeCollectionAfterReload()
    {
      ResetCollectionRuntimeTracking();
      _gameplay.SetCareActionActive(false);
      _gameplay.SetCareCollectionArmed(false);
      if (CurrentRemainingBottleValue <= 0)
      {
        FinishCurrentCollection();
        return;
      }

      // The push already succeeded before this state was persisted. Rebuild
      // only the value that has not arrived; never send the player back through
      // the push gate or treat the rebuilt flight as a new reward.
      if (!EnsureXpBundles())
      {
        if (CareStationStorageRules.Remaining(_save) <= 0 && !CanCollectAnyCareBottleNow())
        {
          EnterStorageFullGate(_save.activeCollectionPhase);
          return;
        }
        _collectionPausedReason = "REBUILDING BUNDLES";
        BeginCollectionState(_save.activeCollectionPhase);
        return;
      }

      if (_gameplay.IsExperienceCollectionInProgress)
      {
        BeginCollectionState(_save.activeCollectionPhase);
        return;
      }
      _gameplay.SetCareCollectionArmed(true);
      if (_gameplay.StartCareCollectionFromSkip())
      {
        // The persisted push completion remains SensorCompleted/FallbackCompleted;
        // this only rebuilds the unfinished flight after Unity objects were lost.
        BeginCollectionState(_save.activeCollectionPhase);
        return;
      }

      // Keep the persisted collection state. MaintainCurrentCollection retries
      // a missing runtime object without requiring another Push Away.
      _gameplay.SetCareCollectionArmed(false);
      _collectionPausedReason = "REBUILDING BUNDLES";
      BeginCollectionState(_save.activeCollectionPhase);
    }

    private void HandleExperienceArrival(int targetId, CareExperienceState state, int value)
    {
      if (!IsCollectionState(State)) return;
      if (!_arrivedCollectionTargetIds.Add(targetId)) return;
      _ledger.RecordArrival(value);
      if (_save.activeCollectionPhase == CareStationCollectionPhase.Offline)
      {
        _save.shiftStoredFullBottles += Mathf.Max(0, value);
        _save.storedFullBottles += Mathf.Max(0, value);
      }
      else if (_save.activeCollectionPhase == CareStationCollectionPhase.Care)
      {
        CareEconomyRules.TryStoreReservedBottle(_save);
      }
      if (_save.activeCollectionPhase == CareStationCollectionPhase.Offline)
        _save.collectedOfflineBottleValue = Mathf.Min(PendingOfflineBottleValue, _save.collectedOfflineBottleValue + Mathf.Max(0, value));
      else
        _save.collectedCareBottleValue = _save.pendingFullBottleShipment <= 0 ? 1 : 0;
      _save.collectedExperienceCount = _save.storedFullBottles;
      _view.ApplyStation(_save);
      _view.SetPendingXp(CurrentRemainingBottleValue, CurrentGoldBottleCount);
      SetState(_save.activeCollectionPhase == CareStationCollectionPhase.Offline
        ? CareStationState.WaitOfflineBottlesStored
        : CareStationState.WaitCareBottlesStored);
      if (CurrentRemainingBottleValue <= 0 || _ledger.IsComplete) FinishCurrentCollection();
      else SaveNow();
    }

    private void FinishCurrentCollection()
    {
      if (CurrentRemainingBottleValue > 0)
      {
        _xpBundlesSpawned = false;
        _gameplay.SetCareCollectionArmed(false);
        EnterStorageFullGate(_save.activeCollectionPhase);
        return;
      }
      if (_save.activeCollectionPhase == CareStationCollectionPhase.Offline)
      {
        _save.pendingOfflineXP = 0;
        _save.collectedOfflineBottleValue = 0;
        _save.offlineCollectionResolved = true;
        _save.returnedNeutralAfterOffline = false;
        _save.activeCollectionPhase = CareStationCollectionPhase.None;
        _save.pushAwayCompleted = false;
        _save.pushAwayCompletion = CareStationPushAwayCompletion.None;
        _view.SetPendingXp(0);
        _gameplay.SetCareCollectionArmed(false);
        EnterWaitReturnToNeutral(CareStationCollectionPhase.Offline);
        return;
      }

      _save.collectedCareBottleValue = 0;
      _save.careCollectionReleased = false;
      _save.activeCollectionPhase = CareStationCollectionPhase.None;
      _view.SetPendingXp(0);
      _gameplay.SetCareCollectionArmed(false);
      EnterWaitReturnToNeutral(CareStationCollectionPhase.Care);
    }

    private void FinishCareCollectionAfterReturn()
    {
      EnterPostCareCheck();
    }

    private void ContinueAfterCareReport()
    {
      if (_save.inspectionActive)
      {
        _save.inspectionRewardStored = true;
        _save.inspectionCompleted = true;
        _save.inspectionActive = false;
        _save.stationLevel = 2;
        CareProductionTransportRules.Synchronize(_save);
        _save.completedShifts++;
        if (!_save.inspectionCompletionSignalSent)
        {
          _save.inspectionCompletionSignalSent = true;
          FirstStationInspectionCompleted?.Invoke();
        }
        EnterShiftComplete();
        return;
      }
      SetState(CareStationState.UpgradeCheck);
      _save.completedShifts++;
      if (CareStationStateRules.CanOfferStationUpgrade(
            _save.completedShifts,
            true,
            _save))
        EnterUpgradeSelection();
      else
      {
        EnterShiftComplete();
      }
    }

    private void EnterWaitReturnToNeutral(CareStationCollectionPhase returnPhase = CareStationCollectionPhase.None)
    {
      _gameplay.SetCareCollectionArmed(false);
      _gameplay.SetCareActionActive(false);
      if (returnPhase != CareStationCollectionPhase.None) _save.pendingReturnPhase = returnPhase;
      if (_save.pendingReturnPhase == CareStationCollectionPhase.None)
        _save.pendingReturnPhase = _save.careActionCompleted
          ? CareStationCollectionPhase.Care
          : CareStationCollectionPhase.Offline;
      _save.activeCollectionPhase = CareStationCollectionPhase.None;
      _returnRecognitionStartedAt = Time.unscaledTime;
      _lastPushScaleSequence = long.MinValue;
      _lastPushFreshSampleAt = -1f;
      _pushFreshSamplesInStep = 0;
      _pushObservedMinimum = float.PositiveInfinity;
      _pushObservedMaximum = float.NegativeInfinity;
      _hasSmoothedPushFaceScale = false;
      _smoothedPushFaceScale = 0f;
      _rawPushFaceScale = 0f;
      BeginPushDistanceStep(CareDistanceDirection.Closer);
      SetState(CareStationState.WaitReturnToNeutral);
      _view.ShowDistanceCollection(0, CareDistanceDirection.Closer, 0f, "DEAD ZONE", false);
      SaveNow();
    }

    private void UpdateReturnToNeutral(float delta)
    {
      var referenceScale = _save.pendingReturnPhase == CareStationCollectionPhase.Care
        ? _save.carePushReferenceScale
        : _save.offlinePushReferenceScale;
      var referenceValid = _save.pendingReturnPhase == CareStationCollectionPhase.Care
        ? _save.carePushReferenceValid
        : _save.offlinePushReferenceValid;
      if (!referenceValid || !CareDistanceReferenceSampler.IsValidScale(referenceScale))
      {
        // Old saves may arrive here without an action reference. Capture one
        // silently so migration never exposes a calibration gate or deadlocks.
        var phase = _save.pendingReturnPhase;
        _save.activeCollectionPhase = phase;
        if (_pushReferenceSampler == null) PreparePushReference(phase);
        if (CapturePushReference(delta)) SaveNow();
        _save.activeCollectionPhase = CareStationCollectionPhase.None;
        _view.ShowDistanceCollection(
          0,
          CareDistanceDirection.Closer,
          0f,
          EffectiveTracking ? "CAPTURING REFERENCE" : "TRACKING LOST",
          CanOfferPushStepFallback);
        return;
      }

      var savedPhase = _save.activeCollectionPhase;
      _save.activeCollectionPhase = _save.pendingReturnPhase;
      var validRatio = TryUpdatePushRatio(delta, out _, out var sampleDelta, out var sampleFresh);
      _save.activeCollectionPhase = savedPhase;
      if (DevelopmentNeutralActive)
      {
        _smoothedPushFaceScale = FaceDistanceRatio.ToFaceScale(referenceScale, 1f + _distanceCompleteThreshold);
        _currentPushFaceScale = _smoothedPushFaceScale;
        _currentPushRatio = FaceDistanceRatio.FromFaceScale(_smoothedPushFaceScale, referenceScale);
        validRatio = true;
        sampleDelta = delta;
        sampleFresh = true;
      }
      if (!validRatio)
      {
        _pushDistanceStep?.FreezeForTrackingLoss();
        _view.ShowDistanceCollection(
          0,
          CareDistanceDirection.Closer,
          _pushDistanceStep?.Progress ?? 0f,
          !EffectiveTracking
            ? "TRACKING LOST"
            : CanOfferPushStepFallback ? "SENSOR UNAVAILABLE" : "DEAD ZONE",
          CanOfferPushStepFallback);
        return;
      }
      EnsurePushDistanceStep(CareDistanceDirection.Closer);
      var completed = Time.unscaledTime - _distanceStepOpenedAt >= _distanceStepTransitionSeconds &&
                      _pushDistanceStep.Advance(
                        _smoothedPushFaceScale,
                        referenceScale,
                        sampleDelta,
                        EffectiveTracking,
                        sampleFresh);
      _view.ShowDistanceCollection(
        0,
        CareDistanceDirection.Closer,
        _pushDistanceStep.Progress,
        CurrentPushDistanceState,
        CanOfferPushStepFallback);
      if (!completed) return;

      CompleteReturnGate(CareStationReturnCompletion.SensorCompleted);
    }

    private void HandleReturnFallback()
    {
      if (_careActions != null && _careActions.CanOfferDistanceFallback)
      {
        var reason = _careActions.DistanceSensorUnavailable
          ? CareDistanceFallbackReason.SensorUnavailable
          : CareDistanceFallbackReason.ChangedBelowThreshold;
        if (_careActions.CompleteCurrentDistanceStepForFallback(reason)) SaveNow();
        return;
      }
      HandleDistanceStepFallback();
    }

    private void CompleteReturnGate(CareStationReturnCompletion completion)
    {
      if (State != CareStationState.WaitReturnToNeutral || _save.pendingReturnPhase == CareStationCollectionPhase.None)
        return;

      var completedPhase = _save.pendingReturnPhase;
      _save.pendingReturnPhase = CareStationCollectionPhase.None;
      _returnRecognitionStartedAt = -1f;
      _pushDistanceStep = null;
      _pushStepDirection = CareDistanceDirection.None;
      _view.SetReturnFallbackAvailable(false);
      _view.HideAllModals();
      if (completedPhase == CareStationCollectionPhase.Care)
      {
        _save.careReturnCompletion = completion;
        _save.carePushReferenceScale = 0f;
        _save.carePushReferenceValid = false;
        FinishCareCollectionAfterReturn();
      }
      else
      {
        _save.offlineReturnCompletion = completion;
        _save.offlinePushReferenceScale = 0f;
        _save.offlinePushReferenceValid = false;
        _save.returnedNeutralAfterOffline = true;
        _gameplay.SetCareActionActive(true);
        EnterStationWorking();
      }
    }

    private void BeginPushDistanceStep(CareDistanceDirection direction)
    {
      _pushStepDirection = direction;
      _pushDistanceStep = new CareRelativeDistanceStep(
        direction,
        _distanceDeadZone,
        _distanceCompleteThreshold,
        _distanceStepHoldSeconds,
        _distanceProgressFallSeconds);
      _distanceStepOpenedAt = Time.unscaledTime;
      _pushFreshSamplesInStep = 0;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      _developmentDistanceRatio = null;
#endif
    }

    private void EnsurePushDistanceStep(CareDistanceDirection direction)
    {
      if (_pushDistanceStep != null && _pushStepDirection == direction) return;
      BeginPushDistanceStep(direction);
    }

    private void HandleDistanceStepFallback()
    {
      if (!CanOfferPushStepFallback || _save == null) return;
      var reason = PushStepSensorUnavailable
        ? CareDistanceFallbackReason.SensorUnavailable
        : CareDistanceFallbackReason.ChangedBelowThreshold;

      if (State == CareStationState.WaitReturnToNeutral)
      {
        RecordDistanceFallback(_save.pendingReturnPhase, CareDistanceDirection.Closer, reason);
        CompleteReturnGate(CareStationReturnCompletion.ReturnFallbackCompleted);
        return;
      }

      if (!IsWaitingForAwayDistanceStep) return;
      var phase = _save.activeCollectionPhase;
      RecordDistanceFallback(phase, CareDistanceDirection.Away, reason);
      _gameplay.SetCareCollectionArmed(true);
      if (!_gameplay.StartCareCollectionFromSkip())
      {
        _gameplay.SetCareCollectionArmed(false);
        return;
      }
      var closerOrigin = _hasSmoothedPushFaceScale && CareDistanceReferenceSampler.IsValidScale(_smoothedPushFaceScale)
        ? _smoothedPushFaceScale
        : CurrentPushReferenceScale;
      SetCurrentPushReference(closerOrigin, true);
      RecordPushAwayCompletion(CareStationPushAwayCompletion.FallbackCompleted);
      BeginCollectionState(phase);
    }

    private void RecordDistanceFallback(
      CareStationCollectionPhase phase,
      CareDistanceDirection direction,
      CareDistanceFallbackReason reason)
    {
      if (phase == CareStationCollectionPhase.Offline)
      {
        if (direction == CareDistanceDirection.Away) _save.offlineAwayFallbackReason = reason;
        else _save.offlineCloserFallbackReason = reason;
      }
      else if (phase == CareStationCollectionPhase.Care)
      {
        if (direction == CareDistanceDirection.Away) _save.careAwayFallbackReason = reason;
        else _save.careCloserFallbackReason = reason;
      }
    }

    private bool IsWaitingForAwayDistanceStep =>
      State == CareStationState.WaitOfflinePushAway ||
      State == CareStationState.WaitCarePushAway ||
      State == CareStationState.WaitPushAwayReady ||
      State == CareStationState.WaitPushAway;

    private bool CanOfferPushStepFallback =>
      (IsWaitingForAwayDistanceStep || State == CareStationState.WaitReturnToNeutral) &&
      _distanceStepOpenedAt >= 0f &&
      Time.unscaledTime - _distanceStepOpenedAt >= _distanceFallbackDelay;

    private bool PushStepSensorUnavailable => CanOfferPushStepFallback &&
      (_pushFreshSamplesInStep <= 1 || !HasMeaningfulPushScaleUpdates());

    private void ObservePushScale(float scale)
    {
      if (!CareDistanceReferenceSampler.IsValidScale(scale)) return;
      _pushObservedMinimum = Mathf.Min(_pushObservedMinimum, scale);
      _pushObservedMaximum = Mathf.Max(_pushObservedMaximum, scale);
    }

    private bool HasMeaningfulPushScaleUpdates()
    {
      var reference = State == CareStationState.WaitReturnToNeutral
        ? ReturnReferenceScale(_save.pendingReturnPhase)
        : CurrentPushReferenceScale;
      return CareDistanceReferenceSampler.HasMeaningfulScaleUpdates(
        _pushObservedMinimum,
        _pushObservedMaximum,
        reference);
    }

    private string CurrentPushDistanceState
    {
      get
      {
        if (!EffectiveTracking) return "TRACKING LOST";
        if (!CurrentPushReferenceValid && State != CareStationState.WaitReturnToNeutral) return "CAPTURING REFERENCE";
        if (PushStepSensorUnavailable) return "SENSOR UNAVAILABLE";
        if (_pushDistanceStep == null || _pushDistanceStep.Progress <= 0f) return "DEAD ZONE";
        if (_pushDistanceStep.StableSeconds > 0f) return "STABILIZING";
        return _pushStepDirection == CareDistanceDirection.Closer ? "MOVING CLOSER" : "MOVING AWAY";
      }
    }

    private void UpdateDistanceSafety(float delta)
    {
      var snapshot = EyeInputDebugState.Latest;
      if (!EffectiveTracking || !snapshot.FaceDetected)
      {
        _tooCloseHeld = 0f;
        _distanceSafetyNeutralHeld = 0f;
        _view.SetDistanceSafetyWarning(false);
        _gameplay?.SetCareStationAbsoluteDistanceSafety(false, 0f);
        return;
      }

      var faceOccupancy = Mathf.Clamp01(Mathf.Max(0f, snapshot.FaceRect.width) * Mathf.Max(0f, snapshot.FaceRect.height));
      if (faceOccupancy >= _unverifiedExtremeFaceOccupancy)
      {
        _tooCloseHeld += delta;
        _distanceSafetyNeutralHeld = 0f;
        if (_tooCloseHeld >= _tooClosePromptDelay)
        {
          var amount = Mathf.InverseLerp(
            _unverifiedExtremeFaceOccupancy,
            Mathf.Min(0.9f, _unverifiedExtremeFaceOccupancy + 0.25f),
            faceOccupancy);
          _view.SetDistanceSafetyWarning(true);
          _gameplay?.SetCareStationAbsoluteDistanceSafety(true, amount);
        }
        return;
      }

      if (faceOccupancy <= _unverifiedExtremeFaceOccupancy * 0.8f)
      {
        _distanceSafetyNeutralHeld += delta;
        if (_distanceSafetyNeutralHeld >= _distanceSafetyRecoverySeconds)
        {
          _tooCloseHeld = 0f;
          _view.SetDistanceSafetyWarning(false);
          _gameplay?.SetCareStationAbsoluteDistanceSafety(false, 0f);
        }
      }
      else
      {
        _distanceSafetyNeutralHeld = 0f;
      }
    }

    private void EnterUpgradeSelection()
    {
      _save.upgradeOffered = true;
      if (!CareStationShiftRules.CanPurchaseAnyUpgrade(_save, _upgradeConfiguration, _economyConfiguration))
      {
        DeferUpgradeOpportunity();
        return;
      }
      _save.upgradeDeferred = false;
      SetState(CareStationState.UpgradeSelection);
      _gameplay.SetCareActionActive(true);
      _view.ShowUpgrade(_save, _upgradeConfiguration, _economyConfiguration);
      SaveNow();
    }

    private void HandleUpgradeSelected(CareStationUpgradeId upgrade)
    {
      var storageRecovery = State == CareStationState.WaitStorageSpace;
      var formalSelection = State == CareStationState.UpgradeSelection;
      var pendingOpportunity = _save.upgradeOffered;
      if (!_view.IsUpgradeVisible || (!pendingOpportunity && !storageRecovery)) return;
      var availability = CareStationShiftRules.EvaluateUpgrade(
        _save,
        upgrade,
        _upgradeConfiguration,
        _economyConfiguration);
      if (!availability.CanPurchase)
      {
        _view.ShowUpgradeFeedback(upgrade, availability.PlayerReason);
        CareAudioFeedbackController.EnsureExists().PlayUpgradeUnavailable();
        return;
      }
      var previousValue = _upgradeConfiguration.Value(upgrade, CareStationShiftRules.GetUpgradeLevel(_save, upgrade));
      if (!CareStationShiftRules.TryPurchaseUpgrade(
            _save,
            upgrade,
            _upgradeConfiguration,
            _economyConfiguration)) return;
      _save.upgradeOffered = false;
      _save.upgradeDeferred = false;
      _save.offlineProductionPausedByFullStorage = CareStationStorageRules.Remaining(_save) <= 0;
      _view.ApplyStation(_save);
      CareAudioFeedbackController.EnsureExists().PlayStepComplete();
      var currentValue = _upgradeConfiguration.Value(upgrade, CareStationShiftRules.GetUpgradeLevel(_save, upgrade));
      var resultTitle = upgrade == CareStationUpgradeId.LargerStorage ? "STORAGE EXPANDED"
        : upgrade == CareStationUpgradeId.MoreWorkers ? "CREW EXPANDED" : "CART EXPANDED";
      _view.ShowStationUpgradeResult(resultTitle, previousValue, currentValue);
      if (storageRecovery)
      {
        ResumeAfterStorageSpaceAvailable();
        return;
      }
      if (formalSelection) EnterShiftComplete();
      else RestoreCurrentPresentation();
    }

    private void HandleNavigationSelected(int index)
    {
      if (_save == null) return;
      if (index == 0)
      {
        HandleUpgradeBackSelected();
        return;
      }
      if (index == 1)
      {
        _view.ShowUpgrade(_save, _upgradeConfiguration, _economyConfiguration);
        return;
      }
      if (index == 2)
      {
        _view.ShowCareReport(_save);
      }
    }

    private void HandleUpgradeBackSelected()
    {
      if (_save == null) return;
      if (State == CareStationState.UpgradeSelection)
      {
        DeferUpgradeOpportunity();
        return;
      }
      if (State == CareStationState.WaitStorageSpace)
      {
        _view.ShowStorageFullStation(_save);
        return;
      }
      RestoreCurrentPresentation();
    }

    private void DeferUpgradeOpportunity()
    {
      if (_save == null) return;
      CareStationShiftRules.MarkUpgradeDeferred(_save, DateTime.UtcNow);
      _view.SetUpgradeOpportunity(true);
      if (!_save.careShiftCompleted)
      {
        _save.careShiftCompleted = true;
        CareStationEventLog.Append(_save, CareStationEventType.ShiftCompleted, DateTime.UtcNow);
      }
      _save.shiftCompleteRewardsShown = true;
      _save.endShiftConsumed = true;
      _save.autoShiftEntered = true;
      SetState(CareStationState.AutoShift);
      _gameplay?.SetCareActionActive(true);
      _view.ShowAutoShift();
      SaveNow();
    }

    private void RestoreCurrentPresentation()
    {
      switch (State)
      {
        case CareStationState.StationWorking:
          _view.ShowStationWorking();
          break;
        case CareStationState.AutoShift:
          _view.ShowAutoShift();
          break;
        case CareStationState.ShiftComplete:
          _view.ShowShiftComplete(_save);
          break;
        case CareStationState.WaitStorageSpace:
          _view.ShowStorageFullStation(_save);
          break;
        case CareStationState.CareReport:
          _view.ShowCareReport(_save);
          break;
        case CareStationState.WaitIncidentSelection:
        case CareStationState.PresentIncident:
          _view.ShowStationWorking();
          break;
        default:
          _view.HideAllModals();
          _view.ApplyStation(_save);
          break;
      }
    }

    private void EnterStorageFullGate(CareStationCollectionPhase phase)
    {
      _save.activeCollectionPhase = phase;
      _save.offlineProductionPausedByFullStorage = true;
      _gameplay.SetCareCollectionArmed(false);
      _gameplay.SetCareActionActive(true);
      SetState(CareStationState.WaitStorageSpace);
      _view.ShowStorageFull(_save, _upgradeConfiguration);
      _view.SetCrewState(CareCrewState.Rest);
      _researchRecorder?.SyncFromSave(_save);
      SaveNow();
    }

    private void RestoreStorageFullGate()
    {
      if (!HasPendingStorageReward(_save))
      {
        _save.activeCollectionPhase = CareStationCollectionPhase.None;
        _save.pendingReturnPhase = CareStationCollectionPhase.None;
        _save.offlineProductionPausedByFullStorage = CareStationStorageRules.Remaining(_save) <= 0;
        EnterStationWorking();
        return;
      }
      if (CareStationStorageRules.Remaining(_save) > 0 || CanCollectAnyCareBottleNow())
      {
        ResumeAfterStorageSpaceAvailable();
        return;
      }
      EnterStorageFullGate(_save.activeCollectionPhase == CareStationCollectionPhase.None
        ? (_save.careActionCompleted ? CareStationCollectionPhase.Care : CareStationCollectionPhase.Offline)
        : _save.activeCollectionPhase);
    }

    private static bool HasPendingStorageReward(CareStationSaveData save)
    {
      if (save == null) return false;
      var pendingCare = save.pendingFullBottleShipment > 0;
      var pendingOffline = save.pendingOfflineXP > save.collectedOfflineBottleValue;
      return pendingCare || pendingOffline;
    }

    private bool IsGuidanceInputExpected()
    {
      if (_careActions == null) return false;
      var action = _careActions.ActionType;
      if (action != CareActionType.PilotEyeRoutine && action != CareActionType.GuidedEyeCircles)
        return false;
      return State == CareStationState.WaitCareActionStart ||
             State == CareStationState.CareActionInProgress ||
             State == CareStationState.CareActionPaused ||
             State == CareStationState.CareActionCompleted;
    }

    private void ResumeAfterStorageSpaceAvailable()
    {
      if (_save == null) return;
      if (_save.productionStage != CareProductionStage.None)
        CareProductionRules.AdvanceForegroundCycle(_save, 0f, _productionConfiguration);
      _save.offlineProductionPausedByFullStorage =
        _save.careEnergy > 0 && CareStationStorageRules.RemainingForAutomaticOfflineSettlement(_save) <= 0;
      _view.ApplyStation(_save);
      // v22 never returns to the retired Push Away collection path. A finished
      // item either commits atomically when a slot exists or stays persisted in
      // WaitingForStorage while the rest of the Station remains available.
      EnterPostCareCheck();
      SaveNow();
    }

    private void EnterShiftComplete()
    {
      if (!_save.careShiftCompleted)
      {
        _save.careShiftCompleted = true;
        CareStationEventLog.Append(_save, CareStationEventType.ShiftCompleted, DateTime.UtcNow);
      }
      _save.shiftCompleteRewardsShown = true;
      _save.endShiftConsumed = false;
      _save.autoShiftEntered = false;
      SetState(CareStationState.ShiftComplete);
      EnterShiftCompletePresentation();
      SaveNow();
    }

    private void EnterShiftCompletePresentation()
    {
      _gameplay?.SetCareActionActive(true);
      SetState(CareStationState.ShiftComplete);
      _view.ShowShiftComplete(_save);
      _view.SetCrewState(CareCrewState.Cheer);
    }

    private void HandleEndShiftSelected()
    {
      if (_save == null || State != CareStationState.ShiftComplete ||
          !_save.careShiftCompleted || _save.endShiftConsumed) return;
      var now = DateTime.UtcNow;
      _save.endShiftConsumed = true;
      _save.autoShiftEntered = true;
      _save.StampActive(now);
      _save.StampClaimed(now);
      CareStationEventLog.Append(_save, CareStationEventType.ShiftEnded, now);
      _researchRecorder?.Persist(_save, true);
      EnterAutoShift();
    }

    private bool PrepareNextShift(bool validOfflineInterval, bool developmentOverride)
    {
      var scheduleInspection = CareStationInspectionRules.CanSchedule(_save);
      if (!CareStationShiftRules.TryBeginNextShift(_save, validOfflineInterval, developmentOverride)) return false;
      _save.selectedIncident = CareStationIncidentType.None;
      _save.shiftIncidentGenerated = false;
      _save.careActionElapsed = 0f;
      _save.careActionCompleted = false;
      _save.careAction?.Reset();
      _save.currentRecipe?.Reset();
      _recipe = null;
      _save.careActionGestureReferenceScale = 0f;
      _save.careActionReferenceValid = false;
      _save.offlinePushReferenceScale = 0f;
      _save.offlinePushReferenceValid = false;
      _save.carePushReferenceScale = 0f;
      _save.carePushReferenceValid = false;
      _save.pendingReturnPhase = CareStationCollectionPhase.None;
      _save.pushAwayCompleted = false;
      _save.pushAwayCompletion = CareStationPushAwayCompletion.None;
      _save.offlineReturnCompletion = CareStationReturnCompletion.None;
      _save.careReturnCompletion = CareStationReturnCompletion.None;
      _save.offlineCollectionResolved = false;
      _save.returnedNeutralAfterOffline = false;
      _save.collectedOfflineBottleValue = 0;
      _save.collectedCareBottleValue = 0;
      _save.activeCollectionPhase = CareStationCollectionPhase.None;
      _save.careCollectionReleased = false;
      // A deferred opportunity remains available from the persistent UPGRADES
      // tab until a route is actually purchased.
      _save.careShiftCompleted = false;
      _save.autoShiftEntered = false;
      _save.shiftCompleteRewardsShown = false;
      _save.endShiftConsumed = false;
      _save.shiftStoredFullBottles = 0;
      _save.shiftStoredGoldBottles = 0;
      _save.careStepChangePending = false;
      _save.careStepWasReplaced = false;
      _save.replacedOriginalAction = CareActionType.None;
      _save.replacedWithAction = CareActionType.None;
      _save.replacementPauseReason = CareActionPauseReason.None;
      _save.currentResearchSessionId = string.Empty;
      _save.researchSessionStartedUtc = string.Empty;
      _save.currentSessionEventRecordReference = string.Empty;
      _save.preCareScores = new CareSubjectiveScores();
      _save.postCareScores = new CareSubjectiveScores();
      _save.careReportShown = false;
      _save.careReportConsumed = false;
      _save.researchSessionExported = false;
      _save.sessionActiveCareSeconds = 0f;
      _save.sessionClosedEyeSeconds = 0f;
      _save.sessionFocusShiftCompletions = 0;
      _save.sessionTrackingLostCount = 0;
      _save.sessionTrackingLostSeconds = 0f;
      _save.distanceResetReferenceScale = 0f;
      _save.distanceResetReferenceValid = false;
      _save.distanceResetAwayScale = 0f;
      _save.distanceResetAwayCompleted = false;
      _save.distanceResetCompleted = false;
      _researchRecorder = new CareResearchSessionRecorder(_researchMode);
      if (scheduleInspection)
      {
        _save.inspectionTriggered = true;
        _save.inspectionActive = true;
        _save.inspectionCurrentCheck = 0;
        _save.inspectionCompletedMask = 0;
        _save.inspectionRewardProduced = false;
        _save.inspectionRewardStored = false;
        _save.currentRecipe = CareStationInspectionRules.CreateRecipe(_save.careShiftId);
        _recipe = new CareRecipeRuntime(_save.currentRecipe);
      }
      _production.SettleLegacyPending(_save);
      SetState(CareStationState.Dormant);
      return true;
    }

    private void AssignOfflineBottlesToCurrentShift(int value)
    {
      value = Mathf.Max(0, value);
      if (value <= 0) return;
      if (IsCurrentShiftSupply && _save.collectedOfflineBottleValue <= 0 && _save.pendingOfflineXP > 0)
        _save.pendingOfflineXP--;
      _save.pendingOfflineXP += value;
      _save.offlineCollectionResolved = false;
      _save.returnedNeutralAfterOffline = false;
      _save.offlineRewardReason = CareStationPushAwayCompletion.None;
      _save.offlinePushAwayCompletion = CareStationPushAwayCompletion.None;
      _save.collectedOfflineBottleValue = 0;
    }

    private void AssignQueuedOfflineBottlesToCurrentShift()
    {
      if (_save.queuedOfflineXP <= 0) return;
      var queued = _save.queuedOfflineXP;
      _save.queuedOfflineXP = 0;
      AssignOfflineBottlesToCurrentShift(queued);
    }

    private void EnterAutoShift()
    {
      _gameplay?.SetCareActionActive(true);
      _save.careShiftCompleted = true;
      _save.endShiftConsumed = true;
      _save.autoShiftEntered = true;
      SetState(CareStationState.AutoShift);
      _view.ShowAutoShift();
      _view.SetCrewState(CareCrewState.Work);
      SaveNow();
    }

    private void ResetCollectionRuntimeTracking()
    {
      _xpBundlesSpawned = false;
      _collectionSpawnedBundleCount = 0;
      _collectionPausedReason = "NONE";
      _lastCollectionRecoveryAttemptAt = -1f;
      _arrivedCollectionTargetIds.Clear();
      _ledger.Begin(0);
    }

    private bool MaintainCurrentCollection()
    {
      if (!IsCollectionState(State)) return false;
      _gameplay.SetCareActionActive(false);

      if (CurrentRemainingBottleValue <= 0)
      {
        FinishCurrentCollection();
        return false;
      }

      var plan = CareStationCollectionRecoveryRules.Plan(
        _save,
        CurrentRemainingBottleValue,
        _gameplay.PendingUnsettledExperienceValue,
        CurrentGoldBottleCount);
      if (plan.StorageBlocked)
      {
        _collectionPausedReason = "STORAGE FULL";
        EnterStorageFullGate(_save.activeCollectionPhase);
        return false;
      }

      if (plan.RequiresRuntimeRebuild &&
          (_lastCollectionRecoveryAttemptAt < 0f || Time.unscaledTime - _lastCollectionRecoveryAttemptAt >= 0.25f))
      {
        _collectionPausedReason = "REBUILDING BUNDLES";
        _lastCollectionRecoveryAttemptAt = Time.unscaledTime;
        EnsureXpBundles();
      }

      if (_gameplay.PendingConvertedExperienceCount > 0)
      {
        _gameplay.SetCareCollectionArmed(true);
        if (_gameplay.StartCareCollectionFromSkip()) _collectionPausedReason = "NONE";
        else _collectionPausedReason = "WAITING FOR FLIGHT";
      }
      else if (_gameplay.IsExperienceCollectionInProgress)
      {
        _collectionPausedReason = "NONE";
      }
      else if (CurrentRemainingBottleValue > 0)
      {
        _collectionPausedReason = "REBUILDING BUNDLES";
      }

      return IsCollectionState(State);
    }

    private bool EnsureXpBundles()
    {
      if (_gameplay == null || _save == null) return false;
      var plan = CareStationCollectionRecoveryRules.Plan(
        _save,
        CurrentRemainingBottleValue,
        _gameplay.PendingUnsettledExperienceValue,
        CurrentGoldBottleCount);
      if (plan.StorageBlocked)
      {
        _collectionPausedReason = "STORAGE FULL";
        return false;
      }
      if (plan.CollectibleValue <= 0)
      {
        _collectionPausedReason = CurrentRemainingBottleValue > 0 ? "NO STORAGE SPACE" : "NONE";
        return false;
      }

      if (_ledger.ExpectedValue != plan.CollectibleValue || _ledger.IsComplete)
        _ledger.Begin(plan.CollectibleValue);
      _gameplay.ConfigureCareRoundExperienceRequirement(plan.CollectibleValue);

      if (!plan.RequiresRuntimeRebuild)
      {
        _xpBundlesSpawned = true;
        _collectionPausedReason = "NONE";
        _view.SetPendingXp(CurrentRemainingBottleValue, CurrentGoldBottleCount);
        return _gameplay.PendingUnsettledExperienceCount > 0;
      }

      var remaining = plan.MissingRuntimeValue;
      var preferredSize = Mathf.Max(1, _save.cartCapacity);
      var count = Mathf.Clamp(
        Mathf.Max(Mathf.CeilToInt(remaining / (float)preferredSize), plan.CollectibleGoldValue),
        1,
        _maximumXpBundleVisuals);
      var goldCount = 0;
      var quotient = remaining / count;
      var remainder = remaining % count;
      var spawnedValue = 0;
      for (var i = 0; i < count; i++)
      {
        var value = quotient + (i < remainder ? 1 : 0);
        var side = i % 2 == 0 ? 0.14f : 0.86f;
        var row = i / 2;
        var y = 0.3f + (row % 10) * 0.045f;
        var state = i >= count - goldCount ? CareExperienceState.Rested : CareExperienceState.Raw;
        var targetId = _gameplay.SpawnPendingCareExperienceBundle(value, state, new Vector2(side, y));
        if (targetId == EdgeOrbitHarvestMvp.NoTargetId) continue;
        spawnedValue += value;
        _collectionSpawnedBundleCount++;
      }
      _xpBundlesSpawned = plan.ExistingRuntimeValue + spawnedValue >= plan.CollectibleValue;
      _collectionPausedReason = _xpBundlesSpawned ? "NONE" : "BUNDLE SPAWN FAILED";
      _view.SetPendingXp(CurrentRemainingBottleValue, CurrentGoldBottleCount);
      return plan.ExistingRuntimeValue + spawnedValue > 0;
    }

    private bool CanCollectAnyCareBottleNow()
    {
      if (_save == null || RemainingCareBottleValue <= 0) return false;
      return CareStationStorageRules.Remaining(_save) > 0;
    }

    private void SetState(CareStationState state)
    {
      if (State == state) return;
      State = state;
      _stateStartedAt = Time.unscaledTime;
      if (_save != null) _save.currentState = state;
      StateChanged?.Invoke(state);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
      Debug.Log($"Care Station state: {state}.", this);
#endif
    }

    private float StateElapsed => Time.unscaledTime - _stateStartedAt;
    private float ReturnReferenceScale(CareStationCollectionPhase phase)
    {
      if (_save == null) return 0f;
      return phase == CareStationCollectionPhase.Offline
        ? _save.offlinePushReferenceScale
        : phase == CareStationCollectionPhase.Care ? _save.carePushReferenceScale : 0f;
    }

    private bool ReturnReferenceValid(CareStationCollectionPhase phase)
    {
      if (_save == null) return false;
      var valid = phase == CareStationCollectionPhase.Offline
        ? _save.offlinePushReferenceValid
        : phase == CareStationCollectionPhase.Care && _save.carePushReferenceValid;
      return valid && CareDistanceReferenceSampler.IsValidScale(ReturnReferenceScale(phase));
    }
    private CareStationIncidentType CurrentIncident => CareStationIncidentType.None;
    private int PendingOfflineBottleValue => _save == null ? 0 : Mathf.Max(0, _save.pendingOfflineXP);
    private int RemainingOfflineBottleValue => _save == null
      ? 0
      : Mathf.Max(0, PendingOfflineBottleValue - _save.collectedOfflineBottleValue);
    private int PendingCareBottleValue => _save == null || !_save.careActionCompleted
      ? 0
      : Mathf.Max(0, _save.pendingFullBottleShipment);
    private int RemainingCareBottleValue => _save == null
      ? 0
      : Mathf.Max(0, PendingCareBottleValue);
    private int CurrentRemainingBottleValue => _save == null
      ? 0
      : _save.activeCollectionPhase == CareStationCollectionPhase.Offline
        ? RemainingOfflineBottleValue
        : _save.activeCollectionPhase == CareStationCollectionPhase.Care ? RemainingCareBottleValue : 0;
    private int CurrentGoldBottleCount => 0;
    private bool IsCurrentShiftSupply => _save != null &&
      _save.shiftSupplyGeneratedForShiftId == _save.careShiftId &&
      _save.offlineRewardReason == CareStationPushAwayCompletion.NoOfflineReward;
    private float RequiredCareSeconds => CurrentIncident == CareStationIncidentType.DrySpot ? _drySpotRestSeconds : _screenDownSeconds;
    private float Progress => _save == null ? 0f : Mathf.Clamp01(_save.careActionElapsed / Mathf.Max(1f, RequiredCareSeconds));

    private bool EffectiveTracking
    {
      get
      {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (_developmentEyesClosed.HasValue) return true;
#endif
        return _gameplay != null && _gameplay.IsTrackingAvailable;
      }
    }
    private bool DevelopmentNeutralActive
    {
      get
      {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        return Time.unscaledTime <= _developmentNeutralUntil;
#else
        return false;
#endif
      }
    }

    private static bool IsSessionEntryState(CareStationState state)
    {
      return state == CareStationState.Dormant || state == CareStationState.AutoShift ||
             state == CareStationState.WelcomeBack ||
             state == CareStationState.OfflineProductionSummary;
    }

    private static bool IsCollectionState(CareStationState state)
    {
      return state == CareStationState.CollectingOfflineBottles ||
             state == CareStationState.WaitOfflineBottlesStored ||
             state == CareStationState.CollectingCareBottles ||
             state == CareStationState.WaitCareBottlesStored ||
             state == CareStationState.CollectingExperience ||
             state == CareStationState.WaitExperienceCollected;
    }

    private void Subscribe()
    {
      if (_subscribed || _gameplay == null) return;
      _gameplay.CareExperienceReachedBar += HandleExperienceArrival;
      _subscribed = true;
    }

    private void Unsubscribe()
    {
      if (!_subscribed) return;
      if (_gameplay != null)
      {
        _gameplay.CareExperienceReachedBar -= HandleExperienceArrival;
      }
      _subscribed = false;
    }

    private void SaveNow()
    {
      if (_save != null && _careActions != null && !_careActions.IsDevelopmentTest && _careActions.SaveData != null)
      {
        _save.careAction = _careActions.SaveData;
        SyncCareActionReferenceToSave();
      }
      _researchRecorder?.Persist(_save, false);
      _saveService?.Save(_save, DateTime.UtcNow);
    }

    private void OnApplicationPause(bool paused)
    {
      if (paused)
      {
        _careActions?.SuspendForApplication(true);
        if (_save != null && _careActions != null && !_careActions.IsDevelopmentTest && _careActions.SaveData != null)
          _save.careAction = _careActions.SaveData;
        SaveNow();
        _resumingFromPause = true;
      }
      else if (_resumingFromPause && _save != null)
      {
        _resumingFromPause = false;
        _resumingFromFocus = false;
        ResumeForegroundSession();
      }
    }

    private void OnApplicationFocus(bool focused)
    {
      if (!focused)
      {
        _careActions?.SuspendForApplication(false);
        if (_save != null && _careActions != null && !_careActions.IsDevelopmentTest && _careActions.SaveData != null)
          _save.careAction = _careActions.SaveData;
        SaveNow();
        _resumingFromFocus = true;
      }
      else if (_resumingFromFocus && !_resumingFromPause && _save != null)
      {
        _resumingFromFocus = false;
        ResumeForegroundSession();
      }
    }

    private void ResumeForegroundSession()
    {
      var sessionEntry = IsSessionEntryState(State);
      SettleOffline(DateTime.UtcNow, false);
      if (_lastOfflineResult.HasAnything && sessionEntry)
      {
        _save.returnedNeutralAfterOffline = false;
        _resumeStateBeforeWelcome = CareStationState.AutoShift;
        SetState(CareStationState.WelcomeBack);
        _view.ShowWelcome(_lastOfflineResult);
      }
      else if (sessionEntry && State != CareStationState.AutoShift && !_save.endShiftConsumed)
      {
        // Foreground resume keeps the current Shift. Each later distance gate
        // silently captures its own reference immediately before it opens.
        _save.returnedNeutralAfterOffline = false;
        BeginSessionCollectionFlow();
      }
      else if (State == CareStationState.AutoShift)
      {
        EnterAutoShift();
      }
      _view?.RebindInputHandlers();
      _view?.SynchronizeUiInputOwnership(IsGuidanceInputExpected());
    }

    private void OnDestroy()
    {
      _stationAudio?.StopWork();
      SaveNow();
      Unsubscribe();
      if (_view != null)
      {
        _view.StartCareSelected -= HandleStartCareSelected;
        _view.ContinueSelected -= HandleWelcomeContinue;
        _view.FallbackCollectSelected -= HandleFallbackCollect;
        _view.ReturnFallbackSelected -= HandleReturnFallback;
        _view.UpgradeSelected -= HandleUpgradeSelected;
        _view.NavigationSelected -= HandleNavigationSelected;
        _view.UpgradeBackSelected -= HandleUpgradeBackSelected;
        _view.ChangeStepSelected -= HandleChangeStepRequested;
        _view.UseRestSelected -= HandleUseRestSelected;
        _view.KeepStepSelected -= HandleKeepStepSelected;
        _view.EndShiftSelected -= HandleEndShiftSelected;
        _view.SubjectiveScoresChanged -= HandleSubjectiveScoresChanged;
        _view.SubjectiveScoresSubmitted -= HandleSubjectiveScoresSubmitted;
        _view.SubjectiveScoresSkipped -= HandleSubjectiveScoresSkipped;
        _view.CareReportDoneSelected -= HandleCareReportDone;
      }
      if (_careActions != null) _careActions.CareActionCompleted -= HandleUnifiedCareActionCompleted;
      if (Instance == this) Instance = null;
    }
  }
}
