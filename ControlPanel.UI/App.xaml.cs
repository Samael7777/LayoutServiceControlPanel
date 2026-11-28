using System.IO;
using System.Windows;
using ControlPanel.Core;
using ControlPanel.Core.Services;
using ControlPanel.UI.ViewModels;
using LayoutTools.Managed;
using PhoenixTools.Watchers;

namespace ControlPanel.UI;

public partial class App
{
    private SwitcherTaskService? _taskService;
    private MainViewModel? _viewModel;
    private RegistryWatcher? _watcher;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Composition root. Services are created on the UI thread on purpose:
        // SwitcherTaskService captures SynchronizationContext.Current and raises its events there.
        var switcherPath = Path.Combine(AppContext.BaseDirectory, AppConstants.WitcherServiceExe);

        _taskService = new SwitcherTaskService(switcherPath);

        var layoutsService = new CycledLayoutsService(Layouts.GetKeyboardLayoutList);

        _viewModel = new MainViewModel(_taskService, layoutsService);

        _watcher = new RegistryWatcher(AppConstants.ApplicationRegRoot, AppConstants.ApplicationRegSubkey, false);
        _watcher.RegistryChanged += (_, _) => { _viewModel.Refresh(); };
        _watcher.Start();

        var window = new MainWindow { DataContext = _viewModel };
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _watcher?.Dispose();
        _viewModel?.Dispose();
        _taskService?.Dispose();
        base.OnExit(e);
    }
}
