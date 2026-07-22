using System;
using System.Collections;
using KeepBlinking.Gameplay;
using UnityEngine;

namespace KeepBlinking.Tutorial
{
  public enum KeepBlinkingTutorialState
  {
    Inactive,
    ShowGoal,
    WaitFirstLock,
    WaitFirstConverted,
    WaitFirstPushAway,
    WaitFirstCollected,
    WaitRepeatConverted,
    WaitRepeatCollected,
    WaitTutorialCrisis,
    WaitEyesClosed,
    WaitFullCoverage,
    WaitReopenRelease,
    WaitCrisisCollected,
    WaitModuleChoice,
    Countdown,
    Completed,
  }

  public sealed class KeepBlinkingTutorialController : MonoBehaviour
  {
    private const float CrisisSuccessFeedbackSeconds = 0.8f;
    private const float CountdownLeadInSeconds = 0.45f;
    private const float CountdownStepSeconds = 0.7f;

    [SerializeField] private EdgeOrbitHarvestMvp _gameplay;
    [SerializeField] private KeepBlinkingTutorialState _state = KeepBlinkingTutorialState.Inactive;
    [SerializeField] private bool _enableTutorialForDevelopment = true;

    private bool _isSubscribed;
    private bool _tutorialRunning;
    private bool _inputSuspended;
    private bool _externalStartBlocked;
    private int _tutorialTargetId = EdgeOrbitHarvestMvp.NoTargetId;
    private Coroutine _countdownRoutine;
    private Coroutine _crisisSuccessRoutine;
    private int _currentCountdownNumber;

    public KeepBlinkingTutorialState State => _state;
    public bool IsRunning => _tutorialRunning;
    public bool IsInputSuspended => _tutorialRunning && _inputSuspended;
    public int TutorialTargetId => _tutorialTargetId;
    public int CurrentCountdownNumber => _currentCountdownNumber;
    private bool CanAdvanceTutorial => _tutorialRunning && !_inputSuspended;
    public int LastLockedTargetId { get; private set; } = EdgeOrbitHarvestMvp.NoTargetId;
    public int LastConvertedTargetId { get; private set; } = EdgeOrbitHarvestMvp.NoTargetId;
    public int LastCollectingBatchCount { get; private set; }
    public int LastExperienceTargetId { get; private set; } = EdgeOrbitHarvestMvp.NoTargetId;
    public ExperienceProgressSignal LastExperienceProgress { get; private set; }
    public int LastCrisisSpawnCount { get; private set; }
    public int LastModuleChoiceIndex { get; private set; } = -1;
    public int LastReopenConvertedCount { get; private set; }
    public int LastCrisisExperienceCollectedCount { get; private set; }
    public bool PushAwayReadyObserved { get; private set; }
    public bool PushAwayTriggeredObserved { get; private set; }
    public bool UpgradeOpenedObserved { get; private set; }
    public bool EyesClosedFreezeObserved { get; private set; }
    public bool FullCoverageObserved { get; private set; }

    public event Action<KeepBlinkingTutorialState, KeepBlinkingTutorialState> StateChanged;
    public event Action<bool> InputSuspensionChanged;
    public event Action<int> CountdownValueChanged;

    private void OnEnable()
    {
      ResolveGameplaySource();
      SubscribeToGameplaySignals();
    }

    private void Start()
    {
      TryStartTutorial();
    }

    private void OnDisable()
    {
      InterruptTutorial();
      UnsubscribeFromGameplaySignals();
    }

