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
    public void MissingBossUpgradeConfigurationFallsBackToFive()
    {
      var gameplay = CreateGameplay();
      SetField(gameplay, "_upgradesRequiredBeforeBoss", 0);

      Assert.That(gameplay.UpgradesRequiredBeforeBoss, Is.EqualTo(5));
    }

    [TestCase(0, FirstLevelModuleId.ChainBlink)]
    [TestCase(1, FirstLevelModuleId.QuietWake)]
    [TestCase(2, FirstLevelModuleId.BonusSample)]
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
        new[] { FirstLevelModuleId.ChainBlink, FirstLevelModuleId.QuietWake, FirstLevelModuleId.BonusSample },
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
    public void FiveSequentialChoicesCompleteBuildExactlyOnce()
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
        FirstLevelModuleId.ChainBlink,
        FirstLevelModuleId.WideBlink,
        FirstLevelModuleId.WideChain,
        FirstLevelModuleId.QuietWake,
        FirstLevelModuleId.BonusSample,
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

      Assert.That(moduleEvents, Is.EqualTo(5));
      Assert.That(choiceEvents, Is.EqualTo(5));
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
        "KeepBlinking ignored an early build-complete signal at 0/5 modules.");
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
      PrepareTransaction(gameplay, new[] { FirstLevelModuleId.ChainBlink }, 0);

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
    public void EveryFiveChoicePoolBranchHasThreeUniqueLegalCards()
    {
      var catalogType = typeof(FirstLevelModuleId).Assembly.GetType("KeepBlinking.Gameplay.FirstLevelUpgradeCatalog");
      Assert.That(catalogType, Is.Not.Null);
      var buildOffer = catalogType.GetMethod("BuildOffer", StaticPrivate);
      Assert.That(buildOffer, Is.Not.Null);

      AuditOfferBranch(buildOffer, 1, new HashSet<FirstLevelModuleId>());
    }

    [Test]
    public void DryCoreUsesNewSoftBlinkWithoutAnyGazeLockState()
    {
      Assert.That(Enum.IsDefined(typeof(DryCoreBossState), "FocusWeakPoint"), Is.False);
      Assert.That(Enum.IsDefined(typeof(DryCoreBossPrompt), "FocusCore"), Is.False);

      var gameplay = CreateGameplay();
      SetField(gameplay, "_autoReadKeepBlinkingEyeInput", false);
      var boss = gameplay.gameObject.AddComponent<DryCoreBossController>();
      boss.Initialize(gameplay);
      SetField(boss, "_remainingCores", 3);
      Invoke(boss, "BeginSoftBlinkRound");
      SetField(boss, "_softBlinkArmed", true);
      SetField(boss, "_softBlinkSerialAtArm", 5);

      Invoke(boss, "HandleSoftBlinkPerformed", 5);
      Assert.That(boss.State, Is.EqualTo(DryCoreBossState.WaitSoftBlink));

      Invoke(boss, "HandleSoftBlinkPerformed", 6);
      Assert.That(boss.State, Is.EqualTo(DryCoreBossState.PromptClose));
      Assert.That(boss.RemainingCores, Is.EqualTo(3));
    }

    private static void AuditOfferBranch(
      MethodInfo buildOffer,
      int upgradeNumber,
      HashSet<FirstLevelModuleId> installed)
    {
      var offer = (List<FirstLevelModuleId>)buildOffer.Invoke(null, new object[] { upgradeNumber, installed });
      Assert.That(offer, Has.Count.EqualTo(3), "Offer count failed at upgrade " + upgradeNumber);
      Assert.That(new HashSet<FirstLevelModuleId>(offer), Has.Count.EqualTo(3));
      for (var i = 0; i < offer.Count; i++)
      {
        Assert.That(offer[i], Is.Not.EqualTo(FirstLevelModuleId.None));
        Assert.That(installed.Contains(offer[i]), Is.False, "Installed module was offered again: " + offer[i]);
      }

      if (upgradeNumber >= 5)
      {
        return;
      }

      for (var i = 0; i < offer.Count; i++)
      {
        var nextInstalled = new HashSet<FirstLevelModuleId>(installed) { offer[i] };
        AuditOfferBranch(buildOffer, upgradeNumber + 1, nextInstalled);
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
