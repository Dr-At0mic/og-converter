using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace OgConverter.Views
{
    /// <summary>
    /// Builds the code-generated "settings" area of the Compress and Archive dialogs (labels, combos, sliders, checks,
    /// notes), the same controls the Windows dialogs build. Changes call <c>refresh</c> (summary only) or <c>rebuild</c>
    /// (the controls depend on the choice); nothing fires while <c>isBuilding</c> is true.
    /// </summary>
    internal sealed class SettingsBuilder
    {
        private readonly Panel _panel;
        private readonly Func<bool> _isBuilding;
        private readonly Action _refresh;
        private readonly Action _rebuild;

        public SettingsBuilder(Panel panel, Func<bool> isBuilding, Action refresh, Action rebuild)
        {
            _panel = panel;
            _isBuilding = isBuilding;
            _refresh = refresh;
            _rebuild = rebuild;
        }

        public void Add(Control control) => _panel.Children.Add(control);

        public void Label(string text) =>
            Add(new TextBlock { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) });

        public void Note(string text) =>
            Add(new TextBlock { Text = text, Classes = { "Caption" }, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) });

        /// <summary>A labelled combo. A saved value that isn't one of the steps shows as "Custom (…)" rather than changing silently.</summary>
        public ComboBox Combo(string label, IList<(object Value, string Text)> items, object current, Action<object> set, bool rebuild = false, bool keepCustom = true)
        {
            Label(label);
            var combo = new ComboBox { Margin = new Thickness(0, 0, 0, 14), HorizontalAlignment = HorizontalAlignment.Stretch, MaxDropDownHeight = 420 };
            int selected = -1;
            for (int i = 0; i < items.Count; i++)
            {
                combo.Items.Add(new ComboBoxItem { Content = items[i].Text, Tag = items[i].Value });
                if (Equals(items[i].Value, current)) selected = i;
            }
            if (selected < 0 && keepCustom)
            {
                combo.Items.Add(new ComboBoxItem { Content = $"Custom ({current})", Tag = current });
                selected = combo.Items.Count - 1;
            }
            else if (selected < 0)
            {
                selected = 0;
                set(items[0].Value);
            }
            combo.SelectedIndex = selected;
            combo.SelectionChanged += (_, _) =>
            {
                if (_isBuilding() || combo.SelectedItem is not ComboBoxItem { Tag: { } value }) return;
                set(value);
                if (rebuild) _rebuild(); else _refresh();
            };
            Add(combo);
            return combo;
        }

        /// <summary>A labelled integer slider with its value on the right and the two ends described under it.</summary>
        public Slider Slider(string label, int min, int max, int value, Action<int> set, Func<int, string> describe, string left, string right, int largeChange = 5)
        {
            var header = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            header.Children.Add(new TextBlock { Text = label, FontWeight = FontWeight.SemiBold });
            var text = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, FontSize = 12, Text = describe(value) };
            text.Bind(TextBlock.ForegroundProperty, header.GetResourceObservable("TextSecondaryBrush"));
            header.Children.Add(text);
            Add(header);

            var slider = new Slider { Minimum = min, Maximum = max, SmallChange = 1, LargeChange = largeChange, IsSnapToTickEnabled = true, TickFrequency = 1, Value = value };
            slider.ValueChanged += (_, e) =>
            {
                int v = (int)Math.Round(e.NewValue);
                set(v);
                text.Text = describe(v);
                if (!_isBuilding()) _refresh();
            };
            Add(slider);

            var scale = new Grid { Margin = new Thickness(0, 2, 0, 14) };
            var l = new TextBlock { Text = left, FontSize = 11 };
            var r = new TextBlock { Text = right, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
            l.Bind(TextBlock.ForegroundProperty, scale.GetResourceObservable("TextTertiaryBrush"));
            r.Bind(TextBlock.ForegroundProperty, scale.GetResourceObservable("TextTertiaryBrush"));
            scale.Children.Add(l);
            scale.Children.Add(r);
            Add(scale);
            return slider;
        }

        public CheckBox Check(string text, bool value, Action<bool> set)
        {
            var check = new CheckBox
            {
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
                IsChecked = value,
                FontSize = 12.5,
                Margin = new Thickness(0, 0, 0, 10)
            };
            check.IsCheckedChanged += (_, _) =>
            {
                set(check.IsChecked == true);
                if (!_isBuilding()) _refresh();
            };
            Add(check);
            return check;
        }
    }
}
