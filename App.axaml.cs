using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using System;
using System.Linq;

namespace map_app;

public partial class App : Application
{
    private bool _openEditor;

    public override void Initialize()
    {
        _openEditor = Environment.GetCommandLineArgs()
            .Any(argument => string.Equals(argument, "--editor", StringComparison.OrdinalIgnoreCase));

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _openEditor ? new MapView() : new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
