using KeepBlinking.Input;
using NUnit.Framework;

namespace KeepBlinking.Tests
{
  public sealed class SessionDistanceTrackerTests
  {
    private static SessionDistanceSettings Settings => new SessionDistanceSettings(
      0.1f,
      3,
      0.1f,
      1000f,
      0.92f,
      1.10f,
      0.82f,
      0.1f,
      0.92f,
      0.1f,
      1.18f,
      0.1f,
      1.10f);

    [Test]
    public void BaselineIsMedianAndNeverDriftsAfterCapture()
    {
      var tracker = new SessionDistanceTracker();
      tracker.ResetSession();
      tracker.Update(1.00f, true, true, true, false, 0f, 0.05f, Settings);
      tracker.Update(1.02f, true, true, true, false, 0.05f, 0.05f, Settings);
      var captured = tracker.Update(0.98f, true, true, true, false, 0.10f, 0.05f, Settings);

      Assert.That(captured.BaselineCaptured, Is.True);
      Assert.That(tracker.BaselineFaceScale, Is.EqualTo(1f).Within(0.0001f));

      for (var i = 0; i < 30; i++)
      {
        tracker.Update(0.8f, true, true, true, false, 0.15f + i * 0.05f, 0.05f, Settings);
      }

      Assert.That(tracker.BaselineFaceScale, Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void FivePushAwayCyclesUseTheSameBaselineAndRequireRearm()
    {
      var tracker = CreateCalibratedTracker(out var now);
      var baseline = tracker.BaselineFaceScale;

      for (var cycle = 0; cycle < 5; cycle++)
      {
        Rearm(tracker, ref now, true);
        Assert.That(tracker.IsPushAwayReady, Is.True, $"Cycle {cycle + 1} did not arm.");

        var triggerCount = 0;
        for (var i = 0; i < 4; i++)
        {
          now += 0.05f;
          if (tracker.Update(0.80f, true, true, true, true, now, 0.05f, Settings).PushAwayTriggered)
          {
            triggerCount++;
          }
        }

        Assert.That(triggerCount, Is.EqualTo(1), $"Cycle {cycle + 1} should trigger exactly once.");
        Assert.That(tracker.BaselineFaceScale, Is.EqualTo(baseline).Within(0.0001f));

        for (var i = 0; i < 6; i++)
        {
          now += 0.05f;
          Assert.That(
            tracker.Update(0.80f, true, true, true, true, now, 0.05f, Settings).PushAwayTriggered,
            Is.False,
            "Push Away repeated before rearm.");
        }
      }
    }

    [Test]
    public void TrackingLossCannotTriggerPushAway()
    {
      var tracker = CreateCalibratedTracker(out var now);
      Rearm(tracker, ref now, true);

      for (var i = 0; i < 10; i++)
      {
        now += 0.05f;
        var update = tracker.Update(0f, false, true, true, true, now, 0.05f, Settings);
        Assert.That(update.PushAwayTriggered, Is.False);
      }

      Assert.That(tracker.State, Is.EqualTo(SessionDistanceState.TrackingLost));
      Assert.That(tracker.HasValidSample, Is.False);
    }

    [Test]
    public void TooCloseUsesHoldAndExitHysteresis()
    {
      var tracker = CreateCalibratedTracker(out var now);

      now += 0.05f;
      tracker.Update(1.22f, true, true, true, false, now, 0.05f, Settings);
      Assert.That(tracker.IsTooClose, Is.False);

      now += 0.1f;
      tracker.Update(1.22f, true, true, true, false, now, 0.1f, Settings);
      Assert.That(tracker.IsTooClose, Is.True);

      now += 0.05f;
      tracker.Update(1.12f, true, true, true, false, now, 0.05f, Settings);
      Assert.That(tracker.IsTooClose, Is.True);

      now += 0.05f;
      tracker.Update(1.08f, true, true, true, false, now, 0.05f, Settings);
      Assert.That(tracker.IsTooClose, Is.False);
    }

    [Test]
    public void UnstableBaselineIsRejectedWithoutFallbackValue()
    {
      var tracker = new SessionDistanceTracker();
      tracker.ResetSession();
      tracker.Update(0.7f, true, true, true, false, 0f, 0.05f, Settings);
      tracker.Update(1.3f, true, true, true, false, 0.05f, 0.05f, Settings);
      var result = tracker.Update(0.8f, true, true, true, false, 0.10f, 0.05f, Settings);

      Assert.That(result.BaselineRejected, Is.True);
      Assert.That(tracker.HasBaseline, Is.False);
      Assert.That(tracker.BaselineFaceScale, Is.LessThan(0f));
    }

    private static SessionDistanceTracker CreateCalibratedTracker(out float now)
    {
      var tracker = new SessionDistanceTracker();
      tracker.ResetSession();
      tracker.Update(1f, true, true, true, false, 0f, 0.05f, Settings);
      tracker.Update(1f, true, true, true, false, 0.05f, 0.05f, Settings);
      tracker.Update(1f, true, true, true, false, 0.10f, 0.05f, Settings);
      now = 0.10f;
      Assert.That(tracker.HasBaseline, Is.True);
      return tracker;
    }

    private static void Rearm(SessionDistanceTracker tracker, ref float now, bool hasCollectableSamples)
    {
      for (var i = 0; i < 4; i++)
      {
        now += 0.05f;
        tracker.Update(1f, true, true, true, hasCollectableSamples, now, 0.05f, Settings);
      }
    }
  }
}
