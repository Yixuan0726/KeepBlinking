using System;
using System.Linq;
using KeepBlinking.CareStation;
using NUnit.Framework;

namespace KeepBlinking.Tests
{
  public sealed class CareRecipeTests
  {
    private static readonly CareRecipeGenerationSettings Settings =
      new CareRecipeGenerationSettings(0.25f, 0.55f, 0.20f, 32);

    [Test]
    public void FirstFourRoutinesAreFormalAThroughDAndPersistProgress()
    {
      var save = new CareStationSaveData { careShiftId = 1 };
      var expectedIds = new[]
      {
        CareRoutineId.FocusFlow,
        CareRoutineId.PilotFlow,
        CareRoutineId.DeepReset,
        CareRoutineId.FullCare,
      };
      var expectedActions = new[]
      {
        new[] { CareActionType.FocusShift, CareActionType.GuidedEyeCircles, CareActionType.ClosedEyeRest },
        new[] { CareActionType.PilotEyeRoutine, CareActionType.GuidedEyeCircles, CareActionType.ClosedEyeRest },
        new[] { CareActionType.FocusShift, CareActionType.ClosedEyeRest },
        new[]
        {
          CareActionType.FocusShift,
          CareActionType.PilotEyeRoutine,
          CareActionType.GuidedEyeCircles,
          CareActionType.ClosedEyeRest,
        },
      };
      for (var index = 0; index < expectedIds.Length; index++)
      {
        save.careShiftId = index + 1;
        var recipe = CareRecipeGenerator.CreateForShift(save, 100 + index, Settings);
        Assert.That(recipe.recipeType, Is.Not.EqualTo(CareRecipeType.Training));
        Assert.That(recipe.routineId, Is.EqualTo(expectedIds[index]));
        Assert.That(recipe.actionList, Is.EqualTo(expectedActions[index]));
        var runtime = new CareRecipeRuntime(recipe);
        while (!recipe.recipeCompleted)
          Assert.That(runtime.CompleteCurrentAction(runtime.CurrentAction).Accepted, Is.True);
        CareRecipeGenerator.ApplyCompletionToProgress(save, recipe);
        Assert.That(save.lastCompletedRoutineId, Is.EqualTo(expectedIds[index]));
      }
      Assert.That(save.trainingProgress, Is.Zero);
      Assert.That(save.careRoutinesCreated, Is.EqualTo(4));
    }

    [Test]
    public void LaterRoutinesDoNotImmediatelyRepeatTheCompletedRoutine()
    {
      foreach (var previous in new[]
      {
        CareRoutineId.FocusFlow,
        CareRoutineId.PilotFlow,
        CareRoutineId.DeepReset,
        CareRoutineId.FullCare,
      })
      {
        for (var seed = 0; seed < 100; seed++)
        {
          var save = new CareStationSaveData
          {
            careRoutinesCreated = 4,
            lastCompletedRoutineId = previous,
            careShiftId = seed + 5,
          };
          var next = CareRecipeGenerator.CreateForShift(save, seed, Settings);
          Assert.That(next.routineId, Is.Not.EqualTo(previous));
        }
      }
    }

    [TestCase(CareRecipeType.Single, 1)]
    [TestCase(CareRecipeType.Double, 2)]
    [TestCase(CareRecipeType.Triple, 3)]
    public void RequestedRecipeLengthsAreCorrect(CareRecipeType type, int expectedLength)
    {
      var recipe = CareRecipeGenerator.CreateFormal(type, 20, 5521, Array.Empty<string>(), 0, 0);
      Assert.That(recipe.ActionCount, Is.EqualTo(expectedLength));
    }

    [Test]
    public void FormalRecipesNeverRepeatAnActionAndAlwaysHaveActiveAndRestWork()
    {
      for (var seed = 0; seed < 100; seed++)
      {
        foreach (var type in new[] { CareRecipeType.Single, CareRecipeType.Double, CareRecipeType.Triple })
        {
          var recipe = CareRecipeGenerator.CreateFormal(type, 20, seed, Array.Empty<string>(), 0, 0);
          Assert.That(recipe.actionList.Distinct().Count(), Is.EqualTo(recipe.ActionCount));
          Assert.That(recipe.actionList.Contains(CareActionType.BlinkReset), Is.False);
          Assert.That(recipe.actionList.Contains(CareActionType.ScreenDown), Is.False);
          if (type != CareRecipeType.Single)
            Assert.That(CareActionLibrary.HasValidFormalComposition(recipe.actionList), Is.True);
        }
      }
    }

