using ControlPanel.Core.Services;

namespace ControlPanel.UI.ViewModels;

/// <summary>Root view model of the single application window.</summary>
public sealed class MainViewModel : IDisposable
{
    public MainViewModel(SwitcherTaskService taskService, CycledLayoutsService layoutsService)
    {
        Service = new ServiceViewModel(taskService);
        Layouts = new LayoutsViewModel(layoutsService);
        Hotkey = new HotkeyViewModel();
    }

    public ServiceViewModel Service { get; }

    public LayoutsViewModel Layouts { get; }

    public HotkeyViewModel Hotkey { get; }

    public void Dispose()
    {
        Service.Dispose();
        Layouts.Dispose();
    }
}
