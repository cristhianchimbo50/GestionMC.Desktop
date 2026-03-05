using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using GestionMC.Desktop.Infrastructure;
using GestionMC.Desktop.Models;
using GestionMC.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionMC.Desktop;

public partial class RecibidosPaginadoWindow : Window
{
    private readonly ISriRecibidosPaginadoService _service;
    private readonly IStoragePathProvider _storagePathProvider;
    private readonly IOracleProveedorService _proveedorService;
    private readonly IOracleFacturaPagoService _facturaPagoService;
    private readonly DisabledProvidersStore _disabledStore;

    private HashSet<string> _proveedoresRuc = new(StringComparer.OrdinalIgnoreCase);
    private List<FacturaPagoRow> _facturasBd = new();
    private readonly List<SriReceivedRow> _rows = new();

    public RecibidosPaginadoWindow()
    {
        InitializeComponent();
        _service = AppHost.Services.GetRequiredService<ISriRecibidosPaginadoService>();
        _storagePathProvider = AppHost.Services.GetRequiredService<IStoragePathProvider>();
        _proveedorService = AppHost.Services.GetRequiredService<IOracleProveedorService>();
        _facturaPagoService = AppHost.Services.GetRequiredService<IOracleFacturaPagoService>();
        _disabledStore = AppHost.Services.GetRequiredService<DisabledProvidersStore>();
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
        CmbMonth.DisplayMemberPath = "Name";
        CmbMonth.SelectedValuePath = "Value";
        CmbMonth.SelectedValue = now.Month;
    }

    private async void BtnDescargar_Click(object sender, RoutedEventArgs e)
    {
        BtnDescargar.IsEnabled = false;
        LblStatus.Text = "Descargando...";
        GridRecibidos.ItemsSource = null;
        _rows.Clear();

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
            var data = await Task.Run(() => _service.DownloadAllPagedAsync(storage, year, month, 0, "1"));

            await CargarProveedoresAsync();
            await CargarFacturasBdAsync();

            var clavesAgregadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int idx = 1;
            foreach (var r in data)
            {
                if (_disabledStore.IsDisabled(r.RucEmisor))
                    continue;

                if (!string.IsNullOrWhiteSpace(r.ClaveAcceso) && !clavesAgregadas.Add(r.ClaveAcceso))
                    continue;

                r.Nro = idx++;
                r.ProveedorExiste = _proveedoresRuc.Contains(r.RucEmisor ?? string.Empty);

                var numeroFactura = r.NumeroFactura ?? string.Empty;
                var numeroSinGuiones = numeroFactura.Replace("-", "");
                var match = _facturasBd.FirstOrDefault(f => string.Equals(f.CoFacpro, numeroSinGuiones, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    r.CoNumero = match.CoNumero;
                    r.CoFacpro = match.CoFacpro;
                    r.PmNrosec = match.PmNrosec;
                    r.PvRucciBd = match.PvRucci;
                    r.PvRazonsBd = match.PvRazons;
                    r.RfCodigo = match.RfCodigo;
                    r.RfCodigo2 = match.RfCodigo2;
                    r.CoincideConBd = true;
                }
                else
                {
                    r.CoincideConBd = false;
                }

                _rows.Add(r);
            }

            GridRecibidos.ItemsSource = _rows;
            LblStatus.Text = _rows.Count > 0 ? $"Listo. {_rows.Count} registros." : "Sin datos.";
        }
        catch (Exception ex)
        {
            LblStatus.Text = "Error: " + ex.Message;
        }
        finally
        {
            BtnDescargar.IsEnabled = true;
        }
    }

    private void BtnView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.DataContext is not SriReceivedRow row) return;
        if (string.IsNullOrWhiteSpace(row.XmlPath) || !File.Exists(row.XmlPath))
        {
            LblStatus.Text = "No existe el XML para esta fila.";
            return;
        }

        var win = new InvoiceDetailWindow(row);
        win.Owner = this;
        win.Show();
    }

    private async Task CargarProveedoresAsync()
    {
        try
        {
            var proveedores = await Task.Run(() => _proveedorService.GetProveedores());
            _proveedoresRuc = new HashSet<string>(proveedores.Select(p => p.RucCi), StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _proveedoresRuc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task CargarFacturasBdAsync()
    {
        try
        {
            _facturasBd = await Task.Run(() => _facturaPagoService.GetFacturasPago());
        }
        catch
        {
            _facturasBd = new List<FacturaPagoRow>();
        }
    }
}
