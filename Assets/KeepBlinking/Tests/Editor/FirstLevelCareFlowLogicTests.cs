using System.Reflection;
using KeepBlinking.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace KeepBlinking.Tests
{
  public sealed class FirstLevelCareFlowLogicTests
  {
    private static readonly BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

    [Test]
    public void DirectionalProgressUsesConfiguredPhoneRelativeAxes()
    {
      Assert.That(DirectionalPhoneMovementLogic.DirectionProgress(
        CareMovementDirection.Left, new Vector2(0.08f, 0f), 0.08f, 0.08f, 1f, 1f), Is.EqualTo(1f));
      Assert.That(DirectionalPhoneMovementLogic.DirectionProgress(
        CareMovementDirection.Right, new Vector2(-0.08f, 0f), 0.08f, 0.08f, 1f, 1f), Is.EqualTo(1f));
      Assert.That(DirectionalPhoneMovementLogic.DirectionProgress(
        CareMovementDirection.Up, new Vector2(0f, 0.08f), 0.08f, 0.08f, 1f, 1f), Is.EqualTo(1f));
      Assert.That(DirectionalPhoneMovementLogic.DirectionProgress(
        CareMovementDirection.Down, new Vector2(0f, -0.08f), 0.08f, 0.08f, 1f, 1f), Is.EqualTo(1f));
    }

    [Test]
    public void HorizontalSweepCrossesCenterWithoutAddingAnIntermediateCenterGate()
    {
      const float leftStart = 0.08f;
      const float rightEnd = 0.08f;
      var signedFacePositions = new[] { 0.08f, 0.04f, 0f, -0.04f, -0.08f };
      var expectedProgress = new[] { 0f, 0.25f, 0.5f, 0.75f, 1f };

      for (var i = 0; i < signedFacePositions.Length; i++)
      {
        Assert.That(
          DirectionalPhoneMovementLogic.SweepProgress(signedFacePositions[i], leftStart, rightEnd),
          Is.EqualTo(expectedProgress[i]).Within(0.0001f));
      }

      Assert.That(System.Enum.IsDefined(typeof(DirectionalPhoneMovementState), "Sweep"), Is.True);
      Assert.That(System.Enum.IsDefined(typeof(DirectionalPhoneMovementState), "HoldEnd"), Is.True);
      Assert.That(System.Enum.IsDefined(typeof(DirectionalPhoneMovementState), "ReturnCenter"), Is.True);
    }

    [Test]
    public void VerticalSweepUsesTheSameContinuousBottomToTopProgress()
    {
      const float downStart = 0.10f;
      const float upEnd = 0.06f;

      Assert.That(DirectionalPhoneMovementLogic.SweepProgress(0.10f, downStart, upEnd), Is.EqualTo(0f));
      Assert.That(DirectionalPhoneMovementLogic.SweepProgress(0f, downStart, upEnd), Is.EqualTo(0.625f).Within(0.0001f));
      Assert.That(DirectionalPhoneMovementLogic.SweepProgress(-0.06f, downStart, upEnd), Is.EqualTo(1f));
    }

    [Test]
    public void CenterZoneUsesAnEllipseRatherThanACombinedRectangle()
    {
      const float horizontalRadius = 0.045f;
      const float verticalRadius = 0.055f;

      Assert.That(DirectionalPhoneMovementLogic.IsInsideCenterEllipse(
        Vector2.zero, horizontalRadius, verticalRadius), Is.True);
      Assert.That(DirectionalPhoneMovementLogic.IsInsideCenterEllipse(
        new Vector2(horizontalRadius, 0f), horizontalRadius, verticalRadius), Is.True);
      Assert.That(DirectionalPhoneMovementLogic.IsInsideCenterEllipse(
        new Vector2(0f, verticalRadius), horizontalRadius, verticalRadius), Is.True);
      Assert.That(DirectionalPhoneMovementLogic.IsInsideCenterEllipse(
        new Vector2(horizontalRadius * 0.8f, verticalRadius * 0.8f), horizontalRadius, verticalRadius), Is.False);
    }

    [Test]
    public void DirectionSignIsLearnedOnlyFromAVisiblePrimaryAxisMovement()
    {
      Assert.That(DirectionalPhoneMovementLogic.TryResolveDirectionSign(
        0.05f, 0.01f, 0.035f, 0.09f, out var positiveSign), Is.True);
      Assert.That(positiveSign, Is.EqualTo(1f));

      Assert.That(DirectionalPhoneMovementLogic.TryResolveDirectionSign(
        -0.05f, 0.01f, 0.035f, 0.09f, out var negativeSign), Is.True);
      Assert.That(negativeSign, Is.EqualTo(-1f));

      Assert.That(DirectionalPhoneMovementLogic.TryResolveDirectionSign(
        0.02f, 0.01f, 0.035f, 0.09f, out _), Is.False, "Noise must not define the camera sign.");
      Assert.That(DirectionalPhoneMovementLogic.TryResolveDirectionSign(
        0.05f, 0.10f, 0.035f, 0.09f, out _), Is.False, "Cross-axis motion must not define the camera sign.");
    }

    [Test]
    public void DirectionalDefaultsCaptureAPerActionCenterAndUseHandheldFriendlyBounds()
    {
      var root = new GameObject("Directional Input Defaults Test");
      try
      {
        var controller = root.AddComponent<DirectionalPhoneMovementController>();

        Assert.That(GetFloat(controller, "_actionBaselineCaptureSeconds"), Is.EqualTo(0.8f));
        Assert.That(GetInt(controller, "_minimumActionBaselineSamples"), Is.GreaterThanOrEqualTo(15));
        Assert.That(GetFloat(controller, "_maximumActionBaselineSpread"), Is.EqualTo(0.035f));
        Assert.That(GetFloat(controller, "_actionNeutralDistanceMin"), Is.EqualTo(0.90f));
        Assert.That(GetFloat(controller, "_actionNeutralDistanceMax"), Is.EqualTo(1.10f));
        Assert.That(GetFloat(controller, "_inputFreshnessSeconds"), Is.GreaterThanOrEqualTo(0.75f));
        Assert.That(GetFloat(controller, "_centerHorizontalRadius"), Is.EqualTo(0.045f));
        Assert.That(GetFloat(controller, "_centerVerticalRadius"), Is.EqualTo(0.055f));
        Assert.That(GetFloat(controller, "_returnCenterHoldSeconds"), Is.InRange(0.3f, 0.4f));
        Assert.That(GetFloat(controller, "_minimumScaleRatio"), Is.EqualTo(0.86f));
        Assert.That(GetFloat(controller, "_maximumScaleRatio"), Is.EqualTo(1.14f));
        Assert.That(GetInt(controller, "_sweepRewardNodes"), Is.EqualTo(14));
      }
      finally
      {
        Object.DestroyImmediate(root);
      }
    }

    [Test]
    public void OneEuroFilterRespondsFasterToMotionAndCanDiscardTrackingHistory()
    {
      var stationaryFilter = new CareOneEuroFilter();
      stationaryFilter.Reset(0f);
      var stationaryResponse = stationaryFilter.Filter(1f, 1f / 30f, 1.25f, 0f, 1f);

      var adaptiveFilter = new CareOneEuroFilter();
      adaptiveFilter.Reset(0f);
      var adaptiveResponse = adaptiveFilter.Filter(1f, 1f / 30f, 1.25f, 0.18f, 1f);

      Assert.That(adaptiveResponse, Is.GreaterThan(stationaryResponse));
      adaptiveFilter.Reset(0.4f);
      Assert.That(adaptiveFilter.Value, Is.EqualTo(0.4f));
      Assert.That(adaptiveFilter.Filter(0.4f, 1f / 30f, 1.25f, 0.18f, 1f), Is.EqualTo(0.4f));
    }

    [Test]
    public void DirectionalMovementRejectsDepthChangeOutsideScaleWindow()
    {
      Assert.That(DirectionalPhoneMovementLogic.ScaleIsValid(0.88f, 1f, 0.88f, 1.12f), Is.True);
      Assert.That(DirectionalPhoneMovementLogic.ScaleIsValid(1.12f, 1f, 0.88f, 1.12f), Is.True);
      Assert.That(DirectionalPhoneMovementLogic.ScaleIsValid(0.87f, 1f, 0.88f, 1.12f), Is.False);
      Assert.That(DirectionalPhoneMovementLogic.ScaleIsValid(1.13f, 1f, 0.88f, 1.12f), Is.False);
    }

    [Test]
    public void DeliberateSweepAllowsOrdinaryCrossAxisDriftButNotWrongAxisMotion()
    {
      Assert.That(DirectionalPhoneMovementLogic.CrossAxisIsValid(0.09f, 0.10f, 0.09f, 1.2f), Is.True);
      Assert.That(DirectionalPhoneMovementLogic.CrossAxisIsValid(0.01f, 0.11f, 0.09f, 1.2f), Is.False);
    }

    [Test]
    public void SweepEndpointUsesEntryThresholdThenHoldHysteresis()
    {
      Assert.That(DirectionalPhoneMovementLogic.IsSweepEndZone(0.998f, false, 0.999f, 0.92f), Is.False);
      Assert.That(DirectionalPhoneMovementLogic.IsSweepEndZone(1f, false, 0.999f, 0.92f), Is.True);
      Assert.That(DirectionalPhoneMovementLogic.IsSweepEndZone(0.94f, true, 0.999f, 0.92f), Is.True);
      Assert.That(DirectionalPhoneMovementLogic.IsSweepEndZone(0.90f, true, 0.999f, 0.92f), Is.False);
    }

    [Test]
    public void RewardSegmentsOnlyPayForNewHighestProgress()
    {
      Assert.That(CareRewardSegmentLogic.CountNewSegments(0f, 0.34f, 6), Is.EqualTo(2));
      Assert.That(CareRewardSegmentLogic.CountNewSegments(0.34f, 0.34f, 6), Is.Zero);
      Assert.That(CareRewardSegmentLogic.CountNewSegments(0.34f, 0.20f, 6), Is.Zero);
      Assert.That(CareRewardSegmentLogic.CountNewSegments(0.34f, 1f, 6), Is.EqualTo(4));
    }

    [Test]
    public void FourteenNodeSweepPaysEverySkippedNodeOnceAndNeverPaysRegression()
    {
      const int nodes = FirstLevelCareRewardPlan.DirectionSweepFragments;
      Assert.That(nodes, Is.EqualTo(14));

      var firstFrame = CareRewardSegmentLogic.CountNewSegments(0f, 0.22f, nodes);
      var skippedFrames = CareRewardSegmentLogic.CountNewSegments(0.22f, 0.75f, nodes);
      var regression = CareRewardSegmentLogic.CountNewSegments(0.75f, 0.40f, nodes);
      var finish = CareRewardSegmentLogic.CountNewSegments(0.75f, 1f, nodes);

      Assert.That(firstFrame, Is.EqualTo(3));
      Assert.That(skippedFrames, Is.EqualTo(7));
      Assert.That(regression, Is.Zero);
      Assert.That(finish, Is.EqualTo(4));
      Assert.That(firstFrame + skippedFrames + finish, Is.EqualTo(nodes));
    }

    [Test]
    public void DefaultCareRoundsHaveTheExpectedRealFragmentBudgets()
    {
      var horizontal = FirstLevelCareRewardPlan.DirectionalFragments(
        CareMovementDirection.Left,
        CareMovementDirection.Right);
      var vertical = FirstLevelCareRewardPlan.DirectionalFragments(
        CareMovementDirection.Up,
        CareMovementDirection.Down);
      var focusShift = FirstLevelCareRewardPlan.FocusShiftFragments(2);

      Assert.That(horizontal, Is.EqualTo(14));
      Assert.That(vertical, Is.EqualTo(14));
      Assert.That(focusShift, Is.EqualTo(40));
      Assert.That(horizontal + vertical + focusShift, Is.EqualTo(68));
      Assert.That(2 + horizontal + 8, Is.EqualTo(24));
      Assert.That(2 + focusShift + 8, Is.EqualTo(50));
      Assert.That(2 + horizontal + vertical + focusShift + 8, Is.EqualTo(78));
      Assert.That((2 + horizontal + 8) + (2 + vertical + 8) +
                  (2 + focusShift + 8) + (2 + horizontal + vertical + focusShift + 8),
        Is.EqualTo(176));
    }

    [Test]
    public void ScreenRestPaysEachCompletedSecondAtMostOnceAndCapsAtEight()
    {
      Assert.That(ScreenDownRestMotionLogic.StoredGoldFragments(0.99f, 8f), Is.Zero);
      Assert.That(ScreenDownRestMotionLogic.StoredGoldFragments(1.01f, 8f), Is.EqualTo(1));
      Assert.That(ScreenDownRestMotionLogic.StoredGoldFragments(7.99f, 8f), Is.EqualTo(7));
      Assert.That(ScreenDownRestMotionLogic.StoredGoldFragments(8.4f, 8f), Is.EqualTo(8));
      Assert.That(ScreenDownRestMotionLogic.StoredGoldFragments(20f, 8f), Is.EqualTo(8));
    }

    [Test]
    public void FocusShiftHealthInvariantsRemainFixed()
    {
      var root = new GameObject("Focus Shift Invariant Test");
      try
      {
        var controller = root.AddComponent<FocusShiftController>();
        Assert.That(controller.FocusShiftCycles, Is.EqualTo(2));
        Assert.That(GetFloat(controller, "_neutralMin"), Is.EqualTo(0.95f));
        Assert.That(GetFloat(controller, "_neutralMax"), Is.EqualTo(1.05f));
        Assert.That(GetFloat(controller, "_nearMin"), Is.EqualTo(1.10f));
        Assert.That(GetFloat(controller, "_nearMax"), Is.EqualTo(1.14f));
        Assert.That(GetFloat(controller, "_farMin"), Is.EqualTo(0.84f));
        Assert.That(GetFloat(controller, "_farMax"), Is.EqualTo(0.88f));
        Assert.That(GetFloat(controller, "_tooCloseRatio"), Is.EqualTo(1.18f));
        Assert.That(GetFloat(controller, "_minimumTransitionSeconds"), Is.GreaterThanOrEqualTo(1f));
        Assert.That(GetFloat(controller, "_localBaselineCaptureSeconds"), Is.GreaterThanOrEqualTo(0.5f));
        Assert.That(GetInt(controller, "_minimumLocalBaselineSamples"), Is.GreaterThanOrEqualTo(8));
      }
      finally
      {
        Object.DestroyImmediate(root);
      }
    }

    [Test]
    public void FourRoundStateMachineContainsEveryRequiredCollectionGate()
    {
      Assert.That(System.Enum.IsDefined(typeof(FirstLevelCareFlowState), "WaitBaseSamples"), Is.True);
      Assert.That(System.Enum.IsDefined(typeof(FirstLevelCareFlowState), "DirectionalMovement"), Is.True);
      Assert.That(System.Enum.IsDefined(typeof(FirstLevelCareFlowState), "FocusShift"), Is.True);
      Assert.That(System.Enum.IsDefined(typeof(FirstLevelCareFlowState), "GuidedEyeMovement"), Is.True);
      Assert.That(System.Enum.IsDefined(typeof(FirstLevelCareFlowState), "ScreenDownRest"), Is.True);
      Assert.That(System.Enum.IsDefined(typeof(FirstLevelCareFlowState), "RecoverTracking"), Is.True);
      Assert.That(System.Enum.IsDefined(typeof(FirstLevelCareFlowState), "WaitReturnNeutral"), Is.True);
      Assert.That(System.Enum.IsDefined(typeof(FirstLevelCareFlowState), "ArmPushAway"), Is.True);
      Assert.That(System.Enum.IsDefined(typeof(FirstLevelCareFlowState), "WaitExperienceCollected"), Is.True);
      Assert.That(System.Enum.IsDefined(typeof(FirstLevelCareFlowState), "OpenUpgrade"), Is.True);
    }

    [Test]
    public void FormalFirstLevelDefaultsToCareFlowWithoutLegacyGazeGate()
    {
      var sessionRoot = new GameObject("Care Session Default Test");
      var fieldRoot = new GameObject("Care Field Default Test");
      try
      {
        var session = sessionRoot.AddComponent<FirstLevelSessionController>();
        var field = fieldRoot.AddComponent<SoftFocusFieldController>();

        Assert.That(GetBool(session, "_runLegacyGazeTutorial"), Is.False);
        Assert.That(GetBool(field, "_useFaceCenterDuringCareRounds"), Is.True);
      }
      finally
      {
        Object.DestroyImmediate(fieldRoot);
        Object.DestroyImmediate(sessionRoot);
      }
    }

    [Test]
    public void FormalPushAwayIsBlockedDuringCareActionsAndBeforeArming()
    {
      var root = new GameObject("Care Collection Gate Test");
      try
      {
        var gameplay = root.AddComponent<EdgeOrbitHarvestMvp>();
        var canUpdate = typeof(EdgeOrbitHarvestMvp).GetMethod("CanUpdateDistanceState", InstancePrivate);
        Assert.That(canUpdate, Is.Not.Null);

        gameplay.SetCareRoundFlowEnabled(true);
        gameplay.SetCareActionActive(true);
        Assert.That((bool)canUpdate.Invoke(gameplay, new object[] { true }), Is.False);

        gameplay.SetCareActionActive(false);
        gameplay.SetCareCollectionArmed(false);
        Assert.That((bool)canUpdate.Invoke(gameplay, new object[] { true }), Is.False);

        gameplay.SetCareCollectionArmed(true);
        Assert.That((bool)canUpdate.Invoke(gameplay, new object[] { true }), Is.True);

        gameplay.SetFocusShiftActive(true);
        Assert.That((bool)canUpdate.Invoke(gameplay, new object[] { true }), Is.False);
      }
      finally
      {
        Object.DestroyImmediate(root);
      }
    }

    [Test]
    public void CareExperiencePoolsAreSizedAboveTheLargestSeventyEightFragmentRound()
    {
      var gameplayRoot = new GameObject("Care Pool Capacity Test");
      var emitterRoot = new GameObject("Care Feedback Pool Test");
      try
      {
        var gameplay = gameplayRoot.AddComponent<EdgeOrbitHarvestMvp>();
        var emitter = emitterRoot.AddComponent<CareExperienceRewardEmitter>();
        Assert.That(GetInt(gameplay, "_experienceSamplePoolCapacity"), Is.GreaterThanOrEqualTo(90));
        Assert.That(GetFloat(gameplay, "_careSampleReleaseInterval"), Is.InRange(0.03f, 0.06f));
        Assert.That(GetInt(emitter, "_floatingTextPoolCapacity"), Is.InRange(3, 4));
      }
      finally
      {
        Object.DestroyImmediate(emitterRoot);
        Object.DestroyImmediate(gameplayRoot);
      }
    }

    [Test]
    public void DryCoreKeepsItsExistingFormalPushAwayInputAvailable()
    {
      var root = new GameObject("Dry Core Distance Gate Test");
      try
      {
        var gameplay = root.AddComponent<EdgeOrbitHarvestMvp>();
        SetField(gameplay, "_autoReadKeepBlinkingEyeInput", false);
        gameplay.BeginFirstLevelBossMode();
        var canUpdate = typeof(EdgeOrbitHarvestMvp).GetMethod("CanUpdateDistanceState", InstancePrivate);
        Assert.That(canUpdate, Is.Not.Null);
        Assert.That((bool)canUpdate.Invoke(gameplay, new object[] { true }), Is.True);
      }
      finally
      {
        Object.DestroyImmediate(root);
      }
    }

    private static float GetFloat(object target, string fieldName)
    {
      var field = target.GetType().GetField(fieldName, InstancePrivate);
      Assert.That(field, Is.Not.Null, "Missing invariant field: " + fieldName);
      return (float)field.GetValue(target);
    }

    private static int GetInt(object target, string fieldName)
    {
      var field = target.GetType().GetField(fieldName, InstancePrivate);
      Assert.That(field, Is.Not.Null, "Missing pool field: " + fieldName);
      return (int)field.GetValue(target);
    }

    private static bool GetBool(object target, string fieldName)
    {
      var field = target.GetType().GetField(fieldName, InstancePrivate);
      Assert.That(field, Is.Not.Null, "Missing invariant field: " + fieldName);
      return (bool)field.GetValue(target);
    }

    private static void SetField(object target, string fieldName, object value)
    {
      var field = target.GetType().GetField(fieldName, InstancePrivate);
      Assert.That(field, Is.Not.Null, "Missing test field: " + fieldName);
      field.SetValue(target, value);
    }
  }
}
