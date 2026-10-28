using System;
using System.Drawing;
using System.Reflection;
using LemonUI.Elements;
using LemonUI.Menus;

namespace RageCoopAdmin.Client;

internal static class MenuStyle
{
    // LemonUI draws the white selection frame itself and its colour cannot be set,
    // so the rows draw their own background, orange when selected. Badges keep their colours.
    internal static readonly ColorSet Items = new ColorSet
    {
        BackgroundNormal = Palette.Matte,
        BackgroundDisabled = Palette.Matte,
        BackgroundHovered = Palette.Orange,
        TitleHovered = Palette.DeepTurquoise,
        AltTitleHovered = Palette.DeepTurquoise,
        ArrowsHovered = Palette.DeepTurquoise,
    };

    // LemonUI paints a disabled row the same whether it is selected or not,
    // so the selected disabled row gets a muted orange of its own.
    internal static readonly ColorSet SelectedDisabled = new ColorSet
    {
        BackgroundNormal = Palette.Matte,
        BackgroundDisabled = Palette.WithAlpha(Palette.Orange, 110),
        BackgroundHovered = Palette.Orange,
        TitleHovered = Palette.DeepTurquoise,
        AltTitleHovered = Palette.DeepTurquoise,
        ArrowsHovered = Palette.DeepTurquoise,
    };

    // LemonUI's default banner height.
    private const float BannerHeight = 108f;
    private static readonly Color Clear = Color.FromArgb(0, 0, 0, 0);
    // LemonUI keeps the subtitle, the gradient under the rows and the description in private fields.
    private static readonly FieldInfo? NameBackground = MenuField("nameImage");
    private static readonly FieldInfo? NameText = MenuField("nameText");
    private static readonly FieldInfo? RowsBackground = MenuField("backgroundImage");
    private static readonly FieldInfo? DescriptionBackground = MenuField("descriptionRect");
    private static readonly FieldInfo? DescriptionText = MenuField("descriptionText");
    private static ScaledRectangle? _description;

    internal static void Frame(NativeMenu menu)
    {
        if (menu == null) throw new ArgumentNullException(nameof(menu));
        // LemonUI's banner is a glossy picture that turns dark blue when tinted, so the banner is flat.
        if (menu.Banner is not ScaledRectangle) menu.Banner = FlatBanner(menu.Banner);
        menu.Banner.Color = Palette.Turquoise;
        if (menu.BannerText != null)
        {
            // Like the GTA logo.
            menu.BannerText.Font = GTA.UI.Font.Pricedown;
            menu.BannerText.Color = Color.White;
            menu.BannerText.Outline = true;
        }

        if (NameBackground?.GetValue(menu) is ScaledRectangle name) name.Color = Palette.MatteDeep;
        if (NameText?.GetValue(menu) is ScaledText nameText) nameText.Color = Palette.MatteText;
        // Every row has its own background, and LemonUI's dark gradients cannot be recoloured, so they are hidden.
        if (RowsBackground?.GetValue(menu) is ScaledTexture rows) rows.Color = Clear;
        if (DescriptionBackground?.GetValue(menu) is ScaledTexture description) description.Color = Clear;
    }

    // LemonUI creates submenu rows itself, so colours are set on the open menu before drawing.
    internal static void Paint(NativeMenu menu)
    {
        if (menu == null) throw new ArgumentNullException(nameof(menu));
        NativeItem? selected = menu.SelectedItem;
        foreach (NativeItem item in menu.Items)
        {
            ColorSet wanted = ReferenceEquals(item, selected) && !item.Enabled ? SelectedDisabled : Items;
            if (item.UseCustomBackground && ReferenceEquals(item.Colors, wanted)) continue;
            item.UseCustomBackground = true;
            item.Colors = wanted;
        }

        // A flat matte box where LemonUI draws the description, before its text.
        if (DescriptionBackground?.GetValue(menu) is ScaledTexture box &&
            DescriptionText?.GetValue(menu) is ScaledText text && !string.IsNullOrWhiteSpace(text.Text))
        {
            ScaledRectangle back = _description ??= new ScaledRectangle(PointF.Empty, SizeF.Empty) { Color = Palette.Matte };
            back.Position = box.Position;
            back.Size = box.Size;
            back.Draw();
        }
    }

    // LemonUI takes the banner height from the element itself, and an empty rectangle has none.
    internal static ScaledRectangle FlatBanner(I2Dimensional? current) =>
        new ScaledRectangle(PointF.Empty, new SizeF(0f, current != null && current.Size.Height > 0f ? current.Size.Height : BannerHeight));

    private static FieldInfo? MenuField(string name) =>
        typeof(NativeMenu).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
}
