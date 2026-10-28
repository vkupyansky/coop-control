using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

// Isolate rendering and physical controller polling, not text-input behavior.
namespace LemonUI.Menus
{
    using LemonUI.Elements;

    internal class NativeMenu
    {
        private bool _visible;
        internal ScaledText BannerText { get; } = new ScaledText(string.Empty, 1f);
        internal string Name { get; set; } = string.Empty;
        internal string Description { get; set; } = string.Empty;
        internal string NoItemsText { get; set; } = string.Empty;
        internal List<NativeItem> Items { get; } = new List<NativeItem>();
        internal int SelectedIndex { get; set; }
        internal bool AcceptsInput { get; set; } = true;
        internal bool Visible
        {
            get => _visible;
            set { if (_visible == value) return; _visible = value; if (!value) Closed?.Invoke(this, EventArgs.Empty); }
        }
        internal event EventHandler? Closed;
        internal void Add(NativeItem item) => Items.Add(item);
        internal void Add(int position, NativeItem item) => Items.Insert(position, item);
        internal void Remove(NativeItem item)
        {
            Items.Remove(item);
            if (SelectedIndex >= Items.Count) SelectedIndex = Items.Count - 1;
        }
    }

    internal class NativeItem
    {
        protected readonly ScaledText title;
        internal string Title { get => title.Text; set => title.Text = value; }
        internal string AltTitle { get; set; } = string.Empty;
        private string _description = string.Empty;
        internal string Description { get => _description; set { _description = value; DescriptionUpdates++; } }
        internal int DescriptionUpdates { get; private set; }
        internal bool Enabled { get; set; } = true;
        internal ScaledTexture? LeftBadge { get; set; }
        internal ScaledTexture? RightBadge { get; set; }
        internal event EventHandler? Activated;
        internal NativeItem(string title, string description)
        { this.title = new ScaledText(title, 0.345f); Description = description; }
        public virtual void Recalculate(PointF position, SizeF size, bool selected)
        { title.Position = new PointF(position.X + 6f, position.Y + 3f); }
        internal void Activate() { if (Enabled) Activated?.Invoke(this, EventArgs.Empty); }
    }

    internal class NativeListItem<T> : NativeItem
    {
        protected readonly ScaledText text = new ScaledText(string.Empty, 0.35f);
        private List<T> _items;
        private int _selectedIndex;
        internal List<T> Items { get => _items; set { _items = value; _ = SelectedItem; } }
        internal int SelectedIndex { get => _selectedIndex; set => _selectedIndex = value; }
        internal T SelectedItem => Items[_selectedIndex];
        internal ScaledTexture LeftArrow { get; } = new ScaledTexture();
        internal ScaledTexture RightArrow { get; } = new ScaledTexture();
        internal event EventHandler<ItemChangedEventArgs>? ItemChanged;
        internal NativeListItem(string title, string description, params T[] values) : base(title, description) => _items = values.ToList();
        internal void Select(int index) { _selectedIndex = index; ItemChanged?.Invoke(this, new ItemChangedEventArgs(index)); }
    }

    internal sealed class NativeCheckboxItem : NativeItem
    {
        internal NativeCheckboxItem(string title, string description, bool selected) : base(title, description)
        { Checked = selected; }
        internal bool Checked { get; set; }
    }

    internal sealed class NativeSubmenuItem : NativeItem
    {
        internal NativeSubmenuItem(NativeMenu menu) : base(menu.Name, string.Empty) { Menu = menu; }
        internal NativeMenu Menu { get; }
    }

    internal sealed class ItemChangedEventArgs : EventArgs
    {
        internal int Index { get; }
        internal ItemChangedEventArgs(int index) => Index = index;
    }
}

namespace LemonUI.Elements
{
    // Only SDK rendering is isolated, production localization and text handling
    // are compiled directly. No actual game text widths are asserted here.
    internal sealed class ScaledText
    {
        internal ScaledText(string text, float scale) { Text = text; Scale = scale; }
        internal string Text { get; set; }
        internal float Scale { get; }
        internal PointF Position { get; set; }
        internal float Width => Text.Length * 9f * Scale / 0.345f;
    }

    internal sealed class ScaledTexture
    {
        internal PointF Position { get; set; }
        internal SizeF Size { get; set; }
    }
}
