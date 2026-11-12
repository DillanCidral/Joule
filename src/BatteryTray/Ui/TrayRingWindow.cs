using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BatteryTray.Ui;

/// <summary>
/// Anel pulsando em volta do icone da bandeja, durante as boas-vindas. O
/// icone e' do Windows (nao da para acende-lo por dentro): isto e' uma
/// janelinha transparente por cima, que deixa o clique passar e nao rouba o foco.
/// </summary>
internal sealed class TrayRingWindow : Window
{
    public TrayRingWindow(Native.PixelRect icon, Brush ring)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Content = new Border { BorderBrush = ring, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(6) };

        const int pad = 3;
        var at = new Native.PixelRect(icon.Left - pad, icon.Top - pad, icon.Right + pad, icon.Bottom + pad);
        SourceInitialized += (_, _) =>
        {
            Native.MakeToolWindow(this, noActivate: true, clickThrough: true);
            Native.PlaceWindow(this, at);
        };
    }
}
