using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace KeepBlinking.Editor
{
  [InitializeOnLoad]
  public static class KeepBlinkingGameViewSetup
  {
    private const int IPhonePortraitWidth = 1170;
    private const int IPhonePortraitHeight = 2532;
    private const string IPhonePortraitName = "iPhone 13 Pro Portrait (1170:2532)";

    static KeepBlinkingGameViewSetup()
    {
      EditorApplication.delayCall += UseIPhonePortraitGameView;
    }

    [MenuItem("KeepBlinking/Game View/Use iPhone Portrait")]
    public static void UseIPhonePortraitGameView()
    {
      try
      {
        var activeGroupName = EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS ? "iOS" : "Standalone";
        var selectedIndex = EnsureIPhonePortraitSize(activeGroupName);
        EnsureIPhonePortraitSize("iOS");

        if (selectedIndex >= 0)
        {
          SelectGameViewSize(selectedIndex);
        }
      }
      catch (Exception ex)
      {
        Debug.LogWarning($"KeepBlinking could not set the Game view to iPhone portrait: {ex.Message}");
      }
    }

    private static int EnsureIPhonePortraitSize(string groupName)
    {
      var editorAssembly = typeof(EditorWindow).Assembly;
      var group = GetGameViewSizeGroup(editorAssembly, groupName);
      if (group == null)
      {
        return -1;
      }

      var existingIndex = FindIPhonePortraitSizeIndex(group);
      if (existingIndex >= 0)
      {
        return existingIndex;
      }

      AddIPhonePortraitSize(editorAssembly, group);
      return FindIPhonePortraitSizeIndex(group);
    }

    private static object GetGameViewSizeGroup(Assembly editorAssembly, string groupName)
    {
      var sizesType = editorAssembly.GetType("UnityEditor.GameViewSizes");
      var groupType = editorAssembly.GetType("UnityEditor.GameViewSizeGroupType");
      if (sizesType == null || groupType == null || !Enum.IsDefined(groupType, groupName))
      {
        return null;
      }

      var singletonType = typeof(ScriptableSingleton<>).MakeGenericType(sizesType);
      var instanceProperty = singletonType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
      var sizesInstance = instanceProperty?.GetValue(null, null);
      var getGroupMethod = sizesType.GetMethod("GetGroup", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      var groupValue = Enum.Parse(groupType, groupName);
      return getGroupMethod?.Invoke(sizesInstance, new[] { groupValue });
    }

    private static int FindIPhonePortraitSizeIndex(object group)
    {
      var groupType = group.GetType();
      var getTotalCountMethod = groupType.GetMethod("GetTotalCount", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      var getGameViewSizeMethod = groupType.GetMethod("GetGameViewSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      if (getTotalCountMethod == null || getGameViewSizeMethod == null)
      {
        return -1;
      }

      var count = (int)getTotalCountMethod.Invoke(group, null);
      for (var i = 0; i < count; i++)
      {
        var size = getGameViewSizeMethod.Invoke(group, new object[] { i });
        var width = GetIntMember(size, "width");
        var height = GetIntMember(size, "height");
        var label = GetStringMember(size, "displayText");
        if ((width == IPhonePortraitWidth && height == IPhonePortraitHeight) ||
            string.Equals(label, IPhonePortraitName, StringComparison.Ordinal))
        {
          return i;
        }
      }

      return -1;
    }

    private static void AddIPhonePortraitSize(Assembly editorAssembly, object group)
    {
      var gameViewSizeType = editorAssembly.GetType("UnityEditor.GameViewSize");
      var gameViewSizeKindType = editorAssembly.GetType("UnityEditor.GameViewSizeType");
      var addCustomSizeMethod = group.GetType().GetMethod("AddCustomSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      if (gameViewSizeType == null || gameViewSizeKindType == null || addCustomSizeMethod == null)
      {
        return;
      }

      var aspectRatioKind = Enum.Parse(gameViewSizeKindType, "AspectRatio");
      var constructor = gameViewSizeType.GetConstructor(
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
        null,
        new[] { gameViewSizeKindType, typeof(int), typeof(int), typeof(string) },
        null);
      var size = constructor?.Invoke(new[] { aspectRatioKind, IPhonePortraitWidth, IPhonePortraitHeight, IPhonePortraitName });
      if (size != null)
      {
        addCustomSizeMethod.Invoke(group, new[] { size });
      }
    }

    private static void SelectGameViewSize(int index)
    {
      var editorAssembly = typeof(EditorWindow).Assembly;
      var gameViewType = editorAssembly.GetType("UnityEditor.GameView");
      if (gameViewType == null)
      {
        return;
      }

      var gameViewWindow = EditorWindow.GetWindow(gameViewType);
      var selectedSizeProperty = gameViewType.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      selectedSizeProperty?.SetValue(gameViewWindow, index, null);
      gameViewWindow.Repaint();
    }

    private static int GetIntMember(object target, string name)
    {
      var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
      var type = target.GetType();
      var property = type.GetProperty(name, flags);
      if (property != null)
      {
        return (int)property.GetValue(target, null);
      }

      var field = type.GetField(name, flags);
      return field == null ? -1 : (int)field.GetValue(target);
    }

    private static string GetStringMember(object target, string name)
    {
      var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
      var type = target.GetType();
      var property = type.GetProperty(name, flags);
      if (property != null)
      {
        return property.GetValue(target, null) as string;
      }

      var field = type.GetField(name, flags);
      return field?.GetValue(target) as string;
    }
  }
}