    [Test]
    public void FormalTripleActionsUseComfortableOrder()
    {
      for (var seed = 0; seed < 64; seed++)
      {
        var recipe = CareRecipeGenerator.CreateFormal(CareRecipeType.Triple, 20, seed, Array.Empty<string>(), 0, 0);
        Assert.That(recipe.actionList[2], Is.EqualTo(CareActionType.ClosedEyeRest));
        Assert.That(CareActionLibrary.IsActiveAction(recipe.actionList[0]) ||
                    CareActionLibrary.IsRestOrOffscreenAction(recipe.actionList[0]), Is.True);
      }
    }

    [Test]
    public void NewlyGeneratedRoutinesFitTheAuthoredTimeRange()
    {
      for (var seed = 0; seed < 100; seed++)
      {
        var save = new CareStationSaveData
        {
          careRoutinesCreated = 4,
          careShiftId = 20 + seed,
        };
        var next = CareRecipeGenerator.CreateForShift(save, seed, Settings);
        Assert.That(next.recipeType, Is.Not.EqualTo(CareRecipeType.Training));
        var duration = CareActionLibrary.EstimatedRecipeSeconds(next);
        Assert.That(duration, Is.InRange(155f, 180f));
      }
    }

    [Test]
    public void GuidedEyeCirclesCannotAppearOnConsecutiveShifts()
    {
      var save = new CareStationSaveData { trainingProgress = 4, careShiftId = 8 };
      var completed = Recipe(CareRecipeType.Single, 8, CareActionType.GuidedEyeCircles);
      completed.recipeCompleted = true;
      CareRecipeGenerator.ApplyCompletionToProgress(save, completed);
      for (var seed = 0; seed < 50; seed++)
      {
        var next = CareRecipeGenerator.CreateFormal(CareRecipeType.Double, 9, seed, Array.Empty<string>(), 0, save.guidedEyeCirclesCooldownUntilShiftId);
        Assert.That(next.actionList.Contains(CareActionType.GuidedEyeCircles), Is.False);
      }
    }

    [Test]
    public void ImmediateAndRecentRecipeHistoryAreAvoidedWhenAlternativesExist()
    {
      var repeated = CareRecipeGenerator.Signature(new[]
        { CareActionType.PilotEyeRoutine, CareActionType.GuidedEyeCircles, CareActionType.ClosedEyeRest });
      var history = new[]
      {
        CareRecipeGenerator.Signature(new[]
          { CareActionType.FocusShift, CareActionType.GuidedEyeCircles, CareActionType.ClosedEyeRest }),
        repeated,
      };
      for (var seed = 0; seed < 32; seed++)
      {
        var recipe = CareRecipeGenerator.CreateFormal(CareRecipeType.Triple, 20, seed, history, 0, 0);
        Assert.That(CareRecipeGenerator.Signature(recipe.actionList), Is.Not.EqualTo(repeated));
      }
    }

    [Test]
    public void RetiredBlinkStepRemovalRemapsMasksAndSupplementsShortRoutine()
    {
      var recipe = Recipe(
        CareRecipeType.Triple,
        50,
        CareActionType.ScreenDown,
        CareActionType.BlinkReset,
        CareActionType.ClosedEyeRest);
      recipe.completedActionMask = 1;
      recipe.currentActionIndex = 1;

      Assert.That(CareRecipeGenerator.RemoveRetiredBlinkReset(recipe, true), Is.True);

      Assert.That(recipe.actionList, Is.EqualTo(new[]
      {
        CareActionType.FocusShift,
        CareActionType.ClosedEyeRest,
      }));
      Assert.That(recipe.completedActionMask, Is.EqualTo(0));
      Assert.That(recipe.currentActionIndex, Is.EqualTo(0));
      Assert.That(CareActionLibrary.EstimatedRecipeSeconds(recipe.actionList, recipe.deepRest), Is.InRange(120f, 180f));
    }

    [Test]
    public void FixedSeedProducesTheSameRecipe()
    {
      var first = CareRecipeGenerator.CreateFormal(CareRecipeType.Double, 12, 99117, Array.Empty<string>(), 0, 0);
      var second = CareRecipeGenerator.CreateFormal(CareRecipeType.Double, 12, 99117, Array.Empty<string>(), 0, 0);
      Assert.That(first.recipeId, Is.EqualTo(second.recipeId));
      Assert.That(first.actionList, Is.EqualTo(second.actionList));
    }

