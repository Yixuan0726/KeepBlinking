using System.Collections.Generic;
using UnityEngine;

namespace KeepBlinking.Input
{
  public enum SessionDistanceState
  {
    WaitingForBaseline,
    TrackingLost,
    Paused,
    Far,
    PushAwayCandidate,
    Normal,
    Near,
    TooCloseCandidate,
    TooClose,
  }

  public readonly struct SessionDistanceUpdate
  {
    public SessionDistanceUpdate(
      bool baselineCaptured,
      bool baselineRejected,
      bool pushAwayReady,
      bool pushAwayTriggered,
      bool tooCloseChanged)
    {
      BaselineCaptured = baselineCaptured;
      BaselineRejected = baselineRejected;
      PushAwayReady = pushAwayReady;
      PushAwayTriggered = pushAwayTriggered;
      TooCloseChanged = tooCloseChanged;
    }

    public bool BaselineCaptured { get; }
    public bool BaselineRejected { get; }
    public bool PushAwayReady { get; }
    public bool PushAwayTriggered { get; }
    public bool TooCloseChanged { get; }
  }

  public readonly struct SessionDistanceSettings
  {
    public SessionDistanceSettings(
      float baselineCaptureSeconds,
      int baselineMinimumSamples,
      float baselineMaximumRelativeSpread,
      float smoothingSpeed,
      float normalMinimumRatio,
      float normalMaximumRatio,
      float pushAwayTriggerRatio,
      float pushAwayHoldSeconds,
      float pushAwayRearmRatio,
      float pushAwayRearmHoldSeconds,
      float tooCloseEnterRatio,
      float tooCloseHoldSeconds,
      float tooCloseExitRatio)
    {
      BaselineCaptureSeconds = Mathf.Max(0.1f, baselineCaptureSeconds);
      BaselineMinimumSamples = Mathf.Max(3, baselineMinimumSamples);
      BaselineMaximumRelativeSpread = Mathf.Max(0.001f, baselineMaximumRelativeSpread);
      SmoothingSpeed = Mathf.Max(0.01f, smoothingSpeed);
      NormalMinimumRatio = Mathf.Max(0.01f, normalMinimumRatio);
      NormalMaximumRatio = Mathf.Max(NormalMinimumRatio, normalMaximumRatio);
      PushAwayTriggerRatio = Mathf.Clamp(pushAwayTriggerRatio, 0.01f, NormalMinimumRatio);
      PushAwayHoldSeconds = Mathf.Max(0f, pushAwayHoldSeconds);
      PushAwayRearmRatio = Mathf.Max(PushAwayTriggerRatio, pushAwayRearmRatio);
      PushAwayRearmHoldSeconds = Mathf.Max(0f, pushAwayRearmHoldSeconds);
      TooCloseEnterRatio = Mathf.Max(NormalMaximumRatio, tooCloseEnterRatio);
      TooCloseHoldSeconds = Mathf.Max(0f, tooCloseHoldSeconds);
      TooCloseExitRatio = Mathf.Clamp(tooCloseExitRatio, NormalMinimumRatio, TooCloseEnterRatio);
    }

    public float BaselineCaptureSeconds { get; }
    public int BaselineMinimumSamples { get; }
    public float BaselineMaximumRelativeSpread { get; }
    public float SmoothingSpeed { get; }
    public float NormalMinimumRatio { get; }
    public float NormalMaximumRatio { get; }
    public float PushAwayTriggerRatio { get; }
    public float PushAwayHoldSeconds { get; }
    public float PushAwayRearmRatio { get; }
    public float PushAwayRearmHoldSeconds { get; }
    public float TooCloseEnterRatio { get; }
    public float TooCloseHoldSeconds { get; }
    public float TooCloseExitRatio { get; }
  }

  public sealed class SessionDistanceTracker
  {
    private const float MinimumValidFaceScale = 0.000001f;

