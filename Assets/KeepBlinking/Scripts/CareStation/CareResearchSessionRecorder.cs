using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KeepBlinking.CareStation
{
  [Serializable]
  public sealed class CareResearchEventData
  {
    public string eventType;
    public string occurredUtc;
    public string action;
    public string result;
    public string pauseReason;
    public float activeSeconds;
  }

  [Serializable]
  public sealed class CareResearchSessionData
  {
    public string sessionId;
    public string participantId;
    public string appVersion;
    public int saveVersion;
    public string devicePlatform;
    public string startedUtc;
    public string endedUtc;
    public bool completed;
    public int stationLevel;
    public string recipeId;
    public int recipeSeed;
    public string[] plannedActions = Array.Empty<string>();
    public string[] completedActions = Array.Empty<string>();
    public float activeCareSeconds;
    public float closedEyeSeconds;
    public int focusShiftCompletions;
    public int trackingLostCount;
    public float trackingLostSeconds;
    public int eyesOpenPauseCount;
    public int sensorUnavailableCount;
    public int stepsReplaced;
    public int developerSkips;
    public int eligibleSensorActions;
    public int sensorCompletedActions;
    public int fallbackCompletedActions;
    public int pushAwayAttempts;
    public int pushAwaySensorCompleted;
    public int pushAwayFallbackCompleted;
    public int pendingBottleCount;
    public int storedFullBottles;
    public int storedGoldBottles;
    public bool storageFull;
    public bool inspectionOccurred;
    public bool inspectionCompleted;
    public CareSubjectiveScores preScores = new CareSubjectiveScores();
    public CareSubjectiveScores postScores = new CareSubjectiveScores();
    public CareResearchEventData[] events = Array.Empty<CareResearchEventData>();
    public bool offlinePushStarted;
    public bool carePushStarted;
  }

  public static class CareReportFormatter
  {
    private static readonly string[] ForbiddenClaims =
    {
      "EYES HEALED",
      "VISION IMPROVED",
      "TREATMENT SUCCESSFUL",
      "DRY EYE CURED",
      "MEDICAL SCORE",
      "CLINICAL IMPROVEMENT",
    };

    public static string Build(CareStationSaveData save)
    {
      if (save == null) return "CARE REPORT";
      var recipe = save.currentRecipe;
      var actionCount = recipe?.ActionCount ?? 0;
      var completed = 0;
      var guidedCompleted = false;
      if (recipe != null)
      {
        for (var index = 0; index < recipe.ActionCount; index++)
        {
          if (!recipe.IsStepCompleted(index)) continue;
          completed++;
          if (recipe.actionList[index] == CareActionType.GuidedEyeCircles) guidedCompleted = true;
        }
      }

      var builder = new StringBuilder(512);
      builder.AppendLine("CARE REPORT");
      builder.AppendLine();
      builder.AppendLine("ROUTINE");
      builder.AppendLine($"STEPS COMPLETED  {completed} / {actionCount}");
      builder.AppendLine($"ACTIVE CARE  {FormatSeconds(save.sessionActiveCareSeconds)}");
      builder.AppendLine($"CLOSED-EYE REST  {FormatSeconds(save.sessionClosedEyeSeconds)}");
      builder.AppendLine($"FOCUS SHIFT  {Math.Max(0, save.sessionFocusShiftCompletions)}");
      var pushes = (save.offlinePushAwayCompletion != CareStationPushAwayCompletion.None ? 1 : 0) +
                   (save.carePushAwayCompletion != CareStationPushAwayCompletion.None ? 1 : 0);
      builder.AppendLine($"PUSH AWAY  {pushes} / 2");
      if (guidedCompleted) builder.AppendLine("GUIDED ROUTINE COMPLETED");
      builder.AppendLine();
      builder.AppendLine("HOW YOU FELT");
      builder.AppendLine("BEFORE  ->  AFTER");
      AppendScore(builder, "COMFORT", save.preCareScores, save.postCareScores, s => s.comfort);
      AppendScore(builder, "DRYNESS", save.preCareScores, save.postCareScores, s => s.dryness);
      AppendScore(builder, "EYE STRAIN", save.preCareScores, save.postCareScores, s => s.eyeStrain);
      AppendScore(builder, "FOCUS DIFFICULTY", save.preCareScores, save.postCareScores, s => s.focusDifficulty);
      AppendNeutralSummary(builder, save.preCareScores, save.postCareScores);
      return builder.ToString().TrimEnd();
    }

    public static bool ContainsMedicalClaim(string report)
    {
      var upper = (report ?? string.Empty).ToUpperInvariant();
      return ForbiddenClaims.Any(upper.Contains);
    }

    public static int? Delta(CareSubjectiveScores before, CareSubjectiveScores after, Func<CareSubjectiveScores, int> selector)
    {
      if (before == null || after == null || !before.submitted || !after.submitted ||
          !before.HasAllResponses || !after.HasAllResponses) return null;
      return selector(after) - selector(before);
    }

    private static void AppendScore(
      StringBuilder builder,
      string label,
      CareSubjectiveScores before,
      CareSubjectiveScores after,
      Func<CareSubjectiveScores, int> selector)
    {
      if (before == null || after == null || !before.submitted || !after.submitted ||
          !before.HasAllResponses || !after.HasAllResponses)
      {
        builder.AppendLine($"{label}  NOT RECORDED");
        return;
      }
      builder.AppendLine($"{label}  {selector(before)} -> {selector(after)}");
    }

    private static void AppendNeutralSummary(
      StringBuilder builder,
      CareSubjectiveScores before,
      CareSubjectiveScores after)
    {
      var comfort = Delta(before, after, s => s.comfort);
      var dryness = Delta(before, after, s => s.dryness);
      var strain = Delta(before, after, s => s.eyeStrain);
      var focus = Delta(before, after, s => s.focusDifficulty);
      if (!comfort.HasValue || !dryness.HasValue || !strain.HasValue || !focus.HasValue) return;
      if (comfort.Value > 0) builder.AppendLine($"YOU REPORTED +{comfort.Value} COMFORT");
      else if (comfort.Value < 0 || dryness.Value > 0 || strain.Value > 0 || focus.Value > 0)
        builder.AppendLine("YOU REPORTED MORE DISCOMFORT");
      if (strain.Value < 0) builder.AppendLine("YOU REPORTED LESS STRAIN");
      else if (strain.Value > 0) builder.AppendLine("YOU REPORTED MORE STRAIN");
      if (comfort.Value < 0 || dryness.Value > 0 || strain.Value > 0 || focus.Value > 0)
        builder.AppendLine("TAKE A BREAK IF NEEDED");
    }

    private static string FormatSeconds(float seconds)
    {
      var rounded = Math.Max(0, Mathf.RoundToInt(seconds));
      return $"{rounded / 60:00}:{rounded % 60:00}";
    }
  }

  /// <summary>
  /// Local-only research log. It stores numeric/enumerated care results and
  /// timestamps; it has no camera, landmark, gaze, account or network access.
  /// </summary>
  public sealed class CareResearchSessionRecorder
  {
    public const string SummaryFileName = "care_station_sessions.csv";
    private readonly bool _enabled;
    private readonly string _directory;
    private CareResearchSessionData _data;
    private CareActionType _lastAction;
    private CareActionStage _lastStage = CareActionStage.Cancelled;
    private CareActionPauseReason _lastPauseReason;

    public CareResearchSessionData Data => _data;
    public bool Enabled => _enabled;
    public string DirectoryPath => _directory;

    public CareResearchSessionRecorder(bool enabled, string directory = null)
    {
      _enabled = enabled;
      _directory = string.IsNullOrWhiteSpace(directory)
        ? Path.Combine(Application.persistentDataPath, "KeepBlinking", "Research")
        : directory;
    }

    public void BeginOrResume(CareStationSaveData save)
    {
      if (save == null) return;
      if (string.IsNullOrWhiteSpace(save.currentResearchSessionId))
        save.currentResearchSessionId = Guid.NewGuid().ToString("N");
      if (string.IsNullOrWhiteSpace(save.anonymousParticipantId))
        save.anonymousParticipantId = ReadOrCreateParticipantId();
      if (string.IsNullOrWhiteSpace(save.researchSessionStartedUtc))
        save.researchSessionStartedUtc = DateTime.UtcNow.ToString("O");

      var workPath = Path.Combine(_directory, save.currentResearchSessionId + ".resume");
      save.currentSessionEventRecordReference = _enabled ? workPath : string.Empty;
      if (_enabled && File.Exists(workPath))
      {
        try { _data = JsonUtility.FromJson<CareResearchSessionData>(File.ReadAllText(workPath)); }
        catch { _data = null; }
      }
      if (_data == null || !string.Equals(_data.sessionId, save.currentResearchSessionId, StringComparison.Ordinal))
      {
        _data = new CareResearchSessionData
        {
          sessionId = save.currentResearchSessionId,
          participantId = save.anonymousParticipantId,
          appVersion = Application.version,
          saveVersion = CareStationSaveService.CurrentVersion,
          devicePlatform = Application.platform.ToString(),
          startedUtc = save.researchSessionStartedUtc,
          stationLevel = Math.Max(1, save.stationLevel),
        };
      }
      SyncFromSave(save);
    }

    public void ObserveAction(CareStationSaveData save, float deltaSeconds)
    {
      if (_data == null || save?.careAction == null) return;
      var action = save.careAction.actionType;
      var stage = save.careAction.stage;
      var reason = save.careAction.pauseReason;
      var delta = Mathf.Clamp(deltaSeconds, 0f, 0.25f);
      if (action != CareActionType.None && (action != _lastAction || stage != _lastStage || reason != _lastPauseReason))
      {
        var eventType = stage == CareActionStage.Active && _lastStage == CareActionStage.Paused
          ? "ActionResumed"
          : stage == CareActionStage.Paused ? "ActionPaused"
          : stage == CareActionStage.Completed ? "ActionCompleted"
          : action != _lastAction ? "ActionStarted" : "ActionStateChanged";
        var result = stage == CareActionStage.Completed && action == CareActionType.GuidedEyeCircles
          ? "TimedGuidanceCompleted"
          : save.careAction.completionSource.ToString();
        AppendEvent(eventType, action, result, reason, save.careAction.elapsedSeconds);
        if (stage == CareActionStage.Paused && reason == CareActionPauseReason.TrackingLost &&
            _lastPauseReason != CareActionPauseReason.TrackingLost)
          _data.trackingLostCount++;
        if (stage == CareActionStage.Paused && reason == CareActionPauseReason.EyesOpen &&
            _lastPauseReason != CareActionPauseReason.EyesOpen)
          _data.eyesOpenPauseCount++;
        if (stage == CareActionStage.Paused && reason == CareActionPauseReason.SensorUnavailable &&
            _lastPauseReason != CareActionPauseReason.SensorUnavailable)
          _data.sensorUnavailableCount++;
        if (stage == CareActionStage.Completed && _lastStage != CareActionStage.Completed)
          RecordActionCompletion(save.careAction);
      }
      if (stage == CareActionStage.Active)
      {
        _data.activeCareSeconds += delta;
        save.sessionActiveCareSeconds += delta;
        if (action == CareActionType.ClosedEyeRest)
        {
          _data.closedEyeSeconds += delta;
          save.sessionClosedEyeSeconds += delta;
        }
      }
      if (stage == CareActionStage.Paused && reason == CareActionPauseReason.TrackingLost)
      {
        _data.trackingLostSeconds += delta;
        save.sessionTrackingLostSeconds += delta;
      }
      save.sessionTrackingLostCount = Math.Max(save.sessionTrackingLostCount, _data.trackingLostCount);
      _lastAction = action;
      _lastStage = stage;
      _lastPauseReason = reason;
    }

    public void RecordRecipe(CareStationSaveData save)
    {
      if (_data == null || save == null) return;
      SyncFromSave(save);
      AppendEvent("RecipePrepared", CareActionType.None, string.Empty, CareActionPauseReason.None, 0f);
    }

    public void RecordStepReplacement(CareActionType original, CareActionType replacement, CareActionPauseReason reason)
    {
      if (_data == null) return;
      _data.stepsReplaced++;
      AppendEvent("CareStepReplaced", original, "Replaced:" + replacement, reason, 0f);
    }

    public void RecordStepChangeRequested(CareActionType original, CareActionPauseReason reason)
    {
      if (_data == null) return;
      AppendEvent("CareStepChangeRequested", original, "Requested", reason, 0f);
    }

    public void RecordDeveloperSkip(CareActionType action)
    {
      if (_data == null) return;
      _data.developerSkips++;
      AppendEvent("CareStepCompleted", action, "DeveloperSkipped", CareActionPauseReason.None, 0f);
    }

    public void RecordPushStarted(CareStationCollectionPhase phase)
    {
      if (_data == null) return;
      if (phase == CareStationCollectionPhase.Offline)
      {
        if (_data.offlinePushStarted) return;
        _data.offlinePushStarted = true;
      }
      else
      {
        if (_data.carePushStarted) return;
        _data.carePushStarted = true;
      }
      _data.pushAwayAttempts++;
      AppendEvent("PushAwayStarted", CareActionType.None, phase.ToString(), CareActionPauseReason.None, 0f);
    }

    public void RecordPushCompleted(CareStationCollectionPhase phase, CareStationPushAwayCompletion completion)
    {
      if (_data == null) return;
      if (completion == CareStationPushAwayCompletion.SensorCompleted) _data.pushAwaySensorCompleted++;
      else if (completion == CareStationPushAwayCompletion.FallbackCompleted) _data.pushAwayFallbackCompleted++;
      AppendEvent("PushAwayCompleted", CareActionType.None, phase + ":" + completion, CareActionPauseReason.None, 0f);
    }

    public void RecordScores(string phase, CareSubjectiveScores scores)
    {
      if (_data == null) return;
      var copy = scores?.Clone() ?? new CareSubjectiveScores { skipped = true };
      if (phase == "Pre") _data.preScores = copy;
      else _data.postScores = copy;
      AppendEvent(phase + "CareCheck", CareActionType.None, copy.skipped ? "MissingResponse" : "Submitted", CareActionPauseReason.None, 0f);
    }

    public void SyncFromSave(CareStationSaveData save)
    {
      if (_data == null || save == null) return;
      _data.saveVersion = CareStationSaveService.CurrentVersion;
      _data.stationLevel = Math.Max(1, save.stationLevel);
      _data.recipeId = save.currentRecipe?.recipeId ?? string.Empty;
      _data.recipeSeed = save.currentRecipe?.recipeSeed ?? 0;
      _data.plannedActions = save.currentRecipe?.originalActionList?.Select(action => action.ToString()).ToArray()
        ?? save.currentRecipe?.actionList?.Select(action => action.ToString()).ToArray()
        ?? Array.Empty<string>();
      _data.preScores = save.preCareScores?.Clone() ?? new CareSubjectiveScores();
      _data.postScores = save.postCareScores?.Clone() ?? new CareSubjectiveScores();
      _data.activeCareSeconds = Math.Max(_data.activeCareSeconds, save.sessionActiveCareSeconds);
      _data.closedEyeSeconds = Math.Max(_data.closedEyeSeconds, save.sessionClosedEyeSeconds);
      _data.focusShiftCompletions = Math.Max(_data.focusShiftCompletions, save.sessionFocusShiftCompletions);
      _data.trackingLostCount = Math.Max(_data.trackingLostCount, save.sessionTrackingLostCount);
      _data.trackingLostSeconds = Math.Max(_data.trackingLostSeconds, save.sessionTrackingLostSeconds);
      _data.pendingBottleCount = Math.Max(0, save.pendingIncidentXP - save.collectedCareBottleValue);
      _data.storedFullBottles = Math.Max(0, save.shiftStoredFullBottles);
      _data.storedGoldBottles = Math.Max(0, save.shiftStoredGoldBottles);
      _data.storageFull |= save.offlineProductionPausedByFullStorage;
      _data.inspectionOccurred |= save.inspectionTriggered;
      _data.inspectionCompleted |= save.inspectionCompleted;
    }

    public bool Persist(CareStationSaveData save, bool completed)
    {
      if (!_enabled || _data == null || save == null) return false;
      SyncFromSave(save);
      _data.completed |= completed;
      if (completed && string.IsNullOrWhiteSpace(_data.endedUtc)) _data.endedUtc = DateTime.UtcNow.ToString("O");
      Directory.CreateDirectory(_directory);
      var workPath = Path.Combine(_directory, _data.sessionId + ".resume");
      var detailPath = Path.Combine(_directory, _data.sessionId + ".json");
      // The extension is deliberately not .json: each session exposes one
      // detailed JSON file while this private resume checkpoint prevents a
      // crash/reload from duplicating the session or its CSV row.
      AtomicWrite(workPath, JsonUtility.ToJson(_data, true));
      AtomicWrite(detailPath, BuildDetailedJson(_data));
      UpsertSummary(_data);
      save.currentSessionEventRecordReference = _data.completed ? detailPath : workPath;
      if (completed) save.researchSessionExported = true;
      return true;
    }

    public void ClearAll()
    {
      if (!_enabled || !Directory.Exists(_directory)) return;
      foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
    }

    private void RecordActionCompletion(CareActionSaveData action)
    {
      var name = action.actionType.ToString();
      if (!_data.completedActions.Contains(name))
        _data.completedActions = _data.completedActions.Concat(new[] { name }).ToArray();
      if (action.actionType == CareActionType.FocusShift) _data.focusShiftCompletions++;
      if (action.completionSource == CareActionCompletionSource.DeveloperSkipped)
      {
        RecordDeveloperSkip(action.actionType);
        return;
      }
      if (action.actionType != CareActionType.GuidedEyeCircles) _data.eligibleSensorActions++;
      if (action.completionSource == CareActionCompletionSource.SensorCompleted &&
          action.actionType != CareActionType.GuidedEyeCircles)
        _data.sensorCompletedActions++;
    }

    private void AppendEvent(
      string eventType,
      CareActionType action,
      string result,
      CareActionPauseReason reason,
      float activeSeconds)
    {
      var item = new CareResearchEventData
      {
        eventType = eventType,
        occurredUtc = DateTime.UtcNow.ToString("O"),
        action = action == CareActionType.None ? string.Empty : action.ToString(),
        result = result ?? string.Empty,
        pauseReason = reason == CareActionPauseReason.None ? string.Empty : reason.ToString(),
        activeSeconds = Math.Max(0f, activeSeconds),
      };
      _data.events = (_data.events ?? Array.Empty<CareResearchEventData>()).Concat(new[] { item }).ToArray();
    }

    private string ReadOrCreateParticipantId()
    {
      if (!_enabled) return Guid.NewGuid().ToString("N");
      Directory.CreateDirectory(_directory);
      var path = Path.Combine(_directory, "participant_id.txt");
      if (File.Exists(path))
      {
        var existing = File.ReadAllText(path).Trim();
        if (Guid.TryParse(existing, out _)) return existing;
      }
      var created = Guid.NewGuid().ToString("N");
      AtomicWrite(path, created);
      return created;
    }

    private void UpsertSummary(CareResearchSessionData data)
    {
      var path = Path.Combine(_directory, SummaryFileName);
      var header = "session_id,participant_id,started_utc,ended_utc,completed,station_level,recipe_length,actions_completed,active_care_seconds,closed_eye_seconds,tracking_lost_count,tracking_lost_seconds,steps_replaced,developer_skips,push_away_sensor_completed,push_away_fallback_completed,storage_full,pre_comfort,post_comfort,comfort_delta,pre_dryness,post_dryness,dryness_delta,pre_strain,post_strain,strain_delta,pre_focus_difficulty,post_focus_difficulty,focus_difficulty_delta";
      var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
      if (lines.Count == 0) lines.Add(header);
      else lines[0] = header;
      var row = BuildCsvRow(data);
      var prefix = Csv(data.sessionId) + ",";
      var replaced = false;
      for (var index = 1; index < lines.Count; index++)
      {
        if (!lines[index].StartsWith(prefix, StringComparison.Ordinal)) continue;
        lines[index] = row;
        replaced = true;
        break;
      }
      if (!replaced) lines.Add(row);
      AtomicWrite(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
    }

    private static string BuildCsvRow(CareResearchSessionData data)
    {
      var pre = data.preScores;
      var post = data.postScores;
      return string.Join(",", new[]
      {
        Csv(data.sessionId), Csv(data.participantId), Csv(data.startedUtc), Csv(data.endedUtc), data.completed ? "true" : "false",
        data.stationLevel.ToString(CultureInfo.InvariantCulture), (data.plannedActions?.Length ?? 0).ToString(CultureInfo.InvariantCulture),
        (data.completedActions?.Length ?? 0).ToString(CultureInfo.InvariantCulture), Number(data.activeCareSeconds), Number(data.closedEyeSeconds),
        data.trackingLostCount.ToString(CultureInfo.InvariantCulture), Number(data.trackingLostSeconds), data.stepsReplaced.ToString(CultureInfo.InvariantCulture),
        data.developerSkips.ToString(CultureInfo.InvariantCulture), data.pushAwaySensorCompleted.ToString(CultureInfo.InvariantCulture),
        data.pushAwayFallbackCompleted.ToString(CultureInfo.InvariantCulture), data.storageFull ? "true" : "false",
        Score(pre, s => s.comfort), Score(post, s => s.comfort), DeltaCsv(pre, post, s => s.comfort),
        Score(pre, s => s.dryness), Score(post, s => s.dryness), DeltaCsv(pre, post, s => s.dryness),
        Score(pre, s => s.eyeStrain), Score(post, s => s.eyeStrain), DeltaCsv(pre, post, s => s.eyeStrain),
        Score(pre, s => s.focusDifficulty), Score(post, s => s.focusDifficulty), DeltaCsv(pre, post, s => s.focusDifficulty),
      });
    }

    private static string BuildDetailedJson(CareResearchSessionData data)
    {
      var b = new StringBuilder(4096);
      b.Append("{\n");
      JsonString(b, "session_id", data.sessionId, true);
      JsonString(b, "participant_id", data.participantId, true);
      JsonString(b, "app_version", data.appVersion, true);
      JsonNumber(b, "save_version", data.saveVersion, true);
      JsonString(b, "device_platform", data.devicePlatform, true);
      JsonString(b, "started_utc", data.startedUtc, true);
      JsonString(b, "ended_utc", data.endedUtc, true);
      JsonBool(b, "completed", data.completed, true);
      JsonNumber(b, "station_level", data.stationLevel, true);
      JsonString(b, "recipe_id", data.recipeId, true);
      JsonNumber(b, "recipe_seed", data.recipeSeed, true);
      JsonArray(b, "planned_actions", data.plannedActions, true);
      JsonArray(b, "completed_actions", data.completedActions, true);
      JsonNumber(b, "active_care_seconds", data.activeCareSeconds, true);
      JsonNumber(b, "closed_eye_seconds", data.closedEyeSeconds, true);
      JsonNumber(b, "tracking_lost_count", data.trackingLostCount, true);
      JsonNumber(b, "tracking_lost_seconds", data.trackingLostSeconds, true);
      JsonNumber(b, "eligible_sensor_actions", data.eligibleSensorActions, true);
      JsonNumber(b, "sensor_completed", data.sensorCompletedActions, true);
      JsonNumber(b, "fallback_completed", data.fallbackCompletedActions, true);
      JsonNumber(b, "steps_replaced", data.stepsReplaced, true);
      JsonNumber(b, "developer_skipped", data.developerSkips, true);
      JsonNumber(b, "push_away_attempts", data.pushAwayAttempts, true);
      JsonNumber(b, "push_away_sensor_completed", data.pushAwaySensorCompleted, true);
      JsonNumber(b, "push_away_fallback_completed", data.pushAwayFallbackCompleted, true);
      JsonNumber(b, "pending_bottles", data.pendingBottleCount, true);
      JsonNumber(b, "stored_full_bottles", data.storedFullBottles, true);
      JsonNumber(b, "stored_gold_bottles", data.storedGoldBottles, true);
      JsonBool(b, "storage_full", data.storageFull, true);
      JsonBool(b, "inspection_occurred", data.inspectionOccurred, true);
      JsonBool(b, "inspection_completed", data.inspectionCompleted, true);
      JsonScores(b, "pre", data.preScores, true);
      JsonScores(b, "post", data.postScores, true);
      b.Append("  \"events\": [");
      var events = data.events ?? Array.Empty<CareResearchEventData>();
      for (var index = 0; index < events.Length; index++)
      {
        if (index > 0) b.Append(',');
        var e = events[index];
        b.Append("\n    {\"event_type\":").Append(Quoted(e.eventType))
          .Append(",\"occurred_utc\":").Append(Quoted(e.occurredUtc))
          .Append(",\"action\":").Append(Quoted(e.action))
          .Append(",\"result\":").Append(Quoted(e.result))
          .Append(",\"pause_reason\":").Append(Quoted(e.pauseReason))
          .Append(",\"active_seconds\":").Append(Number(e.activeSeconds)).Append('}');
      }
      if (events.Length > 0) b.Append('\n').Append("  ");
      b.Append("]\n}");
      return b.ToString();
    }

    private static void JsonScores(StringBuilder b, string name, CareSubjectiveScores scores, bool comma)
    {
      b.Append("  \"").Append(name).Append("\": {\"skipped\":")
        .Append(scores != null && scores.skipped ? "true" : "false").Append(",\"comfort\":")
        .Append(JsonScore(scores, s => s.comfort)).Append(",\"dryness\":")
        .Append(JsonScore(scores, s => s.dryness)).Append(",\"eye_strain\":")
        .Append(JsonScore(scores, s => s.eyeStrain)).Append(",\"focus_difficulty\":")
        .Append(JsonScore(scores, s => s.focusDifficulty)).Append('}').Append(comma ? ",\n" : "\n");
    }

    private static string JsonScore(CareSubjectiveScores scores, Func<CareSubjectiveScores, int> selector)
    {
      return scores != null && scores.submitted && scores.HasAllResponses
        ? selector(scores).ToString(CultureInfo.InvariantCulture)
        : "null";
    }

    private static void JsonString(StringBuilder b, string name, string value, bool comma) =>
      b.Append("  \"").Append(name).Append("\": ").Append(string.IsNullOrEmpty(value) ? "null" : Quoted(value)).Append(comma ? ",\n" : "\n");
    private static void JsonBool(StringBuilder b, string name, bool value, bool comma) =>
      b.Append("  \"").Append(name).Append("\": ").Append(value ? "true" : "false").Append(comma ? ",\n" : "\n");
    private static void JsonNumber(StringBuilder b, string name, int value, bool comma) =>
      b.Append("  \"").Append(name).Append("\": ").Append(value.ToString(CultureInfo.InvariantCulture)).Append(comma ? ",\n" : "\n");
    private static void JsonNumber(StringBuilder b, string name, float value, bool comma) =>
      b.Append("  \"").Append(name).Append("\": ").Append(Number(value)).Append(comma ? ",\n" : "\n");
    private static void JsonArray(StringBuilder b, string name, IEnumerable<string> values, bool comma) =>
      b.Append("  \"").Append(name).Append("\": [").Append(string.Join(",", (values ?? Array.Empty<string>()).Select(Quoted))).Append(']').Append(comma ? ",\n" : "\n");
    private static string Number(float value) => Math.Max(0f, value).ToString("0.###", CultureInfo.InvariantCulture);
    private static string Csv(string value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    private static string Score(CareSubjectiveScores scores, Func<CareSubjectiveScores, int> selector) =>
      scores != null && scores.submitted && scores.HasAllResponses ? selector(scores).ToString(CultureInfo.InvariantCulture) : string.Empty;
    private static string DeltaCsv(CareSubjectiveScores before, CareSubjectiveScores after, Func<CareSubjectiveScores, int> selector)
    {
      var delta = CareReportFormatter.Delta(before, after, selector);
      return delta.HasValue ? delta.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }
    private static string Quoted(string value) => "\"" + Escape(value ?? string.Empty) + "\"";
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");

    private static void AtomicWrite(string path, string contents)
    {
      var directory = Path.GetDirectoryName(path);
      if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
      var temporary = path + ".tmp";
      File.WriteAllText(temporary, contents ?? string.Empty, new UTF8Encoding(false));
      if (!File.Exists(path))
      {
        File.Move(temporary, path);
        return;
      }
      try { File.Replace(temporary, path, null); }
      catch
      {
        File.Copy(temporary, path, true);
        File.Delete(temporary);
      }
    }
  }
}