    public bool TryStartTutorial()
    {
      if (!_enableTutorialForDevelopment ||
          _externalStartBlocked ||
          _tutorialRunning ||
          _gameplay == null ||
          !_gameplay.IsTutorialReady)
      {
        return false;
      }

      ResetObservedSignals();
      _tutorialRunning = true;
      _inputSuspended = false;
      _gameplay.SetTutorialMode(true);
      _gameplay.SetTutorialRandomSpawningPaused(true);
      _gameplay.SetTutorialRandomCrisisPaused(true);
      _gameplay.SetTutorialSessionTimerPaused(true);
      SetState(KeepBlinkingTutorialState.ShowGoal);

      _tutorialTargetId = _gameplay.SpawnTutorialOrbitTarget();
      if (_tutorialTargetId == EdgeOrbitHarvestMvp.NoTargetId)
      {
        InterruptTutorial();
        return false;
      }

      SetState(KeepBlinkingTutorialState.WaitFirstLock);
      return true;
    }

    public void SetExternalStartBlocked(bool blocked, bool tryStartWhenReleased = true)
    {
      _externalStartBlocked = blocked;
      if (!blocked && tryStartWhenReleased)
      {
        TryStartTutorial();
      }
    }

    public void InterruptTutorial()
    {
      if (!_tutorialRunning)
      {
        return;
      }

      _tutorialRunning = false;
      _inputSuspended = false;
      _tutorialTargetId = EdgeOrbitHarvestMvp.NoTargetId;
      StopCrisisSuccessDelay();
      StopCountdown();
      _gameplay?.ResumeFormalGameFlow();
      SetState(KeepBlinkingTutorialState.Inactive);
    }

    public void SetDevelopmentTutorialEnabled(bool enabled)
    {
      _enableTutorialForDevelopment = enabled;
      if (enabled)
      {
        TryStartTutorial();
      }
      else
      {
        InterruptTutorial();
      }
    }

    public bool SetState(KeepBlinkingTutorialState nextState)
    {
      if (_state == nextState)
      {
        return false;
      }

      var previousState = _state;
      _state = nextState;
      InvokeStateChangedSafely(previousState, nextState);

      if (nextState == KeepBlinkingTutorialState.Completed && _tutorialRunning)
      {
        _tutorialRunning = false;
        _inputSuspended = false;
        _tutorialTargetId = EdgeOrbitHarvestMvp.NoTargetId;
        _gameplay?.ResumeFormalGameFlow();
      }

      return true;
    }

    private void InvokeStateChangedSafely(
      KeepBlinkingTutorialState previous,
      KeepBlinkingTutorialState next)
    {
      if (StateChanged == null)
      {
        return;
      }

      var handlers = StateChanged.GetInvocationList();
      for (var i = 0; i < handlers.Length; i++)
      {
        try
        {
          ((Action<KeepBlinkingTutorialState, KeepBlinkingTutorialState>)handlers[i]).Invoke(previous, next);
        }
        catch (Exception exception)
        {
          Debug.LogError("KeepBlinking tutorial state observer failed.", this);
          Debug.LogException(exception, this);
        }
      }
    }

    public void SetGameplaySource(EdgeOrbitHarvestMvp gameplay)
    {
      if (_gameplay == gameplay)
      {
        return;
      }

      InterruptTutorial();
      UnsubscribeFromGameplaySignals();
      _gameplay = gameplay;
      if (isActiveAndEnabled)
      {
        SubscribeToGameplaySignals();
      }
    }

    private void ResolveGameplaySource()
    {
      if (_gameplay == null)
      {
        _gameplay = FindFirstObjectByType<EdgeOrbitHarvestMvp>();
      }
    }

