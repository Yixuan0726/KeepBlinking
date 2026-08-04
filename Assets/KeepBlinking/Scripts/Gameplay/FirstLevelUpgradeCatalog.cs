using System;
using System.Collections.Generic;
using UnityEngine;

namespace KeepBlinking.Gameplay
{
  public enum FirstLevelModuleId
  {
    None,
    ChainBlink,
    WideBlink,
    LockHold,
    WideChain,
    QuietWake,
    QuietField,
    CoreEcho,
    DeepRecovery,
    BonusSample,
    XpDiscount,
    XpReserve,
    LoopBonus,
    WakeEcho,
    RestCache,
    PreciseHarvest,
    FullLoop,
  }

  public enum FirstLevelModuleCategory
  {
    Blink,
    Rest,
    Distance,
    Combo,
  }

  [Flags]
  internal enum FirstLevelCategoryMask
  {
    None = 0,
    Blink = 1,
    Rest = 2,
    Distance = 4,
  }

  internal sealed class FirstLevelModuleDefinition
  {
    public FirstLevelModuleId Id { get; }
    public FirstLevelModuleCategory Category { get; }
    public string CategoryLabel { get; }
    public string Title { get; }
    public string Description { get; }
    public string Delta { get; }
    public Color AccentColor { get; }
    public int Tier { get; }
    public FirstLevelModuleId RequiredModule { get; }
    public FirstLevelCategoryMask RequiredCategories { get; }

    public FirstLevelModuleDefinition(
      FirstLevelModuleId id,
      FirstLevelModuleCategory category,
      string categoryLabel,
      string title,
      string description,
      string delta,
      Color accentColor,
      int tier,
      FirstLevelModuleId requiredModule = FirstLevelModuleId.None,
      FirstLevelCategoryMask requiredCategories = FirstLevelCategoryMask.None)
    {
      Id = id;
      Category = category;
      CategoryLabel = categoryLabel;
      Title = title;
      Description = description;
      Delta = delta;
      AccentColor = accentColor;
      Tier = tier;
      RequiredModule = requiredModule;
      RequiredCategories = requiredCategories;
    }
  }

  internal static class FirstLevelUpgradeCatalog
  {
    private static readonly Color RestColor = new Color32(0x91, 0xB8, 0xD0, 0xFF);

