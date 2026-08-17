using System;
using System.IO;
using UnityEngine;

namespace KeepBlinking.CareStation
{
  public sealed class CareStationSaveService
  {
    public const int CurrentVersion = 15;
    public string SavePath { get; }

    public CareStationSaveService(string savePath = null)
    {
      SavePath = string.IsNullOrWhiteSpace(savePath)
        ? Path.Combine(Application.persistentDataPath, "KeepBlinking", "CareStation", "care_station_v1.json")
        : savePath;
    }

    public CareStationSaveData Load(DateTime utcNow)
    {
      try
      {
        if (File.Exists(SavePath))
        {
          var loaded = JsonUtility.FromJson<CareStationSaveData>(File.ReadAllText(SavePath));
          if (loaded != null)
          {
            Sanitize(loaded, utcNow);
            return loaded;
          }
        }
      }
      catch (Exception exception)
      {
        Debug.LogWarning($"Care Station save could not be loaded: {exception.Message}");
      }

      var created = new CareStationSaveData();
      created.StampActive(utcNow);
      created.StampClaimed(utcNow);
      return created;
    }

    public void Save(CareStationSaveData data, DateTime utcNow)
    {
      if (data == null) return;
      Sanitize(data, utcNow);
      if (utcNow.ToUniversalTime() >= data.ReadLastActiveUtc(utcNow)) data.StampActive(utcNow);
      try
      {
        var directory = Path.GetDirectoryName(SavePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = SavePath + ".tmp";
        File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
        if (File.Exists(SavePath)) File.Delete(SavePath);
        File.Move(temporary, SavePath);
      }
      catch (Exception exception)
      {
        Debug.LogWarning($"Care Station save could not be written: {exception.Message}");
      }
    }

    public void Delete()
    {
      try
      {
        if (File.Exists(SavePath)) File.Delete(SavePath);
        if (File.Exists(SavePath + ".tmp")) File.Delete(SavePath + ".tmp");
      }
      catch (Exception exception)
      {
        Debug.LogWarning($"Care Station save could not be cleared: {exception.Message}");
      }
    }

    private static void Sanitize(CareStationSaveData data, DateTime utcNow)
    {
      var loadedVersion = data.saveVersion;
      if (loadedVersion < 2 && data.selectedUpgrade != CareStationUpgradeId.None)
      {
        data.unlockedUpgradeMask |= CareStationShiftRules.UpgradeBit(data.selectedUpgrade);
        if (data.currentShift >= 3 && data.currentState == CareStationState.AutoShift)
        {
          data.currentShift = 4;
          data.completedShifts = Math.Max(3, data.completedShifts);
        }
      }
      if (loadedVersion < 3)
      {
        // Version 2 used one combined pending/collection count. Preserve any
        // partially collected value against the phase that was active.
        var careHadStarted = data.careActionCompleted ||
                             data.currentState == CareStationState.RepairReveal ||
                             data.currentState == CareStationState.ProduceBottles ||
                             data.currentState == CareStationState.WaitPushAwayReady ||
                             data.currentState == CareStationState.WaitPushAway ||
                             data.currentState == CareStationState.CollectingExperience ||
                             data.currentState == CareStationState.WaitExperienceCollected;
        if (careHadStarted)
        {
          data.collectedCareBottleValue = data.collectedExperienceCount;
          data.offlineCollectionResolved = data.pendingOfflineXP <= 0;
          data.returnedNeutralAfterOffline = data.pendingOfflineXP <= 0;
        }
        else
        {
          data.collectedOfflineBottleValue = data.collectedExperienceCount;
          data.offlineCollectionResolved = data.pendingOfflineXP <= data.collectedOfflineBottleValue;
          data.returnedNeutralAfterOffline = data.offlineCollectionResolved;
        }
      }
      if (loadedVersion < 4)
      {
        data.careShiftId = Math.Max(1, Math.Max(data.currentShift, data.completedShifts + 1));
        if (data.offlinePushAwayCompletion == CareStationPushAwayCompletion.NoOfflineReward)
        {
          data.offlineRewardReason = CareStationPushAwayCompletion.NoOfflineReward;
          data.offlinePushAwayCompletion = CareStationPushAwayCompletion.None;
          // A legacy shift which already passed its no-reward branch remains
          // valid. New shift IDs always use a real SHIFT SUPPLY collection.
          if (data.offlineCollectionResolved)
            data.shiftSupplyGeneratedForShiftId = data.careShiftId;
        }
        var firstGateHasNotStarted = data.currentState == CareStationState.Dormant ||
                                     data.currentState == CareStationState.LoadingSave ||
                                     data.currentState == CareStationState.WelcomeBack ||
                                     data.currentState == CareStationState.AutoShift;
        if (firstGateHasNotStarted && data.pendingOfflineXP <= 0)
        {
          data.offlineCollectionResolved = false;
          data.returnedNeutralAfterOffline = false;
          data.shiftSupplyGeneratedForShiftId = 0;
          CareStationShiftRules.EnsureShiftSupply(data);
        }
      }
      // Version 4 introduced a shift identifier, but older serialized files can
      // deserialize its newly-added field to the class default (1). When the
      // persisted shift is already later, recover the same shift identity rather
      // than creating a new shift or rolling its recipe again.
      if (loadedVersion < 5 && data.careShiftId <= 1 &&
          (data.currentShift > 1 || data.completedShifts > 0))
        data.careShiftId = Math.Max(1, Math.Max(data.currentShift, data.completedShifts + 1));
      if (data.careAction == null) data.careAction = new CareActionSaveData();
      if (loadedVersion < 5)
      {
        MigrateLegacyCareAction(data);
      }
      if (loadedVersion < 6)
      {
        // Legacy session baselines are intentionally not migrated into gesture
        // references. Interrupted distance gates silently capture a fresh origin.
        data.careActionGestureReferenceScale = 0f;
        data.careActionReferenceValid = false;
        data.offlinePushReferenceScale = 0f;
        data.offlinePushReferenceValid = false;
        data.carePushReferenceScale = 0f;
        data.carePushReferenceValid = false;
        if (data.careAction.actionType == CareActionType.FocusShift &&
            data.careAction.stage != CareActionStage.Completed)
        {
          data.careAction.internalPhase = CareActionInternalPhase.FocusReference;
          data.careAction.stage = CareActionStage.Preparing;
          data.careAction.focusTargetStep = 0;
          data.careAction.gestureReferenceScale = 0f;
          data.careAction.gestureReferenceValid = false;
        }
        if (data.currentState == CareStationState.WaitReturnToNeutral &&
            data.pendingReturnPhase == CareStationCollectionPhase.None)
          data.pendingReturnPhase = data.careActionCompleted
            ? CareStationCollectionPhase.Care
            : CareStationCollectionPhase.Offline;
      }
      if (loadedVersion < 8 && data.careAction != null &&
          data.careAction.actionType == CareActionType.FocusShift)
      {
        // Version 7 used one origin plus fixed Near/Far/Return zones. Preserve
        // the completed directional steps while switching the active step to a
        // fresh local reference. The obsolete final Return gate is complete in
        // the four-step model because the second Away was already confirmed.
        switch (data.careAction.internalPhase)
        {
          case CareActionInternalPhase.FocusNearOne:
            data.careAction.focusTargetStep = 0;
            break;
          case CareActionInternalPhase.FocusFarOne:
            data.careAction.focusTargetStep = 1;
            break;
          case CareActionInternalPhase.FocusNearTwo:
            data.careAction.focusTargetStep = 2;
            break;
          case CareActionInternalPhase.FocusFarTwo:
            data.careAction.focusTargetStep = 3;
            break;
          case CareActionInternalPhase.FocusNeutralFinish:
            data.careAction.focusTargetStep = 4;
            data.careAction.stage = CareActionStage.Completed;
            data.careAction.internalPhase = CareActionInternalPhase.FocusFarTwo;
            break;
          default:
            data.careAction.focusTargetStep = Mathf.Clamp(data.careAction.focusTargetStep - 1, 0, 3);
            break;
        }
        if (data.careAction.stage != CareActionStage.Completed)
        {
          data.careAction.internalPhase = CareActionInternalPhase.FocusReference;
          data.careAction.stage = CareActionStage.Preparing;
        }
        data.careAction.gestureReferenceScale = 0f;
        data.careAction.gestureReferenceValid = false;
        data.careAction.distanceDirectionProgress = 0f;
        data.careActionGestureReferenceScale = 0f;
        data.careActionReferenceValid = false;
      }
      if (loadedVersion < CurrentVersion)
      {
        // Recipe-capable legacy versions can still contain an empty recipe
        // while carrying pre-recipe shift progress. Preserve any serialized
        // recipe, and reconstruct only the missing compatibility state.
        MigrateLegacyRecipe(data);
      }
      if (loadedVersion < 10)
      {
        if (data.currentState == CareStationState.ShiftComplete)
        {
          data.careShiftCompleted = true;
          data.shiftCompleteRewardsShown = true;
        }
        else if (data.currentState == CareStationState.AutoShift)
        {
          // v9 advanced the identifiers before entering AutoShift. Reuse that
          // already-reserved identity when the next valid offline interval ends.
          data.careShiftCompleted = true;
          data.shiftCompleteRewardsShown = true;
          data.endShiftConsumed = true;
          data.autoShiftEntered = true;
          data.nextShiftPrepared = true;
        }
      }
      if (loadedVersion < 11)
      {
        // Legacy routes were one-shot. A previously owned route becomes Level 2;
        // untouched routes remain Level 1. Preserve every already stored result.
        data.workerLevel = (data.unlockedUpgradeMask & CareStationShiftRules.UpgradeBit(CareStationUpgradeId.MoreWorkers)) != 0 ? 2 : 1;
        data.storageLevel = (data.unlockedUpgradeMask & CareStationShiftRules.UpgradeBit(CareStationUpgradeId.LargerStorage)) != 0 ? 2 : 1;
        data.cartLevel = (data.unlockedUpgradeMask & CareStationShiftRules.UpgradeBit(CareStationUpgradeId.BiggerCart)) != 0 ? 2 : 1;
        data.storedFullBottles = Math.Max(data.shiftStoredFullBottles, data.collectedExperienceCount);
        data.storedGoldBottles = Math.Max(0, data.shiftStoredGoldBottles);
      }
      if (loadedVersion < 12)
      {
        data.offlineProductionPausedByFullStorage =
          CareStationStorageRules.Stored(data) >= CareStationStorageRules.Capacity(data);
        data.discardedOfflineBottleCount = 0;
        data.careCollectionReleased = data.carePushAwayCompletion != CareStationPushAwayCompletion.None;
      }
      if (loadedVersion < 13)
      {
        data.inspectionTriggered = false;
        data.inspectionActive = false;
        data.inspectionCurrentCheck = 0;
        data.inspectionCompletedMask = 0;
        data.inspectionRewardProduced = false;
        data.inspectionRewardStored = false;
        data.inspectionCompleted = false;
        data.inspectionCompletionSignalSent = false;
        data.stationLevel = 1;
      }
      if (loadedVersion < 14)
      {
        // Subjective checks did not exist before v14. Missing responses remain
        // explicitly missing; never synthesize zero-valued answers.
        data.preCareScores = new CareSubjectiveScores();
        data.postCareScores = new CareSubjectiveScores();
        data.careReportShown = false;
        data.careReportConsumed = false;
        data.researchSessionExported = false;
        data.sessionActiveCareSeconds = Mathf.Max(0f, data.sessionActiveCareSeconds);
        data.sessionClosedEyeSeconds = Mathf.Max(0f, data.sessionClosedEyeSeconds);
        data.sessionFocusShiftCompletions = Mathf.Max(0, data.sessionFocusShiftCompletions);
        data.sessionTrackingLostCount = Mathf.Max(0, data.sessionTrackingLostCount);
        data.sessionTrackingLostSeconds = Mathf.Max(0f, data.sessionTrackingLostSeconds);
      }
      if (loadedVersion < 15)
      {
        // Offline output is now transported by the crew automatically. Only
        // the not-yet-arrived legacy portion is migrated; already collected
        // value remains in storage and can never be counted twice.
        var legacyRemaining = Math.Max(0, data.pendingOfflineXP - data.collectedOfflineBottleValue) +
                              Math.Max(0, data.queuedOfflineXP);
        // Existing care rewards have priority over unattended output. This is
        // especially important for a v14 save interrupted mid-flight: legacy
        // offline bottles must not fill the slots required to resume that care
        // collection.
        var accepted = Math.Min(legacyRemaining, CareStationStorageRules.RemainingForAutomaticOfflineSettlement(data));
        data.storedFullBottles += accepted;
        data.queuedOfflineXP = Math.Max(0, legacyRemaining - accepted);
        data.pendingOfflineXP = 0;
        data.collectedOfflineBottleValue = 0;
        data.offlineCollectionResolved = true;
        data.returnedNeutralAfterOffline = false;
        data.offlinePushAwayCompletion = CareStationPushAwayCompletion.None;
        data.offlineReturnCompletion = CareStationReturnCompletion.None;
        data.offlineRewardReason = CareStationPushAwayCompletion.None;
        data.distanceResetReferenceScale = 0f;
        data.distanceResetReferenceValid = false;
        data.distanceResetAwayScale = 0f;
        data.distanceResetAwayCompleted = false;
        data.distanceResetCompleted = false;
        data.offlineSummaryConsumed = false;
        if (data.currentState == CareStationState.PresentOfflineBottles ||
            data.currentState == CareStationState.WaitOfflinePushAway ||
            data.currentState == CareStationState.CollectingOfflineBottles ||
            data.currentState == CareStationState.WaitOfflineBottlesStored)
          data.currentState = CareStationState.WaitDistanceResetMoveAway;
        data.collectedExperienceCount = data.storedFullBottles + data.storedGoldBottles;
      }
      data.saveVersion = CurrentVersion;
      data.currentShift = Mathf.Max(1, data.currentShift);
      data.careShiftId = Mathf.Max(1, data.careShiftId);
      data.pendingOfflineXP = Mathf.Max(0, data.pendingOfflineXP);
      data.queuedOfflineXP = Mathf.Max(0, data.queuedOfflineXP);
      data.pendingIncidentXP = Mathf.Max(0, data.pendingIncidentXP);
      data.careActionElapsed = Mathf.Max(0f, data.careActionElapsed);
      data.collectedExperienceCount = Mathf.Max(0, data.collectedExperienceCount);
      data.stationConstructionState = Mathf.Max(0, data.stationConstructionState);
      data.workerLevel = Mathf.Clamp(data.workerLevel, 1, CareStationUpgradeConfiguration.MaximumLevel);
      data.storageLevel = Mathf.Clamp(data.storageLevel, 1, CareStationUpgradeConfiguration.MaximumLevel);
      data.cartLevel = Mathf.Clamp(data.cartLevel, 1, CareStationUpgradeConfiguration.MaximumLevel);
      data.storedFullBottles = Mathf.Max(0, data.storedFullBottles);
      data.storedGoldBottles = Mathf.Max(0, data.storedGoldBottles);
      data.discardedOfflineBottleCount = Mathf.Max(0, data.discardedOfflineBottleCount);
      data.inspectionCurrentCheck = Mathf.Clamp(data.inspectionCurrentCheck, 0, 4);
      data.inspectionCompletedMask &= CareStationInspectionRules.AllChecks;
      data.stationLevel = Mathf.Clamp(data.stationLevel, 1, 2);
      if (data.preCareScores == null) data.preCareScores = new CareSubjectiveScores();
      if (data.postCareScores == null) data.postCareScores = new CareSubjectiveScores();
      data.preCareScores.Sanitize();
      data.postCareScores.Sanitize();
      data.sessionActiveCareSeconds = Mathf.Max(0f, data.sessionActiveCareSeconds);
      data.sessionClosedEyeSeconds = Mathf.Clamp(data.sessionClosedEyeSeconds, 0f, data.sessionActiveCareSeconds);
      data.sessionFocusShiftCompletions = Mathf.Max(0, data.sessionFocusShiftCompletions);
      data.sessionTrackingLostCount = Mathf.Max(0, data.sessionTrackingLostCount);
      data.sessionTrackingLostSeconds = Mathf.Max(0f, data.sessionTrackingLostSeconds);
      data.lastOfflineStoredFullBottles = Mathf.Max(0, data.lastOfflineStoredFullBottles);
      data.lastOfflineStoredGoldBottles = Mathf.Max(0, data.lastOfflineStoredGoldBottles);
      data.lastOfflineWorkedSeconds = Mathf.Max(0f, data.lastOfflineWorkedSeconds);
      if (!CareDistanceReferenceSampler.IsValidScale(data.distanceResetReferenceScale))
      {
        data.distanceResetReferenceScale = 0f;
        data.distanceResetReferenceValid = false;
      }
      if (!CareDistanceReferenceSampler.IsValidScale(data.distanceResetAwayScale))
      {
        data.distanceResetAwayScale = 0f;
        data.distanceResetAwayCompleted = false;
      }
      CareStationShiftRules.SynchronizeUpgradeValues(data, new CareStationUpgradeConfiguration());
      data.completedShifts = Mathf.Max(0, data.completedShifts);
      data.unlockedUpgradeMask &= CareStationShiftRules.AllUpgradeMask;
      data.pendingGoldBottleCount = Mathf.Clamp(data.pendingGoldBottleCount, 0, Mathf.Max(1, data.pendingIncidentXP));
      data.collectedOfflineBottleValue = Mathf.Clamp(data.collectedOfflineBottleValue, 0, data.pendingOfflineXP);
      data.collectedCareBottleValue = Mathf.Clamp(data.collectedCareBottleValue, 0, data.pendingIncidentXP);
      data.shiftSupplyGeneratedForShiftId = Mathf.Max(0, data.shiftSupplyGeneratedForShiftId);
      if (data.pendingReturnPhase != CareStationCollectionPhase.None &&
          data.pendingReturnPhase != CareStationCollectionPhase.Offline &&
          data.pendingReturnPhase != CareStationCollectionPhase.Care)
        data.pendingReturnPhase = CareStationCollectionPhase.None;
      if (!Enum.IsDefined(typeof(CareStationReturnCompletion), data.offlineReturnCompletion))
        data.offlineReturnCompletion = CareStationReturnCompletion.None;
      if (!Enum.IsDefined(typeof(CareStationReturnCompletion), data.careReturnCompletion))
        data.careReturnCompletion = CareStationReturnCompletion.None;
      if (!Enum.IsDefined(typeof(CareDistanceFallbackReason), data.offlineAwayFallbackReason))
        data.offlineAwayFallbackReason = CareDistanceFallbackReason.None;
      if (!Enum.IsDefined(typeof(CareDistanceFallbackReason), data.offlineCloserFallbackReason))
        data.offlineCloserFallbackReason = CareDistanceFallbackReason.None;
      if (!Enum.IsDefined(typeof(CareDistanceFallbackReason), data.careAwayFallbackReason))
        data.careAwayFallbackReason = CareDistanceFallbackReason.None;
      if (!Enum.IsDefined(typeof(CareDistanceFallbackReason), data.careCloserFallbackReason))
        data.careCloserFallbackReason = CareDistanceFallbackReason.None;
      SanitizeReference(ref data.careActionGestureReferenceScale, ref data.careActionReferenceValid);
      SanitizeReference(ref data.offlinePushReferenceScale, ref data.offlinePushReferenceValid);
      SanitizeReference(ref data.carePushReferenceScale, ref data.carePushReferenceValid);
      SanitizeCareAction(data.careAction);
      data.trainingProgress = Mathf.Clamp(data.trainingProgress, 0, 4);
      data.formalRecipesCreated = Mathf.Max(0, data.formalRecipesCreated);
      data.focusShiftCooldownUntilShiftId = Mathf.Max(0, data.focusShiftCooldownUntilShiftId);
      data.guidedEyeCirclesCooldownUntilShiftId = Mathf.Max(0, data.guidedEyeCirclesCooldownUntilShiftId);
      data.shiftStoredFullBottles = Mathf.Max(0, data.shiftStoredFullBottles);
      data.shiftStoredGoldBottles = Mathf.Max(0, data.shiftStoredGoldBottles);
      if (!Enum.IsDefined(typeof(CareActionType), data.replacedOriginalAction))
        data.replacedOriginalAction = CareActionType.None;
      if (!Enum.IsDefined(typeof(CareActionType), data.replacedWithAction))
        data.replacedWithAction = CareActionType.None;
      if (!Enum.IsDefined(typeof(CareActionPauseReason), data.replacementPauseReason))
        data.replacementPauseReason = CareActionPauseReason.None;
      if (data.eventHistory == null) data.eventHistory = Array.Empty<CareStationEventRecord>();
      if (data.eventHistory.Length > 64)
      {
        var trimmedEvents = new CareStationEventRecord[64];
        Array.Copy(data.eventHistory, data.eventHistory.Length - 64, trimmedEvents, 0, 64);
        data.eventHistory = trimmedEvents;
      }
      if (data.currentState == CareStationState.AutoShift)
      {
        data.careShiftCompleted = true;
        data.endShiftConsumed = true;
        data.autoShiftEntered = true;
      }
      else if (data.currentState == CareStationState.ShiftComplete)
      {
        data.careShiftCompleted = true;
        data.shiftCompleteRewardsShown = true;
        data.endShiftConsumed = false;
        data.autoShiftEntered = false;
      }
      if (!data.careStepChangePending) data.replacementPauseReason = data.careStepWasReplaced
        ? data.replacementPauseReason
        : CareActionPauseReason.None;
      if (data.recentRecipeHistory == null) data.recentRecipeHistory = Array.Empty<string>();
      if (data.recentRecipeHistory.Length > 3)
      {
        var trimmed = new string[3];
        Array.Copy(data.recentRecipeHistory, data.recentRecipeHistory.Length - 3, trimmed, 0, 3);
        data.recentRecipeHistory = trimmed;
      }
      if (data.currentRecipe == null) data.currentRecipe = new CareRecipeSaveData();
      CareRecipeGenerator.SanitizeRecipe(data.currentRecipe);
      if (data.careAction.actionType == CareActionType.FocusShift)
      {
        data.careAction.gestureReferenceScale = data.careActionGestureReferenceScale;
        data.careAction.gestureReferenceValid = data.careActionReferenceValid;
      }
      if (string.IsNullOrWhiteSpace(data.lastActiveUtc)) data.StampActive(utcNow);
      if (string.IsNullOrWhiteSpace(data.lastClaimedUtc)) data.StampClaimed(utcNow);
    }

    private static void MigrateLegacyRecipe(CareStationSaveData data)
    {
      // Older Care Station builds completed Screen Down and Closed-Eye Rest
      // as real care work. Credit only those known training steps; do not infer
      // unperformed Focus Shift or Guided Eye Circles training.
      data.trainingProgress = Mathf.Clamp(Mathf.Max(data.trainingProgress, Math.Min(data.completedShifts, 2)), 0, 4);
      if (data.careAction != null)
      {
        if (data.careAction.actionType == CareActionType.FocusShift)
          data.trainingProgress = Math.Max(data.trainingProgress, 3);
        else if (data.careAction.actionType == CareActionType.GuidedEyeCircles)
          data.trainingProgress = 4;
      }

      if (data.currentRecipe == null) data.currentRecipe = new CareRecipeSaveData();
      if (data.currentRecipe.ActionCount > 0) return;
      if (!IsRecipeFlowState(data.currentState)) return;

      var action = data.careAction != null && data.careAction.actionType != CareActionType.None
        ? data.careAction.actionType
        : data.selectedIncident == CareStationIncidentType.DrySpot
          ? CareActionType.ClosedEyeRest
          : CareActionType.ScreenDown;
      var completed = data.careActionCompleted || IsPostActionState(data.currentState);
      data.currentRecipe = new CareRecipeSaveData
      {
        recipeId = $"legacy_shift_{Math.Max(1, data.careShiftId)}_{action}",
        recipeSeed = 0,
        recipeType = CareRecipeType.Single,
        actionList = new[] { action },
        currentActionIndex = completed ? 1 : 0,
        completedActionMask = completed ? 1 : 0,
        createdShiftId = Math.Max(1, data.careShiftId),
        recipeCompleted = completed,
        completionSignalSent = completed,
        completionConsumed = IsPostActionState(data.currentState),
      };
    }

    private static bool IsRecipeFlowState(CareStationState state)
    {
      return state == CareStationState.PromptCareAction ||
             state == CareStationState.WaitCareActionStart ||
             state == CareStationState.CareActionInProgress ||
             state == CareStationState.CareActionPaused ||
             state == CareStationState.CareActionCompleted ||
             IsPostActionState(state);
    }

    private static bool IsPostActionState(CareStationState state)
    {
      return state == CareStationState.RepairReveal ||
             state == CareStationState.ProduceBottles ||
             state == CareStationState.PresentCareBottles ||
             state == CareStationState.WaitCarePushAway ||
             state == CareStationState.WaitPushAwayReady ||
             state == CareStationState.WaitPushAway ||
             state == CareStationState.CollectingExperience ||
             state == CareStationState.WaitExperienceCollected ||
             state == CareStationState.CollectingCareBottles ||
             state == CareStationState.WaitCareBottlesStored ||
             state == CareStationState.UpgradeSelection ||
             state == CareStationState.ShiftComplete;
    }

    private static void MigrateLegacyCareAction(CareStationSaveData data)
    {
      var inAction = data.currentState == CareStationState.PromptCareAction ||
                     data.currentState == CareStationState.WaitCareActionStart ||
                     data.currentState == CareStationState.CareActionInProgress ||
                     data.currentState == CareStationState.CareActionPaused ||
                     data.currentState == CareStationState.CareActionCompleted;
      if (!inAction)
      {
        data.careAction.Reset();
        return;
      }

      data.careAction.actionType = data.selectedIncident == CareStationIncidentType.DrySpot
        ? CareActionType.ClosedEyeRest
        : CareActionType.ScreenDown;
      data.careAction.elapsedSeconds = Mathf.Max(0f, data.careActionElapsed);
      data.careAction.stage = data.currentState == CareStationState.CareActionCompleted
        ? CareActionStage.Completed
        : data.currentState == CareStationState.CareActionPaused
          ? CareActionStage.Paused
          : data.currentState == CareStationState.CareActionInProgress
            ? CareActionStage.Active
            : CareActionStage.WaitingForStart;
      data.careAction.pauseReason = data.currentState == CareStationState.CareActionPaused
        ? (data.selectedIncident == CareStationIncidentType.DrySpot
          ? CareActionPauseReason.EyesOpen
          : CareActionPauseReason.ScreenReturned)
        : CareActionPauseReason.None;
      data.careAction.internalPhase = data.selectedIncident == CareStationIncidentType.DrySpot
        ? (data.currentState == CareStationState.CareActionCompleted
          ? CareActionInternalPhase.ClosedEyeWaitReopen
          : data.careActionElapsed > 0f
            ? CareActionInternalPhase.ClosedEyeActive
            : CareActionInternalPhase.ClosedEyePrompt)
        : (data.currentState == CareStationState.CareActionCompleted
          ? CareActionInternalPhase.ScreenDownReturn
          : data.careActionElapsed > 0f
            ? CareActionInternalPhase.ScreenDownRest
            : CareActionInternalPhase.ScreenDownWait);
      data.careAction.completionSignalEmitted = data.currentState == CareStationState.CareActionCompleted;
    }

    private static void SanitizeCareAction(CareActionSaveData action)
    {
      if (action == null) return;
      action.elapsedSeconds = Mathf.Max(0f, action.elapsedSeconds);
      action.phaseElapsedSeconds = Mathf.Max(0f, action.phaseElapsedSeconds);
      action.holdElapsedSeconds = Mathf.Max(0f, action.holdElapsedSeconds);
      action.focusTargetStep = Mathf.Clamp(action.focusTargetStep, 0, 4);
      action.distanceDirectionProgress = Mathf.Clamp01(action.distanceDirectionProgress);
      if (!Enum.IsDefined(typeof(CareDistanceFallbackReason), action.distanceFallbackReason))
        action.distanceFallbackReason = CareDistanceFallbackReason.None;
      if (!Enum.IsDefined(typeof(CareActionCompletionSource), action.completionSource))
        action.completionSource = CareActionCompletionSource.None;
      action.guidedStage = Mathf.Clamp(action.guidedStage, 0, 7);
      SanitizeReference(ref action.gestureReferenceScale, ref action.gestureReferenceValid);
      if (!Enum.IsDefined(typeof(CareActionType), action.actionType)) action.Reset();
    }

    private static void SanitizeReference(ref float scale, ref bool valid)
    {
      valid = valid && CareDistanceReferenceSampler.IsValidScale(scale);
      if (valid) return;
      scale = 0f;
      valid = false;
    }
  }
}
