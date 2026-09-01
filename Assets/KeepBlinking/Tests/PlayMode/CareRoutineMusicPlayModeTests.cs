using System.Collections;
using System.Linq;
using System.Reflection;
using KeepBlinking.CareStation;
using KeepBlinking.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KeepBlinking.Tests.PlayMode
{
  public sealed class CareRoutineMusicPlayModeTests
  {
    [SetUp]
    public void SetUp()
    {
      DestroyAudioObjects();
    }

    [TearDown]
    public void TearDown()
    {
      DestroyAudioObjects();
    }

    [UnityTest]
    public IEnumerator FullRoutineKeepsOneMusicSourceAndContinuousPlaybackPosition()
    {
      var audio = CareAudioFeedbackController.EnsureExists();
      audio.StartActionAmbience(CareActionType.FocusShift);
      yield return new WaitForSecondsRealtime(0.12f);
      var source = MusicSource(audio);
      Assert.That(source, Is.Not.Null);
      Assert.That(source.isPlaying, Is.True);
      Assert.That(source.loop, Is.True,
        "The 2:54 source must loop when a Routine lasts longer than the track.");
      Assert.That(source.ignoreListenerPause, Is.False,
        "The Music source must follow the existing global pause setting.");
      var before = source.timeSamples;
      var sourceCount = audio.GetComponents<AudioSource>().Length;
      var ordinary = typeof(CareAudioFeedbackController)
        .GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)
        ?.GetValue(audio) as AudioSource;
      Assert.That(ordinary, Is.Not.Null);
      Assert.That(ordinary.outputAudioMixerGroup, Is.Not.EqualTo(audio.MusicMixerGroup),
        "Action feedback must remain outside the ducked Music mixer group.");

      var remainingActions = new[]
      {
        CareActionType.PilotEyeRoutine,
        CareActionType.GuidedEyeCircles,
        CareActionType.ClosedEyeRest,
      };
      foreach (var action in remainingActions)
      {
        var beforeSwitch = source.timeSamples;
        audio.StartActionAmbience(action);
        yield return new WaitForSecondsRealtime(0.12f);
        Assert.That(MusicSource(audio), Is.SameAs(source));
        Assert.That(audio.GetComponents<AudioSource>().Length, Is.EqualTo(sourceCount));
        Assert.That(source.timeSamples, Is.GreaterThan(beforeSwitch),
          $"Switching to {action} must not rewind or replace the Routine music source.");
      }
      Assert.That(source.timeSamples, Is.GreaterThan(before));
      Assert.That(source.outputAudioMixerGroup, Is.EqualTo(audio.MusicMixerGroup));
      Assert.That(source.outputAudioMixerGroup?.name, Is.EqualTo("Music"));

      audio.StopActionAmbience();
      yield return new WaitForSecondsRealtime(1.2f);
      Assert.That(source.isPlaying, Is.False,
        "Completing the final Rest must fade and stop the one Routine track.");
    }

    [UnityTest]
    public IEnumerator PauseFreezesMusicAndResumeContinuesWithoutRestart()
    {
      var audio = CareAudioFeedbackController.EnsureExists();
      audio.StartActionAmbience(CareActionType.PilotEyeRoutine);
      yield return new WaitForSecondsRealtime(0.12f);
      var source = MusicSource(audio);
      var beforePause = source.timeSamples;
      audio.SetActionAudioPaused(true);
      yield return new WaitForSecondsRealtime(0.15f);
      Assert.That(source.timeSamples, Is.EqualTo(beforePause).Within(256));

      audio.SetActionAudioPaused(false);
      yield return new WaitForSecondsRealtime(0.12f);
      Assert.That(source.timeSamples, Is.GreaterThan(beforePause));
    }

    [UnityTest]
    public IEnumerator GlobalListenerPauseFreezesMusicAndResumeContinuesWithoutRestart()
    {
      var audio = CareAudioFeedbackController.EnsureExists();
      audio.StartActionAmbience(CareActionType.FocusShift);
      yield return new WaitForSecondsRealtime(0.12f);
      var source = MusicSource(audio);
      var beforePause = source.timeSamples;

      AudioListener.pause = true;
      yield return new WaitForSecondsRealtime(0.15f);
      Assert.That(source.timeSamples, Is.EqualTo(beforePause).Within(256));

      AudioListener.pause = false;
      yield return new WaitForSecondsRealtime(0.12f);
      Assert.That(source.timeSamples, Is.GreaterThan(beforePause));
    }

    [UnityTest]
    public IEnumerator ConsecutiveVoiceRequestsRemainDuckedThenRecoverSmoothly()
    {
      var audio = CareAudioFeedbackController.EnsureExists();
      var voice = CareVoiceService.EnsureExists();
      audio.StartActionAmbience(CareActionType.FocusShift);
      yield return new WaitForSecondsRealtime(1.7f);
      var source = MusicSource(audio);
      var full = audio.UnduckedAmbienceVolume;

      voice.Speak("music-duck-one", "FIRST INSTRUCTION.", 0.5f, CareVoicePriority.Instruction);
      yield return new WaitForSecondsRealtime(0.25f);
      Assert.That(source.volume, Is.EqualTo(audio.DuckedAmbienceVolume).Within(0.01f));

      voice.Speak("music-duck-two", "SECOND INSTRUCTION.", 0.5f, CareVoicePriority.Instruction);
      yield return new WaitForSecondsRealtime(0.25f);
      Assert.That(source.volume, Is.LessThan(full * 0.65f),
        "Replacing narration must not briefly restore the Music channel between lines.");

      voice.Stop();
      yield return new WaitForSecondsRealtime(0.7f);
      Assert.That(source.volume, Is.EqualTo(full).Within(0.01f));
    }

    [UnityTest]
    public IEnumerator ExitFadesStopsAndReentryStartsWithoutAddingSources()
    {
      var audio = CareAudioFeedbackController.EnsureExists();
      audio.StartActionAmbience(CareActionType.ClosedEyeRest);
      yield return new WaitForSecondsRealtime(0.2f);
      var source = MusicSource(audio);
      var sourceCount = audio.GetComponents<AudioSource>().Length;

      audio.StopActionAmbience();
      yield return new WaitForSecondsRealtime(1.2f);
      Assert.That(source.isPlaying, Is.False);
      Assert.That(audio.RoutineMusicRequested, Is.False);

      audio.StartActionAmbience(CareActionType.FocusShift);
      yield return new WaitForSecondsRealtime(0.12f);
      Assert.That(source.isPlaying, Is.True);
      Assert.That(audio.GetComponents<AudioSource>().Length, Is.EqualTo(sourceCount));
      Assert.That(Object.FindObjectsByType<CareAudioFeedbackController>(
        FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(1));
    }

    private static AudioSource MusicSource(CareAudioFeedbackController audio)
    {
      return typeof(CareAudioFeedbackController)
        .GetField("_musicSource", BindingFlags.Instance | BindingFlags.NonPublic)
        ?.GetValue(audio) as AudioSource;
    }

    private static void DestroyAudioObjects()
    {
      AudioListener.pause = false;
      AudioListener.volume = 1f;
      foreach (var audio in Object.FindObjectsByType<CareAudioFeedbackController>(
                 FindObjectsInactive.Include, FindObjectsSortMode.None).ToArray())
        Object.DestroyImmediate(audio.gameObject);
      foreach (var voice in Object.FindObjectsByType<CareVoiceService>(
                 FindObjectsInactive.Include, FindObjectsSortMode.None).ToArray())
        Object.DestroyImmediate(voice.gameObject);
    }
  }
}
