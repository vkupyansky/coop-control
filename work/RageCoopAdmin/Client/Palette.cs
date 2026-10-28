using System.Drawing;

namespace RageCoopAdmin.Client;

// цвета OG Coop: бирюзовый #2A9D8F и оранжевый #F4A261
internal static class Palette
{
    internal static readonly Color Turquoise = Color.FromArgb(255, 0x2A, 0x9D, 0x8F);
    internal static readonly Color Orange = Color.FromArgb(255, 0xF4, 0xA2, 0x61);
    // текст на оранжевой выбранной строке
    internal static readonly Color DeepTurquoise = Color.FromArgb(255, 0x12, 0x45, 0x3E);
    // матовый фон меню: тёмный с бирюзовым оттенком, подзаголовок чуть темнее
    internal static readonly Color Matte = Color.FromArgb(230, 0x10, 0x24, 0x22);
    internal static readonly Color MatteDeep = Color.FromArgb(240, 0x0A, 0x1A, 0x18);
    internal static readonly Color MatteText = Color.FromArgb(255, 0x9F, 0xD3, 0xCB);

    internal static Color WithAlpha(Color color, int alpha) => Color.FromArgb(alpha, color);
}
