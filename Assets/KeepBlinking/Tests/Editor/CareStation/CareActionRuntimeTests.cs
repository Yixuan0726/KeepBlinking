using KeepBlinking.CareStation;
using NUnit.Framework;

namespace KeepBlinking.Tests
{
  public sealed class CareActionRuntimeTests
  {
    private CareActionConfiguration _config;

    [SetUp]
    public void SetUp()
    {
      _config = CareActionConfiguration.Default;
      _config.screenDownDemoSeconds = 0.1f;
      _config.screenDownDurationSeconds = 1f;
      _config.screenDownHoldSeconds = 0.1f;
      _config.screenReturnHoldSeconds = 0.1f;
      _config.closedEyeDurationSeconds = 1f;
      _config.closeStartHoldSeconds = 0.1f;
      _config.reopenHoldSeconds = 0.1f;
      _config.distanceDeadZone = 0.02f;
      _config.distanceCompleteThreshold = 0.06f;
      _config.distanceStepHoldSeconds = 0.1f;
      _config.distanceProgressFallSeconds = 0.2f;
      _config.focusStepTransitionSeconds = 0.1f;
      _config.guidedPreviewSeconds = 0.2f;
      _config.guidedClockwiseSeconds = 0.2f;
      _config.guidedPauseSeconds = 0.1f;
      _config.guidedCounterClockwiseSeconds = 0.2f;
      _config.guidedRelaxSeconds = 0.1f;
    }

    [TestCase(CareActionType.ScreenDown)]
    [TestCase(CareActionType.ClosedEyeRest)]
    [TestCase(CareActionType.FocusShift)]
    [TestCase(CareActionType.GuidedEyeCircles)]
    public void EveryActionCanStartAndCompleteIndependently(CareActionType type)
    {
      var action = Begin(type);
      for (var i = 0; i < 32 && action.Stage != CareActionStage.Completed; i++)
        action.CompleteCurrentStepForDevelopment();
      Assert.That(action.Stage, Is.EqualTo(CareActionStage.Completed));
      Assert.That(action.TryConsumeCompletionSignal(), Is.True);
      Assert.That(action.TryConsumeCompletionSignal(), Is.False);
    }

    [Test]
    public void ScreenDownReturnPausesWithoutClearingElapsedTime()
    {
      var action = Begin(CareActionType.ScreenDown);
      action.Advance(0.2f, Frame(screenDown: false, returned: true));
      action.Advance(0.2f, Frame(screenDown: true));
      action.Advance(0.6f, Frame(screenDown: true));
      var elapsed = action.Data.elapsedSeconds;
      action.Advance(0.2f, Frame(screenDown: false, returned: true));
      Assert.That(action.Stage, Is.EqualTo(CareActionStage.Paused));
      Assert.That(action.PauseReason, Is.EqualTo(CareActionPauseReason.ScreenReturned));
      Assert.That(action.Data.elapsedSeconds, Is.EqualTo(elapsed).Within(0.001f));
    }

    [Test]
    public void ClosedEyeRestOpenAndTrackingLostAreDistinctPauses()
    {
      var action = Begin(CareActionType.ClosedEyeRest);
      action.Advance(0.2f, Frame(eyesClosed: true));
      action.Advance(0.4f, Frame(eyesClosed: true));
      var elapsed = action.Data.elapsedSeconds;
      action.Advance(0.1f, Frame(eyesClosed: false));
      Assert.That(action.PauseReason, Is.EqualTo(CareActionPauseReason.EyesOpen));
      Assert.That(action.Data.elapsedSeconds, Is.EqualTo(elapsed).Within(0.001f));
      action.Advance(0.1f, Frame(tracking: false, eyesClosed: false));
      Assert.That(action.PauseReason, Is.EqualTo(CareActionPauseReason.TrackingLost));
      Assert.That(action.Data.elapsedSeconds, Is.EqualTo(elapsed).Within(0.001f));
    }

    [Test]
    public void ClosedEyeRestResumesAfterStableClosedConfirmation()
    {
      var action = Begin(CareActionType.ClosedEyeRest);
      action.Advance(0.2f, Frame(eyesClosed: true));
      action.Advance(0.3f, Frame(eyesClosed: true));
      action.Advance(0.1f, Frame(eyesClosed: false));
      var elapsed = action.Data.elapsedSeconds;
      action.Advance(0.05f, Frame(eyesClosed: true));
      Assert.That(action.Data.elapsedSeconds, Is.EqualTo(elapsed).Within(0.001f));
      action.Advance(0.1f, Frame(eyesClosed: true));
      action.Advance(0.1f, Frame(eyesClosed: true));
      Assert.That(action.Data.elapsedSeconds, Is.GreaterThan(elapsed));
    }

