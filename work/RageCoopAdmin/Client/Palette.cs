using System.Drawing;

namespace RageCoopAdmin.Client;

internal static class Palette
{
    internal static readonly Color Turquoise = Color.FromArgb(255, 0x2A, 0x9D, 0x8F);
    internal static readonly Color Orange = Color.FromArgb(255, 0xF4, 0xA2, 0x61);
    // Text on the orange selected row.
    internal static readonly Color DeepTurquoise = Color.FromArgb(255, 0x12, 0x45, 0x3E);
    // The menu's matte background, the subtitle a little darker.
    internal static readonly Color Matte = Color.FromArgb(230, 0x10, 0x24, 0x22);
    internal static readonly Color MatteDeep = Color.FromArgb(240, 0x0A, 0x1A, 0x18);
    internal static readonly Color MatteText = Color.FromArgb(255, 0x9F, 0xD3, 0xCB);

    internal static Color WithAlpha(Color color, int alpha) => Color.FromArgb(alpha, color);
}
