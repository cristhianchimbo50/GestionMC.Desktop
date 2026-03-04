using Microsoft.Extensions.DependencyInjection;
using Oracle.ManagedDataAccess.Client;
using GestionMC.Desktop.Infrastructure;
using GestionMC.Desktop.Services;
using System;
using System.Windows;

namespace GestionMC.Desktop;

public partial class ProveedoresWindow : Window
{
    private readonly IOracleProveedorService _proveedorService;

    public ProveedoresWindow()
    {
        InitializeComponent();
        _proveedorService = AppHost.Services.GetRequiredService<IOracleProveedorService>();
    }

    private void BtnLoad_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            LblStatus.Text = "Probando conexión...";

            _proveedorService.GetProveedores(testOnly: true);

            LblStatus.Text = "Conexión OK. Cargando proveedores...";

            var proveedores = _proveedorService.GetProveedores();

            GridProveedores.ItemsSource = proveedores;
            LblStatus.Text = $"Proveedores cargados: {proveedores.Count}";
        }
        catch (OracleException ex)
        {
            LblStatus.Text = $"Error Oracle ORA-{ex.Number}: {ex.Message}";
        }
        catch (Exception ex)
        {
            LblStatus.Text = $"Error general: {ex.Message}";
        }
    }
}