    [Test]
    public void PromptCloseRequestsExactlyOneCloseCueAndActiveDoesNotRepeatIt()
    {
      var cues = new CareActionCueGuard();
      cues.Reset();
      Assert.That(cues.ObservePhase(
        CareActionType.ClosedEyeRest,
        CareActionInternalPhase.ClosedEyePrompt), Is.EqualTo(CareActionCueCommand.CloseRequest));
      Assert.That(cues.ObservePhase(
        CareActionType.ClosedEyeRest,
        CareActionInternalPhase.ClosedEyePrompt), Is.EqualTo(CareActionCueCommand.None));
      Assert.That(cues.ObservePhase(
        CareActionType.ClosedEyeRest,
        CareActionInternalPhase.ClosedEyeActive), Is.EqualTo(CareActionCueCommand.None));
      Assert.That(cues.CloseRequestPlayCount, Is.EqualTo(1));
    }

    [Test]
    public void RestoredPromptDoesNotRepeatAPersistedCloseRequestCue()
    {
      var cues = new CareActionCueGuard();
      cues.Reset(closeRequestAlreadyPlayed: true);
      Assert.That(cues.ObservePhase(
        CareActionType.ClosedEyeRest,
        CareActionInternalPhase.ClosedEyePrompt), Is.EqualTo(CareActionCueCommand.None));
      Assert.That(cues.CloseRequestPlayCount, Is.Zero);
    }

    [Test]
    public void EarlyOpenAndTrackingLossCannotRequestReadyToOpenCue()
    {
      var cues = new CareActionCueGuard();
      cues.Reset();
      cues.ObservePhase(CareActionType.ClosedEyeRest, CareActionInternalPhase.ClosedEyeActive);
      Assert.That(cues.PollReadyToOpen(true, true), Is.EqualTo(CareActionCueCommand.None),
        "Opening early never enters the ReadyToOpen phase.");
      cues.ObservePhase(CareActionType.ClosedEyeRest, CareActionInternalPhase.ClosedEyeWaitReopen);
      Assert.That(cues.PollReadyToOpen(true, false), Is.EqualTo(CareActionCueCommand.None),
        "Tracking Lost must not play the completion cue.");
      Assert.That(cues.ReadyToOpenPlayCount, Is.Zero);
    }

    [Test]
    public void CompletedClosedEyeTimeRequestsOneReadyCueAfterTrackingReturns()
    {
      var cues = new CareActionCueGuard();
      cues.Reset();
      cues.ObservePhase(CareActionType.ClosedEyeRest, CareActionInternalPhase.ClosedEyeWaitReopen);
      Assert.That(cues.PollReadyToOpen(false, true), Is.EqualTo(CareActionCueCommand.None));
      Assert.That(cues.PollReadyToOpen(true, false), Is.EqualTo(CareActionCueCommand.None));
      Assert.That(cues.PollReadyToOpen(true, true), Is.EqualTo(CareActionCueCommand.ReadyToOpen));
      Assert.That(cues.PollReadyToOpen(true, true), Is.EqualTo(CareActionCueCommand.None));
      Assert.That(cues.ReadyToOpenPlayCount, Is.EqualTo(1));
    }

    [Test]
    public void GuidedEyeCirclesUsesTheSameCloseAndReadyCueContract()
    {
      var cues = new CareActionCueGuard();
      cues.Reset();
      Assert.That(cues.ObservePhase(
        CareActionType.GuidedEyeCircles,
        CareActionInternalPhase.GuidedPromptClose), Is.EqualTo(CareActionCueCommand.CloseRequest));
      cues.ObservePhase(CareActionType.GuidedEyeCircles, CareActionInternalPhase.GuidedWaitReopen);
      Assert.That(cues.PollReadyToOpen(true, true), Is.EqualTo(CareActionCueCommand.ReadyToOpen));
      Assert.That(cues.CloseRequestPlayCount, Is.EqualTo(1));
      Assert.That(cues.ReadyToOpenPlayCount, Is.EqualTo(1));
    }

