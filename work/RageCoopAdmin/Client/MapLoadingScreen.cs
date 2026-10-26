using System;
using GTA.Native;

namespace RageCoopAdmin.Client;

// Full-screen loading card in the menu's colours (near-black panel, orange
// accent), drawn above GTA's own screen fade. The fade stays black in frames
// where scripts do not run (the map switch stalls them for seconds), so the
// reloading world never shows. Screen fractions fit every resolution.
// Game thread, every frame.
internal static class MapLoadingScreen
{
    private const float BarWidth = 0.24f;
    private const float BarY = 0.585f;
    private const float SweepWidth = 0.06f;
    private const double SweepSeconds = 1.6;

    internal static void Draw(int alpha, double seconds)
    {
        alpha = Math.Max(0, Math.Min(255, alpha));
        Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME);
        Function.Call((Hash)0x61BB1D9B3A95D802UL, AfterFade); // SET_SCRIPT_GFX_DRAW_ORDER
        Function.Call(Hash.DRAW_RECT, 0.5f, 0.5f, 1f, 1f, 10, 12, 15, alpha, false);

        Text(UiText.Get("OG Coop").ToUpperInvariant(), 0.355f, 0.34f, 255, 167, 46, alpha);
        Function.Call(Hash.DRAW_RECT, 0.5f, 0.392f, 0.036f, 0.0028f, 255, 167, 46, alpha, false);
        Text(UiText.Get("Загружаем карту"), 0.41f, 0.85f, 236, 234, 230, alpha);
        Text(UiText.Get("Авианосцы, яхты, грузовое судно и аэродром"), 0.505f, 0.42f, 170, 178, 188, alpha);

        // Track with a segment gliding across it: steady motion reads as work in progress.
        Function.Call(Hash.DRAW_RECT, 0.5f, BarY, BarWidth, 0.004f, 255, 255, 255, alpha * 40 / 255, false);
        double phase = seconds / SweepSeconds % 1.0;
        float eased = (float)(0.5 - Math.Cos(phase * 2 * Math.PI) / 2); // there and back, easing at both ends
        float left = 0.5f - BarWidth / 2;
        float x = left + eased * (BarWidth - SweepWidth) + SweepWidth / 2;
        Function.Call(Hash.DRAW_RECT, x, BarY, SweepWidth, 0.004f, 255, 167, 46, alpha, false);

        Text(UiText.Get("Один раз за запуск GTA: дальше места просто стоят на карте."), 0.88f, 0.34f, 140, 148, 158, alpha);
        Function.Call((Hash)0x61BB1D9B3A95D802UL, AfterHud);
    }

    // GTA's draw layers: GFX_ORDER_AFTER_HUD (scripts' default) and GFX_ORDER_AFTER_FADE.
    private const int AfterHud = 4;
    private const int AfterFade = 7;

    internal static void FadeOut(int milliseconds) => Function.Call((Hash)0x891B5B39AC6302AFUL, milliseconds); // DO_SCREEN_FADE_OUT
    internal static void FadeIn(int milliseconds) => Function.Call((Hash)0xD4E8E24955024033UL, milliseconds); // DO_SCREEN_FADE_IN
    internal static bool IsFadedOut() => Function.Call<bool>((Hash)0xB16FCE9DDC7BA182UL); // IS_SCREEN_FADED_OUT
    internal static bool IsFadingOut() => Function.Call<bool>((Hash)0x797AC7CB535BA28FUL); // IS_SCREEN_FADING_OUT

    private static void Text(string text, float y, float scale, int r, int g, int b, int alpha)
    {
        Function.Call(Hash.SET_TEXT_FONT, (int)GTA.UI.Font.ChaletLondon);
        Function.Call(Hash.SET_TEXT_SCALE, scale, scale);
        Function.Call(Hash.SET_TEXT_COLOUR, r, g, b, alpha);
        Function.Call(Hash.SET_TEXT_CENTRE, true);
        Function.Call(Hash.BEGIN_TEXT_COMMAND_DISPLAY_TEXT, "STRING");
        Function.Call(Hash.ADD_TEXT_COMPONENT_SUBSTRING_PLAYER_NAME, text);
        Function.Call(Hash.END_TEXT_COMMAND_DISPLAY_TEXT, 0.5f, y, 0);
    }
}
