using ExileCore.Shared.Attributes;
using ExileCore.Shared.Interfaces;
using ExileCore.Shared.Nodes;
using System.Collections.Generic;

namespace StrongboxHighlight;

public class StrongboxHighlightSettings : ISettings
{
    //Mandatory setting to allow enabling/disabling your plugin
    public ToggleNode Enable { get; set; } = new ToggleNode(false);
    public ButtonNode Reload { get; set; } = new ButtonNode();

    [IgnoreMenu]
    public List<StrongboxHighlightEntry> HighlightEntries { get; set; } = new List<StrongboxHighlightEntry>();
}