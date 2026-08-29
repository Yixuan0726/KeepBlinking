using System;
using System.IO;
using KeepBlinking.CareStation;
using NUnit.Framework;

namespace KeepBlinking.Tests
{
  public sealed class CareStationSaveServiceTests
  {
    private string _directory;
    private string _path;

    [SetUp]
    public void SetUp()
    {
      _directory = Path.Combine(Path.GetTempPath(), "KeepBlinkingCareStationTests", Guid.NewGuid().ToString("N"));
      _path = Path.Combine(_directory, "save.json");
    }

    [TearDown]
    public void TearDown()
    {
      if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Test]
    public void MidShiftStateRoundTripsWithoutChangingProgress()
    {
      var now = new DateTime(2026, 8, 11, 12, 0, 0, DateTimeKind.Utc);
      var service = new CareStationSaveService(_path);
      var save = new CareStationSaveData
      {
        currentShift = 2,
        currentState = CareStationState.CareActionPaused,
        selectedIncident = CareStationIncidentType.DrySpot,
        pendingOfflineXP = 72,
        pendingIncidentXP = 24,
        careActionElapsed = 18.5f,
        careActionCompleted = false,
        pushAwayCompleted = false,
      };
      save.StampActive(now);
      save.StampClaimed(now);
      service.Save(save, now);

      var restored = service.Load(now.AddMinutes(1));
      Assert.That(restored.currentShift, Is.EqualTo(2));
      Assert.That(restored.currentState, Is.EqualTo(CareStationState.CareActionPaused));
      Assert.That(restored.selectedIncident, Is.EqualTo(CareStationIncidentType.DrySpot));
      Assert.That(restored.pendingOfflineXP, Is.EqualTo(72));
      Assert.That(restored.pendingIncidentXP, Is.EqualTo(24));
      Assert.That(restored.careActionElapsed, Is.EqualTo(18.5f).Within(0.001f));
    }

    [Test]
    public void MultipleUpgradeLevelsRoundTripWithoutLosingTheirRoute()
    {
      var now = DateTime.UtcNow;
      var service = new CareStationSaveService(_path);
      var save = new CareStationSaveData { currentShift = 3 };
      CareStationShiftRules.ApplyUpgrade(save, CareStationUpgradeId.BiggerCart);
      service.Save(save, now);
      var restored = service.Load(now);

      CareStationShiftRules.ApplyUpgrade(restored, CareStationUpgradeId.MoreWorkers);
      CareStationShiftRules.ApplyUpgrade(restored, CareStationUpgradeId.BiggerCart);
      Assert.That(restored.selectedUpgrade, Is.EqualTo(CareStationUpgradeId.BiggerCart));
      Assert.That(restored.cartCapacity, Is.EqualTo(8));
      Assert.That(restored.crewCount, Is.EqualTo(3));
      Assert.That(restored.cartLevel, Is.EqualTo(3));
      Assert.That(restored.workerLevel, Is.EqualTo(2));
      Assert.That(CareStationShiftRules.HasUpgrade(restored, CareStationUpgradeId.BiggerCart), Is.True);
      Assert.That(CareStationShiftRules.HasUpgrade(restored, CareStationUpgradeId.MoreWorkers), Is.True);
    }

    [Test]
    public void DeleteRemovesTheVersionedStationSave()
    {
      var service = new CareStationSaveService(_path);
      service.Save(new CareStationSaveData(), DateTime.UtcNow);
      Assert.That(File.Exists(_path), Is.True);
      service.Delete();
      Assert.That(File.Exists(_path), Is.False);
    }

    [Test]
    public void EndlessShiftAndGoldBottleStateRoundTrip()
    {
      var now = DateTime.UtcNow;
      var service = new CareStationSaveService(_path);
      var save = new CareStationSaveData
      {
        currentShift = 12,
        completedShifts = 11,
        selectedIncident = CareStationIncidentType.EyeGunk,
        pendingIncidentXP = 36,
        pendingGoldBottleCount = 1,
        workerLevel = 2,
      };
      service.Save(save, now);

      var restored = service.Load(now.AddMinutes(1));
      Assert.That(restored.currentShift, Is.EqualTo(12));
      Assert.That(restored.completedShifts, Is.EqualTo(11));
      Assert.That(restored.selectedIncident, Is.EqualTo(CareStationIncidentType.EyeGunk));
      Assert.That(restored.pendingGoldBottleCount, Is.EqualTo(1));
      Assert.That(CareStationShiftRules.HasUpgrade(restored, CareStationUpgradeId.MoreWorkers), Is.True);
    }

    [Test]
    public void LegacySelectedUpgradeMigratesIntoVersionTwoMask()
    {
      var now = DateTime.UtcNow;
      var legacy = new CareStationSaveData
      {
        saveVersion = 1,
        selectedUpgrade = CareStationUpgradeId.LargerStorage,
        storageHours = 36,
      };
      Directory.CreateDirectory(_directory);
      File.WriteAllText(_path, UnityEngine.JsonUtility.ToJson(legacy));

      var restored = new CareStationSaveService(_path).Load(now);
      Assert.That(restored.saveVersion, Is.EqualTo(CareStationSaveService.CurrentVersion));
      Assert.That(CareStationShiftRules.HasUpgrade(restored, CareStationUpgradeId.LargerStorage), Is.True);
      Assert.That(restored.storageHours, Is.EqualTo(36));
    }

    [Test]
    public void VersionTenOneShotUpgradesMigrateToLevelTwoAndPreserveInventory()
    {
      var legacy = new CareStationSaveData
      {
        saveVersion = 10,
        unlockedUpgradeMask = CareStationShiftRules.AllUpgradeMask,
        collectedExperienceCount = 19,
        shiftStoredFullBottles = 17,
        shiftStoredGoldBottles = 2,
      };
      Directory.CreateDirectory(_directory);
      File.WriteAllText(_path, UnityEngine.JsonUtility.ToJson(legacy));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);
      Assert.That(restored.workerLevel, Is.EqualTo(2));
      Assert.That(restored.storageLevel, Is.EqualTo(2));
      Assert.That(restored.cartLevel, Is.EqualTo(2));
      Assert.That(restored.crewCount, Is.EqualTo(3));
      Assert.That(restored.storageHours, Is.EqualTo(36));
      Assert.That(restored.cartCapacity, Is.EqualTo(6));
      Assert.That(restored.storedFullBottles, Is.EqualTo(19));
      Assert.That(restored.storedGoldBottles, Is.EqualTo(2));
    }

