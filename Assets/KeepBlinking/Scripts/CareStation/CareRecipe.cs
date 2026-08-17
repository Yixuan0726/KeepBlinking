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
      Data.recipeCompleted = Data.currentActionIndex >= Data.ActionCount;
      return new CareRecipeStepResult(true, Data.recipeCompleted, index, completedAction);
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
      CareActionType.ScreenDown,
      CareActionType.ClosedEyeRest,
      CareActionType.FocusShift,
      CareActionType.GuidedEyeCircles,
    };

    private static readonly CareActionType[][] SingleCandidates =
    {
      new[] { CareActionType.ScreenDown },
      new[] { CareActionType.FocusShift },
      new[] { CareActionType.ClosedEyeRest },
      new[] { CareActionType.GuidedEyeCircles },
    };

    private static readonly CareActionType[][] DoubleCandidates =
    {
      new[] { CareActionType.ScreenDown, CareActionType.FocusShift },
      new[] { CareActionType.ScreenDown, CareActionType.ClosedEyeRest },
      new[] { CareActionType.ScreenDown, CareActionType.GuidedEyeCircles },
      new[] { CareActionType.FocusShift, CareActionType.ClosedEyeRest },
      new[] { CareActionType.FocusShift, CareActionType.GuidedEyeCircles },
    };

    private static readonly CareActionType[][] TripleCandidates =
    {
      new[] { CareActionType.ScreenDown, CareActionType.FocusShift, CareActionType.ClosedEyeRest },
      new[] { CareActionType.ScreenDown, CareActionType.FocusShift, CareActionType.GuidedEyeCircles },
    };

    public static CareRecipeSaveData CreateForShift(
      CareStationSaveData save,
      int seed,
      CareRecipeGenerationSettings settings)
    {
      if (save == null) throw new ArgumentNullException(nameof(save));
      if (save.trainingProgress < TrainingActions.Length)
        return CreateTraining(save.trainingProgress, save.careShiftId, seed);

      var forcedDouble = save.formalRecipesCreated < 2;
      var type = forcedDouble ? CareRecipeType.Double : PickType(seed, settings);
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
      var targetLength = LengthForType(requestedType);
      for (var length = targetLength; length >= 1; length--)
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

      // Cooldowns and history can exhaust a requested length. A single Screen
      // Down step is the deterministic, always-valid fallback.
      return Build(
        CareRecipeType.Single,
        shiftId,
        seed,
        new[] { CareActionType.ScreenDown },
        $"recipe_{shiftId}_{seed}_screen_down");
    }

    public static void ApplyCompletionToProgress(CareStationSaveData save, CareRecipeSaveData recipe)
    {
      if (save == null || recipe == null || !recipe.recipeCompleted) return;
      if (recipe.recipeType == CareRecipeType.Training)
        save.trainingProgress = Mathf.Clamp(Mathf.Max(save.trainingProgress, TrainingIndex(recipe) + 1), 0, 4);
      if (recipe.actionList.Contains(CareActionType.FocusShift))
        save.focusShiftCooldownUntilShiftId = Mathf.Max(save.focusShiftCooldownUntilShiftId, recipe.createdShiftId + 1);
      if (recipe.actionList.Contains(CareActionType.GuidedEyeCircles))
        save.guidedEyeCirclesCooldownUntilShiftId = Mathf.Max(save.guidedEyeCirclesCooldownUntilShiftId, recipe.createdShiftId + 1);
      AddHistory(save, Signature(recipe.actionList));
    }

    public static void SanitizeRecipe(CareRecipeSaveData recipe)
    {
      if (recipe == null) return;
      if (recipe.actionList == null) recipe.actionList = Array.Empty<CareActionType>();
      // Normal generated recipes contain at most three actions. Station
      // Inspection is a deterministic four-action system check and must not be
      // truncated by the normal recipe limit when its runtime is restored.
      var maximumActions = recipe.recipeType == CareRecipeType.Inspection ? 4 : 3;
      recipe.actionList = recipe.actionList
        .Where(action => action != CareActionType.None && Enum.IsDefined(typeof(CareActionType), action))
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
      if (recipe.currentActionIndex >= recipe.actionList.Length && recipe.actionList.Length > 0)
        recipe.recipeCompleted = true;
      if (!recipe.recipeCompleted) recipe.completionConsumed = false;
      if (!Enum.IsDefined(typeof(CareRecipeType), recipe.recipeType)) recipe.recipeType = CareRecipeType.Single;
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
      var total = settings.SingleWeight + settings.DoubleWeight + settings.TripleWeight;
      if (total <= 0.0001f) return CareRecipeType.Double;
      var roll = new System.Random(seed ^ 0x4f1bbcdc).NextDouble() * total;
      if (roll < settings.SingleWeight) return CareRecipeType.Single;
      if (roll < settings.SingleWeight + settings.DoubleWeight) return CareRecipeType.Double;
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
      if (actions.Contains(CareActionType.FocusShift) && shiftId <= focusCooldownUntilShiftId) return false;
      if (actions.Contains(CareActionType.GuidedEyeCircles) && shiftId <= guidedCooldownUntilShiftId) return false;
      return !(actions.Contains(CareActionType.GuidedEyeCircles) && actions.Contains(CareActionType.ClosedEyeRest));
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
      return new CareRecipeSaveData
      {
        recipeId = id,
        recipeSeed = seed,
        recipeType = type,
        actionList = actions.ToArray(),
        originalActionList = actions.ToArray(),
        currentActionIndex = 0,
        completedActionMask = 0,
        createdShiftId = Mathf.Max(1, shiftId),
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

    public static int StageMaskForCompletion(int completedStepIndex, int actionCount)
    {
      if (completedStepIndex < 0 || actionCount <= 0 || completedStepIndex >= actionCount) return 0;
      if (actionCount == 1) return Filter | Tank | Press;
      if (actionCount == 2) return completedStepIndex == 0 ? Filter : Tank | Press;
      return completedStepIndex == 0 ? Filter : completedStepIndex == 1 ? Tank : Press;
    }
  }
}
