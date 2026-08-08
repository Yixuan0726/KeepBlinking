using System;
using System.Reflection;
using KeepBlinking.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace KeepBlinking.Tests
{
  public sealed class GuidedEyeMovementLogicTests
  {
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void StateMachineKeepsPreviewBeforeTheClosePromptAndGuidedAudio()
    {
      var ordered = new[]
      {
        GuidedEyeMovementState.Preparing,
        GuidedEyeMovementState.PreviewClockwise,
        GuidedEyeMovementState.PreviewPause,
        GuidedEyeMovementState.PreviewCounterClockwise,
        GuidedEyeMovementState.PromptClose,
        GuidedEyeMovementState.WaitEyesClosed,
        GuidedEyeMovementState.GuidedClockwise,
        GuidedEyeMovementState.GuidedPause,
        GuidedEyeMovementState.GuidedCounterClockwise,
        GuidedEyeMovementState.CompletionCue,
        GuidedEyeMovementState.WaitReopen,
        GuidedEyeMovementState.ReopenFeedback,
        GuidedEyeMovementState.Completed,
      };

      for (var i = 1; i < ordered.Length; i++)
        Assert.That((int)ordered[i], Is.GreaterThan((int)ordered[i - 1]));
    }

    [Test]
    public void ClosingBeforeThePromptCannotStartGuidance()
    {
      Assert.That(GuidedEyeMovementLogic.CanBeginGuidance(false, false, true, true), Is.False);
      Assert.That(GuidedEyeMovementLogic.CanBeginGuidance(true, false, true, true), Is.False);
      Assert.That(GuidedEyeMovementLogic.CanBeginGuidance(true, true, false, true), Is.False);
      Assert.That(GuidedEyeMovementLogic.CanBeginGuidance(true, true, true, false), Is.False);
      Assert.That(GuidedEyeMovementLogic.CanBeginGuidance(true, true, true, true), Is.True);
    }

    [Test]
    public void GoldRepresentsCompletedClosedEyeSecondsAndCapsAtEight()
    {
      Assert.That(GuidedEyeMovementLogic.StoredGoldFragments(0.99f, 8), Is.Zero);
      Assert.That(GuidedEyeMovementLogic.StoredGoldFragments(1.01f, 8), Is.EqualTo(1));
      Assert.That(GuidedEyeMovementLogic.StoredGoldFragments(7.99f, 8), Is.EqualTo(7));
      Assert.That(GuidedEyeMovementLogic.StoredGoldFragments(8.01f, 8), Is.EqualTo(8));
      Assert.That(GuidedEyeMovementLogic.StoredGoldFragments(30f, 8), Is.EqualTo(8));
    }

    [Test]
    public void DefaultTimingUsesBriefPreviewAndSlowFourSecondClosedEyeDirections()
    {
      var owner = new GameObject("Guided Eye Movement Defaults Test");
      try
      {
        var controller = owner.AddComponent<GuidedEyeMovementController>();
        Assert.That(GetFloat(controller, "_previewClockwiseSeconds"), Is.EqualTo(2.5f));
        Assert.That(GetFloat(controller, "_previewPauseSeconds"), Is.InRange(0.8f, 1f));
        Assert.That(GetFloat(controller, "_previewCounterClockwiseSeconds"), Is.EqualTo(2.5f));
        Assert.That(
          GetFloat(controller, "_previewClockwiseSeconds") +
          GetFloat(controller, "_previewPauseSeconds") +
          GetFloat(controller, "_previewCounterClockwiseSeconds"),
          Is.InRange(5f, 6f));
        Assert.That(GetFloat(controller, "_guidedClockwiseSeconds"), Is.EqualTo(4f));
        Assert.That(GetFloat(controller, "_guidedPauseSeconds"), Is.InRange(0.8f, 1f));
        Assert.That(GetFloat(controller, "_guidedCounterClockwiseSeconds"), Is.EqualTo(4f));
        Assert.That(GetInt(controller, "_maximumGoldFragments"), Is.EqualTo(8));
      }
      finally
      {
        UnityEngine.Object.DestroyImmediate(owner);
      }
    }

    [Test]
    public void GuidedControllerDoesNotContainAGazeOrDirectionInputDependency()
    {
      var fields = typeof(GuidedEyeMovementController).GetFields(PrivateInstance);
      for (var i = 0; i < fields.Length; i++)
      {
        var name = fields[i].Name;
        Assert.That(name.IndexOf("gaze", StringComparison.OrdinalIgnoreCase), Is.LessThan(0));
        Assert.That(name.IndexOf("yaw", StringComparison.OrdinalIgnoreCase), Is.LessThan(0));
        Assert.That(name.IndexOf("pitch", StringComparison.OrdinalIgnoreCase), Is.LessThan(0));
        Assert.That(name.IndexOf("l2cs", StringComparison.OrdinalIgnoreCase), Is.LessThan(0));
      }
    }

    [Test]
    public void FirstLevelFlowContainsTheRoundTwoGuidedGate()
    {
      Assert.That(Enum.IsDefined(typeof(FirstLevelCareFlowState), "GuidedEyeMovement"), Is.True);
      Assert.That(Enum.IsDefined(typeof(GuidedEyeMovementState), "PausedTracking"), Is.True);
      Assert.That(Enum.IsDefined(typeof(GuidedEyeMovementState), "WaitReopen"), Is.True);
      Assert.That(Enum.IsDefined(typeof(GuidedEyeMovementState), "Skipped"), Is.True);
      Assert.That(FirstLevelCareFlowController.RoundUsesGuidedEyeMovement(1), Is.False);
      Assert.That(FirstLevelCareFlowController.RoundUsesGuidedEyeMovement(2), Is.True);
      Assert.That(FirstLevelCareFlowController.RoundUsesGuidedEyeMovement(3), Is.False);
      Assert.That(FirstLevelCareFlowController.RoundUsesGuidedEyeMovement(4), Is.False);
      Assert.That(FirstLevelCareFlowController.RoundUsesScreenDownRest(1), Is.True);
      Assert.That(FirstLevelCareFlowController.RoundUsesScreenDownRest(2), Is.False);
      Assert.That(FirstLevelCareFlowController.RoundUsesScreenDownRest(3), Is.True);
      Assert.That(FirstLevelCareFlowController.RoundUsesScreenDownRest(4), Is.True);
    }

    [Test]
    public void GuidedInteractionBlocksFormalPushAwayUntilTheCareFlowArmsIt()
    {
      var owner = new GameObject("Guided Push Away Isolation Test");
      try
      {
        var gameplay = owner.AddComponent<EdgeOrbitHarvestMvp>();
        var canUpdate = typeof(EdgeOrbitHarvestMvp).GetMethod("CanUpdateDistanceState", PrivateInstance);
        Assert.That(canUpdate, Is.Not.Null);
        gameplay.SetCareRoundFlowEnabled(true);
        gameplay.SetCareActionActive(false);
        gameplay.SetCareCollectionArmed(true);
        gameplay.SetGuidedEyeMovementActive(true);
        Assert.That((bool)canUpdate.Invoke(gameplay, new object[] { true }), Is.False);
      }
      finally
      {
        UnityEngine.Object.DestroyImmediate(owner);
      }
    }

    private static float GetFloat(object target, string fieldName)
    {
      var field = target.GetType().GetField(fieldName, PrivateInstance);
      Assert.That(field, Is.Not.Null, "Missing timing field: " + fieldName);
      return (float)field.GetValue(target);
    }

    private static int GetInt(object target, string fieldName)
    {
      var field = target.GetType().GetField(fieldName, PrivateInstance);
      Assert.That(field, Is.Not.Null, "Missing reward field: " + fieldName);
      return (int)field.GetValue(target);
    }
  }
}
