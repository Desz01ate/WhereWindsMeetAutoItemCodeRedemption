using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace WhereWindsMeetItemCodeRedeemer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!IsRunningAsAdministrator())
        {
            try
            {
                var exePath = Environment.ProcessPath ??
                              Process.GetCurrentProcess().MainModule?.FileName;

                if (!string.IsNullOrEmpty(exePath))
                {
                    var processInfo = new ProcessStartInfo
                    {
                        UseShellExecute = true,
                        FileName = exePath,
                        Verb = "runas"
                    };

                    Process.Start(processInfo);
                }
            }
            catch
            {
                MessageBox.Show(
                    "This application requires Administrator privileges to detect and interact with Where Winds Meet.\n\nPlease right-click the application and select 'Run as administrator'.",
                    "Administrator Privileges Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            Shutdown();
            return;
        }
    }

    public static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
