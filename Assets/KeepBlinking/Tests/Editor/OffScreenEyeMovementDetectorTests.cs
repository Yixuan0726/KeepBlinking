using KeepBlinking.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace KeepBlinking.Tests
{
  public sealed class OffScreenEyeMovementDetectorTests
  {
    private static OffScreenEyeMovementThresholds Thresholds => OffScreenEyeMovementThresholds.Default;

    [TestCase(OffScreenDirection.Left, -14f, 0f)]
    [TestCase(OffScreenDirection.Right, 14f, 0f)]
    [TestCase(OffScreenDirection.Up, 0f, 12f)]
    [TestCase(OffScreenDirection.Down, 0f, -12f)]
    public void LargeDirectionRegionRequiresContinuousHold(OffScreenDirection direction, float horizontal, float vertical)
    {
      var detector = new OffScreenEyeMovementDetector();
      var sample = ValidSample(new Vector2(horizontal, vertical));

      for (var index = 0; index < 4; index++)
      {
        Assert.That(detector.UpdateDirection(direction, sample, Thresholds, 0.1f), Is.False);
      }

      Assert.That(detector.UpdateDirection(direction, sample, Thresholds, 0.05f), Is.True);
      Assert.That(detector.DirectionHoldProgress, Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    public void TrackingLossCannotConfirmDirection()
    {
      var detector = new OffScreenEyeMovementDetector();
      var invalid = new OffScreenEyeMovementSample(
        false, true, true, false, false, true,
        Vector2.zero, new Vector2(-20f, 0f), 0f, 0f);

      for (var index = 0; index < 10; index++)
      {
        Assert.That(detector.UpdateDirection(OffScreenDirection.Left, invalid, Thresholds, 0.1f), Is.False);
      }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public void ClosedEyesOrBlinkCannotConfirmDirection(bool isBlinking, bool eyesClosed)
    {
      var detector = new OffScreenEyeMovementDetector();
      var invalid = new OffScreenEyeMovementSample(
        true, true, false, isBlinking, eyesClosed, true,
        Vector2.zero, new Vector2(20f, 0f), 0f, 0f);

      for (var index = 0; index < 10; index++)
      {
        Assert.That(detector.UpdateDirection(OffScreenDirection.Right, invalid, Thresholds, 0.1f), Is.False);
      }
    }

    [TestCase(13f, 0f)]
    [TestCase(0f, -13f)]
    public void LargeHeadTurnCannotReplaceEyeMovement(float headYaw, float headPitch)
    {
      var detector = new OffScreenEyeMovementDetector();
      var sample = new OffScreenEyeMovementSample(
        true, true, true, false, false, true,
        Vector2.zero, new Vector2(-20f, 0f), headYaw, headPitch);

      for (var index = 0; index < 10; index++)
      {
        Assert.That(detector.UpdateDirection(OffScreenDirection.Left, sample, Thresholds, 0.1f), Is.False);
      }

      Assert.That(detector.IsHeadWithinLimit, Is.False);
    }

    [Test]
    public void ReturnCenterUsesLargeRegionAndContinuousHold()
    {
      var detector = new OffScreenEyeMovementDetector();
      var sample = ValidSample(new Vector2(4f, -3f));

      for (var index = 0; index < 3; index++)
      {
        Assert.That(detector.UpdateReturnCenter(sample, Thresholds, 0.1f), Is.False);
      }

      Assert.That(detector.UpdateReturnCenter(sample, Thresholds, 0.05f), Is.True);
      Assert.That(detector.ReturnCenterProgress, Is.EqualTo(1f).Within(0.001f));
    }

    [Test]
    public void BreakingTheHoldResetsProgress()
    {
      var detector = new OffScreenEyeMovementDetector();
      detector.UpdateDirection(OffScreenDirection.Left, ValidSample(new Vector2(-18f, 0f)), Thresholds, 0.3f);
      Assert.That(detector.DirectionHoldProgress, Is.GreaterThan(0.5f));

      detector.UpdateDirection(OffScreenDirection.Left, ValidSample(Vector2.zero), Thresholds, 0.1f);
      Assert.That(detector.DirectionHoldProgress, Is.Zero);
    }

    private static OffScreenEyeMovementSample ValidSample(Vector2 centeredGaze)
    {
      return new OffScreenEyeMovementSample(
        true, true, true, false, false, true,
        centeredGaze, centeredGaze, 2f, -2f);
    }
  }
}
