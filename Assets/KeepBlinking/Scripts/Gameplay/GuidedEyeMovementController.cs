using System;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  public enum GuidedEyeMovementState
  {
    Dormant,
    Preparing,
    PreviewClockwise,
    PreviewPause,
    PreviewCounterClockwise,
    PromptClose,
    WaitEyesClosed,
    GuidedClockwise,
    GuidedPause,
    GuidedCounterClockwise,
    PausedTracking,
    CompletionCue,
    WaitReopen,
    ReopenFeedback,
    Completed,
    Skipped,
  }

  public static class GuidedEyeMovementLogic
  {
    public static int StoredGoldFragments(float validClosedGuidanceSeconds, int maximumFragments)
    {
      return Mathf.Clamp(
        Mathf.FloorToInt(Mathf.Max(0f, validClosedGuidanceSeconds)),
        0,
        Mathf.Max(0, maximumFragments));
    }

    public static bool CanBeginGuidance(bool previewCompleted, bool promptIssued, bool faceTracked, bool eyesClosed)
    {
      return previewCompleted && promptIssued && faceTracked && eyesClosed;
    }
  }

  /// <summary>
  /// Verifies sustained eye closure and times an audio guide. It deliberately
  /// does not read gaze position, gaze direction, head pose, or L2CS output.
  /// </summary>
  public sealed class GuidedEyeMovementController : MonoBehaviour
  {
    public const string ReportDisplayName = "Guided Eye Movement";
    public const string VerificationStatement =
      "The system verifies sustained eye closure and provides timed audio guidance. " +
      "It does not verify eyeball rotation direction or completion.";

    [Header("Preview")]
    [SerializeField, Min(0.1f)] private float _preparingSeconds = 0.3f;
    [SerializeField, Min(2f)] private float _previewClockwiseSeconds = 2.5f;
    [SerializeField, Range(0.8f, 1f)] private float _previewPauseSeconds = 0.9f;
    [SerializeField, Min(2f)] private float _previewCounterClockwiseSeconds = 2.5f;

    [Header("Closed-Eye Audio Guide")]
    [SerializeField, Min(2f)] private float _guidedClockwiseSeconds = 4f;
    [SerializeField, Range(0.8f, 1f)] private float _guidedPauseSeconds = 0.9f;
    [SerializeField, Min(2f)] private float _guidedCounterClockwiseSeconds = 4f;
    [SerializeField, Range(4, 8)] private int _guideNoteCount = 6;
    [SerializeField, Min(0.1f)] private float _closeConfirmationSeconds = 0.18f;
    [SerializeField, Min(0.05f)] private float _earlyReopenConfirmationSeconds = 0.12f;
    [SerializeField, Min(0.1f)] private float _openBeforePromptSeconds = 0.25f;
    [SerializeField, Min(0.1f)] private float _reopenConfirmationSeconds = 0.25f;
    [SerializeField, Min(0.1f)] private float _trackingJitterGraceSeconds = 0.55f;
    [SerializeField, Min(0.1f)] private float _completionCueSeconds = 0.55f;
    [SerializeField, Min(0.3f)] private float _reopenFeedbackSeconds = 1.1f;
    [SerializeField, Range(1, 8)] private int _maximumGoldFragments = 8;

    private EdgeOrbitHarvestMvp _gameplay;
    private GuidedEyeMovementView _view;
    private GuidedEyeMovementState _resumeGuidedState = GuidedEyeMovementState.GuidedClockwise;
    private float _stateStartedAt;
    private float _phaseElapsed;
    private float _validClosedGuidanceSeconds;
    private float _closeHeldSeconds;
    private float _openHeldSeconds;
    private float _earlyOpenHeldSeconds;
    private float _lastTrackingValidAt = -999f;
    private int _storedGoldFragments;
    private int _lastNoteIndex = -1;
    private bool _previewCompleted;
    private bool _closePromptIssued;
    private bool _guidanceStarted;
    private bool _rewardsIssued;

    public static GuidedEyeMovementController Instance { get; private set; }
    public static event Action GuidedEyeMovementStarted;
    public static event Action<int> GuidedEyeMovementRewardsReady;
    public static event Action GuidedEyeMovementCompleted;
    public static event Action GuidedEyeMovementSkipped;

    public GuidedEyeMovementState State { get; private set; } = GuidedEyeMovementState.Dormant;
    public float PhaseProgress => GetPhaseProgress();
    public float ValidClosedGuidanceSeconds => _validClosedGuidanceSeconds;
    public int StoredGoldFragments => _storedGoldFragments;
    public bool PreviewCompleted => _previewCompleted;
    public bool IsActive => State != GuidedEyeMovementState.Dormant &&
                            State != GuidedEyeMovementState.Completed &&
                            State != GuidedEyeMovementState.Skipped;

    public static GuidedEyeMovementController EnsureExists(EdgeOrbitHarvestMvp gameplay)
    {
      if (Instance == null) Instance = FindFirstObjectByType<GuidedEyeMovementController>();
      if (Instance == null)
      {
        var owner = new GameObject("Guided Eye Movement Controller");
        Instance = owner.AddComponent<GuidedEyeMovementController>();
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
      _view = gameObject.AddComponent<GuidedEyeMovementView>();
    }

    private void Bind(EdgeOrbitHarvestMvp gameplay)
    {
      _gameplay = gameplay;
    }

    public bool StartGuidedMovement()
    {
      if (IsActive || _gameplay == null) return false;
      _previewCompleted = false;
      _closePromptIssued = false;
      _guidanceStarted = false;
      _rewardsIssued = false;
      _validClosedGuidanceSeconds = 0f;
      _storedGoldFragments = 0;
      _phaseElapsed = 0f;
      _closeHeldSeconds = 0f;
      _openHeldSeconds = 0f;
      _earlyOpenHeldSeconds = 0f;
      _lastTrackingValidAt = _gameplay.IsTrackingAvailable ? Time.unscaledTime : -999f;
      _resumeGuidedState = GuidedEyeMovementState.GuidedClockwise;
      _gameplay.SetGuidedEyeMovementActive(true);
      _gameplay.SetCareActionActive(true);
      SoftFocusFieldController.Instance?.SetCareInteractionPaused(true);
      _view?.Show();
      EnterState(GuidedEyeMovementState.Preparing, false);
      GuidedEyeMovementStarted?.Invoke();
      Debug.Log($"{ReportDisplayName} started. {VerificationStatement}", this);
      return true;
    }

    public void ReplayPreview()
    {
      if (!IsActive || _guidanceStarted) return;
      if (State != GuidedEyeMovementState.PromptClose && State != GuidedEyeMovementState.WaitEyesClosed) return;
      CareAudioFeedbackController.EnsureExists().StopGuidedCue();
      _previewCompleted = false;
      _closePromptIssued = false;
      _closeHeldSeconds = 0f;
      _openHeldSeconds = 0f;
      EnterState(GuidedEyeMovementState.PreviewClockwise, false);
    }

    public void Skip()
    {
      if (!IsActive) return;
      CareAudioFeedbackController.EnsureExists().StopGuidedCue();
      _gameplay?.SetGuidedEyeMovementActive(false);
      _view?.Hide();
      State = GuidedEyeMovementState.Skipped;
      GuidedEyeMovementSkipped?.Invoke();
      Debug.Log("Guided Eye Movement skipped. No Gold XP or REST completion signal was issued.", this);
    }

    public void ShowReturnNeutralPrompt(string prompt)
    {
      _view?.ShowReturnNeutral(string.IsNullOrEmpty(prompt) ? "RETURN TO CENTER" : prompt);
    }

    public void HideReturnNeutralPrompt()
    {
      _view?.Hide();
    }

    private void Update()
    {
      if (!IsActive) return;
      var delta = Mathf.Min(0.1f, Mathf.Max(0f, Time.unscaledDeltaTime));
      UpdateState(delta);
      _view?.Render(State, PhaseProgress, Mathf.Clamp01(_validClosedGuidanceSeconds / Mathf.Max(1f, _maximumGoldFragments)));
    }

    private void UpdateState(float delta)
    {
      switch (State)
      {
        case GuidedEyeMovementState.Preparing:
          if (StateElapsed >= _preparingSeconds) EnterState(GuidedEyeMovementState.PreviewClockwise, false);
          break;
        case GuidedEyeMovementState.PreviewClockwise:
          PlayClockwiseNotes(_previewClockwiseSeconds);
          _phaseElapsed += delta;
          if (_phaseElapsed >= _previewClockwiseSeconds) EnterState(GuidedEyeMovementState.PreviewPause, false);
          break;
        case GuidedEyeMovementState.PreviewPause:
          _phaseElapsed += delta;
          if (_phaseElapsed >= _previewPauseSeconds) EnterState(GuidedEyeMovementState.PreviewCounterClockwise, false);
          break;
        case GuidedEyeMovementState.PreviewCounterClockwise:
          PlayCounterClockwiseNotes(_previewCounterClockwiseSeconds);
          _phaseElapsed += delta;
          if (_phaseElapsed >= _previewCounterClockwiseSeconds)
          {
            _previewCompleted = true;
            EnterState(GuidedEyeMovementState.PromptClose, false);
          }
          break;
        case GuidedEyeMovementState.PromptClose:
          UpdatePromptClose(delta);
          break;
        case GuidedEyeMovementState.WaitEyesClosed:
          UpdateWaitEyesClosed(delta);
          break;
        case GuidedEyeMovementState.GuidedClockwise:
          UpdateGuidedPhase(delta, _guidedClockwiseSeconds, GuidedEyeMovementState.GuidedPause);
          break;
        case GuidedEyeMovementState.GuidedPause:
          UpdateGuidedPhase(delta, _guidedPauseSeconds, GuidedEyeMovementState.GuidedCounterClockwise);
          break;
        case GuidedEyeMovementState.GuidedCounterClockwise:
          UpdateGuidedPhase(delta, _guidedCounterClockwiseSeconds, GuidedEyeMovementState.CompletionCue);
          break;
        case GuidedEyeMovementState.PausedTracking:
          UpdateTrackingPause(delta);
          break;
        case GuidedEyeMovementState.CompletionCue:
          if (StateElapsed >= _completionCueSeconds) EnterState(GuidedEyeMovementState.WaitReopen, false);
          break;
        case GuidedEyeMovementState.WaitReopen:
          UpdateWaitReopen(delta);
          break;
        case GuidedEyeMovementState.ReopenFeedback:
          if (StateElapsed >= _reopenFeedbackSeconds) FinishCompleted();
          break;
      }
    }

    private void UpdatePromptClose(float delta)
    {
      if (!_gameplay.IsTrackingAvailable)
      {
        _openHeldSeconds = 0f;
        _view?.SetPrompt("FOLLOW THE RHYTHM");
        return;
      }
      _lastTrackingValidAt = Time.unscaledTime;
      if (_gameplay.AreEyesClosed)
      {
        _openHeldSeconds = 0f;
        _view?.SetPrompt("OPEN YOUR EYES");
        return;
      }
      _view?.SetPrompt("CLOSE YOUR EYES");
      _openHeldSeconds += delta;
      if (_openHeldSeconds < _openBeforePromptSeconds) return;
      _closePromptIssued = true;
      CareAudioFeedbackController.EnsureExists().PlayGuidedCloseRequest();
      EnterState(GuidedEyeMovementState.WaitEyesClosed, false);
    }

    private void UpdateWaitEyesClosed(float delta)
    {
      _view?.SetPrompt("CLOSE YOUR EYES");
      if (!_gameplay.IsTrackingAvailable)
      {
        _closeHeldSeconds = 0f;
        _view?.SetPrompt("FOLLOW THE RHYTHM");
        return;
      }
      _lastTrackingValidAt = Time.unscaledTime;
      if (!_gameplay.AreEyesClosed)
      {
        _closeHeldSeconds = 0f;
        return;
      }
      _closeHeldSeconds += delta;
      if (_closeHeldSeconds < _closeConfirmationSeconds) return;
      if (!GuidedEyeMovementLogic.CanBeginGuidance(_previewCompleted, _closePromptIssued, true, true)) return;
      _guidanceStarted = true;
      var resume = IsGuidedPhase(_resumeGuidedState) ? _resumeGuidedState : GuidedEyeMovementState.GuidedClockwise;
      EnterState(resume, true);
    }

    private void UpdateGuidedPhase(float delta, float duration, GuidedEyeMovementState next)
    {
      if (!_gameplay.IsTrackingAvailable)
      {
        if (Time.unscaledTime - _lastTrackingValidAt > _trackingJitterGraceSeconds)
        {
          PauseForTracking();
          return;
        }
      }
      else
      {
        _lastTrackingValidAt = Time.unscaledTime;
        if (!_gameplay.AreEyesClosed)
        {
          _earlyOpenHeldSeconds += delta;
          if (_earlyOpenHeldSeconds >= _earlyReopenConfirmationSeconds) PauseForOpenEyes();
          return;
        }
        _earlyOpenHeldSeconds = 0f;
      }

      if (State == GuidedEyeMovementState.GuidedClockwise) PlayClockwiseNotes(duration);
      else if (State == GuidedEyeMovementState.GuidedCounterClockwise) PlayCounterClockwiseNotes(duration);
      _phaseElapsed += delta;
      _validClosedGuidanceSeconds += delta;
      _storedGoldFragments = Mathf.Max(
        _storedGoldFragments,
        GuidedEyeMovementLogic.StoredGoldFragments(_validClosedGuidanceSeconds, _maximumGoldFragments));
      if (_phaseElapsed >= duration) EnterState(next, false);
    }

    private void PauseForOpenEyes()
    {
      _resumeGuidedState = State;
      _earlyOpenHeldSeconds = 0f;
      _closeHeldSeconds = 0f;
      CareAudioFeedbackController.EnsureExists().StopGuidedCue();
      EnterState(GuidedEyeMovementState.WaitEyesClosed, true);
    }

    private void PauseForTracking()
    {
      _resumeGuidedState = State;
      CareAudioFeedbackController.EnsureExists().PlayGuidedTrackingPause();
      EnterState(GuidedEyeMovementState.PausedTracking, true);
    }

    private void UpdateTrackingPause(float delta)
    {
      _view?.SetPrompt("FOLLOW THE RHYTHM");
      if (!_gameplay.IsTrackingAvailable) return;
      _lastTrackingValidAt = Time.unscaledTime;
      if (_gameplay.AreEyesClosed)
      {
        _closeHeldSeconds += delta;
        if (_closeHeldSeconds >= _closeConfirmationSeconds) EnterState(_resumeGuidedState, true);
      }
      else
      {
        _closeHeldSeconds = 0f;
        EnterState(GuidedEyeMovementState.WaitEyesClosed, true);
      }
    }

    private void UpdateWaitReopen(float delta)
    {
      _view?.SetPrompt("OPEN YOUR EYES");
      if (!_gameplay.IsTrackingAvailable)
      {
        _openHeldSeconds = 0f;
        return;
      }
      _lastTrackingValidAt = Time.unscaledTime;
      if (_gameplay.AreEyesClosed)
      {
        _openHeldSeconds = 0f;
        return;
      }
      _openHeldSeconds += delta;
      if (_openHeldSeconds < _reopenConfirmationSeconds) return;
      EnterState(GuidedEyeMovementState.ReopenFeedback, false);
    }

    private void EnterState(GuidedEyeMovementState state, bool preservePhase)
    {
      State = state;
      _stateStartedAt = Time.unscaledTime;
      if (!preservePhase) _phaseElapsed = 0f;
      _lastNoteIndex = preservePhase ? GetCurrentNoteIndex() - 1 : -1;
      _closeHeldSeconds = 0f;
      _openHeldSeconds = 0f;
      _earlyOpenHeldSeconds = 0f;
      _view?.SetState(state, !_guidanceStarted);

      var audio = CareAudioFeedbackController.EnsureExists();
      if (state == GuidedEyeMovementState.PreviewPause || state == GuidedEyeMovementState.GuidedPause)
        audio.PlayGuidedCenterPause();
      else if (state == GuidedEyeMovementState.CompletionCue)
        audio.PlayGuidedCompletion();
      else if (state == GuidedEyeMovementState.ReopenFeedback && !_rewardsIssued)
      {
        _rewardsIssued = true;
        GuidedEyeMovementRewardsReady?.Invoke(_storedGoldFragments);
      }
      Debug.Log($"Guided Eye Movement state: {state}.", this);
    }

    private void FinishCompleted()
    {
      CareAudioFeedbackController.EnsureExists().StopGuidedCue();
      _gameplay?.SetGuidedEyeMovementActive(false);
      _view?.Hide();
      State = GuidedEyeMovementState.Completed;
      GuidedEyeMovementCompleted?.Invoke();
      Debug.Log(
        $"Guided Eye Movement completed from {_validClosedGuidanceSeconds:F2}s verified closed-eye guidance. " +
        $"Pending Gold XP={_storedGoldFragments}. {VerificationStatement}",
        this);
    }

    private void PlayClockwiseNotes(float duration)
    {
      var note = NoteIndex(duration);
      if (note == _lastNoteIndex) return;
      _lastNoteIndex = note;
      CareAudioFeedbackController.EnsureExists().PlayGuidedClockwiseNote(note, _guideNoteCount);
    }

    private void PlayCounterClockwiseNotes(float duration)
    {
      var note = NoteIndex(duration);
      if (note == _lastNoteIndex) return;
      _lastNoteIndex = note;
      CareAudioFeedbackController.EnsureExists().PlayGuidedCounterClockwiseNote(note, _guideNoteCount);
    }

    private int NoteIndex(float duration)
    {
      return Mathf.Clamp(
        Mathf.FloorToInt((_phaseElapsed / Mathf.Max(0.01f, duration)) * _guideNoteCount),
        0,
        Mathf.Max(0, _guideNoteCount - 1));
    }

    private int GetCurrentNoteIndex()
    {
      if (State == GuidedEyeMovementState.GuidedClockwise) return NoteIndex(_guidedClockwiseSeconds);
      if (State == GuidedEyeMovementState.GuidedCounterClockwise) return NoteIndex(_guidedCounterClockwiseSeconds);
      return -1;
    }

    private float GetPhaseProgress()
    {
      switch (State)
      {
        case GuidedEyeMovementState.PreviewClockwise: return Mathf.Clamp01(_phaseElapsed / _previewClockwiseSeconds);
        case GuidedEyeMovementState.PreviewPause: return Mathf.Clamp01(_phaseElapsed / _previewPauseSeconds);
        case GuidedEyeMovementState.PreviewCounterClockwise: return Mathf.Clamp01(_phaseElapsed / _previewCounterClockwiseSeconds);
        case GuidedEyeMovementState.GuidedClockwise: return Mathf.Clamp01(_phaseElapsed / _guidedClockwiseSeconds);
        case GuidedEyeMovementState.GuidedPause: return Mathf.Clamp01(_phaseElapsed / _guidedPauseSeconds);
        case GuidedEyeMovementState.GuidedCounterClockwise: return Mathf.Clamp01(_phaseElapsed / _guidedCounterClockwiseSeconds);
        case GuidedEyeMovementState.ReopenFeedback: return Mathf.Clamp01(StateElapsed / _reopenFeedbackSeconds);
        default: return 0f;
      }
    }

    private static bool IsGuidedPhase(GuidedEyeMovementState state)
    {
      return state == GuidedEyeMovementState.GuidedClockwise ||
             state == GuidedEyeMovementState.GuidedPause ||
             state == GuidedEyeMovementState.GuidedCounterClockwise;
    }

    private float StateElapsed => Time.unscaledTime - _stateStartedAt;

    private void OnDestroy()
    {
      if (Instance != this) return;
      _gameplay?.SetGuidedEyeMovementActive(false);
      Instance = null;
    }
  }
}
