using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace KeepBlinking.CareStation
{
  public readonly struct CareRecipeGenerationSettings
  {
    public readonly float SingleWeight;
    public readonly float DoubleWeight;
    public readonly float TripleWeight;
    public readonly int MaximumAttempts;

    public CareRecipeGenerationSettings(
      float singleWeight = 0.25f,
      float doubleWeight = 0.55f,
      float tripleWeight = 0.20f,
      int maximumAttempts = 32)
    {
      SingleWeight = Mathf.Max(0f, singleWeight);
      DoubleWeight = Mathf.Max(0f, doubleWeight);
      TripleWeight = Mathf.Max(0f, tripleWeight);
      MaximumAttempts = Mathf.Clamp(maximumAttempts, 1, 128);
    }
  }

  public readonly struct CareRecipeStepResult
  {
    public readonly bool Accepted;
    public readonly bool RecipeCompleted;
    public readonly int CompletedStepIndex;
    public readonly CareActionType ActionType;

    public CareRecipeStepResult(bool accepted, bool recipeCompleted, int completedStepIndex, CareActionType actionType)
    {
      Accepted = accepted;
      RecipeCompleted = recipeCompleted;
      CompletedStepIndex = completedStepIndex;
      ActionType = actionType;
    }
  }

  public readonly struct CareRecipeReplacementResult
  {
    public readonly bool Accepted;
    public readonly CareActionType OriginalAction;
    public readonly CareActionType ReplacementAction;
    public readonly int StepIndex;
    public readonly bool SatisfiedByCompletedRest;
    public readonly bool RecipeCompleted;

    public CareRecipeReplacementResult(
      bool accepted,
      CareActionType originalAction,
      int stepIndex,
      bool satisfiedByCompletedRest,
      bool recipeCompleted)
    {
      Accepted = accepted;
      OriginalAction = originalAction;
      ReplacementAction = CareActionType.ClosedEyeRest;
      StepIndex = stepIndex;
      SatisfiedByCompletedRest = satisfiedByCompletedRest;
      RecipeCompleted = recipeCompleted;
    }
  }

  public sealed class CareRecipeRuntime
  {
    public CareRecipeRuntime(CareRecipeSaveData data)
    {
      Data = data ?? new CareRecipeSaveData();
      CareRecipeGenerator.SanitizeRecipe(Data);
      AdvancePastPersistedCompletedSteps();
    }

    public CareRecipeSaveData Data { get; }
    public CareActionType CurrentAction => Data.CurrentAction;

    public CareRecipeStepResult CompleteCurrentAction(CareActionType completedAction)
    {
      var index = Data.currentActionIndex;
      if (Data.recipeCompleted || index < 0 || index >= Data.ActionCount ||
          Data.IsStepCompleted(index) || Data.actionList[index] != completedAction)
        return new CareRecipeStepResult(false, Data.recipeCompleted, index, completedAction);

      Data.completedActionMask |= 1 << index;
      Data.currentActionIndex = index + 1;
      AdvancePastPersistedCompletedSteps();
      Data.recipeCompleted = Data.currentActionIndex >= Data.ActionCount;
      return new CareRecipeStepResult(true, Data.recipeCompleted, index, completedAction);
    }

    private void AdvancePastPersistedCompletedSteps()
    {
      if (Data.recipeCompleted) return;
      while (Data.currentActionIndex >= 0 && Data.currentActionIndex < Data.ActionCount &&
             Data.IsStepCompleted(Data.currentActionIndex))
        Data.currentActionIndex++;
      if (Data.ActionCount > 0 && Data.currentActionIndex >= Data.ActionCount)
        Data.recipeCompleted = true;
    }

    public bool TryConsumeCompletionSignal()
    {
      if (!Data.recipeCompleted || Data.completionSignalSent) return false;
      Data.completionSignalSent = true;
      return true;
    }

    public bool TryConsumeForProduction()
    {
      if (!Data.recipeCompleted || Data.completionConsumed) return false;
      Data.completionConsumed = true;
      return true;
    }

    public CareRecipeReplacementResult ReplaceCurrentWithClosedEyeRest()
    {
      var index = Data.currentActionIndex;
      if (Data.recipeCompleted || index < 0 || index >= Data.ActionCount ||
          Data.IsStepCompleted(index) || Data.actionList[index] == CareActionType.ClosedEyeRest)
        return new CareRecipeReplacementResult(false, CurrentAction, index, false, Data.recipeCompleted);
      // Pilot and Guided are one safety-reviewed cadence. Once Pilot has been
      // completed, Guided may not be removed or replaced by another step.
      if (Data.actionList[index] == CareActionType.GuidedEyeCircles && index > 0 &&
          Data.actionList[index - 1] == CareActionType.PilotEyeRoutine && Data.IsStepCompleted(index - 1))
        return new CareRecipeReplacementResult(false, CurrentAction, index, false, Data.recipeCompleted);

      var original = Data.actionList[index];
      var existingRest = Array.IndexOf(Data.actionList, CareActionType.ClosedEyeRest);
      if (existingRest >= 0 && existingRest < index && Data.IsStepCompleted(existingRest))
      {
        RemoveStepAt(Data, index);
        Data.currentActionIndex = Mathf.Clamp(index, 0, Data.ActionCount);
        Data.recipeCompleted = Data.currentActionIndex >= Data.ActionCount;
        return new CareRecipeReplacementResult(true, original, index, true, Data.recipeCompleted);
      }

      Data.actionList[index] = CareActionType.ClosedEyeRest;
      if (Data.originalActionList == null || Data.originalActionList.Length != Data.ActionCount)
        Data.originalActionList = (CareActionType[])Data.actionList.Clone();
      Data.originalActionList[index] = original;
      Data.replacedActionMask |= 1 << index;
      if (existingRest > index) RemoveStepAt(Data, existingRest);
      CareRecipeGenerator.SanitizeRecipe(Data);
      return new CareRecipeReplacementResult(true, original, index, false, Data.recipeCompleted);
    }

    private static void RemoveStepAt(CareRecipeSaveData recipe, int index)
    {
      if (recipe == null || recipe.actionList == null || index < 0 || index >= recipe.actionList.Length) return;
      var actions = recipe.actionList.ToList();
      var originals = (recipe.originalActionList != null && recipe.originalActionList.Length == recipe.actionList.Length
        ? recipe.originalActionList
        : recipe.actionList).ToList();
      actions.RemoveAt(index);
      originals.RemoveAt(index);
      recipe.actionList = actions.ToArray();
      recipe.originalActionList = originals.ToArray();
      recipe.completedActionMask = RemoveMaskBit(recipe.completedActionMask, index);
      recipe.developerSkippedActionMask = RemoveMaskBit(recipe.developerSkippedActionMask, index);
      recipe.replacedActionMask = RemoveMaskBit(recipe.replacedActionMask, index);
    }

    private static int RemoveMaskBit(int mask, int index)
    {
      var lower = mask & ((1 << index) - 1);
      var upper = (mask >> (index + 1)) << index;
      return lower | upper;
    }
  }

  public static class CareRecipeGenerator
  {
    private static readonly CareActionType[] TrainingActions =
    {
      CareActionType.FocusShift,
      CareActionType.PilotEyeRoutine,
      CareActionType.GuidedEyeCircles,
      CareActionType.ClosedEyeRest,
    };

    public const int AllTrainingActionMask = 1 | 2 | 4 | 8;

    private static readonly CareActionType[][] SingleCandidates =
    {
      new[] { CareActionType.FocusShift },
      new[] { CareActionType.ClosedEyeRest },
      new[] { CareActionType.GuidedEyeCircles },
    };

    private static readonly CareActionType[][] DoubleCandidates =
    {
      new[] { CareActionType.FocusShift, CareActionType.ClosedEyeRest },
    };

    private static readonly CareActionType[][] TripleCandidates =
    {
      new[] { CareActionType.FocusShift, CareActionType.GuidedEyeCircles, CareActionType.ClosedEyeRest },
      new[] { CareActionType.PilotEyeRoutine, CareActionType.GuidedEyeCircles, CareActionType.ClosedEyeRest },
    };

    public static CareRecipeSaveData CreateForShift(
      CareStationSaveData save,
      int seed,
      CareRecipeGenerationSettings settings)
    {
      if (save == null) throw new ArgumentNullException(nameof(save));
      if (!HasCompletedTraining(save))
        return CreateTraining(NextTrainingIndex(save), save.careShiftId, seed);

      if (save.formalRecipesCreated == 0)
      {
        save.formalRecipesCreated++;
        return Build(CareRecipeType.Double, save.careShiftId, seed,
          new[] { CareActionType.FocusShift, CareActionType.ClosedEyeRest },
          $"recipe_{save.careShiftId}_{seed}_first_formal");
      }
      if (save.formalRecipesCreated == 1)
      {
        save.formalRecipesCreated++;
        return Build(CareRecipeType.Triple, save.careShiftId, seed,
          new[] { CareActionType.PilotEyeRoutine, CareActionType.GuidedEyeCircles, CareActionType.ClosedEyeRest },
          $"recipe_{save.careShiftId}_{seed}_second_formal");
      }

      var type = PickType(seed, settings);
      var recipe = CreateFormal(
        type,
        save.careShiftId,
        seed,
        save.recentRecipeHistory,
        save.focusShiftCooldownUntilShiftId,
        save.guidedEyeCirclesCooldownUntilShiftId,
        settings.MaximumAttempts);
      save.formalRecipesCreated++;
      return recipe;
    }

    public static CareRecipeSaveData CreateTraining(int trainingIndex, int shiftId, int seed)
    {
      var index = Mathf.Clamp(trainingIndex, 0, TrainingActions.Length - 1);
      var actions = new[] { TrainingActions[index] };
      return Build(CareRecipeType.Training, shiftId, seed, actions, $"training_{index + 1}_shift_{shiftId}");
    }

    public static CareRecipeSaveData CreateFormal(
      CareRecipeType requestedType,
      int shiftId,
      int seed,
      IReadOnlyList<string> recentHistory,
      int focusCooldownUntilShiftId,
      int guidedCooldownUntilShiftId,
      int maximumAttempts = 32)
    {
      var random = new System.Random(seed);
      if (requestedType == CareRecipeType.Single)
      {
        var single = SingleCandidates[(seed & int.MaxValue) % SingleCandidates.Length];
        return Build(CareRecipeType.Single, shiftId, seed, single,
          $"recipe_{shiftId}_{seed}_{Signature(single)}");
      }
      var targetLength = LengthForType(requestedType);
      var lengths = targetLength >= 3 ? new[] { 3, 2 } : new[] { 2, 3 };
      foreach (var length in lengths)
      {
        var pool = CandidatesForLength(length)
          .Where(candidate => IsAvailable(candidate, shiftId, focusCooldownUntilShiftId, guidedCooldownUntilShiftId))
          .ToArray();
        if (pool.Length == 0) continue;

        var attempts = Mathf.Clamp(maximumAttempts, 1, 128);
        for (var attempt = 0; attempt < attempts; attempt++)
        {
          var candidate = pool[random.Next(pool.Length)];
          var signature = Signature(candidate);
          if (IsImmediateRepeat(signature, recentHistory)) continue;
          if (AppearsInRecentHistory(signature, recentHistory) &&
              pool.Any(other => !AppearsInRecentHistory(Signature(other), recentHistory))) continue;
          return Build(TypeForLength(length), shiftId, seed, candidate, $"recipe_{shiftId}_{seed}_{signature}");
        }

        var deterministic = pool
          .OrderBy(candidate => AppearsInRecentHistory(Signature(candidate), recentHistory) ? 1 : 0)
          .ThenBy(Signature, StringComparer.Ordinal)
          .FirstOrDefault(candidate => !IsImmediateRepeat(Signature(candidate), recentHistory));
        if (deterministic != null)
          return Build(TypeForLength(length), shiftId, seed, deterministic, $"recipe_{shiftId}_{seed}_{Signature(deterministic)}");
      }

      // Cooldowns and history may exhaust triples. A paced Focus + Rest double
      // is the deterministic formal fallback and is marked as a deep rest so
      // the complete routine remains inside the 2–3 minute target.
      return Build(
        CareRecipeType.Double,
        shiftId,
        seed,
        new[] { CareActionType.FocusShift, CareActionType.ClosedEyeRest },
        $"recipe_{shiftId}_{seed}_focus_rest");
    }

    public static void ApplyCompletionToProgress(CareStationSaveData save, CareRecipeSaveData recipe)
    {
      if (save == null || recipe == null || !recipe.recipeCompleted) return;
      if (recipe.recipeType == CareRecipeType.Training)
      {
        if (recipe.actionList != null && recipe.actionList.Length == 1)
          save.completedTrainingActionMask |= TrainingBit(recipe.actionList[0]);
        save.completedTrainingActionMask &= AllTrainingActionMask;
        save.trainingProgress = CompletedTrainingCount(save.completedTrainingActionMask);
      }
      if (recipe.actionList.Contains(CareActionType.GuidedEyeCircles))
        save.guidedEyeCirclesCooldownUntilShiftId = Mathf.Max(save.guidedEyeCirclesCooldownUntilShiftId, recipe.createdShiftId + 1);
      AddHistory(save, Signature(recipe.actionList));
    }

    public static void SanitizeRecipe(CareRecipeSaveData recipe)
    {
      if (recipe == null) return;
      if (recipe.actionList == null) recipe.actionList = Array.Empty<CareActionType>();
      // Normal generated recipes contain at most three actions. Inspection is
      // also deterministic and currently uses the atomic Pilot -> Guided pair
      // followed by deep Rest.
      var maximumActions = recipe.recipeType == CareRecipeType.Inspection ? 4 : 3;
      recipe.actionList = recipe.actionList
        .Where(action => action != CareActionType.None && Enum.IsDefined(typeof(CareActionType), action) &&
                         !CareActionLibrary.IsRetiredTask(action))
        .Take(maximumActions)
        .ToArray();
      if (recipe.originalActionList == null || recipe.originalActionList.Length != recipe.actionList.Length)
        recipe.originalActionList = (CareActionType[])recipe.actionList.Clone();
      else
        recipe.originalActionList = recipe.originalActionList
          .Select((action, index) => action == CareActionType.None || !Enum.IsDefined(typeof(CareActionType), action)
            ? recipe.actionList[index]
            : action)
          .ToArray();
      recipe.currentActionIndex = Mathf.Clamp(recipe.currentActionIndex, 0, recipe.actionList.Length);
      var validMask = recipe.actionList.Length <= 0 ? 0 : (1 << recipe.actionList.Length) - 1;
      recipe.completedActionMask &= validMask;
      recipe.developerSkippedActionMask &= validMask & recipe.completedActionMask;
      recipe.replacedActionMask &= validMask;
      recipe.createdShiftId = Mathf.Max(0, recipe.createdShiftId);
      recipe.routineIntroElapsedSeconds = Mathf.Max(0f, recipe.routineIntroElapsedSeconds);
      if (recipe.currentActionIndex >= recipe.actionList.Length && recipe.actionList.Length > 0)
        recipe.recipeCompleted = true;
      if (!recipe.recipeCompleted) recipe.completionConsumed = false;
      if (!Enum.IsDefined(typeof(CareRecipeType), recipe.recipeType)) recipe.recipeType = CareRecipeType.Single;
    }

    public static int TrainingBit(CareActionType action)
    {
      switch (action)
      {
        case CareActionType.FocusShift: return 1;
        case CareActionType.PilotEyeRoutine: return 2;
        case CareActionType.GuidedEyeCircles: return 4;
        case CareActionType.ClosedEyeRest: return 8;
        default: return 0;
      }
    }

    public static int CompletedTrainingCount(int mask)
    {
      mask &= AllTrainingActionMask;
      var count = 0;
      while (mask != 0)
      {
        count += mask & 1;
        mask >>= 1;
      }
      return count;
    }

    public static bool HasCompletedTraining(CareStationSaveData save)
    {
      return save != null &&
             ((save.completedTrainingActionMask & AllTrainingActionMask) == AllTrainingActionMask ||
              save.trainingProgress >= TrainingActions.Length);
    }

    public static int NextTrainingIndex(CareStationSaveData save)
    {
      if (save == null) return 0;
      var mask = save.completedTrainingActionMask & AllTrainingActionMask;
      // Runtime-only test data and old callers may still express sequential
      // progress without the v17 mask. Treat that as the new sequence; loaded
      // v16 saves are mapped explicitly by the save migration.
      if (mask == 0 && save.trainingProgress > 0)
        for (var i = 0; i < Mathf.Clamp(save.trainingProgress, 0, TrainingActions.Length); i++)
          mask |= TrainingBit(TrainingActions[i]);
      for (var index = 0; index < TrainingActions.Length; index++)
        if ((mask & TrainingBit(TrainingActions[index])) == 0) return index;
      return TrainingActions.Length - 1;
    }

    /// <summary>
    /// Removes the retired v16 action while preserving the completion
    /// state of every real step. Active formal recipes are supplemented only
    /// when required to remain a valid 2-3 minute routine.
    /// </summary>
    public static bool RemoveRetiredBlinkReset(CareRecipeSaveData recipe, bool supplementActiveFormal)
    {
      if (recipe == null || recipe.actionList == null ||
          !recipe.actionList.Any(CareActionLibrary.IsRetiredTask) &&
          CareActionLibrary.HasPilotGuidedInvariant(recipe.actionList)) return false;

      var oldActions = recipe.actionList;
      var oldOriginals = recipe.originalActionList != null && recipe.originalActionList.Length == oldActions.Length
        ? recipe.originalActionList
        : oldActions;
      var actions = new List<CareActionType>();
      var originals = new List<CareActionType>();
      var completedMask = 0;
      var skippedMask = 0;
      var replacedMask = 0;
      for (var oldIndex = 0; oldIndex < oldActions.Length; oldIndex++)
      {
        if (CareActionLibrary.IsRetiredTask(oldActions[oldIndex])) continue;
        var newIndex = actions.Count;
        actions.Add(oldActions[oldIndex]);
        originals.Add(CareActionLibrary.IsRetiredTask(oldOriginals[oldIndex])
          ? oldActions[oldIndex]
          : oldOriginals[oldIndex]);
        if ((recipe.completedActionMask & (1 << oldIndex)) != 0) completedMask |= 1 << newIndex;
        if ((recipe.developerSkippedActionMask & (1 << oldIndex)) != 0) skippedMask |= 1 << newIndex;
        if ((recipe.replacedActionMask & (1 << oldIndex)) != 0) replacedMask |= 1 << newIndex;
      }

      var originalRecipeType = recipe.recipeType;
      var preserveCompleted = recipe.recipeCompleted || recipe.completionConsumed;
      if (!preserveCompleted && recipe.recipeType != CareRecipeType.Training)
        EnsurePilotFollowedByGuided(actions, originals, ref completedMask, ref skippedMask, ref replacedMask);
      if (!preserveCompleted && supplementActiveFormal &&
          recipe.recipeType != CareRecipeType.Training && recipe.recipeType != CareRecipeType.Inspection)
        SupplementFormalActions(actions, originals, ref completedMask, ref skippedMask, ref replacedMask);

      recipe.actionList = actions.ToArray();
      recipe.originalActionList = originals.ToArray();
      recipe.completedActionMask = completedMask;
      recipe.developerSkippedActionMask = skippedMask & completedMask;
      recipe.replacedActionMask = replacedMask;
      if (preserveCompleted)
      {
        recipe.currentActionIndex = recipe.actionList.Length;
        recipe.completedActionMask = recipe.actionList.Length == 0 ? 0 : (1 << recipe.actionList.Length) - 1;
        recipe.recipeCompleted = true;
      }
      else
      {
        recipe.currentActionIndex = FirstIncompleteIndex(recipe.completedActionMask, recipe.actionList.Length);
        recipe.recipeCompleted = recipe.actionList.Length > 0 && recipe.currentActionIndex >= recipe.actionList.Length;
        if (!recipe.recipeCompleted)
        {
          recipe.completionSignalSent = false;
          recipe.completionConsumed = false;
        }
      }
      recipe.recipeType = originalRecipeType == CareRecipeType.Training || originalRecipeType == CareRecipeType.Inspection
        ? originalRecipeType
        : TypeForLength(recipe.actionList.Length);
      recipe.deepRest = recipe.recipeType != CareRecipeType.Training && recipe.recipeType != CareRecipeType.Single &&
                        CareActionLibrary.EstimatedRecipeSeconds(recipe.actionList, false) <
                        CareActionLibrary.MinimumFormalRoutineSeconds;
      return true;
    }

    private static void EnsurePilotFollowedByGuided(
      List<CareActionType> actions,
      List<CareActionType> originals,
      ref int completedMask,
      ref int skippedMask,
      ref int replacedMask)
    {
      var pilot = actions.IndexOf(CareActionType.PilotEyeRoutine);
      if (pilot < 0) return;
      var guided = actions.IndexOf(CareActionType.GuidedEyeCircles);
      if (guided == pilot + 1) return;

      var guidedCompleted = false;
      var guidedSkipped = false;
      var guidedReplaced = false;
      if (guided >= 0)
      {
        guidedCompleted = (completedMask & (1 << guided)) != 0;
        guidedSkipped = (skippedMask & (1 << guided)) != 0;
        guidedReplaced = (replacedMask & (1 << guided)) != 0;
        actions.RemoveAt(guided);
        originals.RemoveAt(guided);
        completedMask = RemoveMaskBit(completedMask, guided);
        skippedMask = RemoveMaskBit(skippedMask, guided);
        replacedMask = RemoveMaskBit(replacedMask, guided);
        if (guided < pilot) pilot--;
      }

      var insert = pilot + 1;
      actions.Insert(insert, CareActionType.GuidedEyeCircles);
      originals.Insert(insert, CareActionType.GuidedEyeCircles);
      completedMask = InsertEmptyMaskBit(completedMask, insert);
      skippedMask = InsertEmptyMaskBit(skippedMask, insert);
      replacedMask = InsertEmptyMaskBit(replacedMask, insert);
      if (guidedCompleted) completedMask |= 1 << insert;
      if (guidedSkipped) skippedMask |= 1 << insert;
      if (guidedReplaced) replacedMask |= 1 << insert;
    }

    private static void SupplementFormalActions(
      List<CareActionType> actions,
      List<CareActionType> originals,
      ref int completedMask,
      ref int skippedMask,
      ref int replacedMask)
    {
      if (!actions.Any(CareActionLibrary.IsActiveAction) && !actions.Contains(CareActionType.FocusShift))
      {
        var restIndex = actions.IndexOf(CareActionType.ClosedEyeRest);
        if (restIndex < 0) restIndex = actions.Count;
        InsertUncompletedAction(actions, originals, restIndex, CareActionType.FocusShift,
          ref completedMask, ref skippedMask, ref replacedMask);
      }
      if (!actions.Any(CareActionLibrary.IsRestOrOffscreenAction))
      {
        actions.Add(CareActionType.ClosedEyeRest);
        originals.Add(CareActionType.ClosedEyeRest);
      }
      if (CareActionLibrary.EstimatedRecipeSeconds(actions, true) < CareActionLibrary.MinimumFormalRoutineSeconds &&
          actions.Count < 3)
      {
        if (!actions.Contains(CareActionType.FocusShift))
        {
          var restIndex = actions.IndexOf(CareActionType.ClosedEyeRest);
          if (restIndex < 0) restIndex = actions.Count;
          InsertUncompletedAction(actions, originals, restIndex, CareActionType.FocusShift,
            ref completedMask, ref skippedMask, ref replacedMask);
        }
        else if (!actions.Contains(CareActionType.ClosedEyeRest))
        {
          actions.Add(CareActionType.ClosedEyeRest);
          originals.Add(CareActionType.ClosedEyeRest);
        }
      }
      EnsurePilotFollowedByGuided(actions, originals, ref completedMask, ref skippedMask, ref replacedMask);
      if (actions.Count > 3)
      {
        actions.RemoveRange(3, actions.Count - 3);
        originals.RemoveRange(3, originals.Count - 3);
        var validMask = (1 << 3) - 1;
        completedMask &= validMask;
        skippedMask &= validMask;
        replacedMask &= validMask;
      }
    }

    private static void InsertUncompletedAction(
      List<CareActionType> actions,
      List<CareActionType> originals,
      int index,
      CareActionType action,
      ref int completedMask,
      ref int skippedMask,
      ref int replacedMask)
    {
      actions.Insert(index, action);
      originals.Insert(index, action);
      completedMask = InsertEmptyMaskBit(completedMask, index);
      skippedMask = InsertEmptyMaskBit(skippedMask, index);
      replacedMask = InsertEmptyMaskBit(replacedMask, index);
    }

    private static int InsertEmptyMaskBit(int mask, int index)
    {
      var lower = mask & ((1 << index) - 1);
      var upper = (mask & ~((1 << index) - 1)) << 1;
      return lower | upper;
    }

    private static int RemoveMaskBit(int mask, int index)
    {
      var lower = mask & ((1 << index) - 1);
      var upper = (mask >> (index + 1)) << index;
      return lower | upper;
    }

    private static int FirstIncompleteIndex(int mask, int length)
    {
      for (var index = 0; index < length; index++)
        if ((mask & (1 << index)) == 0) return index;
      return length;
    }

    public static string Signature(IEnumerable<CareActionType> actions)
    {
      return actions == null
        ? string.Empty
        : string.Join(">", actions.Select(action => action.ToString()));
    }

    public static int TrainingIndex(CareRecipeSaveData recipe)
    {
      if (recipe == null || recipe.recipeType != CareRecipeType.Training || recipe.actionList == null || recipe.actionList.Length != 1)
        return -1;
      return Array.IndexOf(TrainingActions, recipe.actionList[0]);
    }

    private static CareRecipeType PickType(int seed, CareRecipeGenerationSettings settings)
    {
      var total = settings.DoubleWeight + settings.TripleWeight;
      if (total <= 0.0001f) return CareRecipeType.Double;
      var roll = new System.Random(seed ^ 0x4f1bbcdc).NextDouble() * total;
      if (roll < settings.DoubleWeight) return CareRecipeType.Double;
      return CareRecipeType.Triple;
    }

    private static IEnumerable<CareActionType[]> CandidatesForLength(int length)
    {
      switch (length)
      {
        case 3: return TripleCandidates;
        case 2: return DoubleCandidates;
        default: return SingleCandidates;
      }
    }

    private static int LengthForType(CareRecipeType type)
    {
      return type == CareRecipeType.Triple ? 3 : type == CareRecipeType.Double ? 2 : 1;
    }

    private static CareRecipeType TypeForLength(int length)
    {
      return length >= 3 ? CareRecipeType.Triple : length == 2 ? CareRecipeType.Double : CareRecipeType.Single;
    }

    private static bool IsAvailable(
      IReadOnlyCollection<CareActionType> actions,
      int shiftId,
      int focusCooldownUntilShiftId,
      int guidedCooldownUntilShiftId)
    {
      if (actions == null || actions.Count == 0 || actions.Count != actions.Distinct().Count()) return false;
      if (actions.Contains(CareActionType.GuidedEyeCircles) && shiftId <= guidedCooldownUntilShiftId) return false;
      if (!CareActionLibrary.HasValidFormalComposition(actions)) return false;
      var deepRest = CareActionLibrary.EstimatedRecipeSeconds(actions, false) <
                     CareActionLibrary.MinimumFormalRoutineSeconds;
      var duration = CareActionLibrary.EstimatedRecipeSeconds(actions, deepRest);
      return duration >= CareActionLibrary.MinimumFormalRoutineSeconds &&
             duration <= CareActionLibrary.MaximumFormalRoutineSeconds;
    }

    private static bool IsImmediateRepeat(string signature, IReadOnlyList<string> recentHistory)
    {
      return recentHistory != null && recentHistory.Count > 0 &&
             string.Equals(recentHistory[recentHistory.Count - 1], signature, StringComparison.Ordinal);
    }

    private static bool AppearsInRecentHistory(string signature, IReadOnlyList<string> recentHistory)
    {
      if (recentHistory == null) return false;
      for (var index = Mathf.Max(0, recentHistory.Count - 3); index < recentHistory.Count; index++)
        if (string.Equals(recentHistory[index], signature, StringComparison.Ordinal)) return true;
      return false;
    }

    private static CareRecipeSaveData Build(
      CareRecipeType type,
      int shiftId,
      int seed,
      IReadOnlyList<CareActionType> actions,
      string id)
    {
      var actionArray = actions.ToArray();
      var deepRest = type != CareRecipeType.Training && type != CareRecipeType.Single &&
                     (actionArray.Contains(CareActionType.PilotEyeRoutine) ||
                      CareActionLibrary.EstimatedRecipeSeconds(actionArray, false) <
                      CareActionLibrary.MinimumFormalRoutineSeconds);
      return new CareRecipeSaveData
      {
        recipeId = id,
        recipeSeed = seed,
        recipeType = type,
        actionList = actionArray,
        originalActionList = (CareActionType[])actionArray.Clone(),
        currentActionIndex = 0,
        completedActionMask = 0,
        createdShiftId = Mathf.Max(1, shiftId),
        deepRest = deepRest,
      };
    }

    private static void AddHistory(CareStationSaveData save, string signature)
    {
      if (string.IsNullOrEmpty(signature)) return;
      var history = save.recentRecipeHistory?.ToList() ?? new List<string>();
      if (history.Count == 0 || !string.Equals(history[history.Count - 1], signature, StringComparison.Ordinal))
        history.Add(signature);
      if (history.Count > 3) history.RemoveRange(0, history.Count - 3);
      save.recentRecipeHistory = history.ToArray();
    }
  }

  public static class CareRecipePipeline
  {
    public const int Filter = 1;
    public const int Tank = 2;
    public const int Press = 4;
    public const int Rail = 8;
    public const int CareCore = 16;

    public static int StageMaskForAction(CareActionType action)
    {
      switch (action)
      {
        case CareActionType.FocusShift: return Tank | Press;
        case CareActionType.ClosedEyeRest: return Tank | CareCore;
        case CareActionType.GuidedEyeCircles: return CareCore;
        case CareActionType.PilotEyeRoutine: return Filter | CareCore;
        default: return 0;
      }
    }

    public static int StageMaskForCompletion(int completedStepIndex, int actionCount)
    {
      if (completedStepIndex < 0 || actionCount <= 0 || completedStepIndex >= actionCount) return 0;
      if (actionCount == 1) return Filter | Tank | Press;
      if (actionCount == 2) return completedStepIndex == 0 ? Filter : Tank | Press;
      return completedStepIndex == 0 ? Filter : completedStepIndex == 1 ? Tank : Press;
    }
  }
}
