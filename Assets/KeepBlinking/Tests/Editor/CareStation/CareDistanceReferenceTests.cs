using KeepBlinking.CareStation;
using KeepBlinking.Input;
using NUnit.Framework;

namespace KeepBlinking.Tests
{
  public sealed class CareDistanceReferenceTests
  {
    /// <summary>
    /// Face scales are area-like, so the scale matching a linear ratio r is r squared. Tests
    /// that drive the step with scales must go through here, or they silently assert against
    /// half the movement they read as.
    /// </summary>
    private static float Scale(float linearRatio)
    {
      return FaceDistanceRatio.ToFaceScale(1f, linearRatio);
    }

    [Test]
    public void ReferenceUsesShortMedianWindowAndLocksUntilStepCompletes()
    {
      var sampler = new CareDistanceReferenceSampler(0.3f, 4);
      var samples = new[] { 0.10f, 0.102f, 0.25f, 0.099f, 0.101f };
      for (var index = 0; index < samples.Length; index++)
        sampler.AddFreshSample(index + 1, samples[index], index * 0.1f, true);

      Assert.That(sampler.IsComplete, Is.True);
      var reference = sampler.ReferenceScale;
      Assert.That(reference, Is.EqualTo(0.101f).Within(0.001f));
      sampler.AddFreshSample(99, 0.04f, 2f, true);
      Assert.That(sampler.ReferenceScale, Is.EqualTo(reference));
    }

    [Test]
    public void TrackingLossRestartsOnlyIncompleteReferenceCapture()
    {
      var sampler = new CareDistanceReferenceSampler(0.25f, 3);
      sampler.AddFreshSample(1, 0.1f, 0f, true);
      sampler.AddFreshSample(2, 0.1f, 0.1f, true);
      sampler.AddFreshSample(3, 0f, 0.2f, false);
      Assert.That(sampler.SampleCount, Is.Zero);
      sampler.AddFreshSample(4, 0.2f, 1f, true);
      sampler.AddFreshSample(5, 0.2f, 1.13f, true);
      sampler.AddFreshSample(6, 0.2f, 1.26f, true);
      Assert.That(sampler.ReferenceScale, Is.EqualTo(0.2f).Within(0.0001f));
      sampler.AddFreshSample(7, 0f, 2f, false);
      Assert.That(sampler.ReferenceScale, Is.EqualTo(0.2f).Within(0.0001f));
    }

    [Test]
    public void CloserShowsProgressAboveTwoPercentAndCompletesAtSixPercentHold()
    {
      var step = new CareRelativeDistanceStep(CareDistanceDirection.Closer, 0.02f, 0.06f, 0.25f);
      Assert.That(step.Advance(Scale(1.019f), 1f, 0.1f, true, true), Is.False);
      Assert.That(step.Progress, Is.Zero);
      Assert.That(step.Advance(Scale(1.03f), 1f, 0.1f, true, true), Is.False);
      Assert.That(step.Progress, Is.EqualTo(0.25f).Within(0.01f));
      Assert.That(step.Advance(Scale(1.061f), 1f, 0.13f, true, true), Is.False);
      Assert.That(step.Progress, Is.EqualTo(1f));
      Assert.That(step.Advance(Scale(1.061f), 1f, 0.12f, true, true), Is.True);
    }

    [Test]
    public void AwayShowsProgressAboveTwoPercentAndCompletesAtSixPercentHold()
    {
      var step = new CareRelativeDistanceStep(CareDistanceDirection.Away, 0.02f, 0.06f, 0.25f);
      Assert.That(step.Advance(Scale(0.97f), 1f, 0.1f, true, true), Is.False);
      Assert.That(step.Progress, Is.EqualTo(0.25f).Within(0.01f));
      Assert.That(step.Advance(Scale(0.939f), 1f, 0.13f, true, true), Is.False);
      Assert.That(step.Advance(Scale(0.939f), 1f, 0.12f, true, true), Is.True);
    }

