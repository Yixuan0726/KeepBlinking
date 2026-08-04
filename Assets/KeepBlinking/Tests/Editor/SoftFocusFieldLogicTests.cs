using KeepBlinking.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace KeepBlinking.Tests
{
  public sealed class SoftFocusFieldLogicTests
  {
    [Test]
    public void LargeEllipseAcceptsComfortRegionWithoutPixelPrecision()
    {
      var center = new Vector2(0.5f, 0.55f);
      var size = new Vector2(0.55f, 0.40f);

      Assert.That(SoftFocusFieldLogic.IsInsideEllipse(center, center, size), Is.True);
      Assert.That(SoftFocusFieldLogic.IsInsideEllipse(new Vector2(0.70f, 0.55f), center, size), Is.True);
      Assert.That(SoftFocusFieldLogic.IsInsideEllipse(new Vector2(0.05f, 0.05f), center, size), Is.False);
    }

    [Test]
    public void PeripheralPausePreservesExistingProgress()
    {
      var progress = SoftFocusFieldLogic.AdvancePurification(0.48f, 1f, 1.2f, 1f, false, true);
      Assert.That(progress, Is.EqualTo(0.48f).Within(0.0001f));
    }

    [Test]
    public void NormalFieldCompletesAfterConfiguredDuration()
    {
      var progress = 0f;
      for (var index = 0; index < 12; index++)
      {
        progress = SoftFocusFieldLogic.AdvancePurification(progress, 0.1f, 1.2f, 1f, true, true);
      }

      Assert.That(progress, Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void CriticalDrynessOrTooCloseCanHoldBeforeCompletion()
    {
      var progress = SoftFocusFieldLogic.AdvancePurification(0.96f, 1f, 1.2f, 0.7f, true, false);
      Assert.That(progress, Is.EqualTo(0.995f).Within(0.0001f));
    }
  }
}