    internal static readonly FirstLevelModuleDefinition[] Definitions =
    {
      new FirstLevelModuleDefinition(FirstLevelModuleId.ChainBlink, FirstLevelModuleCategory.Blink, "FOCUS", "Chain Focus", "Purify 1 more target inside the field.", "Capacity +1", KeepBlinkingTheme.AccentPrimary, 1),
      new FirstLevelModuleDefinition(FirstLevelModuleId.WideBlink, FirstLevelModuleCategory.Blink, "FOCUS", "Wide Focus", "Expand the Soft Focus Field by 15%.", "Field +15%", KeepBlinkingTheme.AccentPrimary, 2, FirstLevelModuleId.ChainBlink),
      new FirstLevelModuleDefinition(FirstLevelModuleId.LockHold, FirstLevelModuleCategory.Blink, "FOCUS", "Focus Hold", "Peripheral gaze keeps focus for 0.4s.", "Hold +0.4s", KeepBlinkingTheme.AccentPrimary, 2, FirstLevelModuleId.ChainBlink),
      new FirstLevelModuleDefinition(FirstLevelModuleId.WideChain, FirstLevelModuleCategory.Blink, "FOCUS", "Calm Capacity", "Purify 1 additional field target.", "Capacity +1", KeepBlinkingTheme.AccentPrimary, 3, FirstLevelModuleId.WideBlink),

      new FirstLevelModuleDefinition(FirstLevelModuleId.QuietWake, FirstLevelModuleCategory.Rest, "REST", "Quiet Wake", "Reopen after the cue. Spawns pause.", "Pause +2s", RestColor, 1),
      new FirstLevelModuleDefinition(FirstLevelModuleId.QuietField, FirstLevelModuleCategory.Rest, "REST", "Quiet Field", "Correct reopen briefly expands the field.", "Field +15%", RestColor, 3, FirstLevelModuleId.QuietWake),
      new FirstLevelModuleDefinition(FirstLevelModuleId.CoreEcho, FirstLevelModuleCategory.Rest, "REST", "Core Echo", "Successful rest deals 1 extra core damage.", "Damage +1", RestColor, 2, FirstLevelModuleId.QuietWake),
      new FirstLevelModuleDefinition(FirstLevelModuleId.DeepRecovery, FirstLevelModuleCategory.Rest, "REST", "Deep Recovery", "After rest, the next target starts at 50%.", "Start 50%", RestColor, 2, FirstLevelModuleId.QuietWake),

      new FirstLevelModuleDefinition(FirstLevelModuleId.BonusSample, FirstLevelModuleCategory.Distance, "DISTANCE", "Bonus Sample", "Push away for 1 extra XP.", "XP +1", KeepBlinkingTheme.AccentWarm, 1),
      new FirstLevelModuleDefinition(FirstLevelModuleId.XpDiscount, FirstLevelModuleCategory.Distance, "DISTANCE", "XP Discount", "Next upgrade costs 1 less XP.", "Cost -1", KeepBlinkingTheme.AccentWarm, 2, FirstLevelModuleId.BonusSample),
      new FirstLevelModuleDefinition(FirstLevelModuleId.XpReserve, FirstLevelModuleCategory.Distance, "DISTANCE", "XP Reserve", "Keep 30% XP after upgrading.", "Keep 30%", KeepBlinkingTheme.AccentWarm, 2, FirstLevelModuleId.BonusSample),
      new FirstLevelModuleDefinition(FirstLevelModuleId.LoopBonus, FirstLevelModuleCategory.Distance, "DISTANCE", "Loop Bonus", "The next push-away gains 2 XP.", "XP +2", KeepBlinkingTheme.AccentWarm, 3, FirstLevelModuleId.BonusSample),

      new FirstLevelModuleDefinition(FirstLevelModuleId.WakeEcho, FirstLevelModuleCategory.Combo, "COMBO", "Wake Echo", "After rest, the field briefly reaches farther.", "Field +15%", KeepBlinkingTheme.TextPrimary, 3, requiredCategories: FirstLevelCategoryMask.Blink | FirstLevelCategoryMask.Rest),
      new FirstLevelModuleDefinition(FirstLevelModuleId.RestCache, FirstLevelModuleCategory.Combo, "COMBO", "Rest Cache", "Rest creates 1 sample for push-away.", "XP +1", KeepBlinkingTheme.TextPrimary, 3, requiredCategories: FirstLevelCategoryMask.Rest | FirstLevelCategoryMask.Distance),
      new FirstLevelModuleDefinition(FirstLevelModuleId.PreciseHarvest, FirstLevelModuleCategory.Combo, "COMBO", "Field Harmony", "Purify 3 together to create 1 sample.", "XP +1", KeepBlinkingTheme.TextPrimary, 3, requiredCategories: FirstLevelCategoryMask.Blink | FirstLevelCategoryMask.Distance),
      new FirstLevelModuleDefinition(FirstLevelModuleId.FullLoop, FirstLevelModuleCategory.Combo, "COMBO", "Full Loop", "Complete all 3 actions to gain 1 gold sample.", "XP +1", KeepBlinkingTheme.TextPrimary, 4, requiredCategories: FirstLevelCategoryMask.Blink | FirstLevelCategoryMask.Rest | FirstLevelCategoryMask.Distance),
    };

    internal static FirstLevelModuleDefinition Get(FirstLevelModuleId id)
    {
      for (var i = 0; i < Definitions.Length; i++)
      {
        if (Definitions[i].Id == id)
        {
          return Definitions[i];
        }
      }

      return Definitions[0];
    }

