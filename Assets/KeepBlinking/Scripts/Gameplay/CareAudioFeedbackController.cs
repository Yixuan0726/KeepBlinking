using KeepBlinking.CareStation;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Serialization;

#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace KeepBlinking.Gameplay
{
  public sealed class CareAudioFeedbackController : MonoBehaviour
  {
    [Header("Care Routine Music")]
    [FormerlySerializedAs("_ambienceVolume")]
    [SerializeField, Range(0f, 1f)] private float _routineMusicVolume = 0.075f;
    [FormerlySerializedAs("_ambienceFadeInSeconds")]
    [SerializeField, Range(0.25f, 3f)] private float _routineMusicFadeInSeconds = 1.5f;
    [FormerlySerializedAs("_ambienceFadeOutSeconds")]
    [SerializeField, Range(0.25f, 2f)] private float _routineMusicFadeOutSeconds = 1f;
    [SerializeField, Range(-12f, -3f)] private float _voiceDuckDecibels = -6f;
    [SerializeField] private AudioClip _routineMusicOverride;
    [SerializeField] private string _routineMusicResourcePath =
      "CareStation/Audio/Music/LongNight_Aventure";
    [SerializeField] private string _routineMixerResourcePath =
      "CareStation/Audio/Music/CareRoutineMixer";

    [SerializeField, Range(0f, 1f)] private float _fragmentVolume = 0.13f;
    [SerializeField, Range(0f, 1f)] private float _stepVolume = 0.25f;
    // Procedural clips carry their own gentle envelope. 0.42 keeps the two
    // safety cues plainly audible without approaching the louder reward mix.
    [SerializeField, Range(0f, 1f)] private float _guidedVolume = 0.42f;

    private AudioSource _source;
    private AudioSource _guidedSource;
    private AudioSource _completionSource;
    private AudioSource _musicSource;
    private AudioListener _fallbackListener;
    private AudioClip[] _fragmentNotes;
    private AudioClip[] _fragmentHarmonyNotes;
    private AudioClip _sweepStart;
    private AudioClip _sweepComplete;
    private AudioClip _stepComplete;
    private AudioClip _pushAway;
    private AudioClip _upgradeUnavailable;
    private AudioClip[] _guidedClockwiseNotes;
    private AudioClip[] _guidedCounterClockwiseNotes;
    private AudioClip _guidedCloseRequest;
    private AudioClip _guidedCenterPause;
    private AudioClip _guidedCompletion;
    private AudioClip _guidedTrackingPause;
    private AudioClip _focusCloser;
    private AudioClip _focusAway;
    private AudioClip _focusCycle;
    private AudioClip _focusCompletion;
    private AudioClip _guidedLap;
    private AudioClip _guidedOpen;
    private AudioClip[] _pilotDirections;
    private AudioClip _pilotCenter;
    private AudioClip _pilotAxis;
    private AudioClip _pilotCompletion;
    private AudioClip _restOpen;
    private AudioClip _routineMusic;
    private AudioMixerGroup _musicMixerGroup;
    private AudioClip _careComplete;
    private Coroutine _musicFade;
    private bool _musicDucked;
    private bool _musicStopping;
    private bool _routineMusicRequested;
    private bool _actionAudioPaused;
    private CareActionType _activeAmbienceAction;
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
      if (Application.isPlaying) DontDestroyOnLoad(gameObject);
      _source = gameObject.AddComponent<AudioSource>();
      ConfigureSource(_source, 128);
      // Safety instructions use their own voice so a simultaneous station,
      // bottle, or reward sound cannot stop the close/open cue mid-play.
      _guidedSource = gameObject.AddComponent<AudioSource>();
      ConfigureSource(_guidedSource, 24);
      // Completion cues remain part of Cue/SFX, but use their own high-priority
      // voice so a direction note or narration cannot cut them off.
      _completionSource = gameObject.AddComponent<AudioSource>();
      ConfigureSource(_completionSource, 8);
      _musicSource = gameObject.AddComponent<AudioSource>();
      ConfigureSource(_musicSource, 180);
      _musicSource.loop = true;
      _musicSource.ignoreListenerPause = false;
      LoadRoutineMusicAndMixer();
      ApplyMusicMixerRouting();
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
      _upgradeUnavailable = CreateDoublePulse("Upgrade Unavailable", 220f, 0.055f, 0.095f);
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
      _focusCloser = CreateChord("Focus Closer", 164.81f, 246.94f, 0.28f);
      _focusAway = CreateRisingTone("Focus Away", 329.63f, 493.88f, 0.32f);
      _focusCycle = CreateDoublePulse("Focus Cycle", 392f, 0.09f, 0.11f);
      _focusCompletion = CreateChord("Focus Complete", 392f, 659.25f, 0.62f);
      _guidedLap = CreateTone("Guided Glass Drop", 783.99f, 0.30f);
      _guidedOpen = CreateRisingTone("Guided Open", 523.25f, 783.99f, 0.70f);
      _pilotDirections = new[]
      {
        CreateRisingTone("Pilot Up", 261.63f, 392f, 0.32f),
        CreateRisingTone("Pilot Down", 392f, 246.94f, 0.32f),
        CreateTone("Pilot Left", 311.13f, 0.30f),
        CreateTone("Pilot Right", 369.99f, 0.30f),
        CreateChord("Pilot Upper Left", 293.66f, 440f, 0.34f),
        CreateChord("Pilot Lower Right", 329.63f, 493.88f, 0.34f),
        CreateChord("Pilot Lower Left", 277.18f, 415.30f, 0.34f),
        CreateChord("Pilot Upper Right", 349.23f, 523.25f, 0.34f),
      };
      _pilotCenter = CreateTone("Pilot Center", 349.23f, 0.18f);
      _pilotAxis = CreateChord("Pilot Axis", 349.23f, 523.25f, 0.40f);
      _pilotCompletion = CreateToneSequence("Pilot Four Note", new[] { 349.23f, 440f, 523.25f, 698.46f }, 0.18f);
      _restOpen = CreateRisingTone("Rest Ready To Open", 440f, 622.25f, 0.82f);
      _careComplete = CreateToneSequence("Care Complete Three Tone", new[] { 523.25f, 659.25f, 783.99f }, 0.24f);
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

    public void PlayUpgradeUnavailable()
    {
      PlayExclusive(_upgradeUnavailable, _stepVolume * 0.42f);
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

    public void PlayFocusCloser() => PlayExclusive(_focusCloser, _stepVolume * 0.7f);
    public void PlayFocusAway() => PlayExclusive(_focusAway, _stepVolume * 0.72f);
    public void PlayFocusCycle() => PlayLayered(_source, _focusCycle, _stepVolume * 0.65f, "FOCUS CYCLE");
    public void PlayFocusCompletion() => PlayCompletion(_focusCompletion, _stepVolume * 0.82f, "FOCUS COMPLETE");
    public void PlayGuidedLap() => PlayLayered(_guidedSource, _guidedLap, _guidedVolume * 0.72f, "GUIDED LAP");
    public void PlayGuidedOpen() => PlayCompletion(_guidedOpen, _guidedVolume, "GUIDED OPEN");
    public void PlayPilotDirection(int index)
    {
      if (_pilotDirections == null || _pilotDirections.Length == 0) return;
      var clamped = Mathf.Clamp(index, 0, _pilotDirections.Length - 1);
      var pan = clamped == 2 || clamped == 4 || clamped == 6
        ? -0.28f
        : clamped == 3 || clamped == 5 || clamped == 7 ? 0.28f : 0f;
      PlayGuidedExclusive(_pilotDirections[clamped],
        _guidedVolume * 0.72f, "PILOT DIRECTION", pan);
    }
    public void PlayPilotCenter() => PlayGuidedExclusive(_pilotCenter, _guidedVolume * 0.62f, "PILOT CENTER");
    public void PlayPilotAxis() => PlayLayered(_guidedSource, _pilotAxis, _guidedVolume * 0.82f, "PILOT AXIS");
    public void PlayPilotCompletion() => PlayCompletion(_pilotCompletion, _guidedVolume, "PILOT COMPLETE");
    public void PlayRestOpen() => PlayCompletion(_restOpen, _guidedVolume, "REST OPEN");
    public void PlayCareComplete() => PlayCompletion(_careComplete, _stepVolume, "CARE COMPLETE");

    public void StartActionAmbience(CareActionType action)
    {
      StartActionAmbience(action, false);
    }

    public void StartActionAmbience(CareActionType action, bool startPaused)
    {
      EnsureAudioOutputs();
      var clip = GetAmbienceClip(action);
      if (_musicSource == null || clip == null) return;
      _activeAmbienceAction = action;
      _actionAudioPaused = startPaused;
      if (_musicSource.clip == clip && (_routineMusicRequested || _musicSource.isPlaying))
      {
        _routineMusicRequested = true;
        _musicStopping = false;
        StopMusicFade();
        if (startPaused)
        {
          if (_musicSource.isPlaying) _musicSource.Pause();
        }
        else
        {
          _musicSource.UnPause();
          if (!_musicSource.isPlaying) _musicSource.Play();
          if (!Mathf.Approximately(_musicSource.volume, MusicTargetVolume))
            _musicFade = StartCoroutine(FadeMusic(0.25f, false));
        }
        return;
      }
      StopMusicFade();
      _musicStopping = false;
      _routineMusicRequested = true;
      _musicSource.Stop();
      _musicSource.clip = clip;
      _musicSource.loop = true;
      _musicSource.timeSamples = 0;
      _musicSource.volume = 0f;
      if (!startPaused) _musicSource.Play();
      _musicFade = StartCoroutine(FadeMusic(_routineMusicFadeInSeconds, false));
    }

    public void StopActionAmbience(bool immediate = false)
    {
      if (_musicSource == null ||
          (_musicSource.clip == null && _activeAmbienceAction == CareActionType.None)) return;
      if (immediate)
      {
        StopMusicFade();
        _routineMusicRequested = false;
        _musicStopping = false;
        _activeAmbienceAction = CareActionType.None;
        _musicSource.volume = 0f;
        _musicSource.Stop();
        _musicSource.timeSamples = 0;
        return;
      }
      if (_musicStopping) return;
      StopMusicFade();
      _routineMusicRequested = false;
      _musicStopping = true;
      if (!_musicSource.isPlaying && _musicSource.clip != null) _musicSource.UnPause();
      _musicFade = StartCoroutine(FadeMusic(_routineMusicFadeOutSeconds, true));
    }

    public void StartClosedEyeMusic() => StartActionAmbience(CareActionType.ClosedEyeRest);
    public void StopClosedEyeMusic(bool immediate = false) => StopActionAmbience(immediate);

    public void SetActionAudioPaused(bool paused)
    {
      EnsureAudioOutputs();
      if (_actionAudioPaused == paused) return;
      _actionAudioPaused = paused;
      if (paused)
      {
        if (_musicSource != null && _musicSource.isPlaying) _musicSource.Pause();
        if (_source != null && _source.isPlaying) _source.Pause();
        if (_guidedSource != null && _guidedSource.isPlaying) _guidedSource.Pause();
        if (_completionSource != null && _completionSource.isPlaying) _completionSource.Pause();
      }
      else
      {
        if (_musicSource != null && _musicSource.clip != null) _musicSource.UnPause();
        if (_source != null && _source.clip != null) _source.UnPause();
        if (_guidedSource != null && _guidedSource.clip != null) _guidedSource.UnPause();
        if (_completionSource != null && _completionSource.clip != null) _completionSource.UnPause();
        if (_musicSource != null && _musicSource.clip != null && !_musicSource.isPlaying &&
            _routineMusicRequested && !_musicStopping)
          _musicSource.Play();
      }
    }

    public void SetVoiceDucking(bool active)
    {
      _musicDucked = active;
      if (_musicSource != null && _musicSource.isPlaying && !_musicStopping)
      {
        StopMusicFade();
        _musicFade = StartCoroutine(FadeMusic(active ? 0.18f : 0.6f, false));
      }
    }

    private float MusicTargetVolume => _musicDucked
      ? _routineMusicVolume * Mathf.Pow(10f, _voiceDuckDecibels / 20f)
      : _routineMusicVolume;

    private System.Collections.IEnumerator FadeMusic(float seconds, bool stopAtEnd)
    {
      var start = _musicSource != null ? _musicSource.volume : 0f;
      var elapsed = 0f;
      while (_musicSource != null && elapsed < seconds)
      {
        if (_actionAudioPaused && !stopAtEnd)
        {
          yield return null;
          continue;
        }
        elapsed += Time.unscaledDeltaTime;
        var target = stopAtEnd ? 0f : MusicTargetVolume;
        _musicSource.volume = Mathf.Lerp(start, target, Mathf.Clamp01(elapsed / Mathf.Max(0.01f, seconds)));
        yield return null;
      }
      if (_musicSource != null)
      {
        if (stopAtEnd)
        {
          _musicSource.volume = 0f;
          _musicSource.Stop();
          _musicSource.timeSamples = 0;
          _activeAmbienceAction = CareActionType.None;
        }
        else
        {
          _musicSource.volume = MusicTargetVolume;
        }
      }
      _musicStopping = false;
      _musicFade = null;
    }

    public AudioClip GetAmbienceClip(CareActionType action)
    {
      return action == CareActionType.FocusShift || action == CareActionType.PilotEyeRoutine ||
             action == CareActionType.GuidedEyeCircles || action == CareActionType.ClosedEyeRest
        ? _routineMusic
        : null;
    }

    public CareActionType ActiveAmbienceAction => _activeAmbienceAction;
    public bool ActionAudioPaused => _actionAudioPaused;
    public bool RoutineMusicRequested => _routineMusicRequested;
    public bool RoutineMusicPlaying => _musicSource != null && _musicSource.isPlaying;
    public AudioMixerGroup MusicMixerGroup => _musicMixerGroup;
    public float UnduckedAmbienceVolume => _routineMusicVolume;
    public float DuckedAmbienceVolume => _routineMusicVolume * Mathf.Pow(10f, _voiceDuckDecibels / 20f);

    private void LoadRoutineMusicAndMixer()
    {
      if (_routineMusic == null)
        _routineMusic = _routineMusicOverride != null
          ? _routineMusicOverride
          : Resources.Load<AudioClip>(_routineMusicResourcePath);
      if (_musicMixerGroup != null) return;
      var mixer = Resources.Load<AudioMixer>(_routineMixerResourcePath);
      if (mixer == null) return;
      var groups = mixer.FindMatchingGroups("Music");
      if (groups != null && groups.Length > 0) _musicMixerGroup = groups[0];
    }

    private void ApplyMusicMixerRouting()
    {
      if (_musicSource != null && _musicMixerGroup != null)
        _musicSource.outputAudioMixerGroup = _musicMixerGroup;
    }

    private void StopMusicFade()
    {
      if (_musicFade == null) return;
      StopCoroutine(_musicFade);
      _musicFade = null;
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
      if (_completionSource == null)
      {
        _completionSource = gameObject.AddComponent<AudioSource>();
        ConfigureSource(_completionSource, 8);
      }
      if (_musicSource == null)
      {
        _musicSource = gameObject.AddComponent<AudioSource>();
        ConfigureSource(_musicSource, 180);
        _musicSource.loop = true;
        _musicSource.ignoreListenerPause = false;
      }
      LoadRoutineMusicAndMixer();
      ApplyMusicMixerRouting();
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

    private void PlayGuidedExclusive(AudioClip clip, float volume, string cueName, float pan = 0f)
    {
      EnsureAudioOutputs();
      if (_guidedSource == null || clip == null || _actionAudioPaused) return;
      _guidedSource.Stop();
      _guidedSource.clip = clip;
      _guidedSource.volume = Mathf.Clamp01(volume);
      _guidedSource.panStereo = Mathf.Clamp(pan, -0.5f, 0.5f);
      _guidedSource.Play();
      _lastGuidedCue = cueName;
      _lastGuidedCueAt = Time.unscaledTime;
    }

    private void PlayCompletion(AudioClip clip, float volume, string cueName)
    {
      EnsureAudioOutputs();
      if (_completionSource == null || clip == null || _actionAudioPaused) return;
      _completionSource.Stop();
      _completionSource.clip = clip;
      _completionSource.volume = Mathf.Clamp01(volume);
      _completionSource.panStereo = 0f;
      _completionSource.Play();
      _lastGuidedCue = cueName;
      _lastGuidedCueAt = Time.unscaledTime;
    }

    private void PlayLayered(AudioSource source, AudioClip clip, float volume, string cueName)
    {
      EnsureAudioOutputs();
      if (source == null || clip == null || _actionAudioPaused) return;
      source.PlayOneShot(clip, Mathf.Clamp01(volume));
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
          $"Routine Music: {(_routineMusicRequested ? "ACTIVE" : "STOPPED")}  " +
          $"Clip: {(_musicSource != null && _musicSource.clip != null ? _musicSource.clip.name : "NONE")}  " +
          $"Mixer: {(_musicMixerGroup != null ? _musicMixerGroup.name : "NONE")}  " +
          $"Ducked: {_musicDucked}  Action Paused: {_actionAudioPaused}\n" +
          $"Cue: {_lastGuidedCue}  Age: {(age < 0f ? "--" : age.ToString("0.0") + "s")}  " +
          $"Playing: {(_guidedSource != null && _guidedSource.isPlaying)}  Source: {(_guidedSource != null ? _guidedSource.volume : 0f):0.00}";
      }
    }
#endif

    private void PlayExclusive(AudioClip clip, float volume)
    {
      if (_source == null || clip == null || _actionAudioPaused) return;
      // A single non-overlapping source keeps dense reward runs from building
      // dozens of simultaneous voices on iPhone.
      _source.Stop();
      _source.clip = clip;
      _source.volume = Mathf.Clamp01(volume);
      _source.Play();
    }

    private void PlayExclusive(AudioClip clip, float volume, string cueName)
    {
      PlayExclusive(clip, volume);
      _lastGuidedCue = cueName;
      _lastGuidedCueAt = Time.unscaledTime;
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

    private static AudioClip CreateToneSequence(string name, float[] frequencies, float noteSeconds)
    {
      const int sampleRate = 24000;
      if (frequencies == null || frequencies.Length == 0) return null;
      noteSeconds = Mathf.Max(0.08f, noteSeconds);
      var noteSamples = Mathf.RoundToInt(sampleRate * noteSeconds);
      var count = Mathf.Max(1, noteSamples * frequencies.Length);
      var data = new float[count];
      for (var note = 0; note < frequencies.Length; note++)
      for (var sample = 0; sample < noteSamples; sample++)
      {
        var normalized = sample / (float)Mathf.Max(1, noteSamples - 1);
        var envelope = Mathf.Sin(normalized * Mathf.PI);
        var t = sample / (float)sampleRate;
        data[note * noteSamples + sample] =
          Mathf.Sin(t * Mathf.PI * 2f * frequencies[note]) * envelope * 0.18f;
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

    private static AudioClip CreateAmbientBed(string name, float duration)
    {
      const int sampleRate = 24000;
      var count = Mathf.Max(1, Mathf.RoundToInt(sampleRate * Mathf.Max(2f, duration)));
      var data = new float[count];
      for (var i = 0; i < count; i++)
      {
        var t = i / (float)sampleRate;
        var loopEnvelope = Mathf.Sin(Mathf.PI * Mathf.Clamp01(i / (float)(count - 1)));
        var slow = Mathf.Sin(t * Mathf.PI * 2f * 110f) * 0.020f;
        var air = Mathf.Sin(t * Mathf.PI * 2f * 164.81f) * 0.009f;
        data[i] = (slow + air) * Mathf.Lerp(0.75f, 1f, loopEnvelope);
      }
      var clip = AudioClip.Create(name, count, 1, sampleRate, false);
      clip.SetData(data, 0);
      return clip;
    }

    private static AudioClip CreateActionAmbientFallback(
      string name,
      float fundamental,
      float modulationHz,
      float color)
    {
      const int sampleRate = 24000;
      const float duration = 8f;
      var count = Mathf.RoundToInt(sampleRate * duration);
      var data = new float[count];
      var seed = name.GetHashCode();
      var state = (uint)(seed == 0 ? 1 : seed);
      var filteredNoise = 0f;
      for (var i = 0; i < count; i++)
      {
        var t = i / (float)sampleRate;
        state = state * 1664525u + 1013904223u;
        var white = ((state >> 8) / 16777215f) * 2f - 1f;
        filteredNoise = Mathf.Lerp(filteredNoise, white, Mathf.Clamp01(color * 0.018f));
        var pulse = 0.72f + 0.28f * Mathf.Sin(Mathf.PI * 2f * modulationHz * t);
        var tone = Mathf.Sin(Mathf.PI * 2f * fundamental * t) * 0.012f;
        var harmonic = Mathf.Sin(Mathf.PI * 2f * fundamental * 1.5f * t + color) * 0.006f;
        data[i] = (filteredNoise * 0.026f + tone + harmonic) * pulse;
      }

      // Equal-power crossfade makes the fallback loop click-free as well.
      var blend = Mathf.Min(sampleRate, count / 4);
      for (var i = 0; i < blend; i++)
      {
        var amount = i / (float)Mathf.Max(1, blend - 1);
        var a = data[i];
        var b = data[count - blend + i];
        var mixed = a * Mathf.Sqrt(1f - amount) + b * Mathf.Sqrt(amount);
        data[i] = mixed;
        data[count - blend + i] = mixed;
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
      DestroyClip(_focusCloser);
      DestroyClip(_focusAway);
      DestroyClip(_focusCycle);
      DestroyClip(_focusCompletion);
      DestroyClip(_guidedLap);
      DestroyClip(_guidedOpen);
      DestroyClips(_pilotDirections);
      DestroyClip(_pilotCenter);
      DestroyClip(_pilotAxis);
      DestroyClip(_pilotCompletion);
      DestroyClip(_restOpen);
      DestroyClip(_careComplete);
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

    private static void DestroyGeneratedAmbience(AudioClip clip)
    {
      if (clip != null && clip.name.EndsWith("Fallback")) Destroy(clip);
    }
  }
}
