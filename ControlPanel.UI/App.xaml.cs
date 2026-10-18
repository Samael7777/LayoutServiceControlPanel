using System.IO;
using System.Windows;
using ControlPanel.Core;
using ControlPanel.Core.Services;
using ControlPanel.UI.ViewModels;
using LayoutTools.Managed;

namespace ControlPanel.UI;

public partial class App : Application
{
    private SwitcherTaskService? _taskService;
    private MainViewModel? _viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Composition root. Services are created on the UI thread on purpose:
        // SwitcherTaskService captures SynchronizationContext.Current and raises its events there.
        var switcherPath = Path.Combine(AppContext.BaseDirectory, AppConstants.WitcherServiceExe);

        _taskService = new SwitcherTaskService(switcherPath);

        var layoutsService = new CycledLayoutsService(Layouts.GetKeyboardLayoutList);

        _viewModel = new MainViewModel(_taskService, layoutsService);

        var window = new MainWindow { DataContext = _viewModel };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _viewModel?.Dispose();
        _taskService?.Dispose();
        base.OnExit(e);
    }
}
