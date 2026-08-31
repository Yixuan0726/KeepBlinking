using System;

namespace KeepBlinking.CareStation
{
  public readonly struct CareStationProductionSettlement
  {
    public readonly int LegacyStored;
    public readonly int ProducedStored;
    public readonly int ProducedDiscarded;
    public readonly bool StorageFull;

    public int TotalStored => LegacyStored + ProducedStored;

    public CareStationProductionSettlement(
      int legacyStored,
      int producedStored,
      int producedDiscarded,
      bool storageFull)
    {
      LegacyStored = Math.Max(0, legacyStored);
      ProducedStored = Math.Max(0, producedStored);
      ProducedDiscarded = Math.Max(0, producedDiscarded);
      StorageFull = storageFull;
    }
  }

  /// <summary>
  /// Authoritative phase-one Cart and Auto Shift logistics.
  /// </summary>
  public sealed class CareStationProductionController
  {
    public CareCartSettlementResult SettleCart(
      CareStationSaveData save,
      int throughput,
      string settlementId,
      CareEconomyConfiguration configuration)
    {
      return CareEconomyRules.SettleCart(save, throughput, settlementId, configuration);
    }

    // Legacy test/API wrapper. New runtime code uses SettleCart so Coins,
    // inventory deduction and replay protection are one persisted transaction.
    public CareStationProductionSettlement Settle(
      CareStationSaveData save,
      int producedFullBottles)
    {
      if (save == null) return default;
      var result = SettleCart(
        save,
        producedFullBottles,
        string.Empty,
        new CareEconomyConfiguration());
      return new CareStationProductionSettlement(
        0,
        result.BottlesProduced,
        0,
        result.StorageFull);
    }

    public int SettleLegacyPending(CareStationSaveData save)
    {
      if (save == null || save.queuedOfflineXP <= 0) return 0;
      // v21 migration no longer treats the legacy queue as producible output.
      // Convert it to quota once, then clear it.
      var converted = Math.Max(0, save.queuedOfflineXP);
      save.careEnergy += converted;
      save.queuedOfflineXP = 0;
      return 0;
    }
  }
}
