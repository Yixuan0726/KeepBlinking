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
    [SerializeField, Range(0f, 1f)] private float _guidedVolume = 0.18f;

    private AudioSource _source;
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

    public static CareAudioFeedbackController Instance { get; private set; }

    public static CareAudioFeedbackController EnsureExists()
    {
      if (Instance == null) Instance = FindFirstObjectByType<CareAudioFeedbackController>();
      if (Instance != null) return Instance;
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
      _source.playOnAwake = false;
      _source.spatialBlend = 0f;
      _source.loop = false;
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
      _guidedCloseRequest = CreateChord("Guided Close Request", 246.94f, 369.99f, 0.34f);
      _guidedCenterPause = CreateChord("Guided Center Pause", 196f, 293.66f, 0.30f);
      _guidedCompletion = CreateChord("Guided Completion Cue", 392f, 739.99f, 0.46f);
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
      PlayExclusive(_guidedCloseRequest, _guidedVolume);
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
      PlayExclusive(_guidedCenterPause, _guidedVolume * 0.8f);
    }

    public void PlayGuidedCompletion()
    {
      PlayExclusive(_guidedCompletion, _guidedVolume * 1.15f);
    }

    public void PlayGuidedTrackingPause()
    {
      PlayExclusive(_guidedTrackingPause, _guidedVolume * 0.55f);
    }

    public void StopGuidedCue()
    {
      if (_source != null) _source.Stop();
    }

    private void PlayGuidedNote(AudioClip[] notes, int noteIndex, int requestedNoteCount)
    {
      if (notes == null || notes.Length == 0) return;
      var denominator = Mathf.Max(1, requestedNoteCount - 1);
      var normalized = Mathf.Clamp01(noteIndex / (float)denominator);
      var clipIndex = Mathf.Clamp(Mathf.RoundToInt(normalized * (notes.Length - 1)), 0, notes.Length - 1);
      PlayExclusive(notes[clipIndex], _guidedVolume);
    }

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
