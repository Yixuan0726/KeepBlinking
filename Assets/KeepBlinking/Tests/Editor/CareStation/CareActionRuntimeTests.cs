using System.Linq;
using System.Reflection;
using KeepBlinking.CareStation;
using KeepBlinking.Gameplay;
using NUnit.Framework;
using UnityEngine;

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
      _config.focusTargetHoldSeconds = 0.2f;
      _config.focusMinimumLegSeconds = 2.5f;
      _config.focusDirectionIntervalSeconds = 1.2f;
      _config.focusCycleCount = 6;
      _config.guidedPreviewSeconds = 0.2f;
      _config.guidedClockwiseSeconds = 0.2f;
      _config.guidedPauseSeconds = 0.1f;
      _config.guidedCounterClockwiseSeconds = 0.2f;
      _config.guidedRelaxSeconds = 0.1f;
      _config.guidedLapsPerDirection = 3;
      _config.pilotIntroSeconds = 0.1f;
      _config.pilotRoundSeconds = 0.1f;
      _config.pilotRoundsPerAxis = 3;
      _config.pilotTransitionSeconds = 0.1f;
    }

    [TearDown]
    public void TearDownAudioIntegrationObjects()
    {
      if (CareVoiceService.Instance != null)
        Object.DestroyImmediate(CareVoiceService.Instance.gameObject);
      if (CareAudioFeedbackController.Instance != null)
        Object.DestroyImmediate(CareAudioFeedbackController.Instance.gameObject);
    }

    [TestCase(CareActionType.ClosedEyeRest)]
    [TestCase(CareActionType.FocusShift)]
    [TestCase(CareActionType.GuidedEyeCircles)]
    [TestCase(CareActionType.PilotEyeRoutine)]
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
    public void RetiredScreenAndBlinkTasksCannotStart()
    {
      Assert.That(Begin(CareActionType.ScreenDown).Stage, Is.EqualTo(CareActionStage.Cancelled));
      Assert.That(Begin(CareActionType.BlinkReset).Stage, Is.EqualTo(CareActionStage.Cancelled));
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
    public void FocusShiftUsesImmutableSessionBaselineAndCompletesSixCyclesThenNeutral()
    {
      var action = Begin(CareActionType.FocusShift);
      const float baseline = 0.12f;
      action.Data.gestureReferenceScale = baseline;
      action.Data.gestureReferenceValid = true;
      EnterInitialFocusLeg(action);
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.FocusNearOne));
      for (var cycle = 0; cycle < 6; cycle++)
      {
        CompleteFocusLeg(action, 1.25f);
        Assert.That(action.Data.gestureReferenceScale, Is.EqualTo(baseline));
        EnterNextFocusLeg(action);
        CompleteFocusLeg(action, 0.78f);
        Assert.That(action.Data.gestureReferenceScale, Is.EqualTo(baseline));
        if (cycle < 5) EnterNextFocusLeg(action);
      }
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.FocusNeutralFinish));
      action.Advance(0.25f, FreshFrame(1f, 0.25f));
      Assert.That(action.Stage, Is.EqualTo(CareActionStage.Completed));
      Assert.That(action.Data.focusCycleCount, Is.EqualTo(6));
      Assert.That(action.TryConsumeCompletionSignal(), Is.True);
      Assert.That(action.TryConsumeCompletionSignal(), Is.False,
        "The sensor-completed Focus Shift signal must be one-shot.");
    }

    [Test]
    public void FocusCloserHoldAndMinimumLegAccumulateInParallel()
    {
      _config.focusTargetHoldSeconds = 0.7f;
      _config.focusMinimumLegSeconds = 3f;
      var action = Begin(CareActionType.FocusShift);
      action.Data.gestureReferenceScale = 1f;
      action.Data.gestureReferenceValid = true;
      EnterInitialFocusLeg(action);

      const float tick = 0.05f;
      var elapsed = 0f;
      for (var i = 0; i < 10; i++)
      {
        action.Advance(tick, FreshFrame(1.20f, tick));
        elapsed += tick;
      }
      Assert.That(action.Data.holdElapsedSeconds, Is.Zero);

      action.Advance(tick, FreshFrame(1.25f, tick));
      elapsed += tick;
      var thresholdReachedAt = elapsed;
      Assert.That(action.Data.holdElapsedSeconds, Is.EqualTo(tick).Within(0.001f),
        "Hold must begin on the first Closer sample, before the pace gate is ready.");
      Assert.That(action.Prompt, Does.StartWith("HOLD"));
      var holdBeforePresentationReads = action.Data.holdElapsedSeconds;
      var phaseBeforePresentationReads = action.Data.phaseElapsedSeconds;
      _ = action.Prompt;
      _ = action.Progress;
      _ = action.FocusConfirmationProgress;
      Assert.That(action.Data.holdElapsedSeconds, Is.EqualTo(holdBeforePresentationReads));
      Assert.That(action.Data.phaseElapsedSeconds, Is.EqualTo(phaseBeforePresentationReads),
        "UI/presentation reads must not mutate Focus timers.");

      while (action.Data.holdElapsedSeconds < 0.699f)
      {
        action.Advance(tick, FreshFrame(1.25f, tick));
        elapsed += tick;
      }
      Assert.That(action.Data.focusTargetStep, Is.Zero,
        "Finishing Hold early must still respect the independent three-second rhythm.");
      Assert.That(action.FocusHoldProgress, Is.EqualTo(1f).Within(0.001f));
      Assert.That(action.FocusConfirmationProgress, Is.GreaterThan(0f).And.LessThan(1f));

      while (action.Data.focusTargetStep == 0 && elapsed < 3.2f)
      {
        action.Advance(tick, FreshFrame(1.25f, tick));
        elapsed += tick;
      }

      Assert.That(action.Data.focusTargetStep, Is.EqualTo(1));
      Assert.That(elapsed, Is.EqualTo(3f).Within(tick + 0.001f));
      Assert.That(elapsed - thresholdReachedAt, Is.EqualTo(2.45f).Within(tick + 0.001f));
    }

    [Test]
    public void FocusCloserRequiresAContinuousHoldAcrossThresholdJitter()
    {
      _config.focusTargetHoldSeconds = 0.7f;
      _config.focusMinimumLegSeconds = 3f;
      var action = Begin(CareActionType.FocusShift);
      action.Data.gestureReferenceScale = 1f;
      action.Data.gestureReferenceValid = true;
      EnterInitialFocusLeg(action);

      for (var i = 0; i < 50; i++) action.Advance(0.05f, FreshFrame(1.20f, 0.05f));
      for (var i = 0; i < 7; i++) action.Advance(0.05f, FreshFrame(1.251f, 0.05f));
      Assert.That(action.Data.holdElapsedSeconds, Is.EqualTo(0.35f).Within(0.01f));

      action.Advance(0.05f, FreshFrame(1.249f, 0.05f));
      Assert.That(action.Data.holdElapsedSeconds, Is.Zero,
        "A sample below the unchanged 1.25 threshold must restart the stable hold.");
      for (var i = 0; i < 13; i++) action.Advance(0.05f, FreshFrame(1.251f, 0.05f));
      Assert.That(action.Data.focusTargetStep, Is.Zero);
      action.Advance(0.05f, FreshFrame(1.251f, 0.05f));
      Assert.That(action.Data.focusTargetStep, Is.EqualTo(1));
    }

    [Test]
    public void FocusTrackingLossResetsHoldAndRequiresOneFreshRecoverySample()
    {
      _config.focusTargetHoldSeconds = 0.7f;
      _config.focusMinimumLegSeconds = 3f;
      var action = Begin(CareActionType.FocusShift);
      action.Data.gestureReferenceScale = 1f;
      action.Data.gestureReferenceValid = true;
      EnterInitialFocusLeg(action);

      for (var i = 0; i < 50; i++) action.Advance(0.05f, FreshFrame(1.20f, 0.05f));
      for (var i = 0; i < 8; i++) action.Advance(0.05f, FreshFrame(1.25f, 0.05f));
      Assert.That(action.Data.holdElapsedSeconds, Is.EqualTo(0.4f).Within(0.01f));

      action.Advance(0.1f, Frame(tracking: false, ratio: 1.25f));
      Assert.That(action.PauseReason, Is.EqualTo(CareActionPauseReason.TrackingLost));
      Assert.That(action.Data.holdElapsedSeconds, Is.Zero);
      action.Advance(0.1f, FreshFrame(1.25f, 0.1f));
      Assert.That(action.PauseReason, Is.EqualTo(CareActionPauseReason.TrackingLost));
      Assert.That(action.Data.holdElapsedSeconds, Is.Zero,
        "The first fresh sample only clears the recovery guard.");
      action.Advance(0.1f, FreshFrame(1.25f, 0.1f));
      Assert.That(action.PauseReason, Is.EqualTo(CareActionPauseReason.None));
      Assert.That(action.Data.holdElapsedSeconds, Is.EqualTo(0.1f).Within(0.001f));
    }

    [Test]
    public void FocusNextLegCannotSkipNeutralRearm()
    {
      _config.focusTargetHoldSeconds = 0.7f;
      _config.focusMinimumLegSeconds = 3f;
      var action = Begin(CareActionType.FocusShift);
      action.Data.gestureReferenceScale = 1f;
      action.Data.gestureReferenceValid = true;
      EnterInitialFocusLeg(action);
      CompleteFocusLeg(action, 1.25f);
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.FocusReference));

      while (action.Phase == CareActionInternalPhase.FocusReference)
        action.Advance(0.1f, FreshFrame(0.78f, 0.1f));
      Assert.That(action.Data.focusRearmed, Is.False);
      for (var i = 0; i < 35; i++) action.Advance(0.1f, FreshFrame(0.78f, 0.1f));
      Assert.That(action.Data.focusTargetStep, Is.EqualTo(1));
      Assert.That(action.Data.holdElapsedSeconds, Is.Zero);
      Assert.That(action.Prompt, Is.EqualTo("RETURN TO CENTER"));

      action.Advance(0.1f, FreshFrame(1f, 0.1f));
      Assert.That(action.Data.focusRearmed, Is.True);
      for (var i = 0; i < 8; i++) action.Advance(0.1f, FreshFrame(0.78f, 0.1f));
      Assert.That(action.Data.focusTargetStep, Is.EqualTo(2));
      Assert.That(action.Data.focusCycleCount, Is.EqualTo(1));
    }

    [Test]
    public void FocusTooCloseImmediatelyShowsSafetyPromptAndRejectsHold()
    {
      _config.focusTargetHoldSeconds = 0.7f;
      _config.focusMinimumLegSeconds = 3f;
      var action = Begin(CareActionType.FocusShift);
      action.Data.gestureReferenceScale = 1f;
      action.Data.gestureReferenceValid = true;
      EnterInitialFocusLeg(action);
      for (var i = 0; i < 6; i++) action.Advance(0.1f, FreshFrame(1.25f, 0.1f));
      Assert.That(action.Data.holdElapsedSeconds, Is.EqualTo(0.6f).Within(0.01f));

      action.Advance(0.05f, FreshFrame(1.45f, 0.05f));
      Assert.That(action.PauseReason, Is.EqualTo(CareActionPauseReason.TooClose));
      Assert.That(action.Prompt, Is.EqualTo("TOO CLOSE\nMOVE AWAY"));
      Assert.That(action.Data.holdElapsedSeconds, Is.Zero);
      Assert.That(action.Data.focusTargetStep, Is.Zero);
    }

    [Test]
    public void FocusFarIsOnlyInternalProgressAndDoesNotExposePushAwaySignal()
    {
      var action = Begin(CareActionType.FocusShift);
      Assert.That(typeof(CareActionRuntime).GetEvent("PushAwayTriggered"), Is.Null);
      Assert.That(typeof(CareActionRuntime).GetEvent("CareActionCompleted"), Is.Null);
      action.Data.gestureReferenceScale = 1f;
      action.Data.gestureReferenceValid = true;
      EnterInitialFocusLeg(action);
      CompleteFocusLeg(action, 1.25f);
      EnterNextFocusLeg(action);
      CompleteFocusLeg(action, 0.78f);
      Assert.That(action.Data.focusTargetStep, Is.EqualTo(2));
      Assert.That(action.Stage, Is.Not.EqualTo(CareActionStage.Completed));
    }

    [Test]
    public void FocusShiftRejectsSmallChangesTooCloseAndTrackingRecoveryJump()
    {
      var action = Begin(CareActionType.FocusShift);
      action.Data.gestureReferenceScale = 1f;
      action.Data.gestureReferenceValid = true;
      EnterInitialFocusLeg(action);
      for (var i = 0; i < 16; i++) action.Advance(0.25f, FreshFrame(1.06f, 0.25f));
      Assert.That(action.Data.focusTargetStep, Is.Zero, "A 2%-6% change cannot complete Focus Shift.");
      var progress = action.DirectionProgress;
      action.Advance(0.25f, FreshFrame(1.45f, 0.25f));
      Assert.That(action.PauseReason, Is.EqualTo(CareActionPauseReason.TooClose));
      action.Advance(0.25f, Frame(tracking: false, ratio: 1.25f));
      Assert.That(action.DirectionProgress, Is.EqualTo(progress).Within(0.0001f));
      action.Advance(0.25f, FreshFrame(1.25f, 0.25f));
      Assert.That(action.Data.focusTargetStep, Is.Zero, "The first recovered sample cannot complete a step.");
      Assert.That(action.Data.focusTargetStep, Is.Zero);
    }

    [Test]
    public void GuidedEyeCirclesUsesOnlyClosureAndNeverGazeDirection()
    {
      var action = Begin(CareActionType.GuidedEyeCircles);
      Assert.That(typeof(CareActionInputFrame).GetField("GazeYaw"), Is.Null);
      Assert.That(typeof(CareActionInputFrame).GetField("GazePitch"), Is.Null);
      action.CompleteCurrentStepForDevelopment();
      action.CompleteCurrentStepForDevelopment();
      action.CompleteCurrentStepForDevelopment();
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.GuidedPromptClose));
      action.Advance(0.2f, Frame(eyesClosed: true));
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.GuidedClosedRest));
    }

    [Test]
    public void GuidedEyeCirclesOpenPauseKeepsCurrentPhaseProgress()
    {
      var action = Begin(CareActionType.GuidedEyeCircles);
      while (action.Phase != CareActionInternalPhase.GuidedClosedRest)
        action.CompleteCurrentStepForDevelopment();
      action.Advance(0.05f, Frame(eyesClosed: true));
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
      var action = Begin(CareActionType.PilotEyeRoutine);
      while (action.Stage != CareActionStage.Completed) action.CompleteCurrentStepForDevelopment();
      action.TryConsumeCompletionSignal();
      Assert.That(station.careShiftId, Is.EqualTo(7));
      Assert.That(station.pendingOfflineXP, Is.EqualTo(1));
      Assert.That(station.pendingIncidentXP, Is.EqualTo(24));
      Assert.That(station.collectedExperienceCount, Is.EqualTo(9));
    }

    [Test]
    public void FourPlayerFacingCareActionsUseTheFinalEnglishNames()
    {
      CollectionAssert.AreEquivalent(
        new[] { CareActionType.PilotEyeRoutine, CareActionType.ClosedEyeRest, CareActionType.FocusShift, CareActionType.GuidedEyeCircles },
        new[] { CareActionType.PilotEyeRoutine, CareActionType.ClosedEyeRest, CareActionType.FocusShift, CareActionType.GuidedEyeCircles });
      Assert.That(CareActionRuntime.DisplayNameFor(CareActionType.BlinkReset), Is.Empty,
        "The serialized legacy value must not become a player task again.");
      Assert.That(CareActionLibrary.Purpose(CareActionType.BlinkReset), Is.Empty);
      Assert.That(CareActionLibrary.StationPurpose(CareActionType.BlinkReset), Is.Empty);
      Assert.That(CareActionRuntime.DisplayNameFor(CareActionType.ScreenDown), Is.Empty);
      Assert.That(CareActionRuntime.DisplayNameFor(CareActionType.ClosedEyeRest), Is.EqualTo("CLOSED-EYE REST"));
      Assert.That(CareActionRuntime.DisplayNameFor(CareActionType.FocusShift), Is.EqualTo("FOCUS SHIFT"));
      Assert.That(CareActionRuntime.DisplayNameFor(CareActionType.GuidedEyeCircles), Is.EqualTo("GUIDED EYE MOVEMENT"));
      Assert.That(CareActionRuntime.DisplayNameFor(CareActionType.PilotEyeRoutine), Is.EqualTo("PILOT EYE ROUTINE"));
    }

    [Test]
    public void DefaultActionPacingMatchesTheCareRoutineDesignWindow()
    {
      var defaults = CareActionConfiguration.Default;
      Assert.That(defaults.closedEyeDurationSeconds, Is.EqualTo(45f));
      Assert.That(defaults.focusCycleCount, Is.EqualTo(6));
      Assert.That(defaults.focusCloserRatio, Is.EqualTo(1.25f));
      Assert.That(defaults.focusAwayRatio, Is.EqualTo(0.78f));
      Assert.That(defaults.focusTooCloseRatio, Is.EqualTo(1.45f));
      Assert.That(defaults.focusTargetHoldSeconds, Is.EqualTo(0.7f));
      Assert.That(defaults.focusMinimumLegSeconds, Is.EqualTo(3f));
      Assert.That(defaults.guidedPreviewSeconds +
                  (defaults.guidedClockwiseSeconds + defaults.guidedCounterClockwiseSeconds) *
                  defaults.guidedLapsPerDirection + defaults.guidedPauseSeconds +
                  defaults.guidedRelaxSeconds + defaults.closeStartHoldSeconds +
                  defaults.reopenHoldSeconds, Is.InRange(40f, 48f));
      Assert.That(defaults.pilotRoundSeconds * defaults.pilotRoundsPerAxis * 4f +
                  defaults.pilotIntroSeconds + defaults.pilotTransitionSeconds, Is.InRange(40f, 50f));
    }

    [Test]
    public void RetiredTasksCannotStartButEyeStateStillDrivesRest()
    {
      var retired = Begin(CareActionType.BlinkReset);
      Assert.That(retired.Stage, Is.EqualTo(CareActionStage.Cancelled));
      Assert.That(typeof(CareActionRunner).GetMethod("SimulateBlink"), Is.Null);
      Assert.That(typeof(CareStationController).GetMethod("SimulateBlinkForDevelopment"), Is.Null);
      Assert.That(Begin(CareActionType.ScreenDown).Stage, Is.EqualTo(CareActionStage.Cancelled));

      var rest = Begin(CareActionType.ClosedEyeRest);
      rest.Advance(0.2f, Frame(eyesClosed: true));
      Assert.That(rest.Phase, Is.EqualTo(CareActionInternalPhase.ClosedEyeActive));
      Assert.That(rest.Stage, Is.EqualTo(CareActionStage.Active));
    }

    [Test]
    public void GuidedRequiresThreeClockwiseAndThreeCounterclockwiseLapsBeforeClosingEyes()
    {
      var action = Begin(CareActionType.GuidedEyeCircles);
      action.Advance(0.21f, Frame());
      action.Advance(0.21f, Frame());
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.GuidedClockwise));
      Assert.That(action.Data.guidedLapCount, Is.EqualTo(2));
      action.Advance(0.21f, Frame());
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.GuidedPause));
      action.Advance(0.1f, Frame());
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.GuidedCounterClockwise));
      action.Advance(0.21f, Frame());
      action.Advance(0.21f, Frame());
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.GuidedCounterClockwise));
      action.Advance(0.21f, Frame());
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.GuidedPromptClose));
    }

    [Test]
    public void PilotExecutesFourAxesWithThreeRoundsAndNoCirclePhase()
    {
      var action = Begin(CareActionType.PilotEyeRoutine);
      for (var axis = 0; axis < 4; axis++)
      {
        Assert.That(action.Data.pilotCurrentAxis, Is.EqualTo(axis));
        for (var round = 0; round < 3; round++)
        {
          Assert.That(action.Data.pilotCurrentRound, Is.EqualTo(round));
          var guard = 0;
          while (action.Data.pilotCurrentAxis == axis && action.Data.pilotCurrentRound == round && guard++ < 12)
            action.Advance(0.5f, Frame());
          Assert.That(guard, Is.LessThan(12), "Pilot round did not advance at its sanitized production cadence.");
        }
      }
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.PilotTransition));
      Assert.That(action.Prompt, Does.Contain("AXES COMPLETE"));
      Assert.That(action.Prompt, Does.Not.Contain("CLOCKWISE"));
      action.Advance(1.5f, Frame());
      Assert.That(action.Stage, Is.EqualTo(CareActionStage.Completed));
    }

    [Test]
    public void FourFinalActionsUseDistinctCompletionAudioClips()
    {
      var owner = new GameObject("Care Audio Distinctness Test");
      try
      {
        var audio = owner.AddComponent<CareAudioFeedbackController>();
        var clipFields = new[] { "_focusCompletion", "_guidedOpen", "_pilotCompletion", "_restOpen" };
        var clips = clipFields.Select(field =>
          typeof(CareAudioFeedbackController).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(audio) as AudioClip).ToArray();

        Assert.That(clips, Has.All.Not.Null);
        Assert.That(clips.Distinct().Count(), Is.EqualTo(clipFields.Length));
        Assert.That(clips.Select(clip => clip.name).Distinct().Count(), Is.EqualTo(clipFields.Length));
      }
      finally
      {
        Object.DestroyImmediate(owner);
      }
    }

    [Test]
    public void FormalActionsResolveOneContinuousRoutineMusicClip()
    {
      var owner = new GameObject("Care Routine Music Mapping Test");
      try
      {
        var audio = owner.AddComponent<CareAudioFeedbackController>();
        var actions = new[]
        {
          CareActionType.FocusShift,
          CareActionType.PilotEyeRoutine,
          CareActionType.GuidedEyeCircles,
          CareActionType.ClosedEyeRest,
        };
        var clips = actions.Select(audio.GetAmbienceClip).ToArray();

        Assert.That(clips, Has.All.Not.Null);
        Assert.That(clips.Distinct().Count(), Is.EqualTo(1),
          "Action transitions must retain one authored Routine track instead of restarting action music.");
        Assert.That(clips[0].name, Is.EqualTo("LongNight_Aventure"));
      }
      finally
      {
        Object.DestroyImmediate(owner);
      }
    }

    [Test]
    public void VoiceDuckingUsesConfiguredSixDecibelReduction()
    {
      var owner = new GameObject("Care Voice Ducking Test");
      try
      {
        var audio = owner.AddComponent<CareAudioFeedbackController>();
        Assert.That(audio.UnduckedAmbienceVolume, Is.GreaterThan(0f));
        Assert.That(audio.DuckedAmbienceVolume, Is.GreaterThan(0f));
        var decibels = 20f * Mathf.Log10(audio.DuckedAmbienceVolume / audio.UnduckedAmbienceVolume);
        Assert.That(decibels, Is.EqualTo(-6f).Within(0.01f));
      }
      finally
      {
        Object.DestroyImmediate(owner);
      }
    }

    [Test]
    public void VoiceRequestedDuringPauseStartsOnceOnlyAfterResume()
    {
      var voice = CareVoiceService.EnsureExists();
      voice.Stop();
      voice.SetPaused(true);

      voice.Speak("focus-away", "SLOWLY MOVE THE PHONE AWAY.", 2.9f,
        CareVoicePriority.Direction);

      Assert.That(voice.SpeechRequestCount, Is.EqualTo(1));
      Assert.That(voice.LastRequestedKey, Is.EqualTo("focus-away"));
      Assert.That(voice.LastRequestedText, Is.EqualTo("SLOWLY MOVE THE PHONE AWAY."));
      Assert.That(voice.LastSpokenKey, Is.Empty,
        "A Tracking Lost or application pause must not pretend queued narration has played.");

      voice.SetPaused(false);
      Assert.That(voice.LastSpokenKey, Is.EqualTo("focus-away"));
      Assert.That(voice.LastSpokenText, Is.EqualTo("SLOWLY MOVE THE PHONE AWAY."));
      Assert.That(voice.SpeechRequestCount, Is.EqualTo(1));

      voice.SetPaused(false);
      Assert.That(voice.SpeechRequestCount, Is.EqualTo(1),
        "Repeated resume updates must not re-request an already consumed line.");
    }

    [Test]
    public void RestoringTrackingLostSnapshotStartsEveryAudioChannelPaused()
    {
      var owner = new GameObject("Paused Care Audio Restore Test");
      try
      {
        var runner = owner.AddComponent<CareActionRunner>();
        var snapshot = new CareActionSaveData
        {
          actionType = CareActionType.ClosedEyeRest,
          stage = CareActionStage.Paused,
          internalPhase = CareActionInternalPhase.ClosedEyeActive,
          pauseReason = CareActionPauseReason.TrackingLost,
          elapsedSeconds = 12f,
        };

        Assert.That(runner.StartAction(CareActionType.ClosedEyeRest, snapshot), Is.True);
        Assert.That(CareAudioFeedbackController.Instance.ActionAudioPaused, Is.True,
          "A restored paused action must not start its ambience for one frame.");
        Assert.That(CareVoiceService.Instance.IsPaused, Is.True,
          "Tracking Lost restore must queue narration until a valid action frame resumes it.");
      }
      finally
      {
        Object.DestroyImmediate(owner);
      }
    }

    [Test]
    public void PausedActionAudioRejectsOrdinaryAndGuidedCuePlayback()
    {
      var audio = CareAudioFeedbackController.EnsureExists();
      var type = typeof(CareAudioFeedbackController);
      var ordinary = type.GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)
        ?.GetValue(audio) as AudioSource;
      var guided = type.GetField("_guidedSource", BindingFlags.Instance | BindingFlags.NonPublic)
        ?.GetValue(audio) as AudioSource;
      Assert.That(ordinary, Is.Not.Null);
      Assert.That(guided, Is.Not.Null);

      audio.SetActionAudioPaused(true);
      audio.PlayFocusCloser();
      audio.PlayPilotDirection(0);

      Assert.That(ordinary.clip, Is.Null, "Ordinary cues must remain silent while action audio is paused.");
      Assert.That(guided.clip, Is.Null, "Guidance cues must remain silent while action audio is paused.");
    }

    [Test]
    public void SynchronizedPilotDirectionReplacesAnOlderInstructionWithoutQueueingBehindIt()
    {
      var voice = CareVoiceService.EnsureExists();
      voice.Stop();
      voice.Speak("pilot-axis-0", "LOOK UP AND DOWN.", 3.2f, CareVoicePriority.Instruction);
      Assert.That(voice.LastSpokenKey, Is.EqualTo("pilot-axis-0"));

      voice.SpeakSynchronizedDirection("pilot-direction-0", "UP.", 0.55f);

      Assert.That(voice.LastSpokenKey, Is.EqualTo("pilot-direction-0"));
      Assert.That(voice.LastSpokenText, Is.EqualTo("UP."));
      var pending = typeof(CareVoiceService)
        .GetField("_pending", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(voice);
      var pendingCount = (int)(pending?.GetType().GetProperty("Count")?.GetValue(pending) ?? -1);
      Assert.That(pendingCount, Is.Zero, "A visual direction word must not survive as stale queued speech.");
    }

    [Test]
    public void RestoredPilotTransitionHonorsPersistedCompletionCueConsumption()
    {
      var owner = new GameObject("Pilot Completion Cue Restore Test");
      try
      {
        var runner = owner.AddComponent<CareActionRunner>();
        var snapshot = new CareActionSaveData
        {
          actionType = CareActionType.PilotEyeRoutine,
          stage = CareActionStage.Demonstrating,
          internalPhase = CareActionInternalPhase.PilotTransition,
          pilotCurrentAxis = 4,
          pilotCompletionConsumed = true,
          consumedVoiceCueMask = 1 << 2,
        };
        Assert.That(runner.StartAction(CareActionType.PilotEyeRoutine, snapshot), Is.True);

        var consumed = typeof(CareActionRunner)
          .GetField("_completionCuePlayed", BindingFlags.Instance | BindingFlags.NonPublic)
          ?.GetValue(runner);
        Assert.That(consumed, Is.EqualTo(true));
        InvokeRunner(
          runner,
          "PlayPhaseTransition",
          CareActionInternalPhase.PilotDiagonalB,
          CareActionInternalPhase.PilotTransition,
          true,
          true);
        var completionSource = typeof(CareAudioFeedbackController)
          .GetField("_completionSource", BindingFlags.Instance | BindingFlags.NonPublic)
          ?.GetValue(CareAudioFeedbackController.Instance) as AudioSource;
        Assert.That(completionSource, Is.Not.Null);
        Assert.That(completionSource.clip, Is.Null,
          "A restored Pilot transition must not replay its already consumed completion cue.");
      }
      finally
      {
        Object.DestroyImmediate(owner);
      }
    }

    [TestCase(CareActionType.FocusShift, "focus-intro",
      "KEEP YOUR HEAD STILL. MOVE THE PHONE, NOT YOUR HEAD.")]
    [TestCase(CareActionType.PilotEyeRoutine, "pilot-intro",
      "KEEP YOUR HEAD STILL. MOVE ONLY YOUR EYES.")]
    [TestCase(CareActionType.GuidedEyeCircles, "guided-intro",
      "KEEP YOUR HEAD STILL. FOLLOW THE DOT WITH YOUR EYES.")]
    [TestCase(CareActionType.ClosedEyeRest, "rest-intro",
      "GENTLY CLOSE YOUR EYES.")]
    public void ActionIntroVoiceUsesTheExactActionSpecificLineOnlyOnce(
      CareActionType actionType,
      string expectedKey,
      string expectedText)
    {
      var owner = CreateRunnerWithRuntime(actionType, out var runner);
      try
      {
        var voice = CareVoiceService.EnsureExists();
        voice.Stop();
        var before = voice.SpeechRequestCount;

        InvokeRunner(runner, "PlayActionIntroNarration", actionType);

        Assert.That(voice.SpeechRequestCount, Is.EqualTo(before + 1));
        Assert.That(voice.LastSpokenKey, Is.EqualTo(expectedKey));
        Assert.That(voice.LastSpokenText, Is.EqualTo(expectedText));

        InvokeRunner(runner, "PlayActionIntroNarration", actionType);
        Assert.That(voice.SpeechRequestCount, Is.EqualTo(before + 1),
          "Repeated presentation of the same intro must not enqueue voice every frame.");
      }
      finally
      {
        Object.DestroyImmediate(owner);
      }
    }

    [Test]
    public void FocusPhaseVoiceFollowsCloserAwayEventsAndDoesNotRepeatAnObservedEvent()
    {
      var owner = CreateRunnerWithRuntime(CareActionType.FocusShift, out var runner);
      try
      {
        var voice = CareVoiceService.EnsureExists();
        voice.Stop();
        var before = voice.SpeechRequestCount;
        var sequence = new[]
        {
          (Step: 0, Phase: CareActionInternalPhase.FocusNearOne,
            Key: "focus-closer", Text: "SLOWLY MOVE THE PHONE CLOSER."),
          (Step: 1, Phase: CareActionInternalPhase.FocusFarOne,
            Key: "focus-away", Text: "SLOWLY MOVE THE PHONE AWAY."),
          (Step: 2, Phase: CareActionInternalPhase.FocusNearOne,
            Key: "focus-closer", Text: "MOVE CLOSER."),
          (Step: 3, Phase: CareActionInternalPhase.FocusFarOne,
            Key: "focus-away", Text: "MOVE AWAY."),
        };

        for (var i = 0; i < sequence.Length; i++)
        {
          runner.SaveData.focusTargetStep = sequence[i].Step;
          InvokeRunner(runner, "PlayPhaseNarration", CareActionInternalPhase.FocusReference,
            sequence[i].Phase);
          Assert.That(voice.SpeechRequestCount, Is.EqualTo(before + i + 1));
          Assert.That(voice.LastSpokenKey, Is.EqualTo(sequence[i].Key));
          Assert.That(voice.LastSpokenText, Is.EqualTo(sequence[i].Text));

          InvokeRunner(runner, "PlayPhaseNarration", CareActionInternalPhase.FocusReference,
            sequence[i].Phase);
          Assert.That(voice.SpeechRequestCount, Is.EqualTo(before + i + 1),
            $"Focus event {sequence[i].Step} must be consumed after its first narration.");
        }
      }
      finally
      {
        Object.DestroyImmediate(owner);
      }
    }

    [Test]
    public void GuidedPhaseVoiceUsesClockwiseCenterCounterclockwiseThenCloseSequence()
    {
      var owner = CreateRunnerWithRuntime(CareActionType.GuidedEyeCircles, out var runner);
      try
      {
        var voice = CareVoiceService.EnsureExists();
        voice.Stop();
        var before = voice.SpeechRequestCount;
        var sequence = new[]
        {
          (Phase: CareActionInternalPhase.GuidedClockwise,
            Key: "guided-clockwise", Text: "FOLLOW CLOCKWISE. THREE SLOW CIRCLES."),
          (Phase: CareActionInternalPhase.GuidedPause,
            Key: "guided-center", Text: "RETURN TO CENTER."),
          (Phase: CareActionInternalPhase.GuidedCounterClockwise,
            Key: "guided-counterclockwise", Text: "NOW COUNTERCLOCKWISE. THREE SLOW CIRCLES."),
          (Phase: CareActionInternalPhase.GuidedPromptClose,
            Key: "guided-close", Text: "GENTLY CLOSE YOUR EYES AND RELAX."),
        };

        for (var i = 0; i < sequence.Length; i++)
        {
          InvokeRunner(runner, "PlayPhaseNarration", CareActionInternalPhase.None,
            sequence[i].Phase);
          Assert.That(voice.SpeechRequestCount, Is.EqualTo(before + i + 1));
          Assert.That(voice.LastSpokenKey, Is.EqualTo(sequence[i].Key));
          Assert.That(voice.LastSpokenText, Is.EqualTo(sequence[i].Text));

          InvokeRunner(runner, "PlayPhaseNarration", CareActionInternalPhase.None,
            sequence[i].Phase);
          Assert.That(voice.SpeechRequestCount, Is.EqualTo(before + i + 1));
        }
      }
      finally
      {
        Object.DestroyImmediate(owner);
      }
    }

    [Test]
    public void PilotAxisAndDirectionVoiceUsesTheExactFourAxisOrder()
    {
      var owner = CreateRunnerWithRuntime(CareActionType.PilotEyeRoutine, out var runner);
      try
      {
        var voice = CareVoiceService.EnsureExists();
        voice.Stop();
        var before = voice.SpeechRequestCount;
        var axisLines = new[]
        {
          "LOOK UP AND DOWN.",
          "LOOK LEFT AND RIGHT.",
          "LOOK UPPER LEFT AND LOWER RIGHT.",
          "LOOK LOWER LEFT AND UPPER RIGHT.",
        };
        var directionLines = new[]
        {
          (First: "UP.", Second: "DOWN."),
          (First: "LEFT.", Second: "RIGHT."),
          (First: "UPPER LEFT.", Second: "LOWER RIGHT."),
          (First: "LOWER LEFT.", Second: "UPPER RIGHT."),
        };
        var requests = 0;

        for (var axis = 0; axis < 4; axis++)
        {
          runner.SaveData.pilotCurrentAxis = axis;
          runner.SaveData.pilotCurrentRound = 0;
          runner.SaveData.pilotCurrentEndpoint = 0;
          InvokeRunner(runner, "PlayPilotProgressIfNeeded");
          requests++;
          Assert.That(voice.SpeechRequestCount, Is.EqualTo(before + requests));
          Assert.That(voice.LastSpokenKey, Is.EqualTo($"pilot-axis-{axis}"));
          Assert.That(voice.LastSpokenText, Is.EqualTo(axisLines[axis]));

          runner.SaveData.pilotCurrentEndpoint = 1;
          InvokeRunner(runner, "PlayPilotProgressIfNeeded");
          requests++;
          Assert.That(voice.LastSpokenText, Is.EqualTo(directionLines[axis].First));

          InvokeRunner(runner, "PlayPilotProgressIfNeeded");
          Assert.That(voice.SpeechRequestCount, Is.EqualTo(before + requests),
            "An unchanged Pilot endpoint must not repeat its direction voice.");

          runner.SaveData.pilotCurrentEndpoint = 3;
          InvokeRunner(runner, "PlayPilotProgressIfNeeded");
          requests++;
          Assert.That(voice.LastSpokenText, Is.EqualTo(directionLines[axis].Second));
        }
      }
      finally
      {
        Object.DestroyImmediate(owner);
      }
    }

    [Test]
    public void PausingAndRestoringAConsumedPhaseDoesNotRepeatItsVoice()
    {
      var owner = CreateRunnerWithRuntime(CareActionType.ClosedEyeRest, out var runner);
      GameObject restoredOwner = null;
      try
      {
        var voice = CareVoiceService.EnsureExists();
        voice.Stop();
        var before = voice.SpeechRequestCount;

        InvokeRunner(runner, "PlayPhaseNarration", CareActionInternalPhase.None,
          CareActionInternalPhase.ClosedEyePrompt);
        Assert.That(voice.SpeechRequestCount, Is.EqualTo(before + 1));
        Assert.That(voice.LastSpokenKey, Is.EqualTo("rest-close"));

        runner.PauseAction();
        InvokeRunner(runner, "UpdateAudioSuspension", Frame());
        InvokeRunner(runner, "UpdateAudioSuspension", Frame());
        Assert.That(voice.IsPaused, Is.True);
        Assert.That(voice.SpeechRequestCount, Is.EqualTo(before + 1),
          "Repeated paused updates must pause the same utterance, not enqueue it again.");
        runner.ResumeAction();
        InvokeRunner(runner, "UpdateAudioSuspension", Frame());
        Assert.That(voice.IsPaused, Is.False);
        InvokeRunner(runner, "PlayPhaseNarration", CareActionInternalPhase.None,
          CareActionInternalPhase.ClosedEyePrompt);
        Assert.That(voice.SpeechRequestCount, Is.EqualTo(before + 1));

        var snapshot = JsonUtility.FromJson<CareActionSaveData>(JsonUtility.ToJson(runner.SaveData));
        restoredOwner = CreateRunnerWithRuntime(CareActionType.ClosedEyeRest, out var restoredRunner,
          snapshot);
        InvokeRunner(restoredRunner, "PlayPhaseNarration", CareActionInternalPhase.None,
          CareActionInternalPhase.ClosedEyePrompt);
        Assert.That(voice.SpeechRequestCount, Is.EqualTo(before + 1),
          "A persisted consumed voice bit must survive action restore without replaying narration.");
      }
      finally
      {
        if (restoredOwner != null) Object.DestroyImmediate(restoredOwner);
        Object.DestroyImmediate(owner);
      }
    }

    private CareActionRuntime Begin(CareActionType type)
    {
      var action = new CareActionRuntime();
      action.Begin(type, _config);
      return action;
    }

    private GameObject CreateRunnerWithRuntime(
      CareActionType actionType,
      out CareActionRunner runner,
      CareActionSaveData restore = null)
    {
      var owner = new GameObject($"{actionType} Voice Integration Test");
      runner = owner.AddComponent<CareActionRunner>();
      var runtime = new CareActionRuntime();
      runtime.Begin(actionType, _config, restore);
      typeof(CareActionRunner).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)
        ?.SetValue(runner, runtime);
      return owner;
    }

    private static object InvokeRunner(CareActionRunner runner, string methodName, params object[] arguments)
    {
      var method = typeof(CareActionRunner).GetMethod(
        methodName,
        BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.That(method, Is.Not.Null, $"Missing CareActionRunner.{methodName} test seam.");
      return method.Invoke(runner, arguments);
    }

    private static void EnterInitialFocusLeg(CareActionRuntime action)
    {
      action.Advance(0.01f, FreshFrame(1f, 0.01f));
      for (var i = 0; i < 40 && action.Phase == CareActionInternalPhase.FocusNeutralStart; i++)
        action.Advance(0.05f, FreshFrame(1f, 0.05f));
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.FocusNearOne));
    }

    private static void EnterNextFocusLeg(CareActionRuntime action)
    {
      Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.FocusReference));
      for (var i = 0; i < 6 && action.Phase == CareActionInternalPhase.FocusReference; i++)
        action.Advance(0.25f, FreshFrame(1f, 0.25f));
      action.Advance(0.01f, FreshFrame(1f, 0.01f));
    }

    private static void CompleteFocusLeg(CareActionRuntime action, float ratio)
    {
      for (var i = 0; i < 12 && action.Phase != CareActionInternalPhase.FocusReference &&
                          action.Phase != CareActionInternalPhase.FocusNeutralFinish; i++)
        action.Advance(0.25f, FreshFrame(ratio, 0.25f));
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

    private static CareActionInputFrame FreshFrame(float ratio, float sampleDelta)
    {
      return new CareActionInputFrame(true, true, false, true, false, false, true, ratio, true, sampleDelta);
    }
  }
}