    private readonly List<float> _baselineSamples = new List<float>(180);
    private float _baselineCaptureStartedAt = -1f;
    private float _pushAwayCandidateStartedAt = -1f;
    private float _rearmCandidateStartedAt = -1f;
    private float _tooCloseCandidateStartedAt = -1f;
    private bool _hasSmoothedFaceScale;
    private bool _pushAwayArmed;
    private bool _pushAwayReady;
    private bool _pushAwayTriggeredSinceRearm;
    private bool _tooClose;

    public float BaselineFaceScale { get; private set; } = -1f;
    public float CurrentFaceScale { get; private set; }
    public float SmoothedFaceScale { get; private set; }
    public float DistanceRatio { get; private set; } = 1f;
    public float BaselineRelativeSpread { get; private set; }
    public bool HasBaseline => IsValidScale(BaselineFaceScale);
    public bool HasValidSample { get; private set; }
    public bool IsCapturingBaseline => !HasBaseline;
    public bool IsPushAwayArmed => _pushAwayArmed;
    public bool IsPushAwayReady => _pushAwayReady;
    public bool PushAwayTriggeredSinceRearm => _pushAwayTriggeredSinceRearm;
    public bool IsTooClose => _tooClose;
    public int BaselineSampleCount => _baselineSamples.Count;
    public SessionDistanceState State { get; private set; } = SessionDistanceState.WaitingForBaseline;

    public float BaselineCaptureElapsed(float now)
    {
      return _baselineCaptureStartedAt < 0f ? 0f : Mathf.Max(0f, now - _baselineCaptureStartedAt);
    }

    public float PushAwayCandidateElapsed(float now)
    {
      return _pushAwayCandidateStartedAt < 0f ? 0f : Mathf.Max(0f, now - _pushAwayCandidateStartedAt);
    }

    public float RearmCandidateElapsed(float now)
    {
      return _rearmCandidateStartedAt < 0f ? 0f : Mathf.Max(0f, now - _rearmCandidateStartedAt);
    }

    public void ResetSession()
    {
      BaselineFaceScale = -1f;
      CurrentFaceScale = 0f;
      SmoothedFaceScale = 0f;
      DistanceRatio = 1f;
      BaselineRelativeSpread = 0f;
      HasValidSample = false;
      State = SessionDistanceState.WaitingForBaseline;
      _baselineSamples.Clear();
      _baselineCaptureStartedAt = -1f;
      _hasSmoothedFaceScale = false;
      _pushAwayArmed = false;
      _pushAwayReady = false;
      _pushAwayTriggeredSinceRearm = false;
      _tooClose = false;
      ResetCandidates();
    }

