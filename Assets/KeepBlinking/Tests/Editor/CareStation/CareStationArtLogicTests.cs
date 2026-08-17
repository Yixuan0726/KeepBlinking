using KeepBlinking.CareStation;
using NUnit.Framework;

namespace KeepBlinking.Tests
{
  public sealed class CareStationArtLogicTests
  {
    [TestCase(CareCartTier.SmallBasket, 2)]
    [TestCase(CareCartTier.DeepCart, 4)]
    [TestCase(CareCartTier.CareTower, 8)]
    public void CartTiersExposeStableSlotCounts(CareCartTier tier, int expected)
    {
      Assert.That(CareStationArtLoadLogic.Capacity(tier), Is.EqualTo(expected));
    }

    [Test]
    public void EmptyCartNeverDisplaysAnEmptyBottle()
    {
      Assert.That(CareStationArtLoadLogic.VisibleBottleCount(CareCartTier.CareTower, CareCartLoadPreview.Empty), Is.Zero);
    }

    [TestCase(CareCartTier.SmallBasket, 2)]
    [TestCase(CareCartTier.DeepCart, 4)]
    [TestCase(CareCartTier.CareTower, 8)]
    public void FullLoadUsesEveryAvailableSlot(CareCartTier tier, int expected)
    {
      Assert.That(CareStationArtLoadLogic.VisibleBottleCount(tier, CareCartLoadPreview.FullMint), Is.EqualTo(expected));
    }

    [Test]
    public void MixedLoadPlacesGoldWithoutChangingCapacity()
    {
      Assert.That(CareStationArtLoadLogic.VisibleBottleCount(CareCartTier.CareTower, CareCartLoadPreview.MixedMintAndGold), Is.EqualTo(8));
      Assert.That(CareStationArtLoadLogic.IsGold(CareCartLoadPreview.MixedMintAndGold, 2), Is.True);
      Assert.That(CareStationArtLoadLogic.IsGold(CareCartLoadPreview.MixedMintAndGold, 5), Is.True);
      Assert.That(CareStationArtLoadLogic.IsGold(CareCartLoadPreview.MixedMintAndGold, 4), Is.False);
    }

    [Test]
    public void PendingRatioOnlyChoosesVisualLoadAndDoesNotReturnExperience()
    {
      Assert.That(CareStationArtLoadLogic.FromPendingRatio(0f), Is.EqualTo(CareCartLoadPreview.Empty));
      Assert.That(CareStationArtLoadLogic.FromPendingRatio(0.5f), Is.EqualTo(CareCartLoadPreview.PartialMint));
      Assert.That(CareStationArtLoadLogic.FromPendingRatio(1f), Is.EqualTo(CareCartLoadPreview.FullMint));
    }
  }
}
