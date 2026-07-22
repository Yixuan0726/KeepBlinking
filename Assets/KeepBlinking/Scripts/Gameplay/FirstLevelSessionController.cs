using System;
using System.Collections;
using KeepBlinking.Tutorial;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  public enum FirstLevelSessionState
  {
    WaitingForReadiness,
    PreComfortCheck,
    Tutorial,
    Gameplay,
    BossTransition,
    Boss,
    PostComfortCheck,
    SessionReport,
    Completed,
  }

  public sealed class FirstLevelSessionController : MonoBehaviour
  {
    [SerializeField, Min(0.1f)] private float _bossQuietTransitionSeconds = 1.5f;

    private EdgeOrbitHarvestMvp _gameplay;
    private KeepBlinkingTutorialController _tutorial;
    private DryCoreBossController _boss;
    private SessionComfortCheckController _comfortCheck;
    private SessionMetricsTracker _metrics;
    private SessionReportController _report;
    private Coroutine _bossTransitionRoutine;
    private Coroutine _bossCompletionRoutine;
    private FirstLevelSessionState _state = FirstLevelSessionState.WaitingForReadiness;
    private bool _initialized;
    private bool _preCheckShown;
    private bool _preCheckCompleted;

    public FirstLevelSessionState State => _state;
    public SessionMetricsTracker Metrics => _metrics;

    public event Action<FirstLevelSessionState, FirstLevelSessionState> StateChanged;
    public event Action FirstBossDefeated;
    public event Action SessionReportClosed;
    public event Action FirstLevelCompleted;

    public static FirstLevelSessionController EnsureExists(EdgeOrbitHarvestMvp gameplay)
    {
      if (gameplay == null)
      {
        return null;
      }

      var controller = gameplay.GetComponent<FirstLevelSessionController>();
      if (controller == null)
      {
        controller = gameplay.gameObject.AddComponent<FirstLevelSessionController>();
      }
      controller.Initialize(gameplay);
      return controller;
    }

    public void Initialize(EdgeOrbitHarvestMvp gameplay)
    {
      if (_initialized && _gameplay == gameplay)
      {
        return;
      }

      Unsubscribe();
      _gameplay = gameplay;
      _tutorial = FindFirstObjectByType<KeepBlinkingTutorialController>();
      _boss = GetOrAdd<DryCoreBossController>();
      _comfortCheck = GetOrAdd<SessionComfortCheckController>();
      _metrics = GetOrAdd<SessionMetricsTracker>();
      _report = GetOrAdd<SessionReportController>();

      _boss.Initialize(_gameplay);
      _comfortCheck.EnsureCreated();
      _metrics.Initialize(_gameplay, _boss);
      _report.Initialize(_gameplay, _metrics);
      _tutorial?.SetExternalStartBlocked(true);
      _gameplay.SetFirstLevelModalPaused(true, true);
      Subscribe();
      _initialized = true;

      if (_gameplay.IsTutorialReady)
      {
        HandleReadinessChanged(true);
      }
    }

    private T GetOrAdd<T>() where T : Component
    {
      var component = GetComponent<T>();
      return component != null ? component : gameObject.AddComponent<T>();
    }

    private void HandleReadinessChanged(bool ready)
    {
      if (!ready || _state != FirstLevelSessionState.WaitingForReadiness)
      {
        return;
      }

      if (!_preCheckShown)
      {
        _preCheckShown = true;
        _metrics.BeginSession();
        SetState(FirstLevelSessionState.PreComfortCheck);
        _gameplay.SetFirstLevelModalPaused(true, true);
        _comfortCheck.Show(ComfortCheckPhase.PreSession);
        return;
      }

      if (_preCheckCompleted)
      {
        ContinueAfterPreCheck();
      }
    }

    private void HandleComfortCheckCompleted(ComfortCheckPhase phase, ComfortScores? scores)
    {
      if ((phase == ComfortCheckPhase.PreSession && _state != FirstLevelSessionState.PreComfortCheck) ||
          (phase == ComfortCheckPhase.PostSession && _state != FirstLevelSessionState.PostComfortCheck))
      {
        Debug.LogWarning($"KeepBlinking ignored an out-of-state comfort result: {phase} while {_state}.", this);
        return;
      }

      _metrics.SetComfortScores(phase, scores);
      if (phase == ComfortCheckPhase.PreSession)
      {
        _preCheckCompleted = true;
        if (!_gameplay.IsTutorialReady)
        {
          SetState(FirstLevelSessionState.WaitingForReadiness);
          _gameplay.SetFirstLevelModalPaused(true, true);
          return;
        }

        ContinueAfterPreCheck();
        return;
      }

      SetState(FirstLevelSessionState.SessionReport);
      _report.ShowReport();
    }

    private void ContinueAfterPreCheck()
    {
      if (!_preCheckCompleted || !_gameplay.IsTutorialReady)
      {
        return;
      }

      _gameplay.SetFirstLevelModalPaused(false, false);
      _tutorial?.SetExternalStartBlocked(false);
      SetState(_tutorial != null && _tutorial.IsRunning
        ? FirstLevelSessionState.Tutorial
        : FirstLevelSessionState.Gameplay);
    }

    private void HandleTutorialStateChanged(KeepBlinkingTutorialState previous, KeepBlinkingTutorialState next)
    {
      if (next == KeepBlinkingTutorialState.Completed && _state == FirstLevelSessionState.Tutorial)
      {
        SetState(FirstLevelSessionState.Gameplay);
      }
    }

    private void HandleBuildCompleted()
    {
      TryBeginBossTransition("FirstLevelBuildCompleted");
    }

    private void Update()
    {
      if (_gameplay == null ||
          !_gameplay.IsFirstLevelBuildComplete ||
          _gameplay.InstalledFirstLevelModuleCount < _gameplay.UpgradesRequiredBeforeBoss)
      {
        return;
      }

      if (_state == FirstLevelSessionState.Gameplay ||
          (_state == FirstLevelSessionState.Tutorial && (_tutorial == null || !_tutorial.IsRunning)))
      {
        TryBeginBossTransition("state reconciliation");
      }
    }

    private void TryBeginBossTransition(string source)
    {
      if (_gameplay == null ||
          _state == FirstLevelSessionState.BossTransition ||
          _state == FirstLevelSessionState.Boss ||
          _state == FirstLevelSessionState.PostComfortCheck ||
          _state == FirstLevelSessionState.SessionReport ||
          _state == FirstLevelSessionState.Completed)
      {
        return;
      }

      if (_gameplay.InstalledFirstLevelModuleCount < _gameplay.UpgradesRequiredBeforeBoss)
      {
        Debug.LogWarning(
          $"KeepBlinking ignored an early build-complete signal at {_gameplay.InstalledFirstLevelModuleCount}/{_gameplay.UpgradesRequiredBeforeBoss} modules.",
          this);
        return;
      }

      var tutorialHasFinished = _state == FirstLevelSessionState.Tutorial &&
                                (_tutorial == null || !_tutorial.IsRunning);
      if (_state != FirstLevelSessionState.Gameplay && !tutorialHasFinished)
      {
        Debug.LogWarning(
          $"KeepBlinking deferred the Boss transition from {source} while the session was {_state}.",
          this);
        return;
      }

      SetState(FirstLevelSessionState.BossTransition);
      _gameplay.BeginFirstLevelBossTransition();
      Debug.Log(
        $"KeepBlinking Boss transition started from {source}. Pending converted samples: {_gameplay.PendingConvertedExperienceCount}.",
        this);
      if (_bossTransitionRoutine != null)
      {
        StopCoroutine(_bossTransitionRoutine);
      }
      _bossTransitionRoutine = StartCoroutine(BossTransitionRoutine());
    }

    private IEnumerator BossTransitionRoutine()
    {
      while (!_gameplay.IsFirstLevelFieldSettled || _gameplay.IsModuleInstallationPending)
      {
        yield return null;
      }

      var quietElapsed = 0f;
      while (quietElapsed < _bossQuietTransitionSeconds)
      {
        quietElapsed += Time.unscaledDeltaTime;
        yield return null;
      }

      _bossTransitionRoutine = null;
      SetState(FirstLevelSessionState.Boss);
      _boss.StartBoss();
    }

    private void HandleBossDefeated()
    {
      if (_state != FirstLevelSessionState.Boss)
      {
        return;
      }

      _metrics.EndSession();
      InvokeSignalSafely(FirstBossDefeated, nameof(FirstBossDefeated));
      if (_bossCompletionRoutine == null)
      {
        _bossCompletionRoutine = StartCoroutine(WaitForBossPresentationThenShowPostCheck());
      }
    }

    private IEnumerator WaitForBossPresentationThenShowPostCheck()
    {
      while (_boss != null && _boss.State != DryCoreBossState.Completed)
      {
        yield return null;
      }

      _bossCompletionRoutine = null;
      if (_state != FirstLevelSessionState.Boss)
      {
        yield break;
      }

      SetState(FirstLevelSessionState.PostComfortCheck);
      _gameplay.SetFirstLevelModalPaused(true, true);
      _comfortCheck.Show(ComfortCheckPhase.PostSession);
    }

    private void HandleReportClosed()
    {
      if (_state != FirstLevelSessionState.SessionReport)
      {
        return;
      }

      InvokeSignalSafely(SessionReportClosed, nameof(SessionReportClosed));
      SetState(FirstLevelSessionState.Completed);
      _gameplay.CompleteFirstLevelFlow();
      InvokeSignalSafely(FirstLevelCompleted, nameof(FirstLevelCompleted));
    }

    private void SetState(FirstLevelSessionState next)
    {
      if (_state == next)
      {
        return;
      }
      var previous = _state;
      _state = next;
      InvokeSignalSafely(StateChanged, previous, next, nameof(StateChanged));
    }

    private void InvokeSignalSafely(Action signal, string signalName)
    {
      if (signal == null)
      {
        return;
      }

      var handlers = signal.GetInvocationList();
      for (var i = 0; i < handlers.Length; i++)
      {
        try
        {
          ((Action)handlers[i]).Invoke();
        }
        catch (Exception exception)
        {
          Debug.LogError($"KeepBlinking first-level signal observer failed: {signalName}.", this);
          Debug.LogException(exception, this);
        }
      }
    }

    private void InvokeSignalSafely<TFirst, TSecond>(
      Action<TFirst, TSecond> signal,
      TFirst first,
      TSecond second,
      string signalName)
    {
      if (signal == null)
      {
        return;
      }

      var handlers = signal.GetInvocationList();
      for (var i = 0; i < handlers.Length; i++)
      {
        try
        {
          ((Action<TFirst, TSecond>)handlers[i]).Invoke(first, second);
        }
        catch (Exception exception)
        {
          Debug.LogError($"KeepBlinking first-level signal observer failed: {signalName}.", this);
          Debug.LogException(exception, this);
        }
      }
    }

    private void Subscribe()
    {
      if (_gameplay != null)
      {
        _gameplay.TutorialReadinessChanged += HandleReadinessChanged;
        _gameplay.FirstLevelBuildCompleted += HandleBuildCompleted;
      }
      if (_tutorial != null)
      {
        _tutorial.StateChanged += HandleTutorialStateChanged;
      }
      if (_boss != null)
      {
        _boss.FirstBossDefeated += HandleBossDefeated;
      }
      if (_comfortCheck != null)
      {
        _comfortCheck.Completed += HandleComfortCheckCompleted;
      }
      if (_report != null)
      {
        _report.SessionReportClosed += HandleReportClosed;
      }
    }

    private void Unsubscribe()
    {
      if (_gameplay != null)
      {
        _gameplay.TutorialReadinessChanged -= HandleReadinessChanged;
        _gameplay.FirstLevelBuildCompleted -= HandleBuildCompleted;
      }
      if (_tutorial != null)
      {
        _tutorial.StateChanged -= HandleTutorialStateChanged;
      }
      if (_boss != null)
      {
        _boss.FirstBossDefeated -= HandleBossDefeated;
      }
      if (_comfortCheck != null)
      {
        _comfortCheck.Completed -= HandleComfortCheckCompleted;
      }
      if (_report != null)
      {
        _report.SessionReportClosed -= HandleReportClosed;
      }
    }

    private void OnDestroy()
    {
      Unsubscribe();
      if (_bossTransitionRoutine != null)
      {
        StopCoroutine(_bossTransitionRoutine);
        _bossTransitionRoutine = null;
      }
      if (_bossCompletionRoutine != null)
      {
        StopCoroutine(_bossCompletionRoutine);
        _bossCompletionRoutine = null;
      }
      if (_tutorial != null)
      {
        _tutorial.SetExternalStartBlocked(false, false);
      }
      if (_gameplay != null && _state != FirstLevelSessionState.Completed)
      {
        _gameplay.ReleaseFirstLevelSessionPauses();
      }
    }
  }
}
