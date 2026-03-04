using Microsoft.Extensions.DependencyInjection;
using Oracle.ManagedDataAccess.Client;
using GestionMC.Desktop.Infrastructure;
using GestionMC.Desktop.Models;
using GestionMC.Desktop.Services;
using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace GestionMC.Desktop;

public partial class RetencionDetalleWindow : Window
{
    private readonly IOracleFacturaPagoService _service;
    private readonly string _coNumero;
    private readonly List<RetencionDetalleRow>? _preloaded;
    private readonly string _proveedor;
    private readonly string _numeroFactura;

    public RetencionDetalleWindow(string coNumero, List<RetencionDetalleRow>? preload = null, string proveedor = "", string numeroFactura = "")
    {
        InitializeComponent();
        _service = AppHost.Services.GetRequiredService<IOracleFacturaPagoService>();
        _coNumero = coNumero;
        _preloaded = preload;
        _proveedor = proveedor;
        _numeroFactura = numeroFactura;
        TxtCompra.Text = _coNumero;
        TxtProveedor.Text = _proveedor;
        TxtFactura.Text = _numeroFactura;
        Loaded += async (_, _) => await CargarAsync();
    }

    private async Task CargarAsync()
    {
        try
        {
            LblStatus.Text = "Cargando retención...";

            var data = _preloaded ?? await Task.Run(() => _service.GetRetencionDetalle(_coNumero));

            ApplyData(data);
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

    private void ApplyData(List<RetencionDetalleRow> data)
    {
        GridRetencion.ItemsSource = data;

        if (data == null || data.Count == 0)
        {
            SetResumen(null);
            LblStatus.Text = "No se encontraron registros de retención.";
            return;
        }

        SetResumen(data.First());
        LblStatus.Text = $"Registros: {data.Count}";
    }

    private void SetResumen(RetencionDetalleRow? row)
    {
        if (row == null)
        {
            TxtSubtotal.Text = "";
            TxtIva.Text = "";
            TxtBase15.Text = "";
            TxtTotal.Text = "";
            TxtRetencion.Text = "";
            return;
        }

        TxtSubtotal.Text = Format(row.Subtotal);
        TxtIva.Text = Format(row.Iva);
        TxtBase15.Text = Format(row.Base15);
        TxtTotal.Text = Format(row.Total);
        TxtRetencion.Text = row.PmNrosecDisplay;
    }

    private static string Format(decimal value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
