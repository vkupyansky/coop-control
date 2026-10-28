using System;
using System.Drawing;
using System.Reflection;
using LemonUI.Elements;
using LemonUI.Menus;

namespace RageCoopAdmin.Client;

// меню в цветах Palette
internal static class MenuStyle
{
    // общий набор для всех строк. Белую рамку выбора LemonUI рисует сам и цвет ей не задать,
    // поэтому строки рисуют свой фон: матовый, а у выбранной оранжевый. Значки не красим
    internal static readonly ColorSet Items = new ColorSet
    {
        BackgroundNormal = Palette.Matte,
        BackgroundDisabled = Palette.Matte,
        BackgroundHovered = Palette.Orange,
        TitleHovered = Palette.DeepTurquoise,
        AltTitleHovered = Palette.DeepTurquoise,
        ArrowsHovered = Palette.DeepTurquoise,
    };

    private static readonly Color Clear = Color.FromArgb(0, 0, 0, 0);
    // подзаголовок, общий градиент под строками и описание LemonUI держит в закрытых полях
    private static readonly FieldInfo? NameBackground = MenuField("nameImage");
    private static readonly FieldInfo? NameText = MenuField("nameText");
    private static readonly FieldInfo? RowsBackground = MenuField("backgroundImage");
    private static readonly FieldInfo? DescriptionBackground = MenuField("descriptionRect");
    private static readonly FieldInfo? DescriptionText = MenuField("descriptionText");
    private static ScaledRectangle? _description;

    internal static void Frame(NativeMenu menu)
    {
        if (menu == null) throw new ArgumentNullException(nameof(menu));
        // свою шапку LemonUI рисует картинкой с бликами, подкрашенная она тёмно-синяя, поэтому шапка ровная
        if (menu.Banner is not ScaledRectangle) menu.Banner = new ScaledRectangle(PointF.Empty, SizeF.Empty);
        menu.Banner.Color = Palette.Turquoise;
        if (menu.BannerText != null)
        {
            // как логотип GTA: Pricedown, белые буквы с чёрной обводкой
            menu.BannerText.Font = GTA.UI.Font.Pricedown;
            menu.BannerText.Color = Color.White;
            menu.BannerText.Outline = true;
        }

        if (NameBackground?.GetValue(menu) is ScaledRectangle name) name.Color = Palette.MatteDeep;
        if (NameText?.GetValue(menu) is ScaledText nameText) nameText.Color = Palette.MatteText;
        // у каждой строки свой фон, а тёмные градиенты LemonUI не перекрасить, их прячем
        if (RowsBackground?.GetValue(menu) is ScaledTexture rows) rows.Color = Clear;
        if (DescriptionBackground?.GetValue(menu) is ScaledTexture description) description.Color = Clear;
    }

    // строки подменю LemonUI создаёт сам, поэтому цвета ставятся открытому меню перед отрисовкой
    internal static void Paint(NativeMenu menu)
    {
        if (menu == null) throw new ArgumentNullException(nameof(menu));
        foreach (NativeItem item in menu.Items)
        {
            if (ReferenceEquals(item.Colors, Items)) continue;
            item.UseCustomBackground = true;
            item.Colors = Items;
        }

        // ровный матовый прямоугольник там, где LemonUI рисует описание, до его текста
        if (DescriptionBackground?.GetValue(menu) is ScaledTexture box &&
            DescriptionText?.GetValue(menu) is ScaledText text && !string.IsNullOrWhiteSpace(text.Text))
        {
            ScaledRectangle back = _description ??= new ScaledRectangle(PointF.Empty, SizeF.Empty) { Color = Palette.Matte };
            back.Position = box.Position;
            back.Size = box.Size;
            back.Draw();
        }
    }

    private static FieldInfo? MenuField(string name) =>
        typeof(NativeMenu).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
}
