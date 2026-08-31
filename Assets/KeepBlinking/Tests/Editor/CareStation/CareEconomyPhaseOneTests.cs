using System;
using System.IO;
using KeepBlinking.CareStation;
using NUnit.Framework;
using UnityEngine;

namespace KeepBlinking.Tests
{
  public sealed class CareEconomyPhaseOneTests
  {
    private string _directory;
    private string _path;

    [SetUp]
    public void SetUp()
    {
      _directory = Path.Combine(Path.GetTempPath(), "KeepBlinkingEconomyV21", Guid.NewGuid().ToString("N"));
      _path = Path.Combine(_directory, "care_station.json");
      Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
      if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [TestCase(CareRoutineId.FocusFlow, 4, 3)]
    [TestCase(CareRoutineId.PilotFlow, 4, 3)]
    [TestCase(CareRoutineId.DeepReset, 6, 2)]
    [TestCase(CareRoutineId.FullCare, 3, 4)]
    public void EveryCompletedRoutineStepGrantsItsSlotImmediatelyAndExactlyOnce(
      CareRoutineId routineId,
      int expectedPerStep,
      int expectedSteps)
    {
      var recipe = CareRecipeGenerator.CreateRoutine(routineId, 1, 17);
      var save = new CareStationSaveData { currentRecipe = recipe };
      var runtime = new CareRecipeRuntime(recipe);

      Assert.That(recipe.ActionCount, Is.EqualTo(expectedSteps));
      for (var step = 0; step < expectedSteps; step++)
      {
        var result = runtime.CompleteCurrentAction(runtime.CurrentAction);
        Assert.That(result.Accepted, Is.True);
        Assert.That(CareEconomyRules.TryGrantCompletedRecipeStep(save, step, out var granted), Is.True);
        Assert.That(granted, Is.EqualTo(expectedPerStep));
        Assert.That(CareEconomyRules.TryGrantCompletedRecipeStep(save, step, out var replay), Is.False);
        Assert.That(replay, Is.Zero);
      }

      Assert.That(CareEconomyRules.TryGrantRecipeCareEnergy(
        save, new CareEconomyConfiguration(), out var completionBonus), Is.False);
      Assert.That(completionBonus, Is.Zero);
      Assert.That(save.careEnergy, Is.EqualTo(12));
      Assert.That(recipe.careEnergyRewardedTotal, Is.EqualTo(12));
    }

    [Test]
    public void ForegroundBottleConsumesOneEnergyAndCannotReserveTwice()
    {
      var save = new CareStationSaveData { careEnergy = 2, storageHours = 24 };

      Assert.That(CareEconomyRules.TryReserveForegroundBottle(save), Is.True);
      Assert.That(save.careEnergy, Is.EqualTo(1));
      Assert.That(save.pendingFullBottleShipment, Is.EqualTo(1));
      Assert.That(CareEconomyRules.TryReserveForegroundBottle(save), Is.False);
      Assert.That(save.careEnergy, Is.EqualTo(1));

      Assert.That(CareEconomyRules.TryStoreReservedBottle(save), Is.True);
      Assert.That(save.storedFullBottles, Is.EqualTo(1));
      Assert.That(save.pendingFullBottleShipment, Is.Zero);
    }

    [Test]
    public void FullStoragePreservesEnergyAndCartStillFreesSpace()
    {
      var save = new CareStationSaveData
      {
        storageHours = 24,
        storedFullBottles = 24,
        careEnergy = 12,
      };

      Assert.That(CareEconomyRules.TryReserveForegroundBottle(save), Is.False);
      Assert.That(save.careEnergy, Is.EqualTo(12));
      var result = CareEconomyRules.SettleCart(
        save, 1, "full-rack-cart", new CareEconomyConfiguration());

      Assert.That(result.FullBottlesSold, Is.EqualTo(1));
      Assert.That(result.BottlesProduced, Is.Zero);
      Assert.That(save.storedFullBottles, Is.EqualTo(23));
      Assert.That(save.careEnergy, Is.EqualTo(12));
    }

    [Test]
    public void FullAndPremiumProductsSellForConfiguredCoinsAtomically()
    {
      var save = new CareStationSaveData
      {
        storedFullBottles = 3,
        pendingPremiumShipment = 2,
      };

      var result = CareEconomyRules.SettleCart(
        save, 2, "cart-sale-a", new CareEconomyConfiguration());

      Assert.That(result.FullBottlesSold, Is.EqualTo(2));
      Assert.That(result.PremiumBottlesSold, Is.EqualTo(2));
      Assert.That(result.CoinsEarned, Is.EqualTo(12));
      Assert.That(save.storedFullBottles, Is.EqualTo(1));
      Assert.That(save.pendingPremiumShipment, Is.Zero);
      Assert.That(save.coins, Is.EqualTo(12));
    }

    [Test]
    public void ReloadCannotRepeatCartSaleOrCoins()
    {
      var now = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
      var save = new CareStationSaveData { storedFullBottles = 4, pendingPremiumShipment = 1 };
      CareEconomyRules.SettleCart(save, 2, "stable-settlement", new CareEconomyConfiguration());
      new CareStationSaveService(_path).Save(save, now);

      var restored = new CareStationSaveService(_path).Load(now.AddMinutes(1));
      var coins = restored.coins;
      var bottles = restored.storedFullBottles;
      var replay = CareEconomyRules.SettleCart(
        restored, 2, "stable-settlement", new CareEconomyConfiguration());

      Assert.That(replay.AlreadySettled, Is.True);
      Assert.That(restored.coins, Is.EqualTo(coins));
      Assert.That(restored.storedFullBottles, Is.EqualTo(bottles));
    }

    [Test]
    public void UpgradeUsesCoinsOnlyAndConvertedCostIsCentralized()
    {
      var upgrades = new CareStationUpgradeConfiguration();
      var economy = new CareEconomyConfiguration();
      var save = new CareStationSaveData
      {
        coins = 29,
        storedFullBottles = 80,
        pendingPremiumShipment = 6,
        workerLevel = 2,
      };
      CareStationShiftRules.SynchronizeUpgradeValues(save, upgrades);

      var availability = CareStationShiftRules.EvaluateUpgrade(
        save, CareStationUpgradeId.MoreWorkers, upgrades, economy);
      Assert.That(availability.CoinCost, Is.EqualTo(24 + 5));
      Assert.That(CareStationShiftRules.TryPurchaseUpgrade(
        save, CareStationUpgradeId.MoreWorkers, upgrades, economy), Is.True);

      Assert.That(save.coins, Is.Zero);
      Assert.That(save.storedFullBottles, Is.EqualTo(80));
      Assert.That(save.pendingPremiumShipment, Is.EqualTo(6));
      Assert.That(save.workerLevel, Is.EqualTo(3));
    }

    [Test]
    public void InsufficientCoinsReportsExactNeedWithoutChangingLevel()
    {
      var save = new CareStationSaveData { coins = 4, workerLevel = 1 };
      var availability = CareStationShiftRules.EvaluateUpgrade(
        save,
        CareStationUpgradeId.MoreWorkers,
        new CareStationUpgradeConfiguration(),
        new CareEconomyConfiguration());

      Assert.That(availability.CanPurchase, Is.False);
      Assert.That(availability.PlayerReason, Is.EqualTo("NEED 8 COINS"));
      Assert.That(CareStationShiftRules.TryPurchaseUpgrade(
        save,
        CareStationUpgradeId.MoreWorkers,
        new CareStationUpgradeConfiguration(),
        new CareEconomyConfiguration()), Is.False);
      Assert.That(save.workerLevel, Is.EqualTo(1));
      Assert.That(save.coins, Is.EqualTo(4));
    }

    [Test]
    public void VersionTwentyIncidentAndGoldMigrateWithoutResettingRecipeOrLevels()
    {
      var legacy = new CareStationSaveData
      {
        saveVersion = 20,
        currentState = CareStationState.CareActionPaused,
        selectedIncident = CareStationIncidentType.DrySpot,
        pendingIncidentXP = 24,
        collectedCareBottleValue = 5,
        storedFullBottles = 17,
        storedGoldBottles = 3,
        pendingGoldBottleCount = 1,
        workerLevel = 3,
        storageLevel = 2,
        cartLevel = 4,
        careAction = new CareActionSaveData
        {
          actionType = CareActionType.FocusShift,
          stage = CareActionStage.Paused,
          elapsedSeconds = 31f,
        },
        currentRecipe = new CareRecipeSaveData
        {
          recipeId = "legacy_active_recipe",
          recipeType = CareRecipeType.Double,
          actionList = new[] { CareActionType.FocusShift, CareActionType.ClosedEyeRest },
          originalActionList = new[] { CareActionType.FocusShift, CareActionType.ClosedEyeRest },
          currentActionIndex = 0,
        },
      };
      File.WriteAllText(_path, JsonUtility.ToJson(legacy, true));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);

      Assert.That(restored.saveVersion, Is.EqualTo(CareStationSaveService.CurrentVersion));
      Assert.That(restored.careEnergy, Is.EqualTo(19));
      Assert.That(restored.pendingPremiumShipment, Is.EqualTo(4));
      Assert.That(restored.storedFullBottles, Is.EqualTo(17));
      Assert.That(restored.storedGoldBottles, Is.Zero);
      Assert.That(restored.pendingIncidentXP, Is.Zero);
      Assert.That(restored.selectedIncident, Is.EqualTo(CareStationIncidentType.None));
      Assert.That(restored.currentRecipe.recipeId, Is.EqualTo("legacy_active_recipe"));
      Assert.That(restored.careAction.elapsedSeconds, Is.EqualTo(31f));
      Assert.That(restored.workerLevel, Is.EqualTo(3));
      Assert.That(restored.storageLevel, Is.EqualTo(2));
      Assert.That(restored.cartLevel, Is.EqualTo(4));
    }