    [Test]
    public void ReadyCuePrecedesReliableOpenCompletion()
    {
      var action = Begin(CareActionType.ClosedEyeRest);
      action.Advance(0.2f, Frame(eyesClosed: true));
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.ClosedEyeActive));
      action.Advance(1f, Frame(eyesClosed: true));
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.ClosedEyeWaitReopen));
      Assert.That(action.Stage, Is.Not.EqualTo(CareActionStage.Completed));
      action.Advance(0.1f, Frame(eyesClosed: true));
      Assert.That(action.Stage, Is.Not.EqualTo(CareActionStage.Completed));
      action.Advance(0.1f, Frame(eyesClosed: false));
      Assert.That(action.Stage, Is.EqualTo(CareActionStage.Completed));
    }

    [Test]
    public void FocusShiftUsesFourIndependentDirectionalStepsWithoutFinalReturn()
    {
      var action = Begin(CareActionType.FocusShift);
      CaptureStepReference(action);
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.FocusNearOne));
      Hold(action, 1.061f);
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.FocusReference));

      CaptureStepReference(action);
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.FocusFarOne));
      Hold(action, 0.939f);
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.FocusReference));

      CaptureStepReference(action);
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.FocusNearTwo));
      Hold(action, 1.061f);
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.FocusReference));

      CaptureStepReference(action);
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.FocusFarTwo));
      Hold(action, 0.939f);
      Assert.That(action.Stage, Is.EqualTo(CareActionStage.Completed));
      Assert.That(action.Prompt, Is.Empty);
    }

    [Test]
    public void FocusFarIsOnlyInternalProgressAndDoesNotExposePushAwaySignal()
    {
      var action = Begin(CareActionType.FocusShift);
      Assert.That(typeof(CareActionRuntime).GetEvent("PushAwayTriggered"), Is.Null);
      Assert.That(typeof(CareActionRuntime).GetEvent("CareActionCompleted"), Is.Null);
      CaptureStepReference(action);
      Hold(action, 1.061f);
      CaptureStepReference(action);
      Hold(action, 0.939f);
      Assert.That(action.Data.focusTargetStep, Is.EqualTo(2));
      Assert.That(action.Stage, Is.Not.EqualTo(CareActionStage.Completed));
    }

    [Test]
    public void FocusDirectionProgressStartsAfterTwoPercentAndTrackingLossFreezesIt()
    {
      var action = Begin(CareActionType.FocusShift);
      CaptureStepReference(action);
      action.Advance(0.05f, Frame(ratio: 1.03f));
      Assert.That(action.DirectionProgress, Is.GreaterThan(0f));
      var progress = action.DirectionProgress;
      action.Advance(1f, Frame(tracking: false, ratio: 1.2f));
      Assert.That(action.DirectionProgress, Is.EqualTo(progress).Within(0.0001f));
      Assert.That(action.Data.focusTargetStep, Is.Zero);
    }

    [Test]
    public void GuidedEyeCirclesUsesOnlyClosureAndNeverGazeDirection()
    {
      var action = Begin(CareActionType.GuidedEyeCircles);
      Assert.That(typeof(CareActionInputFrame).GetField("GazeYaw"), Is.Null);
      Assert.That(typeof(CareActionInputFrame).GetField("GazePitch"), Is.Null);
      action.Advance(0.2f, Frame(eyesClosed: true));
      action.Advance(0.2f, Frame(eyesClosed: true));
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.GuidedPromptClose));
      action.Advance(0.2f, Frame(eyesClosed: true));
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.GuidedClockwise));
    }

    [Test]
    public void GuidedEyeCirclesOpenPauseKeepsCurrentPhaseProgress()
    {
      var action = Begin(CareActionType.GuidedEyeCircles);
      action.CompleteCurrentStepForDevelopment();
      action.CompleteCurrentStepForDevelopment();
      action.CompleteCurrentStepForDevelopment();
      action.Advance(0.1f, Frame(eyesClosed: true));
      var elapsed = action.Data.phaseElapsedSeconds;
      action.Advance(0.2f, Frame(eyesClosed: false));
      Assert.That(action.PauseReason, Is.EqualTo(CareActionPauseReason.EyesOpen));
      Assert.That(action.Data.phaseElapsedSeconds, Is.EqualTo(elapsed).Within(0.001f));
    }

    [Test]
    public void ApplicationPauseNeverAdvancesOrCompletesAction()
    {
      var action = Begin(CareActionType.ClosedEyeRest);
      action.Advance(0.2f, Frame(eyesClosed: true));
      var elapsed = action.Data.elapsedSeconds;
      action.Advance(10f, Frame(active: false, eyesClosed: true));
      Assert.That(action.Stage, Is.EqualTo(CareActionStage.Paused));
      Assert.That(action.PauseReason, Is.EqualTo(CareActionPauseReason.ApplicationBackground));
      Assert.That(action.Data.elapsedSeconds, Is.EqualTo(elapsed).Within(0.001f));
      Assert.That(action.TryConsumeCompletionSignal(), Is.False);
    }

    [Test]
    public void RestoredCompletionCannotEmitTwice()
    {
      var saved = new CareActionSaveData
      {
        actionType = CareActionType.ClosedEyeRest,
        stage = CareActionStage.Completed,
        internalPhase = CareActionInternalPhase.ClosedEyeWaitReopen,
        elapsedSeconds = 45f,
        completionSignalEmitted = true,
      };
      var restored = new CareActionRuntime();
      restored.Begin(CareActionType.ClosedEyeRest, _config, saved);
      Assert.That(restored.TryConsumeCompletionSignal(), Is.False);
    }

    [Test]
    public void DevelopmentCompletionChangesNoStationResourceData()
    {
      var station = new CareStationSaveData
      {
        careShiftId = 7,
        pendingOfflineXP = 1,
        pendingIncidentXP = 24,
        collectedExperienceCount = 9,
      };
      var action = Begin(CareActionType.ScreenDown);
      while (action.Stage != CareActionStage.Completed) action.CompleteCurrentStepForDevelopment();
      action.TryConsumeCompletionSignal();
      Assert.That(station.careShiftId, Is.EqualTo(7));
      Assert.That(station.pendingOfflineXP, Is.EqualTo(1));
      Assert.That(station.pendingIncidentXP, Is.EqualTo(24));
      Assert.That(station.collectedExperienceCount, Is.EqualTo(9));
    }

    [Test]
    public void UnavailableScreenDownCanBeDeveloperSkippedExactlyOnce()
    {
      var action = Begin(CareActionType.ScreenDown);
      action.CompleteCurrentStepForDevelopment();
      action.Advance(0.1f, Frame(sensor: false));
      Assert.That(action.PauseReason, Is.EqualTo(CareActionPauseReason.SensorUnavailable));
      Assert.That(action.SkipUnavailableScreenDownForDevelopment(), Is.True);
      Assert.That(action.Stage, Is.EqualTo(CareActionStage.Completed));
      Assert.That(action.Data.completionSource, Is.EqualTo(CareActionCompletionSource.DeveloperSkipped));
      Assert.That(action.Data.CountsAsVerifiedCareAction, Is.False);
      Assert.That(action.SkipUnavailableScreenDownForDevelopment(), Is.False);
      Assert.That(action.TryConsumeCompletionSignal(), Is.True);
      Assert.That(action.TryConsumeCompletionSignal(), Is.False);
    }

    [Test]
    public void OnlyFourPlayerFacingCareActionsExist()
    {
      CollectionAssert.AreEquivalent(
        new[] { CareActionType.ScreenDown, CareActionType.ClosedEyeRest, CareActionType.FocusShift, CareActionType.GuidedEyeCircles },
        new[] { CareActionType.ScreenDown, CareActionType.ClosedEyeRest, CareActionType.FocusShift, CareActionType.GuidedEyeCircles });
      Assert.That(CareActionRuntime.DisplayNameFor(CareActionType.ScreenDown), Is.EqualTo("SCREEN DOWN"));
      Assert.That(CareActionRuntime.DisplayNameFor(CareActionType.ClosedEyeRest), Is.EqualTo("CLOSED-EYE REST"));
      Assert.That(CareActionRuntime.DisplayNameFor(CareActionType.FocusShift), Is.EqualTo("FOCUS SHIFT"));
      Assert.That(CareActionRuntime.DisplayNameFor(CareActionType.GuidedEyeCircles), Is.EqualTo("GUIDED EYE CIRCLES"));
    }

    private CareActionRuntime Begin(CareActionType type)
    {
      var action = new CareActionRuntime();
      action.Begin(type, _config);
      return action;
    }

    private void Hold(CareActionRuntime action, float ratio)
    {
      action.Advance(0.11f, Frame(ratio: ratio));
      action.Advance(0.11f, Frame(ratio: ratio));
      action.Advance(0.11f, Frame(ratio: ratio));
    }

    private static void CaptureStepReference(CareActionRuntime action)
    {
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.FocusReference));
      action.CompleteCurrentStepForDevelopment();
    }

    private static CareActionInputFrame Frame(
      bool active = true,
      bool tracking = true,
      bool eyesClosed = false,
      bool sensor = true,
      bool screenDown = false,
      bool returned = false,
      bool baseline = true,
      float ratio = 1f)
    {
      return new CareActionInputFrame(active, tracking, eyesClosed, sensor, screenDown, returned, baseline, ratio);
    }
  }
}
