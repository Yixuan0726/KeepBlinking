using System;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  public enum CareMovementDirection
  {
    Center,
    Left,
    Right,
    Up,
    Down,
    Near,
    Far,
  }

  public enum CareExperienceState
  {
    Raw,
    Focused,
    Rested,
  }

  public static class CareExperienceStateInfo
  {
    public static int Value(CareExperienceState state)
    {
      return state == CareExperienceState.Rested ? 3 : state == CareExperienceState.Focused ? 2 : 1;
    }

    public static string Label(CareExperienceState state)
    {
      return state == CareExperienceState.Rested
        ? "RESTED XP"
        : state == CareExperienceState.Focused ? "FOCUSED XP" : "RAW XP";
    }

    public static Color Color(CareExperienceState state)
    {
      return state == CareExperienceState.Rested
        ? KeepBlinkingTheme.AccentWarm
        : state == CareExperienceState.Focused ? KeepBlinkingTheme.AccentPrimary : KeepBlinkingTheme.TextPrimary;
    }
  }

  public readonly struct CareStepReward
  {
    public CareStepReward(CareMovementDirection direction, int count, bool gold, float progress)
      : this(direction, count, gold ? CareExperienceState.Rested : CareExperienceState.Raw, progress)
    {
    }

    public CareStepReward(CareMovementDirection direction, int count, CareExperienceState experienceState, float progress)
    {
      Direction = direction;
      Count = count;
      ExperienceState = experienceState;
      Progress = progress;
    }

    public CareMovementDirection Direction { get; }
    public int Count { get; }
    public CareExperienceState ExperienceState { get; }
    public bool Gold => ExperienceState == CareExperienceState.Rested;
    public float Progress { get; }
  }

  public static class CareRewardSegmentLogic
  {
    public static int CountNewSegments(float highestProgress, float progress, int segmentCount)
    {
      segmentCount = Math.Max(0, segmentCount);
      if (segmentCount == 0 || progress <= highestProgress) return 0;
      var previous = Math.Min(segmentCount, (int)Math.Floor(Math.Max(0f, highestProgress) * segmentCount + 0.0001f));
      var current = Math.Min(segmentCount, (int)Math.Floor(Math.Min(1f, progress) * segmentCount + 0.0001f));
      return Math.Max(0, current - previous);
    }
  }

  public static class CareExperienceConversionLogic
  {
    public static int ConvertedCount(int available, float fraction, bool minimumOne)
    {
      available = Math.Max(0, available);
      if (available == 0 || fraction <= 0f) return 0;
      var converted = (int)Math.Ceiling(available * Math.Min(1f, fraction));
      if (minimumOne) converted = Math.Max(1, converted);
      return Math.Min(available, converted);
    }

    public static int TwinPulseRawBonus(int originalPendingValue)
    {
      return Math.Max(0, (int)Math.Floor(Math.Max(0, originalPendingValue) * 0.25f));
    }

    public static int ChainPulseGoldBonus(int originalPendingValue)
    {
      return Math.Max(0, originalPendingValue / 10);
    }
  }

  public static class FirstLevelCareRewardPlan
  {
    public const int DirectionSweepFragments = 14;
    public const int NeutralToNearFragments = 6;
    public const int ShiftSpanFragments = 10;
    public const int FinalNeutralFragments = 4;

    public static int DirectionalFragments(CareMovementDirection first, CareMovementDirection second)
    {
      return first == CareMovementDirection.Center || second == CareMovementDirection.Center
        ? 0
        : DirectionSweepFragments;
    }

    public static int FocusShiftFragments(int cycles)
    {
      cycles = Math.Max(2, cycles);
      return NeutralToNearFragments +
             ShiftSpanFragments * cycles +
             ShiftSpanFragments * (cycles - 1) +
             FinalNeutralFragments;
    }
  }
}
