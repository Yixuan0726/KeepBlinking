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
    public void FourTrainingRecipesAppearInFixedOrderAndPersistProgress()
    {
      var save = new CareStationSaveData { careShiftId = 1 };
      var expected = new[]
      {
        CareActionType.ScreenDown,
        CareActionType.ClosedEyeRest,
        CareActionType.FocusShift,
        CareActionType.GuidedEyeCircles,
      };
      for (var index = 0; index < expected.Length; index++)
      {
        save.careShiftId = index + 1;
        var recipe = CareRecipeGenerator.CreateForShift(save, 100 + index, Settings);
        Assert.That(recipe.recipeType, Is.EqualTo(CareRecipeType.Training));
        Assert.That(recipe.actionList, Is.EqualTo(new[] { expected[index] }));
        var runtime = new CareRecipeRuntime(recipe);
        Assert.That(runtime.CompleteCurrentAction(expected[index]).RecipeCompleted, Is.True);
        CareRecipeGenerator.ApplyCompletionToProgress(save, recipe);
        Assert.That(save.trainingProgress, Is.EqualTo(index + 1));
      }
    }

    [Test]
    public void FirstTwoFormalRecipesAreDoubleActions()
    {
      var save = new CareStationSaveData { trainingProgress = 4, careShiftId = 5 };
      var first = CareRecipeGenerator.CreateForShift(save, 41, Settings);
      Assert.That(first.ActionCount, Is.EqualTo(2));
      CompleteAndApply(save, first);
      save.careShiftId++;
      var second = CareRecipeGenerator.CreateForShift(save, 42, Settings);
      Assert.That(second.ActionCount, Is.EqualTo(2));
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
    public void FormalRecipesNeverRepeatAnActionOrCombineBothClosedEyeActions()
    {
      for (var seed = 0; seed < 100; seed++)
      {
        foreach (var type in new[] { CareRecipeType.Single, CareRecipeType.Double, CareRecipeType.Triple })
        {
          var recipe = CareRecipeGenerator.CreateFormal(type, 20, seed, Array.Empty<string>(), 0, 0);
          Assert.That(recipe.actionList.Distinct().Count(), Is.EqualTo(recipe.ActionCount));
          Assert.That(recipe.actionList.Contains(CareActionType.ClosedEyeRest) &&
                      recipe.actionList.Contains(CareActionType.GuidedEyeCircles), Is.False);
        }
      }
    }

    [Test]
    public void FocusAndClosedEyeActionsUseComfortableOrder()
    {
      for (var seed = 0; seed < 64; seed++)
      {
        var recipe = CareRecipeGenerator.CreateFormal(CareRecipeType.Triple, 20, seed, Array.Empty<string>(), 0, 0);
        Assert.That(recipe.actionList[0], Is.EqualTo(CareActionType.ScreenDown));
        Assert.That(recipe.actionList[1], Is.EqualTo(CareActionType.FocusShift));
        Assert.That(new[] { CareActionType.ClosedEyeRest, CareActionType.GuidedEyeCircles },
          Does.Contain(recipe.actionList[2]));
      }
    }

    [Test]
    public void FocusShiftWaitsOneCompleteShiftBeforeAppearingAgain()
    {
      var save = new CareStationSaveData { trainingProgress = 4, careShiftId = 10 };
      var completed = Recipe(CareRecipeType.Double, 10, CareActionType.ScreenDown, CareActionType.FocusShift);
      completed.recipeCompleted = true;
      CareRecipeGenerator.ApplyCompletionToProgress(save, completed);
      Assert.That(save.focusShiftCooldownUntilShiftId, Is.EqualTo(11));

      for (var seed = 0; seed < 50; seed++)
      {
        var next = CareRecipeGenerator.CreateFormal(CareRecipeType.Triple, 11, seed, Array.Empty<string>(), save.focusShiftCooldownUntilShiftId, 0);
        Assert.That(next.actionList.Contains(CareActionType.FocusShift), Is.False);
        Assert.That(next.ActionCount, Is.LessThan(3), "The generator must lower recipe length when cooldowns exhaust valid triples.");
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
      var repeated = CareRecipeGenerator.Signature(new[] { CareActionType.ScreenDown, CareActionType.FocusShift });
      var history = new[] { "ScreenDown>ClosedEyeRest", "FocusShift>GuidedEyeCircles", repeated };
      for (var seed = 0; seed < 32; seed++)
      {
        var recipe = CareRecipeGenerator.CreateFormal(CareRecipeType.Double, 20, seed, history, 0, 0);
        Assert.That(CareRecipeGenerator.Signature(recipe.actionList), Is.Not.EqualTo(repeated));
      }
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
      var recipe = Recipe(CareRecipeType.Double, 2, CareActionType.ScreenDown, CareActionType.FocusShift);
      var runtime = new CareRecipeRuntime(recipe);
      Assert.That(runtime.CompleteCurrentAction(CareActionType.ScreenDown).Accepted, Is.True);
      Assert.That(runtime.CompleteCurrentAction(CareActionType.ScreenDown).Accepted, Is.False);
      Assert.That(runtime.CompleteCurrentAction(CareActionType.FocusShift).RecipeCompleted, Is.True);
      Assert.That(runtime.TryConsumeCompletionSignal(), Is.True);
      Assert.That(runtime.TryConsumeCompletionSignal(), Is.False);
      Assert.That(runtime.TryConsumeForProduction(), Is.True);
      Assert.That(runtime.TryConsumeForProduction(), Is.False);
    }

    [Test]
    public void DeveloperSkippedScreenDownAdvancesRecipesWithoutDuplicatingProduction()
    {
      var recipes = new[]
      {
        Recipe(CareRecipeType.Single, 31, CareActionType.ScreenDown),
        Recipe(CareRecipeType.Double, 32, CareActionType.ScreenDown, CareActionType.FocusShift),
        Recipe(CareRecipeType.Triple, 33, CareActionType.ScreenDown, CareActionType.FocusShift, CareActionType.ClosedEyeRest),
      };
      foreach (var recipe in recipes)
      {
        var runtime = new CareRecipeRuntime(recipe);
        var skipped = runtime.CompleteCurrentAction(CareActionType.ScreenDown);
        Assert.That(skipped.Accepted, Is.True);
        recipe.developerSkippedActionMask |= 1 << skipped.CompletedStepIndex;
        Assert.That(recipe.IsStepDeveloperSkipped(0), Is.True);
        Assert.That(recipe.CurrentAction, Is.EqualTo(recipe.ActionCount == 1 ? CareActionType.None : CareActionType.FocusShift));
        while (!recipe.recipeCompleted)
          Assert.That(runtime.CompleteCurrentAction(recipe.CurrentAction).Accepted, Is.True);
        Assert.That(runtime.TryConsumeForProduction(), Is.True);
        Assert.That(runtime.TryConsumeForProduction(), Is.False);
      }
    }

    [Test]
    public void ChangeStepReplacesScreenDownWithRealRestAndPreservesCompletedSteps()
    {
      var recipe = Recipe(
        CareRecipeType.Triple,
        40,
        CareActionType.ScreenDown,
        CareActionType.FocusShift,
        CareActionType.GuidedEyeCircles);
      var runtime = new CareRecipeRuntime(recipe);
      Assert.That(runtime.CompleteCurrentAction(CareActionType.ScreenDown).Accepted, Is.True);

      var replacement = runtime.ReplaceCurrentWithClosedEyeRest();
      Assert.That(replacement.Accepted, Is.True);
      Assert.That(replacement.OriginalAction, Is.EqualTo(CareActionType.FocusShift));
      Assert.That(recipe.IsStepCompleted(0), Is.True);
      Assert.That(recipe.CurrentAction, Is.EqualTo(CareActionType.ClosedEyeRest));
      Assert.That(recipe.IsStepReplaced(1), Is.True);
      Assert.That(recipe.OriginalActionAt(1), Is.EqualTo(CareActionType.FocusShift));
      Assert.That(recipe.recipeCompleted, Is.False);
    }

    [Test]
    public void ChangeStepMergesFutureClosedEyeRestInsteadOfRepeatingIt()
    {
      var recipe = Recipe(
        CareRecipeType.Double,
        41,
        CareActionType.ScreenDown,
        CareActionType.ClosedEyeRest);
      var runtime = new CareRecipeRuntime(recipe);
      var replacement = runtime.ReplaceCurrentWithClosedEyeRest();

      Assert.That(replacement.Accepted, Is.True);
      Assert.That(recipe.actionList, Is.EqualTo(new[] { CareActionType.ClosedEyeRest }));
      Assert.That(recipe.OriginalActionAt(0), Is.EqualTo(CareActionType.ScreenDown));
      Assert.That(runtime.CompleteCurrentAction(CareActionType.ClosedEyeRest).RecipeCompleted, Is.True);
      Assert.That(runtime.TryConsumeForProduction(), Is.True);
      Assert.That(runtime.TryConsumeForProduction(), Is.False,
        "Replacing a step cannot create a second bottle-production signal.");
    }

    [TestCase(CareActionType.ScreenDown)]
    [TestCase(CareActionType.FocusShift)]
    [TestCase(CareActionType.GuidedEyeCircles)]
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

    [TestCase(0, 1, CareRecipePipeline.Filter | CareRecipePipeline.Tank | CareRecipePipeline.Press)]
    [TestCase(0, 2, CareRecipePipeline.Filter)]
    [TestCase(1, 2, CareRecipePipeline.Tank | CareRecipePipeline.Press)]
    [TestCase(0, 3, CareRecipePipeline.Filter)]
    [TestCase(1, 3, CareRecipePipeline.Tank)]
    [TestCase(2, 3, CareRecipePipeline.Press)]
    public void PipelineFeedbackMatchesRecipeLength(int step, int count, int expectedMask)
    {
      Assert.That(CareRecipePipeline.StageMaskForCompletion(step, count), Is.EqualTo(expectedMask));
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
