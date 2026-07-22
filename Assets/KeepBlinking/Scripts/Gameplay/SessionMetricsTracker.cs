using System;
using System.Collections.Generic;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  public sealed class SessionMetricsTracker : MonoBehaviour
  {
    private enum LoopStage
    {
      WaitingForBlink,
      WaitingForRest,
      WaitingForDistance,
    }

    private readonly List<FirstLevelModuleId> _selectedModuleIds = new List<FirstLevelModuleId>(5);
    private EdgeOrbitHarvestMvp _gameplay;
    private DryCoreBossController _boss;
    private DateTime _sessionStartUtc;
    private float _sessionStartRealtime;
    private float _completedSessionDurationSeconds;
    private bool _sessionStarted;
    private bool _sessionEnded;
    private LoopStage _loopStage;

    public int SoftBlinkCount { get; private set; }
    public int ValidRestCycleCount { get; private set; }
    public int DistanceShiftCount { get; private set; }
    public int FullLoopCount { get; private set; }
    public int EarlyReopenCount { get; private set; }
    public int BossCyclesCompleted { get; private set; }
    public ComfortScores? PreComfortScores { get; private set; }
    public ComfortScores? PostComfortScores { get; private set; }
    public float SessionDurationSeconds => !_sessionStarted
      ? 0f
      : _sessionEnded
        ? _completedSessionDurationSeconds
        : Mathf.Max(0f, Time.realtimeSinceStartup - _sessionStartRealtime);

    public void Initialize(EdgeOrbitHarvestMvp gameplay, DryCoreBossController boss)
    {
      Unsubscribe();
      _gameplay = gameplay;
      _boss = boss;
      Subscribe();
    }

    public void BeginSession()
    {
      if (_sessionStarted)
      {
        return;
      }

      _sessionStarted = true;
      _sessionStartUtc = DateTime.UtcNow;
      _sessionStartRealtime = Time.realtimeSinceStartup;
      _completedSessionDurationSeconds = 0f;
      _sessionEnded = false;
      _loopStage = LoopStage.WaitingForBlink;
    }

    public void EndSession()
    {
      if (!_sessionStarted || _sessionEnded)
      {
        return;
      }

      _completedSessionDurationSeconds = Mathf.Max(0f, Time.realtimeSinceStartup - _sessionStartRealtime);
      _sessionEnded = true;
    }

    public void SetComfortScores(ComfortCheckPhase phase, ComfortScores? scores)
    {
      if (phase == ComfortCheckPhase.PreSession)
      {
        PreComfortScores = scores;
      }
      else
      {
        PostComfortScores = scores;
      }
    }

    public SessionReportData BuildReportData(string subjectId, int sessionIndex, int totalSessions)
    {
      return new SessionReportData(
        subjectId,
        sessionIndex,
        totalSessions,
        _sessionStarted ? _sessionStartUtc : DateTime.UtcNow,
        SessionDurationSeconds,
        SoftBlinkCount,
        ValidRestCycleCount,
        DistanceShiftCount,
        FullLoopCount,
        EarlyReopenCount,
        BossCyclesCompleted,
        _selectedModuleIds.ToArray(),
        PreComfortScores,
        PostComfortScores,
        Application.version);
    }

    private void Subscribe()
    {
      if (_gameplay != null)
      {
        _gameplay.NormalBlinkConversionCompleted += HandleNormalBlinkConversionCompleted;
        _gameplay.ReopenReleaseCompleted += HandleCrisisReleaseCompleted;
        _gameplay.CrisisReleaseInterrupted += HandleEarlyReopen;
        _gameplay.ConvertedCollectionStarted += HandleCollectionStarted;
        _gameplay.FirstLevelModuleInstalled += HandleModuleInstalled;
      }

      if (_boss != null)
      {
        _boss.BossCycleCompleted += HandleBossCycleCompleted;
        _boss.BossEarlyReopen += HandleEarlyReopen;
      }
    }

    private void Unsubscribe()
    {
      if (_gameplay != null)
      {
        _gameplay.NormalBlinkConversionCompleted -= HandleNormalBlinkConversionCompleted;
        _gameplay.ReopenReleaseCompleted -= HandleCrisisReleaseCompleted;
        _gameplay.CrisisReleaseInterrupted -= HandleEarlyReopen;
        _gameplay.ConvertedCollectionStarted -= HandleCollectionStarted;
        _gameplay.FirstLevelModuleInstalled -= HandleModuleInstalled;
      }

      if (_boss != null)
      {
        _boss.BossCycleCompleted -= HandleBossCycleCompleted;
        _boss.BossEarlyReopen -= HandleEarlyReopen;
      }
    }

    private void HandleNormalBlinkConversionCompleted(int targetId, int convertedCount)
    {
      if (!_sessionStarted || convertedCount <= 0)
      {
        return;
      }

      SoftBlinkCount++;
      _loopStage = LoopStage.WaitingForRest;
    }

    private void HandleCrisisReleaseCompleted(int convertedCount)
    {
      if (convertedCount > 0)
      {
        RecordValidRest();
      }
    }

    private void HandleBossCycleCompleted(int coreDamage)
    {
      if (coreDamage <= 0)
      {
        return;
      }

      BossCyclesCompleted++;
      RecordValidRest();
    }

    private void RecordValidRest()
    {
      if (!_sessionStarted)
      {
        return;
      }

      ValidRestCycleCount++;
      if (_loopStage == LoopStage.WaitingForRest)
      {
        _loopStage = LoopStage.WaitingForDistance;
      }
    }

    private void HandleEarlyReopen()
    {
      if (_sessionStarted)
      {
        EarlyReopenCount++;
      }
    }

    private void HandleCollectionStarted(int sampleCount)
    {
      if (!_sessionStarted || sampleCount <= 0)
      {
        return;
      }

      DistanceShiftCount++;
      if (_loopStage == LoopStage.WaitingForDistance)
      {
        FullLoopCount++;
        _loopStage = LoopStage.WaitingForBlink;
      }
    }

    private void HandleModuleInstalled(FirstLevelModuleId moduleId)
    {
      if (moduleId != FirstLevelModuleId.None && !_selectedModuleIds.Contains(moduleId))
      {
        _selectedModuleIds.Add(moduleId);
      }
    }

    private void OnDestroy()
    {
      Unsubscribe();
    }
  }
}
