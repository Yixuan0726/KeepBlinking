using System;

namespace KeepBlinking.CareStation
{
  public static class CareStationDisplayNames
  {
    public const string Filter = "FILTER";
    public const string Filler = "FILLER";
    public const string Packer = "PACKER";
  }

  public interface ICareActionExecution
  {
    CareActionType ActionType { get; }
    CareActionStage Stage { get; }
    string DisplayName { get; }
    float Progress { get; }
    float RemainingSeconds { get; }
    int RemainingSteps { get; }
    bool RequiresCamera { get; }
    bool RequiresDevicePose { get; }
    CareActionPauseReason PauseReason { get; }
    event Action<CareActionType> CareActionCompleted;
  }

  public enum CareActionType
  {
    None,
    ScreenDown,
    ClosedEyeRest,
    FocusShift,
    GuidedEyeCircles,
    // Retained only so v16 JSON can be migrated without changing serialized
    // enum values. Runtime action libraries, recipes and UI must reject it.
    BlinkReset,
    // v19+ authored action. Legacy values above must never be reordered because
    // older JSON saves serialize these enums numerically.
    PilotEyeRoutine,
  }

  public enum CareRecipeType
  {
    Training,
    Single,
    Double,
    Triple,
    Inspection,
    // Appended for v24. Earlier numeric values are serialized in real saves.
    Full,
  }

  public enum CareRoutineId
  {
    None,
    FocusFlow,
    PilotFlow,
    DeepReset,
    FullCare,
    // Migration-only identity for a started pre-v24 Recipe whose current
    // action order cannot be rewritten without losing real player progress.
    LegacyCompatible,
  }

  [Serializable]
  public sealed class CareRecipeSaveData
  {
    public string recipeId;
    public int recipeSeed;
    public CareRecipeType recipeType = CareRecipeType.Triple;
    public CareRoutineId routineId;
    public CareActionType[] actionList = Array.Empty<CareActionType>();
    // The immutable authored/generated steps. actionList may be safely changed
    // by CHANGE STEP, while this list preserves what was replaced.
    public CareActionType[] originalActionList = Array.Empty<CareActionType>();
    public int currentActionIndex;
    public int completedActionMask;
    public int developerSkippedActionMask;
    public int replacedActionMask;
    public int createdShiftId;
    public bool recipeCompleted;
    public bool completionSignalSent;
    public bool completionConsumed;
    public bool routineIntroCompleted;
    public float routineIntroElapsedSeconds;
    public bool deepRest;
    // v24 Recipe parameters are persisted so a reload cannot turn a short
    // Full Care action back into its standard variant.
    public int focusCycleCount = 6;
    public int pilotRoundsPerAxis = 3;
    public int guidedLapsPerDirection = 3;
    public float closedEyeRestSeconds = 60f;
    // Each live action owns one or more immutable planned reward slots. CHANGE
    // STEP may merge two action entries, but it merges their slot masks rather
    // than creating or deleting Care Energy.
    public int plannedSlotCount;
    public int[] plannedSlotRewards = Array.Empty<int>();
    public int[] rewardSlotMasks = Array.Empty<int>();
    public int rewardedStepMask;
    public int careEnergyRewardedTotal;
    public bool completionFeedbackPlayed;
    // Economy v21: the completed recipe, rather than any presentation state,
    // owns its one-shot Care Energy settlement.
    public bool careEnergyGranted;
    public int careEnergyGrantedAmount;
    // A v19 migration can move the currently running real action later in a
    // repaired recipe (for example Rest after the new Pilot -> Guided pair).
    // Keep that one action snapshot until its turn so real progress is not lost.
    public CareActionSaveData deferredActionSnapshot = new CareActionSaveData();

    public int ActionCount => actionList?.Length ?? 0;
    public CareActionType CurrentAction => !recipeCompleted && actionList != null &&
      currentActionIndex >= 0 && currentActionIndex < actionList.Length
        ? actionList[currentActionIndex]
        : CareActionType.None;

    public bool IsStepCompleted(int index)
    {
      return index >= 0 && index < 31 && (completedActionMask & (1 << index)) != 0;
    }

    public bool IsStepDeveloperSkipped(int index)
    {
      return index >= 0 && index < 31 && (developerSkippedActionMask & (1 << index)) != 0;
    }

    public bool IsStepReplaced(int index)
    {
      return index >= 0 && index < 31 && (replacedActionMask & (1 << index)) != 0;
    }

    public CareActionType OriginalActionAt(int index)
    {
      return originalActionList != null && index >= 0 && index < originalActionList.Length
        ? originalActionList[index]
        : actionList != null && index >= 0 && index < actionList.Length
          ? actionList[index]
          : CareActionType.None;
    }

    public void Reset()
    {
      recipeId = string.Empty;
      recipeSeed = 0;
      recipeType = CareRecipeType.Triple;
      routineId = CareRoutineId.None;
      actionList = Array.Empty<CareActionType>();
      originalActionList = Array.Empty<CareActionType>();
      currentActionIndex = 0;
      completedActionMask = 0;
      developerSkippedActionMask = 0;
      replacedActionMask = 0;
      createdShiftId = 0;
      recipeCompleted = false;
      completionSignalSent = false;
      completionConsumed = false;
      routineIntroCompleted = false;
      routineIntroElapsedSeconds = 0f;
      deepRest = false;
      focusCycleCount = 6;
      pilotRoundsPerAxis = 3;
      guidedLapsPerDirection = 3;
      closedEyeRestSeconds = 60f;
      plannedSlotCount = 0;
      plannedSlotRewards = Array.Empty<int>();
      rewardSlotMasks = Array.Empty<int>();
      rewardedStepMask = 0;
      careEnergyRewardedTotal = 0;
      completionFeedbackPlayed = false;
      careEnergyGranted = false;
      careEnergyGrantedAmount = 0;
      deferredActionSnapshot = new CareActionSaveData();
    }
  }

  public enum CareActionStage
  {
    Preparing,
    Demonstrating,
    WaitingForStart,
    Active,
    Paused,
    Completed,
    Cancelled,
  }

  public enum CareActionPauseReason
  {
    None,
    ApplicationBackground,
    ApplicationFocusLost,
    TrackingLost,
    SensorUnavailable,
    DistanceUnavailable,
    EyesOpen,
    ScreenReturned,
    TooClose,
    Manual,
  }

  public enum CareActionCompletionSource
  {
    None,
    SensorCompleted,
    DeveloperSkipped,
  }

  public enum CareDistanceDirection
  {
    None,
    Closer,
    Away,
  }

  public enum CareDistanceFallbackReason
  {
    None,
    ChangedBelowThreshold,
    SensorUnavailable,
  }

  public enum CareActionInternalPhase
  {
    None,
    ScreenDownDemo,
    ScreenDownWait,
    ScreenDownRest,
    ScreenDownReturn,
    ClosedEyePrompt,
    ClosedEyeActive,
    ClosedEyeWaitReopen,
    FocusReference,
    FocusNeutralStart,
    FocusNearOne,
    FocusFarOne,
    FocusNearTwo,
    FocusFarTwo,
    FocusNeutralFinish,
    GuidedPreviewClockwise,
    GuidedPreviewCounterClockwise,
    GuidedPromptClose,
    GuidedClockwise,
    GuidedPause,
    GuidedCounterClockwise,
    GuidedRelax,
    GuidedWaitReopen,
    BlinkResetIntro,
    BlinkResetWaiting,
    BlinkResetClosed,
    FocusIntro,
    ClosedEyeIntro,
    GuidedClosedRest,
    PilotIntro,
    PilotVertical,
    PilotHorizontal,
    PilotDiagonalA,
    PilotDiagonalB,
    PilotTransition,
  }

  [Serializable]
  public sealed class CareActionSaveData
  {
    public CareActionType actionType;
    public CareActionStage stage = CareActionStage.Preparing;
    public CareActionInternalPhase internalPhase;
    public CareActionPauseReason pauseReason;
    public float elapsedSeconds;
    public float phaseElapsedSeconds;
    public float holdElapsedSeconds;
    public int focusTargetStep;
    public int guidedStage;
    public float gestureReferenceScale;
    public bool gestureReferenceValid;
    public float distanceDirectionProgress;
    public CareDistanceFallbackReason distanceFallbackReason;
    public bool closeRequestCuePlayed;
    public bool completionSignalEmitted;
    public CareActionCompletionSource completionSource;
    public int focusCycleCount;
    public bool focusRearmed;
    public bool focusTrackingRecoveryGuard;
    public bool introWasRequested;
    public int guidedLapCount;
    public float guidedNormalizedProgress;
    public bool guidedClosedPhase;
    public bool guidedOpenCuePlayed;
    public int pilotCurrentAxis;
    public int pilotCurrentRound;
    public int pilotCurrentEndpoint;
    public float pilotNormalizedMoveProgress;
    public bool pilotCompletionConsumed;
    public bool readyToOpenCuePlayed;
    public bool restBenefitVoicePlayed;
    public bool restAlmostCompleteVoicePlayed;
    public bool restCompletionVoicePlayed;
    public float restEarlyOpenVoiceCooldown;
    public int consumedVoiceCueMask;
    public int lastVoiceEventId = -1;