    internal static List<FirstLevelModuleId> BuildOffer(int upgradeNumber, HashSet<FirstLevelModuleId> installed)
    {
      var offer = new List<FirstLevelModuleId>(3);
      if (upgradeNumber <= 1)
      {
        offer.Add(FirstLevelModuleId.ChainBlink);
        offer.Add(FirstLevelModuleId.QuietWake);
        offer.Add(FirstLevelModuleId.BonusSample);
        return offer;
      }

      var legal = new List<FirstLevelModuleDefinition>();
      for (var i = 0; i < Definitions.Length; i++)
      {
        var definition = Definitions[i];
        if (!installed.Contains(definition.Id) && IsLegal(definition, installed))
        {
          legal.Add(definition);
        }
      }

      if (upgradeNumber == 2)
      {
        AddFirstMatching(offer, legal, definition => definition.Category != FirstLevelModuleCategory.Combo && definition.Tier >= 2);
        AddFirstMatching(offer, legal, definition => definition.Tier == 1);
      }
      else if (upgradeNumber == 3)
      {
        AddFirstMatching(offer, legal, definition => definition.Category == FirstLevelModuleCategory.Combo);
        AddFirstMatching(offer, legal, definition => definition.Category != FirstLevelModuleCategory.Combo && definition.Tier >= 2);
      }
      else
      {
        AddSpecific(offer, legal, FirstLevelModuleId.FullLoop);
        AddFirstMatching(offer, legal, definition => definition.Category != FirstLevelModuleCategory.Combo && definition.Tier >= 3);
        AddFirstMatching(offer, legal, definition => definition.Category == FirstLevelModuleCategory.Combo);
      }

      AddFirstMatching(offer, legal, definition => definition.Tier == 1);
      AddFirstMatching(offer, legal, definition => definition.Category != FirstLevelModuleCategory.Combo && definition.Tier >= 2);
      AddFirstMatching(offer, legal, definition => definition.Category == FirstLevelModuleCategory.Combo);

      for (var i = 0; i < legal.Count && offer.Count < 3; i++)
      {
        AddUnique(offer, legal[i].Id);
      }

      return offer;
    }

    private static bool IsLegal(FirstLevelModuleDefinition definition, HashSet<FirstLevelModuleId> installed)
    {
      if (definition.RequiredModule != FirstLevelModuleId.None && !installed.Contains(definition.RequiredModule))
      {
        return false;
      }

      var ownedCategories = GetOwnedCategories(installed);
      return (ownedCategories & definition.RequiredCategories) == definition.RequiredCategories;
    }

    private static FirstLevelCategoryMask GetOwnedCategories(HashSet<FirstLevelModuleId> installed)
    {
      var result = FirstLevelCategoryMask.None;
      foreach (var id in installed)
      {
        switch (Get(id).Category)
        {
          case FirstLevelModuleCategory.Blink:
            result |= FirstLevelCategoryMask.Blink;
            break;
          case FirstLevelModuleCategory.Rest:
            result |= FirstLevelCategoryMask.Rest;
            break;
          case FirstLevelModuleCategory.Distance:
            result |= FirstLevelCategoryMask.Distance;
            break;
        }
      }

      return result;
    }

    private static void AddSpecific(List<FirstLevelModuleId> offer, List<FirstLevelModuleDefinition> legal, FirstLevelModuleId id)
    {
      for (var i = 0; i < legal.Count; i++)
      {
        if (legal[i].Id == id)
        {
          AddUnique(offer, id);
          return;
        }
      }
    }

    private static void AddFirstMatching(
      List<FirstLevelModuleId> offer,
      List<FirstLevelModuleDefinition> legal,
      Predicate<FirstLevelModuleDefinition> predicate)
    {
      if (offer.Count >= 3)
      {
        return;
      }

      for (var i = 0; i < legal.Count; i++)
      {
        if (predicate(legal[i]) && AddUnique(offer, legal[i].Id))
        {
          return;
        }
      }
    }

    private static bool AddUnique(List<FirstLevelModuleId> offer, FirstLevelModuleId id)
    {
      if (id == FirstLevelModuleId.None || offer.Contains(id) || offer.Count >= 3)
      {
        return false;
      }

      offer.Add(id);
      return true;
    }
  }
}
