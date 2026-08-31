using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KeepBlinking.CareStation;
using NUnit.Framework;
using UnityEngine;

namespace KeepBlinking.Tests
{
  public sealed class CareRoutineV24Tests
  {
    private static readonly CareRecipeGenerationSettings Settings =
      new CareRecipeGenerationSettings(0.25f, 0.55f, 0.20f, 32);

    [Test]
    public void AuthoredRoutinesHaveExactOrderParametersRewardsAndEstimatedDuration()
    {
      AssertRoutine(
        CareRoutineId.FocusFlow,
        new[] { CareActionType.FocusShift, CareActionType.GuidedEyeCircles, CareActionType.ClosedEyeRest },
        6, 3, 3, 60f, 4);
      AssertRoutine(
        CareRoutineId.PilotFlow,
        new[] { CareActionType.PilotEyeRoutine, CareActionType.GuidedEyeCircles, CareActionType.ClosedEyeRest },
        6, 3, 3, 60f, 4);
      AssertRoutine(
        CareRoutineId.DeepReset,
        new[] { CareActionType.FocusShift, CareActionType.ClosedEyeRest },
        6, 3, 3, 90f, 6);
      AssertRoutine(
        CareRoutineId.FullCare,
        new[]
        {
          CareActionType.FocusShift,
          CareActionType.PilotEyeRoutine,
          CareActionType.GuidedEyeCircles,
          CareActionType.ClosedEyeRest,
        },
        4, 2, 2, 60f, 3);
    }

    [Test]
    public void PostOnboardingSelectionIsBalancedAndNeverImmediatelyRepeats()
    {
      var counts = new Dictionary<CareRoutineId, int>
      {
        [CareRoutineId.FocusFlow] = 0,
        [CareRoutineId.PilotFlow] = 0,
        [CareRoutineId.DeepReset] = 0,
        [CareRoutineId.FullCare] = 0,
      };
      var save = new CareStationSaveData
      {
        careRoutinesCreated = 4,
        careShiftId = 5,
        lastCompletedRoutineId = CareRoutineId.None,
      };

      for (var seed = 0; seed < 4000; seed++)
      {
        var previous = save.lastCompletedRoutineId;
        var recipe = CareRecipeGenerator.CreateForShift(save, seed, Settings);
        if (previous != CareRoutineId.None)
          Assert.That(recipe.routineId, Is.Not.EqualTo(previous));
        counts[recipe.routineId]++;
        Complete(recipe);
        CareRecipeGenerator.ApplyCompletionToProgress(save, recipe);
        save.careShiftId++;
      }

      foreach (var count in counts.Values)
        Assert.That(count, Is.InRange(850, 1150),
          "A long no-repeat sequence must preserve the authored 25% marginal mix.");
    }

    [Test]
    public void RecipeCannotCompleteOrEmitItsSignalBeforeFinalRest()
    {
      var recipe = CareRecipeGenerator.CreateRoutine(CareRoutineId.FocusFlow, 1, 91);
      var runtime = new CareRecipeRuntime(recipe);

      Assert.That(runtime.CompleteCurrentAction(CareActionType.FocusShift).RecipeCompleted, Is.False);
      Assert.That(runtime.TryConsumeCompletionSignal(), Is.False);
      Assert.That(runtime.CompleteCurrentAction(CareActionType.GuidedEyeCircles).RecipeCompleted, Is.False);
      Assert.That(runtime.TryConsumeCompletionSignal(), Is.False);
      Assert.That(recipe.CurrentAction, Is.EqualTo(CareActionType.ClosedEyeRest));
      Assert.That(runtime.CompleteCurrentAction(CareActionType.ClosedEyeRest).RecipeCompleted, Is.True);
      Assert.That(runtime.TryConsumeCompletionSignal(), Is.True);
      Assert.That(runtime.TryConsumeCompletionSignal(), Is.False);
    }