    public bool CountsAsVerifiedCareAction => completionSource == CareActionCompletionSource.SensorCompleted;

    public void Reset()
    {
      actionType = CareActionType.None;
      stage = CareActionStage.Preparing;
      internalPhase = CareActionInternalPhase.None;
      pauseReason = CareActionPauseReason.None;
      elapsedSeconds = 0f;
      phaseElapsedSeconds = 0f;
      holdElapsedSeconds = 0f;
      focusTargetStep = 0;
      guidedStage = 0;
      gestureReferenceScale = 0f;
      gestureReferenceValid = false;
      distanceDirectionProgress = 0f;
      distanceFallbackReason = CareDistanceFallbackReason.None;
      closeRequestCuePlayed = false;
      completionSignalEmitted = false;
      completionSource = CareActionCompletionSource.None;
      focusCycleCount = 0;
      focusRearmed = false;
      focusTrackingRecoveryGuard = false;
      introWasRequested = false;
      guidedLapCount = 0;
      guidedNormalizedProgress = 0f;
      guidedClosedPhase = false;
      guidedOpenCuePlayed = false;
      pilotCurrentAxis = 0;
      pilotCurrentRound = 0;
      pilotCurrentEndpoint = 0;
      pilotNormalizedMoveProgress = 0f;
      pilotCompletionConsumed = false;
      readyToOpenCuePlayed = false;
      restBenefitVoicePlayed = false;
      restAlmostCompleteVoicePlayed = false;
      restCompletionVoicePlayed = false;
      restEarlyOpenVoiceCooldown = 0f;
      consumedVoiceCueMask = 0;
      lastVoiceEventId = -1;
    }
  }

  public readonly struct CareActionInputFrame
  {
    public readonly bool ApplicationActive;
    public readonly bool TrackingValid;
    public readonly bool EyesClosed;
    public readonly bool DeviceSensorAvailable;
    public readonly bool ScreenDown;
    public readonly bool ScreenReturned;
    public readonly bool DistanceReferenceValid;
    public readonly float DistanceRatio;
    public readonly bool DistanceSampleFresh;
    public readonly float DistanceSampleDeltaSeconds;

    public CareActionInputFrame(
      bool applicationActive,
      bool trackingValid,
      bool eyesClosed,
      bool deviceSensorAvailable,
      bool screenDown,
      bool screenReturned,
      bool distanceReferenceValid,
      float distanceRatio)
      : this(
        applicationActive,
        trackingValid,
        eyesClosed,
        deviceSensorAvailable,
        screenDown,
        screenReturned,
        distanceReferenceValid,
        distanceRatio,
        true,
        -1f)
    {
    }

    public CareActionInputFrame(
      bool applicationActive,
      bool trackingValid,
      bool eyesClosed,
      bool deviceSensorAvailable,
      bool screenDown,
      bool screenReturned,
      bool distanceReferenceValid,
      float distanceRatio,
      bool distanceSampleFresh)
      : this(
        applicationActive,
        trackingValid,
        eyesClosed,
        deviceSensorAvailable,
        screenDown,
        screenReturned,
        distanceReferenceValid,
        distanceRatio,
        distanceSampleFresh,
        -1f)
    {
    }

    public CareActionInputFrame(
      bool applicationActive,
      bool trackingValid,
      bool eyesClosed,
      bool deviceSensorAvailable,
      bool screenDown,
      bool screenReturned,
      bool distanceReferenceValid,
      float distanceRatio,
      bool distanceSampleFresh,
      float distanceSampleDeltaSeconds)
    {
      ApplicationActive = applicationActive;
      TrackingValid = trackingValid;
      EyesClosed = eyesClosed;
      DeviceSensorAvailable = deviceSensorAvailable;
      ScreenDown = screenDown;
      ScreenReturned = screenReturned;
      DistanceReferenceValid = distanceReferenceValid;
      DistanceRatio = distanceRatio;
      DistanceSampleFresh = distanceSampleFresh;
      DistanceSampleDeltaSeconds = distanceSampleDeltaSeconds;
    }
  }

  public enum CareStationState
  {
    Dormant,
    LoadingSave,
    WelcomeBack,
    PresentIncident,
    WaitIncidentSelection,
    PromptCareAction,
    WaitCareActionStart,
    CareActionInProgress,
    CareActionPaused,
    CareActionCompleted,
    RepairReveal,
    WaitPushAwayReady,
    WaitPushAway,
    CollectingExperience,
    WaitExperienceCollected,
    UpgradeSelection,
    ShiftComplete,
    AutoShift,
    StationWorking,
    ProduceBottles,
    PresentOfflineBottles,
    WaitOfflinePushAway,
    CollectingOfflineBottles,
    WaitOfflineBottlesStored,
    WaitReturnToNeutral,
    PresentCareBottles,
    WaitCarePushAway,
    CollectingCareBottles,
    WaitCareBottlesStored,
    WaitStorageSpace,
    UpgradeCheck,
    InspectionPreparing,
    InspectionPassed,
    PreCareCheck,
    PostCareCheck,
    CareReport,
    OfflineProductionSummary,
    WaitDistanceResetMoveAway,
    WaitDistanceResetReturn,
  }

  public enum CareStationIncidentType
  {
    None,
    Dust,
    DrySpot,
    EyeGunk,
  }

  public enum CareProductionStage
  {
    None = 0,
    FilterProcessing = 1,
    TransferFilteredLiquid = 2,
    FillerCreateBottle = 3,
    FillerFilling = 4,
    FillerFilled = 5,
    TransferToPacker = 6,
    PackerCapping = 7,
    PackerLabeling = 8,
    PackerPackaging = 9,
    TransferToStorage = 10,
    WaitingForStorage = 11,
  }

  public enum CareProductionTransportMode
  {
    ManualCarry = 0,
    BasicConveyor = 1,
    AdvancedConveyor = 2,
  }

  [Serializable]
  public sealed class CareProductionConfiguration
  {
    public float filterSeconds = 1.4f;
    public float filteredTransferSeconds = 0.7f;
    public float createBottleSeconds = 0.45f;
    public float fillBottleSeconds = 1.5f;
    public float filledHoldSeconds = 0.35f;
    public float packerTransferSeconds = 0.7f;
    public float capSeconds = 0.45f;
    public float labelSeconds = 0.45f;
    public float packageSeconds = 0.55f;
    public float storageTransferSeconds = 0.8f;

    public float Duration(CareProductionStage stage)
    {
      switch (stage)
      {
        case CareProductionStage.FilterProcessing: return Math.Max(0.05f, filterSeconds);
        case CareProductionStage.TransferFilteredLiquid: return Math.Max(0.05f, filteredTransferSeconds);
        case CareProductionStage.FillerCreateBottle: return Math.Max(0.05f, createBottleSeconds);
        case CareProductionStage.FillerFilling: return Math.Max(0.05f, fillBottleSeconds);
        case CareProductionStage.FillerFilled: return Math.Max(0.05f, filledHoldSeconds);
        case CareProductionStage.TransferToPacker: return Math.Max(0.05f, packerTransferSeconds);
        case CareProductionStage.PackerCapping: return Math.Max(0.05f, capSeconds);
        case CareProductionStage.PackerLabeling: return Math.Max(0.05f, labelSeconds);
        case CareProductionStage.PackerPackaging: return Math.Max(0.05f, packageSeconds);
        case CareProductionStage.TransferToStorage: return Math.Max(0.05f, storageTransferSeconds);
        default: return 0f;
      }
    }
  }

  public enum CareStationUpgradeId
  {
    None,
    MoreWorkers,
    LargerStorage,
    BiggerCart,
  }

  [Serializable]
  public struct CareStationUpgradeCost
  {
    public int fullBottles;
    public int goldBottles;

    public CareStationUpgradeCost(int full, int gold)
    {
      fullBottles = Math.Max(0, full);
      goldBottles = Math.Max(0, gold);
    }
  }

  public enum CareStationUpgradeAvailabilityReason
  {
    Available,
    MaximumLevel,
    MissingResources,
    StorageCapacityTooSmall,
  }

  public readonly struct CareStationUpgradeAvailability
  {
    public readonly CareStationUpgradeAvailabilityReason Reason;
    // Legacy source cost is retained for save/config migration and diagnostics.
    public readonly CareStationUpgradeCost Cost;
    public readonly int MissingFull;
    public readonly int MissingGold;
    public readonly int CoinCost;
    public readonly int MissingCoins;

    public bool CanPurchase => Reason == CareStationUpgradeAvailabilityReason.Available;
    public bool IsMaximum => Reason == CareStationUpgradeAvailabilityReason.MaximumLevel;

    public CareStationUpgradeAvailability(
      CareStationUpgradeAvailabilityReason reason,
      CareStationUpgradeCost cost,
      int missingFull,
      int missingGold)
    {
      Reason = reason;
      Cost = cost;
      MissingFull = Math.Max(0, missingFull);
      MissingGold = Math.Max(0, missingGold);
      CoinCost = Math.Max(0, cost.fullBottles + cost.goldBottles * CareEconomyConfiguration.DefaultPremiumBottleCoinValue);
      MissingCoins = Math.Max(0, MissingFull + MissingGold * CareEconomyConfiguration.DefaultPremiumBottleCoinValue);
    }

    public CareStationUpgradeAvailability(
      CareStationUpgradeAvailabilityReason reason,
      CareStationUpgradeCost legacyCost,
      int coinCost,
      int missingCoins,
      bool coinCurrency)
    {
      Reason = reason;
      Cost = legacyCost;
      MissingFull = 0;
      MissingGold = 0;
      CoinCost = Math.Max(0, coinCost);
      MissingCoins = Math.Max(0, missingCoins);
    }

    public string PlayerReason
    {
      get
      {
        if (Reason == CareStationUpgradeAvailabilityReason.MaximumLevel) return "MAX";
        if (Reason != CareStationUpgradeAvailabilityReason.MissingResources) return string.Empty;
        return MissingCoins > 0 ? $"NEED {MissingCoins} COINS" : string.Empty;
      }
    }
  }

