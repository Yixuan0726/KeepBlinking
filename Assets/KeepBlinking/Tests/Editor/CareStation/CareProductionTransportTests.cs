using System;
using System.IO;
using KeepBlinking.CareStation;
using NUnit.Framework;
using UnityEngine;

namespace KeepBlinking.Tests
{
  public sealed class CareProductionTransportTests
  {
    private string _directory;
    private string _path;

    [SetUp]
    public void SetUp()
    {
      _directory = Path.Combine(Path.GetTempPath(), "KeepBlinkingTransportV23", Guid.NewGuid().ToString("N"));
      _path = Path.Combine(_directory, "care_station.json");
      Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
      if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Test]
    public void LevelOneAlwaysUsesManualCarryWithoutUnlockPresentation()
    {
      var save = new CareStationSaveData { stationLevel = 1 };

      CareProductionTransportRules.Synchronize(save);

      Assert.That(save.productionTransportMode, Is.EqualTo(CareProductionTransportMode.ManualCarry));
      Assert.That(CareProductionTransportRules.TryConsumeBasicConveyorUnlock(save), Is.False);
      Assert.That(save.basicConveyorUnlockPresented, Is.False);
    }

    [Test]
    public void LevelTwoMilestoneUnlocksBasicConveyorExactlyOnce()
    {
      var save = new CareStationSaveData { stationLevel = 2 };

      Assert.That(CareProductionTransportRules.TryConsumeBasicConveyorUnlock(save), Is.True);
      Assert.That(save.productionTransportMode, Is.EqualTo(CareProductionTransportMode.BasicConveyor));
      Assert.That(save.basicConveyorUnlockPresented, Is.True);
      Assert.That(CareProductionTransportRules.TryConsumeBasicConveyorUnlock(save), Is.False);
    }

    [Test]
    public void BasicConveyorAndOneShotFlagSurviveReload()
    {
      var now = new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc);
      var service = new CareStationSaveService(_path);
      var save = new CareStationSaveData { stationLevel = 2 };
      Assert.That(CareProductionTransportRules.TryConsumeBasicConveyorUnlock(save), Is.True);

      service.Save(save, now);
      var restored = service.Load(now.AddMinutes(1));

      Assert.That(restored.productionTransportMode, Is.EqualTo(CareProductionTransportMode.BasicConveyor));
      Assert.That(restored.basicConveyorUnlockPresented, Is.True);
      Assert.That(CareProductionTransportRules.TryConsumeBasicConveyorUnlock(restored), Is.False);
    }

    [TestCase(1, CareProductionTransportMode.ManualCarry, false)]
    [TestCase(2, CareProductionTransportMode.BasicConveyor, true)]
    public void VersionTwentyTwoSaveMigratesWithoutFalseOrRepeatedUnlock(
      int stationLevel,
      CareProductionTransportMode expectedMode,
      bool expectedPresented)
    {
      var now = new DateTime(2026, 8, 31, 9, 0, 0, DateTimeKind.Utc);
      var legacy = new CareStationSaveData { saveVersion = 22, stationLevel = stationLevel };
      File.WriteAllText(_path, JsonUtility.ToJson(legacy, true));

      var restored = new CareStationSaveService(_path).Load(now);

      Assert.That(restored.saveVersion, Is.EqualTo(CareStationSaveService.CurrentVersion));
      Assert.That(restored.productionTransportMode, Is.EqualTo(expectedMode));
      Assert.That(restored.basicConveyorUnlockPresented, Is.EqualTo(expectedPresented));
      Assert.That(CareProductionTransportRules.TryConsumeBasicConveyorUnlock(restored), Is.False);
    }

    [Test]
    public void LevelOneCannotKeepAnInvalidConveyorFlagFromAStaleSave()
    {
      var save = new CareStationSaveData
      {
        stationLevel = 1,
        productionTransportMode = CareProductionTransportMode.BasicConveyor,
        basicConveyorUnlockPresented = true,
      };

      CareProductionTransportRules.Synchronize(save);

      Assert.That(save.productionTransportMode, Is.EqualTo(CareProductionTransportMode.ManualCarry));
    }

    [Test]
    public void StorageFullStopsReservedBottleAtWaitingPositionWithoutLosingEnergy()
    {
      var save = new CareStationSaveData
      {
        stationLevel = 1,
        storageHours = 1,
        storedFullBottles = 1,
        careEnergy = 7,
        productionStage = CareProductionStage.TransferToStorage,
        productionStageElapsedSeconds = 99f,
        productionCycleEnergyConsumed = true,
        productionCycleStored = false,
      };

      var result = CareProductionRules.AdvanceForegroundCycle(save, 1f, new CareProductionConfiguration());

      Assert.That(result.WaitingForStorage, Is.True);
      Assert.That(save.productionStage, Is.EqualTo(CareProductionStage.WaitingForStorage));
      Assert.That(save.productionCycleStored, Is.False);
      Assert.That(save.careEnergy, Is.EqualTo(7));
      Assert.That(save.storedFullBottles, Is.EqualTo(1));
    }
  }
}