    [Test]
    public void ChangeStepMergesPlannedRewardsAndNeverLeavesStandaloneGuided()
    {
      var recipe = CareRecipeGenerator.CreateRoutine(CareRoutineId.PilotFlow, 1, 92);
      var save = new CareStationSaveData { currentRecipe = recipe };
      var runtime = new CareRecipeRuntime(recipe);

      var replacement = runtime.ReplaceCurrentWithClosedEyeRest();

      Assert.That(replacement.Accepted, Is.True);
      Assert.That(recipe.actionList, Is.EqualTo(new[] { CareActionType.ClosedEyeRest }));
      Assert.That(recipe.actionList.Contains(CareActionType.GuidedEyeCircles), Is.False);
      Assert.That(runtime.CompleteCurrentAction(CareActionType.ClosedEyeRest).RecipeCompleted, Is.True);
      Assert.That(CareEconomyRules.TryGrantCompletedRecipeStep(save, 0, out var granted), Is.True);
      Assert.That(granted, Is.EqualTo(12));
      Assert.That(recipe.rewardedStepMask, Is.EqualTo(0b111));
      Assert.That(CareEconomyRules.TryGrantCompletedRecipeStep(save, 0, out _), Is.False);
    }

    [Test]
    public void RoutineRewardChangesOnlyCareEnergyAndNeverCreatesProductsOrCoins()
    {
      foreach (var routineId in new[]
      {
        CareRoutineId.FocusFlow,
        CareRoutineId.PilotFlow,
        CareRoutineId.DeepReset,
        CareRoutineId.FullCare,
      })
      {
        var recipe = CareRecipeGenerator.CreateRoutine(routineId, 2, 93);
        var save = new CareStationSaveData
        {
          currentRecipe = recipe,
          careEnergy = 5,
          coins = 7,
          storedFullBottles = 8,
          pendingPremiumShipment = 2,
        };
        var runtime = new CareRecipeRuntime(recipe);
        while (!recipe.recipeCompleted)
        {
          var result = runtime.CompleteCurrentAction(runtime.CurrentAction);
          Assert.That(CareEconomyRules.TryGrantCompletedRecipeStep(
            save, result.CompletedStepIndex, out _), Is.True);
        }

        Assert.That(save.careEnergy, Is.EqualTo(17));
        Assert.That(save.coins, Is.EqualTo(7));
        Assert.That(save.storedFullBottles, Is.EqualTo(8));
        Assert.That(save.pendingFullBottleShipment, Is.Zero);
        Assert.That(save.pendingPremiumShipment, Is.EqualTo(2));
        Assert.That(save.productionStage, Is.EqualTo(CareProductionStage.None));
      }
    }

    [Test]
    public void ReloadedCompletedSlotCannotRewardAgain()
    {
      var directory = Path.Combine(Path.GetTempPath(), "KeepBlinkingRoutineV24", Guid.NewGuid().ToString("N"));
      var path = Path.Combine(directory, "care_station.json");
      try
      {
        Directory.CreateDirectory(directory);
        var recipe = CareRecipeGenerator.CreateRoutine(CareRoutineId.FocusFlow, 3, 94);
        var save = new CareStationSaveData { currentRecipe = recipe };
        var runtime = new CareRecipeRuntime(recipe);
        var first = runtime.CompleteCurrentAction(CareActionType.FocusShift);
        Assert.That(CareEconomyRules.TryGrantCompletedRecipeStep(save, first.CompletedStepIndex, out var grant), Is.True);
        Assert.That(grant, Is.EqualTo(4));
        new CareStationSaveService(path).Save(save, DateTime.UtcNow);

        var restored = new CareStationSaveService(path).Load(DateTime.UtcNow.AddSeconds(1));
        Assert.That(restored.currentRecipe.rewardedStepMask, Is.EqualTo(1));
        Assert.That(restored.careEnergy, Is.EqualTo(4));
        Assert.That(CareEconomyRules.TryGrantCompletedRecipeStep(restored, 0, out var replay), Is.False);
        Assert.That(replay, Is.Zero);
        Assert.That(restored.careEnergy, Is.EqualTo(4));
      }
      finally
      {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
      }
    }

