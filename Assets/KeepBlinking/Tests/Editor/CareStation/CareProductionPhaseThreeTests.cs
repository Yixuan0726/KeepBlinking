using System;
using System.IO;
using KeepBlinking.CareStation;
using NUnit.Framework;
using UnityEngine;

namespace KeepBlinking.Tests
{
  public sealed class CareProductionPhaseThreeTests
  {
    private string _directory;
    private string _path;

    [SetUp]
    public void SetUp()
    {
      _directory = Path.Combine(Path.GetTempPath(), "KeepBlinkingProductionV22", Guid.NewGuid().ToString("N"));
      _path = Path.Combine(_directory, "care_station.json");
      Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
      if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Test]
    public void ForegroundCycleUsesEveryStageInOrderAndConsumesEnergyOnce()
    {
      var save = new CareStationSaveData { careEnergy = 2, storageHours = 24 };
      var configuration = FastConfiguration();
      var expected = new[]
      {
        CareProductionStage.FilterProcessing,
        CareProductionStage.TransferFilteredLiquid,
        CareProductionStage.FillerCreateBottle,
        CareProductionStage.FillerFilling,
        CareProductionStage.FillerFilled,
        CareProductionStage.TransferToPacker,
        CareProductionStage.PackerCapping,
        CareProductionStage.PackerLabeling,
        CareProductionStage.PackerPackaging,
        CareProductionStage.TransferToStorage,
      };

      Assert.That(CareProductionRules.TryBeginForegroundCycle(save, "recipe-a"), Is.True);
      Assert.That(save.careEnergy, Is.EqualTo(1));
      foreach (var stage in expected)
      {
        Assert.That(save.productionStage, Is.EqualTo(stage));
        CareProductionRules.AdvanceForegroundCycle(save, configuration.Duration(stage), configuration);
      }

      Assert.That(save.productionStage, Is.EqualTo(CareProductionStage.None));
      Assert.That(save.productionCycleStored, Is.True);
      Assert.That(save.storedFullBottles, Is.EqualTo(1));
      Assert.That(save.careEnergy, Is.EqualTo(1));
      Assert.That(CareProductionRules.AdvanceForegroundCycle(save, 100f, configuration).BottleStored, Is.False);
      Assert.That(CareProductionRules.TryBeginForegroundCycle(save, "recipe-a"), Is.False);
      Assert.That(save.storedFullBottles, Is.EqualTo(1));
      Assert.That(save.careEnergy, Is.EqualTo(1));
    }

    [Test]
    public void EmptyBottleDoesNotExistBeforeFillerStage()
    {
      Assert.That(CareProductionStage.FilterProcessing, Is.LessThan(CareProductionStage.FillerCreateBottle));
      Assert.That(CareProductionStage.TransferFilteredLiquid, Is.LessThan(CareProductionStage.FillerCreateBottle));
      Assert.That(CareProductionStage.FillerCreateBottle, Is.LessThan(CareProductionStage.FillerFilling));
      Assert.That(CareProductionStage.FillerFilling, Is.LessThan(CareProductionStage.FillerFilled));
    }

    [Test]
    public void MidStageReloadPreservesStageProgressAndDoesNotDeductAgain()
    {
      var now = new DateTime(2026, 8, 30, 14, 0, 0, DateTimeKind.Utc);
      var save = new CareStationSaveData { careEnergy = 3, storageHours = 24 };
      Assert.That(CareProductionRules.TryBeginForegroundCycle(save, "recipe-reload"), Is.True);
      save.productionStage = CareProductionStage.FillerFilling;
      save.productionStageElapsedSeconds = 0.42f;
      new CareStationSaveService(_path).Save(save, now);

      var restored = new CareStationSaveService(_path).Load(now.AddSeconds(2));

      Assert.That(restored.productionStage, Is.EqualTo(CareProductionStage.FillerFilling));
      Assert.That(restored.productionStageElapsedSeconds, Is.EqualTo(0.42f).Within(0.001f));
      Assert.That(restored.careEnergy, Is.EqualTo(2));
      Assert.That(restored.productionCycleEnergyConsumed, Is.True);
      Assert.That(CareProductionRules.TryBeginForegroundCycle(restored, "recipe-reload"), Is.False);
      Assert.That(restored.careEnergy, Is.EqualTo(2));
    }

    [Test]
    public void FullStoragePausesBeforeCycleAndPreservesCareEnergy()
    {
      var save = new CareStationSaveData
      {
        careEnergy = 12,
        storageHours = 24,
        storedFullBottles = 24,
      };

      Assert.That(CareProductionRules.TryBeginForegroundCycle(save, "full-rack"), Is.False);
      Assert.That(save.productionStage, Is.EqualTo(CareProductionStage.None));
      Assert.That(save.careEnergy, Is.EqualTo(12));
      Assert.That(save.storedFullBottles, Is.EqualTo(24));
    }