  /// <summary>
  /// Central serialized source of truth for the phase-one economy. Legacy
  /// Bottle costs remain authored in CareStationUpgradeConfiguration and are
  /// converted here exactly once at the point of use.
  /// </summary>
  [Serializable]
  public sealed class CareEconomyConfiguration
  {
    public const int DefaultPremiumBottleCoinValue = 5;
    public const int DefaultRoutineCareEnergy = 12;

    // Retained as serialized migration aliases. Every v24 Routine now has the
    // same twelve-point budget, distributed by its persisted slot rewards.
    public int trainingOrSingleCareEnergy = 12;
    public int doubleCareEnergy = 24;
    public int tripleOrInspectionCareEnergy = 36;
    public int routineCareEnergy = DefaultRoutineCareEnergy;
    public int fullBottleCoinValue = 1;
    public int premiumBottleCoinValue = DefaultPremiumBottleCoinValue;

    public int CareEnergyFor(CareRecipeSaveData recipe)
    {
      return recipe == null ? 0 : Math.Max(0, routineCareEnergy);
    }

    public int CoinCost(CareStationUpgradeCost legacyCost)
    {
      return Math.Max(0, legacyCost.fullBottles) +
             Math.Max(0, legacyCost.goldBottles) * Math.Max(0, premiumBottleCoinValue);
    }
  }

  /// <summary>
  /// One serialized source of truth for all station upgrade values and costs.
  /// Array element zero is Level 1; cost element zero purchases Level 2.
  /// </summary>
  [Serializable]
  public sealed class CareStationUpgradeConfiguration
  {
    public int[] workerValues = { 2, 3, 4, 5 };
    public int[] storageValues = { 24, 36, 48, 72 };
    public int[] cartValues = { 4, 6, 8, 12 };
    public CareStationUpgradeCost[] workerCosts =
    {
      new CareStationUpgradeCost(12, 0),
      new CareStationUpgradeCost(24, 1),
      new CareStationUpgradeCost(40, 2),
    };
    public CareStationUpgradeCost[] storageCosts =
    {
      new CareStationUpgradeCost(10, 0),
      new CareStationUpgradeCost(20, 0),
      new CareStationUpgradeCost(36, 2),
    };
    public CareStationUpgradeCost[] cartCosts =
    {
      new CareStationUpgradeCost(10, 0),
      new CareStationUpgradeCost(22, 1),
      new CareStationUpgradeCost(36, 2),
    };

    public const int MaximumLevel = 4;

    public int Value(CareStationUpgradeId upgrade, int level)
    {
      level = Math.Max(1, Math.Min(MaximumLevel, level));
      var values = upgrade == CareStationUpgradeId.MoreWorkers
        ? workerValues
        : upgrade == CareStationUpgradeId.LargerStorage ? storageValues : cartValues;
      var fallback = upgrade == CareStationUpgradeId.MoreWorkers
        ? new[] { 2, 3, 4, 5 }
        : upgrade == CareStationUpgradeId.LargerStorage
          ? new[] { 24, 36, 48, 72 }
          : new[] { 4, 6, 8, 12 };
      return values != null && values.Length >= MaximumLevel
        ? Math.Max(1, values[level - 1])
        : fallback[level - 1];
    }

    public CareStationUpgradeCost Cost(CareStationUpgradeId upgrade, int currentLevel)
    {
      currentLevel = Math.Max(1, Math.Min(MaximumLevel, currentLevel));
      if (currentLevel >= MaximumLevel) return new CareStationUpgradeCost(0, 0);
      var costs = upgrade == CareStationUpgradeId.MoreWorkers
        ? workerCosts
        : upgrade == CareStationUpgradeId.LargerStorage ? storageCosts : cartCosts;
      if (costs != null && costs.Length >= MaximumLevel - 1) return costs[currentLevel - 1];
      var defaults = upgrade == CareStationUpgradeId.MoreWorkers
        ? new[] { new CareStationUpgradeCost(12, 0), new CareStationUpgradeCost(24, 1), new CareStationUpgradeCost(40, 2) }
        : upgrade == CareStationUpgradeId.LargerStorage
          ? new[] { new CareStationUpgradeCost(10, 0), new CareStationUpgradeCost(20, 0), new CareStationUpgradeCost(36, 2) }
          : new[] { new CareStationUpgradeCost(10, 0), new CareStationUpgradeCost(22, 1), new CareStationUpgradeCost(36, 2) };
      return defaults[currentLevel - 1];
    }
  }

  public enum CareStationPushAwayCompletion
  {
    None,
    SensorCompleted,
    FallbackCompleted,
    NoOfflineReward,
  }

  public enum CareStationReturnCompletion
  {
    None,
    SensorCompleted,
    ReturnFallbackCompleted,
  }

  public enum CareStationEventType
  {
    ShiftCompleted,
    ShiftEnded,
    CareStepChangeRequested,
    CareStepReplaced,
    UpgradeDeferred,
  }

  [Serializable]
  public sealed class CareStationEventRecord
  {
    public CareStationEventType eventType;
    public int shiftId;
    public string recordedUtc;
    public CareActionType originalAction;
    public CareActionType replacementAction;
    public CareActionPauseReason pauseReason;
  }

  [Serializable]
  public sealed class CareSubjectiveScores
  {
    public int comfort = -1;
    public int dryness = -1;
    public int eyeStrain = -1;
    public int focusDifficulty = -1;
    public bool submitted;
    public bool skipped;

    public bool HasAllResponses => comfort >= 0 && comfort <= 10 &&
      dryness >= 0 && dryness <= 4 && eyeStrain >= 0 && eyeStrain <= 4 &&
      focusDifficulty >= 0 && focusDifficulty <= 4;
    public bool IsResolved => submitted || skipped;

    public CareSubjectiveScores Clone()
    {
      return new CareSubjectiveScores
      {
        comfort = comfort,
        dryness = dryness,
        eyeStrain = eyeStrain,
        focusDifficulty = focusDifficulty,
        submitted = submitted,
        skipped = skipped,
      };
    }

    public void Sanitize()
    {
      comfort = comfort < 0 ? -1 : Math.Min(10, comfort);
      dryness = dryness < 0 ? -1 : Math.Min(4, dryness);
      eyeStrain = eyeStrain < 0 ? -1 : Math.Min(4, eyeStrain);
      focusDifficulty = focusDifficulty < 0 ? -1 : Math.Min(4, focusDifficulty);
      if (skipped)
      {
        comfort = -1;
        dryness = -1;
        eyeStrain = -1;
        focusDifficulty = -1;
        submitted = false;
      }
      else if (submitted && !HasAllResponses)
      {
        submitted = false;
      }
    }
  }

  public enum CareStationCollectionPhase
  {
    None,
    Offline,
    Care,
  }

  public enum CareCrewState
  {
    Idle,
    Walk,
    Work,
    Carry,
    Rest,
    Cheer,
  }

