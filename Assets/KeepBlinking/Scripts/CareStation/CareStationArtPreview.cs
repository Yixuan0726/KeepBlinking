#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using KeepBlinking.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KeepBlinking.CareStation
{
  internal sealed class CareStationArtPreview : MonoBehaviour
  {
    private RectTransform _root;
    private RectTransform _stage;
    private TextMeshProUGUI _summary;
    private CareCrewArtView _activeView;
    private CareCrewRole _role;
    private CareCrewAnimationState _state;
    private CareCartTier _cart;
    private CareCartLoadPreview _load;
    internal event Action CloseRequested;

    internal bool IsOpen => _root != null && _root.gameObject.activeSelf;

    internal void Open()
    {
      if (_root == null) Build();
      _root.gameObject.SetActive(true);
      RebuildPreview();
    }

    internal void Close()
    {
      if (_root != null) _root.gameObject.SetActive(false);
    }

    private void Build()
    {
      var safe = FirstLevelUiFactory.CreateCanvas(transform, "Care Station Art Preview", 950, out _, out _);
      _root = FirstLevelUiFactory.CreateObject("Art Preview Root", safe).GetComponent<RectTransform>();
      FirstLevelUiFactory.Stretch(_root);
      var background = FirstLevelUiFactory.CreateImage("Background", _root, KeepBlinkingTheme.BackgroundPrimary);
      FirstLevelUiFactory.Stretch(background.rectTransform);

      var title = FirstLevelUiFactory.CreateText("Title", _root, "CARE STATION ART PREVIEW", 29f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextPrimary);
      FirstLevelUiFactory.SetRect(title.rectTransform, new Vector2(0.05f, 0.93f), new Vector2(0.95f, 0.985f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      _summary = FirstLevelUiFactory.CreateText("Summary", _root, string.Empty, 21f, FontStyles.Bold, TextAlignmentOptions.Center, KeepBlinkingTheme.TextSecondary, true);
      FirstLevelUiFactory.SetRect(_summary.rectTransform, new Vector2(0.05f, 0.86f), new Vector2(0.95f, 0.93f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

      _stage = FirstLevelUiFactory.CreateObject("Preview Stage", _root).GetComponent<RectTransform>();
      FirstLevelUiFactory.SetRect(_stage, new Vector2(0.08f, 0.34f), new Vector2(0.92f, 0.85f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var stageBg = FirstLevelUiFactory.CreateImage("Stage Background", _stage, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.SurfaceBase, 0.62f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.Stretch(stageBg.rectTransform);
      var ground = FirstLevelUiFactory.CreateImage("Ground", _stage, KeepBlinkingTheme.WithAlpha(KeepBlinkingTheme.AccentPrimary, 0.24f), FirstLevelUiFactory.RoundedSprite);
      FirstLevelUiFactory.SetRect(ground.rectTransform, new Vector2(0.16f, 0.16f), new Vector2(0.84f, 0.16f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(0f, 12f));

      BuildChoiceRow("CREW", 0.275f, new[] { "DUST", "MENDER", "COURIER", "REST" }, index => { _role = (CareCrewRole)index; RebuildPreview(); });
      BuildChoiceRow("STATE", 0.195f, new[] { "IDLE", "WALK", "WORK", "REST", "CHEER" }, index => { _state = (CareCrewAnimationState)index; ApplyState(); });
      BuildChoiceRow("CART", 0.115f, new[] { "SMALL", "MEDIUM", "LARGE" }, index => { _cart = (CareCartTier)index; ApplyCart(); });
      BuildChoiceRow("LOAD", 0.035f, new[] { "EMPTY", "PARTIAL", "FULL", "GOLD", "MIXED" }, index => { _load = (CareCartLoadPreview)index; ApplyLoad(); });

      var close = FirstLevelUiFactory.CreateButton("Close Preview", _root, "CLOSE", KeepBlinkingTheme.AccentPrimary);
      FirstLevelUiFactory.SetRect((RectTransform)close.transform, new Vector2(0.75f, 0.935f), new Vector2(0.95f, 0.982f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      close.onClick.AddListener(() => CloseRequested?.Invoke());
      _root.gameObject.SetActive(false);
    }

    private void BuildChoiceRow(string label, float y, string[] choices, Action<int> select)
    {
      var rowLabel = FirstLevelUiFactory.CreateText(label, _root, label, 17f, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, KeepBlinkingTheme.TextMuted);
      FirstLevelUiFactory.SetRect(rowLabel.rectTransform, new Vector2(0.035f, y), new Vector2(0.17f, y + 0.055f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
      var width = 0.78f / choices.Length;
      for (var i = 0; i < choices.Length; i++)
      {
        var captured = i;
        var button = FirstLevelUiFactory.CreateButton(label + " " + choices[i], _root, choices[i], KeepBlinkingTheme.AccentPrimary);
        FirstLevelUiFactory.SetRect((RectTransform)button.transform, new Vector2(0.18f + i * width, y), new Vector2(0.18f + (i + 1) * width - 0.008f, y + 0.055f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        button.onClick.AddListener(() => select(captured));
      }
    }

    private void RebuildPreview()
    {
      if (_stage == null) return;
      if (_activeView != null) Destroy(_activeView.gameObject);
      var prefab = Resources.Load<GameObject>("CareStation/Crew/" + PrefabName(_role));
      if (prefab == null)
      {
        _summary.text = "ART PREFABS ARE IMPORTING";
        return;
      }
      var instance = Instantiate(prefab, _stage, false);
      instance.name = "Preview " + PrefabName(_role);
      var rect = instance.GetComponent<RectTransform>();
      rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
      rect.anchoredPosition = new Vector2(_role == CareCrewRole.CareCourier ? 90f : 0f, 12f);
      rect.sizeDelta = new Vector2(500f, 420f);
      rect.localScale = Vector3.one;
      _activeView = instance.GetComponent<CareCrewArtView>();
      ApplyState();
      ApplyCart(false);
      ApplyLoad();
    }

    private void ApplyState()
    {
      if (_activeView != null) _activeView.SetState(_state, true);
      UpdateSummary();
    }

    private void ApplyCart(bool animate = true)
    {
      if (_activeView != null) _activeView.SetCartTier(_cart, animate);
      UpdateSummary();
    }

    private void ApplyLoad()
    {
      if (_activeView != null) _activeView.SetLoad(_load);
      UpdateSummary();
    }

    private void UpdateSummary()
    {
      if (_summary == null) return;
      _summary.text = _role == CareCrewRole.CareCourier
        ? $"{Friendly(_role)}  ·  {_state.ToString().ToUpperInvariant()}  ·  {Friendly(_cart)}  ·  {Friendly(_load)}"
        : $"{Friendly(_role)}  ·  {_state.ToString().ToUpperInvariant()}";
    }

    private static string PrefabName(CareCrewRole role)
    {
      switch (role)
      {
        case CareCrewRole.DustKeeper: return "DustKeeper";
        case CareCrewRole.DrySpotMender: return "DrySpotMender";
        case CareCrewRole.CareCourier: return "CareCourier";
        default: return "RestGuide";
      }
    }

    private static string Friendly(Enum value)
    {
      return value.ToString().Replace("DrySpot", "Dry Spot ").Replace("Care", "Care ").Replace("PartialMint", "Partial Mint").Replace("FullMint", "Full Mint").Replace("OneGoldBottle", "One Gold").Replace("MixedMintAndGold", "Mixed").ToUpperInvariant();
    }
  }
}
#endif
