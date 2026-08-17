using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using KeepBlinking.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KeepBlinking.Tests
{
  public sealed class FirstLevelUpgradeFlowTests
  {
    private static readonly BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
    private readonly List<GameObject> _objects = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
      for (var i = _objects.Count - 1; i >= 0; i--)
        if (_objects[i] != null) UnityEngine.Object.DestroyImmediate(_objects[i]);
      _objects.Clear();
    }

    [Test]
    public void MissingBossUpgradeConfigurationFallsBackToFour()
    {
      var gameplay = CreateGameplay();
      SetField(gameplay, "_upgradesRequiredBeforeBoss", 0);
      Assert.That(gameplay.UpgradesRequiredBeforeBoss, Is.EqualTo(4));
    }

    [TestCase(CareExperienceState.Raw, 1)]
    [TestCase(CareExperienceState.Focused, 2)]
    [TestCase(CareExperienceState.Rested, 3)]
    public void ExperienceStatesHaveExactArrivalValues(CareExperienceState state, int expected)
    {
      Assert.That(CareExperienceStateInfo.Value(state), Is.EqualTo(expected));
    }

    [Test]
    public void BaseConversionUsesTwentyFiveAndFiftyPercent()
    {
      Assert.That(CareExperienceConversionLogic.ConvertedCount(1, 0.25f, true), Is.EqualTo(1));
      Assert.That(CareExperienceConversionLogic.ConvertedCount(8, 0.25f, true), Is.EqualTo(2));
      Assert.That(CareExperienceConversionLogic.ConvertedCount(8, 0.50f, false), Is.EqualTo(4));
      Assert.That(CareExperienceConversionLogic.ConvertedCount(8, 1f, false), Is.EqualTo(8));
    }

    [Test]
    public void ReleaseBonusesAreFiniteAndNonRecursive()
    {
      Assert.That(CareExperienceConversionLogic.TwinPulseRawBonus(39), Is.EqualTo(9));
      Assert.That(CareExperienceConversionLogic.ChainPulseGoldBonus(39), Is.EqualTo(3));
      Assert.That(CareExperienceConversionLogic.ChainPulseGoldBonus(0), Is.Zero);
    }

    [Test]
    public void FirstOfferHasThreeTierOneCardsAcrossCategories()
    {
      var offer = BuildOffer(1, new HashSet<FirstLevelModuleId>());
      AssertOfferBasics(offer, new HashSet<FirstLevelModuleId>());
      for (var i = 0; i < offer.Count; i++) Assert.That(GetDefinitionProperty<int>(offer[i], "Tier"), Is.EqualTo(1));
    }

    [Test]
    public void TierTwoAndThreeRequireTheirExactPredecessor()
    {
      var empty = BuildOffer(1, new HashSet<FirstLevelModuleId>());
      Assert.That(empty.Contains(FirstLevelModuleId.MoveTripleTrail), Is.False);
      Assert.That(empty.Contains(FirstLevelModuleId.MoveGoldenStreak), Is.False);

      var tierOne = new HashSet<FirstLevelModuleId> { FirstLevelModuleId.MoveTwinTrail };
      Assert.That(BuildOffer(2, tierOne).Contains(FirstLevelModuleId.MoveTripleTrail), Is.True);
      Assert.That(BuildOffer(2, tierOne).Contains(FirstLevelModuleId.MoveGoldenStreak), Is.False);

      var tierTwo = new HashSet<FirstLevelModuleId>
      {
        FirstLevelModuleId.MoveTwinTrail,
        FirstLevelModuleId.MoveTripleTrail,
      };
      Assert.That(BuildOffer(3, tierTwo).Contains(FirstLevelModuleId.MoveGoldenStreak), Is.True);
    }

    [TestCase(FirstLevelModuleId.MoveTwinTrail, FirstLevelModuleId.MoveTripleTrail, FirstLevelModuleId.MoveGoldenStreak)]
    [TestCase(FirstLevelModuleId.FocusMintShift, FirstLevelModuleId.FocusFarWave, FirstLevelModuleId.FocusFullRefine)]
    [TestCase(FirstLevelModuleId.RestGoldenRest, FirstLevelModuleId.RestCircuitQuietReturn, FirstLevelModuleId.RestFullRest)]
    [TestCase(FirstLevelModuleId.ReleaseTwinPulse, FirstLevelModuleId.ReleaseChainPulse, FirstLevelModuleId.ReleaseFullRelease)]
    public void EveryCareRouteCanReachItsSecondAndThirdTier(
      FirstLevelModuleId tierOne,
      FirstLevelModuleId tierTwo,
      FirstLevelModuleId tierThree)
    {
      var firstInstalled = new HashSet<FirstLevelModuleId> { tierOne };
      Assert.That(BuildOffer(2, firstInstalled).Contains(tierTwo), Is.True);
      Assert.That(BuildOffer(2, firstInstalled).Contains(tierThree), Is.False);

      var secondInstalled = new HashSet<FirstLevelModuleId> { tierOne, tierTwo };
      Assert.That(BuildOffer(3, secondInstalled).Contains(tierThree), Is.True);
    }

    [Test]
    public void UpgradeFourOffersThreeBossEvolutionsAndMatchesHighestRoute()
    {
      var installed = new HashSet<FirstLevelModuleId>
      {
        FirstLevelModuleId.FocusMintShift,
        FirstLevelModuleId.FocusFarWave,
        FirstLevelModuleId.FocusFullRefine,
        FirstLevelModuleId.MoveTwinTrail,
      };
      var offer = BuildOffer(4, installed);
      Assert.That(offer, Has.Count.EqualTo(3));
      Assert.That(offer.Contains(FirstLevelModuleId.BossMintCore), Is.True);
      for (var i = 0; i < offer.Count; i++) Assert.That(GetDefinitionProperty<bool>(offer[i], "BossOnly"), Is.True);
    }

    [Test]
    public void LegacyCardsNeverEnterTheCareCircuitPool()
    {
      var installed = new HashSet<FirstLevelModuleId>();
      for (var upgrade = 1; upgrade <= 4; upgrade++)
      {
        var offer = BuildOffer(upgrade, installed);
        Assert.That(offer, Is.Not.Empty);
        for (var i = 0; i < offer.Count; i++)
          Assert.That(GetDefinitionProperty<bool>(offer[i], "Legacy"), Is.False);
        installed.Add(offer[0]);
      }
    }

    [Test]
    public void AllSixteenCardsHaveStableIdsShortEnglishAndPassHealthAudit()
    {
      var definitions = GetDefinitions();
      Assert.That(definitions.Length, Is.EqualTo(16));
      var stableIds = new HashSet<string>();
      foreach (var definition in definitions)
      {
        var type = definition.GetType();
        var stableId = (string)type.GetProperty("StableId").GetValue(definition);
        var title = (string)type.GetProperty("CardName").GetValue(definition);
        var description = (string)type.GetProperty("ShortDescription").GetValue(definition);
        Assert.That(stableIds.Add(stableId), Is.True, "Duplicate stable id: " + stableId);
        Assert.That(title.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), Has.Length.LessThanOrEqualTo(2));
        Assert.That(description.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), Has.Length.LessThanOrEqualTo(8));
        Assert.That((bool)type.GetMethod("PassesHealthInvariantAudit").Invoke(definition, null), Is.True, stableId);
        Assert.That((bool)type.GetProperty("DirectlyGrantsExperience").GetValue(definition), Is.False);
        Assert.That((bool)type.GetProperty("SkipsPushAway").GetValue(definition), Is.False);
        Assert.That((bool)type.GetProperty("ReducesFocusShiftCycles").GetValue(definition), Is.False);
        Assert.That((bool)type.GetProperty("ShortensRest").GetValue(definition), Is.False);
        Assert.That((bool)type.GetProperty("ExtraExperienceUsesFormalFlight").GetValue(definition), Is.True);
        Assert.That((bool)type.GetProperty("SkipCanRewardRest").GetValue(definition), Is.False);
        Assert.That((bool)type.GetProperty("TrackingLossCanReward").GetValue(definition), Is.False);
        Assert.That((bool)type.GetProperty("RewardCanTriggerMoreThanOnce").GetValue(definition), Is.False);
      }
    }

    [Test]
    public void CareCircuitCardsMatchTheirPlayerFacingBeforeAfterValues()
    {
      AssertCard(FirstLevelModuleId.MoveTwinTrail, "move_twin_trail", "TWIN TRAIL", "Move drops two XP trails.", "1", "2");
      AssertCard(FirstLevelModuleId.MoveTripleTrail, "move_triple_trail", "TRIPLE TRAIL", "Move drops three XP trails.", "2", "3");
      AssertCard(FirstLevelModuleId.MoveGoldenStreak, "move_golden_streak", "GOLDEN STREAK", "Six move nodes drop Gold XP.", "0", "1");
      AssertCard(FirstLevelModuleId.FocusMintShift, "focus_mint_shift", "MINT SHIFT", "Focus turns half Raw XP mint.", "25%", "50%");
      AssertCard(FirstLevelModuleId.FocusFarWave, "focus_far_wave", "FAR WAVE", "Each Far point drops eight Mint XP.", "0", "8");
      AssertCard(FirstLevelModuleId.FocusFullRefine, "focus_full_refine", "FULL REFINE", "Two cycles turn all XP mint.", "50%", "ALL");
      AssertCard(FirstLevelModuleId.RestGoldenRest, "rest_golden_rest", "GOLDEN REST", "Rest drops two Gold XP each second.", "1", "2");
      AssertCard(FirstLevelModuleId.RestCircuitQuietReturn, "rest_quiet_return", "QUIET RETURN", "Reopen pauses spawns for six seconds.", "0s", "6s");
      AssertCard(FirstLevelModuleId.RestFullRest, "rest_full_rest", "FULL REST", "Full rest turns all Mint XP gold.", "50%", "ALL");
      AssertCard(FirstLevelModuleId.ReleaseTwinPulse, "release_twin_pulse", "TWIN PULSE", "Push sends a bonus second wave.", "1", "2");
      AssertCard(FirstLevelModuleId.ReleaseChainPulse, "release_chain_pulse", "CHAIN PULSE", "Ten collected XP drop one Gold XP.", "0", "1 / 10");
      AssertCard(FirstLevelModuleId.ReleaseFullRelease, "release_full_release", "FULL RELEASE", "Final pulse expands the Focus Field.", "55%", "80% · 12s");
      AssertCard(FirstLevelModuleId.BossShardRain, "boss_shard_rain", "SHARD RAIN", "Boss hits drop two XP trails.", "1", "2");
      AssertCard(FirstLevelModuleId.BossMintCore, "boss_mint_core", "MINT CORE", "Boss drops become Focused XP.", "PART", "ALL");
      AssertCard(FirstLevelModuleId.BossCoreEcho, "boss_core_echo", "CORE ECHO", "Correct rest breaks two cores.", "1", "2");
      AssertCard(FirstLevelModuleId.BossGoldRelease, "boss_gold_release", "GOLD RELEASE", "Final Push drops eight Gold XP.", "0", "8");
    }

    [Test]
    public void FourChoicesCompleteSequenceExactlyOnce()
    {
      var gameplay = CreateGameplay();
      var sequence = 0;
      var choices = 0;
      gameplay.FirstLevelUpgradeSequenceCompleted += () => sequence++;
      gameplay.ModuleChoiceCompleted += _ => choices++;
      var modules = new[]
      {
        FirstLevelModuleId.MoveTwinTrail,
        FirstLevelModuleId.FocusMintShift,
        FirstLevelModuleId.RestGoldenRest,
        FirstLevelModuleId.BossShardRain,
      };
      for (var i = 0; i < modules.Length; i++)
      {
        PrepareTransaction(gameplay, modules[i]);
        Invoke(gameplay, "FinalizeModuleInstallation");
        Invoke(gameplay, "FinalizeModuleInstallation");
      }
      Assert.That(choices, Is.EqualTo(4));
      Assert.That(sequence, Is.EqualTo(1));
      Assert.That(gameplay.IsFirstLevelUpgradeSequenceComplete, Is.True);
      Assert.That(gameplay.IsModuleInstallationPending, Is.False);
    }

    [Test]
    public void TutorialPauseFlagsResumeSymmetrically()
    {
      var gameplay = CreateGameplay();
      gameplay.SetTutorialMode(true);
      gameplay.SetTutorialRandomSpawningPaused(true);
      gameplay.SetTutorialRandomCrisisPaused(true);
      gameplay.SetTutorialSessionTimerPaused(true);
      gameplay.SetTutorialCollectionInputPaused(true);

      gameplay.ResumeFormalGameFlow();

      Assert.That(gameplay.IsTutorialModeEnabled, Is.False);
      Assert.That(gameplay.IsTutorialRandomSpawningPaused, Is.False);
      Assert.That(gameplay.IsTutorialRandomCrisisPaused, Is.False);
      Assert.That(gameplay.IsTutorialSessionTimerPaused, Is.False);
    }

    [Test]
    public void FirstLevelSessionPausesReleaseSymmetrically()
    {
      var gameplay = CreateGameplay();
      SetField(gameplay, "_firstLevelRandomFlowPaused", true);
      SetField(gameplay, "_firstLevelBossTransitionActive", true);
      SetField(gameplay, "_firstLevelBossMode", true);
      SetField(gameplay, "_firstLevelModalPaused", true);

      gameplay.ReleaseFirstLevelSessionPauses();

      Assert.That(gameplay.IsFirstLevelModalPaused, Is.False);
      Assert.That(gameplay.IsFirstLevelBossTransitionActive, Is.False);
      Assert.That(gameplay.IsFirstLevelBossMode, Is.False);
      Assert.That((bool)GetField(gameplay, "_firstLevelRandomFlowPaused"), Is.False);
    }

    [Test]
    public void BossTransitionFlagIsClearedWhenBossModeStarts()
    {
      var gameplay = CreateGameplay();
      gameplay.BeginFirstLevelBossTransition();
      Assert.That(gameplay.IsFirstLevelBossTransitionActive, Is.True);

      gameplay.BeginFirstLevelBossMode();

      Assert.That(gameplay.IsFirstLevelBossTransitionActive, Is.False);
      Assert.That(gameplay.IsFirstLevelBossMode, Is.True);
    }

    [Test]
    public void InvalidPendingSelectionReleasesUpgradeLock()
    {
      var gameplay = CreateGameplay();
      PrepareTransaction(gameplay, FirstLevelModuleId.MoveTwinTrail);
      SetField(gameplay, "_selectedModuleCardIndex", 3);

      LogAssert.Expect(LogType.Error, "KeepBlinking module installation lost its selected card. The upgrade UI is being safely released.");
      Invoke(gameplay, "FinalizeModuleInstallation");

      Assert.That(gameplay.IsModuleUpgradeOpen, Is.False);
      Assert.That(gameplay.IsModuleInstallationPending, Is.False);
      Assert.That(gameplay.InstalledFirstLevelModuleCount, Is.Zero);
    }

    [Test]
    public void EarlyBuildSignalCannotLeaveGameplay()
    {
      var gameplay = CreateGameplay();
      var session = gameplay.gameObject.AddComponent<FirstLevelSessionController>();
      SetField(session, "_gameplay", gameplay);
      SetEnumField(session, "_state", "Gameplay");

      LogAssert.Expect(LogType.Warning, "KeepBlinking ignored an early build-complete signal at 0/4 modules.");
      Invoke(session, "HandleBuildCompleted");

      Assert.That(session.State, Is.EqualTo(FirstLevelSessionState.Gameplay));
    }

    [Test]
    public void FailingObserverCannotInterruptModuleCompletion()
    {
      var gameplay = CreateGameplay();
      var choices = 0;
      gameplay.FirstLevelModuleInstalled += _ => throw new InvalidOperationException("observer failure");
      gameplay.ModuleChoiceCompleted += _ => choices++;
      PrepareTransaction(gameplay, FirstLevelModuleId.MoveTwinTrail);

      LogAssert.Expect(LogType.Error, "KeepBlinking gameplay signal subscriber failed: FirstLevelModuleInstalled.");
      LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: observer failure"));
      Invoke(gameplay, "FinalizeModuleInstallation");

      Assert.That(choices, Is.EqualTo(1));
      Assert.That(gameplay.IsModuleInstallationPending, Is.False);
      Assert.That(gameplay.IsModuleUpgradeOpen, Is.False);
    }

    // Device-space gravity: (0,0,-1) lying face up, (0,0,+1) lying face down,
    // (0,-1,0) held upright in front of the player.
    private static Vector3 GravityForPitchFromUpright(float degreesTippedBack)
    {
      return Quaternion.Euler(degreesTippedBack, 0f, 0f) * Vector3.down;
    }

    [Test]
    public void ScreenDownRestMotionStillRejectsPortraitAndAcceptsFaceDown()
    {
      Assert.That(ScreenDownRestMotionLogic.IsScreenDown(Vector3.back, 40f), Is.False, "lying face up");
      Assert.That(ScreenDownRestMotionLogic.IsScreenDown(Vector3.down, 40f), Is.False, "held upright");
      Assert.That(ScreenDownRestMotionLogic.IsScreenDown(Vector3.forward, 40f), Is.True, "lying face down");
      Assert.That(ScreenDownRestMotionLogic.IsScreenDown(new Vector3(0f, -0.34f, 0.94f), 40f), Is.True, "20 deg off flat");
      Assert.That(ScreenDownRestMotionLogic.IsScreenDown(new Vector3(0f, -0.77f, 0.64f), 40f), Is.False, "50 deg off flat");
    }

    [Test]
    public void ScreenDownRestDetectsFaceDownFromAnyStartingPose()
    {
      // The pose the player actually calibrates in is the phone held up in front of the face,
      // roughly 60 deg from horizontal -- only about 120 deg away from face-down, which is why
      // a relative-angle judgement against the neutral pose never fired on device.
      foreach (var neutral in new[]
               {
                 GravityForPitchFromUpright(0f),
                 GravityForPitchFromUpright(30f),
                 GravityForPitchFromUpright(60f),
                 Vector3.back,
               })
      {
        Assert.That(ScreenDownRestMotionLogic.IsScreenDown(neutral, 40f), Is.False, $"neutral {neutral} must not read as face down");
        Assert.That(ScreenDownRestMotionLogic.IsScreenDown(Vector3.forward, 40f), Is.True, $"face down after neutral {neutral}");
      }
    }

    [Test]
    public void ScreenDownRestRequiresStableMotionAndNeutralReturn()
    {
      Assert.That(ScreenDownRestMotionLogic.IsStable(1f, 0.18f, 0.2f, 0.35f), Is.True);
      Assert.That(ScreenDownRestMotionLogic.IsStable(1.4f, 0.18f, 0.2f, 0.35f), Is.False);
      Assert.That(ScreenDownRestMotionLogic.IsStable(1f, 0.18f, 0.8f, 0.35f), Is.False);
      // A platform reporting m/s^2 must still read as resting rather than as permanent motion.
      Assert.That(ScreenDownRestMotionLogic.IsStable(ScreenDownRestMotionLogic.StandardGravity, 0.18f, 0.2f, 0.35f), Is.True);

      var neutral = GravityForPitchFromUpright(30f);
      Assert.That(ScreenDownRestMotionLogic.IsReturned(neutral, GravityForPitchFromUpright(48f), 20f), Is.True);
      Assert.That(ScreenDownRestMotionLogic.IsReturned(neutral, GravityForPitchFromUpright(54f), 20f), Is.False);
      // Heading changes must not block the return. Device-space gravity is the world down
      // vector seen through the inverse device rotation, and yaw about world up drops out --
      // unlike the attitude quaternion, which carried heading and rejected the return whenever
      // the player had turned more than the tolerance while picking the phone back up.
      foreach (var yaw in new[] { 0f, 45f, 120f, 250f })
      {
        // Tipping the screen 30 deg up is a 30 deg pitch up of the device, so the pose that
        // produces `neutral` is Rx(-30) with any heading applied on top of it.
        var deviceRotation = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(-30f, 0f, 0f);
        var gravityInDeviceSpace = Quaternion.Inverse(deviceRotation) * Vector3.down;
        Assert.That(
          ScreenDownRestMotionLogic.IsReturned(neutral, gravityInDeviceSpace, 20f),
          Is.True,
          $"return must survive a {yaw} deg heading change");
      }
      Assert.That(ScreenDownRestMotionLogic.IsReturned(neutral, Vector3.forward, 20f), Is.False, "face down is not neutral");
    }

    [Test]
    public void DryCorePreparationHasNoGazeLockAndBlinkSignalCannotAdvance()
    {
      Assert.That(Enum.IsDefined(typeof(DryCoreBossState), "FocusWeakPoint"), Is.False);
      Assert.That(Enum.IsDefined(typeof(DryCoreBossPrompt), "FocusCore"), Is.False);

      var gameplay = CreateGameplay();
      SetField(gameplay, "_autoReadKeepBlinkingEyeInput", false);
      var boss = gameplay.gameObject.AddComponent<DryCoreBossController>();
      boss.Initialize(gameplay);
      SetField(boss, "_remainingCores", 3);
      Invoke(boss, "BeginSoftBlinkRound");

      Invoke(boss, "HandleSoftBlinkPerformed", 6);
      Assert.That(boss.State, Is.EqualTo(DryCoreBossState.WaitSoftBlink));
      Assert.That(boss.RemainingCores, Is.EqualTo(3));
    }

    [Test]
    public void SoftBlinkReopenThresholdAdaptsBelowTheLegacyAbsoluteGate()
    {
      var gameplay = CreateGameplay();
      SetField(gameplay, "_softBlinkRelativeReopenRatio", 0.62f);
      SetField(gameplay, "_softBlinkMinimumReopenValue", 0.30f);
      SetField(gameplay, "_openEyeReleaseThreshold", 0.55f);
      var method = typeof(EdgeOrbitHarvestMvp).GetMethod("CalculateAdaptiveSoftBlinkReopenThreshold", InstancePrivate);

      Assert.That(method, Is.Not.Null);
      Assert.That((float)method.Invoke(gameplay, new object[] { 0.80f }), Is.EqualTo(0.496f).Within(0.001f));
      Assert.That((float)method.Invoke(gameplay, new object[] { 0.35f }), Is.EqualTo(0.30f).Within(0.001f));
    }

    private static void AssertOfferBasics(List<FirstLevelModuleId> offer, HashSet<FirstLevelModuleId> installed)
    {
      Assert.That(offer, Has.Count.EqualTo(3));
      Assert.That(new HashSet<FirstLevelModuleId>(offer), Has.Count.EqualTo(3));
      var categories = new HashSet<FirstLevelModuleCategory>();
      for (var i = 0; i < offer.Count; i++)
      {
        Assert.That(installed.Contains(offer[i]), Is.False);
        categories.Add(GetDefinitionProperty<FirstLevelModuleCategory>(offer[i], "Category"));
      }
      Assert.That(categories.Count, Is.GreaterThanOrEqualTo(2));
    }

    private static List<FirstLevelModuleId> BuildOffer(int number, HashSet<FirstLevelModuleId> installed)
    {
      var catalog = typeof(FirstLevelModuleId).Assembly.GetType("KeepBlinking.Gameplay.FirstLevelUpgradeCatalog");
      return (List<FirstLevelModuleId>)catalog.GetMethod("BuildOffer", StaticPrivate).Invoke(null, new object[] { number, installed });
    }

    private static Array GetDefinitions()
    {
      var catalog = typeof(FirstLevelModuleId).Assembly.GetType("KeepBlinking.Gameplay.FirstLevelUpgradeCatalog");
      return (Array)catalog.GetProperty("Definitions", StaticPrivate).GetValue(null);
    }

    private static T GetDefinitionProperty<T>(FirstLevelModuleId id, string property)
    {
      var catalog = typeof(FirstLevelModuleId).Assembly.GetType("KeepBlinking.Gameplay.FirstLevelUpgradeCatalog");
      var definition = catalog.GetMethod("Get", StaticPrivate).Invoke(null, new object[] { id });
      return (T)definition.GetType().GetProperty(property).GetValue(definition);
    }

    private static void AssertCard(
      FirstLevelModuleId id,
      string stableId,
      string title,
      string description,
      string before,
      string after)
    {
      Assert.That(GetDefinitionProperty<string>(id, "StableId"), Is.EqualTo(stableId));
      Assert.That(GetDefinitionProperty<string>(id, "CardName"), Is.EqualTo(title));
      Assert.That(GetDefinitionProperty<string>(id, "ShortDescription"), Is.EqualTo(description));
      Assert.That(GetDefinitionProperty<string>(id, "BeforeLabel"), Is.EqualTo(before));
      Assert.That(GetDefinitionProperty<string>(id, "AfterLabel"), Is.EqualTo(after));
    }

    private EdgeOrbitHarvestMvp CreateGameplay()
    {
      var root = new GameObject("CARE CIRCUIT Test");
      _objects.Add(root);
      return root.AddComponent<EdgeOrbitHarvestMvp>();
    }

    private static void PrepareTransaction(EdgeOrbitHarvestMvp gameplay, FirstLevelModuleId module)
    {
      var offer = (List<FirstLevelModuleId>)GetField(gameplay, "_currentModuleOffer");
      offer.Clear();
      offer.Add(module);
      SetEnumField(gameplay, "_gameplayState", "ModuleUpgrade");
      SetEnumField(gameplay, "_resumeStateAfterUpgrade", "Orbiting");
      SetField(gameplay, "_moduleChoicePending", true);
      SetField(gameplay, "_selectedModuleCardIndex", 0);
      SetField(gameplay, "_moduleInstallStartedAt", Time.unscaledTime);
      SetField(gameplay, "_currentUpgradeSampleRequirement", 10);
    }

    private static object GetField(object target, string name)
    {
      return target.GetType().GetField(name, InstancePrivate).GetValue(target);
    }

    private static void SetField(object target, string name, object value)
    {
      target.GetType().GetField(name, InstancePrivate).SetValue(target, value);
    }

    private static void SetEnumField(object target, string name, string value)
    {
      var field = target.GetType().GetField(name, InstancePrivate);
      field.SetValue(target, Enum.Parse(field.FieldType, value));
    }

    private static void Invoke(object target, string method, params object[] arguments)
    {
      target.GetType().GetMethod(method, InstancePrivate).Invoke(target, arguments);
    }
  }
}
