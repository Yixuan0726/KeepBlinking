using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  internal static class SessionReportExporter
  {
    internal static void TryExport(SessionReportData data)
    {
      if (data == null)
      {
        return;
      }

      try
      {
        var directory = Path.Combine(Application.persistentDataPath, "KeepBlinking", "Reports");
        Directory.CreateDirectory(directory);
        var safeSubject = SanitizeFilePart(data.SubjectId);
        var stem = $"{safeSubject}_session_{data.SessionIndex:00}_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        File.WriteAllText(Path.Combine(directory, stem + ".json"), BuildJson(data), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(directory, stem + ".csv"), BuildCsv(data), new UTF8Encoding(false));
      }
      catch (Exception exception)
      {
        Debug.LogWarning($"KeepBlinking report export failed without blocking completion: {exception.Message}");
      }
    }

    private static string BuildJson(SessionReportData data)
    {
      var builder = new StringBuilder(1024);
      builder.AppendLine("{");
      AppendJson(builder, "subjectId", data.SubjectId, true);
      AppendJson(builder, "sessionIndex", data.SessionIndex, true);
      AppendJson(builder, "totalSessions", data.TotalSessions, true);
      AppendJson(builder, "sessionStartUtc", data.SessionStartUtc.ToString("O", CultureInfo.InvariantCulture), true);
      AppendJson(builder, "sessionDurationSeconds", data.SessionDurationSeconds, true);
      AppendJson(builder, "softBlinkCount", data.SoftBlinkCount, true);
      AppendJson(builder, "validRestCycleCount", data.ValidRestCycleCount, true);
      AppendJson(builder, "distanceShiftCount", data.DistanceShiftCount, true);
      AppendJson(builder, "fullLoopCount", data.FullLoopCount, true);
      AppendJson(builder, "offScreenGazeBreakLabel", OffScreenEyeBreakController.ReportDisplayName, true);
      AppendJson(builder, "offScreenGazeBreakCount", data.OffScreenGazeBreakCount, true);
      AppendJson(builder, "earlyReopenCount", data.EarlyReopenCount, true);
      AppendJson(builder, "bossCyclesCompleted", data.BossCyclesCompleted, true);
      builder.Append("  \"selectedModuleIds\": [");
      builder.Append(string.Join(", ", data.SelectedModuleIds.Select(id => $"\"{Escape(id.ToString())}\"")));
      builder.AppendLine("],");
      AppendComfortJson(builder, "preComfortScores", data.PreComfortScores, true);
      AppendComfortJson(builder, "postComfortScores", data.PostComfortScores, true);
      AppendJson(builder, "buildVersion", data.BuildVersion, false);
      builder.AppendLine("}");
      return builder.ToString();
    }

    private static string BuildCsv(SessionReportData data)
    {
      var header = "subjectId,sessionIndex,totalSessions,sessionStartUtc,sessionDurationSeconds,softBlinkCount,validRestCycleCount,distanceShiftCount,fullLoopCount,offScreenGazeBreakCount,earlyReopenCount,bossCyclesCompleted,selectedModuleIds,preEyeStrain,preDryness,preVisualFatigue,postEyeStrain,postDryness,postVisualFatigue,buildVersion";
      var modules = string.Join("|", data.SelectedModuleIds.Select(id => id.ToString()));
      var pre = data.PreComfortScores;
      var post = data.PostComfortScores;
      var values = new[]
      {
        Csv(data.SubjectId),
        data.SessionIndex.ToString(CultureInfo.InvariantCulture),
        data.TotalSessions.ToString(CultureInfo.InvariantCulture),
        Csv(data.SessionStartUtc.ToString("O", CultureInfo.InvariantCulture)),
        data.SessionDurationSeconds.ToString("F2", CultureInfo.InvariantCulture),
        data.SoftBlinkCount.ToString(CultureInfo.InvariantCulture),
        data.ValidRestCycleCount.ToString(CultureInfo.InvariantCulture),
        data.DistanceShiftCount.ToString(CultureInfo.InvariantCulture),
        data.FullLoopCount.ToString(CultureInfo.InvariantCulture),
        data.OffScreenGazeBreakCount.ToString(CultureInfo.InvariantCulture),
        data.EarlyReopenCount.ToString(CultureInfo.InvariantCulture),
        data.BossCyclesCompleted.ToString(CultureInfo.InvariantCulture),
        Csv(modules),
        NullableScore(pre, score => score.EyeStrain),
        NullableScore(pre, score => score.Dryness),
        NullableScore(pre, score => score.VisualFatigue),
        NullableScore(post, score => score.EyeStrain),
        NullableScore(post, score => score.Dryness),
        NullableScore(post, score => score.VisualFatigue),
        Csv(data.BuildVersion),
      };
      return header + Environment.NewLine + string.Join(",", values) + Environment.NewLine;
    }

    private static void AppendComfortJson(StringBuilder builder, string name, ComfortScores? scores, bool comma)
    {
      if (!scores.HasValue)
      {
        builder.AppendLine($"  \"{name}\": null{(comma ? "," : string.Empty)}");
        return;
      }

      var value = scores.Value;
      builder.Append($"  \"{name}\": {{ \"eyeStrain\": {value.EyeStrain}, \"dryness\": {value.Dryness}, \"visualFatigue\": {value.VisualFatigue} }}");
      builder.AppendLine(comma ? "," : string.Empty);
    }

    private static void AppendJson(StringBuilder builder, string name, string value, bool comma)
    {
      builder.AppendLine($"  \"{name}\": \"{Escape(value)}\"{(comma ? "," : string.Empty)}");
    }

    private static void AppendJson(StringBuilder builder, string name, int value, bool comma)
    {
      builder.AppendLine($"  \"{name}\": {value.ToString(CultureInfo.InvariantCulture)}{(comma ? "," : string.Empty)}");
    }

    private static void AppendJson(StringBuilder builder, string name, float value, bool comma)
    {
      builder.AppendLine($"  \"{name}\": {value.ToString("F2", CultureInfo.InvariantCulture)}{(comma ? "," : string.Empty)}");
    }

    private static string NullableScore(ComfortScores? scores, Func<ComfortScores, int> selector)
    {
      return scores.HasValue ? selector(scores.Value).ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    private static string Escape(string value)
    {
      return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    }

    private static string Csv(string value)
    {
      return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    }

    private static string SanitizeFilePart(string value)
    {
      var invalid = Path.GetInvalidFileNameChars();
      return new string((value ?? "subject").Select(character => invalid.Contains(character) ? '_' : character).ToArray());
    }
  }
}
