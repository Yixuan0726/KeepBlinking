using System.Collections;
using System.IO;
using System.Linq;
using KeepBlinking.CareStation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KeepBlinking.Tests
{
  public sealed class FilterLevelOneScreenshotPlayModeTests
  {
    private static readonly string OutputFolder = Path.GetFullPath(
      Path.Combine(Application.dataPath, "..", "Artifacts", "FilterL1"));

    [UnityTest]
    public IEnumerator CaptureStationStartHalfAndCompleteGameViews()
    {
      var root = new GameObject("FILTER L1 Game View Capture");
      try
      {
        var save = new CareStationSaveData
        {
          stationLevel = 1,
          storageLevel = 2,
          cartLevel = 2,
          workerLevel = 1,
          crewCount = 1,
          storageHours = 36,
        };
        var view = root.AddComponent<CareStationView>();
        view.Build();
        view.ApplyStation(save);
        view.ShowStationWorking();
        yield return null;

        var filter = root.GetComponentInChildren<CareStationFilterArtView>(true);
        Assert.That(filter, Is.Not.Null);
        filter.SetLevel(1, false);
        // Freeze the representative logistics loop so it cannot overwrite the
        // three presentation-only states selected for these Game View frames.
        view.enabled = false;
        var representativeBottle = root.GetComponentsInChildren<RectTransform>(true)
          .First(transform => transform.name == "Representative Production Bottle");
        representativeBottle.gameObject.SetActive(false);

        Directory.CreateDirectory(OutputFolder);
        yield return Capture(filter, FilterProductionVisualState.Filtering, 0.05f,
          Path.Combine(OutputFolder, "FilterL1_Station_Start.png"));
        yield return Capture(filter, FilterProductionVisualState.Filtering, 0.5f,
          Path.Combine(OutputFolder, "FilterL1_Station_Half.png"));
        yield return Capture(filter, FilterProductionVisualState.BottleComplete, 1f,
          Path.Combine(OutputFolder, "FilterL1_Station_Complete.png"));

        var files = Directory.GetFiles(OutputFolder, "FilterL1_Station_*.png")
          .Where(File.Exists)
          .ToArray();
        Assert.That(files, Has.Length.EqualTo(3));
        Assert.That(files.All(path => new FileInfo(path).Length > 0), Is.True);
      }
      finally
      {
        Object.Destroy(root);
      }
    }

    private static IEnumerator Capture(
      CareStationFilterArtView filter,
      FilterProductionVisualState state,
      float progress,
      string path)
    {
      filter.SetProductionVisual(state, progress);
      yield return new WaitForEndOfFrame();
      if (File.Exists(path)) File.Delete(path);
      ScreenCapture.CaptureScreenshot(path, 1);

      var deadline = Time.realtimeSinceStartup + 3f;
      while ((!File.Exists(path) || new FileInfo(path).Length == 0) &&
             Time.realtimeSinceStartup < deadline)
        yield return null;
      Assert.That(File.Exists(path), Is.True, path);
      Assert.That(new FileInfo(path).Length, Is.GreaterThan(0), path);
    }
  }
}
