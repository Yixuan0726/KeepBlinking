using System;
using System.Collections.Generic;
using KeepBlinking.Input;
using KeepBlinking.Tutorial;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  public enum FirstLevelCareFlowState
  {
    Dormant,
    PreparingRound,
    WaitBaseSamples,
    WaitNeutral,
    DirectionalMovement,
    FocusShift,
    GuidedEyeMovement,
    PromptScreenDown,
    ScreenDownRest,
    WaitPhoneReturn,
    RecoverTracking,
    WaitReturnNeutral,
    ArmPushAway,
    WaitPushAway,
    WaitExperienceCollected,
    OpenUpgrade,
    Completed,
  }

  public sealed class FirstLevelCareFlowController : MonoBehaviour
  {
    [SerializeField, Min(1)] private int _baseSamplesPerRound = 2;
    [SerializeField, Min(0.25f)] private float _faceCenterBaselineSeconds = 0.75f;
    [SerializeField, Min(8)] private int _minimumFaceCenterSamples = 15;
    [SerializeField] private float _neutralDistanceMin = 0.95f;
    [SerializeField] private float _neutralDistanceMax = 1.05f;
    [SerializeField, Min(0.2f)] private float _neutralHoldSeconds = 0.5f;

    private readonly List<float> _faceX = new List<float>(90);
    private readonly List<float> _faceY = new List<float>(90);
    private EdgeOrbitHarvestMvp _gameplay;
    private KeepBlinkingTutorialController _tutorial;
    private DirectionalPhoneMovementController _directional;
    private FocusShiftController _focusShift;
    private GuidedEyeMovementController _guidedEyeMovement;
    private ScreenDownRestController _screenRest;
    private CareExperienceRewardEmitter _emitter;
    private CareCircuitController _circuit;
    private FirstLevelCareSkipView _skipView;
    private int _roundIndex;
    private int _roundBaseConverted;
    private float _baselineCaptureStartedAt = -1f;
    private float _neutralHoldStartedAt = -1f;
    private bool _subscribed;
    private bool _roundStarted;
    private bool _sessionFaceCenterFrozen;
    private int _experienceArrivedThisRound;
    private long _lastFaceCenterSampleSequence = -1;
    private long _lastNeutralSampleSequence = -1;
    private string _lastNeutralPrompt = string.Empty;
    private int _restValidSeconds;
    private bool _releaseWasPhysical;
    private bool _baseSamplesSkipped;

    public static FirstLevelCareFlowController Instance { get; private set; }
    public static event Action<int> CareRoundStarted;
    public static event Action CareReturnNeutralCompleted;
    public static event Action CareCollectionArmed;
    public static event Action<int> CareRoundCompleted;

    public FirstLevelCareFlowState State { get; private set; } = FirstLevelCareFlowState.Dormant;
    public int CurrentRound => Mathf.Clamp(_roundIndex + 1, 1, 4);
    public float SessionBaselineFaceScale { get; private set; } = -1f;
    public float SessionBaselineFaceX { get; private set; } = 0.5f;
    public float SessionBaselineFaceY { get; private set; } = 0.5f;
    public bool HasSessionFaceCenterBaseline => _sessionFaceCenterFrozen;
    public Quaternion NeutralPhoneAttitude => _screenRest != null
      ? _screenRest.InitialDeviceAttitude
      : Quaternion.identity;

    public static bool RoundUsesGuidedEyeMovement(int oneBasedRound)
    {
      return oneBasedRound == 2;
    }

    public static bool RoundUsesScreenDownRest(int oneBasedRound)
    {
      return oneBasedRound == 1 || oneBasedRound == 3 || oneBasedRound == 4;
    }

    public static FirstLevelCareFlowController EnsureExists(EdgeOrbitHarvestMvp gameplay)
    {
      if (Instance == null) Instance = FindFirstObjectByType<FirstLevelCareFlowController>();
      if (Instance == null)
      {
        var owner = new GameObject("First Level Care Flow Controller");
        Instance = owner.AddComponent<FirstLevelCareFlowController>();
      }
      Instance.Bind(gameplay);
      return Instance;
    }

    private void Awake()
    {
      if (Instance != null && Instance != this)
      {
        Destroy(gameObject);
        return;
      }
      Instance = this;
    }

    private void Bind(EdgeOrbitHarvestMvp gameplay)
    {
      if (_gameplay == gameplay) return;
      Unsubscribe();
      _gameplay = gameplay;
      _tutorial = FindFirstObjectByType<KeepBlinkingTutorialController>();
      _emitter = CareExperienceRewardEmitter.EnsureExists(gameplay);
      _circuit = CareCircuitController.EnsureExists(gameplay);
      _directional = DirectionalPhoneMovementController.EnsureExists(gameplay);
      _focusShift = FocusShiftController.EnsureExists(gameplay);
      _guidedEyeMovement = GuidedEyeMovementController.EnsureExists(gameplay);
      _screenRest = ScreenDownRestController.EnsureExists(gameplay);
      _skipView = FirstLevelCareSkipView.EnsureExists(this);
      SessionBaselineFaceScale = gameplay != null ? gameplay.BaselineFaceScale : -1f;
      // Reserve the formal first-level flow immediately. This prevents the old
      // random/tutorial loop from spawning while the fixed face-center baseline
      // is being captured.
      _gameplay?.SetCareRoundFlowEnabled(true);
      Subscribe();
      State = FirstLevelCareFlowState.Dormant;
    }

    private void Update()
    {
      if (_gameplay == null || SessionBaselineFaceScale <= 0f) return;
      CaptureSessionFaceCenter();
      _screenRest?.TryCaptureSessionNeutralOrientation();

      if (!_roundStarted && CanBeginFirstRound())
      {
        _roundStarted = true;
        BeginRound();
      }

      if (State == FirstLevelCareFlowState.WaitReturnNeutral)
      {
        UpdateReturnNeutral();
      }
      else if (State == FirstLevelCareFlowState.WaitExperienceCollected)
      {
        if (_gameplay.PendingUnsettledExperienceCount == 0 && _emitter.QueuedCount == 0 && _gameplay.IsModuleUpgradeOpen)
          State = FirstLevelCareFlowState.OpenUpgrade;
      }
      _skipView?.SetVisible(IsSkipAvailable(), State);
    }

    private bool CanBeginFirstRound()
    {
      if (!_sessionFaceCenterFrozen)
        return false;
      if (_tutorial != null && _tutorial.IsRunning) return false;
      if (_gameplay.IsTutorialModeEnabled || _gameplay.IsModuleUpgradeOpen || _gameplay.IsFirstLevelBossMode) return false;
      var session = _gameplay.GetComponent<FirstLevelSessionController>();
      return session == null || session.State == FirstLevelSessionState.Gameplay;
    }

    private void CaptureSessionFaceCenter()
    {
      if (_sessionFaceCenterFrozen) return;
      var snapshot = EyeInputDebugState.Latest;
      if (!snapshot.FaceDetected || !snapshot.HasFaceCenter || snapshot.FaceCenterConfidence < 0.45f ||
          snapshot.SampleSequence == _lastFaceCenterSampleSequence) return;
      _lastFaceCenterSampleSequence = snapshot.SampleSequence;
      if (_baselineCaptureStartedAt < 0f) _baselineCaptureStartedAt = Time.unscaledTime;
      _faceX.Add(snapshot.FaceCenterNormalized.x);
      _faceY.Add(snapshot.FaceCenterNormalized.y);
      if (_faceX.Count > 90)
      {
        _faceX.RemoveAt(0);
        _faceY.RemoveAt(0);
      }
      if (_faceX.Count >= _minimumFaceCenterSamples &&
          Time.unscaledTime - _baselineCaptureStartedAt >= _faceCenterBaselineSeconds)
      {
        FreezeSessionFaceCenter();
      }
    }

    private void FreezeSessionFaceCenter()
    {
      _faceX.Sort();
      _faceY.Sort();
      SessionBaselineFaceX = _faceX.Count > 0 ? _faceX[_faceX.Count / 2] : 0.5f;
      SessionBaselineFaceY = _faceY.Count > 0 ? _faceY[_faceY.Count / 2] : 0.5f;
      _sessionFaceCenterFrozen = true;
      Debug.Log(
        $"First-level fixed care baseline: scale={SessionBaselineFaceScale:F6}, " +
        $"face=({SessionBaselineFaceX:F4},{SessionBaselineFaceY:F4}).",
        this);
    }

    private void BeginRound()
    {
      if (_roundIndex == 0) _gameplay.SetCareRoundFlowEnabled(true);
      _roundBaseConverted = 0;
      _experienceArrivedThisRound = 0;
      _restValidSeconds = 0;
      _releaseWasPhysical = false;
      _baseSamplesSkipped = false;
      _neutralHoldStartedAt = -1f;
      State = FirstLevelCareFlowState.PreparingRound;
      _gameplay.SetCareActionActive(false);
      _gameplay.SetCareCollectionArmed(false);
      _gameplay.SetCareRoundSpawningPaused(false);
      CareUpgradeController.Instance?.ApplyPendingQuietReturn();
      SoftFocusFieldController.Instance?.SetCareInteractionPaused(false);
      State = FirstLevelCareFlowState.WaitBaseSamples;
      CareRoundStarted?.Invoke(CurrentRound);
      _emitter.BeginCareRound();
      _circuit.BeginRound(CurrentRound);
      Debug.Log($"First-level care round {CurrentRound} waiting for {_baseSamplesPerRound} Soft Focus samples.", this);
    }

    private void HandleSoftFocusBatchCompleted(int count)
    {
      if (State != FirstLevelCareFlowState.WaitBaseSamples || count <= 0) return;
      _roundBaseConverted += count;
      if (_roundBaseConverted < _baseSamplesPerRound) return;

      _gameplay.SetCareRoundSpawningPaused(true);
      _gameplay.SetCareActionActive(true);
      SoftFocusFieldController.Instance?.SetCareInteractionPaused(true);
      StartRoundMovement();
    }

    private void StartRoundMovement()
    {
      switch (_roundIndex)
      {
        case 0:
          State = FirstLevelCareFlowState.DirectionalMovement;
          _directional.StartRoutine(DirectionalPhoneRoutine.Horizontal, SessionBaselineFaceX, SessionBaselineFaceY, SessionBaselineFaceScale);
          break;
        case 1:
          State = FirstLevelCareFlowState.DirectionalMovement;
          _directional.StartRoutine(DirectionalPhoneRoutine.Vertical, SessionBaselineFaceX, SessionBaselineFaceY, SessionBaselineFaceScale);
          break;
        case 2:
          _circuit.CompleteMove(!_baseSamplesSkipped);
          StartFocusShift();
          break;
        default:
          State = FirstLevelCareFlowState.DirectionalMovement;
          _directional.StartRoutine(DirectionalPhoneRoutine.Complete, SessionBaselineFaceX, SessionBaselineFaceY, SessionBaselineFaceScale);
          break;
      }
    }

    private void HandleDirectionalCompleted(DirectionalPhoneRoutine routine)
    {
      if (State != FirstLevelCareFlowState.DirectionalMovement) return;
      _circuit.CompleteMove(true);
      StartFocusShift();
    }

    private void HandleDirectionalSkipped(DirectionalPhoneRoutine routine)
    {
      if (State != FirstLevelCareFlowState.DirectionalMovement) return;
      _circuit.CompleteMove(false);
      StartFocusShift();
    }

    private void StartGuidedEyeMovement()
    {
      State = FirstLevelCareFlowState.GuidedEyeMovement;
      if (_guidedEyeMovement.StartGuidedMovement()) return;
      Debug.LogWarning("Round 2 could not start Guided Eye Movement; the care flow remains paused.", this);
    }

    private void StartFocusShift()
    {
      State = FirstLevelCareFlowState.FocusShift;
      _focusShift.StartFocusShift(SessionBaselineFaceScale);
    }

    private void HandleFocusShiftCompleted()
    {
      if (State != FirstLevelCareFlowState.FocusShift) return;
      _circuit.CompleteFocus(true);
      if (RoundUsesGuidedEyeMovement(CurrentRound)) StartGuidedEyeMovement();
      else StartScreenRest();
    }

    private void HandleFocusShiftSkipped()
    {
      if (State != FirstLevelCareFlowState.FocusShift) return;
      _circuit.CompleteFocus(false);
      if (RoundUsesGuidedEyeMovement(CurrentRound)) StartGuidedEyeMovement();
      else StartScreenRest();
    }

    private void HandleFocusShiftStepCompleted(CareMovementDirection direction)
    {
      if (State == FirstLevelCareFlowState.FocusShift && direction == CareMovementDirection.Far)
        _circuit.RegisterValidFarPoint();
    }

    private void StartScreenRest()
    {
      State = FirstLevelCareFlowState.PromptScreenDown;
      if (_screenRest.StartRoundRest()) State = FirstLevelCareFlowState.ScreenDownRest;
    }

    private void HandleRestRewardsReady(int count)
    {
      if (State != FirstLevelCareFlowState.ScreenDownRest || count <= 0) return;
      _restValidSeconds = Mathf.Min(8, count);
    }

    private void HandleGuidedRewardsReady(int count)
    {
      if (State != FirstLevelCareFlowState.GuidedEyeMovement || count <= 0) return;
      _restValidSeconds = Mathf.Min(8, count);
    }

    private void HandleScreenRestCompleted()
    {
      if (State != FirstLevelCareFlowState.ScreenDownRest) return;
      _circuit.CompleteRest(true, _restValidSeconds);
      BeginReturnNeutral();
    }

    private void HandleGuidedEyeMovementCompleted()
    {
      if (State != FirstLevelCareFlowState.GuidedEyeMovement) return;
      _circuit.CompleteRest(true, _restValidSeconds);
      BeginReturnNeutral();
    }

    private void HandleGuidedEyeMovementSkipped()
    {
      if (State != FirstLevelCareFlowState.GuidedEyeMovement) return;
      _circuit.CompleteRest(false, 0);
      BeginReturnNeutral();
    }

    private void BeginReturnNeutral()
    {
      State = FirstLevelCareFlowState.WaitPhoneReturn;
      State = FirstLevelCareFlowState.RecoverTracking;
      State = FirstLevelCareFlowState.WaitReturnNeutral;
      _neutralHoldStartedAt = -1f;
      _lastNeutralSampleSequence = -1;
      SetReturnNeutralPrompt("RETURN TO CENTER");
    }

    private void HandleScreenRestSkipped()
    {
      if (State != FirstLevelCareFlowState.ScreenDownRest) return;
      _circuit.CompleteRest(false, 0);
      BeginReturnNeutral();
    }

    public void SkipCurrentStep()
    {
      switch (State)
      {
        case FirstLevelCareFlowState.Dormant:
          // Distance still uses the valid fixed session baseline. Only the
          // face-center capture is bypassed when that input cannot settle.
          SessionBaselineFaceX = 0.5f;
          SessionBaselineFaceY = 0.5f;
          _sessionFaceCenterFrozen = true;
          if (!_roundStarted)
          {
            _roundStarted = true;
            BeginRound();
          }
          break;
        case FirstLevelCareFlowState.WaitBaseSamples:
          _baseSamplesSkipped = true;
          _gameplay.SetCareRoundSpawningPaused(true);
          _gameplay.SetCareActionActive(true);
          SoftFocusFieldController.Instance?.SetCareInteractionPaused(true);
          StartRoundMovement();
          break;
        case FirstLevelCareFlowState.DirectionalMovement:
          _directional?.Skip();
          break;
        case FirstLevelCareFlowState.FocusShift:
          _focusShift?.Skip();
          break;
        case FirstLevelCareFlowState.GuidedEyeMovement:
          _guidedEyeMovement?.Skip();
          break;
        case FirstLevelCareFlowState.PromptScreenDown:
        case FirstLevelCareFlowState.ScreenDownRest:
          _screenRest?.Skip();
          break;
        case FirstLevelCareFlowState.WaitPhoneReturn:
        case FirstLevelCareFlowState.RecoverTracking:
        case FirstLevelCareFlowState.WaitReturnNeutral:
          SkipReturnNeutralGate();
          break;
        case FirstLevelCareFlowState.ArmPushAway:
        case FirstLevelCareFlowState.WaitPushAway:
          SkipPushAwayRecognition();
          break;
      }
    }

    private bool IsSkipAvailable()
    {
      return (State == FirstLevelCareFlowState.Dormant &&
              !_sessionFaceCenterFrozen &&
              (_tutorial == null || !_tutorial.IsRunning) &&
              !_gameplay.IsCalibrationActive &&
              !_gameplay.IsTutorialModeEnabled) ||
             State == FirstLevelCareFlowState.WaitBaseSamples ||
             State == FirstLevelCareFlowState.DirectionalMovement ||
             State == FirstLevelCareFlowState.FocusShift ||
             State == FirstLevelCareFlowState.GuidedEyeMovement ||
             State == FirstLevelCareFlowState.PromptScreenDown ||
             State == FirstLevelCareFlowState.ScreenDownRest ||
             State == FirstLevelCareFlowState.WaitPhoneReturn ||
             State == FirstLevelCareFlowState.RecoverTracking ||
             State == FirstLevelCareFlowState.WaitReturnNeutral ||
             State == FirstLevelCareFlowState.ArmPushAway ||
             State == FirstLevelCareFlowState.WaitPushAway;
    }

    private void SkipReturnNeutralGate()
    {
      _circuit?.InvalidateRound();
      _emitter?.FlushQueuedImmediately();
      ArmCollection(false);
    }

    private void SkipPushAwayRecognition()
    {
      if (_gameplay == null) return;
      _circuit?.InvalidateRound();
      if (_gameplay.StartCareCollectionFromSkip())
      {
        _releaseWasPhysical = false;
        State = FirstLevelCareFlowState.WaitExperienceCollected;
      }
    }

    private void UpdateReturnNeutral()
    {
      var snapshot = EyeInputDebugState.Latest;
      if (!snapshot.FaceDetected || !snapshot.HasFaceCenter || !_gameplay.HasValidDistanceSample)
      {
        _neutralHoldStartedAt = -1f;
        SetReturnNeutralPrompt("TRACKING LOST");
        return;
      }
      if (_emitter.QueuedCount > 0)
      {
        _neutralHoldStartedAt = -1f;
        SetReturnNeutralPrompt("HOLD STEADY");
        return;
      }
      if (snapshot.SampleSequence == _lastNeutralSampleSequence)
      {
        return;
      }
      _lastNeutralSampleSequence = snapshot.SampleSequence;
      var ratio = _gameplay.DistanceRatio;
      if (ratio < _neutralDistanceMin)
      {
        _neutralHoldStartedAt = -1f;
        SetReturnNeutralPrompt("MOVE CLOSER");
        return;
      }
      if (ratio > _neutralDistanceMax)
      {
        _neutralHoldStartedAt = -1f;
        SetReturnNeutralPrompt("MOVE AWAY");
        return;
      }
      SetReturnNeutralPrompt("HOLD STEADY");
      if (_neutralHoldStartedAt < 0f) _neutralHoldStartedAt = Time.unscaledTime;
      if (Time.unscaledTime - _neutralHoldStartedAt < _neutralHoldSeconds) return;

      ArmCollection(true);
    }

    private void ArmCollection(bool neutralConfirmed)
    {
      State = FirstLevelCareFlowState.ArmPushAway;
      _screenRest.HideReturnNeutralPrompt();
      _guidedEyeMovement.HideReturnNeutralPrompt();
      if (neutralConfirmed) CareReturnNeutralCompleted?.Invoke();
      _circuit.PrepareReleaseBonuses();
      var requirement = Mathf.Max(1, _gameplay.PendingUnsettledExperienceValue);
      _gameplay.ConfigureCareRoundExperienceRequirement(requirement);
      _gameplay.SetCareActionActive(false);
      SoftFocusFieldController.Instance?.SetCareInteractionPaused(false);
      _gameplay.SetCareCollectionArmed(true);
      CareCollectionArmed?.Invoke();
      State = FirstLevelCareFlowState.WaitPushAway;
      Debug.Log($"Care collection armed for round {CurrentRound}: {requirement} real samples.", this);
    }

    private void SetReturnNeutralPrompt(string prompt)
    {
      if (_roundIndex == 1)
        _guidedEyeMovement?.ShowReturnNeutralPrompt(prompt);
      else
        _screenRest?.ShowReturnNeutralPrompt(prompt);
      if (_lastNeutralPrompt == prompt) return;
      _lastNeutralPrompt = prompt;
      Debug.Log($"Care return neutral: {prompt} ratio={_gameplay.DistanceRatio:F3}.", this);
    }

    private void HandlePushAwayTriggered()
    {
      if (State != FirstLevelCareFlowState.WaitPushAway) return;
      _releaseWasPhysical = true;
      State = FirstLevelCareFlowState.WaitExperienceCollected;
      CareAudioFeedbackController.EnsureExists().PlayPushAway();
    }

    private void HandleExperienceReachedBar(int targetId)
    {
      if (State != FirstLevelCareFlowState.WaitExperienceCollected) return;
      _experienceArrivedThisRound++;
      if (_gameplay.PendingUnsettledExperienceCount == 0 && _emitter.QueuedCount == 0)
        _circuit.CompleteRelease(_releaseWasPhysical);
    }

    private void HandleUpgradeOpened()
    {
      if (State == FirstLevelCareFlowState.WaitExperienceCollected)
        State = FirstLevelCareFlowState.OpenUpgrade;
    }

    private void HandleModuleChoiceCompleted(int cardIndex)
    {
      if (State != FirstLevelCareFlowState.OpenUpgrade) return;
      CareRoundCompleted?.Invoke(CurrentRound);
      _roundIndex++;
      if (_roundIndex >= 4)
      {
        State = FirstLevelCareFlowState.Completed;
        _gameplay.SetCareCollectionArmed(false);
        _gameplay.SetCareActionActive(false);
        _gameplay.SetCareRoundSpawningPaused(true);
        return;
      }
      BeginRound();
    }

    private void Subscribe()
    {
      if (_subscribed || _gameplay == null) return;
      _gameplay.SoftFocusBatchCompleted += HandleSoftFocusBatchCompleted;
      _gameplay.PushAwayTriggered += HandlePushAwayTriggered;
      _gameplay.ExperienceReachedBar += HandleExperienceReachedBar;
      _gameplay.UpgradeOpened += HandleUpgradeOpened;
      _gameplay.ModuleChoiceCompleted += HandleModuleChoiceCompleted;
      DirectionalPhoneMovementController.DirectionalMovementCompleted += HandleDirectionalCompleted;
      DirectionalPhoneMovementController.DirectionalMovementSkipped += HandleDirectionalSkipped;
      FocusShiftController.FocusShiftCompleted += HandleFocusShiftCompleted;
      FocusShiftController.FocusShiftSkipped += HandleFocusShiftSkipped;
      FocusShiftController.FocusShiftStepCompleted += HandleFocusShiftStepCompleted;
      GuidedEyeMovementController.GuidedEyeMovementRewardsReady += HandleGuidedRewardsReady;
      GuidedEyeMovementController.GuidedEyeMovementCompleted += HandleGuidedEyeMovementCompleted;
      GuidedEyeMovementController.GuidedEyeMovementSkipped += HandleGuidedEyeMovementSkipped;
      ScreenDownRestController.ScreenDownRestRewardsReady += HandleRestRewardsReady;
      ScreenDownRestController.ScreenDownRestCompleted += HandleScreenRestCompleted;
      ScreenDownRestController.ScreenDownRestSkipped += HandleScreenRestSkipped;
      _subscribed = true;
    }

    private void Unsubscribe()
    {
      if (_gameplay != null)
      {
        _gameplay.SoftFocusBatchCompleted -= HandleSoftFocusBatchCompleted;
        _gameplay.PushAwayTriggered -= HandlePushAwayTriggered;
        _gameplay.ExperienceReachedBar -= HandleExperienceReachedBar;
        _gameplay.UpgradeOpened -= HandleUpgradeOpened;
        _gameplay.ModuleChoiceCompleted -= HandleModuleChoiceCompleted;
      }
      DirectionalPhoneMovementController.DirectionalMovementCompleted -= HandleDirectionalCompleted;
      DirectionalPhoneMovementController.DirectionalMovementSkipped -= HandleDirectionalSkipped;
      FocusShiftController.FocusShiftCompleted -= HandleFocusShiftCompleted;
      FocusShiftController.FocusShiftSkipped -= HandleFocusShiftSkipped;
      FocusShiftController.FocusShiftStepCompleted -= HandleFocusShiftStepCompleted;
      GuidedEyeMovementController.GuidedEyeMovementRewardsReady -= HandleGuidedRewardsReady;
      GuidedEyeMovementController.GuidedEyeMovementCompleted -= HandleGuidedEyeMovementCompleted;
      GuidedEyeMovementController.GuidedEyeMovementSkipped -= HandleGuidedEyeMovementSkipped;
      ScreenDownRestController.ScreenDownRestRewardsReady -= HandleRestRewardsReady;
      ScreenDownRestController.ScreenDownRestCompleted -= HandleScreenRestCompleted;
      ScreenDownRestController.ScreenDownRestSkipped -= HandleScreenRestSkipped;
      _subscribed = false;
    }

    private void OnDestroy()
    {
      Unsubscribe();
      if (Instance == this) Instance = null;
    }
  }
}