    [TestCase(CareStationState.WaitStorageSpace)]
    [TestCase(CareStationState.UpgradeSelection)]
    public void LegacyBlockingStatesRecoverToNonModalFlow(CareStationState state)
    {
      var legacy = CompletedRecipe(CareRecipeType.Single, 1);
      legacy.saveVersion = 20;
      legacy.currentState = state;
      legacy.storedFullBottles = 24;
      legacy.storageHours = 24;
      legacy.pendingIncidentXP = 12;
      legacy.upgradeOffered = state == CareStationState.UpgradeSelection;
      File.WriteAllText(_path, JsonUtility.ToJson(legacy, true));

      var restored = new CareStationSaveService(_path).Load(DateTime.UtcNow);

      Assert.That(restored.currentState, Is.Not.EqualTo(CareStationState.WaitStorageSpace));
      Assert.That(restored.currentState, Is.Not.EqualTo(CareStationState.UpgradeSelection));
      Assert.That(restored.careEnergy, Is.EqualTo(12));
      Assert.That(restored.storedFullBottles, Is.EqualTo(24));
      if (state == CareStationState.UpgradeSelection)
      {
        Assert.That(restored.upgradeOffered, Is.True);
        Assert.That(restored.upgradeDeferred, Is.True);
      }
    }

