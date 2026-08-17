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
  /// Authoritative offline logistics. Offline work is already transported by
  /// the crew and therefore settles directly into available storage; care
  /// rewards remain outside this service and still require their real flight.
  /// </summary>
  public sealed class CareStationProductionController
  {
    public CareStationProductionSettlement Settle(
      CareStationSaveData save,
      int producedFullBottles)
    {
      if (save == null) return default;

      var legacyStored = SettleLegacyPending(save);
      var produced = Math.Max(0, producedFullBottles);
      var available = CareStationStorageRules.RemainingForAutomaticOfflineSettlement(save);
      var accepted = Math.Min(produced, available);
      var discarded = produced - accepted;
      save.storedFullBottles += accepted;
      save.collectedExperienceCount = save.storedFullBottles + save.storedGoldBottles;
      save.discardedOfflineBottleCount += discarded;
      save.offlineProductionPausedByFullStorage =
        CareStationStorageRules.Remaining(save) <= 0 || discarded > 0;
      return new CareStationProductionSettlement(
        legacyStored,
        accepted,
        discarded,
        save.offlineProductionPausedByFullStorage);
    }

    public int SettleLegacyPending(CareStationSaveData save)
    {
      if (save == null || save.queuedOfflineXP <= 0) return 0;
      var accepted = Math.Min(save.queuedOfflineXP, CareStationStorageRules.RemainingForAutomaticOfflineSettlement(save));
      save.queuedOfflineXP -= accepted;
      save.storedFullBottles += accepted;
      save.collectedExperienceCount = save.storedFullBottles + save.storedGoldBottles;
      return accepted;
    }
  }
}
