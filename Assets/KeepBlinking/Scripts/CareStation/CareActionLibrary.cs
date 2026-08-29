using System;
using System.Collections.Generic;
using System.Linq;

namespace KeepBlinking.CareStation
{
  /// <summary>
  /// One authoritative semantic layer for action labels, purposes, station
  /// feedback and routine-duration estimates. It contains no economy logic.
  /// </summary>
  public static class CareActionLibrary
  {
    public const float RoutineIntroSeconds = 2.5f;
    public const float RecipeCompletionFeedbackSeconds = 1.5f;
    public const float NormalRestSeconds = 45f;
    public const float DeepRestSeconds = 60f;
    public const float MinimumFormalRoutineSeconds = 120f;
    public const float MaximumFormalRoutineSeconds = 180f;

    public static string DisplayName(CareActionType type)
    {
      switch (type)
      {
        case CareActionType.FocusShift: return "FOCUS SHIFT";
        case CareActionType.ClosedEyeRest: return "CLOSED-EYE REST";
        case CareActionType.GuidedEyeCircles: return "GUIDED EYE MOVEMENT";
        case CareActionType.PilotEyeRoutine: return "PILOT EYE ROUTINE";
        default: return string.Empty;
      }
    }

    public static string Purpose(CareActionType type)
    {
      switch (type)
      {
        case CareActionType.FocusShift: return "CHANGE YOUR VIEWING DISTANCE FOR ONE MINUTE.";
        case CareActionType.ClosedEyeRest: return "CLOSE YOUR EYES AND LET THEM REST.";
        case CareActionType.GuidedEyeCircles: return "FOLLOW THE SLOW CIRCLES, THEN RELAX.";
        case CareActionType.PilotEyeRoutine: return "FOLLOW THE FOUR AXES SLOWLY.";
        default: return string.Empty;
      }
    }

    public static string StationPurpose(CareActionType type)
    {
      switch (type)
      {
        case CareActionType.FocusShift: return "RESTORE THE PRESS AND TANK";
        case CareActionType.ClosedEyeRest: return "RESTORE THE TANK AND CARE CORE";
        case CareActionType.GuidedEyeCircles: return "STABILIZE THE CARE CORE";
        case CareActionType.PilotEyeRoutine: return "RESTORE THE FILTER AND CARE CORE";
        default: return string.Empty;
      }
    }

    public static float EstimatedSeconds(CareActionType type, bool deepRest = false)
    {
      switch (type)
      {
        case CareActionType.FocusShift: return 60f;
        // Includes the reliable-close and reliable-open holds around the
        // configured active rest duration.
        case CareActionType.ClosedEyeRest: return (deepRest ? DeepRestSeconds : NormalRestSeconds) + 2f;
        case CareActionType.GuidedEyeCircles: return 43f;
        case CareActionType.PilotEyeRoutine: return 45f;
        default: return 0f;
      }
    }

    public static float EstimatedRecipeSeconds(IEnumerable<CareActionType> actions, bool deepRest)
    {
      if (actions == null) return 0f;
      var list = actions.Where(action => action != CareActionType.None).ToArray();
      if (list.Length == 0) return 0f;
      // Includes the non-interactive opening and short device transitions, but
      // never counts tracking loss or player pauses.
      return RoutineIntroSeconds + RecipeCompletionFeedbackSeconds +
             list.Sum(action => EstimatedSeconds(action, deepRest)) +
             Math.Max(0, list.Length - 1) * 0.75f;
    }

    public static bool IsActiveAction(CareActionType type)
    {
      return type == CareActionType.FocusShift || type == CareActionType.GuidedEyeCircles ||
             type == CareActionType.PilotEyeRoutine;
    }

    public static bool IsRestOrOffscreenAction(CareActionType type)
    {
      return type == CareActionType.ClosedEyeRest;
    }

    public static bool HasValidFormalComposition(IReadOnlyCollection<CareActionType> actions)
    {
      return actions != null && actions.Count >= 2 && actions.Count <= 3 &&
             actions.Count == actions.Distinct().Count() &&
             actions.All(action => !IsRetiredTask(action)) &&
             actions.Any(IsActiveAction) && actions.Any(IsRestOrOffscreenAction) &&
             HasPilotGuidedInvariant(actions);
    }

    public static bool IsRetiredTask(CareActionType type)
    {
      return type == CareActionType.BlinkReset || type == CareActionType.ScreenDown;
    }

    public static bool HasPilotGuidedInvariant(IEnumerable<CareActionType> actions)
    {
      if (actions == null) return false;
      var list = actions.ToArray();
      var pilot = Array.IndexOf(list, CareActionType.PilotEyeRoutine);
      return pilot < 0 || pilot + 1 < list.Length && list[pilot + 1] == CareActionType.GuidedEyeCircles;
    }
  }
}
