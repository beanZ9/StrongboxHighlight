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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ExileCore.Shared.Nodes;
using Color = SharpDX.Color;

namespace StrongboxHighlight;

public class StrongboxHighlight : BaseSettingsPlugin<StrongboxHighlightSettings>
{
    private readonly List<string> _excludedStrings = new List<string>() { "account-bound", "italic" };
    private Dictionary<StrongboxHighlightEntry, List<Element>> _highlightedLabels = new Dictionary<StrongboxHighlightEntry, List<Element>>();
    private List<LabelOnGround> _chestLabels = new List<LabelOnGround>();
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

        Settings.Reload.OnPressed += () => {

        };
        return true;
    }

    public override void AreaChange(AreaInstance area)
    {
        _enabledArea = !area.IsHideout && !area.IsTown && !area.IsPeaceful;
        if (_enabledArea) {
            _currentTask = Update();
        } else {
            _currentTask = null;
        }
    }

    public override Job Tick()
    {
        if (_currentTask != null) {
            TaskUtils.RunOrRestart(ref _currentTask, () => null);
        }
        return null;
    }

    private async SyncTask<bool> Update() {
        while (_enabledArea) {
            if (_chestLabels.Count < 1) {
                _chestLabels = FindChests(100);
                _highlightedLabels.Clear();
                
            }
            await ProcessChests();
            await Task.Delay(50);
            return true;
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

    private async SyncTask<bool> ProcessChests() {        
        for (int i = 0; i < Settings.HighlightEntries.Count; i++) {
            var entry = Settings.HighlightEntries[i];
            Regex regex;
            try {
                regex = new Regex(entry.Regex, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            } catch (Exception e) {
                DebugWindow.LogError($"Can't compile regex: {e.Message}");
                return false;
            }

            foreach (var chest in _chestLabels) {
                if (chest.Label[0] != null && chest.Label[0][1] != null && chest.Label[0][1].Children
                    .Where(x => !string.IsNullOrEmpty(x.Text) && !_excludedStrings.Any(s => x.Text.Contains(s, StringComparison.OrdinalIgnoreCase)))
                    .Select(x => x.Text.ToLower())
                    .ToArray() is { Length: > 0 } modStrings) {

                    string added = string.Join("\n", modStrings);

                    if (regex.IsMatch(added)) {
                        if (_highlightedLabels.TryGetValue(entry, out var labels)) {
                            labels.Add(chest.Label);
                        } else {
                            _highlightedLabels.Add(entry, new List<Element>() { chest.Label });
                        }
                    }
                }
            }
        }
        _chestLabels.Clear();
        await Task.Delay(1);
        return true;
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