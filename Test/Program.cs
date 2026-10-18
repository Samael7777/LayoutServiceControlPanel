using Microsoft.Win32.TaskScheduler;
using static System.Console;

namespace Test;

internal class Program
{
    private const string TaskPath = $@"\{AppConstants.SwitcherTaskName}";
    static void Main(string[] args)
    {
        var ts = TaskService.Instance ?? throw new InvalidOperationException();
        var t = ts.GetTask(TaskPath);

        ReadKey();
    }
}