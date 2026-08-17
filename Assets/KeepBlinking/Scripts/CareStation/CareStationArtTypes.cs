using System;

namespace KeepBlinking.CareStation
{
  public enum CareCrewRole
  {
    DustKeeper,
    DrySpotMender,
    CareCourier,
    RestGuide,
  }

  public enum CareCrewAnimationState
  {
    Idle,
    Walk,
    Work,
    Rest,
    Cheer,
  }

  public enum CareCartTier
  {
    SmallBasket,
    DeepCart,
    CareTower,
  }

  public enum CareCartLoadPreview
  {
    Empty,
    PartialMint,
    FullMint,
    OneGoldBottle,
    MixedMintAndGold,
  }

  public static class CareStationArtLoadLogic
  {
    public static int Capacity(CareCartTier tier)
    {
      switch (tier)
      {
        case CareCartTier.SmallBasket: return 2;
        case CareCartTier.DeepCart: return 4;
        case CareCartTier.CareTower: return 8;
        default: return 0;
      }
    }

    public static int VisibleBottleCount(CareCartTier tier, CareCartLoadPreview load)
    {
      var capacity = Capacity(tier);
      switch (load)
      {
        case CareCartLoadPreview.Empty: return 0;
        case CareCartLoadPreview.PartialMint: return Math.Max(1, capacity / 2);
        case CareCartLoadPreview.FullMint:
        case CareCartLoadPreview.MixedMintAndGold: return capacity;
        case CareCartLoadPreview.OneGoldBottle: return capacity > 0 ? 1 : 0;
        default: return 0;
      }
    }

    public static bool IsGold(CareCartLoadPreview load, int bottleIndex)
    {
      if (bottleIndex < 0) return false;
      if (load == CareCartLoadPreview.OneGoldBottle) return bottleIndex == 0;
      return load == CareCartLoadPreview.MixedMintAndGold && bottleIndex % 3 == 2;
    }

    public static CareCartLoadPreview FromPendingRatio(float normalizedPending)
    {
      if (normalizedPending <= 0f) return CareCartLoadPreview.Empty;
      return normalizedPending < 0.999f ? CareCartLoadPreview.PartialMint : CareCartLoadPreview.FullMint;
    }
  }
}