  [Serializable]
  public sealed class CareStationSaveData
  {
    public int saveVersion = 24;
    public int currentShift = 1;
    public int careShiftId = 1;
    public CareStationState currentState = CareStationState.Dormant;
    public string lastActiveUtc;
    public string lastClaimedUtc;
    public int pendingOfflineXP;
    public int queuedOfflineXP;
    public int pendingIncidentXP;
    public CareStationIncidentType selectedIncident;
    public float careActionElapsed;
    public bool careActionCompleted;
    public bool pushAwayCompleted;
    public CareStationPushAwayCompletion pushAwayCompletion;
    public int collectedExperienceCount;
    public CareStationUpgradeId selectedUpgrade;
    public int stationConstructionState;
    public int crewCount = 2;
    public int storageHours = 24;
    public int cartCapacity = 4;
    public int workerLevel = 1;
    public int storageLevel = 1;
    public int cartLevel = 1;
    public int storedFullBottles;
    public int careEnergy;
    public int coins;
    // A produced foreground bottle is reserved until its dispatch animation
    // reaches Storage. It has already consumed one Care Energy.
    public int pendingFullBottleShipment;
    public CareProductionStage productionStage;
    public float productionStageElapsedSeconds;
    public int productionCycleId;
    public bool productionCycleEnergyConsumed;
    public bool productionCycleStored;
    public string productionCycleSourceRecipeId;
    public string lastForegroundProductionRecipeId;
    public CareProductionTransportMode productionTransportMode;
    public bool basicConveyorUnlockPresented;
    // v21 destination for every legacy Gold Bottle. Premium products do not
    // occupy normal Storage and are sold by the next valid Cart settlement.
    public int pendingPremiumShipment;
    public string lastCartSettlementId;
    public int lastCartFullBottlesSold;
    public int lastCartPremiumBottlesSold;
    public int lastCartCoinsEarned;
    public int lastAutoProducedBottles;
    public int storedGoldBottles;
    public bool offlineProductionPausedByFullStorage;
    public int discardedOfflineBottleCount;
    public bool careCollectionReleased;
    public bool inspectionTriggered;
    public bool inspectionActive;
    public int inspectionCurrentCheck;
    public int inspectionCompletedMask;
    public bool inspectionRewardProduced;
    public bool inspectionRewardStored;
    public bool inspectionCompleted;
    public bool inspectionCompletionSignalSent;
    public int stationLevel = 1;
    public bool upgradeOffered;
    public bool upgradeDeferred;
    public bool firstFormalGoldBottleGenerated;
    public bool shiftIncidentGenerated;
    public int completedShifts;
    public int unlockedUpgradeMask;
    public int pendingGoldBottleCount;
    public int collectedOfflineBottleValue;
    public int collectedCareBottleValue;
    public CareStationPushAwayCompletion offlinePushAwayCompletion;
    public CareStationPushAwayCompletion carePushAwayCompletion;
    public CareStationReturnCompletion offlineReturnCompletion;
    public CareStationReturnCompletion careReturnCompletion;
    public CareStationCollectionPhase activeCollectionPhase;
    public CareStationCollectionPhase pendingReturnPhase;
    public bool offlineCollectionResolved;
    public bool returnedNeutralAfterOffline;
    public int shiftSupplyGeneratedForShiftId;
    public CareStationPushAwayCompletion offlineRewardReason;
    public float careActionGestureReferenceScale;
    public bool careActionReferenceValid;
    public float offlinePushReferenceScale;
    public bool offlinePushReferenceValid;
    public float carePushReferenceScale;
    public bool carePushReferenceValid;
    public CareDistanceFallbackReason offlineAwayFallbackReason;
    public CareDistanceFallbackReason offlineCloserFallbackReason;
    public CareDistanceFallbackReason careAwayFallbackReason;
    public CareDistanceFallbackReason careCloserFallbackReason;
    public CareActionSaveData careAction = new CareActionSaveData();
    public int trainingProgress;
    // Independent v17 training completion bits. This prevents removal of the
    // former first training step from erasing completion of later actions.
    public int completedTrainingActionMask;
    public int formalRecipesCreated;
    public int careRoutinesCreated;
    public CareRoutineId lastCompletedRoutineId;
    public CareRecipeSaveData currentRecipe = new CareRecipeSaveData();
    public string[] recentRecipeHistory = Array.Empty<string>();
    public int focusShiftCooldownUntilShiftId;
    public int guidedEyeCirclesCooldownUntilShiftId;
    public bool careShiftCompleted;
    public bool autoShiftEntered;
    public bool shiftCompleteRewardsShown;
    public bool endShiftConsumed;
    // v9 AutoShift advanced the ID before showing the idle station. This marker
    // lets migrated saves reuse that reserved identity instead of skipping one.
    public bool nextShiftPrepared;
    public int shiftStoredFullBottles;
    public int shiftStoredGoldBottles;
    public bool careStepChangePending;
    public bool careStepWasReplaced;
    public CareActionType replacedOriginalAction;
    public CareActionType replacedWithAction;
    public CareActionPauseReason replacementPauseReason;
    public CareStationEventRecord[] eventHistory = Array.Empty<CareStationEventRecord>();
    public string currentResearchSessionId;
    public string anonymousParticipantId;
    public string researchSessionStartedUtc;
    public string currentSessionEventRecordReference;
    public CareSubjectiveScores preCareScores = new CareSubjectiveScores();
    public CareSubjectiveScores postCareScores = new CareSubjectiveScores();
    public bool careReportShown;
    public bool careReportConsumed;
    public bool researchSessionExported;
    public float sessionActiveCareSeconds;
    public float sessionClosedEyeSeconds;
    public int sessionFocusShiftCompletions;
    public int sessionTrackingLostCount;
    public float sessionTrackingLostSeconds;
    public int lastOfflineStoredFullBottles;
    public int lastOfflineStoredGoldBottles;
    public float lastOfflineWorkedSeconds;
    public bool offlineSummaryConsumed;
    public float distanceResetReferenceScale;
    public bool distanceResetReferenceValid;
    public float distanceResetAwayScale;
    public bool distanceResetAwayCompleted;
    public bool distanceResetCompleted;
    public bool hasSeenFocusShiftIntro;
    // Legacy-only v18 flag. ScreenDown is no longer an authored care action.
    public bool hasSeenScreenBreakIntro;
    public bool hasSeenClosedEyeRestIntro;
    public bool hasSeenGuidedMovementIntro;
    public bool hasSeenPilotEyeRoutineIntro;

    public DateTime ReadLastActiveUtc(DateTime fallback)
    {
      return ParseUtc(lastActiveUtc, fallback);
    }

    public DateTime ReadLastClaimedUtc(DateTime fallback)
    {
      return ParseUtc(lastClaimedUtc, fallback);
    }

    public void StampActive(DateTime utcNow)
    {
      lastActiveUtc = utcNow.ToUniversalTime().ToString("O");
    }

    public void StampClaimed(DateTime utcNow)
    {
      lastClaimedUtc = utcNow.ToUniversalTime().ToString("O");
    }

    private static DateTime ParseUtc(string value, DateTime fallback)
    {
      return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
        ? parsed.ToUniversalTime()
        : fallback.ToUniversalTime();
    }
  }

  public readonly struct CareStationOfflineResult
  {
    public readonly TimeSpan CreditedDuration;
    public readonly int ExperienceMade;
    public readonly int BuildCompleteCount;
    public readonly int HelpNeededCount;

    public CareStationOfflineResult(TimeSpan duration, int xp, int builds, int help)
    {
      CreditedDuration = duration;
      ExperienceMade = Math.Max(0, xp);
      BuildCompleteCount = Math.Max(0, builds);
      HelpNeededCount = Math.Max(0, help);
    }

    public bool HasAnything => ExperienceMade > 0 || BuildCompleteCount > 0 || HelpNeededCount > 0;
  }

  public static class CareStationOfflineCalculator
  {
    public static CareStationOfflineResult Calculate(
      DateTime lastClaimedUtc,
      DateTime nowUtc,
      float minimumMinutes,
      float maximumHours,
      float xpPerHour,
      bool incidentAlreadyGenerated)
    {
      var elapsed = nowUtc.ToUniversalTime() - lastClaimedUtc.ToUniversalTime();
      if (elapsed <= TimeSpan.Zero || elapsed.TotalMinutes < Math.Max(1f, minimumMinutes))
        return new CareStationOfflineResult(TimeSpan.Zero, 0, 0, 0);

      var creditedHours = Math.Min(elapsed.TotalHours, Math.Max(0.5, maximumHours));
      var credited = TimeSpan.FromHours(creditedHours);
      var xp = (int)Math.Floor(creditedHours * Math.Max(0f, xpPerHour));
      var builds = creditedHours >= 4d ? 1 : 0;
      var help = incidentAlreadyGenerated ? 0 : 1;
      return new CareStationOfflineResult(credited, xp, builds, help);
    }
  }

  public readonly struct CareStationOfflineStorageResult
  {
    public readonly int Accepted;
    public readonly int Discarded;
    public readonly bool StorageFull;

    public CareStationOfflineStorageResult(int accepted, int discarded, bool storageFull)
    {
      Accepted = Math.Max(0, accepted);
      Discarded = Math.Max(0, discarded);
      StorageFull = storageFull;
    }
  }

  /// <summary>
  /// Offline production is capacity-limited. Verified care rewards deliberately
  /// stay outside this limiter so they can remain pending until space exists.
  /// </summary>
  public static class CareStationStorageRules
  {
    public static int Capacity(CareStationSaveData save)
    {
      return save == null ? 24 : Math.Max(1, save.storageHours);
    }

    public static int Stored(CareStationSaveData save)
    {
      // Gold is a separate rare-resource wallet. Only Full Bottles consume the
      // physical storage capacity shown by the station rack.
      return save == null ? 0 : Math.Max(0, save.storedFullBottles);
    }

    public static int Remaining(CareStationSaveData save)
    {
      return Math.Max(0, Capacity(save) - Stored(save));
    }

    public static int RemainingForOfflineProduction(CareStationSaveData save)
    {
      if (save == null) return 0;
      var reserved = Math.Max(0, save.pendingFullBottleShipment) +
                     CareProductionRules.ReservedBottleCount(save);
      return Math.Max(0, Capacity(save) - Stored(save) - reserved);
    }

    public static int RemainingForAutomaticOfflineSettlement(CareStationSaveData save)
    {
      if (save == null) return 0;
      return Math.Max(0, Remaining(save) - Math.Max(0, save.pendingFullBottleShipment) -
        CareProductionRules.ReservedBottleCount(save));
    }

