using Microsoft.Extensions.DependencyInjection;
using Oracle.ManagedDataAccess.Client;
using GestionMC.Desktop.Infrastructure;
using GestionMC.Desktop.Models;
using GestionMC.Desktop.Services;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

namespace GestionMC.Desktop;

public partial class FacturasPagoWindow : Window
{
    private readonly IOracleFacturaPagoService _facturaPagoService;

    public FacturasPagoWindow()
    {
        InitializeComponent();
        _facturaPagoService = AppHost.Services.GetRequiredService<IOracleFacturaPagoService>();
        GridFacturas.CanUserResizeColumns = false;
        GridFacturas.CanUserResizeRows = false;
        _ = CargarAsync();
    }

    private async Task CargarAsync()
    {
        try
        {
            LblStatus.Text = "Cargando facturas desde Oracle...";

            var data = await Task.Run(() => _facturaPagoService.GetFacturasPago());

            GridFacturas.ItemsSource = data;
            LblStatus.Text = $"Total facturas: {data.Count}";
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

    private void BtnRefrescar_Click(object sender, RoutedEventArgs e)
    {
        _ = CargarAsync();
    }
}
