using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using GestionMC.Desktop.Infrastructure;
using GestionMC.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionMC.Desktop;

public partial class EmitidosWindow : Window
{
    private readonly ISriEmitidosService _emitidosService;
    private readonly IStoragePathProvider _storagePathProvider;

    public EmitidosWindow()
    {
        InitializeComponent();
        _emitidosService = AppHost.Services.GetRequiredService<ISriEmitidosService>();
        _storagePathProvider = AppHost.Services.GetRequiredService<IStoragePathProvider>();
        CargarOpciones();
    }

    private void CargarOpciones()
    {
        var now = DateTime.Now;
        var years = Enumerable.Range(now.Year - 3, 7).ToList();
        CmbYear.ItemsSource = years;
        CmbYear.SelectedItem = now.Year;

        var months = Enumerable.Range(1, 12)
            .Select(m => new { Value = m, Name = CultureInfo.GetCultureInfo("es-ES").DateTimeFormat.GetMonthName(m).ToUpperInvariant() })
            .ToList();
        CmbMonth.ItemsSource = months;
        CmbMonth.SelectedValuePath = "Value";
        CmbMonth.DisplayMemberPath = "Name";
        CmbMonth.SelectedValue = now.Month;
    }

    private async void BtnDescargar_Click(object sender, RoutedEventArgs e)
    {
        BtnDescargar.IsEnabled = false;
        LblStatus.Text = "Procesando...";
        TxtLog.Clear();

        try
        {
            if (CmbYear.SelectedItem is not int year)
            {
                LblStatus.Text = "Seleccione un año.";
                return;
            }

            var month = (CmbMonth.SelectedValue as int?) ?? 0;
            if (month <= 0)
            {
                LblStatus.Text = "Seleccione un mes.";
                return;
            }

            var storage = _storagePathProvider.GetStoragePath();
            var progress = new Progress<string>(msg =>
            {
                LblStatus.Text = msg;
                AppendLog(msg);
            });

            var total = await Task.Run(() => _emitidosService.DownloadRetencionesByMonthAsync(storage, year, month, progress));
            LblStatus.Text = $"Listo. Descargados {total} archivos.";
        }
        catch (Exception ex)
        {
            LblStatus.Text = "Error: " + ex.Message;
            AppendLog(ex.ToString());
        }
        finally
        {
            BtnDescargar.IsEnabled = true;
        }
    }

    private void AppendLog(string message)
    {
        Dispatcher.Invoke(() =>
        {
            TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            TxtLog.ScrollToEnd();
        });
    }
}
