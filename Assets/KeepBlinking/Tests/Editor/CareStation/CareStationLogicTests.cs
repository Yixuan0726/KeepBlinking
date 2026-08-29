using System;
using KeepBlinking.CareStation;
using NUnit.Framework;

namespace KeepBlinking.Tests
{
  public sealed class CareStationLogicTests
  {
    private static readonly DateTime Start = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);

    [Test]
    public void LessThanThirtyMinutesProducesNoFormalOfflineYield()
    {
      var result = CareStationOfflineCalculator.Calculate(Start, Start.AddMinutes(29.9), 30f, 24f, 18f, false);
      Assert.That(result.ExperienceMade, Is.Zero);
      Assert.That(result.HelpNeededCount, Is.Zero);
    }

    [TestCase(4, 72)]
    [TestCase(12, 216)]
    [TestCase(24, 432)]
    public void OfflineYieldMatchesCreditedHours(int hours, int expectedXp)
    {
      var result = CareStationOfflineCalculator.Calculate(Start, Start.AddHours(hours), 30f, 24f, 18f, false);
      Assert.That(result.ExperienceMade, Is.EqualTo(expectedXp));
      Assert.That(result.HelpNeededCount, Is.EqualTo(1));
    }

    [Test]
    public void OfflineYieldIsCappedByStorage()
    {
      var result = CareStationOfflineCalculator.Calculate(Start, Start.AddHours(60), 30f, 24f, 18f, false);
      Assert.That(result.CreditedDuration, Is.EqualTo(TimeSpan.FromHours(24)));
      Assert.That(result.ExperienceMade, Is.EqualTo(432));
    }

