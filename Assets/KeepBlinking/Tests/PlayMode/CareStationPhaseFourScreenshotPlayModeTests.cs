using System.Collections;
using System.IO;
using KeepBlinking.CareStation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KeepBlinking.Tests.PlayMode
{
  public sealed class CareStationPhaseFourScreenshotPlayModeTests
  {
    private static readonly string OutputFolder = Path.GetFullPath(
      Path.Combine(Application.dataPath, "..", "Artifacts", "StationPhase4"));

    [UnityTest]
    public IEnumerator CaptureIdleProducingAndStorageWaitingGameViews()
    {
      var previousWidth = Screen.width;
      var previousHeight = Screen.height;
      var root = new GameObject("Station Phase 4 Game View Capture");
      try
      {
        Screen.SetResolution(368, 797, false);
        yield return null;

        var view = root.AddComponent<CareStationView>();
        view.Build();
        var save = new CareStationSaveData
        {
          stationLevel = 1,
          storageLevel = 1,
          cartLevel = 1,
          workerLevel = 1,
          storageHours = 24,
          storedFullBottles = 7,
          careEnergy = 18,
          coins = 14,
          productionTransportMode = CareProductionTransportMode.ManualCarry,
        };
        view.ApplyStation(save);
        view.ShowStationWorking();
        yield return SettleUi();

        Directory.CreateDirectory(OutputFolder);
        yield return Capture(Path.Combine(OutputFolder, "StationPhase4_Idle.png"));

        view.ShowProductionStage(CareProductionStage.PackerLabeling, 0.55f, save);
        yield return SettleUi();
        yield return Capture(Path.Combine(OutputFolder, "StationPhase4_Producing.png"));

        save.storedFullBottles = save.storageHours;
        save.offlineProductionPausedByFullStorage = true;
        view.ApplyStation(save);
        view.ShowProductionStage(CareProductionStage.WaitingForStorage, 1f, save);
        yield return SettleUi();
        yield return Capture(Path.Combine(OutputFolder, "StationPhase4_StorageFull.png"));

        foreach (var fileName in new[]
                 {
                   "StationPhase4_Idle.png",
                   "StationPhase4_Producing.png",
                   "StationPhase4_StorageFull.png",
                 })
        {
          var path = Path.Combine(OutputFolder, fileName);
          Assert.That(File.Exists(path), Is.True, path);
          Assert.That(new FileInfo(path).Length, Is.GreaterThan(0), path);
          var texture = new Texture2D(2, 2);
          try
          {
            Assert.That(texture.LoadImage(File.ReadAllBytes(path)), Is.True, path);
            Assert.That(texture.width, Is.EqualTo(368), path);
            Assert.That(texture.height, Is.EqualTo(797), path);
          }
          finally
          {
            Object.DestroyImmediate(texture);
          }
        }
        Assert.That(root.GetComponentsInChildren<CareStationWorkerArtView>(true), Is.Empty);
      }
      finally
      {
        Screen.SetResolution(previousWidth, previousHeight, false);
        if (root != null) Object.DestroyImmediate(root);
      }
    }

    private static IEnumerator Capture(string path)
    {
      if (File.Exists(path)) File.Delete(path);
      yield return new WaitForEndOfFrame();
      ScreenCapture.CaptureScreenshot(path, 1);
      var deadline = Time.realtimeSinceStartup + 5f;
      while ((!File.Exists(path) || new FileInfo(path).Length == 0) &&
             Time.realtimeSinceStartup < deadline)
        yield return null;
      Assert.That(File.Exists(path), Is.True, path);
      Assert.That(new FileInfo(path).Length, Is.GreaterThan(0), path);
    }

    private static IEnumerator SettleUi()
    {
      Canvas.ForceUpdateCanvases();
      yield return null;
      Canvas.ForceUpdateCanvases();
      yield return null;
    }
  }
}
