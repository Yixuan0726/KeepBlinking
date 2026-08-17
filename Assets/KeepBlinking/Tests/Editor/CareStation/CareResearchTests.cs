using System;
using System.IO;
using System.Linq;
using KeepBlinking.CareStation;
using NUnit.Framework;

namespace KeepBlinking.Tests
{
  public sealed class CareResearchTests
  {
    private string _directory;

    [SetUp]
    public void SetUp()
    {
      _directory = Path.Combine(Path.GetTempPath(), "KeepBlinkingResearchTests", Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
      if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Test]
    public void UpgradeReasonReportsExactMissingResourcesAndUsesSharedGate()
    {
      var save = new CareStationSaveData
      {
        workerLevel = 3,
        storageLevel = 4,
        storageHours = 72,
        storedFullBottles = 35,
        storedGoldBottles = 1,
      };
      var configuration = new CareStationUpgradeConfiguration();

      var availability = CareStationShiftRules.EvaluateUpgrade(save, CareStationUpgradeId.MoreWorkers, configuration);

      Assert.That(availability.CanPurchase, Is.False);
      Assert.That(availability.PlayerReason, Is.EqualTo("NEED 5 FULL + 1 GOLD"));
      Assert.That(CareStationShiftRules.CanPurchaseUpgrade(save, CareStationUpgradeId.MoreWorkers, configuration), Is.False);
      Assert.That(CareStationShiftRules.TryPurchaseUpgrade(save, CareStationUpgradeId.MoreWorkers, configuration), Is.False);
    }

    [Test]
    public void UpgradeWhoseCostCannotFitShowsStorageReason()
    {
      var save = new CareStationSaveData
      {
        workerLevel = 3,
        storageLevel = 1,
        storedFullBottles = 24,
        storedGoldBottles = 0,
      };

      var availability = CareStationShiftRules.EvaluateUpgrade(
        save,
        CareStationUpgradeId.MoreWorkers,
        new CareStationUpgradeConfiguration());

      Assert.That(availability.Reason, Is.EqualTo(CareStationUpgradeAvailabilityReason.StorageCapacityTooSmall));
      Assert.That(availability.PlayerReason, Is.EqualTo("UPGRADE STORAGE FIRST"));
    }

    [Test]
    public void SubjectiveCheckHasNoDefaultAndSkipRemainsMissing()
    {
      var scores = new CareSubjectiveScores();
      Assert.That(scores.HasAllResponses, Is.False);
      Assert.That(scores.IsResolved, Is.False);

      scores.skipped = true;
      scores.Sanitize();

      Assert.That(scores.IsResolved, Is.True);
      Assert.That(scores.submitted, Is.False);
      Assert.That(new[] { scores.comfort, scores.dryness, scores.eyeStrain, scores.focusDifficulty },
        Is.All.EqualTo(-1));
    }

    [Test]
    public void ReportUsesRealDeltasAndNeutralWording()
    {
      var save = CompletedReportSave();
      var report = CareReportFormatter.Build(save);

      Assert.That(report, Does.Contain("COMFORT  5 -> 7"));
      Assert.That(report, Does.Contain("EYE STRAIN  3 -> 1"));
      Assert.That(report, Does.Contain("YOU REPORTED +2 COMFORT"));
      Assert.That(report, Does.Contain("YOU REPORTED LESS STRAIN"));
      Assert.That(CareReportFormatter.ContainsMedicalClaim(report), Is.False);
    }

    [Test]
    public void MissingCheckProducesNoFabricatedDelta()
    {
      var save = CompletedReportSave();
      save.preCareScores = new CareSubjectiveScores { skipped = true };
      var report = CareReportFormatter.Build(save);

      Assert.That(report, Does.Contain("COMFORT  NOT RECORDED"));
      Assert.That(CareReportFormatter.Delta(save.preCareScores, save.postCareScores, s => s.comfort), Is.Null);
      Assert.That(report, Does.Not.Contain("YOU REPORTED +"));
    }

    [Test]
    public void TrackingLossFallbackAndGuidedRoutineAreNotSensorSuccess()
    {
      var save = CompletedReportSave();
      var recorder = new CareResearchSessionRecorder(true, _directory);
      recorder.BeginOrResume(save);
      save.careAction = new CareActionSaveData
      {
        actionType = CareActionType.GuidedEyeCircles,
        stage = CareActionStage.Completed,
        completionSource = CareActionCompletionSource.SensorCompleted,
      };
      recorder.ObserveAction(save, 0.1f);
      save.careAction = new CareActionSaveData
      {
        actionType = CareActionType.ClosedEyeRest,
        stage = CareActionStage.Paused,
        pauseReason = CareActionPauseReason.TrackingLost,
      };
      recorder.ObserveAction(save, 0.2f);
      recorder.RecordPushCompleted(CareStationCollectionPhase.Care, CareStationPushAwayCompletion.FallbackCompleted);

      Assert.That(recorder.Data.eligibleSensorActions, Is.Zero, "Guided circles do not validate eye direction.");
      Assert.That(recorder.Data.sensorCompletedActions, Is.Zero);
      Assert.That(recorder.Data.trackingLostCount, Is.EqualTo(1));
      Assert.That(recorder.Data.pushAwaySensorCompleted, Is.Zero);
      Assert.That(recorder.Data.pushAwayFallbackCompleted, Is.EqualTo(1));
    }

    [Test]
    public void ReplacementAndDeveloperSkipAreRecordedSeparately()
    {
      var save = CompletedReportSave();
      var recorder = new CareResearchSessionRecorder(true, _directory);
      recorder.BeginOrResume(save);
      recorder.RecordStepChangeRequested(CareActionType.ScreenDown, CareActionPauseReason.SensorUnavailable);
      recorder.RecordStepReplacement(CareActionType.ScreenDown, CareActionType.ClosedEyeRest, CareActionPauseReason.SensorUnavailable);
      recorder.RecordDeveloperSkip(CareActionType.FocusShift);

      Assert.That(recorder.Data.stepsReplaced, Is.EqualTo(1));
      Assert.That(recorder.Data.developerSkips, Is.EqualTo(1));
      Assert.That(recorder.Data.events.Any(item => item.eventType == "CareStepChangeRequested"), Is.True);
      Assert.That(recorder.Data.events.Any(item => item.result == "Replaced:ClosedEyeRest"), Is.True);
      Assert.That(recorder.Data.events.Any(item => item.result == "DeveloperSkipped"), Is.True);
    }

    [Test]
    public void ResearchFilesArePrivateMissingAwareAndUpsertOneSession()
    {
      var save = CompletedReportSave();
      save.preCareScores = new CareSubjectiveScores { skipped = true };
      var recorder = new CareResearchSessionRecorder(true, _directory);
      recorder.BeginOrResume(save);
      Assert.That(recorder.Persist(save, false), Is.True);
      Assert.That(recorder.Persist(save, true), Is.True);

      var json = File.ReadAllText(Path.Combine(_directory, save.currentResearchSessionId + ".json"));
      var csv = File.ReadAllLines(Path.Combine(_directory, CareResearchSessionRecorder.SummaryFileName));
      Assert.That(json, Does.Contain("\"comfort\":null"));
      Assert.That(json.ToLowerInvariant(), Does.Not.Contain("landmark"));
      Assert.That(json.ToLowerInvariant(), Does.Not.Contain("raw_gaze"));
      Assert.That(json.ToLowerInvariant(), Does.Not.Contain("email"));
      Assert.That(json.ToLowerInvariant(), Does.Not.Contain("photo"));
      Assert.That(json.ToLowerInvariant(), Does.Not.Contain("video"));
      Assert.That(csv.Length, Is.EqualTo(2), "Repeated persistence must upsert one row for the session.");
      Assert.That(csv[1].Split(',').Length, Is.GreaterThan(20));
    }

    [Test]
    public void ResearchModeOffWritesNoFiles()
    {
      var save = CompletedReportSave();
      var recorder = new CareResearchSessionRecorder(false, _directory);
      recorder.BeginOrResume(save);

      Assert.That(recorder.Persist(save, true), Is.False);
      Assert.That(Directory.Exists(_directory), Is.False);
    }

    [Test]
    public void SessionResumeKeepsIdentityAndCompletedFlag()
    {
      var save = CompletedReportSave();
      var first = new CareResearchSessionRecorder(true, _directory);
      first.BeginOrResume(save);
      var id = save.currentResearchSessionId;
      first.Persist(save, true);

      var resumed = new CareResearchSessionRecorder(true, _directory);
      resumed.BeginOrResume(save);
      resumed.Persist(save, false);

      Assert.That(save.currentResearchSessionId, Is.EqualTo(id));
      Assert.That(resumed.Data.completed, Is.True);
      Assert.That(File.ReadAllLines(Path.Combine(_directory, CareResearchSessionRecorder.SummaryFileName)).Length, Is.EqualTo(2));
    }

    [Test]
    public void VersionThirteenMigrationDoesNotInventSurveyAnswers()
    {
      var path = Path.Combine(_directory, "care_station.json");
      Directory.CreateDirectory(_directory);
      var legacy = CompletedReportSave();
      legacy.saveVersion = 13;
      legacy.preCareScores = new CareSubjectiveScores
      {
        comfort = 0, dryness = 0, eyeStrain = 0, focusDifficulty = 0, submitted = true,
      };
      legacy.postCareScores = legacy.preCareScores.Clone();
      File.WriteAllText(path, UnityEngine.JsonUtility.ToJson(legacy));

      var restored = new CareStationSaveService(path).Load(DateTime.UtcNow);

      Assert.That(restored.saveVersion, Is.EqualTo(CareStationSaveService.CurrentVersion));
      Assert.That(restored.preCareScores.IsResolved, Is.False);
      Assert.That(restored.postCareScores.IsResolved, Is.False);
      Assert.That(restored.preCareScores.comfort, Is.EqualTo(-1));
      Assert.That(restored.postCareScores.comfort, Is.EqualTo(-1));
    }

    private static CareStationSaveData CompletedReportSave()
    {
      var recipe = CareRecipeGenerator.CreateTraining(2, 12, 1003);
      recipe.completedActionMask = (1 << recipe.ActionCount) - 1;
      recipe.recipeCompleted = true;
      return new CareStationSaveData
      {
        saveVersion = CareStationSaveService.CurrentVersion,
        careShiftId = 12,
        currentRecipe = recipe,
        sessionActiveCareSeconds = 18f,
        sessionClosedEyeSeconds = 8f,
        sessionFocusShiftCompletions = 1,
        offlinePushAwayCompletion = CareStationPushAwayCompletion.SensorCompleted,
        carePushAwayCompletion = CareStationPushAwayCompletion.SensorCompleted,
        preCareScores = new CareSubjectiveScores
        {
          comfort = 5, dryness = 3, eyeStrain = 3, focusDifficulty = 2, submitted = true,
        },
        postCareScores = new CareSubjectiveScores
        {
          comfort = 7, dryness = 1, eyeStrain = 1, focusDifficulty = 1, submitted = true,
        },
      };
    }
  }
}
