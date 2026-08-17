using UnityEngine;

#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace KeepBlinking.Gameplay
{
  public sealed class CareAudioFeedbackController : MonoBehaviour
  {
    [SerializeField, Range(0f, 1f)] private float _fragmentVolume = 0.13f;
    [SerializeField, Range(0f, 1f)] private float _stepVolume = 0.25f;
    // Procedural clips carry their own gentle envelope. 0.42 keeps the two
    // safety cues plainly audible without approaching the louder reward mix.
    [SerializeField, Range(0f, 1f)] private float _guidedVolume = 0.42f;

    private AudioSource _source;
    private AudioSource _guidedSource;
    private AudioListener _fallbackListener;
    private AudioClip[] _fragmentNotes;
    private AudioClip[] _fragmentHarmonyNotes;
    private AudioClip _sweepStart;
    private AudioClip _sweepComplete;
    private AudioClip _stepComplete;
    private AudioClip _pushAway;
    private AudioClip[] _guidedClockwiseNotes;
    private AudioClip[] _guidedCounterClockwiseNotes;
    private AudioClip _guidedCloseRequest;
    private AudioClip _guidedCenterPause;
    private AudioClip _guidedCompletion;
    private AudioClip _guidedTrackingPause;
    private int _noteIndex;
    private float _nextListenerCheckAt;
    private string _lastGuidedCue = "NONE";
    private float _lastGuidedCueAt = -1f;

    public static CareAudioFeedbackController Instance { get; private set; }

    public static CareAudioFeedbackController EnsureExists()
    {
      if (Instance == null) Instance = FindFirstObjectByType<CareAudioFeedbackController>();
      if (Instance != null)
      {
        Instance.EnsureAudioOutputs();
        return Instance;
      }
      var owner = new GameObject("Care Audio Feedback");
      Instance = owner.AddComponent<CareAudioFeedbackController>();
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
      _source = gameObject.AddComponent<AudioSource>();
      ConfigureSource(_source, 128);
      // Safety instructions use their own voice so a simultaneous station,
      // bottle, or reward sound cannot stop the close/open cue mid-play.
      _guidedSource = gameObject.AddComponent<AudioSource>();
      ConfigureSource(_guidedSource, 24);
      EnsureListenerState();
      _fragmentNotes = new[]
      {
        CreateTone("Care XP 1", 349.23f, 0.064f),
        CreateTone("Care XP 2", 392f, 0.064f),
        CreateTone("Care XP 3", 440f, 0.064f),
        CreateTone("Care XP 4", 466.16f, 0.064f),
        CreateTone("Care XP 5", 523.25f, 0.064f),
        CreateTone("Care XP 6", 587.33f, 0.064f),
        CreateTone("Care XP 7", 659.25f, 0.064f),
        CreateTone("Care XP 8", 698.46f, 0.072f),
      };
      _fragmentHarmonyNotes = new[]
      {
        CreateChord("Care XP Harmony 1", 349.23f, 523.25f, 0.078f),
        CreateChord("Care XP Harmony 2", 392f, 587.33f, 0.078f),
        CreateChord("Care XP Harmony 3", 440f, 659.25f, 0.078f),
        CreateChord("Care XP Harmony 4", 466.16f, 698.46f, 0.078f),
        CreateChord("Care XP Harmony 5", 523.25f, 783.99f, 0.078f),
        CreateChord("Care XP Harmony 6", 587.33f, 880f, 0.078f),
        CreateChord("Care XP Harmony 7", 659.25f, 987.77f, 0.078f),
        CreateChord("Care XP Harmony 8", 698.46f, 1046.5f, 0.086f),
      };
      _sweepStart = CreateChord("Care Sweep Start", 293.66f, 440f, 0.13f);
      _sweepComplete = CreateChord("Care Sweep Complete", 523.25f, 659.25f, 0.24f);
      _stepComplete = CreateTone("Care Direction Complete", 659.25f, 0.23f);
      _pushAway = CreateTone("Care Push Away", 349.23f, 0.18f);
      // The guide uses slower, longer tones and a separate pitch contour from
      // reward, movement, Screen-Down Rest, and Boss cues.
      _guidedClockwiseNotes = new[]
      {
        CreateTone("Guided Clockwise 1", 261.63f, 0.24f),
        CreateTone("Guided Clockwise 2", 293.66f, 0.24f),
        CreateTone("Guided Clockwise 3", 329.63f, 0.24f),
        CreateTone("Guided Clockwise 4", 392f, 0.24f),
        CreateTone("Guided Clockwise 5", 440f, 0.24f),
        CreateTone("Guided Clockwise 6", 523.25f, 0.26f),
        CreateTone("Guided Clockwise 7", 587.33f, 0.26f),
        CreateTone("Guided Clockwise 8", 659.25f, 0.28f),
      };
      _guidedCounterClockwiseNotes = new[]
      {
        CreateTone("Guided Counter 1", 622.25f, 0.24f),
        CreateTone("Guided Counter 2", 554.37f, 0.24f),
        CreateTone("Guided Counter 3", 466.16f, 0.24f),
        CreateTone("Guided Counter 4", 415.30f, 0.24f),
        CreateTone("Guided Counter 5", 349.23f, 0.24f),
        CreateTone("Guided Counter 6", 311.13f, 0.26f),
        CreateTone("Guided Counter 7", 277.18f, 0.26f),
        CreateTone("Guided Counter 8", 233.08f, 0.28f),
      };
      // A low, clearly separated double pulse means "close now". It must not
      // resemble the longer, brighter single cue used when reopening is safe.
      _guidedCloseRequest = CreateDoublePulse("Closed-Eye Close Request", 196f, 0.11f, 0.18f);
      _guidedCenterPause = CreateChord("Guided Center Pause", 196f, 293.66f, 0.30f);
      _guidedCompletion = CreateRisingTone("Ready To Open Cue", 392f, 587.33f, 0.58f);
      _guidedTrackingPause = CreateTone("Guided Tracking Pause", 174.61f, 0.20f);
    }

    public void PlayFragment()
    {
      var progress = _fragmentNotes == null || _fragmentNotes.Length <= 1
        ? 0f
        : Mathf.Clamp01(_noteIndex / (float)(_fragmentNotes.Length - 1));
      PlayFragment(progress);
    }

    public void PlayFragment(float normalizedTrackProgress)
    {
      if (_fragmentNotes == null || _fragmentNotes.Length == 0) return;
      var index = Mathf.Clamp(
        Mathf.RoundToInt(Mathf.Clamp01(normalizedTrackProgress) * (_fragmentNotes.Length - 1)),
        0,
        _fragmentNotes.Length - 1);
      var useHarmony = (_noteIndex + 1) % 4 == 0 &&
                       _fragmentHarmonyNotes != null &&
                       index < _fragmentHarmonyNotes.Length;
      PlayExclusive(useHarmony ? _fragmentHarmonyNotes[index] : _fragmentNotes[index], _fragmentVolume);
      _noteIndex++;
      if (_noteIndex % 5 == 0) PulseLight();
    }

    public void PlaySweepStart()
    {
      _noteIndex = 0;
      PlayExclusive(_sweepStart, _stepVolume * 0.72f);
    }

    public void PlaySweepComplete()
    {
      PlayExclusive(_sweepComplete, _stepVolume);
      PulseLight();
    }

    public void PlaySweepEnd()
    {
      PlaySweepComplete();
    }

    public void PlayStepComplete()
    {
      PlayExclusive(_stepComplete, _stepVolume);
      PulseLight();
    }

    public void PlayPushAway()
    {
      PlayExclusive(_pushAway, _stepVolume * 0.9f);
      PulseLight();
    }

    public void PlayGuidedCloseRequest()
    {
      PlayGuidedExclusive(_guidedCloseRequest, _guidedVolume * 0.90f, "CLOSE REQUEST");
    }

    public void PlayGuidedClockwiseNote(int noteIndex, int requestedNoteCount)
    {
      PlayGuidedNote(_guidedClockwiseNotes, noteIndex, requestedNoteCount);
    }

    public void PlayGuidedCounterClockwiseNote(int noteIndex, int requestedNoteCount)
    {
      PlayGuidedNote(_guidedCounterClockwiseNotes, noteIndex, requestedNoteCount);
    }

    public void PlayGuidedCenterPause()
    {
      PlayGuidedExclusive(_guidedCenterPause, _guidedVolume * 0.8f, "CENTER PAUSE");
    }

    public void PlayGuidedCompletion()
    {
      PlayGuidedExclusive(_guidedCompletion, _guidedVolume, "READY TO OPEN");
    }

    public void PlayGuidedTrackingPause()
    {
      PlayGuidedExclusive(_guidedTrackingPause, _guidedVolume * 0.55f, "TRACKING PAUSE");
    }

    public void StopGuidedCue()
    {
      if (_guidedSource != null) _guidedSource.Stop();
    }

    private void PlayGuidedNote(AudioClip[] notes, int noteIndex, int requestedNoteCount)
    {
      if (notes == null || notes.Length == 0) return;
      var denominator = Mathf.Max(1, requestedNoteCount - 1);
      var normalized = Mathf.Clamp01(noteIndex / (float)denominator);
      var clipIndex = Mathf.Clamp(Mathf.RoundToInt(normalized * (notes.Length - 1)), 0, notes.Length - 1);
      PlayGuidedExclusive(notes[clipIndex], _guidedVolume, "GUIDED NOTE");
    }

    private static void ConfigureSource(AudioSource source, int priority)
    {
      source.playOnAwake = false;
      source.spatialBlend = 0f;
      source.loop = false;
      source.priority = priority;
      source.ignoreListenerPause = true;
      source.bypassReverbZones = true;
    }

    private void EnsureAudioOutputs()
    {
      if (_source == null)
      {
        _source = gameObject.AddComponent<AudioSource>();
        ConfigureSource(_source, 128);
      }
      if (_guidedSource == null)
      {
        _guidedSource = gameObject.AddComponent<AudioSource>();
        ConfigureSource(_guidedSource, 24);
      }
      EnsureListenerState();
    }

    private void Update()
    {
      if (Time.unscaledTime < _nextListenerCheckAt) return;
      _nextListenerCheckAt = Time.unscaledTime + 1f;
      EnsureListenerState();
    }

    private void EnsureListenerState()
    {
      var listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None);
      var hasExternalListener = false;
      for (var i = 0; i < listeners.Length; i++)
      {
        var listener = listeners[i];
        if (listener == null || listener == _fallbackListener) continue;
        if (listener.enabled && listener.gameObject.activeInHierarchy)
        {
          hasExternalListener = true;
          break;
        }
      }

      if (hasExternalListener)
      {
        if (_fallbackListener != null) _fallbackListener.enabled = false;
        return;
      }
      if (_fallbackListener == null) _fallbackListener = gameObject.AddComponent<AudioListener>();
      _fallbackListener.enabled = true;
    }

    private void PlayGuidedExclusive(AudioClip clip, float volume, string cueName)
    {
      EnsureAudioOutputs();
      if (_guidedSource == null || clip == null) return;
      _guidedSource.Stop();
      _guidedSource.clip = clip;
      _guidedSource.volume = Mathf.Clamp01(volume);
      _guidedSource.Play();
      _lastGuidedCue = cueName;
      _lastGuidedCueAt = Time.unscaledTime;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public string DevelopmentAudioDiagnostics
    {
      get
      {
        var listenerCount = 0;
        var listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (var i = 0; i < listeners.Length; i++)
          if (listeners[i] != null && listeners[i].enabled) listenerCount++;
        var age = _lastGuidedCueAt < 0f ? -1f : Mathf.Max(0f, Time.unscaledTime - _lastGuidedCueAt);
        return
          $"Audio Listeners: {listenerCount}  Paused: {AudioListener.pause}  Volume: {AudioListener.volume:0.00}\n" +
          $"Cue: {_lastGuidedCue}  Age: {(age < 0f ? "--" : age.ToString("0.0") + "s")}  " +
          $"Playing: {(_guidedSource != null && _guidedSource.isPlaying)}  Source: {(_guidedSource != null ? _guidedSource.volume : 0f):0.00}";
      }
    }
#endif

    private void PlayExclusive(AudioClip clip, float volume)
    {
      if (_source == null || clip == null) return;
      // A single non-overlapping source keeps dense reward runs from building
      // dozens of simultaneous voices on iPhone.
      _source.Stop();
      _source.clip = clip;
      _source.volume = Mathf.Clamp01(volume);
      _source.Play();
    }

    private static AudioClip CreateTone(string name, float frequency, float duration)
    {
      const int sampleRate = 24000;
      var count = Mathf.Max(1, Mathf.RoundToInt(sampleRate * duration));
      var data = new float[count];
      for (var i = 0; i < count; i++)
      {
        var t = i / (float)sampleRate;
        var envelope = Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI);
        data[i] = Mathf.Sin(t * Mathf.PI * 2f * frequency) * envelope * 0.15f;
      }
      var clip = AudioClip.Create(name, count, 1, sampleRate, false);
      clip.SetData(data, 0);
      return clip;
    }

    private static AudioClip CreateChord(string name, float firstFrequency, float secondFrequency, float duration)
    {
      const int sampleRate = 24000;
      var count = Mathf.Max(1, Mathf.RoundToInt(sampleRate * duration));
      var data = new float[count];
      for (var i = 0; i < count; i++)
      {
        var t = i / (float)sampleRate;
        var envelope = Mathf.Sin(Mathf.Clamp01(t / duration) * Mathf.PI);
        var first = Mathf.Sin(t * Mathf.PI * 2f * firstFrequency);
        var second = Mathf.Sin(t * Mathf.PI * 2f * secondFrequency);
        data[i] = (first * 0.11f + second * 0.055f) * envelope;
      }
      var clip = AudioClip.Create(name, count, 1, sampleRate, false);
      clip.SetData(data, 0);
      return clip;
    }

    private static AudioClip CreateDoublePulse(string name, float frequency, float pulseSeconds, float gapSeconds)
    {
      const int sampleRate = 24000;
      pulseSeconds = Mathf.Max(0.04f, pulseSeconds);
      gapSeconds = Mathf.Max(0.08f, gapSeconds);
      var totalDuration = pulseSeconds * 2f + gapSeconds;
      var count = Mathf.Max(1, Mathf.RoundToInt(sampleRate * totalDuration));
      var data = new float[count];
      for (var i = 0; i < count; i++)
      {
        var t = i / (float)sampleRate;
        var local = t < pulseSeconds
          ? t
          : t >= pulseSeconds + gapSeconds ? t - pulseSeconds - gapSeconds : -1f;
        if (local < 0f || local > pulseSeconds) continue;
        var envelope = Mathf.Sin(Mathf.Clamp01(local / pulseSeconds) * Mathf.PI);
        data[i] = Mathf.Sin(local * Mathf.PI * 2f * frequency) * envelope * 0.32f;
      }
      var clip = AudioClip.Create(name, count, 1, sampleRate, false);
      clip.SetData(data, 0);
      return clip;
    }

    private static AudioClip CreateRisingTone(
      string name,
      float startFrequency,
      float endFrequency,
      float duration)
    {
      const int sampleRate = 24000;
      duration = Mathf.Max(0.1f, duration);
      var count = Mathf.Max(1, Mathf.RoundToInt(sampleRate * duration));
      var data = new float[count];
      var phase = 0f;
      for (var i = 0; i < count; i++)
      {
        var normalized = i / (float)Mathf.Max(1, count - 1);
        var frequency = Mathf.Lerp(startFrequency, endFrequency, Mathf.SmoothStep(0f, 1f, normalized));
        phase += Mathf.PI * 2f * frequency / sampleRate;
        var envelope = Mathf.Sin(normalized * Mathf.PI);
        data[i] = Mathf.Sin(phase) * envelope * 0.28f;
      }
      var clip = AudioClip.Create(name, count, 1, sampleRate, false);
      clip.SetData(data, 0);
      return clip;
    }

    public static void PulseLight()
    {
#if UNITY_IOS && !UNITY_EDITOR
      AudioServicesPlaySystemSound(1519);
#endif
    }

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void AudioServicesPlaySystemSound(uint soundId);
#endif

    private void OnDestroy()
    {
      DestroyClips(_fragmentNotes);
      DestroyClips(_fragmentHarmonyNotes);
      DestroyClip(_sweepStart);
      DestroyClip(_sweepComplete);
      DestroyClip(_stepComplete);
      DestroyClip(_pushAway);
      DestroyClips(_guidedClockwiseNotes);
      DestroyClips(_guidedCounterClockwiseNotes);
      DestroyClip(_guidedCloseRequest);
      DestroyClip(_guidedCenterPause);
      DestroyClip(_guidedCompletion);
      DestroyClip(_guidedTrackingPause);
      if (Instance == this) Instance = null;
    }

    private static void DestroyClips(AudioClip[] clips)
    {
      if (clips == null) return;
      for (var i = 0; i < clips.Length; i++) DestroyClip(clips[i]);
    }

    private static void DestroyClip(AudioClip clip)
    {
      if (clip != null) Destroy(clip);
    }
  }
}