    public static CareStationOfflineStorageResult LimitOfflineProduction(CareStationSaveData save, int produced)
    {
      produced = Math.Max(0, produced);
      var available = RemainingForOfflineProduction(save);
      var accepted = Math.Min(produced, available);
      return new CareStationOfflineStorageResult(
        accepted,
        produced - accepted,
        accepted >= available || produced > accepted || Remaining(save) <= 0);
    }

    public static int CollectibleNow(CareStationSaveData save, int pending)
    {
      return Math.Min(Math.Max(0, pending), Remaining(save));
    }
  }

  public readonly struct CareCartSettlementResult
  {
    public readonly int FullBottlesSold;
    public readonly int PremiumBottlesSold;
    public readonly int CoinsEarned;
    public readonly int BottlesProduced;
    public readonly bool AlreadySettled;
    public readonly bool StorageFull;

    public CareCartSettlementResult(
      int fullSold,
      int premiumSold,
      int coinsEarned,
      int produced,
      bool alreadySettled,
      bool storageFull)
    {
      FullBottlesSold = Math.Max(0, fullSold);
      PremiumBottlesSold = Math.Max(0, premiumSold);
      CoinsEarned = Math.Max(0, coinsEarned);
      BottlesProduced = Math.Max(0, produced);
      AlreadySettled = alreadySettled;
      StorageFull = storageFull;
    }
  }

  /// <summary>
  /// Pure phase-one economy transitions. Controllers may replay visuals, but
  /// only these persisted transitions grant energy, produce bottles or settle
  /// Cart proceeds.
  /// </summary>
  public static class CareEconomyRules
  {
    public static bool TryGrantCompletedRecipeStep(
      CareStationSaveData save,
      int completedStepIndex,
      out int granted)
    {
      granted = 0;
      var recipe = save?.currentRecipe;
      if (recipe == null || !recipe.IsStepCompleted(completedStepIndex) ||
          completedStepIndex < 0 || completedStepIndex >= recipe.ActionCount) return false;
      CareRecipeGenerator.SanitizeRecipe(recipe);
      var slotMask = recipe.rewardSlotMasks != null && completedStepIndex < recipe.rewardSlotMasks.Length
        ? recipe.rewardSlotMasks[completedStepIndex]
        : 1 << completedStepIndex;
      var validSlots = recipe.plannedSlotCount <= 0 ? 0 : (1 << recipe.plannedSlotCount) - 1;
      var unclaimed = slotMask & validSlots & ~recipe.rewardedStepMask;
      if (unclaimed == 0) return false;

      var remainingBudget = Math.Max(0,
        CareEconomyConfiguration.DefaultRoutineCareEnergy - recipe.careEnergyRewardedTotal);
      for (var slot = 0; slot < recipe.plannedSlotCount && granted < remainingBudget; slot++)
      {
        if ((unclaimed & (1 << slot)) == 0) continue;
        var authored = recipe.plannedSlotRewards != null && slot < recipe.plannedSlotRewards.Length
          ? Math.Max(0, recipe.plannedSlotRewards[slot])
          : 0;
        granted += Math.Min(authored, remainingBudget - granted);
      }

      recipe.rewardedStepMask |= unclaimed;
      recipe.careEnergyRewardedTotal = Math.Min(
        CareEconomyConfiguration.DefaultRoutineCareEnergy,
        Math.Max(0, recipe.careEnergyRewardedTotal) + granted);
      recipe.careEnergyGrantedAmount = recipe.careEnergyRewardedTotal;
      recipe.careEnergyGranted = recipe.careEnergyRewardedTotal >=
                                 CareEconomyConfiguration.DefaultRoutineCareEnergy;
      if (granted <= 0) return false;
      save.careEnergy = Math.Max(0, save.careEnergy) + granted;
      return true;
    }

    public static bool TryGrantAllCompletedRecipeSteps(CareStationSaveData save, out int granted)
    {
      granted = 0;
      var recipe = save?.currentRecipe;
      if (recipe == null) return false;
      var changed = false;
      for (var step = 0; step < recipe.ActionCount; step++)
      {
        if (!recipe.IsStepCompleted(step)) continue;
        if (!TryGrantCompletedRecipeStep(save, step, out var stepGrant)) continue;
        granted += stepGrant;
        changed = true;
      }
      return changed;
    }

    public static bool TryGrantRecipeCareEnergy(
      CareStationSaveData save,
      CareEconomyConfiguration configuration,
      out int granted)
    {
      // Compatibility entry point used by older controllers/tests. It no
      // longer creates a completion bonus; it only reconciles already-completed
      // planned slots that have not been claimed yet.
      return TryGrantAllCompletedRecipeSteps(save, out granted);
    }

    public static bool TryReserveForegroundBottle(CareStationSaveData save)
    {
      if (save == null || save.careEnergy <= 0 || save.pendingFullBottleShipment > 0 ||
          save.productionStage != CareProductionStage.None ||
          CareStationStorageRules.RemainingForAutomaticOfflineSettlement(save) <= 0) return false;
      save.careEnergy--;
      save.pendingFullBottleShipment = 1;
      save.offlineProductionPausedByFullStorage = false;
      return true;
    }

    public static bool TryStoreReservedBottle(CareStationSaveData save)
    {
      if (save == null || save.pendingFullBottleShipment <= 0 || CareStationStorageRules.Remaining(save) <= 0)
        return false;
      save.pendingFullBottleShipment--;
      save.storedFullBottles++;
      save.shiftStoredFullBottles++;
      save.collectedExperienceCount = save.storedFullBottles;
      save.offlineProductionPausedByFullStorage = CareStationStorageRules.Remaining(save) <= 0;
      return true;
    }

    public static CareCartSettlementResult SettleCart(
      CareStationSaveData save,
      int throughput,
      string settlementId,
      CareEconomyConfiguration configuration)
    {
      if (save == null) return default;
      configuration = configuration ?? new CareEconomyConfiguration();
      throughput = Math.Max(0, throughput);
      settlementId = settlementId ?? string.Empty;
      if (!string.IsNullOrEmpty(settlementId) &&
          string.Equals(save.lastCartSettlementId, settlementId, StringComparison.Ordinal))
        return new CareCartSettlementResult(0, 0, 0, 0, true, CareStationStorageRules.Remaining(save) <= 0);

      // Sell only inventory that existed before this settlement. Auto Shift
      // production is stored for a later Cart trip and can never become Coins
      // in the same atomic transaction.
      // Premium shipments use their retained non-Storage lane and are cleared
      // on the next valid Cart settlement. Normal Cart throughput is therefore
      // always still able to free a full ordinary rack.
      var premiumSold = Math.Max(0, save.pendingPremiumShipment);
      var fullSold = Math.Min(Math.Max(0, save.storedFullBottles), throughput);
      var coinsEarned = fullSold * Math.Max(0, configuration.fullBottleCoinValue) +
                        premiumSold * Math.Max(0, configuration.premiumBottleCoinValue);
      var storedAfterSale = Math.Max(0, save.storedFullBottles - fullSold);
      // Auto Shift can use space that existed when the interval began; Cart
      // slots freed by this transaction remain visibly free until a later
      // interval. A full rack therefore never prevents shipment.
      var availableForProduction = Math.Max(0,
        CareStationStorageRules.Capacity(save) - Math.Max(0, save.storedFullBottles) -
        Math.Max(0, save.pendingFullBottleShipment) - CareProductionRules.ReservedBottleCount(save));
      var produced = CareProductionRules.OfflineProductionCount(save, throughput, availableForProduction);

      // Commit together after every value has been derived. SaveService then
      // persists the complete result with the settlement id.
      save.pendingPremiumShipment = Math.Max(0, save.pendingPremiumShipment - premiumSold);
      save.storedFullBottles = storedAfterSale + produced;
      save.careEnergy = Math.Max(0, save.careEnergy - produced);
      save.coins = Math.Max(0, save.coins) + coinsEarned;
      save.lastCartSettlementId = settlementId;
      save.lastCartFullBottlesSold = fullSold;
      save.lastCartPremiumBottlesSold = premiumSold;
      save.lastCartCoinsEarned = coinsEarned;
      save.lastAutoProducedBottles = produced;
      save.shiftStoredFullBottles = Math.Max(0, save.shiftStoredFullBottles + produced - fullSold);
      save.collectedExperienceCount = save.storedFullBottles;
      save.offlineProductionPausedByFullStorage =
        save.careEnergy > 0 && CareStationStorageRules.RemainingForAutomaticOfflineSettlement(save) <= 0;
      return new CareCartSettlementResult(
        fullSold,
        premiumSold,
        coinsEarned,
        produced,
        false,
        CareStationStorageRules.RemainingForAutomaticOfflineSettlement(save) <= 0);
    }
  }

  public readonly struct CareProductionAdvanceResult
  {
    public readonly bool StageChanged;
    public readonly bool BottleStored;
    public readonly bool WaitingForStorage;

    public CareProductionAdvanceResult(bool changed, bool stored, bool waiting)
    {
      StageChanged = changed;
      BottleStored = stored;
      WaitingForStorage = waiting;
    }
  }