    [Test]
    public void RetiredIncidentAndGoldGeneratorsStayDisabled()
    {
      var save = CompletedRecipe(CareRecipeType.Double, 2);

      Assert.That(CareStationShiftRules.EnsureFirstFormalGoldBottle(save), Is.False);
      Assert.That(save.pendingPremiumShipment, Is.Zero);
      Assert.That(save.pendingGoldBottleCount, Is.Zero);
      Assert.That(CareStationDisplayNames.Filter, Is.EqualTo("FILTER"));
      Assert.That(CareStationDisplayNames.Filler, Is.EqualTo("FILLER"));
      Assert.That(CareStationDisplayNames.Packer, Is.EqualTo("PACKER"));
    }

    private static CareStationSaveData CompletedRecipe(CareRecipeType type, int steps)
    {
      var actions = new CareActionType[Math.Max(1, steps)];
      for (var index = 0; index < actions.Length; index++) actions[index] = CareActionType.ClosedEyeRest;
      return new CareStationSaveData
      {
        careActionCompleted = true,
        currentRecipe = new CareRecipeSaveData
        {
          recipeId = "economy_test_recipe",
          recipeType = type,
          actionList = actions,
          originalActionList = (CareActionType[])actions.Clone(),
          currentActionIndex = actions.Length,
          completedActionMask = (1 << actions.Length) - 1,
          recipeCompleted = true,
          completionSignalSent = true,
        },
      };
    }
  }
}
