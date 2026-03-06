using Color = SharpDX.Color;

namespace StrongboxHighlight {
    public class StrongboxHighlightEntry {
        public string Regex { get; set; } = string.Empty;
        public Color FrameColor { get; set; } = new Color(255, 0, 0, 255);
        public Color BoxColor { get; set; } = new Color(255, 0, 0, 255);
        public bool DrawFrame { get; set; } = false;
        public bool DrawBox { get; set; } = false;
    }
}
