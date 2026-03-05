using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Oracle.ManagedDataAccess.Client;
using GestionMC.Desktop.Infrastructure;
using GestionMC.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionMC.Desktop;

public partial class ColoresWindow : Window
{
    private readonly IOracleColorService _colorService;
    private CancellationTokenSource? _cts;

    public ColoresWindow()
    {
        InitializeComponent();
        _colorService = AppHost.Services.GetRequiredService<IOracleColorService>();
        GridColores.CanUserResizeColumns = false;
        GridColores.CanUserResizeRows = false;
    }

    private async Task BuscarAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            LblStatus.Text = "Buscando...";

            var cedula = TxtCedula.Text?.Trim();
            var color = TxtColor.Text?.Trim();
            var nombre = TxtNombre.Text?.Trim();
            var desde = DpDesde.SelectedDate;
            var hasta = DpHasta.SelectedDate;

            if (desde.HasValue && hasta.HasValue && hasta < desde)
            {
                LblStatus.Text = "Rango de fechas inválido (hasta es menor que desde).";
                return;
            }

            var data = await Task.Run(() => _colorService.BuscarColores(cedula, color, nombre, desde, hasta), cancellationToken);

            if (cancellationToken.IsCancellationRequested)
                return;

            GridColores.ItemsSource = data;
            LblStatus.Text = $"Total registros: {data.Count}";
        }
        catch (OracleException ex)
        {
            LblStatus.Text = $"Error Oracle ORA-{ex.Number}: {ex.Message}";
        }
        catch (Exception ex)
        {
            LblStatus.Text = "Error: " + ex.Message;
        }
    }

    private void BtnBuscar_Click(object sender, RoutedEventArgs e)
    {
        TriggerAutoBuscar();
    }

    private void BtnLimpiar_Click(object sender, RoutedEventArgs e)
    {
        TxtCedula.Text = string.Empty;
        TxtColor.Text = string.Empty;
        TxtNombre.Text = string.Empty;
        DpDesde.SelectedDate = null;
        DpHasta.SelectedDate = null;
        GridColores.ItemsSource = null;
        LblStatus.Text = "Listo.";
    }

    private void TxtFiltro_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        TriggerAutoBuscar();
    }

    private void TriggerAutoBuscar()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        _ = BuscarAsync(token);
    }
}
