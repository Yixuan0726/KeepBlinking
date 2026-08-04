using System;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  public enum OffScreenDirection
  {
    Left,
    Right,
    Up,
    Down,
  }

  [Serializable]
  public struct OffScreenEyeMovementThresholds
  {
    public float LeftDegrees;
    public float RightDegrees;
    public float UpDegrees;
    public float DownDegrees;
    public float DirectionHoldSeconds;
    public float ReturnHorizontalDegrees;
    public float ReturnVerticalDegrees;
    public float ReturnHoldSeconds;
    public float MaximumHeadYawDegrees;
    public float MaximumHeadPitchDegrees;

    public static OffScreenEyeMovementThresholds Default => new OffScreenEyeMovementThresholds
    {
      LeftDegrees = 12f,
      RightDegrees = 12f,
      UpDegrees = 10f,
      DownDegrees = 10f,
      DirectionHoldSeconds = 0.45f,
      ReturnHorizontalDegrees = 6f,
      ReturnVerticalDegrees = 5f,
      ReturnHoldSeconds = 0.35f,
      MaximumHeadYawDegrees = 12f,
      MaximumHeadPitchDegrees = 12f,
    };
  }

  public readonly struct OffScreenEyeMovementSample
  {
    public OffScreenEyeMovementSample(
      bool faceTracked,
      bool gazeValid,
      bool eyesOpen,
      bool isBlinking,
      bool eyesClosed,
      bool hasHeadPose,
      Vector2 rawGazeDegrees,
      Vector2 centeredGazeDegrees,
      float headYawDegrees,
      float headPitchDegrees)
    {
      FaceTracked = faceTracked;
      GazeValid = gazeValid;
      EyesOpen = eyesOpen;
      IsBlinking = isBlinking;
      EyesClosed = eyesClosed;
      HasHeadPose = hasHeadPose;
      RawGazeDegrees = rawGazeDegrees;
      CenteredGazeDegrees = centeredGazeDegrees;
      HeadYawDegrees = headYawDegrees;
      HeadPitchDegrees = headPitchDegrees;
    }

    public bool FaceTracked { get; }
    public bool GazeValid { get; }
    public bool EyesOpen { get; }
    public bool IsBlinking { get; }
    public bool EyesClosed { get; }
    public bool HasHeadPose { get; }
    public Vector2 RawGazeDegrees { get; }
    public Vector2 CenteredGazeDegrees { get; }
    public float HeadYawDegrees { get; }
    public float HeadPitchDegrees { get; }
  }

  public sealed class OffScreenEyeMovementDetector
  {
    private float _directionHoldSeconds;
    private float _returnHoldSeconds;

    public float DirectionHoldProgress { get; private set; }
    public float ReturnCenterProgress { get; private set; }
    public bool IsHeadWithinLimit { get; private set; }
    public bool IsDirectionRegionActive { get; private set; }
    public bool IsReturnRegionActive { get; private set; }

    public bool UpdateDirection(
      OffScreenDirection direction,
      in OffScreenEyeMovementSample sample,
      in OffScreenEyeMovementThresholds thresholds,
      float deltaTime)
    {
      IsHeadWithinLimit = IsHeadAllowed(sample, thresholds);
      IsDirectionRegionActive = IsDirectionActive(direction, sample, thresholds);
      if (!IsSampleAllowed(sample) || !IsHeadWithinLimit || !IsDirectionRegionActive)
      {
        _directionHoldSeconds = 0f;
        DirectionHoldProgress = 0f;
        return false;
      }

      _directionHoldSeconds += Mathf.Max(0f, deltaTime);
      var required = Mathf.Max(0.05f, thresholds.DirectionHoldSeconds);
      DirectionHoldProgress = Mathf.Clamp01(_directionHoldSeconds / required);
      return _directionHoldSeconds >= required;
    }

    public bool UpdateReturnCenter(
      in OffScreenEyeMovementSample sample,
      in OffScreenEyeMovementThresholds thresholds,
      float deltaTime)
    {
      IsHeadWithinLimit = IsHeadAllowed(sample, thresholds);
      IsReturnRegionActive = IsReturnActive(sample, thresholds);
      if (!IsSampleAllowed(sample) || !IsHeadWithinLimit || !IsReturnRegionActive)
      {
        _returnHoldSeconds = 0f;
        ReturnCenterProgress = 0f;
        return false;
      }

      _returnHoldSeconds += Mathf.Max(0f, deltaTime);
      var required = Mathf.Max(0.05f, thresholds.ReturnHoldSeconds);
      ReturnCenterProgress = Mathf.Clamp01(_returnHoldSeconds / required);
      return _returnHoldSeconds >= required;
    }

    public void ResetDirectionHold()
    {
      _directionHoldSeconds = 0f;
      DirectionHoldProgress = 0f;
      IsDirectionRegionActive = false;
    }

    public void ResetReturnHold()
    {
      _returnHoldSeconds = 0f;
      ReturnCenterProgress = 0f;
      IsReturnRegionActive = false;
    }

    public void Reset()
    {
      ResetDirectionHold();
      ResetReturnHold();
      IsHeadWithinLimit = false;
    }

    private static bool IsSampleAllowed(in OffScreenEyeMovementSample sample)
    {
      return sample.FaceTracked &&
             sample.GazeValid &&
             sample.EyesOpen &&
             !sample.IsBlinking &&
             !sample.EyesClosed &&
             sample.HasHeadPose;
    }

    private static bool IsHeadAllowed(
      in OffScreenEyeMovementSample sample,
      in OffScreenEyeMovementThresholds thresholds)
    {
      return sample.HasHeadPose &&
             Mathf.Abs(sample.HeadYawDegrees) <= Mathf.Max(0f, thresholds.MaximumHeadYawDegrees) &&
             Mathf.Abs(sample.HeadPitchDegrees) <= Mathf.Max(0f, thresholds.MaximumHeadPitchDegrees);
    }

    private static bool IsDirectionActive(
      OffScreenDirection direction,
      in OffScreenEyeMovementSample sample,
      in OffScreenEyeMovementThresholds thresholds)
    {
      var gaze = sample.CenteredGazeDegrees;
      switch (direction)
      {
        case OffScreenDirection.Left:
          return gaze.x <= -Mathf.Abs(thresholds.LeftDegrees);
        case OffScreenDirection.Right:
          return gaze.x >= Mathf.Abs(thresholds.RightDegrees);
        case OffScreenDirection.Up:
          return gaze.y >= Mathf.Abs(thresholds.UpDegrees);
        case OffScreenDirection.Down:
          return gaze.y <= -Mathf.Abs(thresholds.DownDegrees);
        default:
          return false;
      }
    }

    private static bool IsReturnActive(
      in OffScreenEyeMovementSample sample,
      in OffScreenEyeMovementThresholds thresholds)
    {
      return Mathf.Abs(sample.CenteredGazeDegrees.x) <= Mathf.Abs(thresholds.ReturnHorizontalDegrees) &&
             Mathf.Abs(sample.CenteredGazeDegrees.y) <= Mathf.Abs(thresholds.ReturnVerticalDegrees);
    }
  }
}
