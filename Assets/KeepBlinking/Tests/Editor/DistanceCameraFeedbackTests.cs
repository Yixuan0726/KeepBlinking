using KeepBlinking.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace KeepBlinking.Tests
{
  public sealed class DistanceCameraFeedbackTests
  {
    [Test]
    public void NearCurveHasFourClearlySeparatedLevels()
    {
      var normal = EvaluateSettledNearAmount(1.00f);
      var light = EvaluateSettledNearAmount(1.10f);
      var strong = EvaluateSettledNearAmount(1.20f);
      var maximum = EvaluateSettledNearAmount(1.30f);

      Assert.That(normal, Is.LessThan(0.001f));
      Assert.That(light, Is.GreaterThan(0.10f));
      Assert.That(strong, Is.GreaterThan(light + 0.35f));
      Assert.That(maximum, Is.GreaterThan(strong + 0.20f));
      Assert.That(maximum, Is.GreaterThan(0.99f));
    }

    [Test]
    public void TrackingLossRestoresFeedbackWithinConfiguredWindow()
    {
      var root = new GameObject("Distance Feedback Recovery Test");
      try
      {
        var feedback = root.AddComponent<DistanceCameraFeedback>();
        feedback.SetInput(1.30f, true, true, true);
        for (var i = 0; i < 30; i++)
        {
          feedback.Tick(0.05f);
        }
        Assert.That(feedback.NearAmount, Is.GreaterThan(0.99f));

        feedback.SetInput(1.30f, false, false, false);
        feedback.Tick(0.35f);
        Assert.That(feedback.NearAmount, Is.EqualTo(0f).Within(0.0001f));
        Assert.That(feedback.CurrentDistortionStrength, Is.EqualTo(0f).Within(0.0001f));
      }
      finally
      {
        Object.DestroyImmediate(root);
      }
    }

    private static float EvaluateSettledNearAmount(float distanceRatio)
    {
      var root = new GameObject($"Distance Feedback Curve {distanceRatio:F2}");
      try
      {
        var feedback = root.AddComponent<DistanceCameraFeedback>();
        feedback.SetInput(distanceRatio, true, true, distanceRatio >= 1.18f);
        for (var i = 0; i < 30; i++)
        {
          feedback.Tick(0.05f);
        }
        return feedback.NearAmount;
      }
      finally
      {
        Object.DestroyImmediate(root);
      }
    }
  }
}
