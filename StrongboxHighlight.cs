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
using Color = SharpDX.Color;

namespace StrongboxHighlight;

public class StrongboxHighlight : BaseSettingsPlugin<StrongboxHighlightSettings>
{
    private readonly List<string> _excludedStrings = new List<string>() { "account-bound", "italic" };
    private Dictionary<StrongboxHighlightEntry, List<Element>> _highlightedLabels = new Dictionary<StrongboxHighlightEntry, List<Element>>();
    private SyncTask<bool> _currentTask;
    private bool _enabledArea;
    public override bool Initialise()
    {
        if (!Settings.HighlightEntries.Any()) {
            Settings.HighlightEntries = new List<StrongboxHighlightEntry> {
                new StrongboxHighlightEntry() {
                    FrameColor = new Color(255, 0, 0, 100),
                    BoxColor = new Color(255, 0, 0, 100),
                    DrawFrame = true,
                    DrawBox = true,
                    Regex = "detonates nearby corpses"
                },
                new StrongboxHighlightEntry() { 
                    FrameColor = new Color(0, 255, 255, 100),
                    BoxColor = new Color(0, 255, 255, 100),
                    DrawFrame = true,
                    DrawBox = true,
                    Regex = "ice nova"
                }
            };
        }
        return true;
    }

    public override void AreaChange(AreaInstance area)
    {
        _enabledArea = !area.IsHideout && !area.IsTown && !area.IsPeaceful;
    }

    public override Job Tick()
    {
        GameController.MultiThreadManager.AddJob(Update, Name);
        return null;
    }

    private void Update() {
        if (!_enabledArea) return;

        DebugWindow.LogMsg($"{base.Name}: Update");
        var fetchedChests = GetChests(100);
        ProcessChests(fetchedChests);

    }

    public override void Render()
    {
        foreach (var highlight in _highlightedLabels) {
            if (highlight.Key.DrawFrame) {
                foreach (var label in highlight.Value) {
                    Graphics.DrawFrame(label.GetClientRectCache, highlight.Key.FrameColor, 5);
                }
            }
            if (highlight.Key.DrawBox) {
                foreach (var label in highlight.Value) {
                    Graphics.DrawBox(label.GetClientRectCache, highlight.Key.BoxColor, 0);
                }
            }
        }
    }

    private List<LabelOnGround> GetChests(int radius) {
        var chestLabels = new List<LabelOnGround>();
        chestLabels = GameController.Game.IngameState.IngameUi.ItemsOnGroundLabelsVisible
            .Where(x => x is not null 
                && x.ItemOnGround != null && x.ItemOnGround.Metadata != null
                && x.ItemOnGround.Metadata.Contains("Metadata/Chests/StrongBoxes") && !x.ItemOnGround.IsOpened && x.ItemOnGround.DistancePlayer <= radius)
            .ToList();
        return chestLabels;
    }

    private void ProcessChests(List<LabelOnGround> chestLabels) {
        var processedLabels = new Dictionary<StrongboxHighlightEntry, List<Element>>();
        for (int i = 0; i < Settings.HighlightEntries.Count; i++) {
            var entry = Settings.HighlightEntries[i];
            Regex regex = new Regex(entry.Regex, RegexOptions.IgnoreCase | RegexOptions.Compiled);

            foreach (var chest in chestLabels) {
                if (chest.Label[0] != null && chest.Label[0][1] != null && chest.Label[0][1].Children
                    .Where(x => !string.IsNullOrEmpty(x.Text) && !_excludedStrings.Any(s => x.Text.Contains(s, StringComparison.OrdinalIgnoreCase)))
                    .Select(x => x.Text.ToLower())
                    .ToArray() is { Length: > 0 } modStrings) {

                    string added = string.Join("\n", modStrings);

                    if (regex.IsMatch(added)) {
                        if (processedLabels.TryGetValue(entry, out var labels)) {
                            labels.Add(chest.Label);
                        } else {
                            processedLabels.Add(entry, new List<Element>() { chest.Label });
                        }
                    }
                }
            }
        }
        _highlightedLabels.Clear();
        _highlightedLabels = processedLabels;
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