  public static class CareProductionRules
  {
    public static int ReservedBottleCount(CareStationSaveData save)
    {
      return save != null && save.productionStage != CareProductionStage.None &&
             !save.productionCycleStored ? 1 : 0;
    }

    public static bool TryBeginForegroundCycle(CareStationSaveData save, string recipeId)
    {
      if (save == null || save.productionStage != CareProductionStage.None ||
          save.careEnergy <= 0 ||
          CareStationStorageRules.RemainingForAutomaticOfflineSettlement(save) <= 0) return false;
      recipeId = recipeId ?? string.Empty;
      if (!string.IsNullOrEmpty(recipeId) &&
          string.Equals(save.lastForegroundProductionRecipeId, recipeId, StringComparison.Ordinal)) return false;

      save.careEnergy--;
      save.productionCycleId = Math.Max(0, save.productionCycleId) + 1;
      save.productionStage = CareProductionStage.FilterProcessing;
      save.productionStageElapsedSeconds = 0f;
      save.productionCycleEnergyConsumed = true;
      save.productionCycleStored = false;
      save.productionCycleSourceRecipeId = recipeId;
      save.lastForegroundProductionRecipeId = recipeId;
      save.pendingFullBottleShipment = 0;
      save.offlineProductionPausedByFullStorage = false;
      return true;
    }

    public static CareProductionAdvanceResult AdvanceForegroundCycle(
      CareStationSaveData save,
      float unscaledDeltaSeconds,
      CareProductionConfiguration configuration)
    {
      if (save == null || save.productionStage == CareProductionStage.None)
        return default;
      configuration = configuration ?? new CareProductionConfiguration();
      var changed = false;
      var stored = false;
      var remainingDelta = Math.Max(0f, unscaledDeltaSeconds);

      if (save.productionStage == CareProductionStage.WaitingForStorage)
      {
        stored = TryCommitForegroundBottle(save);
        return new CareProductionAdvanceResult(stored, stored, !stored);
      }

      save.productionStageElapsedSeconds = Math.Max(0f, save.productionStageElapsedSeconds) + remainingDelta;
      for (var guard = 0; guard < 12 && save.productionStage != CareProductionStage.None; guard++)
      {
        var duration = configuration.Duration(save.productionStage);
        if (duration <= 0f || save.productionStageElapsedSeconds < duration) break;
        save.productionStageElapsedSeconds -= duration;
        var next = NextStage(save.productionStage);
        save.productionStage = next;
        changed = true;
        if (next != CareProductionStage.WaitingForStorage) continue;
        stored = TryCommitForegroundBottle(save);
        break;
      }

      return new CareProductionAdvanceResult(
        changed,
        stored,
        save.productionStage == CareProductionStage.WaitingForStorage);
    }

    public static float StageProgress(CareStationSaveData save, CareProductionConfiguration configuration)
    {
      if (save == null || save.productionStage == CareProductionStage.None) return 0f;
      if (save.productionStage == CareProductionStage.WaitingForStorage) return 1f;
      configuration = configuration ?? new CareProductionConfiguration();
      var duration = configuration.Duration(save.productionStage);
      return duration <= 0f ? 1f : Math.Min(1f, Math.Max(0f, save.productionStageElapsedSeconds) / duration);
    }

    public static int OfflineProductionCount(CareStationSaveData save, int throughput, int availableAtStart)
    {
      if (save == null) return 0;
      return Math.Min(Math.Max(0, throughput),
        Math.Min(Math.Max(0, save.careEnergy), Math.Max(0, availableAtStart)));
    }

    private static bool TryCommitForegroundBottle(CareStationSaveData save)
    {
      if (save == null || save.productionCycleStored) return false;
      if (CareStationStorageRules.Remaining(save) <= 0)
      {
        save.productionStage = CareProductionStage.WaitingForStorage;
        save.productionStageElapsedSeconds = 0f;
        save.offlineProductionPausedByFullStorage = true;
        return false;
      }

      save.storedFullBottles++;
      save.shiftStoredFullBottles++;
      save.collectedExperienceCount = save.storedFullBottles;
      save.productionCycleStored = true;
      save.productionStage = CareProductionStage.None;
      save.productionStageElapsedSeconds = 0f;
      save.offlineProductionPausedByFullStorage = CareStationStorageRules.Remaining(save) <= 0;
      return true;
    }

    private static CareProductionStage NextStage(CareProductionStage stage)
    {
      switch (stage)
      {
        case CareProductionStage.FilterProcessing: return CareProductionStage.TransferFilteredLiquid;
        case CareProductionStage.TransferFilteredLiquid: return CareProductionStage.FillerCreateBottle;
        case CareProductionStage.FillerCreateBottle: return CareProductionStage.FillerFilling;
        case CareProductionStage.FillerFilling: return CareProductionStage.FillerFilled;
        case CareProductionStage.FillerFilled: return CareProductionStage.TransferToPacker;
        case CareProductionStage.TransferToPacker: return CareProductionStage.PackerCapping;
        case CareProductionStage.PackerCapping: return CareProductionStage.PackerLabeling;
        case CareProductionStage.PackerLabeling: return CareProductionStage.PackerPackaging;
        case CareProductionStage.PackerPackaging: return CareProductionStage.TransferToStorage;
        case CareProductionStage.TransferToStorage: return CareProductionStage.WaitingForStorage;
        default: return CareProductionStage.None;
      }
    }
  }

  /// <summary>
  /// One authoritative transport-mode gateway. The current progression has a
  /// single Station level, so L2 represents all three production devices
  /// completing their basic automation retrofit. The method boundary is kept
  /// deliberately independent from that representation so future per-device
  /// levels can replace the predicate without changing save or view callers.
  /// </summary>
  public static class CareProductionTransportRules
  {
    public static bool HasBasicAutomationMilestone(CareStationSaveData save)
    {
      return save != null && (save.stationLevel >= 2 || save.inspectionCompleted);
    }

    public static CareProductionTransportMode AuthoritativeMode(CareStationSaveData save)
    {
      if (!HasBasicAutomationMilestone(save)) return CareProductionTransportMode.ManualCarry;
      return save != null && save.productionTransportMode == CareProductionTransportMode.AdvancedConveyor
        ? CareProductionTransportMode.AdvancedConveyor
        : CareProductionTransportMode.BasicConveyor;
    }

    public static void Synchronize(CareStationSaveData save)
    {
      if (save == null) return;
      if (!Enum.IsDefined(typeof(CareProductionTransportMode), save.productionTransportMode))
        save.productionTransportMode = CareProductionTransportMode.ManualCarry;
      save.productionTransportMode = AuthoritativeMode(save);
    }

    public static bool TryConsumeBasicConveyorUnlock(CareStationSaveData save)
    {
      if (save == null || !HasBasicAutomationMilestone(save)) return false;
      save.productionTransportMode = save.productionTransportMode == CareProductionTransportMode.AdvancedConveyor
        ? CareProductionTransportMode.AdvancedConveyor
        : CareProductionTransportMode.BasicConveyor;
      if (save.basicConveyorUnlockPresented) return false;
      save.basicConveyorUnlockPresented = true;
      return true;
    }
  }

  /// <summary>
  /// Describes the runtime work needed to resume an interrupted bottle flight.
  /// Persisted arrival values remain the source of truth; scene objects only
  /// represent the still-unsettled portion and may safely be rebuilt.
  /// </summary>
  public readonly struct CareStationCollectionRecoveryPlan
  {
    public readonly int RemainingValue;
    public readonly int AvailableStorage;
    public readonly int CollectibleValue;
    public readonly int CollectibleGoldValue;
    public readonly int ExistingRuntimeValue;
    public readonly int MissingRuntimeValue;

    public bool StorageBlocked => RemainingValue > 0 && CollectibleValue <= 0;
    public bool RequiresRuntimeRebuild => MissingRuntimeValue > 0 && !StorageBlocked;

    public CareStationCollectionRecoveryPlan(
      int remainingValue,
      int availableStorage,
      int collectibleValue,
      int collectibleGoldValue,
      int existingRuntimeValue,
      int missingRuntimeValue)
    {
      RemainingValue = Math.Max(0, remainingValue);
      AvailableStorage = Math.Max(0, availableStorage);
      CollectibleValue = Math.Max(0, collectibleValue);
      CollectibleGoldValue = Math.Max(0, Math.Min(CollectibleValue, collectibleGoldValue));
      ExistingRuntimeValue = Math.Max(0, existingRuntimeValue);
      MissingRuntimeValue = Math.Max(0, missingRuntimeValue);
    }
  }

  public static class CareStationCollectionRecoveryRules
  {
    public static CareStationCollectionRecoveryPlan Plan(
      CareStationSaveData save,
      int remainingValue,
      int runtimeUnsettledValue,
      int pendingGoldValue = 0)
    {
      var remaining = Math.Max(0, remainingValue);
      var available = CareStationStorageRules.Remaining(save);
      var collectibleGold = Math.Min(remaining, Math.Max(0, pendingGoldValue));
      var remainingFull = Math.Max(0, remaining - collectibleGold);
      var collectible = collectibleGold + Math.Min(remainingFull, available);
      var existing = Math.Min(collectible, Math.Max(0, runtimeUnsettledValue));
      return new CareStationCollectionRecoveryPlan(
        remaining,
        available,
        collectible,
        collectibleGold,
        existing,
        Math.Max(0, collectible - existing));
    }
  }

