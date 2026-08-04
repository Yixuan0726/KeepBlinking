using System;
using System.Collections.Generic;

namespace KeepBlinking.Gameplay
{
  public sealed class SessionReportData
  {
    private readonly FirstLevelModuleId[] _selectedModuleIds;

    public string SubjectId { get; }
    public int SessionIndex { get; }
    public int TotalSessions { get; }
    public DateTime SessionStartUtc { get; }
    public float SessionDurationSeconds { get; }
    public int SoftBlinkCount { get; }
    public int ValidRestCycleCount { get; }
    public int DistanceShiftCount { get; }
    public int FullLoopCount { get; }
    public int OffScreenGazeBreakCount { get; }
    public int EarlyReopenCount { get; }
    public int BossCyclesCompleted { get; }
    public IReadOnlyList<FirstLevelModuleId> SelectedModuleIds => _selectedModuleIds;
    public ComfortScores? PreComfortScores { get; }
    public ComfortScores? PostComfortScores { get; }
    public string BuildVersion { get; }

    public SessionReportData(
      string subjectId,
      int sessionIndex,
      int totalSessions,
      DateTime sessionStartUtc,
      float sessionDurationSeconds,
      int softBlinkCount,
      int validRestCycleCount,
      int distanceShiftCount,
      int fullLoopCount,
      int offScreenGazeBreakCount,
      int earlyReopenCount,
      int bossCyclesCompleted,
      FirstLevelModuleId[] selectedModuleIds,
      ComfortScores? preComfortScores,
      ComfortScores? postComfortScores,
      string buildVersion)
    {
      SubjectId = string.IsNullOrWhiteSpace(subjectId) ? "S-021" : subjectId;
      SessionIndex = Math.Max(1, sessionIndex);
      TotalSessions = Math.Max(1, totalSessions);
      SessionStartUtc = sessionStartUtc;
      SessionDurationSeconds = Math.Max(0f, sessionDurationSeconds);
      SoftBlinkCount = Math.Max(0, softBlinkCount);
      ValidRestCycleCount = Math.Max(0, validRestCycleCount);
      DistanceShiftCount = Math.Max(0, distanceShiftCount);
      FullLoopCount = Math.Max(0, fullLoopCount);
      OffScreenGazeBreakCount = Math.Max(0, offScreenGazeBreakCount);
      EarlyReopenCount = Math.Max(0, earlyReopenCount);
      BossCyclesCompleted = Math.Max(0, bossCyclesCompleted);
      _selectedModuleIds = selectedModuleIds == null ? Array.Empty<FirstLevelModuleId>() : (FirstLevelModuleId[])selectedModuleIds.Clone();
      PreComfortScores = preComfortScores;
      PostComfortScores = postComfortScores;
      BuildVersion = string.IsNullOrWhiteSpace(buildVersion) ? "unknown" : buildVersion;
    }
  }
}
