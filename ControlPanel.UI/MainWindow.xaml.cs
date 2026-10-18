using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ControlPanel.UI;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();

        // Follow the Windows light/dark theme and keep the Mica backdrop in sync.
        SystemThemeWatcher.Watch(this);
    }

    /// <summary>View-only concern: put the keyboard focus into the search box when the key picker opens.</summary>
    private void KeyPopup_Opened(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(
            () =>
            {
                KeySearchBox.Focus();
                Keyboard.Focus(KeySearchBox);
            },
            DispatcherPriority.Input);
    }
}
