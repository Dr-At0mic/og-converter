using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OgConverter.Services;

namespace OgConverter.Views
{
    /// <summary>
    /// "Will it fit?" for Guetzli, in the spirit of LM Studio's model settings: this PC's CPU and memory, a bar of the
    /// memory the encode needs against what's free, and the time it should take.
    /// </summary>
    internal static class GuetzliResourcePanel
    {
        public static FrameworkElement Build(FrameworkElement owner, GuetzliPlan plan)
        {
            Brush Res(string key) => (Brush)owner.FindResource(key);
            var specs = plan.Specs;
            string level = plan.MemoryLoad <= 0.7 ? "SuccessBrush" : plan.MemoryLoad <= 1 ? "WarningBrush" : "DangerBrush";

            var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 14) };

            var cpu = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            cpu.Children.Add(new TextBlock { Text = "", FontFamily = (FontFamily)owner.FindResource("IconFont"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Foreground = Res("TextSecondaryBrush") });
            cpu.Children.Add(new TextBlock { Text = specs.Describe(), FontSize = 12, Foreground = Res("TextSecondaryBrush"), TextTrimming = TextTrimming.CharacterEllipsis });
            panel.Children.Add(cpu);

            // Memory bar: the encode's peak against the memory that's free now
            double fill = Math.Clamp(plan.MemoryLoad, 0, 1);
            var bar = new Grid { Height = 8, Margin = new Thickness(0, 0, 0, 6) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(fill, 0.001), GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - fill, 0.001), GridUnitType.Star) });
            var track = new Border { CornerRadius = new CornerRadius(4), Background = Res("BorderStrongBrush") };
            Grid.SetColumnSpan(track, 2);
            bar.Children.Add(track);
            bar.Children.Add(new Border { CornerRadius = new CornerRadius(4), Background = Res(level) });
            panel.Children.Add(bar);

            var numbers = new Grid();
            numbers.Children.Add(new TextBlock
            {
                Text = $"Memory: up to {SystemSpecs.Gb(plan.PeakBytes)} of {SystemSpecs.Gb(specs.FreeRamBytes)} free",
                FontSize = 11.5,
                Foreground = Res(level)
            });
            numbers.Children.Add(new TextBlock
            {
                Text = plan.Included.Count == 0 ? "Nothing to encode"
                     : $"{GuetzliService.DescribeTime(plan.Seconds)} · {plan.Jobs} at once",
                FontSize = 11.5,
                HorizontalAlignment = HorizontalAlignment.Right,
                Foreground = Res("TextSecondaryBrush")
            });
            panel.Children.Add(numbers);
            return panel;
        }
    }
}
