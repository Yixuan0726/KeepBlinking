using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace KeepBlinking.CareStation
{
  public enum CareVoicePriority
  {
    Direction = 0,
    Instruction = 1,
    Completion = 2,
  }

  /// <summary>
  /// English-only care narration. iOS uses the system speech synthesizer, so
  /// no voice recording or network request is required. Other platforms keep
  /// the same timing/ducking contract and show the English line in the UI.
  /// </summary>
  public sealed class CareVoiceService : MonoBehaviour
  {
    [Serializable]
    private sealed class VoiceClipOverride
    {
      public string key;
      public AudioClip clip;
    }

    [SerializeField] private bool _voiceEnabled = true;
    [SerializeField, Range(0f, 1f)] private float _voiceVolume = 0.78f;
    [SerializeField, Range(0.3f, 0.6f)] private float _iosSpeechRate = 0.43f;
    [SerializeField, Range(0.8f, 1.2f)] private float _iosSpeechPitch = 0.96f;
    [SerializeField] private VoiceClipOverride[] _clipOverrides = Array.Empty<VoiceClipOverride>();

    private readonly List<VoiceRequest> _pending = new List<VoiceRequest>(16);
    private float _speakingUntil;
    private float _pausedAt = -1f;
    private AudioSource _voiceSource;
    private bool _hasCurrentRequest;
    private CareVoicePriority _currentPriority;
    public static CareVoiceService Instance { get; private set; }
    public bool IsSpeaking => _hasCurrentRequest && (IsPaused || Time.unscaledTime < _speakingUntil);
    public bool IsPaused => _pausedAt >= 0f;
    public string LastSpokenText { get; private set; } = string.Empty;
    public string LastSpokenKey { get; private set; } = string.Empty;
    public string LastRequestedText { get; private set; } = string.Empty;
    public string LastRequestedKey { get; private set; } = string.Empty;
    public int SpeechRequestCount { get; private set; }

    public static CareVoiceService EnsureExists()
    {
      if (Instance == null) Instance = FindFirstObjectByType<CareVoiceService>();
      if (Instance != null) return Instance;
      var owner = new GameObject("Care Voice");
      Instance = owner.AddComponent<CareVoiceService>();
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
      _voiceSource = gameObject.AddComponent<AudioSource>();
      _voiceSource.playOnAwake = false;
      _voiceSource.loop = false;
      _voiceSource.spatialBlend = 0f;
      _voiceSource.priority = 16;
      _voiceSource.ignoreListenerPause = true;
    }

    public void Speak(string englishText, float estimatedSeconds = 4f)
    {
      Speak(string.Empty, englishText, estimatedSeconds, CareVoicePriority.Instruction);
    }

    public void Speak(string cueKey, string englishText, float estimatedSeconds = 4f)
    {
      Speak(cueKey, englishText, estimatedSeconds, CareVoicePriority.Instruction);
    }

    public void Speak(
      string cueKey,
      string englishText,
      float estimatedSeconds,
      CareVoicePriority priority)
    {
      if (!_voiceEnabled || string.IsNullOrWhiteSpace(englishText)) return;
      SpeechRequestCount++;
      var request = new VoiceRequest(cueKey, englishText, estimatedSeconds, priority);
      LastRequestedKey = request.Key;
      LastRequestedText = request.Text;
      if (IsPaused)
      {
        EnqueueRequest(request);
        return;
      }
      if (_hasCurrentRequest)
      {
        // A new state owns the current instruction. Keeping an older direction
        // or instruction talking after the visual has moved on is worse than a
        // gentle interruption; completion narration always remains dominant.
        if (priority > _currentPriority ||
            (priority == _currentPriority && priority != CareVoicePriority.Completion))
        {
          StopCurrentSpeech();
          StartRequest(request);
        }
        else EnqueueRequest(request);
        return;
      }
      StartRequest(request);
    }

    /// <summary>
    /// Pilot target words belong to the moving guide, so a word that waits
    /// behind an older instruction is no longer useful. Keep Completion voice
    /// dominant, but otherwise replace the current narration immediately and
    /// discard any older queued direction word.
    /// </summary>
    public void SpeakSynchronizedDirection(string cueKey, string englishText, float estimatedSeconds = 0.55f)
    {
      if (!_voiceEnabled || string.IsNullOrWhiteSpace(englishText)) return;
      SpeechRequestCount++;
      var request = new VoiceRequest(
        cueKey,
        englishText,
        Mathf.Clamp(estimatedSeconds, 0.35f, 1.1f),
        CareVoicePriority.Direction);
      LastRequestedKey = request.Key;
      LastRequestedText = request.Text;
      _pending.RemoveAll(item => item.Priority == CareVoicePriority.Direction);
      if (IsPaused || (_hasCurrentRequest && _currentPriority == CareVoicePriority.Completion))
      {
        EnqueueRequest(request);
        return;
      }
      if (_hasCurrentRequest) StopCurrentSpeech();
      StartRequest(request);
    }

    private void EnqueueRequest(VoiceRequest request)
    {
      if (request.Priority == CareVoicePriority.Completion)
      {
        // Once a completion state has been reached, queued direction phrases
        // from the previous phase are stale and must never replay afterward.
        _pending.RemoveAll(item => item.Priority < CareVoicePriority.Completion);
      }
      else if (request.Priority == CareVoicePriority.Direction)
      {
        // Direction guidance follows a moving target. Retain only the newest
        // pending direction so speech cannot lag multiple endpoints behind.
        _pending.RemoveAll(item => item.Priority == CareVoicePriority.Direction);
      }
      if (_pending.Count >= 32) _pending.RemoveAt(0);
      _pending.Add(request);
    }

    private bool TryTakeNext(out VoiceRequest request)
    {
      request = default;
      if (_pending.Count == 0) return false;
      var best = 0;
      for (var i = 1; i < _pending.Count; i++)
        if (_pending[i].Priority > _pending[best].Priority) best = i;
      request = _pending[best];
      _pending.RemoveAt(best);
      return true;
    }

    private void StartRequest(VoiceRequest request)
    {
      _hasCurrentRequest = true;
      _currentPriority = request.Priority;
      LastSpokenKey = request.Key;
      LastSpokenText = request.Text;
      var clip = FindOverride(request.Key);
      var duration = clip != null ? clip.length : Mathf.Max(0.5f, request.EstimatedSeconds);
      // Keep the music bed lowered for a short release after the estimated
      // utterance. This avoids an abrupt level jump behind the last word.
      _speakingUntil = Time.unscaledTime + duration + 0.6f;
      CareAudioFeedbackControllerProxy.SetVoiceDucking(true);
      if (clip != null && _voiceSource != null)
      {
        _voiceSource.clip = clip;
        _voiceSource.volume = _voiceVolume;
        _voiceSource.Play();
      }
#if UNITY_IOS && !UNITY_EDITOR
      else CareVoiceSpeak(request.Text, _iosSpeechRate, _iosSpeechPitch, _voiceVolume);
#elif UNITY_EDITOR || DEVELOPMENT_BUILD
      Debug.Log($"[CareVoice:{request.Key}] {request.Text}");
#endif
    }

    public void Stop()
    {
      _pending.Clear();
      StopCurrentSpeech();
      _pausedAt = -1f;
      CareAudioFeedbackControllerProxy.SetVoiceDucking(false);
    }

    private void StopCurrentSpeech()
    {
#if UNITY_IOS && !UNITY_EDITOR
      CareVoiceStop();
#endif
      if (_voiceSource != null) _voiceSource.Stop();
      _speakingUntil = 0f;
      _hasCurrentRequest = false;
    }

    public void SetPaused(bool paused)
    {
      if (paused == IsPaused) return;
      if (paused)
      {
        _pausedAt = Time.unscaledTime;
        if (_voiceSource != null && _voiceSource.isPlaying) _voiceSource.Pause();
#if UNITY_IOS && !UNITY_EDITOR
        CareVoicePause();
#endif
      }
      else
      {
        if (_pausedAt >= 0f && _speakingUntil > 0f)
          _speakingUntil += Mathf.Max(0f, Time.unscaledTime - _pausedAt);
        _pausedAt = -1f;
        if (_voiceSource != null && _voiceSource.clip != null) _voiceSource.UnPause();
#if UNITY_IOS && !UNITY_EDITOR
        CareVoiceResume();
#endif
        if (_hasCurrentRequest && _pending.Count > 0)
        {
          var bestPriority = _pending[0].Priority;
          for (var i = 1; i < _pending.Count; i++)
            if (_pending[i].Priority > bestPriority) bestPriority = _pending[i].Priority;
          if (bestPriority > _currentPriority && TryTakeNext(out var urgent))
          {
            StopCurrentSpeech();
            StartRequest(urgent);
          }
        }
        else if (!_hasCurrentRequest && TryTakeNext(out var next)) StartRequest(next);
      }
    }

    private void Update()
    {
      if (IsPaused || _speakingUntil <= 0f || Time.unscaledTime < _speakingUntil) return;
      _speakingUntil = 0f;
      _hasCurrentRequest = false;
      if (TryTakeNext(out var next)) StartRequest(next);
      else CareAudioFeedbackControllerProxy.SetVoiceDucking(false);
    }

    private AudioClip FindOverride(string key)
    {
      if (string.IsNullOrEmpty(key) || _clipOverrides == null) return null;
      for (var i = 0; i < _clipOverrides.Length; i++)
      {
        var item = _clipOverrides[i];
        if (item != null && item.clip != null &&
            string.Equals(item.key, key, StringComparison.OrdinalIgnoreCase)) return item.clip;
      }
      return null;
    }

    private void OnDestroy()
    {
      _pending.Clear();
      StopCurrentSpeech();
      if (KeepBlinking.Gameplay.CareAudioFeedbackController.Instance != null)
        KeepBlinking.Gameplay.CareAudioFeedbackController.Instance.SetVoiceDucking(false);
      if (Instance == this) Instance = null;
    }

    private readonly struct VoiceRequest
    {
      public readonly string Key;
      public readonly string Text;
      public readonly float EstimatedSeconds;
      public readonly CareVoicePriority Priority;

      public VoiceRequest(string key, string text, float estimatedSeconds, CareVoicePriority priority)
      {
        Key = key ?? string.Empty;
        Text = text;
        EstimatedSeconds = estimatedSeconds;
        Priority = priority;
      }
    }

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void CareVoiceSpeak(string text, float rate, float pitch, float volume);

    [DllImport("__Internal")]
    private static extern void CareVoiceStop();

    [DllImport("__Internal")]
    private static extern void CareVoicePause();

    [DllImport("__Internal")]
    private static extern void CareVoiceResume();
#endif
  }

  internal static class CareAudioFeedbackControllerProxy
  {
    public static void SetVoiceDucking(bool active)
    {
      KeepBlinking.Gameplay.CareAudioFeedbackController.EnsureExists().SetVoiceDucking(active);
    }
  }
}