    [Test]
    public void InProgressPackedBottleSurvivesFullStorageThenSettlesOnceAfterCart()
    {
      var configuration = FastConfiguration();
      var save = new CareStationSaveData
      {
        careEnergy = 2,
        storageHours = 24,
        storedFullBottles = 23,
      };
      Assert.That(CareProductionRules.TryBeginForegroundCycle(save, "waiting-bottle"), Is.True);
      save.storedFullBottles = 24; // Simulates an external legacy fill during the saved cycle.
      var waiting = CareProductionRules.AdvanceForegroundCycle(save, 20f, configuration);

      Assert.That(waiting.WaitingForStorage, Is.True);
      Assert.That(save.productionStage, Is.EqualTo(CareProductionStage.WaitingForStorage));
      Assert.That(save.careEnergy, Is.EqualTo(1));
      Assert.That(save.productionCycleStored, Is.False);

      var sale = CareEconomyRules.SettleCart(save, 1, "free-slot", new CareEconomyConfiguration());
      Assert.That(sale.FullBottlesSold, Is.EqualTo(1));
      var stored = CareProductionRules.AdvanceForegroundCycle(save, 0f, configuration);
      Assert.That(stored.BottleStored, Is.True);
      Assert.That(save.storedFullBottles, Is.EqualTo(24));
      Assert.That(save.careEnergy, Is.EqualTo(1));
      Assert.That(CareProductionRules.AdvanceForegroundCycle(save, 0f, configuration).BottleStored, Is.False);
      Assert.That(save.storedFullBottles, Is.EqualTo(24));
    }

    [Test]
    public void ActiveCycleReservesItsStorageSlotFromOfflineProduction()
    {
      var save = new CareStationSaveData
      {
        careEnergy = 10,
        storageHours = 24,
        storedFullBottles = 23,
      };
      Assert.That(CareProductionRules.TryBeginForegroundCycle(save, "reserve-slot"), Is.True);

      var settlement = CareEconomyRules.SettleCart(
        save, 8, "reserved-offline", new CareEconomyConfiguration());

      Assert.That(settlement.BottlesProduced, Is.Zero);
      Assert.That(save.productionStage, Is.EqualTo(CareProductionStage.FilterProcessing));
      Assert.That(save.careEnergy, Is.EqualTo(9));
    }

    [Test]
    public void OfflineFastForwardUsesCareEnergyCapacityAndCartCoinRules()
    {
      var save = new CareStationSaveData
      {
        careEnergy = 5,
        storageHours = 24,
        storedFullBottles = 2,
        pendingPremiumShipment = 1,
      };

      var settlement = CareEconomyRules.SettleCart(
        save, 3, "offline-fast-forward", new CareEconomyConfiguration());

      Assert.That(settlement.FullBottlesSold, Is.EqualTo(2));
      Assert.That(settlement.PremiumBottlesSold, Is.EqualTo(1));
      Assert.That(settlement.CoinsEarned, Is.EqualTo(7));
      Assert.That(settlement.BottlesProduced, Is.EqualTo(3));
      Assert.That(save.storedFullBottles, Is.EqualTo(3));
      Assert.That(save.careEnergy, Is.EqualTo(2));
      Assert.That(save.coins, Is.EqualTo(7));
    }

    [Test]
    public void VersionTwentyOnePendingBottleMigratesIntoProductionLineWithoutAnotherCharge()
    {
      var legacy = new CareStationSaveData
      {
        saveVersion = 21,
        currentState = CareStationState.WaitCarePushAway,
        careEnergy = 9,
        pendingFullBottleShipment = 1,
        currentRecipe = new CareRecipeSaveData
        {
          recipeId = "v21-pending",
          recipeType = CareRecipeType.Single,
          actionList = new[] { CareActionType.ClosedEyeRest },
          originalActionList = new[] { CareActionType.ClosedEyeRest },
          currentActionIndex = 1,
          completedActionMask = 1,
          recipeCompleted = true,
          careEnergyGranted = true,
          careEnergyGrantedAmount = 12,
        },
      };
      File.WriteAllText(_path, JsonUtility.ToJson(legacy, true));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);

      Assert.That(restored.saveVersion, Is.EqualTo(CareStationSaveService.CurrentVersion));
      Assert.That(restored.currentState, Is.EqualTo(CareStationState.ProduceBottles));
      Assert.That(restored.productionStage, Is.EqualTo(CareProductionStage.TransferToStorage));
      Assert.That(restored.productionCycleEnergyConsumed, Is.True);
      Assert.That(restored.pendingFullBottleShipment, Is.Zero);
      Assert.That(restored.careEnergy, Is.EqualTo(9));
    }

    private static CareProductionConfiguration FastConfiguration()
    {
      return new CareProductionConfiguration
      {
        filterSeconds = 0.1f,
        filteredTransferSeconds = 0.1f,
        createBottleSeconds = 0.1f,
        fillBottleSeconds = 0.1f,
        filledHoldSeconds = 0.1f,
        packerTransferSeconds = 0.1f,
        capSeconds = 0.1f,
        labelSeconds = 0.1f,
        packageSeconds = 0.1f,
        storageTransferSeconds = 0.1f,
      };
    }
  }
}
