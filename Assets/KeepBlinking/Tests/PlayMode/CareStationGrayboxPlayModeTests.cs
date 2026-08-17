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
        careActionCompleted = false,
        offlineCollectionResolved = false,
      };
      save.storageHours = 48;
      Assert.That(CareStationShiftRules.IncidentForShift(2), Is.EqualTo(CareStationIncidentType.DrySpot));
      var settlement = new CareStationProductionController().Settle(save, save.pendingOfflineXP);
      save.pendingOfflineXP = 0;
      Assert.That(settlement.ProducedStored, Is.EqualTo(48));
      Assert.That(settlement.ProducedDiscarded, Is.EqualTo(24));
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
        offlineCollectionResolved = false,
      };
      save.storageHours = 48;
      var production = new CareStationProductionController();
      var settlement = production.Settle(save, save.pendingOfflineXP);
      save.pendingOfflineXP = 0;
      save.offlineCollectionResolved = true;
      Assert.That(settlement.ProducedStored, Is.EqualTo(48));
      Assert.That(settlement.ProducedDiscarded, Is.EqualTo(24));
      Assert.That(save.storedFullBottles, Is.EqualTo(48));
      Assert.That(save.activeCollectionPhase, Is.EqualTo(CareStationCollectionPhase.None));
      save.returnedNeutralAfterOffline = true;
      save.careActionCompleted = true;
      Assert.That(CareStationStateRules.CanArmCollection(CareStationCollectionPhase.Care, true, true), Is.True);
      Assert.That(save.carePushAwayCompletion, Is.EqualTo(CareStationPushAwayCompletion.None));
      yield return null;
    }

    [UnityTest]
    public IEnumerator FormalStationViewUsesOnlyGrayboxPlaceholders()
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

        Assert.That(root.GetComponentsInChildren<CareCrewArtView>(true), Is.Empty,
          "The formal station view must not instantiate imported character art.");
        Assert.That(root.GetComponentsInChildren<Transform>(true).Count(item => item.name.StartsWith("Care Crew") && item.gameObject.activeSelf), Is.EqualTo(3));
        var cart = root.GetComponentsInChildren<Transform>(true).First(item => item.name == "Bottle Cart");
        Assert.That(cart.localScale.x, Is.GreaterThan(1.3f));
        Assert.That(root.GetComponentsInChildren<Transform>(true).Any(item => item.name == "Care Core Platform"), Is.True);
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
        CareActionType.ScreenDown,
        CareActionType.ClosedEyeRest,
        CareActionType.FocusShift,
        CareActionType.GuidedEyeCircles,
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
          actionList = new[] { CareActionType.ScreenDown, CareActionType.FocusShift },
          createdShiftId = 11,
        },
      };
      var runtime = new CareRecipeRuntime(save.currentRecipe);

      var first = runtime.CompleteCurrentAction(CareActionType.ScreenDown);
      save.careActionCompleted = first.RecipeCompleted;
      Assert.That(first.Accepted, Is.True);
      Assert.That(save.careActionCompleted, Is.False);
      Assert.That(CareStationStateRules.CanArmCollection(CareStationCollectionPhase.Care, save.careActionCompleted, true), Is.False);
      Assert.That(runtime.TryConsumeCompletionSignal(), Is.False);

      var second = runtime.CompleteCurrentAction(CareActionType.FocusShift);
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
    public IEnumerator RecipeViewShowsTrainingAndPipelineProgressWithoutChangingResources()
    {
      var root = new GameObject("Care Recipe View Test");
      var save = new CareStationSaveData { pendingIncidentXP = 24, collectedExperienceCount = 3 };
      try
      {
        var view = root.AddComponent<CareStationView>();
        view.Build();
        var recipe = CareRecipeGenerator.CreateTraining(2, 3, 13);
        view.ConfigureRecipe(recipe);
        view.RestoreRecipePipeline(recipe);
        view.PlayRecipePipelineStep(0, 1);
        yield return null;

        var visibleText = root.GetComponentsInChildren<Component>(true)
          .Where(item => item != null && item.GetType().FullName == "TMPro.TextMeshProUGUI")
          .Select(item => item.GetType().GetProperty("text")?.GetValue(item) as string ?? string.Empty)
          .ToArray();
        Assert.That(visibleText, Does.Contain("TRAINING 3 / 4"));
        Assert.That(visibleText, Does.Contain("STEP 1 / 1"));
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
    public IEnumerator StationInspectionCompletesFourActionsBeforeRewardCanSettle()
    {
      var save = new CareStationSaveData
      {
        trainingProgress = 4,
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
      }
      Assert.That(save.currentRecipe.recipeCompleted, Is.True);
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
    public IEnumerator SensorUnavailableScreenDownShowsClickableDeveloperSkip()
    {
      var root = new GameObject("Screen Down Developer Skip Test");
      try
      {
        var view = root.AddComponent<CareStationView>();
        view.Build();
        var runner = root.AddComponent<CareActionRunner>();
        runner.Bind(null, view);
        var completionCount = 0;
        runner.CareActionCompleted += _ => completionCount++;
        Assert.That(runner.StartAction(CareActionType.ScreenDown), Is.True);
        runner.CompleteCurrentStepForDevelopment();
        yield return null;
        yield return null;

        Assert.That(runner.PauseReason, Is.EqualTo(CareActionPauseReason.SensorUnavailable));
        var skip = root.GetComponentsInChildren<Button>(true)
          .First(item => item.name == "Skip Care Step");
        Assert.That(skip.gameObject.activeInHierarchy, Is.True);
        skip.onClick.Invoke();
        yield return null;

        Assert.That(runner.Stage, Is.EqualTo(CareActionStage.Completed));
        Assert.That(runner.SaveData.completionSource, Is.EqualTo(CareActionCompletionSource.DeveloperSkipped));
        Assert.That(runner.SaveData.CountsAsVerifiedCareAction, Is.False);
        Assert.That(completionCount, Is.EqualTo(1));
        skip.onClick.Invoke();
        Assert.That(completionCount, Is.EqualTo(1));
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
            actionList = new[] { CareActionType.ScreenDown, CareActionType.FocusShift },
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
        Assert.That(visibleText.Any(text => text.Contains("FULL BOTTLES  18")), Is.True);
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
        });
        yield return null;

        var visibleText = root.GetComponentsInChildren<Component>(true)
          .Where(item => item != null && item.GetType().FullName == "TMPro.TextMeshProUGUI")
          .Select(item => item.GetType().GetProperty("text")?.GetValue(item) as string ?? string.Empty)
          .ToArray();
        Assert.That(visibleText.Any(text => text.Contains("NEED 5 FULL + 1 GOLD")), Is.True);
        Assert.That(view.IsUpgradeInteractable(CareStationUpgradeId.MoreWorkers), Is.False);
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
        Assert.That(stage.anchorMin.y, Is.EqualTo(0.31f).Within(0.001f));
        Assert.That(stage.anchorMax.y, Is.EqualTo(0.88f).Within(0.001f));
        Assert.That(routine.anchorMin.y, Is.EqualTo(0.09f).Within(0.001f));
        Assert.That(routine.anchorMax.y, Is.EqualTo(0.29f).Within(0.001f));
        Assert.That(navigation.anchorMax.y, Is.EqualTo(0.075f).Within(0.001f));
        Assert.That(representative.gameObject.activeSelf, Is.True);
        Assert.That(save.storedFullBottles, Is.EqualTo(15));
        Assert.That(save.storedGoldBottles, Is.EqualTo(1));
      }
      finally
      {
        Object.Destroy(root);
      }
    }
  }
}
