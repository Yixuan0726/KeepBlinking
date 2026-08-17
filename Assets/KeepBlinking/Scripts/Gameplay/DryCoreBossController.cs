using System;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  public enum DryCoreBossState
  {
    Inactive,
    Entrance,
    WaitSoftBlink,
    PromptClose,
    WaitEyesClosed,
    ExpandingCoverage,
    ReadyToReopen,
    WaitReopen,
    HitFeedback,
    WaitPushAwayReady,
    WaitPushAway,
    WaitExperienceCollected,
    NextPhase,
    RetryDelay,
    Defeated,
    Completed,
  }

  public sealed class DryCoreBossController : MonoBehaviour
  {
    private enum DefeatedRewardState
    {
      None,
      HitFeedback,
      WaitPushAwayReady,
      WaitPushAway,
      WaitExperienceCollected,
      ObservationComplete,
    }

    [SerializeField, Min(0.01f)] private float _coverageGrowthScreenRatioPerSecond = 0.1f;
    [SerializeField, Min(5.5f)] private float _coverageSafetyLimitSeconds = 6f;
    [SerializeField, Min(0.1f)] private float _earlyReopenRetryDelaySeconds = 1.5f;
    [SerializeField, Min(0.2f)] private float _faceScreenPreparationSeconds = 1.2f;
    [SerializeField, Min(0.1f)] private float _hitFeedbackSeconds = 0.55f;
    [SerializeField, Min(0.1f)] private float _observationCompleteSeconds = 1f;

    private EdgeOrbitHarvestMvp _gameplay;
    private DryCoreBossView _view;
    private CoverageTargetGeometry[] _coverageTargets = Array.Empty<CoverageTargetGeometry>();
    private DryCoreBossState _state = DryCoreBossState.Inactive;
    private DefeatedRewardState _defeatedRewardState;
    private float _stateElapsed;
    private float _defeatedRewardElapsed;
    private float _coverageElapsed;
    private float _coverageRadiusPixels;
    private float _quietSecondsAfterRound;
    private float _faceScreenPreparationElapsed;
    private bool _coverageCuePlayed;
    private bool _trackingPaused;
    private bool _trackingResumedDuringCoverage;
    private bool _resolvingRestModules;
    private bool _dryCoreDefeatedEmitted;
    private bool _bossRewardCompletedEmitted;
    private bool _firstBossDefeatedEmitted;
    private int _pendingExtraCoreDamage;
    private int _remainingCores = 3;
    private int _completedCycles;
    private int _roundSerial;
    private int _currentRoundId;
    private int _expectedRoundSampleCount;
    private bool _finalGoldReleaseSpawned;

    public DryCoreBossState State => _state;
    public bool IsActive => _state != DryCoreBossState.Inactive && _state != DryCoreBossState.Completed;
    public int RemainingCores => _remainingCores;
    public int CompletedCycles => _completedCycles;

    public event Action<DryCoreBossState, DryCoreBossState> StateChanged;
    public event Action<int> BossCycleCompleted;
    public event Action BossEarlyReopen;
    public event Action DryCoreDefeated;
    public event Action BossRewardCollectionCompleted;
    public event Action FirstBossDefeated;

    public void Initialize(EdgeOrbitHarvestMvp gameplay)
    {
      Unsubscribe();
      _gameplay = gameplay;
      _view = GetComponent<DryCoreBossView>();
      if (_view == null)
      {
        _view = gameObject.AddComponent<DryCoreBossView>();
      }
      _view.EnsureCreated();
      Subscribe();
    }

    public void StartBoss()
    {
      if (_gameplay == null || _state != DryCoreBossState.Inactive)
      {
        return;
      }

      _remainingCores = 3;
      _completedCycles = 0;
      _roundSerial = 0;
      _currentRoundId = 0;
      _expectedRoundSampleCount = 0;
      _trackingPaused = false;
      _trackingResumedDuringCoverage = false;
      _defeatedRewardState = DefeatedRewardState.None;
      _dryCoreDefeatedEmitted = false;
      _bossRewardCompletedEmitted = false;
      _firstBossDefeatedEmitted = false;
      _finalGoldReleaseSpawned = false;
      _gameplay.BeginFirstLevelBossMode();
      _view.Show();
      _view.SetFragmentFeedbackCount(0);
      _view.SetSoftBlinkReady(false);
      SetState(DryCoreBossState.Entrance);
    }

    private void Update()
    {
      if (_gameplay == null || _state == DryCoreBossState.Inactive || _state == DryCoreBossState.Completed)
      {
        return;
      }

      var observationComplete = _state == DryCoreBossState.Defeated &&
                                _defeatedRewardState == DefeatedRewardState.ObservationComplete;
      if (!observationComplete && !_gameplay.IsTrackingAvailable)
      {
        if (!_trackingPaused)
        {
          _trackingPaused = true;
          _faceScreenPreparationElapsed = 0f;
          _view.SetPrompt(DryCoreBossPrompt.None);
        }
        _view.TickVisuals(0f, false);
        return;
      }

      if (_trackingPaused)
      {
        _trackingPaused = false;
        _faceScreenPreparationElapsed = 0f;
        _trackingResumedDuringCoverage = _state == DryCoreBossState.ExpandingCoverage &&
                                         !_gameplay.AreEyesClosed;
        RestorePromptForState();
      }

      var deltaTime = Time.unscaledDeltaTime;
      _stateElapsed += deltaTime;
      var freezeMotion = _state == DryCoreBossState.ExpandingCoverage ||
                         _state == DryCoreBossState.ReadyToReopen ||
                         _state == DryCoreBossState.WaitReopen ||
                         _state == DryCoreBossState.Defeated;
      _view.TickVisuals(deltaTime, !freezeMotion);

      switch (_state)
      {
        case DryCoreBossState.Entrance:
          if (_stateElapsed >= 1.05f)
          {
            BeginSoftBlinkRound();
          }
          break;
        case DryCoreBossState.WaitSoftBlink:
          UpdateFaceScreenPreparation(deltaTime);
          break;
        case DryCoreBossState.PromptClose:
          if (_gameplay.HasStableOpenEyesForSoftBlink)
          {
            _gameplay.PlayBossFeedback(BossFeedbackCue.CloseRequest);
            _view.SetPrompt(DryCoreBossPrompt.CloseEyes);
            SetState(DryCoreBossState.WaitEyesClosed);
          }
          break;
        case DryCoreBossState.WaitEyesClosed:
          if (_gameplay.AreEyesClosed)
          {
            BeginCoverage();
          }
          break;
        case DryCoreBossState.ExpandingCoverage:
          UpdateCoverage(deltaTime);
          break;
        case DryCoreBossState.ReadyToReopen:
          SetState(DryCoreBossState.WaitReopen);
          break;
        case DryCoreBossState.WaitReopen:
          if (!_gameplay.AreEyesClosed)
          {
            ResolveSuccessfulReopen();
          }
          break;
        case DryCoreBossState.HitFeedback:
          if (_stateElapsed >= _hitFeedbackSeconds)
          {
            ContinueAfterHitFeedback(false);
          }
          break;
        case DryCoreBossState.WaitPushAwayReady:
          if (_gameplay.IsPushAwayCollectionReady)
          {
            EnterWaitPushAway(false);
          }
          else
          {
            RecoverIfRoundSamplesWereLost(false);
          }
          break;
        case DryCoreBossState.WaitPushAway:
        case DryCoreBossState.WaitExperienceCollected:
          RecoverIfRoundSamplesWereLost(false);
          break;
        case DryCoreBossState.NextPhase:
          if (_stateElapsed >= Mathf.Max(0.8f, _quietSecondsAfterRound))
          {
            BeginSoftBlinkRound();
          }
          break;
        case DryCoreBossState.RetryDelay:
          if (_stateElapsed >= _earlyReopenRetryDelaySeconds)
          {
            _gameplay.PlayBossFeedback(BossFeedbackCue.CloseRequest);
            _view.SetPrompt(DryCoreBossPrompt.CloseEyes);
            SetState(DryCoreBossState.WaitEyesClosed);
          }
          break;
        case DryCoreBossState.Defeated:
          UpdateDefeatedReward(deltaTime);
          break;
      }
    }

    private void BeginSoftBlinkRound()
    {
      if (_remainingCores <= 0)
      {
        return;
      }

      _currentRoundId = ++_roundSerial;
      _coverageCuePlayed = false;
      _coverageElapsed = 0f;
      _coverageRadiusPixels = 0f;
      _expectedRoundSampleCount = 0;
      _faceScreenPreparationElapsed = 0f;
      _view.EndCoverage(false);
      _view.SetFragmentFeedbackCount(0);
      _view.SetSoftBlinkReady(true);
      _view.SetPrompt(DryCoreBossPrompt.SoftBlink);
      SetState(DryCoreBossState.WaitSoftBlink);
    }

    private void UpdateFaceScreenPreparation(float deltaTime)
    {
      if (!_gameplay.IsComfortGazeForBoss)
      {
        _faceScreenPreparationElapsed = 0f;
        return;
      }

      _faceScreenPreparationElapsed += Mathf.Max(0f, deltaTime);
      if (_faceScreenPreparationElapsed < Mathf.Max(0.2f, _faceScreenPreparationSeconds))
      {
        return;
      }

      _faceScreenPreparationElapsed = 0f;
      _view.SetSoftBlinkReady(false);
      _view.PlaySoftBlinkActivation();
      var fragmentCount = _gameplay.ApplyBossBlinkModules();
      _view.SetFragmentFeedbackCount(fragmentCount);
      _view.SetPrompt(DryCoreBossPrompt.None);
      SetState(DryCoreBossState.PromptClose);
    }

    private void HandleSoftBlinkPerformed(int softBlinkSerial)
    {
      // Retained for compatibility with the existing signal. Boss preparation no longer requires a blink.
    }

    private void BeginCoverage()
    {
      _coverageElapsed = 0f;
      _coverageRadiusPixels = 0f;
      _coverageCuePlayed = false;
      _trackingResumedDuringCoverage = false;
      _coverageTargets = _view.CaptureCoverageTargets();
      _view.SetPrompt(DryCoreBossPrompt.WaitForTone);
      _view.BeginCoverage();
      SetState(DryCoreBossState.ExpandingCoverage);
    }

    private void UpdateCoverage(float deltaTime)
    {
      if (_trackingResumedDuringCoverage)
      {
        _trackingResumedDuringCoverage = false;
        ResetCoverageForRetry(false);
        return;
      }

      if (!_gameplay.AreEyesClosed)
      {
        ResetCoverageForRetry(true);
        return;
      }

      _coverageElapsed += deltaTime;
      _coverageRadiusPixels += Mathf.Min(Screen.width, Screen.height) *
                               _coverageGrowthScreenRatioPerSecond * deltaTime;
      _view.SetCoverageRadiusPixels(_coverageRadiusPixels);
      var minimumClosedSeconds = Mathf.Min(5f, 3f + _completedCycles);
      var geometryCovered = AreCoverageTargetsFullyCovered();
      var safetyReleased = _coverageElapsed >= _coverageSafetyLimitSeconds;
      if (_coverageCuePlayed ||
          _coverageElapsed < minimumClosedSeconds ||
          (!geometryCovered && !safetyReleased))
      {
        return;
      }

      _coverageCuePlayed = true;
      if (safetyReleased && !geometryCovered)
      {
        Debug.Log("Dry Core coverage safety limit reached. Reopen is allowed without penalty.", this);
      }
      _gameplay.PlayBossFeedback(BossFeedbackCue.CoverageComplete);
      _view.SetPrompt(DryCoreBossPrompt.Open);
      SetState(DryCoreBossState.ReadyToReopen);
    }

    private bool AreCoverageTargetsFullyCovered()
    {
      if (_coverageTargets == null || _coverageTargets.Length == 0)
      {
        return false;
      }

      var center = _view.BossCenterScreenPosition;
      for (var i = 0; i < _coverageTargets.Length; i++)
      {
        var requiredRadius = Vector2.Distance(center, _coverageTargets[i].ScreenPosition) +
                             _coverageTargets[i].RadiusPixels;
        if (requiredRadius > _coverageRadiusPixels)
        {
          return false;
        }
      }

      return true;
    }

    private void ResetCoverageForRetry(bool countAsEarlyReopen)
    {
      _coverageCuePlayed = false;
      _coverageRadiusPixels = 0f;
      _view.EndCoverage(false);
      _view.SetPrompt(DryCoreBossPrompt.None);
      if (countAsEarlyReopen)
      {
        InvokeSignalSafely(BossEarlyReopen, nameof(BossEarlyReopen));
      }
      SetState(DryCoreBossState.RetryDelay);
    }

    private void ResolveSuccessfulReopen()
    {
      _view.EndCoverage(true);
      _view.SetPrompt(DryCoreBossPrompt.None);
      _pendingExtraCoreDamage = 0;
      _resolvingRestModules = true;
      _quietSecondsAfterRound = _gameplay.ApplySuccessfulBossRestModules(
        _currentRoundId,
        _view.BossViewportAnchor);
      _resolvingRestModules = false;

      var requestedDamage = 1 + Mathf.Max(0, _pendingExtraCoreDamage);
      if (CareUpgradeController.Instance != null && CareUpgradeController.Instance.BossCoreEchoEnabled)
      {
        requestedDamage++;
        _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.BossCoreEcho);
      }
      if (_completedCycles == 0)
      {
        requestedDamage = Mathf.Min(requestedDamage, Mathf.Max(1, _remainingCores - 1));
      }
      var actualDamage = Mathf.Clamp(requestedDamage, 1, _remainingCores);
      _remainingCores -= actualDamage;
      _completedCycles++;
      _view.ApplyCoreDamage(_remainingCores, actualDamage);
      _gameplay.PlayBossFeedback(BossFeedbackCue.SuccessfulRelease);
      InvokeSignalSafely(BossCycleCompleted, actualDamage, nameof(BossCycleCompleted));

      var finalCoreDestroyed = _remainingCores <= 0;
      if (finalCoreDestroyed)
      {
        SetState(DryCoreBossState.Defeated);
        SetDefeatedRewardState(DefeatedRewardState.HitFeedback);
        if (!_dryCoreDefeatedEmitted)
        {
          _dryCoreDefeatedEmitted = true;
          InvokeSignalSafely(DryCoreDefeated, nameof(DryCoreDefeated));
        }
      }
      else
      {
        SetState(DryCoreBossState.HitFeedback);
      }

      _gameplay.SpawnBossExperienceSamples(
        _currentRoundId,
        actualDamage,
        _view.BossViewportAnchor,
        KeepBlinkingTheme.AccentPrimary);
      _expectedRoundSampleCount = _gameplay.GetPendingBossExperienceSampleCount(_currentRoundId);
      if (_expectedRoundSampleCount <= 0)
      {
        Debug.LogError(
          "Dry Core produced no collectable samples. The resolved phase will continue without awarding XP.",
          this);
      }
    }

    private void HandleFutureBossCoreDamageRequested(int damage)
    {
      if (_resolvingRestModules)
      {
        _pendingExtraCoreDamage += Mathf.Max(0, damage);
      }
    }

    private void ContinueAfterHitFeedback(bool finalReward)
    {
      if (_expectedRoundSampleCount <= 0)
      {
        if (finalReward)
        {
          CompleteDefeatedRewardCollection();
        }
        else
        {
          CompleteCurrentRoundAfterCollection();
        }
        return;
      }

      if (finalReward)
      {
        SetDefeatedRewardState(DefeatedRewardState.WaitPushAwayReady);
        if (_gameplay.IsPushAwayCollectionReady)
        {
          EnterWaitPushAway(true);
        }
      }
      else
      {
        SetState(DryCoreBossState.WaitPushAwayReady);
        if (_gameplay.IsPushAwayCollectionReady)
        {
          EnterWaitPushAway(false);
        }
      }
    }

    private void HandlePushAwayCollectionReady()
    {
      if (_state == DryCoreBossState.WaitPushAwayReady)
      {
        EnterWaitPushAway(false);
      }
      else if (_state == DryCoreBossState.Defeated &&
               _defeatedRewardState == DefeatedRewardState.WaitPushAwayReady)
      {
        EnterWaitPushAway(true);
      }
    }

    private void EnterWaitPushAway(bool finalReward)
    {
      _view.SetPrompt(DryCoreBossPrompt.PushAway);
      if (finalReward)
      {
        SetDefeatedRewardState(DefeatedRewardState.WaitPushAway);
      }
      else
      {
        SetState(DryCoreBossState.WaitPushAway);
      }
    }

    private void HandlePushAwayTriggered()
    {
      if (_state == DryCoreBossState.WaitPushAway)
      {
        _view.SetPrompt(DryCoreBossPrompt.None);
        SetState(DryCoreBossState.WaitExperienceCollected);
      }
      else if (_state == DryCoreBossState.Defeated &&
               _defeatedRewardState == DefeatedRewardState.WaitPushAway)
      {
        if (!_finalGoldReleaseSpawned && CareUpgradeController.Instance != null && CareUpgradeController.Instance.BossGoldReleaseEnabled)
        {
          _finalGoldReleaseSpawned = true;
          _gameplay.SpawnBossBonusExperienceSamples(
            _currentRoundId,
            8,
            _view.BossViewportAnchor,
            CareExperienceState.Rested);
          _expectedRoundSampleCount = _gameplay.GetPendingBossExperienceSampleCount(_currentRoundId);
          _gameplay.NotifyCareUpgradeActivated(FirstLevelModuleId.BossGoldRelease);
        }
        _view.SetPrompt(DryCoreBossPrompt.None);
        SetDefeatedRewardState(DefeatedRewardState.WaitExperienceCollected);
      }
    }

    private void HandleBossExperienceCollectionCompleted(int bossRoundId, int collectedCount)
    {
      if (bossRoundId != _currentRoundId || collectedCount <= 0)
      {
        return;
      }

      if (_state == DryCoreBossState.WaitExperienceCollected)
      {
        CompleteCurrentRoundAfterCollection();
      }
      else if (_state == DryCoreBossState.Defeated &&
               _defeatedRewardState == DefeatedRewardState.WaitExperienceCollected)
      {
        CompleteDefeatedRewardCollection();
      }
    }

    private void RecoverIfRoundSamplesWereLost(bool finalReward)
    {
      if (_expectedRoundSampleCount <= 0 ||
          _gameplay.GetPendingBossExperienceSampleCount(_currentRoundId) > 0)
      {
        return;
      }

      Debug.LogWarning(
        "Dry Core collectable samples disappeared before collection completed. The phase will continue without awarding missing XP.",
        this);
      if (finalReward)
      {
        CompleteDefeatedRewardCollection();
      }
      else
      {
        CompleteCurrentRoundAfterCollection();
      }
    }

    private void CompleteCurrentRoundAfterCollection()
    {
      _expectedRoundSampleCount = 0;
      _view.SetPrompt(DryCoreBossPrompt.None);
      SetState(DryCoreBossState.NextPhase);
    }

    private void UpdateDefeatedReward(float deltaTime)
    {
      _defeatedRewardElapsed += deltaTime;
      switch (_defeatedRewardState)
      {
        case DefeatedRewardState.HitFeedback:
          if (_defeatedRewardElapsed >= _hitFeedbackSeconds)
          {
            ContinueAfterHitFeedback(true);
          }
          break;
        case DefeatedRewardState.WaitPushAwayReady:
          if (_gameplay.IsPushAwayCollectionReady)
          {
            EnterWaitPushAway(true);
          }
          else
          {
            RecoverIfRoundSamplesWereLost(true);
          }
          break;
        case DefeatedRewardState.WaitPushAway:
        case DefeatedRewardState.WaitExperienceCollected:
          RecoverIfRoundSamplesWereLost(true);
          break;
        case DefeatedRewardState.ObservationComplete:
          if (_defeatedRewardElapsed >= _observationCompleteSeconds)
          {
            _view.Hide();
            SetState(DryCoreBossState.Completed);
          }
          break;
      }
    }

    private void CompleteDefeatedRewardCollection()
    {
      if (_bossRewardCompletedEmitted)
      {
        return;
      }

      _expectedRoundSampleCount = 0;
      _bossRewardCompletedEmitted = true;
      _gameplay.BeginFirstLevelSettlement();
      InvokeSignalSafely(BossRewardCollectionCompleted, nameof(BossRewardCollectionCompleted));
      if (!_firstBossDefeatedEmitted)
      {
        _firstBossDefeatedEmitted = true;
        InvokeSignalSafely(FirstBossDefeated, nameof(FirstBossDefeated));
      }
      _view.SetPrompt(DryCoreBossPrompt.Complete);
      SetDefeatedRewardState(DefeatedRewardState.ObservationComplete);
    }

    private void SetDefeatedRewardState(DefeatedRewardState next)
    {
      _defeatedRewardState = next;
      _defeatedRewardElapsed = 0f;
    }

    private void RestorePromptForState()
    {
      switch (_state)
      {
        case DryCoreBossState.WaitSoftBlink:
          _view.SetPrompt(DryCoreBossPrompt.SoftBlink);
          break;
        case DryCoreBossState.WaitEyesClosed:
          _view.SetPrompt(DryCoreBossPrompt.CloseEyes);
          break;
        case DryCoreBossState.ExpandingCoverage:
          _view.SetPrompt(DryCoreBossPrompt.WaitForTone);
          break;
        case DryCoreBossState.ReadyToReopen:
        case DryCoreBossState.WaitReopen:
          _view.SetPrompt(DryCoreBossPrompt.Open);
          break;
        case DryCoreBossState.WaitPushAway:
          _view.SetPrompt(DryCoreBossPrompt.PushAway);
          break;
        case DryCoreBossState.Defeated:
          RestoreDefeatedPrompt();
          break;
        default:
          _view.SetPrompt(DryCoreBossPrompt.None);
          break;
      }
    }

    private void RestoreDefeatedPrompt()
    {
      switch (_defeatedRewardState)
      {
        case DefeatedRewardState.WaitPushAway:
          _view.SetPrompt(DryCoreBossPrompt.PushAway);
          break;
        case DefeatedRewardState.ObservationComplete:
          _view.SetPrompt(DryCoreBossPrompt.Complete);
          break;
        default:
          _view.SetPrompt(DryCoreBossPrompt.None);
          break;
      }
    }

    private void SetState(DryCoreBossState next)
    {
      if (_state == next)
      {
        return;
      }

      var previous = _state;
      _state = next;
      _stateElapsed = 0f;
      InvokeSignalSafely(StateChanged, previous, next, nameof(StateChanged));
    }

    private void Subscribe()
    {
      if (_gameplay == null)
      {
        return;
      }
      _gameplay.SoftBlinkPerformed += HandleSoftBlinkPerformed;
      _gameplay.PushAwayCollectionReady += HandlePushAwayCollectionReady;
      _gameplay.PushAwayTriggered += HandlePushAwayTriggered;
      _gameplay.BossExperienceCollectionCompleted += HandleBossExperienceCollectionCompleted;
      _gameplay.FutureBossCoreDamageRequested += HandleFutureBossCoreDamageRequested;
    }

    private void Unsubscribe()
    {
      if (_gameplay == null)
      {
        return;
      }
      _gameplay.SoftBlinkPerformed -= HandleSoftBlinkPerformed;
      _gameplay.PushAwayCollectionReady -= HandlePushAwayCollectionReady;
      _gameplay.PushAwayTriggered -= HandlePushAwayTriggered;
      _gameplay.BossExperienceCollectionCompleted -= HandleBossExperienceCollectionCompleted;
      _gameplay.FutureBossCoreDamageRequested -= HandleFutureBossCoreDamageRequested;
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
          Debug.LogError($"KeepBlinking Dry Core signal observer failed: {signalName}.", this);
          Debug.LogException(exception, this);
        }
      }
    }

    private void InvokeSignalSafely<T>(Action<T> signal, T value, string signalName)
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
          ((Action<T>)handlers[i]).Invoke(value);
        }
        catch (Exception exception)
        {
          Debug.LogError($"KeepBlinking Dry Core signal observer failed: {signalName}.", this);
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
          Debug.LogError($"KeepBlinking Dry Core signal observer failed: {signalName}.", this);
          Debug.LogException(exception, this);
        }
      }
    }

    private void OnDestroy()
    {
      Unsubscribe();
    }
  }
}