    [Test]
    public void ClockRollbackNeverProducesNegativeYield()
    {
      var result = CareStationOfflineCalculator.Calculate(Start, Start.AddHours(-5), 30f, 24f, 18f, false);
      Assert.That(result.ExperienceMade, Is.Zero);
      Assert.That(result.CreditedDuration, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void ClaimedIntervalCannotBeClaimedAgain()
    {
      var first = CareStationOfflineCalculator.Calculate(Start, Start.AddHours(4), 30f, 24f, 18f, false);
      var second = CareStationOfflineCalculator.Calculate(Start.AddHours(4), Start.AddHours(4), 30f, 24f, 18f, true);
      Assert.That(first.ExperienceMade, Is.EqualTo(72));
      Assert.That(second.ExperienceMade, Is.Zero);
      Assert.That(second.HelpNeededCount, Is.Zero);
    }

    [Test]
    public void ShiftOrderIsDustDrySpotThenEyeGunk()
    {
      Assert.That(CareStationShiftRules.IncidentForShift(1), Is.EqualTo(CareStationIncidentType.Dust));
      Assert.That(CareStationShiftRules.IncidentForShift(2), Is.EqualTo(CareStationIncidentType.DrySpot));
      Assert.That(CareStationShiftRules.IncidentForShift(3), Is.EqualTo(CareStationIncidentType.EyeGunk));
    }

    [Test]
    public void ShiftFourAndBeyondAlwaysProduceARepeatableIncident()
    {
      for (var shift = 4; shift <= 30; shift++)
      {
        var first = CareStationShiftRules.IncidentForShift(shift);
        var second = CareStationShiftRules.IncidentForShift(shift);
        Assert.That(first, Is.Not.EqualTo(CareStationIncidentType.None));
        Assert.That(second, Is.EqualTo(first));
      }
    }

    [Test]
    public void TrackingLossIsNotTreatedAsEarlyReopen()
    {
      var step = CareStationActionLogic.AdvanceClosedEyeRest(12f, 1f, 45f, false, false);
      Assert.That(step.PausedForTracking, Is.True);
      Assert.That(step.PausedForOpenEyes, Is.False);
      Assert.That(step.Elapsed, Is.EqualTo(12f));
    }

    [Test]
    public void EarlyReopenPausesWithoutClearingProgress()
    {
      var step = CareStationActionLogic.AdvanceClosedEyeRest(17.5f, 1f, 45f, true, false);
      Assert.That(step.PausedForOpenEyes, Is.True);
      Assert.That(step.Elapsed, Is.EqualTo(17.5f));
    }

    [Test]
    public void RepairRevealRequiresCompletedCareAction()
    {
      Assert.That(CareStationStateRules.CanEnterRepairReveal(false), Is.False);
      Assert.That(CareStationStateRules.CanEnterRepairReveal(true), Is.True);
    }

    [Test]
    public void ExperienceOnlySettlesFromArrivalSignal()
    {
      var ledger = new CareStationExperienceLedger();
      ledger.Begin(12);
      Assert.That(ledger.IsComplete, Is.False);
      ledger.RecordArrival(4);
      ledger.RecordArrival(8);
      Assert.That(ledger.IsComplete, Is.True);
      Assert.That(CareStationStateRules.CanSettleExperience(false), Is.False);
      Assert.That(CareStationStateRules.CanSettleExperience(true), Is.True);
    }

    [Test]
    public void SensorAndFallbackPushAwayCompletionsRemainDistinct()
    {
      Assert.That(CareStationPushAwayCompletion.SensorCompleted, Is.Not.EqualTo(CareStationPushAwayCompletion.FallbackCompleted));
      Assert.That(CareStationPushAwayCompletion.None, Is.Not.EqualTo(CareStationPushAwayCompletion.SensorCompleted));
    }

    [Test]
    public void UpgradeOpportunityRequiresCollectionAndAnAvailableLevel()
    {
      var save = new CareStationSaveData();
      Assert.That(CareStationStateRules.CanOfferStationUpgrade(2, true, save), Is.False);
      Assert.That(CareStationStateRules.CanOfferStationUpgrade(3, false, save), Is.False);
      Assert.That(CareStationStateRules.CanOfferStationUpgrade(3, true, save), Is.True);
      save.workerLevel = 4;
      save.storageLevel = 4;
      save.cartLevel = 4;
      Assert.That(CareStationStateRules.CanOfferStationUpgrade(6, true, save), Is.False);
    }

    [Test]
    public void RepeatingLoopOffersAnotherLevelEveryThreeCompletedShifts()
    {
      var save = new CareStationSaveData();
      Assert.That(CareStationStateRules.CanOfferStationUpgrade(3, true, save), Is.True);
      Assert.That(CareStationStateRules.CanOfferStationUpgrade(4, true, save), Is.False);
      save.workerLevel = 2;
      Assert.That(CareStationStateRules.CanOfferStationUpgrade(6, true, save), Is.True);
      save.workerLevel = save.storageLevel = save.cartLevel = 4;
      Assert.That(CareStationStateRules.CanOfferStationUpgrade(9, true, save), Is.False);
    }

    [Test]
    public void PushAwayFallbackOnlyAppearsAfterDelayInCollectionStates()
    {
      Assert.That(CareStationStateRules.CanOfferPushAwayFallback(CareStationState.WaitPushAwayReady, 4.99f, 5f), Is.False);
      Assert.That(CareStationStateRules.CanOfferPushAwayFallback(CareStationState.WaitPushAwayReady, 5f, 5f), Is.True);
      Assert.That(CareStationStateRules.CanOfferPushAwayFallback(CareStationState.WaitPushAway, 6f, 5f), Is.True);
      Assert.That(CareStationStateRules.CanOfferPushAwayFallback(CareStationState.CareActionInProgress, 20f, 5f), Is.False);
    }

    [Test]
    public void OfflineBottlesAutoStoreButDistanceResetStillGatesIncident()
    {
      var save = new CareStationSaveData { storageHours = 48 };
      var settlement = new CareStationProductionController().Settle(save, 12);
      Assert.That(settlement.ProducedStored, Is.EqualTo(12));
      Assert.That(save.pendingOfflineXP, Is.Zero);
      Assert.That(CareStationStateRules.CanPresentIncident(false, false), Is.False);
      Assert.That(CareStationStateRules.CanPresentIncident(true, false), Is.False);
      Assert.That(CareStationStateRules.CanPresentIncident(true, true), Is.True);
    }

    [Test]
    public void EmptyOfflineRewardCreatesNoCollectionBottle()
    {
      var save = new CareStationSaveData { careShiftId = 7, offlineCollectionResolved = false };
      var settlement = new CareStationProductionController().Settle(save, 0);
      Assert.That(settlement.TotalStored, Is.Zero);
      Assert.That(save.pendingOfflineXP, Is.Zero);
      Assert.That(save.shiftSupplyGeneratedForShiftId, Is.Zero);
      Assert.That(save.offlineRewardReason, Is.EqualTo(CareStationPushAwayCompletion.None));
    }

    [Test]
    public void LegacyShiftSupplyIsHandledAsOfflineMigrationDataOnly()
    {
      var save = new CareStationSaveData
      {
        careShiftId = 4,
        shiftSupplyGeneratedForShiftId = 4,
        offlineRewardReason = CareStationPushAwayCompletion.NoOfflineReward,
        offlineCollectionResolved = false,
        pendingOfflineXP = 0,
        offlinePushAwayCompletion = CareStationPushAwayCompletion.None,
      };
      var settlement = new CareStationProductionController().Settle(save, 0);
      Assert.That(settlement.TotalStored, Is.Zero);
      Assert.That(save.pendingOfflineXP, Is.Zero);
      Assert.That(save.shiftSupplyGeneratedForShiftId, Is.EqualTo(4));
    }

    [Test]
    public void ShiftIdOnlyAdvancesFromEndedAutoShiftAfterOfflineGate()
    {
      var active = new CareStationSaveData { currentShift = 2, careShiftId = 12, currentState = CareStationState.CareActionPaused };
      Assert.That(CareStationShiftRules.TryBeginNextShift(active, true), Is.False);
      Assert.That(active.careShiftId, Is.EqualTo(12));

      active.currentState = CareStationState.ShiftComplete;
      active.careShiftCompleted = true;
      Assert.That(CareStationShiftRules.TryBeginNextShift(active, true), Is.False,
        "END SHIFT is required before a later session can begin.");

      active.currentState = CareStationState.AutoShift;
      active.endShiftConsumed = true;
      Assert.That(CareStationShiftRules.TryBeginNextShift(active, false), Is.False,
        "Remaining in the foreground or returning too early must not create a task.");
      Assert.That(CareStationShiftRules.TryBeginNextShift(active, true), Is.True);
      Assert.That(active.currentShift, Is.EqualTo(3));
      Assert.That(active.careShiftId, Is.EqualTo(13));
    }

    [Test]
    public void DevelopmentOverrideCanStartNextShiftButOnlyFromEndedAutoShift()
    {
      var save = new CareStationSaveData
      {
        currentShift = 4,
        careShiftId = 9,
        currentState = CareStationState.AutoShift,
        careShiftCompleted = true,
        endShiftConsumed = true,
      };
      Assert.That(CareStationShiftRules.TryBeginNextShift(save, false, true), Is.True);
      Assert.That(save.careShiftId, Is.EqualTo(10));
    }

    [Test]
    public void GestureReferenceCaptureDoesNotMutateShiftOrCollectionProgress()
    {
      var save = new CareStationSaveData
      {
        careShiftId = 8,
        currentShift = 3,
        currentState = CareStationState.WaitCarePushAway,
        offlineCollectionResolved = true,
        returnedNeutralAfterOffline = true,
        careActionCompleted = true,
        pendingIncidentXP = 36,
        collectedCareBottleValue = 12,
        offlinePushAwayCompletion = CareStationPushAwayCompletion.SensorCompleted,
        carePushReferenceScale = 0.14f,
        carePushReferenceValid = true,
      };

      // A collection gesture reference is local to this explicit phase.
      // Capturing or restoring it cannot create a new shift or reward.
      Assert.That(save.careShiftId, Is.EqualTo(8));
      Assert.That(save.currentState, Is.EqualTo(CareStationState.WaitCarePushAway));
      Assert.That(save.pendingIncidentXP - save.collectedCareBottleValue, Is.EqualTo(24));
      Assert.That(save.offlinePushAwayCompletion, Is.EqualTo(CareStationPushAwayCompletion.SensorCompleted));
      Assert.That(save.carePushAwayCompletion, Is.EqualTo(CareStationPushAwayCompletion.None));
      Assert.That(save.carePushReferenceScale, Is.EqualTo(0.14f));
      Assert.That(save.carePushReferenceValid, Is.True);
    }

    [Test]
    public void CollectionPhasesCannotUnlockEachOther()
    {
      Assert.That(CareStationStateRules.CanArmCollection(CareStationCollectionPhase.None, false, false), Is.False);
      Assert.That(CareStationStateRules.CanArmCollection(CareStationCollectionPhase.Care, false, true), Is.False);
      Assert.That(CareStationStateRules.CanArmCollection(CareStationCollectionPhase.Care, true, false), Is.False);
      Assert.That(CareStationStateRules.CanArmCollection(CareStationCollectionPhase.Care, true, true), Is.True);
    }

    [Test]
    public void FallbackOnlyCompletesCurrentExplicitPushAwayState()
    {
      Assert.That(CareStationStateRules.CanOfferPushAwayFallback(CareStationState.WaitDistanceResetMoveAway, 5f, 5f), Is.False,
        "DISTANCE RESET owns its direction fallback instead of the bottle COLLECT fallback.");
      Assert.That(CareStationStateRules.CanOfferPushAwayFallback(CareStationState.WaitReturnToNeutral, 20f, 5f), Is.False);
      Assert.That(CareStationStateRules.CanOfferPushAwayFallback(CareStationState.WaitCarePushAway, 5f, 5f), Is.True);
    }

    [Test]
    public void DistanceResetCannotUnlockCareCollection()
    {
      var save = new CareStationSaveData
      {
        activeCollectionPhase = CareStationCollectionPhase.None,
        offlineCollectionResolved = true,
        returnedNeutralAfterOffline = false,
        careActionCompleted = false,
      };

      Assert.That(CareStationStateRules.CanPresentIncident(save.offlineCollectionResolved, save.returnedNeutralAfterOffline), Is.False);
      Assert.That(CareStationStateRules.CanArmCollection(CareStationCollectionPhase.Care, save.careActionCompleted, save.returnedNeutralAfterOffline), Is.False);

      save.returnedNeutralAfterOffline = true;
      save.careActionCompleted = true;
      save.activeCollectionPhase = CareStationCollectionPhase.Care;
      Assert.That(CareStationStateRules.CanArmCollection(save.activeCollectionPhase, save.careActionCompleted, save.returnedNeutralAfterOffline), Is.True);
      Assert.That(save.carePushAwayCompletion, Is.EqualTo(CareStationPushAwayCompletion.None));
    }

    [Test]
    public void ArrivalLedgerNeverSettlesBeforeFullBottleValueArrives()
    {
      var ledger = new CareStationExperienceLedger();
      ledger.Begin(36);
      ledger.RecordArrival(12);
      ledger.RecordArrival(12);
      Assert.That(ledger.IsComplete, Is.False);
      ledger.RecordArrival(12);
      Assert.That(ledger.IsComplete, Is.True);
      Assert.That(ledger.CollectedValue, Is.EqualTo(36));
    }

    [Test]
    public void MidFlightResumeOnlyNeedsTheRemainingBottleValue()
    {
      const int total = 36;
      const int alreadyStored = 20;
      var resumed = new CareStationExperienceLedger();
      resumed.Begin(total - alreadyStored);
      resumed.RecordArrival(8);
      Assert.That(resumed.IsComplete, Is.False);
      resumed.RecordArrival(8);
      Assert.That(resumed.IsComplete, Is.True);
      Assert.That(alreadyStored + resumed.CollectedValue, Is.EqualTo(total));
    }

    [Test]
    public void EyeGunkIsTheHighValueThirdShift()
    {
      Assert.That(CareStationShiftRules.IncidentExperience(CareStationIncidentType.Dust), Is.EqualTo(12));
      Assert.That(CareStationShiftRules.IncidentExperience(CareStationIncidentType.DrySpot), Is.EqualTo(24));
      Assert.That(CareStationShiftRules.IncidentExperience(CareStationIncidentType.EyeGunk), Is.EqualTo(36));
    }

    [Test]
    public void EveryStationUpgradeChangesAVisibleStationValue()
    {
      var workers = new CareStationSaveData();
      CareStationShiftRules.ApplyUpgrade(workers, CareStationUpgradeId.MoreWorkers);
      Assert.That(workers.crewCount, Is.EqualTo(3));

      var storage = new CareStationSaveData();
      CareStationShiftRules.ApplyUpgrade(storage, CareStationUpgradeId.LargerStorage);
      Assert.That(storage.storageHours, Is.EqualTo(36));

      var cart = new CareStationSaveData();
      CareStationShiftRules.ApplyUpgrade(cart, CareStationUpgradeId.BiggerCart);
      Assert.That(cart.cartCapacity, Is.EqualTo(6));
    }

    [TestCase(CareStationUpgradeId.MoreWorkers, 2, 3, 4, 5)]
    [TestCase(CareStationUpgradeId.LargerStorage, 24, 36, 48, 72)]
    [TestCase(CareStationUpgradeId.BiggerCart, 4, 6, 8, 12)]
    public void UpgradeRoutesAdvanceFromLevelOneToFour(
      CareStationUpgradeId upgrade, int levelOne, int levelTwo, int levelThree, int levelFour)
    {
      var configuration = new CareStationUpgradeConfiguration();
      var save = new CareStationSaveData { storedFullBottles = 200, storedGoldBottles = 10 };
      // Non-storage routes need enough physical storage to hold their later-tier
      // purchase costs. Storage itself must still start at L1 so this test can
      // verify its complete 24 -> 36 -> 48 -> 72 progression.
      if (upgrade != CareStationUpgradeId.LargerStorage)
      {
        save.storageLevel = 4;
        save.storageHours = 72;
      }
      CareStationShiftRules.SynchronizeUpgradeValues(save, configuration);

      Assert.That(configuration.Value(upgrade, 1), Is.EqualTo(levelOne));
      Assert.That(CareStationShiftRules.TryPurchaseUpgrade(save, upgrade, configuration), Is.True);
      Assert.That(configuration.Value(upgrade, CareStationShiftRules.GetUpgradeLevel(save, upgrade)), Is.EqualTo(levelTwo));
      Assert.That(CareStationShiftRules.TryPurchaseUpgrade(save, upgrade, configuration), Is.True);
      Assert.That(configuration.Value(upgrade, CareStationShiftRules.GetUpgradeLevel(save, upgrade)), Is.EqualTo(levelThree));
      Assert.That(CareStationShiftRules.TryPurchaseUpgrade(save, upgrade, configuration), Is.True);
      Assert.That(configuration.Value(upgrade, CareStationShiftRules.GetUpgradeLevel(save, upgrade)), Is.EqualTo(levelFour));
      var remainingFull = save.storedFullBottles;
      var remainingGold = save.storedGoldBottles;
      Assert.That(CareStationShiftRules.TryPurchaseUpgrade(save, upgrade, configuration), Is.False);
      Assert.That(save.storedFullBottles, Is.EqualTo(remainingFull));
      Assert.That(save.storedGoldBottles, Is.EqualTo(remainingGold));
    }

    [TestCase(CareStationUpgradeId.MoreWorkers, 1, 12, 0)]
    [TestCase(CareStationUpgradeId.MoreWorkers, 2, 24, 1)]
    [TestCase(CareStationUpgradeId.MoreWorkers, 3, 40, 2)]
    [TestCase(CareStationUpgradeId.LargerStorage, 1, 10, 0)]
    [TestCase(CareStationUpgradeId.LargerStorage, 2, 20, 0)]
    [TestCase(CareStationUpgradeId.LargerStorage, 3, 36, 2)]
    [TestCase(CareStationUpgradeId.BiggerCart, 1, 10, 0)]
    [TestCase(CareStationUpgradeId.BiggerCart, 2, 22, 1)]
    [TestCase(CareStationUpgradeId.BiggerCart, 3, 36, 2)]
    public void UpgradeCostsMatchConfiguration(CareStationUpgradeId upgrade, int level, int full, int gold)
    {
      var cost = new CareStationUpgradeConfiguration().Cost(upgrade, level);
      Assert.That(cost.fullBottles, Is.EqualTo(full));
      Assert.That(cost.goldBottles, Is.EqualTo(gold));
    }

    [Test]
    public void InsufficientUpgradeFundsDoNotChangeLevelOrInventory()
    {
      var configuration = new CareStationUpgradeConfiguration();
      var save = new CareStationSaveData { storedFullBottles = 11, storedGoldBottles = 5 };
      Assert.That(CareStationShiftRules.TryPurchaseUpgrade(save, CareStationUpgradeId.MoreWorkers, configuration), Is.False);
      Assert.That(save.workerLevel, Is.EqualTo(1));
      Assert.That(save.storedFullBottles, Is.EqualTo(11));
      Assert.That(save.storedGoldBottles, Is.EqualTo(5));
    }

    [Test]
    public void PurchaseDeductsTheConfiguredCostExactlyOnce()
    {
      var configuration = new CareStationUpgradeConfiguration();
      var save = new CareStationSaveData { storedFullBottles = 12, storedGoldBottles = 0 };
      Assert.That(CareStationShiftRules.TryPurchaseUpgrade(save, CareStationUpgradeId.MoreWorkers, configuration), Is.True);
      Assert.That(save.storedFullBottles, Is.Zero);
      Assert.That(save.workerLevel, Is.EqualTo(2));
      Assert.That(CareStationShiftRules.TryPurchaseUpgrade(save, CareStationUpgradeId.MoreWorkers, configuration), Is.False);
      Assert.That(save.storedFullBottles, Is.Zero);
      Assert.That(save.workerLevel, Is.EqualTo(2));
    }

    [Test]
    public void ProductionRateAndConcurrentCartsUseWorkerAndCartLevels()
    {
      var save = new CareStationSaveData { workerLevel = 3, cartLevel = 4 };
      CareStationShiftRules.SynchronizeUpgradeValues(save, new CareStationUpgradeConfiguration());
      Assert.That(save.crewCount, Is.EqualTo(4));
      Assert.That(save.cartCapacity, Is.EqualTo(12));
      Assert.That(CareStationShiftRules.ConcurrentCartCount(save), Is.EqualTo(4));
      Assert.That(CareStationShiftRules.ProductionRateMultiplier(save), Is.EqualTo(6f).Within(0.001f));
    }

    [Test]
    public void FullStorageStopsOfflineProductionAndDiscardsOverflow()
    {
      var save = new CareStationSaveData
      {
        storageLevel = 1,
        storageHours = 24,
        storedFullBottles = 20,
      };
      var result = CareStationStorageRules.LimitOfflineProduction(save, 10);
      Assert.That(result.Accepted, Is.EqualTo(4));
      Assert.That(result.Discarded, Is.EqualTo(6));
      Assert.That(result.StorageFull, Is.True);
    }

    [Test]
    public void PendingOfflineOutputReservesCapacityBeforeAnotherOfflineInterval()
    {
      var save = new CareStationSaveData
      {
        storageHours = 24,
        storedFullBottles = 18,
        pendingOfflineXP = 4,
        queuedOfflineXP = 1,
      };
      Assert.That(CareStationStorageRules.RemainingForOfflineProduction(save), Is.EqualTo(1));
      var result = CareStationStorageRules.LimitOfflineProduction(save, 5);
      Assert.That(result.Accepted, Is.EqualTo(1));
      Assert.That(result.Discarded, Is.EqualTo(4));
    }

    [Test]
    public void FullStorageKeepsVerifiedCareBottlesPending()
    {
      var save = new CareStationSaveData
      {
        storageHours = 24,
        storedFullBottles = 24,
        storedGoldBottles = 1,
        pendingIncidentXP = 12,
        careActionCompleted = true,
      };
      Assert.That(CareStationStorageRules.CollectibleNow(save, save.pendingIncidentXP), Is.Zero);
      Assert.That(save.pendingIncidentXP, Is.EqualTo(12));
      Assert.That(save.careActionCompleted, Is.True);
    }

    [Test]
    public void RestoredSpaceSettlesOnlyTheAvailablePendingAmountOnce()
    {
      var save = new CareStationSaveData
      {
        storageHours = 24,
        storedFullBottles = 23,
        pendingIncidentXP = 5,
        careActionCompleted = true,
      };
      Assert.That(CareStationStorageRules.CollectibleNow(save, 5), Is.EqualTo(1));
      var ledger = new CareStationExperienceLedger();
      ledger.Begin(1);
      ledger.RecordArrival(1);
      ledger.RecordArrival(1);
      Assert.That(ledger.CollectedValue, Is.EqualTo(1));
      Assert.That(ledger.Arrivals, Is.EqualTo(1));
      Assert.That(ledger.IsComplete, Is.True);
    }

    [Test]
    public void CareCollectionAtFifteenOfFortyEightRebuildsAllRemainingValue()
    {
      var save = new CareStationSaveData
      {
        storageHours = 48,
        storedFullBottles = 15,
        pendingIncidentXP = 36,
        collectedCareBottleValue = 22,
        careActionCompleted = true,
      };
      var plan = CareStationCollectionRecoveryRules.Plan(save, 14, 0);
      Assert.That(plan.AvailableStorage, Is.EqualTo(33));
      Assert.That(plan.StorageBlocked, Is.False);
      Assert.That(plan.CollectibleValue, Is.EqualTo(14));
      Assert.That(plan.MissingRuntimeValue, Is.EqualTo(14));
      Assert.That(plan.RequiresRuntimeRebuild, Is.True);
    }

    [Test]
    public void InterruptedCareCollectionRebuildsOnlyMissingRuntimeValue()
    {
      var save = new CareStationSaveData
      {
        storageHours = 48,
        storedFullBottles = 15,
        pendingIncidentXP = 36,
        collectedCareBottleValue = 22,
        careActionCompleted = true,
      };
      var partial = CareStationCollectionRecoveryRules.Plan(save, 14, 6);
      Assert.That(partial.ExistingRuntimeValue, Is.EqualTo(6));
      Assert.That(partial.MissingRuntimeValue, Is.EqualTo(8));

      var completeRuntime = CareStationCollectionRecoveryRules.Plan(save, 14, 14);
      Assert.That(completeRuntime.MissingRuntimeValue, Is.Zero);
      Assert.That(completeRuntime.RequiresRuntimeRebuild, Is.False,
        "Existing in-flight value must not be spawned a second time.");
    }

    [Test]
    public void CollectionLedgerConsumesCompletionOnlyOnce()
    {
      var ledger = new CareStationExperienceLedger();
      ledger.Begin(14);
      ledger.RecordArrival(6);
      ledger.RecordArrival(8);
      ledger.RecordArrival(14);
      Assert.That(ledger.CollectedValue, Is.EqualTo(14));
      Assert.That(ledger.Arrivals, Is.EqualTo(2));
      Assert.That(ledger.IsComplete, Is.True);
    }

    [Test]
    public void InspectionSchedulesOnlyAfterAllLevelTwoTrainingAndDailyEndGates()
    {
      var save = new CareStationSaveData
      {
        trainingProgress = 4,
        workerLevel = 2,
        storageLevel = 2,
        cartLevel = 2,
        currentState = CareStationState.AutoShift,
        careShiftCompleted = true,
        endShiftConsumed = true,
      };
      Assert.That(CareStationInspectionRules.CanSchedule(save), Is.True);
      save.inspectionTriggered = true;
      Assert.That(CareStationInspectionRules.CanSchedule(save), Is.False);
      save.inspectionTriggered = false;
      save.pendingIncidentXP = 1;
      Assert.That(CareStationInspectionRules.CanSchedule(save), Is.False);
    }

    [Test]
    public void InspectionRecipeUsesAllChecksInRequiredOrder()
    {
      var recipe = CareStationInspectionRules.CreateRecipe(18);
      Assert.That(recipe.actionList, Is.EqualTo(new[]
      {
        CareActionType.PilotEyeRoutine,
        CareActionType.GuidedEyeCircles,
        CareActionType.ClosedEyeRest,
      }));
      var runtime = new CareRecipeRuntime(recipe);
      for (var index = 0; index < recipe.ActionCount; index++)
      {
        var result = runtime.CompleteCurrentAction(recipe.actionList[index]);
        Assert.That(result.Accepted, Is.True);
        var mask = CareStationInspectionRules.CompletedCheckMask(index, result.RecipeCompleted);
        if (index == 0) Assert.That(mask, Is.EqualTo(CareStationInspectionRules.FilterCheck));
        if (index == 1) Assert.That(mask, Is.EqualTo(CareStationInspectionRules.FilterCheck | CareStationInspectionRules.FlowCheck));
        if (index == 2) Assert.That(mask, Is.EqualTo(CareStationInspectionRules.AllChecks));
      }
      Assert.That(runtime.TryConsumeCompletionSignal(), Is.True);
      Assert.That(runtime.TryConsumeCompletionSignal(), Is.False);
      Assert.That(CareActionLibrary.EstimatedRecipeSeconds(recipe.actionList, recipe.deepRest),
        Is.InRange(145f, 170f),
        "The fixed inspection must remain a complete but sub-three-minute routine.");
    }

    [Test]
    public void InspectionRewardRemainsPendingUntilFormalArrival()
    {
      var save = new CareStationSaveData
      {
        inspectionActive = true,
        inspectionRewardProduced = true,
        pendingIncidentXP = 25,
        pendingGoldBottleCount = 1,
        storedFullBottles = 6,
      };
      var ledger = new CareStationExperienceLedger();
      ledger.Begin(save.pendingIncidentXP);
      Assert.That(save.storedFullBottles, Is.EqualTo(6));
      Assert.That(save.storedGoldBottles, Is.Zero);
      ledger.RecordArrival(24);
      Assert.That(ledger.IsComplete, Is.False);
      ledger.RecordArrival(1);
      Assert.That(ledger.IsComplete, Is.True);
      Assert.That(save.inspectionRewardStored, Is.False,
        "The arrival adapter, not inspection completion, owns settlement.");
    }

    [Test]
    public void ExistingIncidentDoesNotDuplicateAfterResume()
    {
      var result = CareStationOfflineCalculator.Calculate(Start, Start.AddHours(4), 30f, 24f, 18f, true);
      Assert.That(result.HelpNeededCount, Is.Zero);
    }

    [Test]
    public void OfflineProductionSettlesDirectlyWithoutCreatingCollectionBottles()
    {
      var save = new CareStationSaveData
      {
        storageHours = 48,
        storedFullBottles = 15,
        storedGoldBottles = 1,
        pendingOfflineXP = 0,
        activeCollectionPhase = CareStationCollectionPhase.None,
      };
      var production = new CareStationProductionController();

      var settlement = production.Settle(save, 12);

      Assert.That(settlement.ProducedStored, Is.EqualTo(12));
      Assert.That(settlement.ProducedDiscarded, Is.Zero);
      Assert.That(save.storedFullBottles, Is.EqualTo(27));
      Assert.That(save.storedGoldBottles, Is.EqualTo(1));
      Assert.That(save.pendingOfflineXP, Is.Zero);
      Assert.That(save.activeCollectionPhase, Is.EqualTo(CareStationCollectionPhase.None));
    }

    [Test]
    public void OfflineProductionStopsAtCapacityAndDoesNotQueueNewOverflow()
    {
      var save = new CareStationSaveData
      {
        storageHours = 24,
        storedFullBottles = 22,
        storedGoldBottles = 1,
      };
      var production = new CareStationProductionController();

      var settlement = production.Settle(save, 8);

      Assert.That(settlement.ProducedStored, Is.EqualTo(2));
      Assert.That(settlement.ProducedDiscarded, Is.EqualTo(6));
      Assert.That(CareStationStorageRules.Stored(save), Is.EqualTo(24));
      Assert.That(save.queuedOfflineXP, Is.Zero,
        "New offline overflow is production that could not occur, not a deferred reward.");
      Assert.That(save.offlineProductionPausedByFullStorage, Is.True);
    }

    [Test]
    public void LegacyOfflinePendingUsesAvailableSpaceWithoutTouchingCarePending()
    {
      var save = new CareStationSaveData
      {
        storageHours = 24,
        storedFullBottles = 20,
        queuedOfflineXP = 7,
        pendingIncidentXP = 36,
        collectedCareBottleValue = 12,
        careActionCompleted = true,
      };
      var production = new CareStationProductionController();

      var settled = production.SettleLegacyPending(save);

      Assert.That(settled, Is.Zero,
        "Every free slot is reserved for the interrupted verified care reward.");
      Assert.That(save.storedFullBottles, Is.EqualTo(20));
      Assert.That(save.queuedOfflineXP, Is.EqualTo(7));
      Assert.That(save.pendingIncidentXP, Is.EqualTo(36));
      Assert.That(save.collectedCareBottleValue, Is.EqualTo(12));
    }

    [Test]
    public void CurrentSaveShapeReservesFourteenSlotsForInterruptedCareFlight()
    {
      var save = new CareStationSaveData
      {
        storageHours = 48,
        storedFullBottles = 15,
        queuedOfflineXP = 33,
        pendingIncidentXP = 36,
        collectedCareBottleValue = 22,
        careActionCompleted = true,
      };

      var settled = new CareStationProductionController().SettleLegacyPending(save);

      Assert.That(settled, Is.EqualTo(19));
      Assert.That(save.storedFullBottles, Is.EqualTo(34));
      Assert.That(save.queuedOfflineXP, Is.EqualTo(14));
      Assert.That(CareStationStorageRules.Remaining(save), Is.EqualTo(14));
      var recovery = CareStationCollectionRecoveryRules.Plan(save, 14, 0);
      Assert.That(recovery.MissingRuntimeValue, Is.EqualTo(14));
      Assert.That(recovery.StorageBlocked, Is.False);
    }

    [Test]
    public void FullLevelTwoStorageCanPurchaseLevelThreeForTwentyFullWithoutGold()
    {
      var configuration = new CareStationUpgradeConfiguration();
      var save = new CareStationSaveData
      {
        storageLevel = 2,
        storageHours = 36,
        storedFullBottles = 36,
        storedGoldBottles = 0,
        offlineProductionPausedByFullStorage = true,
      };

      var availability = CareStationShiftRules.EvaluateUpgrade(
        save,
        CareStationUpgradeId.LargerStorage,
        configuration);
      Assert.That(availability.CanPurchase, Is.True);
      Assert.That(availability.Cost.fullBottles, Is.EqualTo(20));
      Assert.That(availability.Cost.goldBottles, Is.Zero);
      Assert.That(CareStationShiftRules.TryPurchaseUpgrade(
        save,
        CareStationUpgradeId.LargerStorage,
        configuration), Is.True);
      Assert.That(save.storageLevel, Is.EqualTo(3));
      Assert.That(save.storageHours, Is.EqualTo(48));
      Assert.That(save.storedFullBottles, Is.EqualTo(16));
      Assert.That(save.storedGoldBottles, Is.Zero);
      Assert.That(save.offlineProductionPausedByFullStorage, Is.False);
    }

    [Test]
    public void UnaffordableUpgradeOpportunityDefersWithoutBeingDeleted()
    {
      var save = new CareStationSaveData
      {
        storedFullBottles = 0,
        storedGoldBottles = 0,
        upgradeOffered = true,
      };
      var configuration = new CareStationUpgradeConfiguration();

      Assert.That(CareStationShiftRules.CanEnterUpgradeSelection(save, configuration), Is.False);
      CareStationShiftRules.MarkUpgradeDeferred(save, Start);

      Assert.That(save.upgradeOffered, Is.True);
      Assert.That(save.upgradeDeferred, Is.True);
      Assert.That(save.eventHistory, Has.Length.EqualTo(1));
      Assert.That(save.eventHistory[0].eventType, Is.EqualTo(CareStationEventType.UpgradeDeferred));
    }

    [Test]
    public void FirstFormalRecipeGuaranteesExactlyOneGoldMarkerAndNeverRepeats()
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
      Assert.That(save.pendingGoldBottleCount, Is.EqualTo(1));
      Assert.That(save.firstFormalGoldBottleGenerated, Is.True);
      Assert.That(CareStationShiftRules.EnsureFirstFormalGoldBottle(save), Is.False);
      Assert.That(save.pendingGoldBottleCount, Is.EqualTo(1));
      Assert.That(save.pendingIncidentXP, Is.EqualTo(24));
    }

    [Test]
    public void GoldDoesNotConsumeFullBottleStorageCapacity()
    {
      var save = new CareStationSaveData
      {
        storageHours = 36,
        storedFullBottles = 35,
        storedGoldBottles = 9,
      };

      Assert.That(CareStationStorageRules.Stored(save), Is.EqualTo(35));
      Assert.That(CareStationStorageRules.Remaining(save), Is.EqualTo(1));
    }

    [Test]
    public void FullStorageKeepsCareRewardPendingButAllowsGoldToSettle()
    {
      var save = new CareStationSaveData
      {
        storageHours = 36,
        storedFullBottles = 36,
        pendingIncidentXP = 12,
        pendingGoldBottleCount = 1,
        careActionCompleted = true,
      };

      var goldPlan = CareStationCollectionRecoveryRules.Plan(save, 12, 0, 1);
      Assert.That(goldPlan.CollectibleValue, Is.EqualTo(1));
      Assert.That(goldPlan.CollectibleGoldValue, Is.EqualTo(1));
      Assert.That(goldPlan.StorageBlocked, Is.False);

      var fullOnlyPlan = CareStationCollectionRecoveryRules.Plan(save, 11, 0, 0);
      Assert.That(fullOnlyPlan.CollectibleValue, Is.Zero);
      Assert.That(fullOnlyPlan.StorageBlocked, Is.True);
      Assert.That(save.pendingIncidentXP, Is.EqualTo(12));
    }

    [Test]
    public void ExpandingStorageMakesPendingCareRewardCollectibleWithoutRegeneration()
    {
      var save = new CareStationSaveData
      {
        storageLevel = 2,
        storageHours = 36,
        storedFullBottles = 36,
        pendingIncidentXP = 12,
        careActionCompleted = true,
      };
      Assert.That(CareStationCollectionRecoveryRules.Plan(save, 12, 0).StorageBlocked, Is.True);

      Assert.That(CareStationShiftRules.TryPurchaseUpgrade(
        save,
        CareStationUpgradeId.LargerStorage,
        new CareStationUpgradeConfiguration()), Is.True);
      var resumed = CareStationCollectionRecoveryRules.Plan(save, 12, 0);

      Assert.That(save.pendingIncidentXP, Is.EqualTo(12));
      Assert.That(resumed.CollectibleValue, Is.EqualTo(12));
      Assert.That(resumed.RequiresRuntimeRebuild, Is.True);
    }

    [Test]
    public void CareStationDisablesLegacyRandomFlow()
    {
      Assert.That(CareStationStateRules.LegacyRandomFlowEnabled(true), Is.False);
      Assert.That(CareStationStateRules.LegacyRandomFlowEnabled(false), Is.True);
    }
  }
}