    private void SubscribeToGameplaySignals()
    {
      if (_isSubscribed || _gameplay == null)
      {
        return;
      }

      _gameplay.TargetLockChanged += HandleTargetLockChanged;
      _gameplay.TargetConverted += HandleTargetConverted;
      _gameplay.PushAwayCollectionReady += HandlePushAwayCollectionReady;
      _gameplay.PushAwayTriggered += HandlePushAwayTriggered;
      _gameplay.ConvertedCollectionStarted += HandleConvertedCollectionStarted;
      _gameplay.ExperienceReachedBar += HandleExperienceReachedBar;
      _gameplay.ExperienceProgressChanged += HandleExperienceProgressChanged;
      _gameplay.UpgradeOpened += HandleUpgradeOpened;
      _gameplay.ModuleChoiceCompleted += HandleModuleChoiceCompleted;
      _gameplay.CrisisStarted += HandleCrisisStarted;
      _gameplay.EyesClosedFreezeStarted += HandleEyesClosedFreezeStarted;
      _gameplay.FullCoverageReached += HandleFullCoverageReached;
      _gameplay.CrisisReleaseInterrupted += HandleCrisisReleaseInterrupted;
      _gameplay.ReopenReleaseCompleted += HandleReopenReleaseCompleted;
      _gameplay.CrisisExperienceCollectionCompleted += HandleCrisisExperienceCollectionCompleted;
      _gameplay.TutorialReadinessChanged += HandleTutorialReadinessChanged;
      _isSubscribed = true;
    }

    private void UnsubscribeFromGameplaySignals()
    {
      if (!_isSubscribed || _gameplay == null)
      {
        _isSubscribed = false;
        return;
      }

      _gameplay.TargetLockChanged -= HandleTargetLockChanged;
      _gameplay.TargetConverted -= HandleTargetConverted;
      _gameplay.PushAwayCollectionReady -= HandlePushAwayCollectionReady;
      _gameplay.PushAwayTriggered -= HandlePushAwayTriggered;
      _gameplay.ConvertedCollectionStarted -= HandleConvertedCollectionStarted;
      _gameplay.ExperienceReachedBar -= HandleExperienceReachedBar;
      _gameplay.ExperienceProgressChanged -= HandleExperienceProgressChanged;
      _gameplay.UpgradeOpened -= HandleUpgradeOpened;
      _gameplay.ModuleChoiceCompleted -= HandleModuleChoiceCompleted;
      _gameplay.CrisisStarted -= HandleCrisisStarted;
      _gameplay.EyesClosedFreezeStarted -= HandleEyesClosedFreezeStarted;
      _gameplay.FullCoverageReached -= HandleFullCoverageReached;
      _gameplay.CrisisReleaseInterrupted -= HandleCrisisReleaseInterrupted;
      _gameplay.ReopenReleaseCompleted -= HandleReopenReleaseCompleted;
      _gameplay.CrisisExperienceCollectionCompleted -= HandleCrisisExperienceCollectionCompleted;
      _gameplay.TutorialReadinessChanged -= HandleTutorialReadinessChanged;
      _isSubscribed = false;
    }

    private void ResetObservedSignals()
    {
      LastLockedTargetId = EdgeOrbitHarvestMvp.NoTargetId;
      LastConvertedTargetId = EdgeOrbitHarvestMvp.NoTargetId;
      LastCollectingBatchCount = 0;
      LastExperienceTargetId = EdgeOrbitHarvestMvp.NoTargetId;
      LastExperienceProgress = default;
      LastCrisisSpawnCount = 0;
      LastModuleChoiceIndex = -1;
      LastReopenConvertedCount = 0;
      LastCrisisExperienceCollectedCount = 0;
      _currentCountdownNumber = 0;
      PushAwayReadyObserved = false;
      PushAwayTriggeredObserved = false;
      UpgradeOpenedObserved = false;
      EyesClosedFreezeObserved = false;
      FullCoverageObserved = false;
    }

    private void HandleTutorialReadinessChanged(bool ready)
    {
      if (_tutorialRunning)
      {
        var suspended = !ready;
        if (_inputSuspended != suspended)
        {
          _inputSuspended = suspended;
          InvokeSignalSafely(InputSuspensionChanged, suspended, nameof(InputSuspensionChanged));
          if (!suspended && _state == KeepBlinkingTutorialState.Countdown && _currentCountdownNumber > 0)
          {
            InvokeSignalSafely(
              CountdownValueChanged,
              _currentCountdownNumber,
              nameof(CountdownValueChanged));
          }
        }
        return;
      }

      if (ready)
      {
        TryStartTutorial();
      }
    }