  public static class CareStationInspectionRules
  {
    public const int FilterCheck = 1;
    public const int FlowCheck = 2;
    public const int CoreCheck = 4;
    public const int AllChecks = FilterCheck | FlowCheck | CoreCheck;

    public static bool CanSchedule(CareStationSaveData save)
    {
      return save != null && !save.inspectionTriggered && !save.inspectionCompleted &&
             save.workerLevel >= 2 && save.storageLevel >= 2 && save.cartLevel >= 2 &&
             save.pendingOfflineXP <= 0 && save.queuedOfflineXP <= 0 &&
             save.pendingFullBottleShipment <= 0 &&
             save.currentState == CareStationState.AutoShift &&
             save.careShiftCompleted && save.endShiftConsumed;
    }

    public static CareRecipeSaveData CreateRecipe(int shiftId)
    {
      return CareRecipeGenerator.CreateRoutine(
        CareRoutineId.PilotFlow,
        Math.Max(1, shiftId),
        unchecked(Math.Max(1, shiftId) * 486187739),
        true);
    }

    public static int CompletedCheckMask(int completedActionIndex, bool recipeCompleted)
    {
      if (completedActionIndex <= 0) return FilterCheck;
      if (completedActionIndex == 1) return FilterCheck | FlowCheck;
      return recipeCompleted ? AllChecks : FilterCheck | FlowCheck;
    }
  }

  public readonly struct CareStationActionStep
  {
    public readonly float Elapsed;
    public readonly bool PausedForTracking;
    public readonly bool PausedForOpenEyes;
    public readonly bool Completed;

    public CareStationActionStep(float elapsed, bool trackingPause, bool openPause, bool completed)
    {
      Elapsed = Math.Max(0f, elapsed);
      PausedForTracking = trackingPause;
      PausedForOpenEyes = openPause;
      Completed = completed;
    }
  }

  public static class CareStationActionLogic
  {
    public static CareStationActionStep AdvanceClosedEyeRest(
      float elapsed,
      float delta,
      float requiredSeconds,
      bool trackingValid,
      bool eyesClosed)
    {
      if (!trackingValid) return new CareStationActionStep(elapsed, true, false, false);
      if (!eyesClosed) return new CareStationActionStep(elapsed, false, true, false);
      var next = Math.Min(Math.Max(0.1f, requiredSeconds), Math.Max(0f, elapsed) + Math.Max(0f, delta));
      return new CareStationActionStep(next, false, false, next >= Math.Max(0.1f, requiredSeconds));
    }
  }

  public sealed class CareStationExperienceLedger
  {
    public int ExpectedValue { get; private set; }
    public int CollectedValue { get; private set; }
    public int Arrivals { get; private set; }
    public bool IsComplete => ExpectedValue > 0 && CollectedValue >= ExpectedValue;

    public void Begin(int expectedValue)
    {
      ExpectedValue = Math.Max(0, expectedValue);
      CollectedValue = 0;
      Arrivals = 0;
    }

    public void RecordArrival(int value)
    {
      if (value <= 0 || IsComplete) return;
      CollectedValue = Math.Min(ExpectedValue, CollectedValue + value);
      Arrivals++;
    }
  }

  public static class CareStationShiftRules
  {
    public const int AllUpgradeMask = 0b111;

    public static CareStationIncidentType IncidentForShift(int shift)
    {
      shift = Math.Max(1, shift);
      switch (shift)
      {
        case 1: return CareStationIncidentType.Dust;
        case 2: return CareStationIncidentType.DrySpot;
        case 3: return CareStationIncidentType.EyeGunk;
      }

      // A stable shift-seeded choice gives a varied endless loop without changing
      // the incident when a saved game is opened again.
      var seededIndex = Math.Abs(unchecked(shift * 1103515245 + 12345)) % 3;
      return seededIndex == 0
        ? CareStationIncidentType.Dust
        : seededIndex == 1 ? CareStationIncidentType.DrySpot : CareStationIncidentType.EyeGunk;
    }

    public static int IncidentExperience(int shift)
    {
      return IncidentExperience(IncidentForShift(shift));
    }

    public static int IncidentExperience(CareStationIncidentType incident)
    {
      switch (incident)
      {
        case CareStationIncidentType.Dust: return 12;
        case CareStationIncidentType.DrySpot: return 24;
        case CareStationIncidentType.EyeGunk: return 36;
        default: return 0;
      }
    }

    public static int UpgradeBit(CareStationUpgradeId upgrade)
    {
      return upgrade == CareStationUpgradeId.None ? 0 : 1 << ((int)upgrade - 1);
    }

    public static bool HasUpgrade(CareStationSaveData save, CareStationUpgradeId upgrade)
    {
      return GetUpgradeLevel(save, upgrade) > 1;
    }

    public static bool HasAvailableUpgrade(CareStationSaveData save)
    {
      return save != null &&
             (save.workerLevel < CareStationUpgradeConfiguration.MaximumLevel ||
              save.storageLevel < CareStationUpgradeConfiguration.MaximumLevel ||
              save.cartLevel < CareStationUpgradeConfiguration.MaximumLevel);
    }

    public static void ApplyUpgrade(CareStationSaveData save, CareStationUpgradeId upgrade)
    {
      ApplyUpgradeWithoutCost(save, upgrade, new CareStationUpgradeConfiguration());
    }

    public static int GetUpgradeLevel(CareStationSaveData save, CareStationUpgradeId upgrade)
    {
      if (save == null) return 1;
      switch (upgrade)
      {
        case CareStationUpgradeId.MoreWorkers: return Math.Max(1, Math.Min(4, save.workerLevel));
        case CareStationUpgradeId.LargerStorage: return Math.Max(1, Math.Min(4, save.storageLevel));
        case CareStationUpgradeId.BiggerCart: return Math.Max(1, Math.Min(4, save.cartLevel));
        default: return 1;
      }
    }

    public static bool CanPurchaseUpgrade(
      CareStationSaveData save,
      CareStationUpgradeId upgrade,
      CareStationUpgradeConfiguration configuration)
    {
      return EvaluateUpgrade(save, upgrade, configuration, null).CanPurchase;
    }

    public static bool CanPurchaseAnyUpgrade(
      CareStationSaveData save,
      CareStationUpgradeConfiguration configuration)
    {
      return CanPurchaseAnyUpgrade(save, configuration, null);
    }

    public static bool CanPurchaseAnyUpgrade(
      CareStationSaveData save,
      CareStationUpgradeConfiguration configuration,
      CareEconomyConfiguration economy)
    {
      return EvaluateUpgrade(save, CareStationUpgradeId.MoreWorkers, configuration, economy).CanPurchase ||
             EvaluateUpgrade(save, CareStationUpgradeId.LargerStorage, configuration, economy).CanPurchase ||
             EvaluateUpgrade(save, CareStationUpgradeId.BiggerCart, configuration, economy).CanPurchase;
    }

    public static bool CanEnterUpgradeSelection(
      CareStationSaveData save,
      CareStationUpgradeConfiguration configuration)
    {
      return CanPurchaseAnyUpgrade(save, configuration);
    }

    public static void MarkUpgradeDeferred(CareStationSaveData save, DateTime utcNow)
    {
      if (save == null) return;
      if (!save.upgradeDeferred)
        CareStationEventLog.Append(save, CareStationEventType.UpgradeDeferred, utcNow);
      save.upgradeOffered = true;
      save.upgradeDeferred = true;
    }

    public static bool EnsureFirstFormalGoldBottle(CareStationSaveData save)
    {
      // Legacy API retained so old call sites and v20 tests deserialize safely.
      // Gold/Premium products are never generated by care in the v21 economy.
      return false;
    }

    public static CareStationUpgradeAvailability EvaluateUpgrade(
      CareStationSaveData save,
      CareStationUpgradeId upgrade,
      CareStationUpgradeConfiguration configuration)
    {
      return EvaluateUpgrade(save, upgrade, configuration, null);
    }

    public static CareStationUpgradeAvailability EvaluateUpgrade(
      CareStationSaveData save,
      CareStationUpgradeId upgrade,
      CareStationUpgradeConfiguration configuration,
      CareEconomyConfiguration economy)
    {
      if (save == null || upgrade == CareStationUpgradeId.None)
        return new CareStationUpgradeAvailability(
          CareStationUpgradeAvailabilityReason.MissingResources,
          default,
          0,
          0);
      configuration = configuration ?? new CareStationUpgradeConfiguration();
      economy = economy ?? new CareEconomyConfiguration();
      var level = GetUpgradeLevel(save, upgrade);
      if (level >= CareStationUpgradeConfiguration.MaximumLevel)
        return new CareStationUpgradeAvailability(
          CareStationUpgradeAvailabilityReason.MaximumLevel,
          default,
          0,
          0);
      var legacyCost = configuration.Cost(upgrade, level);
      var coinCost = economy.CoinCost(legacyCost);
      var missingCoins = Math.Max(0, coinCost - Math.Max(0, save.coins));
      return new CareStationUpgradeAvailability(
        missingCoins <= 0
          ? CareStationUpgradeAvailabilityReason.Available
          : CareStationUpgradeAvailabilityReason.MissingResources,
        legacyCost,
        coinCost,
        missingCoins,
        true);
    }