    [Test]
    public void VersionTwentyThreeActiveRecipeMigratesWithoutLosingProgressOrEconomy()
    {
      var directory = Path.Combine(Path.GetTempPath(), "KeepBlinkingRoutineMigrationV24", Guid.NewGuid().ToString("N"));
      var path = Path.Combine(directory, "care_station.json");
      try
      {
        Directory.CreateDirectory(directory);
        var legacy = new CareStationSaveData
        {
          saveVersion = 23,
          careEnergy = 9,
          coins = 17,
          storedFullBottles = 11,
          pendingPremiumShipment = 2,
          stationLevel = 2,
          basicConveyorUnlockPresented = true,
          productionTransportMode = CareProductionTransportMode.BasicConveyor,
          currentState = CareStationState.CareActionInProgress,
          careAction = new CareActionSaveData
          {
            actionType = CareActionType.GuidedEyeCircles,
            stage = CareActionStage.Active,
            elapsedSeconds = 13.5f,
          },
          currentRecipe = new CareRecipeSaveData
          {
            recipeId = "v23_active_pilot_flow",
            recipeType = CareRecipeType.Triple,
            actionList = new[]
            {
              CareActionType.PilotEyeRoutine,
              CareActionType.GuidedEyeCircles,
              CareActionType.ClosedEyeRest,
            },
            originalActionList = new[]
            {
              CareActionType.PilotEyeRoutine,
              CareActionType.GuidedEyeCircles,
              CareActionType.ClosedEyeRest,
            },
            currentActionIndex = 1,
            completedActionMask = 1,
          },
        };
        File.WriteAllText(path, JsonUtility.ToJson(legacy, true));

        var restored = new CareStationSaveService(path).Load(DateTime.UtcNow);

        Assert.That(restored.saveVersion, Is.EqualTo(24));
        Assert.That(restored.currentRecipe.recipeId, Is.EqualTo("v23_active_pilot_flow"));
        Assert.That(restored.currentRecipe.routineId, Is.EqualTo(CareRoutineId.PilotFlow));
        Assert.That(restored.currentRecipe.currentActionIndex, Is.EqualTo(1));
        Assert.That(restored.currentRecipe.completedActionMask, Is.EqualTo(1));
        Assert.That(restored.careAction.actionType, Is.EqualTo(CareActionType.GuidedEyeCircles));
        Assert.That(restored.careAction.elapsedSeconds, Is.EqualTo(13.5f).Within(0.001f));
        Assert.That(restored.careEnergy, Is.EqualTo(13), "The completed legacy slot is settled once.");
        Assert.That(restored.coins, Is.EqualTo(17));
        Assert.That(restored.storedFullBottles, Is.EqualTo(11));
        Assert.That(restored.pendingPremiumShipment, Is.EqualTo(2));
        Assert.That(restored.stationLevel, Is.EqualTo(2));
        Assert.That(restored.productionTransportMode, Is.EqualTo(CareProductionTransportMode.BasicConveyor));
      }
      finally
      {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
      }
    }

