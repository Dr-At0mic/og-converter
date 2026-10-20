using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>
    /// "Will it fit?" for Guetzli, in the spirit of LM Studio's model settings: this computer's CPU and memory, a bar of the
    /// memory the encode needs against what's free, and the time it should take.
    /// </summary>
    internal static class GuetzliResourcePanel
    {
        public static Control Build(GuetzliPlan plan)
        {
            var specs = plan.Specs;
            string level = plan.MemoryLoad <= 0.7 ? "SuccessBrush" : plan.MemoryLoad <= 1 ? "WarningBrush" : "DangerBrush";

            var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 14) };
            T Themed<T>(T control, AvaloniaProperty property, string key) where T : Control
            {
                control.Bind(property, panel.GetResourceObservable(key));
                return control;
            }

            var cpu = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            cpu.Children.Add(Themed(new TextBlock { Text = Icons.Icons.Glyph(""), FontFamily = Icons.Icons.Font, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) },
                TextBlock.ForegroundProperty, "TextSecondaryBrush"));
            cpu.Children.Add(Themed(new TextBlock { Text = specs.Describe(), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis },
                TextBlock.ForegroundProperty, "TextSecondaryBrush"));
            panel.Children.Add(cpu);

            // Memory bar: the encode's peak against the memory that's free now
            double fill = Math.Clamp(plan.MemoryLoad, 0, 1);
            var bar = new Grid { Height = 8, Margin = new Thickness(0, 0, 0, 6) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(fill, 0.001), GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - fill, 0.001), GridUnitType.Star) });
            var track = Themed(new Border { CornerRadius = new CornerRadius(4) }, Border.BackgroundProperty, "BorderStrongBrush");
            Grid.SetColumnSpan(track, 2);
            bar.Children.Add(track);
            bar.Children.Add(Themed(new Border { CornerRadius = new CornerRadius(4) }, Border.BackgroundProperty, level));
            panel.Children.Add(bar);

            var numbers = new Grid();
            numbers.Children.Add(Themed(new TextBlock
            {
                Text = $"Memory: up to {SystemSpecs.Gb(plan.PeakBytes)} of {SystemSpecs.Gb(specs.FreeRamBytes)} free",
                FontSize = 11.5
            }, TextBlock.ForegroundProperty, level));
            numbers.Children.Add(Themed(new TextBlock
            {
                Text = plan.Included.Count == 0 ? "Nothing to encode"
                     : $"{GuetzliService.DescribeTime(plan.Seconds)} · {plan.Jobs} at once",
                FontSize = 11.5,
                HorizontalAlignment = HorizontalAlignment.Right
            }, TextBlock.ForegroundProperty, "TextSecondaryBrush"));
            panel.Children.Add(numbers);
            return panel;
        }
    }
}
