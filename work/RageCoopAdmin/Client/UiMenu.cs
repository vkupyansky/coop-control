using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using LemonUI.Menus;

namespace RageCoopAdmin.Client;

// Bind only resource-owned UI slots. Player names, model names and editable text
// are passed through untouched; translation never searches/replaces user data.
internal static class UiMenu
{
    private static readonly ConditionalWeakTable<object, Binding> Bindings = new ConditionalWeakTable<object, Binding>();
    internal static bool IsRefreshing { get; private set; }

    internal static NativeItem Item(Func<string> title, Func<string>? description = null, Action<NativeItem>? configure = null) =>
        Create((a, b, _) => new NativeItem(a, b), title, description, null, configure);

    internal static NativeCheckboxItem Checkbox(Func<string> title, Func<string> description, bool selected = false,
        Action<NativeCheckboxItem>? configure = null) =>
        Create((a, b, _) => new NativeCheckboxItem(a, b, selected), title, description, null, configure);

    internal static NativeListItem<string> List(Func<string> title, Func<string> description, Func<string[]> options,
        Action<NativeListItem<string>>? configure = null) =>
        Create((a, b, c) => new NativeListItem<string>(a, b, c), title, description, options, configure);

    internal static VehicleMenuItem VehicleItem(Func<string> title, Func<string> description, Action<VehicleMenuItem>? configure = null) =>
        Create((a, b, _) => new VehicleMenuItem(a, b), title, description, null, configure);

    internal static VehicleMenuListItem VehicleList(Func<string> title, Func<string> description, Func<string[]> options,
        Action<VehicleMenuListItem>? configure = null) =>
        Create((a, b, c) => new VehicleMenuListItem(a, b, c), title, description, options, configure);

    private static T Create<T>(Func<string, string, string[], T> create, Func<string> title, Func<string>? description,
        Func<string[]>? options, Action<T>? configure) where T : NativeItem
    {
        var binding = new Binding(UiText.Capture(title), description == null ? null : UiText.Capture(description),
            options == null ? null : CaptureOptions(options));
        T item = create(binding.Title.Value, binding.Description?.Value ?? string.Empty,
            binding.Options?.Select(value => value.Value).ToArray() ?? Array.Empty<string>());
        configure?.Invoke(item);
        Bindings.Add(item, binding);
        return item;
    }

    internal static NativeMenu Bind(NativeMenu menu, string title, string name)
    {
        Bindings.Add(menu, new Binding(UiText.Capture(() => UiText.Get(title)), UiText.Capture(() => UiText.Get(name)), null));
        Apply(menu);
        return menu;
    }

    internal static void Title(NativeItem item, Func<string> value)
    {
        Binding binding = GetBinding(item);
        binding.Title = UiText.Capture(value);
        SetTitle(item, binding.Title.Value);
    }

    internal static void Description(NativeItem item, Func<string> value)
    {
        Binding binding = GetBinding(item);
        binding.Description = UiText.Capture(value);
        item.Description = binding.Description.Value;
    }

    internal static void Description(NativeMenu menu, Func<string> value)
    {
        if (!Bindings.TryGetValue(menu, out Binding? binding) || binding == null) throw new InvalidOperationException("Menu is not localized.");
        binding.MenuDescription = UiText.Capture(value);
        menu.Description = binding.MenuDescription.Value;
    }

    internal static void AltTitle(NativeItem item, Func<string> value)
    {
        Binding binding = GetBinding(item);
        binding.AltTitle = UiText.Capture(value);
        item.AltTitle = binding.AltTitle.Value;
    }

    internal static void Options(NativeListItem<string> item, Func<string[]> values)
    {
        Binding binding = GetBinding(item);
        binding.Options = CaptureOptions(values);
        bool refreshing = IsRefreshing;
        IsRefreshing = true;
        try { SetOptions(item, binding.Options); }
        finally { IsRefreshing = refreshing; }
    }

    internal static void Name(NativeMenu menu, Func<string> value)
    {
        if (!Bindings.TryGetValue(menu, out Binding? binding) || binding == null) throw new InvalidOperationException("Menu is not localized.");
        binding.Description = UiText.Capture(value);
        menu.Name = binding.Description.Value;
    }

    internal static void Refresh(IEnumerable<NativeMenu> menus)
    {
        IsRefreshing = true;
        try
        {
            NativeMenu[] all = menus.ToArray();
            foreach (NativeMenu menu in all) Apply(menu);
            foreach (NativeMenu menu in all)
            {
                foreach (NativeItem item in menu.Items)
                {
                    if (Bindings.TryGetValue(item, out Binding? binding) && binding != null) Apply(item, binding);
                    if (item is NativeSubmenuItem submenu)
                    {
                        item.Title = submenu.Menu.Name;
                        item.Description = submenu.Menu.Description;
                    }
                }
            }
        }
        finally { IsRefreshing = false; }
    }

    private static void Apply(NativeMenu menu)
    {
        if (!Bindings.TryGetValue(menu, out Binding? binding) || binding == null) return;
        if (menu.BannerText.Text != binding.Title.Value) menu.BannerText.Text = binding.Title.Value;
        if (menu.Name != binding.Description!.Value) menu.Name = binding.Description.Value;
        menu.NoItemsText = UiText.Get("Пока пусто");
        if (binding.MenuDescription != null) menu.Description = binding.MenuDescription.Value;
    }

    private static void Apply(NativeItem item, Binding binding)
    {
        SetTitle(item, binding.Title.Value);
        if (binding.Description != null && item.Description != binding.Description.Value) item.Description = binding.Description.Value;
        if (binding.AltTitle != null && item.AltTitle != binding.AltTitle.Value) item.AltTitle = binding.AltTitle.Value;
        if (binding.Options != null && item is NativeListItem<string> list) SetOptions(list, binding.Options);
    }

    private static void SetTitle(NativeItem item, string value)
    {
        if (item is VehicleMenuItem vehicle)
        {
            if (vehicle.FullTitle != value) vehicle.FullTitle = value;
        }
        else if (item is VehicleMenuListItem vehicleList)
        {
            if (vehicleList.FullTitle != value) vehicleList.FullTitle = value;
        }
        else if (item.Title != value) item.Title = value;
    }

    private static void SetOptions(NativeListItem<string> item, UiTextPair[] values)
    {
        var rendered = values.Select(value => value.Value).ToList();
        if (item.Items.SequenceEqual(rendered)) return;
        int selected = item.SelectedIndex;
        item.SelectedIndex = 0;
        item.Items = rendered;
        item.SelectedIndex = Math.Max(0, Math.Min(selected, rendered.Count - 1));
    }

    private static UiTextPair[] CaptureOptions(Func<string[]> values)
    {
        // Materialize once before capturing each position: no future enumeration
        // or retained mutable list enters a binding.
        return UiText.CaptureOptions(values);
    }

    private static Binding GetBinding(NativeItem item) => Bindings.GetValue(item,
        value => new Binding(new UiTextPair(((NativeItem)value).Title, ((NativeItem)value).Title), null, null));

    private sealed class Binding
    {
        internal Binding(UiTextPair title, UiTextPair? description, UiTextPair[]? options)
        { Title = title; Description = description; Options = options; }
        internal UiTextPair Title;
        internal UiTextPair? Description, AltTitle, MenuDescription;
        internal UiTextPair[]? Options;
    }
}