    public SessionDistanceUpdate Update(
      float currentFaceScale,
      bool sampleValid,
      bool calibrationComplete,
      bool distanceStateAllowed,
      bool hasCollectableSamples,
      float now,
      float deltaTime,
      SessionDistanceSettings settings)
    {
      var baselineCaptured = false;
      var baselineRejected = false;
      var readyJustBecame = false;
      var pushAwayTriggered = false;
      var tooCloseChanged = false;

      sampleValid = sampleValid && IsValidScale(currentFaceScale);
      HasValidSample = sampleValid;
      CurrentFaceScale = sampleValid ? currentFaceScale : 0f;

      if (!calibrationComplete)
      {
        SuspendForCalibration();
        return new SessionDistanceUpdate(false, false, false, false, false);
      }

      if (!HasBaseline)
      {
        if (!distanceStateAllowed)
        {
          RestartBaselineWindow();
          State = sampleValid ? SessionDistanceState.Paused : SessionDistanceState.TrackingLost;
          return new SessionDistanceUpdate(false, false, false, false, false);
        }

        if (!sampleValid)
        {
          RestartBaselineWindow();
          State = SessionDistanceState.TrackingLost;
          return new SessionDistanceUpdate(false, false, false, false, false);
        }

        if (_baselineCaptureStartedAt < 0f)
        {
          _baselineCaptureStartedAt = now;
        }
        _baselineSamples.Add(currentFaceScale);
        State = SessionDistanceState.WaitingForBaseline;

        if (now - _baselineCaptureStartedAt >= settings.BaselineCaptureSeconds &&
            _baselineSamples.Count >= settings.BaselineMinimumSamples)
        {
          _baselineSamples.Sort();
          var median = Percentile(_baselineSamples, 0.5f);
          var low = Percentile(_baselineSamples, 0.1f);
          var high = Percentile(_baselineSamples, 0.9f);
          var relativeSpread = IsValidScale(median) ? Mathf.Max(0f, high - low) / median : float.PositiveInfinity;

          if (!IsValidScale(median) || relativeSpread > settings.BaselineMaximumRelativeSpread)
          {
            BaselineRelativeSpread = relativeSpread;
            RestartBaselineWindow();
            baselineRejected = true;
          }
          else
          {
            BaselineFaceScale = median;
            BaselineRelativeSpread = relativeSpread;
            SmoothedFaceScale = median;
            DistanceRatio = 1f;
            _hasSmoothedFaceScale = true;
            _rearmCandidateStartedAt = now;
            State = SessionDistanceState.Normal;
            baselineCaptured = true;
          }
        }

        return new SessionDistanceUpdate(baselineCaptured, baselineRejected, false, false, false);
      }

      if (!sampleValid)
      {
        _pushAwayReady = false;
        _tooClose = false;
        ResetCandidates();
        State = SessionDistanceState.TrackingLost;
        return new SessionDistanceUpdate(false, false, false, false, false);
      }

      if (!_hasSmoothedFaceScale)
      {
        SmoothedFaceScale = currentFaceScale;
        _hasSmoothedFaceScale = true;
      }
      else
      {
        var smoothing = 1f - Mathf.Exp(-settings.SmoothingSpeed * Mathf.Max(0f, deltaTime));
        SmoothedFaceScale = Mathf.Lerp(SmoothedFaceScale, currentFaceScale, smoothing);
      }
      DistanceRatio = SmoothedFaceScale / BaselineFaceScale;

      if (!distanceStateAllowed)
      {
        _pushAwayReady = false;
        if (_tooClose)
        {
          _tooClose = false;
          tooCloseChanged = true;
        }
        ResetCandidates();
        State = SessionDistanceState.Paused;
        return new SessionDistanceUpdate(false, false, false, false, tooCloseChanged);
      }

      var wasTooClose = _tooClose;
      UpdateTooClose(now, deltaTime, settings);
      tooCloseChanged = wasTooClose != _tooClose;
      UpdateRearm(now, deltaTime, settings);

      var wasReady = _pushAwayReady;
      _pushAwayReady = _pushAwayArmed && hasCollectableSamples && !_tooClose;
      readyJustBecame = _pushAwayReady && !wasReady;

      if (_pushAwayReady && DistanceRatio <= settings.PushAwayTriggerRatio)
      {
        if (_pushAwayCandidateStartedAt < 0f)
        {
          _pushAwayCandidateStartedAt = now - Mathf.Max(0f, deltaTime);
        }

        if (now - _pushAwayCandidateStartedAt >= settings.PushAwayHoldSeconds)
        {
          _pushAwayArmed = false;
          _pushAwayReady = false;
          _pushAwayTriggeredSinceRearm = true;
          _pushAwayCandidateStartedAt = -1f;
          _rearmCandidateStartedAt = -1f;
          pushAwayTriggered = true;
        }
      }
      else
      {
        _pushAwayCandidateStartedAt = -1f;
      }

      UpdateStateLabel(settings);
      return new SessionDistanceUpdate(false, false, readyJustBecame, pushAwayTriggered, tooCloseChanged);
    }