    public static bool TryPurchaseUpgrade(
      CareStationSaveData save,
      CareStationUpgradeId upgrade,
      CareStationUpgradeConfiguration configuration)
    {
      return TryPurchaseUpgrade(save, upgrade, configuration, null);
    }

    public static bool TryPurchaseUpgrade(
      CareStationSaveData save,
      CareStationUpgradeId upgrade,
      CareStationUpgradeConfiguration configuration,
      CareEconomyConfiguration economy)
    {
      configuration = configuration ?? new CareStationUpgradeConfiguration();
      economy = economy ?? new CareEconomyConfiguration();
      var availability = EvaluateUpgrade(save, upgrade, configuration, economy);
      if (!availability.CanPurchase) return false;
      save.coins -= availability.CoinCost;
      ApplyUpgradeWithoutCost(save, upgrade, configuration);
      save.offlineProductionPausedByFullStorage = CareStationStorageRules.Remaining(save) <= 0;
      return true;
    }

    public static void SynchronizeUpgradeValues(
      CareStationSaveData save,
      CareStationUpgradeConfiguration configuration)
    {
      if (save == null) return;
      configuration = configuration ?? new CareStationUpgradeConfiguration();
      save.workerLevel = Math.Max(1, Math.Min(4, save.workerLevel));
      save.storageLevel = Math.Max(1, Math.Min(4, save.storageLevel));
      save.cartLevel = Math.Max(1, Math.Min(4, save.cartLevel));
      save.crewCount = configuration.Value(CareStationUpgradeId.MoreWorkers, save.workerLevel);
      save.storageHours = configuration.Value(CareStationUpgradeId.LargerStorage, save.storageLevel);
      save.cartCapacity = configuration.Value(CareStationUpgradeId.BiggerCart, save.cartLevel);
      save.unlockedUpgradeMask = 0;
      if (save.workerLevel > 1) save.unlockedUpgradeMask |= UpgradeBit(CareStationUpgradeId.MoreWorkers);
      if (save.storageLevel > 1) save.unlockedUpgradeMask |= UpgradeBit(CareStationUpgradeId.LargerStorage);
      if (save.cartLevel > 1) save.unlockedUpgradeMask |= UpgradeBit(CareStationUpgradeId.BiggerCart);
      save.collectedExperienceCount = Math.Max(0, save.storedFullBottles);
    }

    public static float ProductionRateMultiplier(CareStationSaveData save)
    {
      if (save == null) return 1f;
      return Math.Max(0.1f, save.crewCount / 2f) * Math.Max(0.1f, save.cartCapacity / 4f);
    }

    public static int ConcurrentCartCount(CareStationSaveData save)
    {
      return save == null ? 0 : Math.Max(0, save.crewCount);
    }

    private static void ApplyUpgradeWithoutCost(
      CareStationSaveData save,
      CareStationUpgradeId upgrade,
      CareStationUpgradeConfiguration configuration)
    {
      if (save == null || upgrade == CareStationUpgradeId.None) return;
      var level = GetUpgradeLevel(save, upgrade);
      if (level >= CareStationUpgradeConfiguration.MaximumLevel) return;
      save.selectedUpgrade = upgrade;
      switch (upgrade)
      {
        case CareStationUpgradeId.MoreWorkers:
          save.workerLevel = level + 1;
          break;
        case CareStationUpgradeId.LargerStorage:
          save.storageLevel = level + 1;
          break;
        case CareStationUpgradeId.BiggerCart:
          save.cartLevel = level + 1;
          break;
      }
      SynchronizeUpgradeValues(save, configuration);
    }

    public static bool EnsureShiftSupply(CareStationSaveData save)
    {
      if (save == null || save.offlineCollectionResolved || save.pendingOfflineXP > 0) return false;
      save.careShiftId = Math.Max(1, save.careShiftId);
      if (save.shiftSupplyGeneratedForShiftId == save.careShiftId)
      {
        // A generated supply which has not been stored must survive reload;
        // the generated marker is the idempotency key for this shift.
        if (save.offlinePushAwayCompletion == CareStationPushAwayCompletion.None)
        {
          save.pendingOfflineXP = 1;
          save.collectedOfflineBottleValue = 0;
          save.offlineRewardReason = CareStationPushAwayCompletion.NoOfflineReward;
        }
        return false;
      }

      save.pendingOfflineXP = 1;
      save.collectedOfflineBottleValue = 0;
      save.shiftSupplyGeneratedForShiftId = save.careShiftId;
      save.offlineRewardReason = CareStationPushAwayCompletion.NoOfflineReward;
      save.offlinePushAwayCompletion = CareStationPushAwayCompletion.None;
      return true;
    }

    public static bool TryBeginNextShift(
      CareStationSaveData save,
      bool validOfflineInterval,
      bool developmentOverride = false)
    {
      if (save == null || save.currentState != CareStationState.AutoShift ||
          !save.careShiftCompleted || !save.endShiftConsumed ||
          (!validOfflineInterval && !developmentOverride)) return false;
      if (!save.nextShiftPrepared)
      {
        save.currentShift = Math.Max(1, save.currentShift + 1);
        save.careShiftId = Math.Max(1, save.careShiftId + 1);
      }
      save.nextShiftPrepared = false;
      return true;
    }
  }

  public static class CareStationEventLog
  {
    private const int MaximumRecords = 64;

    public static void Append(
      CareStationSaveData save,
      CareStationEventType eventType,
      DateTime utcNow,
      CareActionType originalAction = CareActionType.None,
      CareActionType replacementAction = CareActionType.None,
      CareActionPauseReason pauseReason = CareActionPauseReason.None)
    {
      if (save == null) return;
      var previous = save.eventHistory ?? Array.Empty<CareStationEventRecord>();
      var keep = Math.Min(previous.Length, MaximumRecords - 1);
      var records = new CareStationEventRecord[keep + 1];
      var sourceStart = Math.Max(0, previous.Length - keep);
      if (keep > 0) Array.Copy(previous, sourceStart, records, 0, keep);
      records[keep] = new CareStationEventRecord
      {
        eventType = eventType,
        shiftId = Math.Max(1, save.careShiftId),
        recordedUtc = utcNow.ToUniversalTime().ToString("O"),
        originalAction = originalAction,
        replacementAction = replacementAction,
        pauseReason = pauseReason,
      };
      save.eventHistory = records;
    }
  }

  public static class CareStationStateRules
  {
    public static bool CanEnterRepairReveal(bool careActionCompleted)
    {
      return careActionCompleted;
    }

    public static bool CanSettleExperience(bool experienceReachedBar)
    {
      return experienceReachedBar;
    }

    public static bool CanOfferStationUpgrade(int shift, bool allExperienceCollected, CareStationUpgradeId selected)
    {
      return shift == 3 && allExperienceCollected && selected == CareStationUpgradeId.None;
    }

    public static bool CanOfferStationUpgrade(int completedShift, bool allExperienceCollected, int unlockedUpgradeMask)
    {
      return completedShift > 0 && completedShift % 3 == 0 && allExperienceCollected &&
             (unlockedUpgradeMask & CareStationShiftRules.AllUpgradeMask) != CareStationShiftRules.AllUpgradeMask;
    }

    public static bool CanOfferStationUpgrade(
      int completedShift,
      bool allExperienceCollected,
      CareStationSaveData save)
    {
      return completedShift > 0 && completedShift % 3 == 0 && allExperienceCollected &&
             CareStationShiftRules.HasAvailableUpgrade(save);
    }

    public static bool CanOfferPushAwayFallback(CareStationState state, float elapsedSeconds, float delaySeconds)
    {
      return (state == CareStationState.WaitPushAwayReady ||
              state == CareStationState.WaitPushAway ||
              state == CareStationState.WaitOfflinePushAway ||
              state == CareStationState.WaitCarePushAway) &&
             elapsedSeconds >= Math.Max(0f, delaySeconds);
    }

    public static bool CanOfferReturnFallback(CareStationState state, float elapsedSeconds, float delaySeconds)
    {
      return state == CareStationState.WaitReturnToNeutral &&
             elapsedSeconds >= Math.Max(0f, delaySeconds);
    }

    public static bool RequiresOfflineCollection(int pendingOfflineBottleValue, bool offlineCollectionResolved)
    {
      return pendingOfflineBottleValue > 0 && !offlineCollectionResolved;
    }

    public static bool CanPresentIncident(bool offlineCollectionResolved, bool returnedNeutralAfterOffline)
    {
      return offlineCollectionResolved && returnedNeutralAfterOffline;
    }

    public static bool CanArmCollection(
      CareStationCollectionPhase phase,
      bool careActionCompleted,
      bool returnedNeutralAfterOffline)
    {
      if (phase == CareStationCollectionPhase.Offline) return !careActionCompleted;
      if (phase == CareStationCollectionPhase.Care) return careActionCompleted && returnedNeutralAfterOffline;
      return false;
    }

    public static bool LegacyRandomFlowEnabled(bool careStationMode)
    {
      return !careStationMode;
    }
  }
}