    private void HandleTargetLockChanged(int targetId)
    {
      if (_tutorialRunning && _inputSuspended && targetId == EdgeOrbitHarvestMvp.NoTargetId)
      {
        return;
      }

      LastLockedTargetId = targetId;
      if (CanAdvanceTutorial &&
          _state == KeepBlinkingTutorialState.WaitFirstLock &&
          targetId == _tutorialTargetId)
      {
        SetState(KeepBlinkingTutorialState.WaitFirstConverted);
        return;
      }

    }

    private void HandleTargetConverted(int targetId)
    {
      LastConvertedTargetId = targetId;
      if (CanAdvanceTutorial &&
          _state == KeepBlinkingTutorialState.WaitFirstConverted &&
          targetId == _tutorialTargetId)
      {
        SetState(KeepBlinkingTutorialState.WaitFirstPushAway);
      }
    }

    private void HandlePushAwayCollectionReady()
    {
      PushAwayReadyObserved = true;
      if (CanAdvanceTutorial && _state == KeepBlinkingTutorialState.WaitFirstPushAway)
      {
        SetState(KeepBlinkingTutorialState.WaitFirstCollected);
      }
    }

    private void HandlePushAwayTriggered()
    {
      PushAwayTriggeredObserved = true;
    }

    private void HandleConvertedCollectionStarted(int count)
    {
      LastCollectingBatchCount = count;
    }

    private void HandleExperienceReachedBar(int targetId)
    {
      LastExperienceTargetId = targetId;
      if (CanAdvanceTutorial &&
          _state == KeepBlinkingTutorialState.WaitFirstCollected &&
          targetId == _tutorialTargetId)
      {
        _tutorialTargetId = EdgeOrbitHarvestMvp.NoTargetId;
        PushAwayReadyObserved = false;
        PushAwayTriggeredObserved = false;
        EyesClosedFreezeObserved = false;
        FullCoverageObserved = false;
        LastLockedTargetId = EdgeOrbitHarvestMvp.NoTargetId;
        var crisisCount = _gameplay != null ? _gameplay.CrisisSpawnCount : 0;
        if (_gameplay == null ||
            crisisCount <= 0 ||
            _gameplay.SpawnTutorialCrisisTargets(crisisCount) != crisisCount)
        {
          InterruptTutorial();
          return;
        }

        SetState(KeepBlinkingTutorialState.WaitEyesClosed);
      }
    }

    private void HandleExperienceProgressChanged(ExperienceProgressSignal progress)
    {
      LastExperienceProgress = progress;
    }

    private void HandleUpgradeOpened()
    {
      UpgradeOpenedObserved = true;
    }

    private void HandleModuleChoiceCompleted(int cardIndex)
    {
      LastModuleChoiceIndex = cardIndex;
    }

    private void HandleCrisisStarted(int count)
    {
      LastCrisisSpawnCount = count;
    }

    private void HandleEyesClosedFreezeStarted()
    {
      EyesClosedFreezeObserved = true;
      if (CanAdvanceTutorial && _state == KeepBlinkingTutorialState.WaitEyesClosed)
      {
        SetState(KeepBlinkingTutorialState.WaitFullCoverage);
      }
    }

    private void HandleFullCoverageReached()
    {
      FullCoverageObserved = true;
      if (CanAdvanceTutorial && _state == KeepBlinkingTutorialState.WaitFullCoverage)
      {
        SetState(KeepBlinkingTutorialState.WaitReopenRelease);
      }
    }

    private void HandleCrisisReleaseInterrupted()
    {
      if (!CanAdvanceTutorial || _state != KeepBlinkingTutorialState.WaitFullCoverage)
      {
        return;
      }

      EyesClosedFreezeObserved = false;
      FullCoverageObserved = false;
      LastLockedTargetId = EdgeOrbitHarvestMvp.NoTargetId;
      SetState(KeepBlinkingTutorialState.WaitEyesClosed);
    }

