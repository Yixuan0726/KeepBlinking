using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using KeepBlinking.Gameplay;
using KeepBlinking.Input;
using NUnit.Framework;
using UnityEngine;

namespace KeepBlinking.Tests
{
  public sealed class FocusShiftStabilityTests
  {
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

    [Test]
    public void TwoCyclePlanHasNoIntermediateNeutralStep()
    {
      var root = new GameObject("Focus Shift Plan Test");
      try
      {
        var controller = root.AddComponent<FocusShiftController>();
        Assert.That(controller.StartFocusShift(1f), Is.True);
        var steps = (IList)GetField(controller, "_steps");
        var directions = new List<CareMovementDirection>(steps.Count);
        foreach (var step in steps)
        {
          var property = step.GetType().GetProperty("Direction", BindingFlags.Instance | BindingFlags.Public);
          Assert.That(property, Is.Not.Null);
          directions.Add((CareMovementDirection)property.GetValue(step));
        }

        Assert.That(directions, Is.EqualTo(new[]
        {
          CareMovementDirection.Near,
          CareMovementDirection.Far,
          CareMovementDirection.Near,
          CareMovementDirection.Far,
          CareMovementDirection.Center,
        }));
        Assert.That(controller.State, Is.EqualTo(FocusShiftState.CalibratingNeutral));
        Assert.That(controller.CurrentStepIndex, Is.EqualTo(-1));
      }
      finally
      {
        Object.DestroyImmediate(root);
      }
    }

    [Test]
    public void StartKeepsSessionBaselineAndRequiresFreshLocalMedian()
    {
      var root = new GameObject("Focus Shift Baseline Test");
      try
      {
        var controller = root.AddComponent<FocusShiftController>();
        Assert.That(controller.StartFocusShift(0.42f), Is.True);
        Assert.That(controller.SessionBaselineFaceScale, Is.EqualTo(0.42f));
        Assert.That(controller.HasLocalFocusBaseline, Is.False);
        Assert.That(controller.LocalFocusBaseline, Is.LessThan(0f));
        Assert.That(GetFloat(controller, "_localBaselineCaptureSeconds"), Is.EqualTo(0.5f));

        var medianMethod = typeof(FocusShiftController).GetMethod("Median", PrivateStatic);
        Assert.That(medianMethod, Is.Not.Null);
        var median = (float)medianMethod.Invoke(null, new object[]
        {
          new List<float> { 0.419f, 0.421f, 0.420f, 1.5f, 0.418f },
        });
        Assert.That(median, Is.EqualTo(0.420f).Within(0.0001f));
        Assert.That(controller.SessionBaselineFaceScale, Is.EqualTo(0.42f),
          "Local sampling must never overwrite the fixed session baseline.");
      }
      finally
      {
        Object.DestroyImmediate(root);
      }
    }

    [Test]
    public void TrackingPausePreservesCurrentStepAndRewardMask()
    {
      var root = new GameObject("Focus Shift Tracking Pause Test");
      try
      {
        var controller = root.AddComponent<FocusShiftController>();
        Assert.That(controller.StartFocusShift(1f), Is.True);
        SetField(controller, "_localFocusBaseline", 1f);
        SetField(controller, "_stepIndex", 0);
        SetField(controller, "_rewardedSegmentMask", 0b1011UL);
        SetField(controller, "_highestProgressReached", 0.67f);

        Invoke(controller, "PauseForTracking", 10f);
        Assert.That(controller.State, Is.EqualTo(FocusShiftState.PausedTracking));
        Assert.That(controller.CurrentStepIndex, Is.Zero);
        Assert.That(controller.RewardedSegmentMask, Is.EqualTo(0b1011UL));
        Assert.That(controller.HighestProgressReached, Is.EqualTo(0.67f));

        Invoke(controller, "ResumeTracking", 10.5f, 1f);
        Assert.That(controller.CurrentStepIndex, Is.Zero);
        Assert.That(controller.RewardedSegmentMask, Is.EqualTo(0b1011UL));
        Assert.That(controller.HighestProgressReached, Is.EqualTo(0.67f));
      }
      finally
      {
        Object.DestroyImmediate(root);
      }
    }

