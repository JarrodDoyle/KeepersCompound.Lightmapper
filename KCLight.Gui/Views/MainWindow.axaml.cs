using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using KCLight.Gui.ViewModels;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace KCLight.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        MissionNameBox.GotFocus += OnAutocompleteFocus;
        CampaignNameBox.GotFocus += OnAutocompleteFocus;
    }

    private static void OnAutocompleteFocus(object? sender, GotFocusEventArgs e)
    {
        if (sender is AutoCompleteBox box)
        {
            box.IsDropDownOpen = true;
            e.Handled = true;
        }
    }

    private void Control_OnLoaded(object? sender, RoutedEventArgs e)
    {
        const string outputTemplate = "{Timestamp:HH:mm:ss.fff} [{Level}] {Message:lj}{NewLine}{Exception}";
        var logPath = $"{AppDomain.CurrentDomain.BaseDirectory}/logs/{DateTime.Now:yyyyMMdd_HHmmss}.log";
        var config = new LoggerConfiguration();
#if DEBUG
        config.MinimumLevel.Debug();
#endif

        Log.Logger = config
            .WriteTo.Console(theme: AnsiConsoleTheme.Sixteen, outputTemplate: outputTemplate)
            .WriteTo.File(logPath, outputTemplate: outputTemplate)
            .WriteTo.Observers(events => events.Subscribe((DataContext as MainWindowViewModel)!))
            .CreateLogger();

        e.Handled = true;
    }

    private void Control_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        LogScrollViewer.ScrollToEnd();
    }
}