    private void HandleReopenReleaseCompleted(int convertedCount)
    {
      LastReopenConvertedCount = convertedCount;
      if (!CanAdvanceTutorial || _state != KeepBlinkingTutorialState.WaitReopenRelease)
      {
        return;
      }

      if (convertedCount <= 0)
      {
        _gameplay?.SetTutorialCollectionInputPaused(false);
        EyesClosedFreezeObserved = false;
        FullCoverageObserved = false;
        LastLockedTargetId = EdgeOrbitHarvestMvp.NoTargetId;
        SetState(KeepBlinkingTutorialState.WaitEyesClosed);
        return;
      }

      PushAwayReadyObserved = false;
      PushAwayTriggeredObserved = false;
      _gameplay?.SetTutorialCollectionInputPaused(true);
      SetState(KeepBlinkingTutorialState.WaitCrisisCollected);
      StartCrisisSuccessDelay();
    }

    private void HandleCrisisExperienceCollectionCompleted(int collectedCount)
    {
      LastCrisisExperienceCollectedCount = collectedCount;
      if (CanAdvanceTutorial &&
          _state == KeepBlinkingTutorialState.WaitCrisisCollected &&
          collectedCount > 0)
      {
        StopCrisisSuccessDelay();
        _gameplay?.SetTutorialCollectionInputPaused(false);
        SetState(KeepBlinkingTutorialState.Countdown);
        StartCountdown();
      }
    }

    private void StartCrisisSuccessDelay()
    {
      StopCrisisSuccessDelay();
      _crisisSuccessRoutine = StartCoroutine(CrisisSuccessDelayRoutine());
    }

    private void StopCrisisSuccessDelay()
    {
      if (_crisisSuccessRoutine != null)
      {
        StopCoroutine(_crisisSuccessRoutine);
        _crisisSuccessRoutine = null;
      }
    }

    private IEnumerator CrisisSuccessDelayRoutine()
    {
      var elapsed = 0f;
      while (elapsed < CrisisSuccessFeedbackSeconds)
      {
        if (!_inputSuspended)
        {
          elapsed += Time.unscaledDeltaTime;
        }
        yield return null;
      }

      _crisisSuccessRoutine = null;
      if (_tutorialRunning && _state == KeepBlinkingTutorialState.WaitCrisisCollected)
      {
        _gameplay?.SetTutorialCollectionInputPaused(false);
      }
    }

    private void StartCountdown()
    {
      StopCountdown();
      _countdownRoutine = StartCoroutine(CountdownRoutine());
    }

    private void StopCountdown()
    {
      if (_countdownRoutine != null)
      {
        StopCoroutine(_countdownRoutine);
        _countdownRoutine = null;
      }

      if (_currentCountdownNumber != 0)
      {
        _currentCountdownNumber = 0;
        InvokeSignalSafely(CountdownValueChanged, 0, nameof(CountdownValueChanged));
      }
    }

    private IEnumerator CountdownRoutine()
    {
      var leadInElapsed = 0f;
      while (leadInElapsed < CountdownLeadInSeconds)
      {
        if (!_inputSuspended)
        {
          leadInElapsed += Time.unscaledDeltaTime;
        }
        yield return null;
      }

      for (var number = 3; number >= 1; number--)
      {
        _currentCountdownNumber = number;
        InvokeSignalSafely(CountdownValueChanged, number, nameof(CountdownValueChanged));
        var elapsed = 0f;
        while (elapsed < CountdownStepSeconds)
        {
          if (!_inputSuspended)
          {
            elapsed += Time.unscaledDeltaTime;
          }
          yield return null;
        }
      }

      _currentCountdownNumber = 0;
      InvokeSignalSafely(CountdownValueChanged, 0, nameof(CountdownValueChanged));
      _countdownRoutine = null;
      SetState(KeepBlinkingTutorialState.Completed);
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
          Debug.LogError($"KeepBlinking tutorial signal observer failed: {signalName}.", this);
          Debug.LogException(exception, this);
        }
      }
    }
  }
}