    [Test]
    public void WrongDirectionFallsGraduallyWithoutPenaltyOrCompletion()
    {
      var step = new CareRelativeDistanceStep(CareDistanceDirection.Closer, 0.02f, 0.06f, 0.25f, 0.4f);
      step.Advance(Scale(1.05f), 1f, 0.1f, true, true);
      var prior = step.Progress;
      Assert.That(prior, Is.GreaterThan(0f));
      Assert.That(step.Advance(Scale(0.97f), 1f, 0.1f, true, true), Is.False);
      Assert.That(step.Progress, Is.GreaterThan(0f));
      Assert.That(step.Progress, Is.LessThan(prior));
    }

    [Test]
    public void TrackingLossAndRepeatedRenderFramesFreezeProgressAndReference()
    {
      const float reference = 0.14f;
      var step = new CareRelativeDistanceStep(CareDistanceDirection.Away);
      step.Advance(reference * Scale(0.88f), reference, 0.1f, true, true);
      var progress = step.Progress;
      Assert.That(progress, Is.GreaterThan(0f));
      step.FreezeForTrackingLoss();
      step.Advance(reference * Scale(0.5f), reference, 1f, false, true);
      step.Advance(reference * Scale(0.5f), reference, 1f, true, false);
      Assert.That(step.Progress, Is.EqualTo(progress).Within(0.0001f));
      Assert.That(reference, Is.EqualTo(0.14f));
    }

    [Test]
    public void CurrentScaleImmediatelyChangesDirectionalProgress()
    {
      var step = new CareRelativeDistanceStep(CareDistanceDirection.Away);
      step.Advance(Scale(0.99f), 1f, 0.05f, true, true);
      Assert.That(step.Progress, Is.Zero);
      // Half of the way from the 5% dead zone to the 22% completion threshold.
      step.Advance(Scale(0.865f), 1f, 0.05f, true, true);
      Assert.That(step.DirectionDelta, Is.EqualTo(0.135f).Within(0.0001f));
      Assert.That(step.Progress, Is.EqualTo(0.5f).Within(0.01f));
    }

    [Test]
    public void ConstantScaleIsUnavailableButSmallRealChangesAreObservable()
    {
      Assert.That(CareDistanceReferenceSampler.HasMeaningfulScaleUpdates(0.1f, 0.1f, 0.1f), Is.False);
      Assert.That(CareDistanceReferenceSampler.HasMeaningfulScaleUpdates(0.1f, 0.1002f, 0.1f), Is.True);
      Assert.That(CareDistanceReferenceSampler.HasMeaningfulScaleUpdates(float.NaN, 0.1f, 0.1f), Is.False);
    }

