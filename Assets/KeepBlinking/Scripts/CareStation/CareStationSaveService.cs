using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace KeepBlinking.CareStation
{
  public sealed class CareStationSaveService
  {
    public const int CurrentVersion = 24;
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
            MigrateStaleUiStateAfterLoad(loaded);
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
      // JsonUtility leaves explicitly-null reference fields null. Several
      // migrations inspect the recipe before the common sanitization block at
      // the end of this method, so restore the required object immediately.
      if (data.currentRecipe == null) data.currentRecipe = new CareRecipeSaveData();
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
      if (loadedVersion < 9)
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
      if (loadedVersion < 16)
      {
        // v16 changes the care language and action cadence without replacing an
        // in-progress shift. The serialized enum values for ScreenDown and
        // GuidedEyeCircles remain stable; only their player-facing names change.
        // An already-running legacy action has effectively passed the new
        // routine opening card, so resume it directly instead of replaying an
        // input-blocking introduction after a reload.
        if (IsRecipeFlowState(data.currentState) && data.currentRecipe != null)
          data.currentRecipe.routineIntroCompleted = true;

        if (data.currentRecipe != null && data.currentRecipe.ActionCount > 0)
        {
          data.currentRecipe.deepRest =
            CareActionLibrary.EstimatedRecipeSeconds(data.currentRecipe.actionList, false) <
            CareActionLibrary.MinimumFormalRoutineSeconds &&
            CareActionLibrary.EstimatedRecipeSeconds(data.currentRecipe.actionList, true) <=
            CareActionLibrary.MaximumFormalRoutineSeconds;
        }

        // Old Focus Shift checkpoints used a different threshold and step
        // model. Preserve the recipe and shift, but safely restart only this
        // current action against the immutable Session baseline.
        if (data.careAction != null &&
            data.careAction.actionType == CareActionType.FocusShift &&
            data.careAction.stage != CareActionStage.Completed)
        {
          var sessionBaseline = data.careActionGestureReferenceScale;
          var sessionBaselineValid = data.careActionReferenceValid &&
                                     CareDistanceReferenceSampler.IsValidScale(sessionBaseline);
          data.careAction.Reset();
          data.careAction.actionType = CareActionType.FocusShift;
          data.careAction.internalPhase = CareActionInternalPhase.FocusReference;
          data.careAction.stage = CareActionStage.Preparing;
          data.careAction.gestureReferenceScale = sessionBaselineValid ? sessionBaseline : 0f;
          data.careAction.gestureReferenceValid = sessionBaselineValid;
        }
      }
      if (loadedVersion < 17)
      {
        MigrateRetiredBlinkReset(data, loadedVersion);
      }
      if (loadedVersion < 18)
      {
        // v18 separates rare Gold Bottles from the physical Full Bottle rack
        // and makes unaffordable upgrade opportunities deferrable. Inventory,
        // recipe, collection and shift progress remain untouched.
        data.upgradeDeferred = false;
      }
      if (loadedVersion < 19)
      {
        MigrateFinalCareActionLibrary(data);
      }
      if (loadedVersion < 20)
      {
        // v20 adds resumable, one-shot narration bookkeeping. Legacy actions
        // keep all timing and progress; only narration starts with safe defaults.
        ResetLegacyVoiceState(data.careAction);
        ResetLegacyVoiceState(data.currentRecipe?.deferredActionSnapshot);
      }
      if (loadedVersion < 21)
      {
        MigrateEconomyV21(data);
      }
      if (loadedVersion < 22)
      {
        MigrateProductionV22(data);
      }
      if (loadedVersion < 23)
      {
        MigrateTransportV23(data);
      }
      if (loadedVersion < 24)
      {
        MigrateRoutineV24(data);
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
      data.careEnergy = Mathf.Max(0, data.careEnergy);
      data.coins = Mathf.Max(0, data.coins);
      data.pendingFullBottleShipment = Mathf.Clamp(data.pendingFullBottleShipment, 0, 1);
      if (!Enum.IsDefined(typeof(CareProductionStage), data.productionStage))
        data.productionStage = CareProductionStage.None;
      data.productionStageElapsedSeconds = Mathf.Max(0f, data.productionStageElapsedSeconds);
      data.productionCycleId = Mathf.Max(0, data.productionCycleId);
      data.productionCycleSourceRecipeId = data.productionCycleSourceRecipeId ?? string.Empty;
      data.lastForegroundProductionRecipeId = data.lastForegroundProductionRecipeId ?? string.Empty;
      if (data.productionCycleStored && data.productionStage != CareProductionStage.None)
      {
        data.productionStage = CareProductionStage.None;
        data.productionStageElapsedSeconds = 0f;
      }
      data.pendingPremiumShipment = Mathf.Max(0, data.pendingPremiumShipment);
      data.lastCartFullBottlesSold = Mathf.Max(0, data.lastCartFullBottlesSold);
      data.lastCartPremiumBottlesSold = Mathf.Max(0, data.lastCartPremiumBottlesSold);
      data.lastCartCoinsEarned = Mathf.Max(0, data.lastCartCoinsEarned);
      data.lastAutoProducedBottles = Mathf.Max(0, data.lastAutoProducedBottles);
      data.lastCartSettlementId = data.lastCartSettlementId ?? string.Empty;
      // These fields are read only by the v21 migration above. Keeping them
      // empty prevents any legacy presentation from becoming an economy source.
      data.storedGoldBottles = 0;
      data.pendingIncidentXP = 0;
      data.pendingGoldBottleCount = 0;
      data.selectedIncident = CareStationIncidentType.None;
      data.discardedOfflineBottleCount = Mathf.Max(0, data.discardedOfflineBottleCount);
      data.inspectionCurrentCheck = Mathf.Clamp(data.inspectionCurrentCheck, 0, 4);
      data.inspectionCompletedMask &= CareStationInspectionRules.AllChecks;
      data.stationLevel = Mathf.Clamp(data.stationLevel, 1, 2);
      CareProductionTransportRules.Synchronize(data);
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
      if (loadedVersion < 18)
        data.offlineProductionPausedByFullStorage = CareStationStorageRules.Remaining(data) <= 0;
      data.completedShifts = Mathf.Max(0, data.completedShifts);
      data.unlockedUpgradeMask &= CareStationShiftRules.AllUpgradeMask;
      data.pendingGoldBottleCount = 0;
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
      if (data.currentRecipe.deferredActionSnapshot == null)
        data.currentRecipe.deferredActionSnapshot = new CareActionSaveData();
      SanitizeCareAction(data.currentRecipe.deferredActionSnapshot);
      if (CareActionLibrary.IsRetiredTask(data.currentRecipe.deferredActionSnapshot.actionType))
        data.currentRecipe.deferredActionSnapshot.Reset();
      data.completedTrainingActionMask &= CareRecipeGenerator.AllTrainingActionMask;
      data.trainingProgress = CareRecipeGenerator.CompletedTrainingCount(data.completedTrainingActionMask);
      data.formalRecipesCreated = Mathf.Max(0, data.formalRecipesCreated);
      data.careRoutinesCreated = Mathf.Max(0, data.careRoutinesCreated);
      if (!Enum.IsDefined(typeof(CareRoutineId), data.lastCompletedRoutineId))
        data.lastCompletedRoutineId = CareRoutineId.None;
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
      data.currentRecipe.careEnergyGrantedAmount = Mathf.Max(0, data.currentRecipe.careEnergyGrantedAmount);
      if (data.careAction.actionType == CareActionType.FocusShift)
      {
        data.careAction.gestureReferenceScale = data.careActionGestureReferenceScale;
        data.careAction.gestureReferenceValid = data.careActionReferenceValid;
      }
      if (string.IsNullOrWhiteSpace(data.lastActiveUtc)) data.StampActive(utcNow);
      if (string.IsNullOrWhiteSpace(data.lastClaimedUtc)) data.StampClaimed(utcNow);
    }

    private static void MigrateEconomyV21(CareStationSaveData data)
    {
      if (data == null) return;

      var oldPending = Math.Max(0, data.pendingIncidentXP);
      var oldCollected = Math.Max(0, data.collectedCareBottleValue);
      var oldIncidentActive = data.selectedIncident != CareStationIncidentType.None &&
                              !data.careShiftCompleted &&
                              (data.currentState == CareStationState.PresentIncident ||
                               data.currentState == CareStationState.WaitIncidentSelection ||
                               IsRecipeFlowState(data.currentState));
      if (oldPending <= 0 && oldIncidentActive)
        oldPending = CareStationShiftRules.IncidentExperience(data.selectedIncident);
      var pendingCareEnergy = Math.Max(0, oldPending - oldCollected);
      data.careEnergy = Math.Max(0, data.careEnergy) + pendingCareEnergy;

      // Gold was an inventory/upgrade currency. Its sole v21 meaning is an
      // unsold Premium product; the shift count was already a subset of the
      // stored wallet and must not be added a second time.
      data.pendingPremiumShipment = Math.Max(0, data.pendingPremiumShipment) +
                                    Math.Max(0, data.storedGoldBottles) +
                                    Math.Max(0, data.pendingGoldBottleCount);

      if (data.currentRecipe != null && (oldPending > 0 || data.currentRecipe.recipeCompleted))
      {
        data.currentRecipe.careEnergyGranted = true;
        data.currentRecipe.careEnergyGrantedAmount = pendingCareEnergy;
      }

      data.pendingIncidentXP = 0;
      data.collectedCareBottleValue = 0;
      data.pendingGoldBottleCount = 0;
      data.storedGoldBottles = 0;
      data.shiftStoredGoldBottles = 0;
      data.selectedIncident = CareStationIncidentType.None;
      data.firstFormalGoldBottleGenerated = false;
      data.inspectionRewardProduced = false;
      data.inspectionRewardStored = false;
      data.careCollectionReleased = false;
      data.pendingFullBottleShipment = 0;
      data.lastCartSettlementId = string.Empty;
      data.lastCartFullBottlesSold = 0;
      data.lastCartPremiumBottlesSold = 0;
      data.lastCartCoinsEarned = 0;
      data.lastAutoProducedBottles = 0;
      data.collectedExperienceCount = Math.Max(0, data.storedFullBottles);

      if (data.currentState == CareStationState.PresentIncident ||
          data.currentState == CareStationState.WaitIncidentSelection)
        data.currentState = CareStationState.StationWorking;

      if (data.currentState == CareStationState.ProduceBottles ||
          data.currentState == CareStationState.PresentCareBottles ||
          data.currentState == CareStationState.WaitCarePushAway ||
          data.currentState == CareStationState.WaitPushAwayReady ||
          data.currentState == CareStationState.WaitPushAway ||
          data.currentState == CareStationState.CollectingExperience ||
          data.currentState == CareStationState.WaitExperienceCollected ||
          data.currentState == CareStationState.CollectingCareBottles ||
          data.currentState == CareStationState.WaitCareBottlesStored ||
          data.currentState == CareStationState.WaitStorageSpace)
        data.currentState = data.currentRecipe != null && data.currentRecipe.recipeCompleted
          ? CareStationState.RepairReveal
          : CareStationState.StationWorking;

      if (data.currentState == CareStationState.UpgradeSelection)
      {
        data.upgradeOffered = true;
        data.upgradeDeferred = true;
        data.currentState = CareStationState.StationWorking;
      }
      data.activeCollectionPhase = CareStationCollectionPhase.None;
      data.pendingReturnPhase = CareStationCollectionPhase.None;
      data.carePushAwayCompletion = CareStationPushAwayCompletion.None;
      data.careReturnCompletion = CareStationReturnCompletion.None;
      data.pushAwayCompleted = false;
    }

    private static void MigrateProductionV22(CareStationSaveData data)
    {
      if (data == null) return;

      if (data.pendingFullBottleShipment > 0)
      {
        data.productionCycleId = Math.Max(1, data.productionCycleId);
        data.productionStage = CareProductionStage.TransferToStorage;
        data.productionStageElapsedSeconds = 0f;
        data.productionCycleEnergyConsumed = true;
        data.productionCycleStored = false;
        data.productionCycleSourceRecipeId = data.currentRecipe?.recipeId ?? string.Empty;
        data.lastForegroundProductionRecipeId = data.productionCycleSourceRecipeId;
      }
      data.pendingFullBottleShipment = 0;

      var legacyBottleFlow = data.currentState == CareStationState.PresentCareBottles ||
                             data.currentState == CareStationState.WaitCarePushAway ||
                             data.currentState == CareStationState.WaitPushAwayReady ||
                             data.currentState == CareStationState.WaitPushAway ||
                             data.currentState == CareStationState.CollectingExperience ||
                             data.currentState == CareStationState.WaitExperienceCollected ||
                             data.currentState == CareStationState.CollectingCareBottles ||
                             data.currentState == CareStationState.WaitCareBottlesStored;
      if (legacyBottleFlow)
        data.currentState = data.productionStage != CareProductionStage.None
          ? CareStationState.ProduceBottles
          : CareStationState.RepairReveal;
      if (data.currentState == CareStationState.WaitStorageSpace)
        data.currentState = data.productionStage != CareProductionStage.None
          ? CareStationState.ProduceBottles
          : CareStationState.PostCareCheck;
      if (data.currentState == CareStationState.WaitReturnToNeutral &&
          data.pendingReturnPhase == CareStationCollectionPhase.Care)
        data.currentState = CareStationState.PostCareCheck;

      data.activeCollectionPhase = CareStationCollectionPhase.None;
      data.pendingReturnPhase = CareStationCollectionPhase.None;
      data.careCollectionReleased = false;
      data.carePushAwayCompletion = CareStationPushAwayCompletion.None;
      data.careReturnCompletion = CareStationReturnCompletion.None;
      data.pushAwayCompleted = false;
      data.pushAwayCompletion = CareStationPushAwayCompletion.None;
      data.collectedCareBottleValue = 0;
    }

    private static void MigrateTransportV23(CareStationSaveData data)
    {
      if (data == null) return;
      data.productionTransportMode = CareProductionTransportRules.HasBasicAutomationMilestone(data)
        ? CareProductionTransportMode.BasicConveyor
        : CareProductionTransportMode.ManualCarry;
      // A pre-v23 L2 save already crossed its milestone before one-shot
      // presentation existed. Adopt the correct transport without replaying an
      // unlock banner on every load.
      data.basicConveyorUnlockPresented =
        data.productionTransportMode >= CareProductionTransportMode.BasicConveyor;
    }

    private static void MigrateRoutineV24(CareStationSaveData data)
    {
      if (data == null) return;
      var recipe = data.currentRecipe;
      var hadRecipe = recipe != null && recipe.ActionCount > 0;
      var experienced = hadRecipe || data.trainingProgress > 0 || data.completedTrainingActionMask != 0 ||
                        data.formalRecipesCreated > 0 || data.completedShifts > 0;
      data.careRoutinesCreated = experienced ? Math.Max(4, data.formalRecipesCreated) : 0;
      data.lastCompletedRoutineId = CareRoutineId.None;
      if (!hadRecipe) return;

      CareRecipeGenerator.RemoveRetiredBlinkReset(recipe, false);
      if (!recipe.recipeCompleted) EnsureCompatibleFinalRest(recipe);
      var routineId = InferRoutineId(recipe.actionList);
      ApplyRoutineV24Parameters(recipe, routineId, data.inspectionActive || recipe.recipeType == CareRecipeType.Inspection);
      if (recipe.recipeCompleted && recipe.routineId >= CareRoutineId.FocusFlow &&
          recipe.routineId <= CareRoutineId.FullCare)
        data.lastCompletedRoutineId = recipe.routineId;
      else if (data.recentRecipeHistory != null && data.recentRecipeHistory.Length > 0)
        data.lastCompletedRoutineId = InferRoutineIdFromSignature(
          data.recentRecipeHistory[data.recentRecipeHistory.Length - 1]);

      var oldRewardAlreadySettled = recipe.careEnergyGranted;
      recipe.plannedSlotCount = Mathf.Clamp(recipe.ActionCount, 1, 4);
      recipe.plannedSlotRewards = EvenRoutineRewards(recipe.plannedSlotCount);
      recipe.rewardSlotMasks = Enumerable.Range(0, recipe.ActionCount).Select(index => 1 << index).ToArray();
      recipe.rewardedStepMask = oldRewardAlreadySettled ? (1 << recipe.plannedSlotCount) - 1 : 0;
      recipe.careEnergyRewardedTotal = oldRewardAlreadySettled
        ? CareEconomyConfiguration.DefaultRoutineCareEnergy
        : 0;
      recipe.careEnergyGranted = oldRewardAlreadySettled;
      recipe.careEnergyGrantedAmount = recipe.careEnergyRewardedTotal;
      CareRecipeGenerator.SanitizeRecipe(recipe);

      // Pre-v24 steps were rewarded only at Recipe completion. Credit completed
      // but unsettled slots once during migration, while a previously settled
      // 12/24/36 reward remains untouched and can never be paid again.
      if (!oldRewardAlreadySettled)
        CareEconomyRules.TryGrantAllCompletedRecipeSteps(data, out _);
    }

    private static void EnsureCompatibleFinalRest(CareRecipeSaveData recipe)
    {
      if (recipe?.actionList == null || recipe.actionList.Length == 0) return;
      var actions = recipe.actionList.ToList();
      var originals = (recipe.originalActionList != null &&
                       recipe.originalActionList.Length == recipe.actionList.Length
        ? recipe.originalActionList
        : recipe.actionList).ToList();
      var pilot = actions.IndexOf(CareActionType.PilotEyeRoutine);
      if (pilot >= 0 && (pilot + 1 >= actions.Count || actions[pilot + 1] != CareActionType.GuidedEyeCircles) &&
          actions.Count < 4)
      {
        actions.Insert(pilot + 1, CareActionType.GuidedEyeCircles);
        originals.Insert(pilot + 1, CareActionType.GuidedEyeCircles);
        recipe.completedActionMask = InsertEmptyMaskBit(recipe.completedActionMask, pilot + 1);
        recipe.developerSkippedActionMask = InsertEmptyMaskBit(recipe.developerSkippedActionMask, pilot + 1);
        recipe.replacedActionMask = InsertEmptyMaskBit(recipe.replacedActionMask, pilot + 1);
        if (recipe.currentActionIndex > pilot) recipe.currentActionIndex++;
      }
      if (!actions.Contains(CareActionType.ClosedEyeRest) && actions.Count < 4)
      {
        actions.Add(CareActionType.ClosedEyeRest);
        originals.Add(CareActionType.ClosedEyeRest);
      }
      recipe.actionList = actions.Take(4).ToArray();
      recipe.originalActionList = originals.Take(4).ToArray();
      recipe.recipeType = recipe.actionList.Length >= 4 ? CareRecipeType.Full
        : recipe.actionList.Length == 3 ? CareRecipeType.Triple
        : recipe.actionList.Length == 2 ? CareRecipeType.Double
        : CareRecipeType.Single;
      recipe.currentActionIndex = Mathf.Clamp(recipe.currentActionIndex, 0, recipe.actionList.Length);
    }

    private static int InsertEmptyMaskBit(int mask, int index)
    {
      var lower = mask & ((1 << index) - 1);
      var upper = (mask & ~((1 << index) - 1)) << 1;
      return lower | upper;
    }

    private static CareRoutineId InferRoutineId(CareActionType[] actions)
    {
      var signature = CareRecipeGenerator.Signature(actions);
      if (signature == CareRecipeGenerator.Signature(new[]
          { CareActionType.FocusShift, CareActionType.GuidedEyeCircles, CareActionType.ClosedEyeRest }))
        return CareRoutineId.FocusFlow;
      if (signature == CareRecipeGenerator.Signature(new[]
          { CareActionType.PilotEyeRoutine, CareActionType.GuidedEyeCircles, CareActionType.ClosedEyeRest }))
        return CareRoutineId.PilotFlow;
      if (signature == CareRecipeGenerator.Signature(new[]
          { CareActionType.FocusShift, CareActionType.ClosedEyeRest }))
        return CareRoutineId.DeepReset;
      if (signature == CareRecipeGenerator.Signature(new[]
          { CareActionType.FocusShift, CareActionType.PilotEyeRoutine, CareActionType.GuidedEyeCircles, CareActionType.ClosedEyeRest }))
        return CareRoutineId.FullCare;
      return CareRoutineId.LegacyCompatible;
    }

    private static CareRoutineId InferRoutineIdFromSignature(string signature)
    {
      if (string.IsNullOrEmpty(signature)) return CareRoutineId.None;
      foreach (var routineId in new[]
      {
        CareRoutineId.FocusFlow,
        CareRoutineId.PilotFlow,
        CareRoutineId.DeepReset,
        CareRoutineId.FullCare,
      })
      {
        var recipe = CareRecipeGenerator.CreateRoutine(routineId, 1, 0);
        if (string.Equals(CareRecipeGenerator.Signature(recipe.actionList), signature, StringComparison.Ordinal))
          return routineId;
      }
      return CareRoutineId.None;
    }

    private static void ApplyRoutineV24Parameters(
      CareRecipeSaveData recipe,
      CareRoutineId routineId,
      bool inspection)
    {
      var parameterId = inspection ? CareRoutineId.PilotFlow : routineId;
      recipe.routineId = parameterId;
      if (inspection) recipe.recipeType = CareRecipeType.Inspection;
      recipe.focusCycleCount = parameterId == CareRoutineId.FullCare ? 4 : 6;
      recipe.pilotRoundsPerAxis = parameterId == CareRoutineId.FullCare ? 2 : 3;
      recipe.guidedLapsPerDirection = parameterId == CareRoutineId.FullCare ? 2 : 3;
      recipe.closedEyeRestSeconds = parameterId == CareRoutineId.DeepReset ? 90f : 60f;
      recipe.deepRest = recipe.closedEyeRestSeconds > CareActionLibrary.NormalRestSeconds;
    }

    private static int[] EvenRoutineRewards(int count)
    {
      count = Mathf.Clamp(count, 1, 4);
      var rewards = new int[count];
      for (var index = 0; index < count; index++)
        rewards[index] = CareEconomyConfiguration.DefaultRoutineCareEnergy / count;
      return rewards;
    }

    private static void MigrateStaleUiStateAfterLoad(CareStationSaveData data)
    {
      if (data == null) return;

      if (data.currentState == CareStationState.UpgradeSelection &&
          !CareStationShiftRules.CanPurchaseAnyUpgrade(data, new CareStationUpgradeConfiguration()))
      {
        data.upgradeOffered = true;
        data.upgradeDeferred = true;
        data.currentState = CareStationState.StationWorking;
      }

      if (data.currentState != CareStationState.WaitStorageSpace) return;

      var pendingCareReward = data.pendingFullBottleShipment > 0;
      var pendingOfflineReward = data.pendingOfflineXP > data.collectedOfflineBottleValue;
      if (pendingCareReward || pendingOfflineReward) return;

      // WaitStorageSpace is a resumable collection gate, not the persistent
      // representation of a merely full rack. Old saves could retain the gate
      // and collection phase after their reward was already resolved, causing
      // the controller to restore an input-blocking presentation forever.
      // Preserve all economy and progression data; only repair the impossible
      // presentation state while loading from disk.
      data.currentState = CareStationState.StationWorking;
      data.activeCollectionPhase = CareStationCollectionPhase.None;
      data.pendingReturnPhase = CareStationCollectionPhase.None;
      data.offlineCollectionResolved = true;
      data.returnedNeutralAfterOffline = true;
      data.offlineProductionPausedByFullStorage = CareStationStorageRules.Remaining(data) <= 0;
    }

    private static void MigrateRetiredBlinkReset(CareStationSaveData data, int loadedVersion)
    {
      var completedTrainingMask = 0;
      if (loadedVersion >= 16)
      {
        // v16 training order was retired Blink, Focus, Screen Break, Rest.
        // Blink supplied no care credit; retain only real completed actions.
        if (data.trainingProgress >= 2) completedTrainingMask |= CareRecipeGenerator.TrainingBit(CareActionType.FocusShift);
        if (data.trainingProgress >= 3) completedTrainingMask |= CareRecipeGenerator.TrainingBit(CareActionType.ScreenDown);
        if (data.trainingProgress >= 4) completedTrainingMask |= CareRecipeGenerator.TrainingBit(CareActionType.ClosedEyeRest);
      }
      else
      {
        // v1-v15 used Screen Down, Closed-Eye Rest, Focus Shift and Guided
        // Eye Circles. All four remain real actions under their current names.
        if (data.trainingProgress >= 1) completedTrainingMask |= CareRecipeGenerator.TrainingBit(CareActionType.ScreenDown);
        if (data.trainingProgress >= 2) completedTrainingMask |= CareRecipeGenerator.TrainingBit(CareActionType.ClosedEyeRest);
        if (data.trainingProgress >= 3) completedTrainingMask |= CareRecipeGenerator.TrainingBit(CareActionType.FocusShift);
        if (data.trainingProgress >= 4) completedTrainingMask |= CareRecipeGenerator.TrainingBit(CareActionType.GuidedEyeCircles);
      }

      var recipe = data.currentRecipe;
      if (recipe != null && recipe.recipeType == CareRecipeType.Training && recipe.recipeCompleted &&
          recipe.actionList != null && recipe.actionList.Length == 1)
        completedTrainingMask |= CareRecipeGenerator.TrainingBit(recipe.actionList[0]);
      data.completedTrainingActionMask = completedTrainingMask & CareRecipeGenerator.AllTrainingActionMask;
      data.trainingProgress = CareRecipeGenerator.CompletedTrainingCount(data.completedTrainingActionMask);

      var activeBlinkTraining = recipe != null && recipe.recipeType == CareRecipeType.Training &&
                                !recipe.recipeCompleted && recipe.actionList != null &&
                                recipe.actionList.Contains(CareActionType.BlinkReset);
      if (activeBlinkTraining)
      {
        var replacement = CareRecipeGenerator.CreateTraining(
          CareRecipeGenerator.NextTrainingIndex(data),
          Math.Max(1, recipe.createdShiftId > 0 ? recipe.createdShiftId : data.careShiftId),
          recipe.recipeSeed);
        // The shift/routine itself is already in progress. Avoid replaying the
        // global opening card, while allowing the Focus action's own intro.
        replacement.routineIntroCompleted = recipe.routineIntroCompleted;
        replacement.routineIntroElapsedSeconds = recipe.routineIntroElapsedSeconds;
        data.currentRecipe = recipe = replacement;
      }
      else if (recipe != null)
      {
        CareRecipeGenerator.RemoveRetiredBlinkReset(recipe, true);
      }

      if (data.careAction != null && data.careAction.actionType == CareActionType.BlinkReset)
        data.careAction.Reset();

      if (data.recentRecipeHistory != null)
      {
        data.recentRecipeHistory = data.recentRecipeHistory
          .Where(entry => !string.IsNullOrEmpty(entry) &&
                          entry.IndexOf(nameof(CareActionType.BlinkReset), StringComparison.Ordinal) < 0)
          .ToArray();
      }
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
      action.focusTargetStep = Mathf.Clamp(action.focusTargetStep, 0, 12);
      action.focusCycleCount = Mathf.Clamp(action.focusCycleCount, 0, 6);
      action.distanceDirectionProgress = Mathf.Clamp01(action.distanceDirectionProgress);
      if (!Enum.IsDefined(typeof(CareDistanceFallbackReason), action.distanceFallbackReason))
        action.distanceFallbackReason = CareDistanceFallbackReason.None;
      if (!Enum.IsDefined(typeof(CareActionCompletionSource), action.completionSource))
        action.completionSource = CareActionCompletionSource.None;
      action.guidedStage = Mathf.Clamp(action.guidedStage, 0, 7);
      action.guidedLapCount = Mathf.Clamp(action.guidedLapCount, 0, 3);
      action.guidedNormalizedProgress = Mathf.Clamp01(action.guidedNormalizedProgress);
      action.pilotCurrentAxis = Mathf.Clamp(action.pilotCurrentAxis, 0, 4);
      action.pilotCurrentRound = Mathf.Clamp(action.pilotCurrentRound, 0, 3);
      action.pilotCurrentEndpoint = Mathf.Clamp(action.pilotCurrentEndpoint, 0, 4);
      action.pilotNormalizedMoveProgress = Mathf.Clamp01(action.pilotNormalizedMoveProgress);
      action.restEarlyOpenVoiceCooldown = Mathf.Max(0f, action.restEarlyOpenVoiceCooldown);
      action.consumedVoiceCueMask &= 0x00FFFFFF;
      action.lastVoiceEventId = Mathf.Max(-1, action.lastVoiceEventId);
      SanitizeReference(ref action.gestureReferenceScale, ref action.gestureReferenceValid);
      if (!Enum.IsDefined(typeof(CareActionType), action.actionType)) action.Reset();
    }

    private static void ResetLegacyVoiceState(CareActionSaveData action)
    {
      if (action == null) return;
      action.consumedVoiceCueMask = 0;
      action.lastVoiceEventId = -1;
    }

    private static void MigrateFinalCareActionLibrary(CareStationSaveData data)
    {
      if (data == null) return;

      var displacedActiveAction = data.careAction != null &&
                                  data.careAction.actionType != CareActionType.None &&
                                  !CareActionLibrary.IsRetiredTask(data.careAction.actionType) &&
                                  data.careAction.internalPhase != CareActionInternalPhase.None &&
                                  data.careAction.stage != CareActionStage.Completed
        ? data.careAction
        : null;

      // v18 bit 2 represented the retired Screen Break training. It must not
      // become credit for the new Pilot action which intentionally reuses that
      // bit in v19. Real Focus, Guided and Rest credit is preserved.
      data.completedTrainingActionMask &=
        CareRecipeGenerator.TrainingBit(CareActionType.FocusShift) |
        CareRecipeGenerator.TrainingBit(CareActionType.GuidedEyeCircles) |
        CareRecipeGenerator.TrainingBit(CareActionType.ClosedEyeRest);
      data.trainingProgress = CareRecipeGenerator.CompletedTrainingCount(data.completedTrainingActionMask);

      var recipe = data.currentRecipe;
      var postProduction = recipe != null && (recipe.recipeCompleted || recipe.completionConsumed ||
        data.careActionCompleted || data.pendingIncidentXP > 0 || data.collectedCareBottleValue > 0);
      if (postProduction && recipe != null) recipe.completionFeedbackPlayed = true;
      if (recipe != null && !postProduction)
      {
        var retiredTraining = recipe.recipeType == CareRecipeType.Training && recipe.actionList != null &&
                              recipe.actionList.Any(CareActionLibrary.IsRetiredTask);
        if (retiredTraining)
        {
          var next = CareRecipeGenerator.NextTrainingIndex(data);
          var replacement = CareRecipeGenerator.CreateTraining(
            next,
            Math.Max(1, recipe.createdShiftId > 0 ? recipe.createdShiftId : data.careShiftId),
            recipe.recipeSeed);
          replacement.routineIntroCompleted = recipe.routineIntroCompleted;
          replacement.routineIntroElapsedSeconds = recipe.routineIntroElapsedSeconds;
          data.currentRecipe = recipe = replacement;
        }
        else if (recipe.recipeType == CareRecipeType.Inspection)
        {
          var replacement = CareStationInspectionRules.CreateRecipe(
            Math.Max(1, recipe.createdShiftId > 0 ? recipe.createdShiftId : data.careShiftId));
          var oldGuided = Array.IndexOf(recipe.actionList ?? Array.Empty<CareActionType>(), CareActionType.GuidedEyeCircles);
          var oldRest = Array.IndexOf(recipe.actionList ?? Array.Empty<CareActionType>(), CareActionType.ClosedEyeRest);
          if (oldGuided >= 0 && recipe.IsStepCompleted(oldGuided)) replacement.completedActionMask |= 1 << 1;
          if (oldRest >= 0 && recipe.IsStepCompleted(oldRest)) replacement.completedActionMask |= 1 << 2;
          replacement.currentActionIndex = FirstIncompleteRecipeStep(replacement);
          replacement.routineIntroCompleted = recipe.routineIntroCompleted;
          replacement.routineIntroElapsedSeconds = recipe.routineIntroElapsedSeconds;
          data.currentRecipe = recipe = replacement;
        }
        else
        {
          CareRecipeGenerator.RemoveRetiredBlinkReset(recipe, true);
        }

        if (displacedActiveAction != null && recipe.actionList != null &&
            recipe.actionList.Contains(displacedActiveAction.actionType) &&
            recipe.CurrentAction != displacedActiveAction.actionType)
        {
          recipe.deferredActionSnapshot = displacedActiveAction;
          data.careAction = new CareActionSaveData();
        }
      }

      if (!postProduction && data.careAction != null &&
          CareActionLibrary.IsRetiredTask(data.careAction.actionType))
        data.careAction.Reset();

      if (data.recentRecipeHistory != null)
      {
        data.recentRecipeHistory = data.recentRecipeHistory
          .Where(entry => !string.IsNullOrEmpty(entry) &&
                          entry.IndexOf(nameof(CareActionType.BlinkReset), StringComparison.Ordinal) < 0 &&
                          entry.IndexOf(nameof(CareActionType.ScreenDown), StringComparison.Ordinal) < 0)
          .ToArray();
      }
    }

    private static int FirstIncompleteRecipeStep(CareRecipeSaveData recipe)
    {
      if (recipe == null) return 0;
      for (var index = 0; index < recipe.ActionCount; index++)
        if (!recipe.IsStepCompleted(index)) return index;
      return recipe.ActionCount;
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