    [Test]
    public void FullStorageAndReleasedCareCollectionRoundTrip()
    {
      var now = DateTime.UtcNow;
      var save = new CareStationSaveData
      {
        storedFullBottles = 24,
        pendingIncidentXP = 12,
        collectedCareBottleValue = 4,
        careActionCompleted = true,
        careCollectionReleased = true,
        offlineProductionPausedByFullStorage = true,
        discardedOfflineBottleCount = 9,
        currentState = CareStationState.WaitStorageSpace,
        activeCollectionPhase = CareStationCollectionPhase.Care,
      };
      var service = new CareStationSaveService(_path);
      service.Save(save, now);

      var restored = service.Load(now.AddMinutes(1));
      Assert.That(restored.currentState, Is.EqualTo(CareStationState.WaitStorageSpace));
      Assert.That(restored.activeCollectionPhase, Is.EqualTo(CareStationCollectionPhase.Care));
      Assert.That(restored.pendingIncidentXP, Is.EqualTo(12));
      Assert.That(restored.collectedCareBottleValue, Is.EqualTo(4));
      Assert.That(restored.careCollectionReleased, Is.True);
      Assert.That(restored.offlineProductionPausedByFullStorage, Is.True);
      Assert.That(restored.discardedOfflineBottleCount, Is.EqualTo(9));
    }

    [Test]
    public void InspectionProgressAndUnstoredRewardRoundTrip()
    {
      var now = DateTime.UtcNow;
      var save = new CareStationSaveData
      {
        inspectionTriggered = true,
        inspectionActive = true,
        inspectionCurrentCheck = 2,
        inspectionCompletedMask = CareStationInspectionRules.FilterCheck | CareStationInspectionRules.FlowCheck,
        inspectionRewardProduced = true,
        inspectionRewardStored = false,
        stationLevel = 2,
        pendingIncidentXP = 25,
        pendingGoldBottleCount = 1,
        currentRecipe = CareStationInspectionRules.CreateRecipe(22),
      };
      save.currentRecipe.currentActionIndex = 2;
      save.currentRecipe.completedActionMask = 3;
      var service = new CareStationSaveService(_path);
      service.Save(save, now);

      var restored = service.Load(now.AddMinutes(1));
      Assert.That(restored.inspectionTriggered, Is.True);
      Assert.That(restored.inspectionActive, Is.True);
      Assert.That(restored.inspectionCurrentCheck, Is.EqualTo(2));
      Assert.That(restored.inspectionCompletedMask, Is.EqualTo(3));
      Assert.That(restored.inspectionRewardProduced, Is.True);
      Assert.That(restored.inspectionRewardStored, Is.False);
      Assert.That(restored.pendingIncidentXP, Is.EqualTo(25));
      Assert.That(restored.currentRecipe.currentActionIndex, Is.EqualTo(2));
    }

