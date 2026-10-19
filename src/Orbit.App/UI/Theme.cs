using System.Windows.Markup;
using Orbit.App.Services;

namespace Orbit.App.UI;

public static class Theme
{
    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    public static void Apply(AppSettings settings)
    {
        var dark = settings.IsDark;
        var highContrast = SystemParameters.HighContrast;
        var glass = settings.Glass && !highContrast;
        var colors = new Dictionary<string, Color>
        {
            ["Ink"] = highContrast ? SystemColors.WindowTextColor : ColorOf(dark ? "#EDF5F6" : "#193A42"),
            ["MutedInk"] = highContrast ? SystemColors.WindowTextColor : ColorOf(dark ? "#B4C8CD" : "#506B70"),
            ["WindowFill"] = highContrast ? SystemColors.WindowColor : ColorOf(dark ? (glass ? "#240F1B26" : "#FF182630") : (glass ? "#28F3FAFF" : "#FFECF5F3")),
            ["PanelFill"] = highContrast ? SystemColors.WindowColor : ColorOf(dark ? "#10FFFFFF" : "#38FFFFFF"),
            ["InputFill"] = highContrast ? SystemColors.WindowColor : ColorOf(dark ? "#58202C38" : "#8CFFFFFF"),
            ["Edge"] = highContrast ? SystemColors.WindowTextColor : ColorOf(dark ? "#52FFFFFF" : "#D9FFFFFF"),
            ["Line"] = highContrast ? SystemColors.WindowTextColor : ColorOf(dark ? "#22FFFFFF" : "#72FFFFFF"),
            ["Accent"] = highContrast ? SystemColors.HighlightColor : ColorOf("#167D72"),
            ["AccentInk"] = highContrast ? SystemColors.HighlightTextColor : Colors.White,
            ["Danger"] = ColorOf(dark ? "#FFB2A9" : "#A93832")
        };
        foreach (var pair in colors) { var brush = new SolidColorBrush(pair.Value); brush.Freeze(); Application.Current.Resources[pair.Key] = brush; }
        var sheen = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(1, 1),
            GradientStops = new GradientStopCollection
            {
                new(ColorOf(highContrast || !glass ? "#00FFFFFF" : dark ? "#13FFFFFF" : "#40FFFFFF"), 0),
                new(Colors.Transparent, .48), new(ColorOf(highContrast || !glass ? "#00FFFFFF" : "#0600CDBA"), 1)
            }
        };
        sheen.Freeze(); Application.Current.Resources["GlassSheen"] = sheen;
    }
    private static Color ColorOf(string value) => (Color)ColorConverter.ConvertFromString(value);

    public static void InstallStyles()
    {
        const string xaml = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="TextBlock"><Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="FontFamily" Value="Segoe UI Variable Text"/><Setter Property="FontSize" Value="13"/></Style>
          <Style TargetType="Button">
            <Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="Background" Value="{DynamicResource PanelFill}"/>
            <Setter Property="BorderBrush" Value="{DynamicResource Edge}"/><Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="14,9"/><Setter Property="FontFamily" Value="Segoe UI Variable Text"/><Setter Property="FontSize" Value="13"/>
            <Setter Property="Cursor" Value="Hand"/><Setter Property="MinHeight" Value="36"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="border" CornerRadius="10" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="border" Property="Opacity" Value="0.8"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="border" Property="BorderBrush" Value="{DynamicResource Accent}"/><Setter TargetName="border" Property="BorderThickness" Value="2"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="border" Property="Opacity" Value="0.4"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key="PrimaryButton" TargetType="Button" BasedOn="{StaticResource {x:Type Button}}"><Setter Property="Background" Value="{DynamicResource Accent}"/><Setter Property="Foreground" Value="{DynamicResource AccentInk}"/><Setter Property="BorderBrush" Value="{DynamicResource Accent}"/></Style>
          <Style TargetType="TextBox"><Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="Background" Value="{DynamicResource InputFill}"/><Setter Property="BorderBrush" Value="{DynamicResource Line}"/><Setter Property="Padding" Value="9,7"/><Setter Property="FontSize" Value="13"/><Setter Property="CaretBrush" Value="{DynamicResource Ink}"/></Style>
          <Style TargetType="CheckBox"><Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="FontSize" Value="13"/><Setter Property="Margin" Value="0,6"/><Setter Property="VerticalContentAlignment" Value="Center"/></Style>
          <Style TargetType="ComboBox">
            <Setter Property="FontSize" Value="13"/><Setter Property="Padding" Value="9,7"/><Setter Property="MinHeight" Value="34"/>
            <Setter Property="Foreground" Value="{DynamicResource {x:Static SystemColors.ControlTextBrushKey}}"/>
            <Setter Property="ItemTemplate"><Setter.Value><DataTemplate><TextBlock Text="{Binding}" Foreground="{DynamicResource {x:Static SystemColors.ControlTextBrushKey}}"/></DataTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="Slider"><Setter Property="Margin" Value="0,8"/><Setter Property="IsSnapToTickEnabled" Value="True"/><Setter Property="TickFrequency" Value="1"/></Style>
        </ResourceDictionary>
        """;
        Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(xaml));
    }
}

public static class Ui
{
    public static TextBlock Text(string text, double size = 13, bool muted = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, muted ? "MutedInk" : "Ink"); return block;
    }
    public static Button Button(string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text };
        if (primary) button.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        button.Click += (_, _) => action(); return button;
    }
    public static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children) { if (child is FrameworkElement element) element.Margin = new Thickness(0, 0, 8, 0); row.Children.Add(child); }
        return row;
    }
    public static Border Panel(UIElement child, int padding = 18)
    {
        var border = new Border { Child = child, Padding = new Thickness(padding), CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "PanelFill"); border.SetResourceReference(Border.BorderBrushProperty, "Line"); return border;
    }
}
