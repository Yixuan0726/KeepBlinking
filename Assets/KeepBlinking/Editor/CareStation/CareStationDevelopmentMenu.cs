#if UNITY_EDITOR
using System;
using KeepBlinking.CareStation;
using UnityEditor;
using UnityEngine;

namespace KeepBlinking.EditorTools
{
  internal static class CareStationDevelopmentMenu
  {
    private const string Root = "KeepBlinking/Care Station/";

    [MenuItem(Root + "Simulate 30 Minutes")]
    private static void Simulate30Minutes() => Simulate(TimeSpan.FromMinutes(30));

    [MenuItem(Root + "Simulate 4 Hours")]
    private static void Simulate4Hours() => Simulate(TimeSpan.FromHours(4));

    [MenuItem(Root + "Simulate 12 Hours")]
    private static void Simulate12Hours() => Simulate(TimeSpan.FromHours(12));

    [MenuItem(Root + "Simulate 24 Hours")]
    private static void Simulate24Hours() => Simulate(TimeSpan.FromHours(24));

    [MenuItem(Root + "Clear Care Station Save")]
    private static void ClearSave()
    {
      if (CareStationController.Instance != null) CareStationController.Instance.ClearStationSave();
      else new CareStationSaveService().Delete();
      Debug.Log("Care Station development save cleared.");
    }

    [MenuItem(Root + "Jump To Shift 1")]
    private static void Jump1() => Jump(1);

    [MenuItem(Root + "Jump To Shift 2")]
    private static void Jump2() => Jump(2);

    [MenuItem(Root + "Jump To Shift 3")]
    private static void Jump3() => Jump(3);

    private static void Simulate(TimeSpan duration)
    {
      if (CareStationController.Instance == null)
      {
        Debug.LogWarning("Enter Play Mode before using Care Station simulation tools.");
        return;
      }
      CareStationController.Instance.SimulateOffline(duration);
    }

    private static void Jump(int shift)
    {
      if (CareStationController.Instance == null)
      {
        Debug.LogWarning("Enter Play Mode before jumping to a Care Shift.");
        return;
      }
      CareStationController.Instance.JumpToShift(shift);
    }
  }
}
#endif