    [Test]
    public void CompletedStepAndRecipeSignalsCanOnlyBeConsumedOnce()
    {
      var recipe = Recipe(CareRecipeType.Double, 2, CareActionType.FocusShift, CareActionType.ClosedEyeRest);
      var runtime = new CareRecipeRuntime(recipe);
      Assert.That(runtime.CompleteCurrentAction(CareActionType.FocusShift).Accepted, Is.True);
      Assert.That(runtime.CompleteCurrentAction(CareActionType.FocusShift).Accepted, Is.False);
      Assert.That(runtime.CompleteCurrentAction(CareActionType.ClosedEyeRest).RecipeCompleted, Is.True);
      Assert.That(runtime.TryConsumeCompletionSignal(), Is.True);
      Assert.That(runtime.TryConsumeCompletionSignal(), Is.False);
      Assert.That(runtime.TryConsumeForProduction(), Is.True);
      Assert.That(runtime.TryConsumeForProduction(), Is.False);
    }

    [Test]
    public void RetiredActionsAreRemovedWithoutDuplicatingProduction()
    {
      var recipe = Recipe(CareRecipeType.Triple, 33,
        CareActionType.ScreenDown, CareActionType.FocusShift, CareActionType.ClosedEyeRest);
      Assert.That(CareRecipeGenerator.RemoveRetiredBlinkReset(recipe, true), Is.True);
      CollectionAssert.DoesNotContain(recipe.actionList, CareActionType.ScreenDown);
      var runtime = new CareRecipeRuntime(recipe);
      while (!recipe.recipeCompleted)
        Assert.That(runtime.CompleteCurrentAction(recipe.CurrentAction).Accepted, Is.True);
      Assert.That(runtime.TryConsumeForProduction(), Is.True);
      Assert.That(runtime.TryConsumeForProduction(), Is.False);
    }

    [Test]
    public void ChangeStepReplacesGuidedWithRealRestAndPreservesCompletedSteps()
    {
      var recipe = Recipe(
        CareRecipeType.Triple,
        40,
        CareActionType.FocusShift,
        CareActionType.GuidedEyeCircles,
        CareActionType.ClosedEyeRest);
      var runtime = new CareRecipeRuntime(recipe);
      Assert.That(runtime.CompleteCurrentAction(CareActionType.FocusShift).Accepted, Is.True);

      var replacement = runtime.ReplaceCurrentWithClosedEyeRest();
      Assert.That(replacement.Accepted, Is.True);
      Assert.That(replacement.OriginalAction, Is.EqualTo(CareActionType.GuidedEyeCircles));
      Assert.That(recipe.IsStepCompleted(0), Is.True);
      Assert.That(recipe.CurrentAction, Is.EqualTo(CareActionType.ClosedEyeRest));
      Assert.That(recipe.IsStepReplaced(1), Is.True);
      Assert.That(recipe.OriginalActionAt(1), Is.EqualTo(CareActionType.GuidedEyeCircles));
      Assert.That(recipe.recipeCompleted, Is.False);
    }

    [Test]
    public void ChangeStepMergesFutureClosedEyeRestInsteadOfRepeatingIt()
    {
      var recipe = Recipe(
        CareRecipeType.Double,
        41,
        CareActionType.FocusShift,
        CareActionType.ClosedEyeRest);
      var runtime = new CareRecipeRuntime(recipe);
      var replacement = runtime.ReplaceCurrentWithClosedEyeRest();

      Assert.That(replacement.Accepted, Is.True);
      Assert.That(recipe.actionList, Is.EqualTo(new[] { CareActionType.ClosedEyeRest }));
      Assert.That(recipe.OriginalActionAt(0), Is.EqualTo(CareActionType.FocusShift));
      Assert.That(runtime.CompleteCurrentAction(CareActionType.ClosedEyeRest).RecipeCompleted, Is.True);
      Assert.That(runtime.TryConsumeForProduction(), Is.True);
      Assert.That(runtime.TryConsumeForProduction(), Is.False,
        "Replacing a step cannot create a second bottle-production signal.");
    }

    [TestCase(CareActionType.FocusShift)]
    [TestCase(CareActionType.GuidedEyeCircles)]
    [TestCase(CareActionType.PilotEyeRoutine)]
    public void EveryEligibleCareActionCanBeReplacedByClosedEyeRest(CareActionType original)
    {
      var recipe = Recipe(CareRecipeType.Single, 43, original);
      var runtime = new CareRecipeRuntime(recipe);

      var replacement = runtime.ReplaceCurrentWithClosedEyeRest();

      Assert.That(replacement.Accepted, Is.True);
      Assert.That(replacement.OriginalAction, Is.EqualTo(original));
      Assert.That(recipe.CurrentAction, Is.EqualTo(CareActionType.ClosedEyeRest));
      Assert.That(recipe.OriginalActionAt(0), Is.EqualTo(original));
      Assert.That(recipe.recipeCompleted, Is.False);
    }