    public static string GetStateLabel(SessionDistanceState state)
    {
      switch (state)
      {
        case SessionDistanceState.WaitingForBaseline:
          return "WAITING FOR BASELINE";
        case SessionDistanceState.TrackingLost:
          return "TRACKING LOST";
        case SessionDistanceState.Paused:
          return "PAUSED";
        case SessionDistanceState.PushAwayCandidate:
          return "PUSH AWAY CANDIDATE";
        case SessionDistanceState.TooCloseCandidate:
          return "TOO CLOSE CANDIDATE";
        case SessionDistanceState.TooClose:
          return "TOO CLOSE";
        case SessionDistanceState.Far:
          return "FAR";
        case SessionDistanceState.Near:
          return "NEAR";
        default:
          return "NORMAL";
      }
    }

    private void SuspendForCalibration()
    {
      HasValidSample = false;
      CurrentFaceScale = 0f;
      State = SessionDistanceState.WaitingForBaseline;
      RestartBaselineWindow();
      _pushAwayReady = false;
      _tooClose = false;
      ResetCandidates();
    }

    private void RestartBaselineWindow()
    {
      _baselineSamples.Clear();
      _baselineCaptureStartedAt = -1f;
    }

    private void UpdateTooClose(float now, float deltaTime, SessionDistanceSettings settings)
    {
      if (_tooClose)
      {
        if (DistanceRatio <= settings.TooCloseExitRatio)
        {
          _tooClose = false;
          _tooCloseCandidateStartedAt = -1f;
        }
        return;
      }

      if (DistanceRatio < settings.TooCloseEnterRatio)
      {
        _tooCloseCandidateStartedAt = -1f;
        return;
      }

      if (_tooCloseCandidateStartedAt < 0f)
      {
        _tooCloseCandidateStartedAt = now - Mathf.Max(0f, deltaTime);
      }

      if (now - _tooCloseCandidateStartedAt >= settings.TooCloseHoldSeconds)
      {
        _tooClose = true;
        _tooCloseCandidateStartedAt = -1f;
      }
    }

    private void UpdateRearm(float now, float deltaTime, SessionDistanceSettings settings)
    {
      if (_pushAwayArmed)
      {
        _rearmCandidateStartedAt = -1f;
        return;
      }

      if (DistanceRatio < settings.PushAwayRearmRatio)
      {
        _rearmCandidateStartedAt = -1f;
        return;
      }

      if (_rearmCandidateStartedAt < 0f)
      {
        _rearmCandidateStartedAt = now - Mathf.Max(0f, deltaTime);
      }

      if (now - _rearmCandidateStartedAt >= settings.PushAwayRearmHoldSeconds)
      {
        _pushAwayArmed = true;
        _pushAwayTriggeredSinceRearm = false;
        _rearmCandidateStartedAt = -1f;
      }
    }

    private void UpdateStateLabel(SessionDistanceSettings settings)
    {
      if (_tooClose)
      {
        State = SessionDistanceState.TooClose;
      }
      else if (_tooCloseCandidateStartedAt >= 0f)
      {
        State = SessionDistanceState.TooCloseCandidate;
      }
      else if (_pushAwayCandidateStartedAt >= 0f)
      {
        State = SessionDistanceState.PushAwayCandidate;
      }
      else if (DistanceRatio < settings.NormalMinimumRatio)
      {
        State = SessionDistanceState.Far;
      }
      else if (DistanceRatio > settings.NormalMaximumRatio)
      {
        State = SessionDistanceState.Near;
      }
      else
      {
        State = SessionDistanceState.Normal;
      }
    }

    private void ResetCandidates()
    {
      _pushAwayCandidateStartedAt = -1f;
      _rearmCandidateStartedAt = -1f;
      _tooCloseCandidateStartedAt = -1f;
    }

    private static bool IsValidScale(float value)
    {
      return value > MinimumValidFaceScale && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static float Percentile(IReadOnlyList<float> sortedValues, float percentile)
    {
      if (sortedValues == null || sortedValues.Count == 0)
      {
        return 0f;
      }

      var position = Mathf.Clamp01(percentile) * (sortedValues.Count - 1);
      var lower = Mathf.FloorToInt(position);
      var upper = Mathf.CeilToInt(position);
      return Mathf.Lerp(sortedValues[lower], sortedValues[upper], position - lower);
    }
  }
}
