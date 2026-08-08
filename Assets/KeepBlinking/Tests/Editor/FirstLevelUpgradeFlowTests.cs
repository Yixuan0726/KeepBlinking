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
    private readonly List<GameObject> _objectsToDestroy = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
      for (var i = _objectsToDestroy.Count - 1; i >= 0; i--)
      {
        if (_objectsToDestroy[i] != null)
        {
          UnityEngine.Object.DestroyImmediate(_objectsToDestroy[i]);
        }
      }
      _objectsToDestroy.Clear();
    }

    [Test]
    public void MissingBossUpgradeConfigurationFallsBackToFour()
    {
      var gameplay = CreateGameplay();
      SetField(gameplay, "_upgradesRequiredBeforeBoss", 0);

      Assert.That(gameplay.UpgradesRequiredBeforeBoss, Is.EqualTo(4));
    }

    [TestCase(0, FirstLevelModuleId.WiderField)]
    [TestCase(1, FirstLevelModuleId.BlinkBloom)]
    [TestCase(2, FirstLevelModuleId.ExtraSamples)]
    public void EveryFirstOfferCardCommitsOnceAndReturnsToGameplay(int selectedIndex, FirstLevelModuleId expectedModule)
    {
      var gameplay = CreateGameplay();
      var installedCount = 0;
      var completedCount = 0;
      var buildCompletedCount = 0;
      gameplay.FirstLevelModuleInstalled += module =>
      {
        Assert.That(module, Is.EqualTo(expectedModule));
        installedCount++;
      };
      gameplay.ModuleChoiceCompleted += index =>
      {
        Assert.That(index, Is.EqualTo(selectedIndex));
        completedCount++;
      };
      gameplay.FirstLevelBuildCompleted += () => buildCompletedCount++;

      PrepareTransaction(
        gameplay,
        new[] { FirstLevelModuleId.WiderField, FirstLevelModuleId.BlinkBloom, FirstLevelModuleId.ExtraSamples },
        selectedIndex);
      Invoke(gameplay, "FinalizeModuleInstallation");
      Invoke(gameplay, "FinalizeModuleInstallation");

      Assert.That(gameplay.IsModuleUpgradeOpen, Is.False);
      Assert.That(gameplay.IsModuleInstallationPending, Is.False);
      Assert.That(gameplay.InstalledFirstLevelModuleCount, Is.EqualTo(1));
      Assert.That(gameplay.HasFirstLevelModule(expectedModule), Is.True);
      Assert.That(installedCount, Is.EqualTo(1));
      Assert.That(completedCount, Is.EqualTo(1));
      Assert.That(buildCompletedCount, Is.Zero);
      Assert.That(gameplay.IsFirstLevelBuildComplete, Is.False);
      Assert.That(Time.timeScale, Is.EqualTo(1f));
    }

    [Test]
    public void FourSequentialChoicesCompleteBuildExactlyOnce()
    {
      var gameplay = CreateGameplay();
      var moduleEvents = 0;
      var choiceEvents = 0;
      var sequenceEvents = 0;
      var buildEvents = 0;
      gameplay.FirstLevelModuleInstalled += _ => moduleEvents++;
      gameplay.ModuleChoiceCompleted += _ => choiceEvents++;
      gameplay.FirstLevelUpgradeSequenceCompleted += () => sequenceEvents++;
      gameplay.FirstLevelBuildCompleted += () => buildEvents++;
      var modules = new[]
      {
        FirstLevelModuleId.WiderField,
        FirstLevelModuleId.MoreTargets,
        FirstLevelModuleId.RestBloom,
        FirstLevelModuleId.DoublePulse,
      };

      for (var i = 0; i < modules.Length; i++)
      {
        PrepareTransaction(gameplay, new[] { modules[i] }, 0);
        Invoke(gameplay, "FinalizeModuleInstallation");
        Invoke(gameplay, "FinalizeModuleInstallation");

        Assert.That(gameplay.IsModuleUpgradeOpen, Is.False, "Upgrade remained open at choice " + (i + 1));
        Assert.That(gameplay.IsModuleInstallationPending, Is.False, "Install lock remained set at choice " + (i + 1));
        Assert.That(gameplay.InstalledFirstLevelModuleCount, Is.EqualTo(i + 1));
        Assert.That(buildEvents, Is.EqualTo(i == modules.Length - 1 ? 1 : 0));
      }

      Assert.That(moduleEvents, Is.EqualTo(4));
      Assert.That(choiceEvents, Is.EqualTo(4));
      Assert.That(sequenceEvents, Is.EqualTo(1));
      Assert.That(buildEvents, Is.EqualTo(1));
      Assert.That(gameplay.IsFirstLevelBuildComplete, Is.True);
      Assert.That(gameplay.IsFirstLevelUpgradeSequenceComplete, Is.True);
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

      Assert.That(gameplay.IsTutorialModeEnabled, Is.True);
      Assert.That(gameplay.IsTutorialRandomSpawningPaused, Is.True);
      Assert.That(gameplay.IsTutorialRandomCrisisPaused, Is.True);
      Assert.That(gameplay.IsTutorialSessionTimerPaused, Is.True);

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
      PrepareTransaction(gameplay, new[] { FirstLevelModuleId.ChainBlink }, 3);

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

      LogAssert.Expect(
        LogType.Warning,
        "KeepBlinking ignored an early build-complete signal at 0/4 modules.");
      Invoke(session, "HandleBuildCompleted");

      Assert.That(session.State, Is.EqualTo(FirstLevelSessionState.Gameplay));
    }

    [Test]
    public void FailingObserverCannotInterruptModuleCompletion()
    {
      var gameplay = CreateGameplay();
      var choiceEvents = 0;
      gameplay.FirstLevelModuleInstalled += _ => throw new InvalidOperationException("observer failure");
      gameplay.ModuleChoiceCompleted += _ => choiceEvents++;
      PrepareTransaction(gameplay, new[] { FirstLevelModuleId.WiderField }, 0);

      LogAssert.Expect(
        LogType.Error,
        "KeepBlinking gameplay signal subscriber failed: FirstLevelModuleInstalled.");
      LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: observer failure"));
      Invoke(gameplay, "FinalizeModuleInstallation");

      Assert.That(choiceEvents, Is.EqualTo(1));
      Assert.That(gameplay.IsModuleInstallationPending, Is.False);
      Assert.That(gameplay.IsModuleUpgradeOpen, Is.False);
    }

    [Test]
    public void EveryFourChoicePoolBranchHasThreeUniqueLegalCards()
    {
      var catalogType = typeof(FirstLevelModuleId).Assembly.GetType("KeepBlinking.Gameplay.FirstLevelUpgradeCatalog");
      Assert.That(catalogType, Is.Not.Null);
      var buildOffer = catalogType.GetMethod("BuildOffer", StaticPrivate);
      Assert.That(buildOffer, Is.Not.Null);

      AuditOfferBranch(buildOffer, 1, new HashSet<FirstLevelModuleId>());
    }

    [Test]
    public void EveryFinalCareCardIsReachableInTheFourChoiceTree()
    {
      var catalogType = typeof(FirstLevelModuleId).Assembly.GetType("KeepBlinking.Gameplay.FirstLevelUpgradeCatalog");
      var buildOffer = catalogType.GetMethod("BuildOffer", StaticPrivate);
      var seen = new HashSet<FirstLevelModuleId>();
      CollectReachableCards(buildOffer, 1, new HashSet<FirstLevelModuleId>(), seen);

      for (var raw = (int)FirstLevelModuleId.WiderField; raw <= (int)FirstLevelModuleId.FullRecovery; raw++)
      {
        if ((FirstLevelModuleId)raw == FirstLevelModuleId.ShiftReward) continue;
        Assert.That(seen.Contains((FirstLevelModuleId)raw), Is.True, "CARE card is unreachable: " + (FirstLevelModuleId)raw);
      }
    }

    [Test]
    public void EveryCareCardPassesHealthInvariantAudit()
    {
      var catalogType = typeof(FirstLevelModuleId).Assembly.GetType("KeepBlinking.Gameplay.FirstLevelUpgradeCatalog");
      var definitionsProperty = catalogType.GetProperty("Definitions", StaticPrivate);
      var definitions = (Array)definitionsProperty.GetValue(null);

      Assert.That(definitions.Length, Is.EqualTo(14));
      foreach (var definition in definitions)
      {
        var type = definition.GetType();
        Assert.That((bool)type.GetMethod("PassesHealthInvariantAudit", InstancePrivate | BindingFlags.Public).Invoke(definition, null), Is.True);
        Assert.That(((string)type.GetProperty("Title").GetValue(definition)).Split(' '), Has.Length.LessThanOrEqualTo(2));
        Assert.That(((string)type.GetProperty("CategoryLabel").GetValue(definition)).Split(' '), Has.Length.EqualTo(1));
        Assert.That(((string)type.GetProperty("Description").GetValue(definition)).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries), Has.Length.LessThanOrEqualTo(5));
      }
    }

    [Test]
    public void ScreenDownRestRejectsNormalPortraitOrientation()
    {
      var normal = Vector3.forward;
      Assert.That(ScreenDownRestMotionLogic.IsScreenDown(normal, normal, Vector3.down, 150f, 30f), Is.False);
    }

    [Test]
    public void ScreenDownRestAcceptsRelativeFlipOrGroundFacingNormal()
    {
      Assert.That(ScreenDownRestMotionLogic.IsScreenDown(Vector3.forward, Vector3.back, Vector3.down, 150f, 30f), Is.True);
      Assert.That(ScreenDownRestMotionLogic.IsScreenDown(Vector3.forward, Vector3.down, Vector3.down, 150f, 30f), Is.True);
    }

    [Test]
    public void ScreenDownRestRequiresStableMotion()
    {
      Assert.That(ScreenDownRestMotionLogic.IsStable(1f, 1f, 0.18f, 0.2f, 0.35f), Is.True);
      Assert.That(ScreenDownRestMotionLogic.IsStable(1.4f, 1f, 0.18f, 0.2f, 0.35f), Is.False);
      Assert.That(ScreenDownRestMotionLogic.IsStable(1f, 1f, 0.18f, 0.8f, 0.35f), Is.False);
    }

    [Test]
    public void ScreenDownRestRequiresReturnNearCapturedAttitude()
    {
      Assert.That(ScreenDownRestMotionLogic.IsReturned(Quaternion.identity, Quaternion.Euler(0f, 18f, 0f), 20f), Is.True);
      Assert.That(ScreenDownRestMotionLogic.IsReturned(Quaternion.identity, Quaternion.Euler(0f, 24f, 0f), 20f), Is.False);
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
      var method = typeof(EdgeOrbitHarvestMvp).GetMethod(
        "CalculateAdaptiveSoftBlinkReopenThreshold",
        InstancePrivate);

      Assert.That(method, Is.Not.Null);
      Assert.That((float)method.Invoke(gameplay, new object[] { 0.80f }), Is.EqualTo(0.496f).Within(0.001f));
      Assert.That((float)method.Invoke(gameplay, new object[] { 0.35f }), Is.EqualTo(0.30f).Within(0.001f));
    }

    [Test]
    public void BlinkBloomCompletedBlinkStartsVisibleSixSecondExpansion()
    {
      var gameplay = CreateGameplay();
      SetField(gameplay, "_autoReadKeepBlinkingEyeInput", false);
      SetField(gameplay, "_tutorialMode", false);
      SetEnumField(gameplay, "_gameplayState", "Orbiting");
      ((List<FirstLevelModuleId>)GetField(gameplay, "_installedModuleOrder")).Add(FirstLevelModuleId.BlinkBloom);
      ((HashSet<FirstLevelModuleId>)GetField(gameplay, "_installedModules")).Add(FirstLevelModuleId.BlinkBloom);

      var fieldOwner = new GameObject("Blink Bloom Field Test");
      _objectsToDestroy.Add(fieldOwner);
      var field = fieldOwner.AddComponent<SoftFocusFieldController>();
      SoftFocusFieldController.EnsureExists(gameplay);

      var upgradeOwner = new GameObject("Blink Bloom Upgrade Test");
      _objectsToDestroy.Add(upgradeOwner);
      var upgrades = upgradeOwner.AddComponent<CareUpgradeController>();
      CareUpgradeController.EnsureExists(gameplay);
      Invoke(upgrades, "HandleNaturalBlink", 1);

      Assert.That(field.IsTemporaryExpansionActive, Is.True);
      Assert.That(field.TemporaryExpansionTargetScale, Is.EqualTo(1.35f).Within(0.001f));
      Assert.That(field.TemporaryExpansionRemainingSeconds, Is.GreaterThan(5.8f));
      var canvas = field.GetComponentInChildren<Canvas>(true);
      Assert.That(canvas, Is.Not.Null);
      Assert.That(canvas.sortingOrder, Is.EqualTo(60));
    }

    private static void AuditOfferBranch(
      MethodInfo buildOffer,
      int upgradeNumber,
      HashSet<FirstLevelModuleId> installed)
    {
      var offer = (List<FirstLevelModuleId>)buildOffer.Invoke(null, new object[] { upgradeNumber, installed });
      Assert.That(offer, Has.Count.EqualTo(3), "Offer count failed at upgrade " + upgradeNumber);
      Assert.That(new HashSet<FirstLevelModuleId>(offer), Has.Count.EqualTo(3));
      var catalogType = typeof(FirstLevelModuleId).Assembly.GetType("KeepBlinking.Gameplay.FirstLevelUpgradeCatalog");
      var getDefinition = catalogType.GetMethod("Get", StaticPrivate);
      var categories = new HashSet<object>();
      for (var i = 0; i < offer.Count; i++)
      {
        Assert.That(offer[i], Is.Not.EqualTo(FirstLevelModuleId.None));
        Assert.That(installed.Contains(offer[i]), Is.False, "Installed module was offered again: " + offer[i]);
        Assert.That((int)offer[i], Is.GreaterThanOrEqualTo((int)FirstLevelModuleId.WiderField), "Legacy combat card entered CARE pool: " + offer[i]);
        var definition = getDefinition.Invoke(null, new object[] { offer[i] });
        categories.Add(definition.GetType().GetProperty("Category").GetValue(definition));
      }
      Assert.That(categories.Count, Is.GreaterThanOrEqualTo(2), "Offer must contain at least two CARE categories.");

      if (upgradeNumber >= 4)
      {
        return;
      }

      for (var i = 0; i < offer.Count; i++)
      {
        var nextInstalled = new HashSet<FirstLevelModuleId>(installed) { offer[i] };
        AuditOfferBranch(buildOffer, upgradeNumber + 1, nextInstalled);
      }
    }

    private static void CollectReachableCards(
      MethodInfo buildOffer,
      int upgradeNumber,
      HashSet<FirstLevelModuleId> installed,
      HashSet<FirstLevelModuleId> seen)
    {
      var offer = (List<FirstLevelModuleId>)buildOffer.Invoke(null, new object[] { upgradeNumber, installed });
      for (var i = 0; i < offer.Count; i++) seen.Add(offer[i]);
      if (upgradeNumber >= 4) return;
      for (var i = 0; i < offer.Count; i++)
      {
        var nextInstalled = new HashSet<FirstLevelModuleId>(installed) { offer[i] };
        CollectReachableCards(buildOffer, upgradeNumber + 1, nextInstalled, seen);
      }
    }

    private EdgeOrbitHarvestMvp CreateGameplay()
    {
      var root = new GameObject("First Level Upgrade Flow Test");
      _objectsToDestroy.Add(root);
      return root.AddComponent<EdgeOrbitHarvestMvp>();
    }

    private static void PrepareTransaction(
      EdgeOrbitHarvestMvp gameplay,
      IReadOnlyList<FirstLevelModuleId> offer,
      int selectedIndex)
    {
      var currentOffer = (List<FirstLevelModuleId>)GetField(gameplay, "_currentModuleOffer");
      currentOffer.Clear();
      for (var i = 0; i < offer.Count; i++)
      {
        currentOffer.Add(offer[i]);
      }

      SetEnumField(gameplay, "_gameplayState", "ModuleUpgrade");
      SetEnumField(gameplay, "_resumeStateAfterUpgrade", "Orbiting");
      SetField(gameplay, "_moduleChoicePending", true);
      SetField(gameplay, "_selectedModuleCardIndex", selectedIndex);
      SetField(gameplay, "_moduleInstallStartedAt", Time.unscaledTime);
      SetField(gameplay, "_currentUpgradeSampleRequirement", 10);
    }

    private static object GetField(object target, string fieldName)
    {
      var field = target.GetType().GetField(fieldName, InstancePrivate);
      Assert.That(field, Is.Not.Null, "Missing test field: " + fieldName);
      return field.GetValue(target);
    }

    private static void SetField(object target, string fieldName, object value)
    {
      var field = target.GetType().GetField(fieldName, InstancePrivate);
      Assert.That(field, Is.Not.Null, "Missing test field: " + fieldName);
      field.SetValue(target, value);
    }

    private static void SetEnumField(object target, string fieldName, string value)
    {
      var field = target.GetType().GetField(fieldName, InstancePrivate);
      Assert.That(field, Is.Not.Null, "Missing test enum field: " + fieldName);
      field.SetValue(target, Enum.Parse(field.FieldType, value));
    }

    private static void Invoke(object target, string methodName)
    {
      var method = target.GetType().GetMethod(methodName, InstancePrivate);
      Assert.That(method, Is.Not.Null, "Missing test method: " + methodName);
      method.Invoke(target, null);
    }

    private static void Invoke(object target, string methodName, object argument)
    {
      var method = target.GetType().GetMethod(methodName, InstancePrivate);
      Assert.That(method, Is.Not.Null, "Missing test method: " + methodName);
      method.Invoke(target, new[] { argument });
    }
  }
}
