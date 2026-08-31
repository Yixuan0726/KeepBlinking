using System.Collections;
using KeepBlinking.CareStation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KeepBlinking.Tests
{
  public sealed class FocusShiftTimingPlayModeTests
  {
    [UnityTest]
    public IEnumerator CloserConfirmationUsesRealtimeAcrossTimeScaleChange()
    {
      var originalTimeScale = Time.timeScale;
      try
      {
        Time.timeScale = 1f;
        var configuration = CareActionConfiguration.Default;
        configuration.showIntro = false;
        configuration.focusTargetHoldSeconds = 0.7f;
        configuration.focusMinimumLegSeconds = 3f;
        var action = new CareActionRuntime();
        action.Begin(CareActionType.FocusShift, configuration);
        action.Data.gestureReferenceScale = 1f;
        action.Data.gestureReferenceValid = true;

        action.Advance(0.01f, FreshFrame(1f, 0.01f));
        while (action.Phase == CareActionInternalPhase.FocusNeutralStart)
          action.Advance(0.25f, FreshFrame(1f, 0.25f));
        Assert.That(action.Phase, Is.EqualTo(CareActionInternalPhase.FocusNearOne));

        var legStartedAt = Time.realtimeSinceStartup;
        var thresholdReachedAt = -1f;
        while (action.Data.focusTargetStep == 0 && Time.realtimeSinceStartup - legStartedAt < 4f)
        {
          var elapsed = Time.realtimeSinceStartup - legStartedAt;
          var ratio = elapsed >= 0.5f ? 1.25f : 1.20f;
          if (thresholdReachedAt < 0f && ratio >= 1.25f) thresholdReachedAt = elapsed;
          if (elapsed >= 1f) Time.timeScale = 0.05f;
          var unscaledDelta = Mathf.Clamp(Time.unscaledDeltaTime, 0f, 0.25f);
          action.Advance(unscaledDelta, FreshFrame(ratio, unscaledDelta));
          if (thresholdReachedAt >= 0f && action.Data.focusTargetStep == 0)
            Assert.That(action.Prompt, Does.StartWith("HOLD"));
          yield return null;
        }

        var confirmedAt = Time.realtimeSinceStartup - legStartedAt;
        Assert.That(thresholdReachedAt, Is.InRange(0.45f, 0.75f));
        Assert.That(action.Data.focusTargetStep, Is.EqualTo(1));
        Assert.That(confirmedAt, Is.InRange(2.85f, 3.30f),
          "Changing Unity timeScale must not change the real sensor confirmation delay.");
      }
      finally
      {
        Time.timeScale = originalTimeScale;
      }
    }

    private static CareActionInputFrame FreshFrame(float ratio, float sampleDelta)
    {
      return new CareActionInputFrame(
        true,
        true,
        false,
        true,
        false,
        false,
        true,
        ratio,
        true,
        sampleDelta);
    }
  }
}