    [Test]
    public void LegacyCompletedThirdShiftContinuesAtShiftFour()
    {
      var legacy = new CareStationSaveData
      {
        saveVersion = 1,
        currentShift = 3,
        currentState = CareStationState.AutoShift,
        selectedUpgrade = CareStationUpgradeId.MoreWorkers,
        crewCount = 3,
      };
      Directory.CreateDirectory(_directory);
      File.WriteAllText(_path, UnityEngine.JsonUtility.ToJson(legacy));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);
      Assert.That(restored.currentShift, Is.EqualTo(4));
      Assert.That(restored.completedShifts, Is.EqualTo(3));
      Assert.That(CareStationShiftRules.IncidentForShift(restored.currentShift), Is.Not.EqualTo(CareStationIncidentType.None));
    }

    [Test]
    public void VersionTwoCombinedCollectionMigratesWithoutDuplicatingBottles()
    {
      var legacy = new CareStationSaveData
      {
        saveVersion = 2,
        currentState = CareStationState.CollectingExperience,
        selectedIncident = CareStationIncidentType.DrySpot,
        pendingOfflineXP = 72,
        pendingIncidentXP = 24,
        careActionCompleted = true,
        collectedExperienceCount = 11,
      };
      Directory.CreateDirectory(_directory);
      File.WriteAllText(_path, UnityEngine.JsonUtility.ToJson(legacy));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);
      Assert.That(restored.saveVersion, Is.EqualTo(CareStationSaveService.CurrentVersion));
      Assert.That(restored.collectedCareBottleValue, Is.EqualTo(11));
      Assert.That(restored.collectedOfflineBottleValue, Is.Zero);
    }

    [Test]
    public void TwoCollectionCompletionsRoundTripIndependently()
    {
      var now = DateTime.UtcNow;
      var save = new CareStationSaveData
      {
        offlineCollectionResolved = true,
        returnedNeutralAfterOffline = true,
        offlinePushAwayCompletion = CareStationPushAwayCompletion.SensorCompleted,
        carePushAwayCompletion = CareStationPushAwayCompletion.FallbackCompleted,
        activeCollectionPhase = CareStationCollectionPhase.Care,
      };
      var service = new CareStationSaveService(_path);
      service.Save(save, now);
      var restored = service.Load(now);
      Assert.That(restored.offlinePushAwayCompletion, Is.EqualTo(CareStationPushAwayCompletion.SensorCompleted));
      Assert.That(restored.carePushAwayCompletion, Is.EqualTo(CareStationPushAwayCompletion.FallbackCompleted));
      Assert.That(restored.activeCollectionPhase, Is.EqualTo(CareStationCollectionPhase.Care));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void LegacyVersionsMigrateToCurrentVersionWithoutCreatingAnotherShift(int version)
    {
      var legacy = new CareStationSaveData
      {
        saveVersion = version,
        currentShift = 2,
        completedShifts = 1,
        currentState = CareStationState.CareActionPaused,
        selectedIncident = CareStationIncidentType.DrySpot,
        careActionElapsed = 17f,
      };
      Directory.CreateDirectory(_directory);
      File.WriteAllText(_path, UnityEngine.JsonUtility.ToJson(legacy));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);
      Assert.That(restored.saveVersion, Is.EqualTo(CareStationSaveService.CurrentVersion));
      Assert.That(restored.currentShift, Is.EqualTo(2));
      Assert.That(restored.careShiftId, Is.EqualTo(2));
      Assert.That(restored.currentState, Is.EqualTo(CareStationState.CareActionPaused));
      Assert.That(restored.careActionElapsed, Is.EqualTo(17f));
      Assert.That(restored.careAction.actionType, Is.EqualTo(CareActionType.ClosedEyeRest));
      Assert.That(restored.careAction.elapsedSeconds, Is.EqualTo(17f));
      Assert.That(restored.careAction.completionSignalEmitted, Is.False);
    }

    [Test]
    public void ShiftSupplyIdempotencyStateRoundTrips()
    {
      var now = DateTime.UtcNow;
      var save = new CareStationSaveData
      {
        careShiftId = 9,
        currentShift = 5,
        currentState = CareStationState.WaitOfflinePushAway,
        pendingOfflineXP = 1,
        shiftSupplyGeneratedForShiftId = 9,
        offlineRewardReason = CareStationPushAwayCompletion.NoOfflineReward,
      };
      var service = new CareStationSaveService(_path);
      service.Save(save, now);
      var restored = service.Load(now.AddMinutes(1));

      Assert.That(restored.careShiftId, Is.EqualTo(9));
      Assert.That(restored.shiftSupplyGeneratedForShiftId, Is.EqualTo(9));
      Assert.That(restored.pendingOfflineXP, Is.EqualTo(1));
      Assert.That(CareStationShiftRules.EnsureShiftSupply(restored), Is.False);
      Assert.That(restored.pendingOfflineXP, Is.EqualTo(1));
    }

    [Test]
    public void VersionThreeSessionEntryMigratesLegacySupplyDirectlyIntoStorage()
    {
      var legacy = new CareStationSaveData
      {
        saveVersion = 3,
        currentShift = 4,
        completedShifts = 3,
        currentState = CareStationState.AutoShift,
        pendingOfflineXP = 0,
        offlineCollectionResolved = true,
        offlinePushAwayCompletion = CareStationPushAwayCompletion.NoOfflineReward,
      };
      Directory.CreateDirectory(_directory);
      File.WriteAllText(_path, UnityEngine.JsonUtility.ToJson(legacy));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);
      Assert.That(restored.currentShift, Is.EqualTo(4));
      Assert.That(restored.careShiftId, Is.EqualTo(4));
      Assert.That(restored.pendingOfflineXP, Is.Zero);
      Assert.That(restored.storedFullBottles, Is.EqualTo(1));
      Assert.That(restored.shiftSupplyGeneratedForShiftId, Is.EqualTo(4));
      Assert.That(restored.offlineCollectionResolved, Is.True);
      Assert.That(restored.offlineRewardReason, Is.EqualTo(CareStationPushAwayCompletion.None));
      Assert.That(restored.saveVersion, Is.EqualTo(CareStationSaveService.CurrentVersion));
    }

    [Test]
    public void VersionFourteenOfflineCollectionMigratesWithoutTouchingCareProgress()
    {
      var legacy = new CareStationSaveData
      {
        saveVersion = 14,
        currentShift = 2,
        careShiftId = 9,
        currentState = CareStationState.CollectingOfflineBottles,
        storageHours = 24,
        storedFullBottles = 20,
        pendingOfflineXP = 9,
        collectedOfflineBottleValue = 2,
        pendingIncidentXP = 36,
        collectedCareBottleValue = 11,
        careActionCompleted = true,
      };
      Directory.CreateDirectory(_directory);
      File.WriteAllText(_path, UnityEngine.JsonUtility.ToJson(legacy));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);

      Assert.That(restored.saveVersion, Is.EqualTo(CareStationSaveService.CurrentVersion));
      Assert.That(restored.currentState, Is.EqualTo(CareStationState.WaitDistanceResetMoveAway));
      Assert.That(restored.storedFullBottles, Is.EqualTo(20));
      Assert.That(restored.queuedOfflineXP, Is.EqualTo(7));
      Assert.That(restored.pendingOfflineXP, Is.Zero);
      Assert.That(restored.pendingIncidentXP, Is.EqualTo(36));
      Assert.That(restored.collectedCareBottleValue, Is.EqualTo(11));
      Assert.That(restored.careActionCompleted, Is.True);
    }

    [Test]
    public void InterruptedCareCollectionMigrationReservesItsRemainingStorage()
    {
      var legacy = new CareStationSaveData
      {
        saveVersion = 14,
        currentShift = 7,
        careShiftId = 8,
        currentState = CareStationState.CollectingCareBottles,
        activeCollectionPhase = CareStationCollectionPhase.Care,
        storageLevel = 3,
        storageHours = 48,
        storedFullBottles = 15,
        queuedOfflineXP = 33,
        pendingIncidentXP = 36,
        collectedCareBottleValue = 22,
        careActionCompleted = true,
        careCollectionReleased = true,
      };
      Directory.CreateDirectory(_directory);
      File.WriteAllText(_path, UnityEngine.JsonUtility.ToJson(legacy));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);

      Assert.That(restored.currentState, Is.EqualTo(CareStationState.CollectingCareBottles));
      Assert.That(restored.activeCollectionPhase, Is.EqualTo(CareStationCollectionPhase.Care));
      Assert.That(restored.storedFullBottles, Is.EqualTo(34));
      Assert.That(restored.queuedOfflineXP, Is.EqualTo(14));
      Assert.That(restored.pendingIncidentXP - restored.collectedCareBottleValue, Is.EqualTo(14));
      Assert.That(CareStationStorageRules.Remaining(restored), Is.EqualTo(14));
      Assert.That(CareStationCollectionRecoveryRules.Plan(restored, 14, 0).RequiresRuntimeRebuild, Is.True);
    }

    [Test]
    public void GestureReferencesRoundTripIndependently()
    {
      var now = DateTime.UtcNow;
      var save = new CareStationSaveData
      {
        currentState = CareStationState.WaitCarePushAway,
        activeCollectionPhase = CareStationCollectionPhase.Care,
        careActionGestureReferenceScale = 0.13f,
        careActionReferenceValid = true,
        offlinePushReferenceScale = 0.11f,
        offlinePushReferenceValid = true,
        carePushReferenceScale = 0.16f,
        carePushReferenceValid = true,
        offlineReturnCompletion = CareStationReturnCompletion.SensorCompleted,
        careReturnCompletion = CareStationReturnCompletion.ReturnFallbackCompleted,
        careAction = new CareActionSaveData
        {
          actionType = CareActionType.FocusShift,
          stage = CareActionStage.Active,
          internalPhase = CareActionInternalPhase.FocusFarOne,
          gestureReferenceScale = 0.13f,
          gestureReferenceValid = true,
        },
      };
      var service = new CareStationSaveService(_path);
      service.Save(save, now);
      var restored = service.Load(now.AddMinutes(1));

      Assert.That(restored.careActionGestureReferenceScale, Is.EqualTo(0.13f).Within(0.0001f));
      Assert.That(restored.offlinePushReferenceScale, Is.EqualTo(0.11f).Within(0.0001f));
      Assert.That(restored.carePushReferenceScale, Is.EqualTo(0.16f).Within(0.0001f));
      Assert.That(restored.careActionReferenceValid, Is.True);
      Assert.That(restored.offlinePushReferenceValid, Is.True);
      Assert.That(restored.carePushReferenceValid, Is.True);
      Assert.That(restored.offlineReturnCompletion, Is.EqualTo(CareStationReturnCompletion.SensorCompleted));
      Assert.That(restored.careReturnCompletion, Is.EqualTo(CareStationReturnCompletion.ReturnFallbackCompleted));
    }

    [Test]
    public void VersionFiveDistanceActionMigratesToSilentReferenceCapture()
    {
      var legacy = new CareStationSaveData
      {
        saveVersion = 5,
        currentState = CareStationState.CareActionInProgress,
        careAction = new CareActionSaveData
        {
          actionType = CareActionType.FocusShift,
          stage = CareActionStage.Active,
          internalPhase = CareActionInternalPhase.FocusFarOne,
          focusTargetStep = 2,
        },
      };
      Directory.CreateDirectory(_directory);
      File.WriteAllText(_path, UnityEngine.JsonUtility.ToJson(legacy));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);
      Assert.That(restored.saveVersion, Is.EqualTo(CareStationSaveService.CurrentVersion));
      Assert.That(restored.careAction.internalPhase, Is.EqualTo(CareActionInternalPhase.FocusReference));
      Assert.That(restored.careAction.stage, Is.EqualTo(CareActionStage.Preparing));
      Assert.That(restored.careActionReferenceValid, Is.False);
      Assert.That(restored.offlinePushReferenceValid, Is.False);
      Assert.That(restored.carePushReferenceValid, Is.False);
    }

    [Test]
    public void IncompleteRecipeRoundTripsWithoutRerollingOrRepeatingCompletedSteps()
    {
      var now = DateTime.UtcNow;
      var save = new CareStationSaveData
      {
        careShiftId = 14,
        currentShift = 9,
        currentState = CareStationState.CareActionPaused,
        trainingProgress = 4,
        completedTrainingActionMask = CareRecipeGenerator.AllTrainingActionMask,
        formalRecipesCreated = 3,
        currentRecipe = new CareRecipeSaveData
        {
          recipeId = "recipe_14_fixed",
          recipeSeed = 7751,
          recipeType = CareRecipeType.Triple,
          actionList = new[] { CareActionType.FocusShift, CareActionType.GuidedEyeCircles, CareActionType.ClosedEyeRest },
          currentActionIndex = 1,
          completedActionMask = 1,
          createdShiftId = 14,
        },
      };
      var service = new CareStationSaveService(_path);
      service.Save(save, now);

      var restored = service.Load(now.AddMinutes(1));
      Assert.That(restored.currentRecipe.recipeId, Is.EqualTo("recipe_14_fixed"));
      Assert.That(restored.currentRecipe.recipeSeed, Is.EqualTo(7751));
      Assert.That(restored.currentRecipe.currentActionIndex, Is.EqualTo(1));
      Assert.That(restored.currentRecipe.IsStepCompleted(0), Is.True);
      Assert.That(restored.currentRecipe.CurrentAction, Is.EqualTo(CareActionType.GuidedEyeCircles));
    }

    [Test]
    public void DeveloperSkippedRecipeStepRoundTripsWithoutRepeatingCompletion()
    {
      var now = DateTime.UtcNow;
      var save = new CareStationSaveData
      {
        careShiftId = 21,
        currentState = CareStationState.CareActionCompleted,
        careAction = new CareActionSaveData
        {
          actionType = CareActionType.FocusShift,
          stage = CareActionStage.Completed,
          internalPhase = CareActionInternalPhase.FocusNeutralFinish,
          completionSource = CareActionCompletionSource.DeveloperSkipped,
          completionSignalEmitted = true,
        },
        currentRecipe = new CareRecipeSaveData
        {
          recipeId = "developer_skip_restore",
          recipeType = CareRecipeType.Double,
          actionList = new[] { CareActionType.FocusShift, CareActionType.ClosedEyeRest },
          currentActionIndex = 1,
          completedActionMask = 1,
          developerSkippedActionMask = 1,
          createdShiftId = 21,
        },
      };
      var service = new CareStationSaveService(_path);
      service.Save(save, now);
      var restored = service.Load(now.AddMinutes(1));

      Assert.That(restored.careAction.completionSource, Is.EqualTo(CareActionCompletionSource.DeveloperSkipped));
      Assert.That(restored.careAction.CountsAsVerifiedCareAction, Is.False);
      Assert.That(restored.currentRecipe.IsStepCompleted(0), Is.True);
      Assert.That(restored.currentRecipe.IsStepDeveloperSkipped(0), Is.True);
      var runtime = new CareRecipeRuntime(restored.currentRecipe);
      Assert.That(runtime.CompleteCurrentAction(CareActionType.FocusShift).Accepted, Is.False);
      Assert.That(runtime.CurrentAction, Is.EqualTo(CareActionType.ClosedEyeRest));
    }

    [Test]
    public void VersionEightInterruptedCareActionMigratesToOneSafeRecipe()
    {
      var legacy = new CareStationSaveData
      {
        saveVersion = 8,
        careShiftId = 6,
        currentShift = 6,
        completedShifts = 2,
        currentState = CareStationState.CareActionPaused,
        selectedIncident = CareStationIncidentType.DrySpot,
        careAction = new CareActionSaveData
        {
          actionType = CareActionType.ClosedEyeRest,
          stage = CareActionStage.Paused,
          internalPhase = CareActionInternalPhase.ClosedEyeActive,
          elapsedSeconds = 12f,
        },
      };
      Directory.CreateDirectory(_directory);
      File.WriteAllText(_path, UnityEngine.JsonUtility.ToJson(legacy));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);
      Assert.That(restored.saveVersion, Is.EqualTo(CareStationSaveService.CurrentVersion));
      Assert.That(restored.trainingProgress, Is.EqualTo(1));
      Assert.That(restored.currentRecipe.ActionCount, Is.EqualTo(1));
      Assert.That(restored.currentRecipe.CurrentAction, Is.EqualTo(CareActionType.ClosedEyeRest));
      Assert.That(restored.currentRecipe.recipeCompleted, Is.False);
      Assert.That(restored.currentRecipe.createdShiftId, Is.EqualTo(6));
    }

    [TestCase(1, 1)]
    [TestCase(2, 2)]
    [TestCase(3, 3)]
    [TestCase(4, 4)]
    [TestCase(5, 5)]
    [TestCase(6, 6)]
    [TestCase(7, 7)]
    [TestCase(8, 8)]
    [TestCase(9, 13)]
    [TestCase(14, 14)]
    [TestCase(15, 15)]
    [TestCase(16, 16)]
    [TestCase(17, 17)]
    [TestCase(18, 18)]
    public void EveryLegacySaveVersionLoadsIntoCurrentRecipeCapableVersion(int firstVersion, int lastVersion)
    {
      for (var version = firstVersion; version <= lastVersion; version++)
      {
        var legacy = new CareStationSaveData
        {
          saveVersion = version,
          currentShift = 3,
          careShiftId = 3,
          completedShifts = 2,
          currentState = CareStationState.Dormant,
        };
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_path, UnityEngine.JsonUtility.ToJson(legacy));

        var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);
        Assert.That(restored.saveVersion, Is.EqualTo(CareStationSaveService.CurrentVersion), $"v{version}");
        Assert.That(restored.currentShift, Is.GreaterThanOrEqualTo(3), $"v{version}");
        Assert.That(restored.trainingProgress, Is.EqualTo(version < 9 ? 1 : 0), $"v{version}");
        Assert.That(restored.currentRecipe, Is.Not.Null, $"v{version}");
        Assert.That(restored.recentRecipeHistory, Is.Not.Null, $"v{version}");
      }
    }

    [Test]
    public void VersionFifteenMigratesCareLanguageWithoutLosingCurrentShiftOrInventory()
    {
      var legacy = new CareStationSaveData
      {
        saveVersion = 15,
        currentShift = 7,
        careShiftId = 9,
        trainingProgress = 3,
        storedFullBottles = 27,
        storedGoldBottles = 2,
        workerLevel = 3,
        storageLevel = 3,
        cartLevel = 2,
        currentState = CareStationState.CareActionInProgress,
        currentRecipe = new CareRecipeSaveData
        {
          recipeId = "v15_screen_down",
          recipeType = CareRecipeType.Double,
          actionList = new[] { CareActionType.ScreenDown, CareActionType.ClosedEyeRest },
          currentActionIndex = 0,
          createdShiftId = 9,
        },
        careAction = new CareActionSaveData
        {
          actionType = CareActionType.ScreenDown,
          internalPhase = CareActionInternalPhase.ScreenDownRest,
          stage = CareActionStage.Active,
          elapsedSeconds = 8f,
        },
      };
      Directory.CreateDirectory(_directory);
      File.WriteAllText(_path, UnityEngine.JsonUtility.ToJson(legacy));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);

      Assert.That(restored.saveVersion, Is.EqualTo(CareStationSaveService.CurrentVersion));
      Assert.That(restored.careShiftId, Is.EqualTo(9));
      Assert.That(restored.trainingProgress, Is.EqualTo(2));
      Assert.That(restored.storedFullBottles, Is.EqualTo(27));
      Assert.That(restored.storedGoldBottles, Is.EqualTo(2));
      Assert.That(restored.currentRecipe.actionList, Is.EqualTo(new[]
        { CareActionType.FocusShift, CareActionType.ClosedEyeRest }));
      Assert.That(restored.careAction.actionType, Is.EqualTo(CareActionType.None));
      Assert.That(restored.currentRecipe.routineIntroCompleted, Is.True);
    }

    [Test]
    public void CareActionIntroductionFlagsRoundTripWithoutChangingProgress()
    {
      var save = new CareStationSaveData
      {
        saveVersion = CareStationSaveService.CurrentVersion,
        currentShift = 8,
        careShiftId = 11,
        trainingProgress = 4,
        completedTrainingActionMask = CareRecipeGenerator.AllTrainingActionMask,
        storedFullBottles = 19,
        storedGoldBottles = 2,
        hasSeenFocusShiftIntro = true,
        hasSeenScreenBreakIntro = false,
        hasSeenClosedEyeRestIntro = true,
        hasSeenGuidedMovementIntro = false,
        hasSeenPilotEyeRoutineIntro = true,
      };
      new CareStationSaveService(_path).Save(save, DateTime.UtcNow);

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);

      Assert.That(restored.hasSeenFocusShiftIntro, Is.True);
      Assert.That(restored.hasSeenScreenBreakIntro, Is.False);
      Assert.That(restored.hasSeenClosedEyeRestIntro, Is.True);
      Assert.That(restored.hasSeenGuidedMovementIntro, Is.False);
      Assert.That(restored.hasSeenPilotEyeRoutineIntro, Is.True);
      Assert.That(restored.trainingProgress, Is.EqualTo(4));
      Assert.That(restored.storedFullBottles, Is.EqualTo(19));
      Assert.That(restored.storedGoldBottles, Is.EqualTo(2));
    }

    [Test]
    public void VersionSixteenActiveBlinkRecipeRemovesRetiredStepWithoutChangingEconomy()
    {
      var legacy = new CareStationSaveData
      {
        saveVersion = 16,
        currentState = CareStationState.CareActionInProgress,
        currentShift = 8,
        careShiftId = 12,
        trainingProgress = 4,
        storedFullBottles = 31,
        storedGoldBottles = 2,
        workerLevel = 3,
        storageLevel = 4,
        cartLevel = 2,
        pendingIncidentXP = 9,
        currentRecipe = new CareRecipeSaveData
        {
          recipeId = "v16_retired_focus_rest",
          recipeType = CareRecipeType.Triple,
          actionList = new[]
          {
            CareActionType.BlinkReset,
            CareActionType.FocusShift,
            CareActionType.ClosedEyeRest,
          },
          currentActionIndex = 0,
          createdShiftId = 12,
        },
        careAction = new CareActionSaveData
        {
          actionType = CareActionType.BlinkReset,
          internalPhase = CareActionInternalPhase.BlinkResetWaiting,
          stage = CareActionStage.WaitingForStart,
        },
      };
      Directory.CreateDirectory(_directory);
      File.WriteAllText(_path, UnityEngine.JsonUtility.ToJson(legacy));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);

      Assert.That(restored.saveVersion, Is.EqualTo(CareStationSaveService.CurrentVersion));
      Assert.That(restored.currentRecipe.actionList, Is.EqualTo(new[]
        { CareActionType.FocusShift, CareActionType.ClosedEyeRest }));
      Assert.That(restored.currentRecipe.currentActionIndex, Is.Zero);
      Assert.That(restored.currentRecipe.completedActionMask, Is.Zero);
      Assert.That(restored.currentRecipe.deepRest, Is.True);
      Assert.That(restored.careAction.actionType, Is.EqualTo(CareActionType.None));
      Assert.That(restored.storedFullBottles, Is.EqualTo(31));
      Assert.That(restored.storedGoldBottles, Is.EqualTo(2));
      Assert.That(restored.pendingIncidentXP, Is.EqualTo(9));
      Assert.That(restored.workerLevel, Is.EqualTo(3));
      Assert.That(restored.storageLevel, Is.EqualTo(4));
      Assert.That(restored.cartLevel, Is.EqualTo(2));
    }

    [Test]
    public void VersionSixteenBlinkTrainingContinuesAtNextUnfinishedRealTraining()
    {
      var legacy = new CareStationSaveData
      {
        saveVersion = 16,
        careShiftId = 6,
        currentState = CareStationState.PromptCareAction,
        trainingProgress = 2,
        currentRecipe = new CareRecipeSaveData
        {
          recipeId = "training_legacy_blink",
          recipeSeed = 19,
          recipeType = CareRecipeType.Training,
          actionList = new[] { CareActionType.BlinkReset },
          createdShiftId = 6,
        },
        careAction = new CareActionSaveData { actionType = CareActionType.BlinkReset },
      };
      Directory.CreateDirectory(_directory);
      File.WriteAllText(_path, UnityEngine.JsonUtility.ToJson(legacy));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);

      Assert.That(restored.completedTrainingActionMask &
                  CareRecipeGenerator.TrainingBit(CareActionType.FocusShift), Is.Not.Zero);
      Assert.That(restored.currentRecipe.recipeType, Is.EqualTo(CareRecipeType.Training));
      Assert.That(restored.currentRecipe.actionList, Is.EqualTo(new[] { CareActionType.PilotEyeRoutine }));
      Assert.That(restored.currentRecipe.currentActionIndex, Is.Zero);
      Assert.That(restored.careAction.actionType, Is.EqualTo(CareActionType.None));
    }

    [Test]
    public void VersionSixteenBlinkRemovalRemapsCompletedMaskAndSupplementsOneRemainingAction()
    {
      var legacy = new CareStationSaveData
      {
        saveVersion = 16,
        careShiftId = 13,
        currentState = CareStationState.CareActionInProgress,
        storedFullBottles = 22,
        currentRecipe = new CareRecipeSaveData
        {
          recipeId = "v16_partial",
          recipeType = CareRecipeType.Triple,
          actionList = new[]
            { CareActionType.ScreenDown, CareActionType.BlinkReset, CareActionType.ClosedEyeRest },
          currentActionIndex = 1,
          completedActionMask = 1,
          createdShiftId = 13,
        },
      };
      Directory.CreateDirectory(_directory);
      File.WriteAllText(_path, UnityEngine.JsonUtility.ToJson(legacy));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);

      Assert.That(restored.currentRecipe.actionList, Is.EqualTo(new[]
        { CareActionType.FocusShift, CareActionType.ClosedEyeRest }));
      Assert.That(restored.currentRecipe.completedActionMask, Is.Zero);
      Assert.That(restored.currentRecipe.currentActionIndex, Is.Zero);
      Assert.That(restored.currentRecipe.recipeCompleted, Is.False);
      Assert.That(restored.storedFullBottles, Is.EqualTo(22));
    }

    [Test]
    public void VersionSixteenCompletedBlinkRecipeNeverReopensOrProducesAgain()
    {
      var legacy = new CareStationSaveData
      {
        saveVersion = 16,
        careShiftId = 15,
        currentState = CareStationState.PresentCareBottles,
        pendingIncidentXP = 12,
        collectedCareBottleValue = 3,
        currentRecipe = new CareRecipeSaveData
        {
          recipeId = "v16_already_produced",
          recipeType = CareRecipeType.Double,
          actionList = new[] { CareActionType.BlinkReset, CareActionType.ClosedEyeRest },
          currentActionIndex = 2,
          completedActionMask = 3,
          recipeCompleted = true,
          completionSignalSent = true,
          completionConsumed = true,
          createdShiftId = 15,
        },
      };
      Directory.CreateDirectory(_directory);
      File.WriteAllText(_path, UnityEngine.JsonUtility.ToJson(legacy));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);

      Assert.That(restored.currentRecipe.actionList, Is.EqualTo(new[] { CareActionType.ClosedEyeRest }));
      Assert.That(restored.currentRecipe.recipeCompleted, Is.True);
      Assert.That(restored.currentRecipe.completionSignalSent, Is.True);
      Assert.That(restored.currentRecipe.completionConsumed, Is.True);
      Assert.That(restored.pendingIncidentXP, Is.EqualTo(12));
      Assert.That(restored.collectedCareBottleValue, Is.EqualTo(3));
    }

    [Test]
    public void VersionEighteenInspectionMigratesCurrentSaveShapeWithoutLosingProgressOrEconomy()
    {
      var legacy = new CareStationSaveData
      {
        saveVersion = 18,
        currentState = CareStationState.CareActionInProgress,
        currentShift = 5,
        careShiftId = 5,
        storedFullBottles = 48,
        storedGoldBottles = 0,
        workerLevel = 3,
        storageLevel = 3,
        cartLevel = 3,
        inspectionActive = true,
        currentRecipe = new CareRecipeSaveData
        {
          recipeId = "station_inspection_5",
          recipeType = CareRecipeType.Inspection,
          actionList = new[]
          {
            CareActionType.ScreenDown,
            CareActionType.FocusShift,
            CareActionType.GuidedEyeCircles,
            CareActionType.ClosedEyeRest,
          },
          currentActionIndex = 3,
          completedActionMask = 7,
          createdShiftId = 5,
        },
        careAction = new CareActionSaveData
        {
          actionType = CareActionType.ClosedEyeRest,
          internalPhase = CareActionInternalPhase.ClosedEyeActive,
          stage = CareActionStage.Active,
          elapsedSeconds = 12f,
        },
      };
      Directory.CreateDirectory(_directory);
      File.WriteAllText(_path, UnityEngine.JsonUtility.ToJson(legacy));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);

      Assert.That(restored.currentRecipe.actionList, Is.EqualTo(new[]
      {
        CareActionType.PilotEyeRoutine,
        CareActionType.GuidedEyeCircles,
        CareActionType.ClosedEyeRest,
      }));
      Assert.That(restored.currentRecipe.completedActionMask, Is.EqualTo(2),
        "The already completed Guided step is preserved; retired Screen and old Focus do not impersonate Pilot.");
      Assert.That(restored.currentRecipe.currentActionIndex, Is.Zero);
      Assert.That(restored.currentRecipe.deferredActionSnapshot.actionType,
        Is.EqualTo(CareActionType.ClosedEyeRest));
      Assert.That(restored.currentRecipe.deferredActionSnapshot.elapsedSeconds, Is.EqualTo(12f),
        "The interrupted real Rest is deferred, not discarded, while Pilot and the already-completed Guided step are reconciled.");
      var runtime = new CareRecipeRuntime(restored.currentRecipe);
      Assert.That(runtime.CompleteCurrentAction(CareActionType.PilotEyeRoutine).Accepted, Is.True);
      Assert.That(runtime.CurrentAction, Is.EqualTo(CareActionType.ClosedEyeRest),
        "A previously completed real Guided step is not demanded again after the inserted Pilot.");
      Assert.That(restored.storedFullBottles, Is.EqualTo(48));
      Assert.That(restored.storedGoldBottles, Is.Zero);
      Assert.That(restored.workerLevel, Is.EqualTo(3));
      Assert.That(restored.storageLevel, Is.EqualTo(3));
      Assert.That(restored.cartLevel, Is.EqualTo(3));
    }

    [Test]
    public void VersionNineAutoShiftMigratesWithoutSkippingReservedShiftId()
    {
      var legacy = new CareStationSaveData
      {
        saveVersion = 9,
        currentShift = 6,
        careShiftId = 9,
        currentState = CareStationState.AutoShift,
      };
      Directory.CreateDirectory(_directory);
      File.WriteAllText(_path, UnityEngine.JsonUtility.ToJson(legacy));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);
      Assert.That(restored.careShiftCompleted, Is.True);
      Assert.That(restored.endShiftConsumed, Is.True);
      Assert.That(restored.autoShiftEntered, Is.True);
      Assert.That(restored.nextShiftPrepared, Is.True);
      Assert.That(CareStationShiftRules.TryBeginNextShift(restored, true), Is.True);
      Assert.That(restored.careShiftId, Is.EqualTo(9),
        "The identity already reserved by v9 must be reused once.");
    }

    [Test]
    public void ReplacedCareStepAndShiftEndFlagsRoundTrip()
    {
      var save = new CareStationSaveData
      {
        currentState = CareStationState.CareActionPaused,
        careShiftId = 12,
        careStepChangePending = true,
        careStepWasReplaced = true,
        replacedOriginalAction = CareActionType.FocusShift,
        replacedWithAction = CareActionType.ClosedEyeRest,
        replacementPauseReason = CareActionPauseReason.DistanceUnavailable,
        currentRecipe = new CareRecipeSaveData
        {
          recipeId = "replacement",
          recipeType = CareRecipeType.Double,
          actionList = new[] { CareActionType.FocusShift, CareActionType.ClosedEyeRest },
          originalActionList = new[] { CareActionType.FocusShift, CareActionType.GuidedEyeCircles },
          currentActionIndex = 1,
          completedActionMask = 1,
          replacedActionMask = 2,
          createdShiftId = 12,
        },
      };
      new CareStationSaveService(_path).Save(save, DateTime.UtcNow);
      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);

      Assert.That(restored.careStepChangePending, Is.True);
      Assert.That(restored.currentRecipe.CurrentAction, Is.EqualTo(CareActionType.ClosedEyeRest));
      Assert.That(restored.currentRecipe.IsStepCompleted(0), Is.True);
      Assert.That(restored.currentRecipe.IsStepReplaced(1), Is.True);
      Assert.That(restored.currentRecipe.OriginalActionAt(1), Is.EqualTo(CareActionType.GuidedEyeCircles));
    }

    [Test]
    public void VersionSeventeenFullStorageSaveMigratesWithoutChangingInventoryAndCanExpand()
    {
      var legacy = new CareStationSaveData
      {
        saveVersion = 17,
        currentState = CareStationState.UpgradeSelection,
        storageLevel = 2,
        storageHours = 36,
        storedFullBottles = 36,
        storedGoldBottles = 0,
        offlineProductionPausedByFullStorage = true,
        upgradeOffered = true,
      };
      Directory.CreateDirectory(_directory);
      File.WriteAllText(_path, UnityEngine.JsonUtility.ToJson(legacy));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);

      Assert.That(restored.saveVersion, Is.EqualTo(CareStationSaveService.CurrentVersion));
      Assert.That(restored.currentState, Is.EqualTo(CareStationState.UpgradeSelection));
      Assert.That(restored.storedFullBottles, Is.EqualTo(36));
      Assert.That(restored.storedGoldBottles, Is.Zero);
      Assert.That(CareStationShiftRules.CanPurchaseUpgrade(
        restored,
        CareStationUpgradeId.LargerStorage,
        new CareStationUpgradeConfiguration()), Is.True);
    }

    [Test]
    public void GuaranteedFormalGoldMarkerRoundTripsAndCannotGenerateTwice()
    {
      var save = new CareStationSaveData
      {
        pendingIncidentXP = 24,
        currentRecipe = new CareRecipeSaveData
        {
          recipeType = CareRecipeType.Double,
          actionList = new[] { CareActionType.FocusShift, CareActionType.ClosedEyeRest },
        },
      };
      Assert.That(CareStationShiftRules.EnsureFirstFormalGoldBottle(save), Is.True);
      new CareStationSaveService(_path).Save(save, DateTime.UtcNow);

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);

      Assert.That(restored.firstFormalGoldBottleGenerated, Is.True);
      Assert.That(restored.pendingGoldBottleCount, Is.EqualTo(1));
      Assert.That(CareStationShiftRules.EnsureFirstFormalGoldBottle(restored), Is.False);
      Assert.That(restored.pendingGoldBottleCount, Is.EqualTo(1));
    }

    [Test]
    public void CurrentVoiceConsumptionStateRoundTripsForActiveAndDeferredActions()
    {
      var save = new CareStationSaveData
      {
        saveVersion = CareStationSaveService.CurrentVersion,
        currentState = CareStationState.CareActionInProgress,
        careAction = VoiceSnapshot(CareActionType.ClosedEyeRest, 0x6D5, 3123),
        currentRecipe = new CareRecipeSaveData
        {
          recipeId = "voice_consumption_roundtrip",
          recipeType = CareRecipeType.Double,
          actionList = new[] { CareActionType.FocusShift, CareActionType.ClosedEyeRest },
          currentActionIndex = 1,
          completedActionMask = 1,
          deferredActionSnapshot = VoiceSnapshot(CareActionType.ClosedEyeRest, 0x31A, 2042),
        },
      };

      new CareStationSaveService(_path).Save(save, DateTime.UtcNow);
      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);

      AssertVoiceSnapshot(restored.careAction, 0x6D5, 3123);
      AssertVoiceSnapshot(restored.currentRecipe.deferredActionSnapshot, 0x31A, 2042);
    }

    [Test]
    public void VersionNineteenAddsVoiceConsumptionBookkeepingWithoutChangingActionProgress()
    {
      var legacy = new CareStationSaveData
      {
        saveVersion = 19,
        currentState = CareStationState.CareActionInProgress,
        careAction = new CareActionSaveData
        {
          actionType = CareActionType.ClosedEyeRest,
          stage = CareActionStage.Active,
          internalPhase = CareActionInternalPhase.ClosedEyeActive,
          elapsedSeconds = 18.5f,
          consumedVoiceCueMask = 0x7FFFFF,
          lastVoiceEventId = 3099,
        },
        currentRecipe = new CareRecipeSaveData
        {
          recipeId = "v19_voice_migration",
          recipeType = CareRecipeType.Double,
          actionList = new[] { CareActionType.FocusShift, CareActionType.ClosedEyeRest },
          currentActionIndex = 1,
          completedActionMask = 1,
          deferredActionSnapshot = new CareActionSaveData
          {
            actionType = CareActionType.ClosedEyeRest,
            stage = CareActionStage.Paused,
            internalPhase = CareActionInternalPhase.ClosedEyeActive,
            pauseReason = CareActionPauseReason.EyesOpen,
            elapsedSeconds = 11f,
            consumedVoiceCueMask = 0x123,
            lastVoiceEventId = 2011,
          },
        },
      };
      Directory.CreateDirectory(_directory);
      File.WriteAllText(_path, UnityEngine.JsonUtility.ToJson(legacy));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);

      Assert.That(restored.saveVersion, Is.EqualTo(CareStationSaveService.CurrentVersion));
      Assert.That(restored.careAction.elapsedSeconds, Is.EqualTo(18.5f).Within(0.001f));
      Assert.That(restored.careAction.internalPhase,
        Is.EqualTo(CareActionInternalPhase.ClosedEyeActive));
      Assert.That(restored.careAction.consumedVoiceCueMask, Is.Zero);
      Assert.That(restored.careAction.lastVoiceEventId, Is.EqualTo(-1));
      Assert.That(restored.currentRecipe.deferredActionSnapshot.elapsedSeconds,
        Is.EqualTo(11f).Within(0.001f));
      Assert.That(restored.currentRecipe.deferredActionSnapshot.pauseReason,
        Is.EqualTo(CareActionPauseReason.EyesOpen));
      Assert.That(restored.currentRecipe.deferredActionSnapshot.consumedVoiceCueMask, Is.Zero);
      Assert.That(restored.currentRecipe.deferredActionSnapshot.lastVoiceEventId, Is.EqualTo(-1));
    }

    private static CareActionSaveData VoiceSnapshot(
      CareActionType actionType,
      int consumedMask,
      int eventId)
    {
      return new CareActionSaveData
      {
        actionType = actionType,
        stage = CareActionStage.Active,
        internalPhase = CareActionInternalPhase.ClosedEyeActive,
        elapsedSeconds = 17f,
        closeRequestCuePlayed = true,
        readyToOpenCuePlayed = true,
        guidedOpenCuePlayed = true,
        restBenefitVoicePlayed = true,
        restAlmostCompleteVoicePlayed = true,
        restCompletionVoicePlayed = true,
        consumedVoiceCueMask = consumedMask,
        lastVoiceEventId = eventId,
      };
    }

    private static void AssertVoiceSnapshot(
      CareActionSaveData action,
      int expectedMask,
      int expectedEventId)
    {
      Assert.That(action.closeRequestCuePlayed, Is.True);
      Assert.That(action.readyToOpenCuePlayed, Is.True);
      Assert.That(action.guidedOpenCuePlayed, Is.True);
      Assert.That(action.restBenefitVoicePlayed, Is.True);
      Assert.That(action.restAlmostCompleteVoicePlayed, Is.True);
      Assert.That(action.restCompletionVoicePlayed, Is.True);
      Assert.That(action.consumedVoiceCueMask, Is.EqualTo(expectedMask));
      Assert.That(action.lastVoiceEventId, Is.EqualTo(expectedEventId));
    }
  }
}