    [Test]
    public void FocusUsesOneSessionBaselineForSixCyclesAndFinalNeutral()
    {
      var configuration = FocusTestConfiguration();
      var action = new CareActionRuntime();
      action.Begin(CareActionType.FocusShift, configuration);
      const float sessionBaseline = 0.11f;
      action.Data.gestureReferenceScale = sessionBaseline;
      action.Data.gestureReferenceValid = true;
      EnterInitialLeg(action);
      for (var step = 0; step < 12; step++)
      {
        Assert.That(action.ExpectedDistanceDirection,
          Is.EqualTo((step & 1) == 0 ? CareDistanceDirection.Closer : CareDistanceDirection.Away));
        CompleteLeg(action, (step & 1) == 0 ? 1.25f : 0.78f);
        Assert.That(action.Data.gestureReferenceScale, Is.EqualTo(sessionBaseline),
          "Focus Shift must never recapture its Session baseline.");
        if (step < 11) EnterNextLeg(action);
      }
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.FocusNeutralFinish));
      for (var index = 0; index < 3; index++)
        action.Advance(0.25f, FreshFrame(1f, 0.25f));
      Assert.That(action.Stage, Is.EqualTo(CareActionStage.Completed));
      Assert.That(action.Data.focusCycleCount, Is.EqualTo(6));
      Assert.That(typeof(CareActionRuntime).GetEvent("PushAwayTriggered"), Is.Null);
    }

    [Test]
    public void SeparatePushPhasesAndCloserResetUseIndependentStepOrigins()
    {
      const float firstOrigin = 0.12f;
      var farOrigin = firstOrigin * Scale(0.779f);
      var firstAway = new CareRelativeDistanceStep(CareDistanceDirection.Away, holdSeconds: 0.1f);
      Assert.That(firstAway.Advance(farOrigin, firstOrigin, 0.1f, true, true), Is.True);

      var closer = new CareRelativeDistanceStep(CareDistanceDirection.Closer, holdSeconds: 0.1f);
      Assert.That(closer.Advance(firstOrigin, farOrigin, 0.1f, true, true), Is.True);

      const float secondOrigin = 0.09f;
      var secondAway = new CareRelativeDistanceStep(CareDistanceDirection.Away, holdSeconds: 0.1f);
      Assert.That(secondAway.Advance(farOrigin, secondOrigin, 1f, true, true), Is.False,
        "The first collection movement cannot complete the second collection origin.");
      Assert.That(secondAway.Advance(secondOrigin * Scale(0.779f), secondOrigin, 0.1f, true, true), Is.True);
    }

    [Test]
    public void FocusFallbackCompletesOnlyTheCurrentStep()
    {
      var action = new CareActionRuntime();
      action.Begin(CareActionType.FocusShift, CareActionConfiguration.Default);
      Assert.That(action.CompleteFocusStepForFallback(CareDistanceFallbackReason.SensorUnavailable), Is.True);
      Assert.That(action.Data.focusTargetStep, Is.EqualTo(1));
      Assert.That(action.Stage, Is.Not.EqualTo(CareActionStage.Completed));
      Assert.That(action.Data.distanceFallbackReason, Is.EqualTo(CareDistanceFallbackReason.SensorUnavailable));
    }

    [Test]
    public void FocusThresholdsRejectSmallMovementAndRequireStableTargets()
    {
      var configuration = FocusTestConfiguration();
      var action = new CareActionRuntime();
      action.Begin(CareActionType.FocusShift, configuration);
      action.Data.gestureReferenceScale = 0.1f;
      action.Data.gestureReferenceValid = true;
      EnterInitialLeg(action);
      for (var index = 0; index < 16; index++)
        action.Advance(0.25f, FreshFrame(1.06f, 0.25f));
      Assert.That(action.Data.focusTargetStep, Is.Zero, "2%-6% movement is not a valid target.");

      for (var index = 0; index < 2; index++)
        action.Advance(0.25f, FreshFrame(1.25f, 0.25f));
      Assert.That(action.Data.focusTargetStep, Is.Zero, "The target must remain stable for 0.7 seconds.");
      action.Advance(0.25f, FreshFrame(1.25f, 0.25f));
      Assert.That(action.Data.focusTargetStep, Is.EqualTo(1));
    }

    private static CareActionConfiguration FocusTestConfiguration()
    {
      var configuration = CareActionConfiguration.Default;
      configuration.focusTargetHoldSeconds = 0.7f;
      configuration.focusMinimumLegSeconds = 2.5f;
      configuration.focusDirectionIntervalSeconds = 1.2f;
      configuration.focusCycleCount = 6;
      return configuration;
    }

    private static void EnterInitialLeg(CareActionRuntime action)
    {
      action.Advance(0.01f, FreshFrame(1f, 0.01f));
      for (var index = 0; index < 3; index++)
        action.Advance(0.25f, FreshFrame(1f, 0.25f));
    }

    private static void EnterNextLeg(CareActionRuntime action)
    {
      for (var index = 0; index < 6 && action.Phase == CareActionInternalPhase.FocusReference; index++)
        action.Advance(0.25f, FreshFrame(1f, 0.25f));
      action.Advance(0.01f, FreshFrame(1f, 0.01f));
    }

    private static void CompleteLeg(CareActionRuntime action, float ratio)
    {
      for (var index = 0; index < 12 && action.Phase != CareActionInternalPhase.FocusReference &&
                          action.Phase != CareActionInternalPhase.FocusNeutralFinish; index++)
        action.Advance(0.25f, FreshFrame(ratio, 0.25f));
    }

    private static CareActionInputFrame FreshFrame(float ratio, float sampleDelta)
    {
      return new CareActionInputFrame(true, true, false, true, false, false, true, ratio, true, sampleDelta);
    }
  }
}