    [Test]
    public void SettledLegacyRecipeKeepsAllExistingEnergyAndCannotPayAgain()
    {
      var directory = Path.Combine(Path.GetTempPath(), "KeepBlinkingSettledRoutineV24", Guid.NewGuid().ToString("N"));
      var path = Path.Combine(directory, "care_station.json");
      try
      {
        Directory.CreateDirectory(directory);
        var legacy = new CareStationSaveData
        {
          saveVersion = 23,
          careEnergy = 36,
          coins = 8,
          storedFullBottles = 14,
          currentRecipe = new CareRecipeSaveData
          {
            recipeId = "v23_settled_triple",
            recipeType = CareRecipeType.Triple,
            actionList = new[]
            {
              CareActionType.PilotEyeRoutine,
              CareActionType.GuidedEyeCircles,
              CareActionType.ClosedEyeRest,
            },
            originalActionList = new[]
            {
              CareActionType.PilotEyeRoutine,
              CareActionType.GuidedEyeCircles,
              CareActionType.ClosedEyeRest,
            },
            currentActionIndex = 3,
            completedActionMask = 0b111,
            recipeCompleted = true,
            careEnergyGranted = true,
            careEnergyGrantedAmount = 36,
          },
        };
        File.WriteAllText(path, JsonUtility.ToJson(legacy, true));

        var restored = new CareStationSaveService(path).Load(DateTime.UtcNow);

        Assert.That(restored.careEnergy, Is.EqualTo(36));
        Assert.That(restored.coins, Is.EqualTo(8));
        Assert.That(restored.storedFullBottles, Is.EqualTo(14));
        Assert.That(restored.currentRecipe.careEnergyRewardedTotal, Is.EqualTo(12));
        Assert.That(restored.currentRecipe.rewardedStepMask, Is.EqualTo(0b111));
        Assert.That(restored.lastCompletedRoutineId, Is.EqualTo(CareRoutineId.PilotFlow));
        Assert.That(CareEconomyRules.TryGrantRecipeCareEnergy(
          restored, new CareEconomyConfiguration(), out var replay), Is.False);
        Assert.That(replay, Is.Zero);
        Assert.That(restored.careEnergy, Is.EqualTo(36));
      }
      finally
      {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
      }
    }

    [Test]
    public void InspectionAlwaysUsesCompletePilotFlow()
    {
      var recipe = CareStationInspectionRules.CreateRecipe(12);

      Assert.That(recipe.recipeType, Is.EqualTo(CareRecipeType.Inspection));
      Assert.That(recipe.routineId, Is.EqualTo(CareRoutineId.PilotFlow));
      Assert.That(recipe.actionList, Is.EqualTo(new[]
      {
        CareActionType.PilotEyeRoutine,
        CareActionType.GuidedEyeCircles,
        CareActionType.ClosedEyeRest,
      }));
      Assert.That(recipe.plannedSlotRewards, Is.EqualTo(new[] { 4, 4, 4 }));
      Assert.That(recipe.closedEyeRestSeconds, Is.EqualTo(60f));
    }

    private static void AssertRoutine(
      CareRoutineId routineId,
      CareActionType[] actions,
      int focusCycles,
      int pilotRounds,
      int guidedLaps,
      float restSeconds,
      int rewardPerSlot)
    {
      var recipe = CareRecipeGenerator.CreateRoutine(routineId, 1, 11);
      Assert.That(recipe.recipeType, Is.Not.EqualTo(CareRecipeType.Training));
      Assert.That(recipe.actionList, Is.EqualTo(actions));
      Assert.That(recipe.actionList[recipe.ActionCount - 1], Is.EqualTo(CareActionType.ClosedEyeRest));
      Assert.That(CareActionLibrary.HasPilotGuidedInvariant(recipe.actionList), Is.True);
      Assert.That(recipe.focusCycleCount, Is.EqualTo(focusCycles));
      Assert.That(recipe.pilotRoundsPerAxis, Is.EqualTo(pilotRounds));
      Assert.That(recipe.guidedLapsPerDirection, Is.EqualTo(guidedLaps));
      Assert.That(recipe.closedEyeRestSeconds, Is.EqualTo(restSeconds));
      Assert.That(recipe.plannedSlotRewards, Is.All.EqualTo(rewardPerSlot));
      Assert.That(recipe.plannedSlotRewards.Sum(), Is.EqualTo(12));
      Assert.That(CareActionLibrary.EstimatedRecipeSeconds(recipe), Is.InRange(155f, 180f));
    }

    private static void Complete(CareRecipeSaveData recipe)
    {
      var runtime = new CareRecipeRuntime(recipe);
      while (!recipe.recipeCompleted)
        Assert.That(runtime.CompleteCurrentAction(runtime.CurrentAction).Accepted, Is.True);
    }
  }
}
