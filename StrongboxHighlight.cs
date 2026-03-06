using ExileCore;
using ExileCore.PoEMemory;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.Elements;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared;
using ExileCore.Shared.Helpers;
using ImGuiNET;
using SharpDX;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Color = SharpDX.Color;

namespace StrongboxHighlight;

public class StrongboxHighlight : BaseSettingsPlugin<StrongboxHighlightSettings>
{
    private sealed class CachedRegex
    {
        public string Pattern { get; set; } = string.Empty;
        public Regex Regex { get; set; }
    }

    private readonly List<string> _excludedStrings = new List<string>() { "account-bound", "italic" };
    private Dictionary<StrongboxHighlightEntry, List<Element>> _highlightedLabels = new Dictionary<StrongboxHighlightEntry, List<Element>>();
    private readonly Dictionary<StrongboxHighlightEntry, CachedRegex> _regexCache = new Dictionary<StrongboxHighlightEntry, CachedRegex>();
    private readonly StringBuilder _modTextBuilder = new StringBuilder(256);
    private SyncTask<bool> _currentTask;
    private bool _enabledArea;
    
    public override bool Initialise()
    {
        if (!Settings.HighlightEntries.Any()) {
            Settings.HighlightEntries = new List<StrongboxHighlightEntry> {
                new StrongboxHighlightEntry() {
                    FrameColor = new Color(255, 0, 0, 255),
                    BoxColor = new Color(255, 0, 0, 100),
                    DrawFrame = true,
                    DrawBox = true,
                    Regex = "detonates nearby corpses"
                },
                new StrongboxHighlightEntry() { 
                    FrameColor = new Color(0, 255, 255, 255),
                    BoxColor = new Color(0, 255, 255, 100),
                    DrawFrame = true,
                    DrawBox = true,
                    Regex = "ice nova"
                }
            };
        }

        Settings.Reload.OnPressed += RebuildRegexCache;
        RebuildRegexCache();
        return true;
    }

    public override void AreaChange(AreaInstance area)
    {
        _enabledArea = !area.IsHideout && !area.IsTown && !area.IsPeaceful;
        if (_enabledArea) {
            _currentTask = Update();
        } else {
            _currentTask = null;
            _highlightedLabels.Clear();
        }
    }

    public override Job Tick()
    {
        if (_currentTask != null) {
            TaskUtils.RunOrRestart(ref _currentTask, () => null);
        }
        return null;
    }

    private async SyncTask<bool> Update()
    {
        while (_enabledArea) {
            RecomputeHighlights();
            await Task.Delay(50);
        }
        return false;
    }

    public override void Render() {
        foreach (var highlight in _highlightedLabels) {
            foreach (var label in highlight.Value) {
                var rect = label.GetClientRect();
                if (highlight.Key.DrawBox) {
                    Graphics.DrawBox(rect, highlight.Key.BoxColor, 0);
                }
                if (highlight.Key.DrawFrame) {
                    Graphics.DrawFrame(rect, highlight.Key.FrameColor, 5);
                }
            }
        }
    }

    private List<LabelOnGround> FindChests(int radius) {
        var foundChests = GameController.Game.IngameState.IngameUi.ItemsOnGroundLabelsVisible
            .Where(x => x is not null
                && x.ItemOnGround != null && x.ItemOnGround.Metadata != null
                && x.ItemOnGround.Metadata.Contains("Metadata/Chests/StrongBoxes") && !x.ItemOnGround.IsOpened && x.ItemOnGround.DistancePlayer <= radius)
            .ToList();
        return foundChests;
    }

    private void RecomputeHighlights() {
        _highlightedLabels.Clear();

        var chestLabels = FindChests(100);
        if (chestLabels.Count == 0) {
            return;
        }

        for (int i = 0; i < Settings.HighlightEntries.Count; i++) {
            var entry = Settings.HighlightEntries[i];
            if (!TryGetRegex(entry, out var regex)) {
                continue;
            }

            foreach (var chest in chestLabels) {
                if (ChestMatchesRegex(chest, regex, _modTextBuilder)) {
                    if (_highlightedLabels.TryGetValue(entry, out var labels)) {
                        labels.Add(chest.Label);
                    } else {
                        _highlightedLabels.Add(entry, new List<Element>() { chest.Label });
                    }
                }
            }
        }
    }

