using System.Windows;
using GestionMC.Desktop.Infrastructure;

namespace GestionMC.Desktop
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            AppHost.Initialize();
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            AppHost.Dispose();
            base.OnExit(e);
        }
    }

}
