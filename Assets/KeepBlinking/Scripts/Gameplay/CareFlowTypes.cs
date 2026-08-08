using System;

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

  public readonly struct CareStepReward
  {
    public CareStepReward(CareMovementDirection direction, int count, bool gold, float progress)
    {
      Direction = direction;
      Count = count;
      Gold = gold;
      Progress = progress;
    }

    public CareMovementDirection Direction { get; }
    public int Count { get; }
    public bool Gold { get; }
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