    private bool ChestMatchesRegex(LabelOnGround chest, Regex regex, StringBuilder modTextBuilder)
    {
        modTextBuilder.Clear();

        var label = chest?.Label;
        if (label?.Children == null || label.Children.Count < 1) {
            return false;
        }

        var firstRow = label.Children[0];
        if (firstRow?.Children == null || firstRow.Children.Count < 2) {
            return false;
        }

        var modRoot = firstRow.Children[1];
        if (modRoot?.Children == null || modRoot.Children.Count < 1) {
            return false;
        }

        var hasModText = false;
        foreach (var child in modRoot.Children) {
            var text = child?.Text;
            if (string.IsNullOrEmpty(text) || ContainsExcludedText(text)) {
                continue;
            }

            if (hasModText) {
                modTextBuilder.Append('\n');
            }
            modTextBuilder.Append(text.ToLower());
            hasModText = true;
        }

        return hasModText && regex.IsMatch(modTextBuilder.ToString());
    }

    private bool ContainsExcludedText(string text)
    {
        for (int i = 0; i < _excludedStrings.Count; i++) {
            if (text.Contains(_excludedStrings[i], StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    private void RebuildRegexCache()
    {
        _regexCache.Clear();
        for (int i = 0; i < Settings.HighlightEntries.Count; i++) {
            _ = TryGetRegex(Settings.HighlightEntries[i], out _);
        }
    }

    private bool TryGetRegex(StrongboxHighlightEntry entry, out Regex regex)
    {
        regex = null;
        if (entry == null || string.IsNullOrWhiteSpace(entry.Regex)) {
            return false;
        }

        if (_regexCache.TryGetValue(entry, out var cached)
            && string.Equals(cached.Pattern, entry.Regex, StringComparison.Ordinal)) {
            regex = cached.Regex;
            return regex != null;
        }

        try {
            regex = new Regex(entry.Regex, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            _regexCache[entry] = new CachedRegex {
                Pattern = entry.Regex,
                Regex = regex
            };
            return true;
        } catch (ArgumentException e) {
            _regexCache[entry] = new CachedRegex {
                Pattern = entry.Regex,
                Regex = null
            };
            DebugWindow.LogError($"Invalid regex '{entry.Regex}': {e.Message}");
            return false;
        }
    }

    public override void DrawSettings() {
        base.DrawSettings();

        if (ImGui.TreeNodeEx("Configure Highlights", ImGuiTreeNodeFlags.DefaultOpen)) {
            for (int i = 0; i < Settings.HighlightEntries.Count; i++) {
                var entry = Settings.HighlightEntries[i];
                ImGui.PushID(i);
                var regex = entry.Regex;
                var frameColor = entry.FrameColor.ToImguiVec4();
                var boxColor = entry.BoxColor.ToImguiVec4();
                var drawFrame = entry.DrawFrame;
                var drawBox = entry.DrawBox;
                ImGui.InputTextWithHint($"Regex##{i}", "Enter regex", ref regex, 2048);
                if (ImGui.Checkbox("Draw Frame", ref drawFrame)) {
                    entry.DrawFrame = drawFrame;
                }
                if (drawFrame) {
                    ImGui.SameLine();
                    if (ImGui.ColorButton($"framecolor##{i}", frameColor)) {
                        ImGui.SetNextWindowPos(ImGui.GetMousePos());
                        ImGui.OpenPopup($"edit_framecolor##{i}");
                    }
                    if (ImGui.BeginPopup($"edit_framecolor##{i}", ImGuiWindowFlags.AlwaysAutoResize)) {
                        ImGui.ColorPicker4($"##framecolor_picker{i}", ref frameColor, ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreview);
                        ImGui.EndPopup();
                    }
                }
                if (ImGui.Checkbox("Draw Box", ref drawBox)) {
                    entry.DrawBox = drawBox;
                }
                if (drawBox) {
                    ImGui.SameLine();
                    if (ImGui.ColorButton($"boxcolor##{i}", boxColor)) {
                        ImGui.SetNextWindowPos(ImGui.GetMousePos());
                        ImGui.OpenPopup($"edit_boxcolor##{i}");
                    }
                    if (ImGui.BeginPopup($"edit_boxcolor##{i}", ImGuiWindowFlags.AlwaysAutoResize)) {
                        ImGui.ColorPicker4($"##boxcolor_picker##{i}", ref boxColor, ImGuiColorEditFlags.AlphaBar | ImGuiColorEditFlags.AlphaPreview);
                        ImGui.EndPopup();
                    }
                }
                if (ImGui.Button($"Delete##{i}")) {
                    Settings.HighlightEntries.RemoveAt(i);
                    return;
                }
                entry.Regex = regex;
                entry.FrameColor = frameColor.ToSharpColor();
                entry.BoxColor = boxColor.ToSharpColor();
                ImGui.PopID();
            }
            ImGui.TreePop();
            if (ImGui.Button("Add")) {
                Settings.HighlightEntries.Add(new StrongboxHighlightEntry());
            }
        }
    }
}
