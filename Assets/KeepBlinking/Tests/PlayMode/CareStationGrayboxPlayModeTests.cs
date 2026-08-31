using System.Collections;
using System.Linq;
using KeepBlinking.CareStation;
using KeepBlinking.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace KeepBlinking.Tests
{
  public sealed class CareStationGrayboxPlayModeTests
  {
    // Face scales are area-like, so a completed Away step -- 22% farther, a linear ratio of
    // 0.78 -- is that ratio squared. See FaceDistanceRatio.
    private const float FarStepScale = 0.6084f;

    [UnityTest]
    public IEnumerator UnifiedClosedEyeRunnerPlaysEachCueOnlyOnce()
    {
      var root = new GameObject("Closed-Eye Cue Runner Test");
      try
      {
        var runner = root.AddComponent<CareActionRunner>();
        runner.ConfigureStationDurations(1f, 1f, 0.1f, 0.1f);
        Assert.That(runner.StartAction(CareActionType.ClosedEyeRest, null, true), Is.True);
        Assert.That(runner.CloseRequestCuePlayCount, Is.EqualTo(1));
        Assert.That(runner.ReadyToOpenCuePlayCount, Is.Zero);

        runner.CompleteCurrentStepForDevelopment();
        Assert.That(runner.CloseRequestCuePlayCount, Is.EqualTo(1),
          "Entering Active must not replay the close request.");
        Assert.That(runner.ReadyToOpenCuePlayCount, Is.Zero);

        runner.CompleteCurrentStepForDevelopment();
        Assert.That(runner.ReadyToOpenCuePlayCount, Is.EqualTo(1));
        runner.CompleteCurrentStepForDevelopment();
        Assert.That(runner.ReadyToOpenCuePlayCount, Is.EqualTo(1),
          "Reliable reopen/completion must not replay the ready cue.");
      }
      finally
      {
        Object.Destroy(root);
        if (CareAudioFeedbackController.Instance != null)
          Object.Destroy(CareAudioFeedbackController.Instance.gameObject);
      }
      yield return null;
    }

    [UnityTest]
    public IEnumerator ThreeShiftGrayboxLoopSettlesOnArrivalThenContinues()
    {
      var save = new CareStationSaveData();
      for (var shift = 1; shift <= 3; shift++)
      {
        save.currentShift = shift;
        save.selectedIncident = CareStationShiftRules.IncidentForShift(shift);
        save.pendingIncidentXP = CareStationShiftRules.IncidentExperience(save.selectedIncident);
        save.pendingGoldBottleCount = shift == 3 ? 1 : 0;

        if (save.selectedIncident == CareStationIncidentType.DrySpot)
        {
          var rest = CareStationActionLogic.AdvanceClosedEyeRest(0f, 45f, 45f, true, true);
          Assert.That(rest.Completed, Is.True);
        }

        var ledger = new CareStationExperienceLedger();
        ledger.Begin(save.pendingIncidentXP);
        ledger.RecordArrival(save.pendingIncidentXP - 1);
        Assert.That(ledger.IsComplete, Is.False, "Storage must not settle before the final bottle arrives.");
        ledger.RecordArrival(1);
        Assert.That(ledger.IsComplete, Is.True);

        save.completedShifts++;
        save.currentShift++;
        save.selectedIncident = CareStationIncidentType.None;
        save.pendingIncidentXP = 0;
      }

      Assert.That(save.pendingGoldBottleCount, Is.EqualTo(1), "Shift 3 must produce one Gold Bottle before collection reset.");
      Assert.That(CareStationStateRules.CanOfferStationUpgrade(save.completedShifts, true, save), Is.True);
      CareStationShiftRules.ApplyUpgrade(save, CareStationUpgradeId.MoreWorkers);
      Assert.That(save.crewCount, Is.EqualTo(3));
      Assert.That(CareStationShiftRules.IncidentForShift(save.currentShift), Is.Not.EqualTo(CareStationIncidentType.None));
      yield return null;
    }

    [UnityTest]
    public IEnumerator OfflineProductionAndCareBottlesUseSeparateSettlementAndDistanceGate()
    {
      var save = new CareStationSaveData
      {
        pendingOfflineXP = 72,
        pendingIncidentXP = 24,
        careEnergy = 72,
        careActionCompleted = false,
        offlineCollectionResolved = false,
      };
      save.storageHours = 48;
      Assert.That(CareStationShiftRules.IncidentForShift(2), Is.EqualTo(CareStationIncidentType.DrySpot));
      var settlement = new CareStationProductionController().Settle(save, save.pendingOfflineXP);
      save.pendingOfflineXP = 0;
      Assert.That(settlement.ProducedStored, Is.EqualTo(48));
      Assert.That(settlement.ProducedDiscarded, Is.Zero,
        "Full Storage pauses production instead of discarding the remaining Care Energy.");
      Assert.That(save.careEnergy, Is.EqualTo(24));
      save.offlineCollectionResolved = true;
      Assert.That(CareStationStateRules.CanPresentIncident(save.offlineCollectionResolved, false), Is.False);

      save.returnedNeutralAfterOffline = true;
      Assert.That(CareStationStateRules.CanPresentIncident(true, true), Is.True);
      Assert.That(CareStationStateRules.CanArmCollection(CareStationCollectionPhase.Care, false, true), Is.False);
      save.careActionCompleted = true;
      Assert.That(CareStationStateRules.CanArmCollection(CareStationCollectionPhase.Care, true, true), Is.True);
      yield return null;
    }

    [UnityTest]
    public IEnumerator NewSaveCompletesDistanceResetThenOneCareCollectionBeforeShiftComplete()
    {
      var save = new CareStationSaveData
      {
        careShiftId = 1,
        currentShift = 1,
        currentState = CareStationState.Dormant,
        offlineCollectionResolved = false,
      };

      var storageValue = 0;
      var production = new CareStationProductionController();
      var offlineSettlement = production.Settle(save, 0);
      Assert.That(offlineSettlement.TotalStored, Is.Zero);
      Assert.That(save.pendingOfflineXP, Is.Zero);

      save.distanceResetReferenceScale = 0.12f;
      save.distanceResetReferenceValid = true;
      var resetAway = new CareRelativeDistanceStep(
        CareDistanceDirection.Away, holdSeconds: 0.1f);
      var resetAwayScale = save.distanceResetReferenceScale * FarStepScale;
      Assert.That(resetAway.Advance(
        resetAwayScale, save.distanceResetReferenceScale, 0.1f, true, true), Is.True);
      save.distanceResetAwayScale = resetAwayScale;
      save.distanceResetAwayCompleted = true;
      Assert.That(storageValue, Is.Zero, "DISTANCE RESET must never settle Storage.");

      var resetCloser = new CareRelativeDistanceStep(
        CareDistanceDirection.Closer, holdSeconds: 0.1f);
      Assert.That(resetCloser.Advance(
        save.distanceResetReferenceScale, resetAwayScale, 0.1f, true, true), Is.True);
      save.distanceResetCompleted = true;
      save.offlineCollectionResolved = true;
      save.returnedNeutralAfterOffline = true;
      save.selectedIncident = CareStationShiftRules.IncidentForShift(save.currentShift);
      save.pendingIncidentXP = CareStationShiftRules.IncidentExperience(save.selectedIncident);
      Assert.That(CareStationStateRules.CanArmCollection(CareStationCollectionPhase.Care, false, true), Is.False);
      save.careActionCompleted = true;
      Assert.That(CareStationStateRules.CanArmCollection(CareStationCollectionPhase.Care, true, true), Is.True);
      Assert.That(save.carePushAwayCompletion, Is.EqualTo(CareStationPushAwayCompletion.None),
        "DISTANCE RESET must not complete the care collection.");

      save.carePushReferenceScale = 0.09f;
      save.carePushReferenceValid = true;
      Assert.That(save.carePushReferenceScale, Is.Not.EqualTo(0.12f));
      var carePush = new CareRelativeDistanceStep(
        CareDistanceDirection.Away, holdSeconds: 0.1f);
      Assert.That(carePush.Advance(resetAwayScale, save.carePushReferenceScale, 0.1f, true, true), Is.False,
        "The distance reset scale cannot be reused as the care collection origin.");
      var careFarScale = save.carePushReferenceScale * FarStepScale;
      Assert.That(carePush.Advance(
        careFarScale, save.carePushReferenceScale, 0.1f, true, true), Is.True);

      var care = new CareStationExperienceLedger();
      care.Begin(save.pendingIncidentXP);
      care.RecordArrival(save.pendingIncidentXP - 1);
      Assert.That(care.IsComplete, Is.False);
      care.RecordArrival(1);
      storageValue += care.CollectedValue;
      save.carePushAwayCompletion = CareStationPushAwayCompletion.SensorCompleted;
      var careCloser = new CareRelativeDistanceStep(
        CareDistanceDirection.Closer, holdSeconds: 0.1f);
      Assert.That(careCloser.Advance(
        save.carePushReferenceScale, careFarScale, 0.1f, true, true), Is.True);
      save.carePushReferenceScale = 0f;
      save.carePushReferenceValid = false;
      save.currentState = CareStationState.AutoShift;
      save.careShiftCompleted = true;
      save.endShiftConsumed = true;

      Assert.That(storageValue, Is.EqualTo(CareStationShiftRules.IncidentExperience(CareStationIncidentType.Dust)));
      Assert.That(CareStationShiftRules.TryBeginNextShift(save, true), Is.True);
      Assert.That(save.careShiftId, Is.EqualTo(2));
      Assert.That(save.currentShift, Is.EqualTo(2));
      yield return null;
    }

    [UnityTest]
    public IEnumerator PushTimelineUsesAwayThenNewCloserOrigin()
    {
      const float referenceScale = 0.12f;
      const float awayScale = referenceScale * FarStepScale;
      var away = new CareRelativeDistanceStep(
        CareDistanceDirection.Away, holdSeconds: 0.1f);

      Assert.That(away.Advance(awayScale, referenceScale, 0.1f, true, true), Is.True);
      Assert.That(referenceScale, Is.EqualTo(0.12f));

      var closer = new CareRelativeDistanceStep(
        CareDistanceDirection.Closer, holdSeconds: 0.2f);
      Assert.That(closer.Advance(referenceScale, awayScale, 0.1f, true, true), Is.False);
      Assert.That(closer.Advance(referenceScale, awayScale, 1f, true, false), Is.False,
        "Render frames without a fresh camera sample must not manufacture stable time.");
      Assert.That(closer.Advance(referenceScale, awayScale, 0.1f, true, true), Is.True);
      Assert.That(closer.Advance(referenceScale, awayScale, 1f, true, true), Is.False,
        "The same closer completion cannot be emitted twice.");
      yield return null;
    }

    [UnityTest]
    public IEnumerator OfflineRewardAutoStoresBeforeIndependentDistanceResetAndCareCollection()
    {
      var save = new CareStationSaveData
      {
        careShiftId = 3,
        currentShift = 3,
        pendingOfflineXP = 72,
        careEnergy = 72,
        offlineCollectionResolved = false,
      };
      save.storageHours = 48;
      var production = new CareStationProductionController();
      var settlement = production.Settle(save, save.pendingOfflineXP);
      save.pendingOfflineXP = 0;
      save.offlineCollectionResolved = true;
      Assert.That(settlement.ProducedStored, Is.EqualTo(48));
      Assert.That(settlement.ProducedDiscarded, Is.Zero);
      Assert.That(save.storedFullBottles, Is.EqualTo(48));
      Assert.That(save.careEnergy, Is.EqualTo(24),
        "Storage Full preserves production quota for a later Cart cycle.");
      Assert.That(save.activeCollectionPhase, Is.EqualTo(CareStationCollectionPhase.None));
      save.returnedNeutralAfterOffline = true;
      save.careActionCompleted = true;
      Assert.That(CareStationStateRules.CanArmCollection(CareStationCollectionPhase.Care, true, true), Is.True);
      Assert.That(save.carePushAwayCompletion, Is.EqualTo(CareStationPushAwayCompletion.None));
      yield return null;
    }

    [UnityTest]
    public IEnumerator WorkshopStationVisualPassHidesLegacyWorkers()
    {
      var root = new GameObject("Care Station Graybox Test");
      try
      {
        var view = root.AddComponent<CareStationView>();
        view.Build();
        view.ApplyStation(new CareStationSaveData { workerLevel = 2, storageLevel = 2, cartLevel = 3, crewCount = 3, storageHours = 36, cartCapacity = 8 });
        view.ShowIncident(CareStationIncidentType.EyeGunk, true);
        view.SetPendingXp(36, 1);
        view.ShowBottleProduction(36, 1);
        view.ShowSendXp(36, true);
        yield return null;

        var workers = root.GetComponentsInChildren<CareStationWorkerArtView>(true);
        Assert.That(workers, Is.Empty,
          "The Station workshop pass reserves handoff anchors but must not render the retired droplet workers.");
        Assert.That(root.GetComponentsInChildren<CareCrewArtView>(true), Is.Empty,
          "The formal station view must not fall back to the retired role-based character art.");
        Assert.That(root.GetComponentsInChildren<MonoBehaviour>(true)
          .Any(item => item != null && item.GetType().Name == "CareCrewPlaceholderView"), Is.False,
          "The formal station view must not fall back to the retired graybox Worker.");
        var cart = root.GetComponentsInChildren<Transform>(true).First(item => item.name == "Bottle Cart");
        Assert.That(cart.localScale.x, Is.GreaterThan(1f));
        Assert.That(root.GetComponentsInChildren<Transform>(true).Any(item => item.name == "Care Core Platform"), Is.False);
        Assert.That(root.GetComponentsInChildren<Transform>(true).Any(item => item.name == "Worker Bottle Pickup Anchor"), Is.True);
        var visibleText = root.GetComponentsInChildren<Component>(true)
          .Where(item => item != null && item.GetType().FullName == "TMPro.TextMeshProUGUI")
          .Select(item => item.GetType().GetProperty("text")?.GetValue(item) as string ?? string.Empty)
          .ToArray();
        Assert.That(visibleText.Any(item => item.Contains("XP")), Is.False,
          "Care Station player-facing text must use bottle terminology.");
        Assert.That(visibleText.Any(item => item.Any(character => character >= '\u4e00' && character <= '\u9fff')), Is.False,
          "Care Station player-facing text must remain English.");
      }
      finally
      {
        Object.Destroy(root);
      }
      yield return null;
    }

    [UnityTest]
    public IEnumerator UnifiedCareActionLibraryCompletesWithoutChangingStationResources()
    {
      var station = new CareStationSaveData
      {
        careShiftId = 4,
        pendingOfflineXP = 1,
        pendingIncidentXP = 36,
        collectedExperienceCount = 7,
      };
      var configuration = CareActionConfiguration.Default;
      var types = new[]
      {
        CareActionType.ClosedEyeRest,
        CareActionType.FocusShift,
        CareActionType.GuidedEyeCircles,
        CareActionType.PilotEyeRoutine,
      };
      foreach (var type in types)
      {
        var action = new CareActionRuntime();
        action.Begin(type, configuration);
        for (var i = 0; i < 32 && action.Stage != CareActionStage.Completed; i++)
          action.CompleteCurrentStepForDevelopment();
        Assert.That(action.Stage, Is.EqualTo(CareActionStage.Completed), type.ToString());
        Assert.That(action.TryConsumeCompletionSignal(), Is.True);
        Assert.That(action.TryConsumeCompletionSignal(), Is.False);
      }

      Assert.That(station.careShiftId, Is.EqualTo(4));
      Assert.That(station.pendingOfflineXP, Is.EqualTo(1));
      Assert.That(station.pendingIncidentXP, Is.EqualTo(36));
      Assert.That(station.collectedExperienceCount, Is.EqualTo(7));
      yield return null;
    }

    [UnityTest]
    public IEnumerator WorkerLevelsRemainEconomicDataWhileWorkshopWorkersStayHidden()
    {
      var root = new GameObject("Formal Worker Level Test");
      try
      {
        var save = new CareStationSaveData
        {
          workerLevel = 1,
          crewCount = 5,
          storageLevel = 3,
          storageHours = 48,
          cartLevel = 3,
          cartCapacity = 8,
          storedFullBottles = 17,
          storedGoldBottles = 2,
          pendingIncidentXP = 9,
        };
        var view = root.AddComponent<CareStationView>();
        view.Build();
        var workers = root.GetComponentsInChildren<CareStationWorkerArtView>(true)
          .OrderBy(item => item.name)
          .ToArray();
        Assert.That(workers, Is.Empty);

        view.ApplyStation(save);

        save.workerLevel = 2;
        view.ApplyStation(save);

        save.workerLevel = 3;
        view.ApplyStation(save);
        var handoffAnchors = new[]
        {
          "Worker Bottle Pickup Anchor",
          "Worker Packer Handoff Anchor",
          "Worker Storage Handoff Anchor",
        };
        Assert.That(handoffAnchors.All(anchorName => root.GetComponentsInChildren<Transform>(true)
          .Any(item => item.name == anchorName && item.gameObject.activeInHierarchy)), Is.True,
          "The visual pass keeps only the invisible Worker handoff interface anchors.");

        Assert.That(root.GetComponentsInChildren<CareCrewArtView>(true), Is.Empty);
        Assert.That(root.GetComponentsInChildren<MonoBehaviour>(true)
          .Any(item => item != null && item.GetType().Name == "CareCrewPlaceholderView"), Is.False);
        Assert.That(save.crewCount, Is.EqualTo(5), "Visual Worker tiers must not rewrite production crewCount.");
        Assert.That(save.storedFullBottles, Is.EqualTo(17));
        Assert.That(save.storedGoldBottles, Is.EqualTo(2));
        Assert.That(save.pendingIncidentXP, Is.EqualTo(9));
      }
      finally
      {
        Object.Destroy(root);
      }
      yield return null;
    }

    [UnityTest]
    public IEnumerator FocusShiftDevelopmentSequenceStillRequiresSixCompleteCycles()
    {
      var action = new CareActionRuntime();
      action.Begin(CareActionType.FocusShift, CareActionConfiguration.Default);
      for (var guard = 0; guard < 32 && action.Stage != CareActionStage.Completed; guard++)
        action.CompleteCurrentStepForDevelopment();

      Assert.That(action.Stage, Is.EqualTo(CareActionStage.Completed));
      Assert.That(action.Data.focusCycleCount, Is.EqualTo(6));
      Assert.That(action.Data.focusTargetStep, Is.EqualTo(12));
      Assert.That(action.TryConsumeCompletionSignal(), Is.True);
      Assert.That(action.TryConsumeCompletionSignal(), Is.False);
      yield return null;
    }

    [UnityTest]
    public IEnumerator GuidedAndPilotDevelopmentFlowsUseTheirFinalCadence()
    {
      var configuration = CareActionConfiguration.Default;
      var guided = new CareActionRuntime();
      guided.Begin(CareActionType.GuidedEyeCircles, configuration);
      guided.CompleteCurrentStepForDevelopment();
      Assert.That(guided.Data.guidedLapCount, Is.EqualTo(3));
      guided.CompleteCurrentStepForDevelopment();
      guided.CompleteCurrentStepForDevelopment();
      Assert.That(guided.Data.guidedLapCount, Is.EqualTo(3));
      Assert.That(guided.Phase, Is.EqualTo(CareActionInternalPhase.GuidedPromptClose));

      var pilot = new CareActionRuntime();
      pilot.Begin(CareActionType.PilotEyeRoutine, configuration);
      for (var axis = 0; axis < 4; axis++) pilot.CompleteCurrentStepForDevelopment();
      Assert.That(pilot.Phase, Is.EqualTo(CareActionInternalPhase.PilotTransition));
      Assert.That(pilot.Data.pilotCurrentAxis, Is.EqualTo(4));
      Assert.That(pilot.TryConsumeCompletionSignal(), Is.False);
      pilot.CompleteCurrentStepForDevelopment();
      Assert.That(pilot.Stage, Is.EqualTo(CareActionStage.Completed));
      yield return null;
    }

    [UnityTest]
    public IEnumerator PilotStepAdvancesOnlyToAdjacentGuidedWithoutProducingBottles()
    {
      var recipe = new CareRecipeSaveData
      {
        recipeId = "pilot_guided_playmode",
        recipeType = CareRecipeType.Triple,
        actionList = new[]
        {
          CareActionType.PilotEyeRoutine,
          CareActionType.GuidedEyeCircles,
          CareActionType.ClosedEyeRest,
        },
        createdShiftId = 24,
      };
      var runtime = new CareRecipeRuntime(recipe);
      var pilot = runtime.CompleteCurrentAction(CareActionType.PilotEyeRoutine);
      Assert.That(pilot.Accepted, Is.True);
      Assert.That(pilot.RecipeCompleted, Is.False);
      Assert.That(runtime.CurrentAction, Is.EqualTo(CareActionType.GuidedEyeCircles));
      Assert.That(runtime.TryConsumeForProduction(), Is.False);
      Assert.That(recipe.currentActionIndex, Is.EqualTo(1), "The UI derives STEP 2 / 3 from this persisted index.");
      yield return null;
    }

    [UnityTest]
    public IEnumerator PilotGrayboxUsesAxisLabelsAndKeepsPupilsInsideTheEyes()
    {
      var root = new GameObject("Pilot Safe Area Test");
      try
      {
        var view = root.AddComponent<CareStationView>();
        view.Build();
        var data = new CareActionSaveData
        {
          actionType = CareActionType.PilotEyeRoutine,
          internalPhase = CareActionInternalPhase.PilotVertical,
          stage = CareActionStage.Active,
          pilotCurrentAxis = 0,
          pilotCurrentRound = 0,
          pilotCurrentEndpoint = 1,
          pilotNormalizedMoveProgress = 0.25f,
        };
        view.RenderCareAction(CareActionType.PilotEyeRoutine, data.internalPhase,
          "LOOK UP AND DOWN", 0.1f, 1f, 0f, CareDistanceDirection.None, 0);
        view.RenderCareActionMotionData(data);
        yield return null;

        var visibleText = root.GetComponentsInChildren<Component>(true)
          .Where(item => item != null && item.GetType().FullName == "TMPro.TextMeshProUGUI")
          .Select(item => item.GetType().GetProperty("text")?.GetValue(item) as string ?? string.Empty)
          .ToArray();
        Assert.That(visibleText.Any(text => text.Contains("AXIS 1 / 4")), Is.True);
        Assert.That(visibleText.Any(text => text.Contains("DIRECTION 1 / 8")), Is.False);
        foreach (var pupil in root.GetComponentsInChildren<RectTransform>(true)
                   .Where(item => item.name.Contains("Pupil")))
          Assert.That(pupil.anchoredPosition.magnitude, Is.LessThanOrEqualTo(13.5f));
      }
      finally
      {
        Object.Destroy(root);
      }
      yield return null;
    }

    [UnityTest]
    public IEnumerator PilotFullscreenOverlayHidesStationAndUsesMostOfTheSafeArea()
    {
      var root = new GameObject("Fullscreen Pilot Layout Test");
      try
      {
        var view = root.AddComponent<CareStationView>();
        view.Build();
        var data = new CareActionSaveData
        {
          actionType = CareActionType.PilotEyeRoutine,
          internalPhase = CareActionInternalPhase.PilotVertical,
          stage = CareActionStage.Active,
          pilotCurrentAxis = 0,
          pilotCurrentRound = 1,
          pilotCurrentEndpoint = 1,
          pilotNormalizedMoveProgress = 0.25f,
        };
        view.RenderCareAction(data.actionType, data.internalPhase, "LOOK UP AND DOWN",
          0.25f, 1f, 0f, CareDistanceDirection.None, 0);
        view.RenderCareActionMotionData(data);
        Canvas.ForceUpdateCanvases();
        yield return null;

        var transforms = root.GetComponentsInChildren<RectTransform>(true);
        var safe = transforms.First(item => item.name == "Safe Area");
        var overlay = transforms.First(item => item.name == "EyeMovementGuidanceOverlay");
        var guide = transforms.First(item => item.name == "Fullscreen Eye Movement Guide");
        var head = transforms.First(item => item.name == "Worker Head Closeup");
        var eyes = transforms.Where(item => item.name == "Guidance Left Eye" || item.name == "Guidance Right Eye").ToArray();
        var endpoints = transforms.Where(item => item.name.StartsWith("Fullscreen Pilot Endpoint")).ToArray();
        var prompt = transforms.First(item => item.name == "Guidance Current Prompt");
        var navigation = transforms.First(item => item.name == "Station Navigation");
        var stationHud = transforms.First(item => item.name == "Station HUD");
        var routine = transforms.First(item => item.name == "Care Routine Dock");
        var transport = transforms.First(item => item.name == "Bottle Transport");
        var content = transforms.First(item => item.name == "Comfort Padded Content");
        var filterLabel = transforms.First(item => item.name == "FILTER Label");

        Assert.That(overlay.gameObject.activeSelf, Is.True);
        Assert.That(guide.rect.width / safe.rect.width, Is.GreaterThanOrEqualTo(0.76f));
        Assert.That(head.rect.width / safe.rect.width, Is.GreaterThanOrEqualTo(0.32f));
        Assert.That(eyes, Has.Length.EqualTo(2));
        Assert.That(eyes.All(eye => eye.rect.width / safe.rect.width >= 0.07f), Is.True);
        Assert.That(endpoints, Has.Length.EqualTo(8));
        Assert.That(endpoints.All(endpoint => IsInside(safe, endpoint)), Is.True);
        Assert.That(Overlaps(prompt, guide), Is.False, "The bottom prompt must remain below the full-size guide.");
        Assert.That(navigation.gameObject.activeSelf, Is.False);
        Assert.That(stationHud.gameObject.activeSelf, Is.False);
        Assert.That(routine.gameObject.activeSelf, Is.False);
        Assert.That(transport.gameObject.activeSelf, Is.False);
        Assert.That(filterLabel.gameObject.activeSelf, Is.False);
        Assert.That(root.GetComponentsInChildren<CareStationWorkerArtView>(true), Is.Empty);
        Assert.That(content.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
        Assert.That(VisibleText(root).Count(text => text == "LOOK UP AND DOWN"), Is.EqualTo(1));
      }
      finally
      {
        Object.Destroy(root);
      }
      yield return null;
    }

    [UnityTest]
    public IEnumerator PilotToGuidedKeepsOneFullscreenOverlayUntilReliableOpenCompletes()
    {
      var root = new GameObject("Pilot Guided Overlay Continuity Test");
      try
      {
        var view = root.AddComponent<CareStationView>();
        view.Build();
        var pilot = new CareActionSaveData
        {
          actionType = CareActionType.PilotEyeRoutine,
          internalPhase = CareActionInternalPhase.PilotTransition,
          stage = CareActionStage.Demonstrating,
          pilotCurrentAxis = 4,
          pilotCurrentRound = 0,
          phaseElapsedSeconds = 0.8f,
        };
        view.RenderCareAction(pilot.actionType, pilot.internalPhase, "AXES COMPLETE\nNEXT: SLOW CIRCLES",
          0.99f, 1f, 0f, CareDistanceDirection.None, 0);
        view.RenderCareActionMotionData(pilot);
        Canvas.ForceUpdateCanvases();
        yield return null;

        var transforms = root.GetComponentsInChildren<RectTransform>(true);
        var overlay = transforms.First(item => item.name == "EyeMovementGuidanceOverlay");
        var navigation = transforms.First(item => item.name == "Station Navigation");
        var recipe = new CareRecipeSaveData
        {
          recipeType = CareRecipeType.Triple,
          actionList = new[]
          {
            CareActionType.PilotEyeRoutine,
            CareActionType.GuidedEyeCircles,
            CareActionType.ClosedEyeRest,
          },
          currentActionIndex = 1,
          completedActionMask = 1,
        };
        view.ShowRecipeStepFeedback(recipe, CareActionType.PilotEyeRoutine);
        Assert.That(overlay.gameObject.activeSelf, Is.True);
        Assert.That(navigation.gameObject.activeSelf, Is.False);
        Assert.That(VisibleText(root).Any(text => text == "AXES COMPLETE"), Is.True);

        var guided = new CareActionSaveData
        {
          actionType = CareActionType.GuidedEyeCircles,
          internalPhase = CareActionInternalPhase.GuidedClockwise,
          stage = CareActionStage.Active,
          guidedLapCount = 1,
          guidedNormalizedProgress = 0.4f,
        };
        view.RenderCareAction(guided.actionType, guided.internalPhase, "CLOCKWISE",
          0.4f, 1f, 0f, CareDistanceDirection.None, 0);
        view.RenderCareActionMotionData(guided);
        Canvas.ForceUpdateCanvases();
        yield return null;
        var guide = transforms.First(item => item.name == "Fullscreen Eye Movement Guide");
        var circle = transforms.First(item => item.name == "Fullscreen Guided Circle");
        Assert.That(overlay.gameObject.activeSelf, Is.True);
        Assert.That(circle.rect.width / guide.rect.width, Is.GreaterThanOrEqualTo(0.8f));
        Assert.That(navigation.gameObject.activeSelf, Is.False);

        guided.internalPhase = CareActionInternalPhase.GuidedClosedRest;
        guided.stage = CareActionStage.Paused;
        guided.pauseReason = CareActionPauseReason.TrackingLost;
        view.RenderCareAction(guided.actionType, guided.internalPhase, "TRACKING LOST",
          0.82f, 1f, 0f, CareDistanceDirection.None, 0);
        view.RenderCareActionMotionData(guided);
        Assert.That(overlay.gameObject.activeSelf, Is.True);
        Assert.That(transforms.First(item => item.name == "Guided Closed Rest Breathing Ring").gameObject.activeSelf, Is.True);
        Assert.That(navigation.gameObject.activeSelf, Is.False,
          "Pause and tracking loss must not restore the Station surface.");

        recipe.currentActionIndex = 2;
        recipe.completedActionMask = 3;
        view.ShowRecipeStepFeedback(recipe, CareActionType.GuidedEyeCircles);
        yield return new WaitForSecondsRealtime(0.5f);
        Assert.That(overlay.gameObject.activeSelf, Is.False);
        Assert.That(navigation.gameObject.activeSelf, Is.True);
      }
      finally
      {
        Object.Destroy(root);
      }
      yield return null;
    }

    private static bool IsInside(RectTransform outer, RectTransform inner)
    {
      var outerCorners = new Vector3[4];
      var innerCorners = new Vector3[4];
      outer.GetWorldCorners(outerCorners);
      inner.GetWorldCorners(innerCorners);
      return innerCorners.All(corner => corner.x >= outerCorners[0].x - 0.5f &&
                                        corner.x <= outerCorners[2].x + 0.5f &&
                                        corner.y >= outerCorners[0].y - 0.5f &&
                                        corner.y <= outerCorners[2].y + 0.5f);
    }

    private static bool Overlaps(RectTransform first, RectTransform second)
    {
      var a = new Vector3[4];
      var b = new Vector3[4];
      first.GetWorldCorners(a);
      second.GetWorldCorners(b);
      return a[0].x < b[2].x && a[2].x > b[0].x && a[0].y < b[2].y && a[2].y > b[0].y;
    }

    private static string[] VisibleText(GameObject root)
    {
      return root.GetComponentsInChildren<Component>(false)
        .Where(item => item != null && item.GetType().FullName == "TMPro.TextMeshProUGUI")
        .Select(item => item.GetType().GetProperty("text")?.GetValue(item) as string ?? string.Empty)
        .ToArray();
    }

    [UnityTest]
    public IEnumerator RecipeOnlyUnlocksCareBottleProductionAfterItsFinalAction()
    {
      var save = new CareStationSaveData
      {
        careShiftId = 11,
        pendingIncidentXP = 36,
        returnedNeutralAfterOffline = true,
        currentRecipe = new CareRecipeSaveData
        {
          recipeId = "playmode_double_recipe",
          recipeSeed = 71,
          recipeType = CareRecipeType.Double,
          actionList = new[] { CareActionType.FocusShift, CareActionType.ClosedEyeRest },
          createdShiftId = 11,
        },
      };
      var runtime = new CareRecipeRuntime(save.currentRecipe);

      var first = runtime.CompleteCurrentAction(CareActionType.FocusShift);
      save.careActionCompleted = first.RecipeCompleted;
      Assert.That(first.Accepted, Is.True);
      Assert.That(save.careActionCompleted, Is.False);
      Assert.That(CareStationStateRules.CanArmCollection(CareStationCollectionPhase.Care, save.careActionCompleted, true), Is.False);
      Assert.That(runtime.TryConsumeCompletionSignal(), Is.False);

      var second = runtime.CompleteCurrentAction(CareActionType.ClosedEyeRest);
      save.careActionCompleted = second.RecipeCompleted;
      Assert.That(second.RecipeCompleted, Is.True);
      Assert.That(runtime.TryConsumeCompletionSignal(), Is.True);
      Assert.That(runtime.TryConsumeCompletionSignal(), Is.False);
      Assert.That(CareStationStateRules.CanArmCollection(CareStationCollectionPhase.Care, save.careActionCompleted, true), Is.True);
      Assert.That(runtime.TryConsumeForProduction(), Is.True);
      Assert.That(runtime.TryConsumeForProduction(), Is.False);
      yield return null;
    }

    [UnityTest]
    public IEnumerator RecipeViewShowsFormalRoutineAndPipelineProgressWithoutChangingResources()
    {
      var root = new GameObject("Care Recipe View Test");
      var save = new CareStationSaveData { pendingIncidentXP = 24, collectedExperienceCount = 3 };
      try
      {
        var view = root.AddComponent<CareStationView>();
        view.Build();
        var recipe = CareRecipeGenerator.CreateRoutine(CareRoutineId.FocusFlow, 3, 13);
        view.ConfigureRecipe(recipe);
        view.RestoreRecipePipeline(recipe);
        view.PlayRecipePipelineStep(0, recipe.ActionCount);
        yield return null;

        var visibleText = root.GetComponentsInChildren<Component>(true)
          .Where(item => item != null && item.GetType().FullName == "TMPro.TextMeshProUGUI")
          .Select(item => item.GetType().GetProperty("text")?.GetValue(item) as string ?? string.Empty)
          .ToArray();
        Assert.That(visibleText, Does.Contain("A · FOCUS FLOW"));
        Assert.That(visibleText, Does.Contain("STEP 1 / 3"));
        Assert.That(save.pendingIncidentXP, Is.EqualTo(24));
        Assert.That(save.collectedExperienceCount, Is.EqualTo(3));
      }
      finally
      {
        Object.Destroy(root);
      }
      yield return null;
    }

    [UnityTest]
    public IEnumerator CompletedStepShowsImmediateEnergyFeedbackMintProgressAndFinalTotal()
    {
      var root = new GameObject("Routine Step Reward Feedback Test");
      try
      {
        var view = root.AddComponent<CareStationView>();
        view.Build();
        var save = new CareStationSaveData { careEnergy = 4 };
        view.ApplyStation(save);
        var recipe = CareRecipeGenerator.CreateRoutine(CareRoutineId.FocusFlow, 4, 14);
        recipe.completedActionMask = 1;
        recipe.currentActionIndex = 1;

        view.ConfigureRecipe(recipe);
        view.ShowRecipeStepFeedback(recipe, CareActionType.FocusShift, 4);
        yield return null;

        var visibleText = root.GetComponentsInChildren<Component>(true)
          .Where(item => item != null && item.GetType().FullName == "TMPro.TextMeshProUGUI")
          .Select(item => item.GetType().GetProperty("text")?.GetValue(item) as string ?? string.Empty)
          .ToArray();
        Assert.That(visibleText, Does.Contain("+4 CARE ENERGY"));
        Assert.That(root.GetComponentsInChildren<Transform>(true)
          .Any(item => item.name.StartsWith("Care Energy Reward Particle")), Is.True);
        var completedDot = root.GetComponentsInChildren<Image>(true)
          .First(item => item.name == "Recipe Step Dot" && item.gameObject.activeSelf);
        Assert.That(completedDot.color.g, Is.GreaterThan(completedDot.color.r),
          "The completed slot uses the mint success color.");

        save.careEnergy = 12;
        recipe.completedActionMask = 0b111;
        recipe.currentActionIndex = 3;
        recipe.recipeCompleted = true;
        view.ApplyStation(save);
        view.ShowRecipeStepFeedback(recipe, CareActionType.ClosedEyeRest, 4);
        yield return null;
        visibleText = root.GetComponentsInChildren<Component>(true)
          .Where(item => item != null && item.GetType().FullName == "TMPro.TextMeshProUGUI")
          .Select(item => item.GetType().GetProperty("text")?.GetValue(item) as string ?? string.Empty)
          .ToArray();
        Assert.That(visibleText, Does.Contain("ROUTINE COMPLETE · 12 ENERGY"));
      }
      finally
      {
        Object.Destroy(root);
      }
      yield return null;
    }

    [UnityTest]
    public IEnumerator StationInspectionCompletesPilotFlowBeforeLevelOrRewardCanSettle()
    {
      var save = new CareStationSaveData
      {
        trainingProgress = 0,
        workerLevel = 2,
        storageLevel = 2,
        cartLevel = 2,
        currentState = CareStationState.AutoShift,
        careShiftCompleted = true,
        endShiftConsumed = true,
      };
      Assert.That(CareStationInspectionRules.CanSchedule(save), Is.True);
      save.currentRecipe = CareStationInspectionRules.CreateRecipe(5);
      var runtime = new CareRecipeRuntime(save.currentRecipe);
      foreach (var action in save.currentRecipe.actionList)
      {
        var result = runtime.CompleteCurrentAction(action);
        Assert.That(result.Accepted, Is.True);
        Assert.That(CareEconomyRules.TryGrantCompletedRecipeStep(
          save, result.CompletedStepIndex, out var granted), Is.True);
        Assert.That(granted, Is.EqualTo(4));
      }
      Assert.That(save.currentRecipe.recipeCompleted, Is.True);
      Assert.That(save.careEnergy, Is.EqualTo(12));
      Assert.That(save.stationLevel, Is.EqualTo(1),
        "Completing the Recipe data alone cannot bypass the inspected controller settlement.");
      Assert.That(runtime.TryConsumeForProduction(), Is.True);
      Assert.That(runtime.TryConsumeForProduction(), Is.False);

      save.pendingIncidentXP = 25;
      save.pendingGoldBottleCount = 1;
      var beforeStorage = CareStationStorageRules.Stored(save);
      yield return null;
      Assert.That(CareStationStorageRules.Stored(save), Is.EqualTo(beforeStorage));
      Assert.That(save.pendingIncidentXP, Is.EqualTo(25));
    }

    [UnityTest]
    public IEnumerator LostCareBundleRecoveryPreservesArrivalsAndCompletesOneRemainingFlight()
    {
      var save = new CareStationSaveData
      {
        currentState = CareStationState.CollectingCareBottles,
        activeCollectionPhase = CareStationCollectionPhase.Care,
        storageHours = 48,
        storedFullBottles = 15,
        pendingIncidentXP = 36,
        collectedCareBottleValue = 22,
        careActionCompleted = true,
        careCollectionReleased = true,
      };

      var restored = CareStationCollectionRecoveryRules.Plan(save, 14, 0);
      Assert.That(restored.StorageBlocked, Is.False);
      Assert.That(restored.MissingRuntimeValue, Is.EqualTo(14));
      var ledger = new CareStationExperienceLedger();
      ledger.Begin(restored.CollectibleValue);
      ledger.RecordArrival(4);
      save.storedFullBottles += 4;
      save.collectedCareBottleValue += 4;
      yield return null;

      var afterOneArrival = CareStationCollectionRecoveryRules.Plan(save, 10, 6);
      Assert.That(afterOneArrival.MissingRuntimeValue, Is.EqualTo(4),
        "Only the lost bundle value should be recreated after a partial flight.");
      ledger.Begin(afterOneArrival.CollectibleValue);
      ledger.RecordArrival(6);
      ledger.RecordArrival(4);
      ledger.RecordArrival(4);
      Assert.That(ledger.IsComplete, Is.True);
      Assert.That(ledger.Arrivals, Is.EqualTo(2),
        "A completion signal after the expected value must not settle twice.");
      Assert.That(save.storedFullBottles, Is.EqualTo(19),
        "Planning a recovery must never directly modify storage.");
    }

    [UnityTest]
    public IEnumerator RetiredScreenTaskAndDeveloperSkipAreNotAvailable()
    {
      var root = new GameObject("Screen Down Developer Skip Test");
      try
      {
        var view = root.AddComponent<CareStationView>();
        view.Build();
        var runner = root.AddComponent<CareActionRunner>();
        runner.Bind(null, view);
        Assert.That(runner.StartAction(CareActionType.ScreenDown), Is.False);
        yield return null;
        Assert.That(root.GetComponentsInChildren<Button>(true)
          .Any(item => item.name == "Skip Care Step"), Is.False);
      }
      finally
      {
        Object.Destroy(root);
      }
      yield return null;
    }

    [UnityTest]
    public IEnumerator ChangeStepConfirmationAndShiftEndUseExplicitButtons()
    {
      var root = new GameObject("Daily End And Change Step View Test");
      try
      {
        var view = root.AddComponent<CareStationView>();
        view.Build();
        var changeRequests = 0;
        var useRest = 0;
        var ended = 0;
        view.ChangeStepSelected += () => changeRequests++;
        view.UseRestSelected += () => useRest++;
        view.EndShiftSelected += () => ended++;

        view.RenderCareAction(
          CareActionType.FocusShift,
          CareActionInternalPhase.FocusNearOne,
          "MOVE CLOSER",
          0.2f,
          1f,
          0.2f,
          CareDistanceDirection.Closer,
          0);
        view.SetCareActionChangeAvailable(true);
        var change = root.GetComponentsInChildren<Button>(true).First(item => item.name == "Change Care Step");
        Assert.That(change.gameObject.activeInHierarchy, Is.True);
        change.onClick.Invoke();
        Assert.That(changeRequests, Is.EqualTo(1));
        view.ShowCareStepChangeConfirmation();
        var rest = root.GetComponentsInChildren<Button>(true).First(item => item.name == "Use Rest");
        Assert.That(rest.gameObject.activeInHierarchy, Is.True);
        rest.onClick.Invoke();
        Assert.That(useRest, Is.EqualTo(1));

        var save = new CareStationSaveData
        {
          shiftStoredFullBottles = 18,
          shiftStoredGoldBottles = 1,
          currentRecipe = new CareRecipeSaveData
          {
            actionList = new[] { CareActionType.PilotEyeRoutine, CareActionType.GuidedEyeCircles },
            completedActionMask = 3,
            currentActionIndex = 2,
            recipeCompleted = true,
          },
        };
        view.ShowShiftComplete(save);
        var end = root.GetComponentsInChildren<Button>(true).First(item => item.name == "End Shift");
        Assert.That(end.gameObject.activeInHierarchy, Is.True);
        end.onClick.Invoke();
        Assert.That(ended, Is.EqualTo(1));
        var visibleText = root.GetComponentsInChildren<Component>(true)
          .Where(item => item != null && item.GetType().FullName == "TMPro.TextMeshProUGUI")
          .Select(item => item.GetType().GetProperty("text")?.GetValue(item) as string ?? string.Empty)
          .ToArray();
        Assert.That(visibleText.Any(text => text.Contains("CARE ROUTINE COMPLETE")), Is.True);
        Assert.That(visibleText.Any(text => text.Contains("CARE ENERGY")), Is.True);
        Assert.That(visibleText.Any(text => text.Contains("FULL BOTTLES")), Is.False,
          "Routine completion reports the granted production quota, not a direct Bottle reward.");
      }
      finally
      {
        Object.Destroy(root);
      }
      yield return null;
    }

    [UnityTest]
    public IEnumerator EndedAutoShiftDoesNotCreateAnotherShiftWithoutOfflineGate()
    {
      var save = new CareStationSaveData
      {
        currentShift = 5,
        careShiftId = 20,
        currentState = CareStationState.AutoShift,
        careShiftCompleted = true,
        endShiftConsumed = true,
        autoShiftEntered = true,
      };
      Assert.That(CareStationShiftRules.TryBeginNextShift(save, false), Is.False);
      Assert.That(save.currentShift, Is.EqualTo(5));
      Assert.That(save.careShiftId, Is.EqualTo(20));
      yield return null;
    }

    [UnityTest]
    public IEnumerator CareChecksStartUnselectedAndUseOneTouchScreen()
    {
      var root = new GameObject("Care Check View Test");
      try
      {
        var view = root.AddComponent<CareStationView>();
        view.Build();
        view.ShowSubjectiveCheck(false, new CareSubjectiveScores());
        yield return null;

        var scoreValues = root.GetComponentsInChildren<Component>(true)
          .Where(item => item != null
            && item.name == "Score Value"
            && item.GetType().FullName == "TMPro.TextMeshProUGUI")
          .Select(item => item.GetType().GetProperty("text")?.GetValue(item) as string ?? string.Empty)
          .ToArray();
        var continueButton = root.GetComponentsInChildren<Button>(true)
          .First(item => item.name == "Continue Care Check");
        Assert.That(scoreValues.Length, Is.EqualTo(4));
        Assert.That(scoreValues, Is.All.EqualTo("--"));
        Assert.That(continueButton.interactable, Is.False);
      }
      finally
      {
        Object.Destroy(root);
      }
      yield return null;
    }

    [UnityTest]
    public IEnumerator DisabledUpgradeExplainsExactResourceShortfall()
    {
      var root = new GameObject("Upgrade Reason View Test");
      try
      {
        var view = root.AddComponent<CareStationView>();
        view.Build();
        view.ShowUpgrade(new CareStationSaveData
        {
          workerLevel = 3,
          storageLevel = 4,
          storageHours = 72,
          storedFullBottles = 35,
          storedGoldBottles = 1,
          coins = 0,
        });
        yield return null;

        var visibleText = root.GetComponentsInChildren<Component>(true)
          .Where(item => item != null && item.GetType().FullName == "TMPro.TextMeshProUGUI")
          .Select(item => item.GetType().GetProperty("text")?.GetValue(item) as string ?? string.Empty)
          .ToArray();
        Assert.That(visibleText.Any(text => text.Contains("NEED 50 COINS")), Is.True);
        Assert.That(view.IsUpgradeInteractable(CareStationUpgradeId.MoreWorkers), Is.False);
        var clicked = CareStationUpgradeId.None;
        view.UpgradeSelected += upgrade => clicked = upgrade;
        var unavailableCard = root.GetComponentsInChildren<Button>(true)
          .First(item => item.name == CareStationUpgradeId.MoreWorkers.ToString());
        Assert.That(unavailableCard.interactable, Is.True,
          "Unavailable cards remain clickable so they can explain the shortfall.");
        unavailableCard.onClick.Invoke();
        Assert.That(clicked, Is.EqualTo(CareStationUpgradeId.MoreWorkers));
        view.ShowUpgradeFeedback(CareStationUpgradeId.MoreWorkers, "NEED 50 COINS");
      }
      finally
      {
        Object.Destroy(root);
      }
      yield return null;
    }

    [UnityTest]
    public IEnumerator UpgradeOverlayKeepsNavigationAndBackToStationClickable()
    {
      var root = new GameObject("Upgrade Navigation Test");
      try
      {
        var view = root.AddComponent<CareStationView>();
        view.Build();
        view.ShowUpgrade(new CareStationSaveData
        {
          storageLevel = 2,
          storageHours = 36,
          storedFullBottles = 36,
          storedGoldBottles = 0,
          coins = 36,
          upgradeOffered = true,
        });
        yield return null;

        var buttons = root.GetComponentsInChildren<Button>(true);
        var upgradeText = root.GetComponentsInChildren<Component>(true)
          .Where(item => item != null && item.GetType().FullName == "TMPro.TextMeshProUGUI")
          .Select(item => item.GetType().GetProperty("text")?.GetValue(item) as string ?? string.Empty)
          .ToArray();
        Assert.That(upgradeText.Any(text => text.Contains("36 -> 48") && text.Contains("20 COINS")), Is.True);
        Assert.That(view.IsUpgradeInteractable(CareStationUpgradeId.LargerStorage), Is.True);
        var navigation = new[] { "STATION Tab", "UPGRADES Tab", "REPORTS Tab" };
        var selected = -1;
        var backed = false;
        view.NavigationSelected += index => selected = index;
        view.UpgradeBackSelected += () => backed = true;
        for (var index = 0; index < navigation.Length; index++)
        {
          var button = buttons.First(item => item.name == navigation[index]);
          Assert.That(button.interactable, Is.True, navigation[index]);
          button.onClick.Invoke();
          Assert.That(selected, Is.EqualTo(index));
        }

        var back = buttons.First(item => item.name == "Back To Station");
        Assert.That(back.interactable, Is.True);
        back.onClick.Invoke();
        Assert.That(backed, Is.True);
      }
      finally
      {
        Object.Destroy(root);
      }
      yield return null;
    }

    [UnityTest]
    public IEnumerator FullStorageStationStillShowsCareEntryAndUsableNavigation()
    {
      var root = new GameObject("Full Storage Care Entry Test");
      try
      {
        var view = root.AddComponent<CareStationView>();
        view.Build();
        view.ApplyStation(new CareStationSaveData
        {
          storageLevel = 2,
          storageHours = 36,
          storedFullBottles = 36,
          offlineProductionPausedByFullStorage = true,
        });
        view.ShowStationWorking();
        yield return null;

        var visibleText = root.GetComponentsInChildren<Component>(false)
          .Where(item => item != null && item.GetType().FullName == "TMPro.TextMeshProUGUI")
          .Select(item => item.GetType().GetProperty("text")?.GetValue(item) as string ?? string.Empty)
          .ToArray();
        Assert.That(visibleText.Any(text => text.Contains("START CARE")), Is.True);
        Assert.That(root.GetComponentsInChildren<Button>(true)
          .Count(item => item.name.EndsWith(" Tab") && item.interactable), Is.EqualTo(3));
      }
      finally
      {
        Object.Destroy(root);
      }
      yield return null;
    }

    [UnityTest]
    public IEnumerator FinalStationLayoutKeepsProductionAnimationVisualOnly()
    {
      var root = new GameObject("Final Station Layout Test");
      try
      {
        var save = new CareStationSaveData
        {
          workerLevel = 2,
          storageLevel = 3,
          cartLevel = 3,
          crewCount = 3,
          storageHours = 48,
          cartCapacity = 8,
          storedFullBottles = 15,
          storedGoldBottles = 1,
        };
        var view = root.AddComponent<CareStationView>();
        view.Build();
        view.ApplyStation(save);
        view.ShowAutoShift();
        yield return null;

        var transforms = root.GetComponentsInChildren<RectTransform>(true);
        var stage = transforms.First(item => item.name == "Station Stage");
        var routine = transforms.First(item => item.name == "Care Routine Dock");
        var navigation = transforms.First(item => item.name == "Station Navigation");
        var representative = transforms.First(item => item.name == "Representative Production Bottle");
        Assert.That(stage.anchorMin.y, Is.EqualTo(0.205f).Within(0.001f));
        Assert.That(stage.anchorMax.y, Is.EqualTo(0.90f).Within(0.001f));
        Assert.That(routine.anchorMin.y, Is.EqualTo(0.082f).Within(0.001f));
        Assert.That(routine.anchorMax.y, Is.EqualTo(0.19f).Within(0.001f));
        Assert.That(navigation.anchorMax.y, Is.EqualTo(0.069f).Within(0.001f));
        Assert.That(representative.gameObject.activeSelf, Is.False,
          "Opening Auto Shift is informational and cannot manufacture a production animation.");
        Assert.That(save.storedFullBottles, Is.EqualTo(15));
        Assert.That(save.storedGoldBottles, Is.EqualTo(1));
      }
      finally
      {
        Object.Destroy(root);
      }
    }

    [UnityTest]
    public IEnumerator FilterArtUsesStationLevelAndProductionStateWithoutChangingEconomy()
    {
      var root = new GameObject("FILTER Art Runtime Test");
      var previousTimeScale = Time.timeScale;
      try
      {
        var save = new CareStationSaveData
        {
          stationLevel = 1,
          crewCount = 2,
          storageHours = 48,
          storedFullBottles = 15,
          storedGoldBottles = 1,
          pendingIncidentXP = 11,
          pendingGoldBottleCount = 2,
          collectedExperienceCount = 7,
        };
        var view = root.AddComponent<CareStationView>();
        view.Build();
        view.ApplyStation(save);
        var filter = root.GetComponentInChildren<CareStationFilterArtView>(true);
        Assert.That(filter, Is.Not.Null);
        Assert.That(filter.Level, Is.EqualTo(1));
        Assert.That(filter.HitRect.raycastTarget, Is.False);
        var filterRect = filter.GetComponent<RectTransform>();
        Assert.That(filterRect.sizeDelta.x, Is.EqualTo(300f).Within(0.001f));
        Assert.That(filterRect.sizeDelta.y, Is.EqualTo(450f).Within(0.001f));
        Assert.That(filterRect.parent.localScale, Is.EqualTo(Vector3.one));
        Assert.That(filterRect.localScale, Is.EqualTo(Vector3.one));
        Assert.That(filter.Levels[0].root.localScale, Is.EqualTo(Vector3.one));
        Assert.That(filter.Levels[0].contentRoot.localScale, Is.EqualTo(Vector3.one));
        Assert.That(filter.Levels[0].baseImage.rectTransform.localScale, Is.EqualTo(Vector3.one));
        Assert.That(filter.Levels[0].root.GetComponentsInChildren<Image>(true)
          .All(image => image.rectTransform.localScale == Vector3.one), Is.True,
          "FILTER visual states must not resize raster layers with Transform scale.");
        Assert.That(filter.Levels[0].displayScale, Is.EqualTo(Vector2.one));
        Assert.That(filter.Levels[0].root.GetComponentsInChildren<Image>(true)
          .All(image => image.preserveAspect), Is.True,
          "Every authored FILTER layer must preserve its source aspect ratio.");
        Assert.That(filter.Levels[0].crankImage, Is.Null,
          "The approved Level 1 FILTER design does not contain a crank.");
        var levelOne = filter.Levels[0];
        Assert.That(levelOne.machineRoot, Is.Not.Null);
        Assert.That(levelOne.machineBaseImage, Is.Not.Null);
        Assert.That(levelOne.rawLiquidMask, Is.Not.Null);
        Assert.That(levelOne.rawLiquidBodyImage, Is.Not.Null);
        Assert.That(levelOne.rawLiquidSurfaceImage, Is.Not.Null);
        Assert.That(levelOne.filterBedImage, Is.Not.Null);
        Assert.That(levelOne.filterDripImage, Is.Not.Null);
        Assert.That(levelOne.outletFlowImage, Is.Not.Null);
        Assert.That(levelOne.bottleFillAnchor, Is.Not.Null);
        Assert.That(levelOne.bottleRoot, Is.Not.Null);
        Assert.That(levelOne.bottlePickupAnchor, Is.Not.Null);
        Assert.That(levelOne.bottleLiquidMask, Is.Not.Null);
        Assert.That(levelOne.bottleGlassImage, Is.Not.Null);
        Assert.That(levelOne.bottleLiquidBodyImage, Is.Not.Null);
        Assert.That(levelOne.bottleLiquidSurfaceImage, Is.Not.Null);
        Assert.That(levelOne.filterDripFrames, Has.Length.EqualTo(4));
        Assert.That(levelOne.outletFlowFrames, Has.Length.EqualTo(4));
        Assert.That(levelOne.machineRoot.parent, Is.SameAs(levelOne.contentRoot));
        Assert.That(levelOne.bottleFillAnchor.parent, Is.SameAs(levelOne.contentRoot));
        Assert.That(levelOne.bottleRoot.parent, Is.SameAs(levelOne.bottleFillAnchor));
        Assert.That(levelOne.bottleRoot.IsChildOf(levelOne.machineRoot), Is.False,
          "The bottle must never be baked into or parented under MachineRoot.");
        Assert.That(levelOne.bottlePickupAnchor.IsChildOf(levelOne.bottleRoot), Is.True);
        Assert.That(filter.Levels[0].root.GetComponentsInChildren<Image>(true)
          .All(image => !image.raycastTarget), Is.True,
          "Authored FILTER layers must not intercept Station input.");
        Assert.That(filter.Levels[0].normalizedHitBounds.width, Is.LessThan(1f));
        Assert.That(filter.Levels[0].normalizedHitBounds.height, Is.LessThan(1f));
        Assert.That(levelOne.filterDripImage.gameObject.activeSelf, Is.False);
        Assert.That(levelOne.outletFlowImage.gameObject.activeSelf, Is.False,
          "Idle Level 1 keeps both filtering stages disabled.");
        var levelOneBase = filter.Levels[0].baseImage.sprite;

        view.ShowAutoShift();
        yield return null;
        Assert.That(filter.Running, Is.False,
          "The FILTER animates only from a persisted production stage, never from the Auto Shift page itself.");
        Assert.That(levelOne.filterDripImage.gameObject.activeSelf ||
                    levelOne.outletFlowImage.gameObject.activeSelf, Is.False);
        filter.SetPipelineHighlighted(true);
        yield return null;
        Assert.That(filter.Levels[0].root.localScale, Is.EqualTo(Vector3.one));
        Assert.That(filter.Levels[0].contentRoot.localScale, Is.EqualTo(Vector3.one));
        Assert.That(filter.Levels[0].root.GetComponentsInChildren<Image>(true)
          .All(image => image.rectTransform.localScale == Vector3.one), Is.True,
          "Pipeline feedback may tint authored art but must not rescale its raster layers.");
        filter.SetPipelineHighlighted(false);
        filter.SetRunning(false);
        Assert.That(levelOne.filterDripImage.gameObject.activeSelf, Is.False);
        Assert.That(levelOne.outletFlowImage.gameObject.activeSelf, Is.False,
          "Returning to Idle stops the flow without changing the economy.");
        view.ShowStationWorking();
        yield return null;
        Assert.That(filter.isActiveAndEnabled, Is.True,
          "Real-time animation assertions must run while the Station FILTER view is visible.");
        // This test now drives explicit presentation states. Freeze the
        // representative logistics loop so it cannot overwrite them per-frame.
        view.enabled = false;
        filter.SetRunning(false);
        filter.SetProductionVisual(FilterProductionVisualState.Filtering, 0.05f);
        Assert.That(levelOne.filterDripImage.gameObject.activeSelf, Is.True,
          "Filtering begins with droplets from the filter bed into the funnel.");
        Assert.That(levelOne.outletFlowImage.gameObject.activeSelf, Is.False);
        filter.SetProductionVisual(FilterProductionVisualState.Filtering, 0.55f);
        Assert.That(filter.ProductionVisualState, Is.EqualTo(FilterProductionVisualState.Filtering));
        Assert.That(filter.ProductionVisualProgress, Is.EqualTo(0.55f).Within(0.001f));
        Assert.That(filter.Running, Is.True);
        Assert.That(levelOne.filterDripImage.gameObject.activeSelf, Is.False);
        Assert.That(levelOne.outletFlowImage.gameObject.activeSelf, Is.False,
          "The Station sends filtered liquid through its side hose instead of the retired downward Bottle outlet.");
        Assert.That(levelOne.bottleRoot.gameObject.activeSelf, Is.False,
          "A Bottle may not appear before the FILLER stage.");
        Assert.That(levelOne.rawLiquidMask.rectTransform.sizeDelta.y,
          Is.EqualTo(levelOne.rawLiquidMaxHeight * 0.45f).Within(0.01f));
        Assert.That(levelOne.bottleLiquidMask.rectTransform.sizeDelta.y,
          Is.Zero.Within(0.01f));
        Assert.That(levelOne.rawLiquidSurfaceImage.rectTransform.anchoredPosition.y,
          Is.EqualTo(levelOne.rawLiquidMaxHeight * 0.45f).Within(0.01f));
        Assert.That(levelOne.bottleLiquidSurfaceImage.rectTransform.anchoredPosition.y,
          Is.Zero.Within(0.01f));
        Assert.That(levelOne.rawLiquidBodyImage.rectTransform.localScale, Is.EqualTo(Vector3.one));
        Assert.That(levelOne.bottleLiquidBodyImage.rectTransform.localScale, Is.EqualTo(Vector3.one));
        Assert.That(filter.Levels[0].baseImage.sprite, Is.SameAs(levelOneBase));
        Assert.That(filter.Levels[0].root.localScale, Is.EqualTo(Vector3.one));
        Assert.That(filter.Levels[0].contentRoot.localScale, Is.EqualTo(Vector3.one));
        Assert.That(filter.Levels[0].baseImage.rectTransform.localScale, Is.EqualTo(Vector3.one));
        Assert.That(filter.Levels[0].root.GetComponentsInChildren<Image>(true)
          .All(image => image.rectTransform.localScale == Vector3.one), Is.True);
        filter.SetProductionVisual(FilterProductionVisualState.BottleComplete, 1f);
        Assert.That(filter.ProductionVisualState, Is.EqualTo(FilterProductionVisualState.BottleComplete));
        Assert.That(filter.ProductionVisualProgress, Is.EqualTo(1f));
        Assert.That(filter.Running, Is.False);
        Assert.That(levelOne.outletFlowImage.gameObject.activeSelf, Is.False,
          "FILTER completion remains bottle-free in the Station production line.");
        Assert.That(levelOne.bottleRoot.gameObject.activeSelf, Is.False);
        Assert.That(levelOne.rawLiquidBodyImage.gameObject.activeSelf, Is.False);
        Assert.That(levelOne.bottleLiquidMask.rectTransform.sizeDelta.y,
          Is.Zero.Within(0.01f));
        Assert.That(filter.Levels[0].baseImage.sprite, Is.SameAs(levelOneBase),
          "Idle, Filtering, and Bottle Complete must share the same high-resolution Base sprite.");
        Assert.That(filter.Levels[0].root.localScale, Is.EqualTo(Vector3.one));
        Assert.That(filter.Levels[0].contentRoot.localScale, Is.EqualTo(Vector3.one));
        Assert.That(filter.Levels[0].baseImage.rectTransform.localScale, Is.EqualTo(Vector3.one));
        Assert.That(filter.Levels[0].root.GetComponentsInChildren<Image>(true)
          .All(image => image.rectTransform.localScale == Vector3.one), Is.True);
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(0.7f);
        Assert.That(levelOne.outletFlowImage.gameObject.activeSelf, Is.False,
          "The retired downward FILTER outlet stays hidden even when timeScale is zero.");
        Time.timeScale = previousTimeScale;
        filter.SetProductionVisual(FilterProductionVisualState.Idle, 0f);
        Assert.That(levelOne.machineBaseImage.sprite, Is.SameAs(levelOneBase),
          "Post-collection Idle must not restore the retired Level 1 artwork.");
        Assert.That(levelOne.rawLiquidMask.rectTransform.sizeDelta.y,
          Is.EqualTo(levelOne.rawLiquidMaxHeight).Within(0.01f));
        Assert.That(levelOne.bottleLiquidBodyImage.gameObject.activeSelf, Is.False);

        var carryParent = new GameObject("Future Worker Carry Root", typeof(RectTransform))
          .GetComponent<RectTransform>();
        carryParent.SetParent(levelOne.contentRoot, false);
        levelOne.bottleRoot.SetParent(carryParent, false);
        Assert.That(levelOne.bottleRoot.parent, Is.SameAs(carryParent),
          "BottleRoot must be detachable from BottleFillAnchor for a future Worker pickup.");
        Assert.That(levelOne.bottlePickupAnchor.IsChildOf(levelOne.bottleRoot), Is.True);
        levelOne.bottleRoot.SetParent(levelOne.bottleFillAnchor, false);
        filter.SetHitTestEnabled(true);
        Assert.That(filter.HitRect.raycastTarget, Is.True);
        Assert.That(filter.Levels[0].root.GetComponentsInChildren<Image>(true)
          .All(image => !image.raycastTarget), Is.True);
        filter.SetHitTestEnabled(false);
        Assert.That(save.storedFullBottles, Is.EqualTo(15));
        Assert.That(save.storedGoldBottles, Is.EqualTo(1));
        Assert.That(save.pendingIncidentXP, Is.EqualTo(11));
        Assert.That(save.pendingGoldBottleCount, Is.EqualTo(2));
        Assert.That(save.collectedExperienceCount, Is.EqualTo(7));

        save.stationLevel = 2;
        view.ApplyStation(save);
        yield return new WaitForSecondsRealtime(0.35f);
        Assert.That(filter.Level, Is.EqualTo(1));

        save.stationLevel = 3;
        view.ApplyStation(save);
        yield return new WaitForSecondsRealtime(0.35f);
        Assert.That(filter.Level, Is.EqualTo(1));
        Assert.That(filter.Levels[2].brushImage, Is.Not.Null);
        Assert.That(filter.Levels[2].gaugeNeedleImage, Is.Not.Null);
        Assert.That(save.storedFullBottles, Is.EqualTo(15));
        Assert.That(save.storedGoldBottles, Is.EqualTo(1));
        Assert.That(save.pendingIncidentXP, Is.EqualTo(11));
        Assert.That(save.pendingGoldBottleCount, Is.EqualTo(2));
        Assert.That(save.collectedExperienceCount, Is.EqualTo(7));
      }
      finally
      {
        Time.timeScale = previousTimeScale;
        Object.Destroy(root);
      }
    }
  }
}
