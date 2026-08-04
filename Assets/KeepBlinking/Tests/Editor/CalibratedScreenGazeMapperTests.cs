using System.Collections.Generic;
using KeepBlinking.Input;
using NUnit.Framework;
using UnityEngine;

namespace KeepBlinking.Tests
{
  public sealed class CalibratedScreenGazeMapperTests
  {
    [Test]
    public void SetCalibration_MapsIndependentAxesAndPreservesProviderIsolation()
    {
      var current = new CalibratedScreenGazeMapper();
      var l2cs = new CalibratedScreenGazeMapper();
      var targets = FiveTargets();

      Assert.That(current.SetCalibration(
        new[]
        {
          new Vector2(0.50f, 0.50f), new Vector2(0.15f, 0.85f), new Vector2(0.85f, 0.85f),
          new Vector2(0.85f, 0.15f), new Vector2(0.15f, 0.15f),
        }, targets), Is.True);
      Assert.That(l2cs.SetCalibration(
        new[]
        {
          new Vector2(0f, 0f), new Vector2(18f, -12f), new Vector2(-18f, -12f),
          new Vector2(-18f, 12f), new Vector2(18f, 12f),
        }, targets), Is.True);

      Assert.That(current.TryMap(new Vector2(0.85f, 0.15f), out var currentMapped), Is.True);
      Assert.That(l2cs.TryMap(new Vector2(-18f, 12f), out var l2csMapped), Is.True);
      Assert.That(currentMapped.x, Is.EqualTo(0.85f).Within(0.001f));
      Assert.That(currentMapped.y, Is.EqualTo(0.15f).Within(0.001f));
      Assert.That(l2csMapped.x, Is.EqualTo(0.85f).Within(0.001f));
      Assert.That(l2csMapped.y, Is.EqualTo(0.15f).Within(0.001f));
      l2cs.Reset();
      Assert.That(current.IsCalibrated, Is.True);
      Assert.That(l2cs.IsCalibrated, Is.False);
    }

    [Test]
    public void SetCalibration_RejectsDegenerateInputAndLeavesMapperUncalibrated()
    {
      var mapper = new CalibratedScreenGazeMapper();
      var repeated = new List<Vector2> { Vector2.one, Vector2.one, Vector2.one };
      var targets = new List<Vector2> { Vector2.zero, Vector2.one, Vector2.right };

      Assert.That(mapper.SetCalibration(repeated, targets), Is.False);
      Assert.That(mapper.IsCalibrated, Is.False);
      Assert.That(mapper.TryMap(Vector2.zero, out _), Is.False);
    }

    private static Vector2[] FiveTargets()
    {
      return new[]
      {
        new Vector2(0.50f, 0.50f), new Vector2(0.15f, 0.85f), new Vector2(0.85f, 0.85f),
        new Vector2(0.85f, 0.15f), new Vector2(0.15f, 0.15f),
      };
    }
  }
}
