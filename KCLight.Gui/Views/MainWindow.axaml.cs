using Avalonia.Controls;
using Avalonia.Input;
using Serilog;

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
}