    [Test]
    public void ClosedEyeRestCannotBeChangedAgain()
    {
      var recipe = Recipe(CareRecipeType.Single, 42, CareActionType.ClosedEyeRest);
      var runtime = new CareRecipeRuntime(recipe);
      Assert.That(runtime.ReplaceCurrentWithClosedEyeRest().Accepted, Is.False);
      Assert.That(recipe.actionList, Is.EqualTo(new[] { CareActionType.ClosedEyeRest }));
    }

    [TestCase(0, 1, CareRecipePipeline.Filter | CareRecipePipeline.Filler | CareRecipePipeline.Packer)]
    [TestCase(0, 2, CareRecipePipeline.Filter)]
    [TestCase(1, 2, CareRecipePipeline.Filler | CareRecipePipeline.Packer)]
    [TestCase(0, 3, CareRecipePipeline.Filter)]
    [TestCase(1, 3, CareRecipePipeline.Filler)]
    [TestCase(2, 3, CareRecipePipeline.Packer)]
    public void PipelineFeedbackMatchesRecipeLength(int step, int count, int expectedMask)
    {
      Assert.That(CareRecipePipeline.StageMaskForCompletion(step, count), Is.EqualTo(expectedMask));
    }

    [Test]
    public void ActionsMapOnlyToFilterFillerAndPacker()
    {
      Assert.That(
        CareRecipePipeline.StageMaskForAction(CareActionType.PilotEyeRoutine),
        Is.EqualTo(CareRecipePipeline.Filter));
      Assert.That(CareRecipePipeline.StageMaskForAction(CareActionType.GuidedEyeCircles),
        Is.EqualTo(CareRecipePipeline.Packer));
      Assert.That(CareRecipePipeline.StageMaskForAction(CareActionType.ClosedEyeRest),
        Is.EqualTo(CareRecipePipeline.Filler));
      Assert.That(CareRecipePipeline.StageMaskForAction(CareActionType.FocusShift),
        Is.EqualTo(CareRecipePipeline.Packer | CareRecipePipeline.Filler));
      Assert.That(CareRecipePipeline.StageMaskForAction(CareActionType.ScreenDown), Is.Zero);
      Assert.That(CareRecipePipeline.StageMaskForAction(CareActionType.BlinkReset), Is.Zero);
    }

    [Test]
    public void EveryPilotIsImmediatelyFollowedByGuidedAndNeverLast()
    {
      for (var seed = 0; seed < 100; seed++)
      {
        var legacySingle = CareRecipeGenerator.CreateFormal(CareRecipeType.Single, 30, seed,
          Array.Empty<string>(), 0, 0);
        Assert.That(legacySingle.actionList.Contains(CareActionType.PilotEyeRoutine), Is.False,
          "Even the legacy/development Single entry point must never create a standalone Pilot task.");

        var recipe = CareRecipeGenerator.CreateFormal(CareRecipeType.Triple, 30, seed,
          Array.Empty<string>(), 0, 0);
        var pilot = Array.IndexOf(recipe.actionList, CareActionType.PilotEyeRoutine);
        if (pilot < 0) continue;
        Assert.That(pilot, Is.LessThan(recipe.ActionCount - 1));
        Assert.That(recipe.actionList[pilot + 1], Is.EqualTo(CareActionType.GuidedEyeCircles));
      }
    }

    [Test]
    public void IllegalPilotRecipeMovesGuidedNextToPilotWithoutRepeatingIt()
    {
      var recipe = Recipe(CareRecipeType.Triple, 77,
        CareActionType.GuidedEyeCircles, CareActionType.PilotEyeRoutine, CareActionType.ClosedEyeRest);
      Assert.That(CareRecipeGenerator.RemoveRetiredBlinkReset(recipe, true), Is.True);
      Assert.That(recipe.actionList, Is.EqualTo(new[]
      {
        CareActionType.PilotEyeRoutine,
        CareActionType.GuidedEyeCircles,
        CareActionType.ClosedEyeRest,
      }));
      Assert.That(recipe.actionList.Count(action => action == CareActionType.GuidedEyeCircles), Is.EqualTo(1));
    }

    private static CareRecipeSaveData Recipe(CareRecipeType type, int shift, params CareActionType[] actions)
    {
      return new CareRecipeSaveData
      {
        recipeId = $"test_{shift}_{CareRecipeGenerator.Signature(actions)}",
        recipeSeed = 1,
        recipeType = type,
        actionList = actions,
        createdShiftId = shift,
      };
    }

    private static void CompleteAndApply(CareStationSaveData save, CareRecipeSaveData recipe)
    {
      var runtime = new CareRecipeRuntime(recipe);
      foreach (var action in recipe.actionList) runtime.CompleteCurrentAction(action);
      CareRecipeGenerator.ApplyCompletionToProgress(save, recipe);
    }
  }
}
