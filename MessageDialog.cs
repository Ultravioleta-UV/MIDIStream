using Avalonia.Controls;
using Avalonia.Layout;

namespace MidiShortcuts;

/// <summary>Avalonia no trae MessageBox; este es un aviso simple con boton Aceptar.</summary>
internal static class MessageDialog
{
    public static async Task ShowAsync(Window owner, string title, string message)
    {
        var okBtn = new Button
        {
            Content = "Aceptar",
            MinWidth = 90,
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };

        var win = new Window
        {
            Title = title,
            Width = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    okBtn,
                },
            },
        };

        okBtn.Click += (_, _) => win.Close();
        await win.ShowDialog(owner);
    }
}
