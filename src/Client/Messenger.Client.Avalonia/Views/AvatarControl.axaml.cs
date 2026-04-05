using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Messenger.Client.Avalonia.Views;

public partial class AvatarControl : UserControl
{
    private static readonly string[] Palette =
    [
        "#2C7A7B",
        "#3B5998",
        "#6B46C1",
        "#2D6A4F",
        "#C0392B",
        "#E67E22",
        "#16213E",
        "#4A4E69"
    ];

    public static readonly StyledProperty<string> DisplayNameProperty =
        AvaloniaProperty.Register<AvatarControl, string>(nameof(DisplayName), defaultValue: string.Empty);

    public string DisplayName
    {
        get => GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    public AvatarControl()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DisplayNameProperty)
        {
            UpdateAvatar((string?)change.NewValue ?? string.Empty);
        }
    }

    private void UpdateAvatar(string displayName)
    {
        if (AvatarBorder is null || InitialText is null) return;

        var colorHex = string.IsNullOrEmpty(displayName)
            ? Palette[0]
            : Palette[Math.Abs(displayName.GetHashCode()) % Palette.Length];

        AvatarBorder.Background = SolidColorBrush.Parse(colorHex);

        InitialText.Text = string.IsNullOrEmpty(displayName)
            ? "?"
            : displayName[0].ToString().ToUpperInvariant();
    }
}
