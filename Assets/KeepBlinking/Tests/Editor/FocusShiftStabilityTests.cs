using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using KeepBlinking.Gameplay;
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
    public void DistanceReferenceHasClearMonotonicFarNeutralNearAndTooCloseSizes()
    {
      const float farScale = 0.42f;
      const float nearScale = 1.72f;
      const float capScale = 1.95f;
      float Scale(float ratio) => FocusShiftView.EvaluateDistanceVisualScale(
        ratio, farScale, nearScale, capScale, 0.72f, 1.18f, 1.30f);

      Assert.That(Scale(0.72f), Is.EqualTo(farScale).Within(0.001f));
      Assert.That(Scale(1f), Is.EqualTo(1f).Within(0.001f));
      Assert.That(Scale(1.18f), Is.EqualTo(nearScale).Within(0.001f));
      Assert.That(Scale(1.30f), Is.EqualTo(capScale).Within(0.001f));
      Assert.That(Scale(2f), Is.EqualTo(capScale).Within(0.001f),
        "Too Close visual feedback must remain capped.");
      Assert.That(Scale(0.88f), Is.LessThan(Scale(0.95f)));
      Assert.That(Scale(0.95f), Is.LessThan(Scale(1f)));
      Assert.That(Scale(1f), Is.LessThan(Scale(1.10f)));
      Assert.That(Scale(1.10f), Is.LessThan(Scale(1.18f)));
      Assert.That(Scale(1.18f), Is.LessThan(Scale(1.30f)));
    }

    [Test]
    public void GuidanceCorrectsFarAndNearOvershootInsteadOfRepeatingWrongPrompt()
    {
      FocusShiftGuidance Guidance(CareMovementDirection direction, float ratio) =>
        FocusShiftController.ResolveGuidance(direction, ratio, 0.95f, 1.05f, 1.10f, 1.14f, 0.84f, 0.88f, 1.18f);

      Assert.That(Guidance(CareMovementDirection.Far, 0.92f), Is.EqualTo(FocusShiftGuidance.MoveAway));
      Assert.That(Guidance(CareMovementDirection.Far, 0.86f), Is.EqualTo(FocusShiftGuidance.HoldSteady));
      Assert.That(Guidance(CareMovementDirection.Far, 0.80f), Is.EqualTo(FocusShiftGuidance.MoveCloser));
      Assert.That(Guidance(CareMovementDirection.Near, 1.06f), Is.EqualTo(FocusShiftGuidance.MoveCloser));
      Assert.That(Guidance(CareMovementDirection.Near, 1.12f), Is.EqualTo(FocusShiftGuidance.HoldSteady));
      Assert.That(Guidance(CareMovementDirection.Near, 1.16f), Is.EqualTo(FocusShiftGuidance.MoveAway));
      Assert.That(Guidance(CareMovementDirection.Near, 1.20f), Is.EqualTo(FocusShiftGuidance.MoveAway));
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