    [Test]
    public void DistanceRatioIsLinearSoTheNearBandNeedsRealMovement()
    {
      // RobustFaceScale is a squared span, so a raw scale ratio moves twice as fast as the
      // player does. Quadrupling the face scale is only half the distance.
      Assert.That(FaceDistanceRatio.FromFaceScale(4f, 1f), Is.EqualTo(2f).Within(0.0001f));
      Assert.That(FaceDistanceRatio.FromFaceScale(1f, 1f), Is.EqualTo(1f).Within(0.0001f));
      Assert.That(FaceDistanceRatio.DistanceMultiple(2f), Is.EqualTo(0.5f).Within(0.0001f));
      Assert.That(FaceDistanceRatio.ToFaceScale(0.1f, 1.25f), Is.EqualTo(0.15625f).Within(0.000001f));
      Assert.That(FaceDistanceRatio.FromFaceScale(-1f, 1f), Is.Zero, "Invalid input must not pass a scale gate.");
      Assert.That(FaceDistanceRatio.FromFaceScale(1f, 0f), Is.Zero);

      // 1.10 was the old nearMin. Read as a raw face scale it is barely a 5% lean, which must
      // no longer come anywhere near completing the Near step.
      var oldNearThreshold = FaceDistanceRatio.FromFaceScale(1.10f, 1f);
      Assert.That(oldNearThreshold, Is.LessThan(1.25f));
      Assert.That(FaceDistanceRatio.DistanceMultiple(oldNearThreshold), Is.GreaterThan(0.95f));
      // The shipping Near band is a real arm movement: 20% to 30% of the baseline distance.
      Assert.That(FaceDistanceRatio.DistanceMultiple(1.25f), Is.EqualTo(0.80f).Within(0.005f));
      Assert.That(FaceDistanceRatio.DistanceMultiple(1.43f), Is.EqualTo(0.70f).Within(0.005f));
      Assert.That(FaceDistanceRatio.DistanceMultiple(0.80f), Is.EqualTo(1.25f).Within(0.005f));
      Assert.That(FaceDistanceRatio.DistanceMultiple(0.69f), Is.EqualTo(1.45f).Within(0.005f));
    }

    [Test]
    public void DistanceReferenceHasClearMonotonicFarNeutralNearAndTooCloseSizes()
    {
      const float farScale = 0.42f;
      const float nearScale = 1.72f;
      const float capScale = 1.95f;
      float Scale(float ratio) => FocusShiftView.EvaluateDistanceVisualScale(
        ratio, farScale, nearScale, capScale, 0.69f, 1.34f, 1.60f);

      Assert.That(Scale(0.69f), Is.EqualTo(farScale).Within(0.001f));
      Assert.That(Scale(1f), Is.EqualTo(1f).Within(0.001f));
      Assert.That(Scale(1.34f), Is.EqualTo(nearScale).Within(0.001f));
      Assert.That(Scale(1.60f), Is.EqualTo(capScale).Within(0.001f));
      Assert.That(Scale(2f), Is.EqualTo(capScale).Within(0.001f),
        "Too Close visual feedback must remain capped.");
      // The whole Far -> Near travel must still read as continuous growth.
      Assert.That(Scale(0.80f), Is.LessThan(Scale(0.95f)));
      Assert.That(Scale(0.95f), Is.LessThan(Scale(1f)));
      Assert.That(Scale(1f), Is.LessThan(Scale(1.25f)));
      Assert.That(Scale(1.25f), Is.LessThan(Scale(1.34f)));
      Assert.That(Scale(1.34f), Is.LessThan(Scale(1.43f)));
      Assert.That(Scale(1.43f), Is.LessThan(Scale(1.60f)));
    }

    [Test]
    public void GuidanceCorrectsFarAndNearOvershootInsteadOfRepeatingWrongPrompt()
    {
      // Shipping bands, as linear ratios: neutral 0.95-1.05, near 1.25-1.43, far 0.69-0.80.
      FocusShiftGuidance Guidance(CareMovementDirection direction, float ratio) =>
        FocusShiftController.ResolveGuidance(direction, ratio, 0.95f, 1.05f, 1.25f, 1.43f, 0.69f, 0.80f, 1.60f);

      Assert.That(Guidance(CareMovementDirection.Far, 0.92f), Is.EqualTo(FocusShiftGuidance.MoveAway));
      Assert.That(Guidance(CareMovementDirection.Far, 0.75f), Is.EqualTo(FocusShiftGuidance.HoldSteady));
      Assert.That(Guidance(CareMovementDirection.Far, 0.64f), Is.EqualTo(FocusShiftGuidance.MoveCloser));
      Assert.That(Guidance(CareMovementDirection.Near, 1.06f), Is.EqualTo(FocusShiftGuidance.MoveCloser));
      // A ratio that used to complete the Near step is now barely a fifth of the way there.
      Assert.That(Guidance(CareMovementDirection.Near, 1.12f), Is.EqualTo(FocusShiftGuidance.MoveCloser));
      Assert.That(Guidance(CareMovementDirection.Near, 1.34f), Is.EqualTo(FocusShiftGuidance.HoldSteady));
      Assert.That(Guidance(CareMovementDirection.Near, 1.50f), Is.EqualTo(FocusShiftGuidance.MoveAway));
      Assert.That(Guidance(CareMovementDirection.Near, 1.65f), Is.EqualTo(FocusShiftGuidance.MoveAway));
    }

    private static object GetField(object target, string name)
    {
      var field = target.GetType().GetField(name, PrivateInstance);
      Assert.That(field, Is.Not.Null, "Missing field: " + name);
      return field.GetValue(target);
    }

    private static float GetFloat(object target, string name)
    {
      return (float)GetField(target, name);
    }

    private static void SetField(object target, string name, object value)
    {
      var field = target.GetType().GetField(name, PrivateInstance);
      Assert.That(field, Is.Not.Null, "Missing field: " + name);
      field.SetValue(target, value);
    }

    private static void Invoke(object target, string methodName, params object[] arguments)
    {
      var method = target.GetType().GetMethod(methodName, PrivateInstance);
      Assert.That(method, Is.Not.Null, "Missing method: " + methodName);
      method.Invoke(target, arguments);
    }
  }
}
