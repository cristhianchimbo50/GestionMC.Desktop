using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using GestionMC.Desktop.Models;
using GestionMC.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;
using GestionMC.Desktop.Infrastructure;

namespace GestionMC.Desktop;

public partial class FacturasMesWindow : Window
{
    private readonly DisabledProvidersStore _disabledStore;
    private readonly IOracleFacturaPagoService _facturaPagoService;
    private readonly IOracleProveedorService _proveedorService;
    private HashSet<string> _proveedoresRuc = new(StringComparer.OrdinalIgnoreCase);
    private List<FacturaPagoRow> _facturasBd = new();
    private readonly List<SriReceivedRow> _rows = new();

    public FacturasMesWindow()
    {
        InitializeComponent();
        _disabledStore = AppHost.Services.GetRequiredService<DisabledProvidersStore>();
        _facturaPagoService = AppHost.Services.GetRequiredService<IOracleFacturaPagoService>();
        _proveedorService = AppHost.Services.GetRequiredService<IOracleProveedorService>();
        GridFacturas.CanUserResizeColumns = false;
        GridFacturas.CanUserResizeRows = false;
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

    private async void BtnCargar_Click(object sender, RoutedEventArgs e)
    {
        await CargarMesAsync();
    }

    private async Task CargarMesAsync()
    {
        LblStatus.Text = "Cargando...";
        GridFacturas.ItemsSource = null;
        _rows.Clear();

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

        try
        {
            await CargarFacturasBdAsync();
            await CargarProveedoresAsync();

            var baseFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GestionMC",
                "Xml",
                year.ToString("D4"));

            if (!Directory.Exists(baseFolder))
            {
                LblStatus.Text = "No hay datos para el año seleccionado.";
                return;
            }

            string? monthDir = Directory.GetDirectories(baseFolder)
                .FirstOrDefault(d => ParseMonthPrefix(Path.GetFileName(d)) == month);

            if (string.IsNullOrWhiteSpace(monthDir) || !Directory.Exists(monthDir))
            {
                LblStatus.Text = "No hay datos para el mes seleccionado.";
                return;
            }

            var culture = new CultureInfo("es-ES");

            var dayDirs = Directory.GetDirectories(monthDir)
                .Select(d => new
                {
                    Path = d,
                    Date = DateTime.TryParseExact(Path.GetFileName(d), "dd-MM-yyyy", culture, DateTimeStyles.None, out var dt)
                        ? dt
                        : (DateTime?)null
                })
                .OrderBy(x => x.Date ?? DateTime.MaxValue)
                .Select(x => x.Path)
                .ToList();

            int idx = 1;

            foreach (var dayDir in dayDirs)
            {
                foreach (var file in Directory.GetFiles(dayDir, "*.xml", SearchOption.AllDirectories))
                {
                    try
                    {
                        var xml = await File.ReadAllTextAsync(file);
                        var parsed = SriXmlParser.ParseFacturaAutorizada(xml, file).header;

                        if (_disabledStore.IsDisabled(parsed.RucEmisor))
                            continue;

                        _rows.Add(new SriReceivedRow
                        {
                            Nro = idx++,
                            NumeroFactura = parsed.NumeroFactura,
                            RazonSocialEmisor = parsed.RazonSocialEmisor,
                            RucEmisor = parsed.RucEmisor,
                            FechaEmision = parsed.FechaEmision.ToString("yyyy-MM-dd", culture),
                            XmlPath = file,
                            ClaveAcceso = parsed.ClaveAcceso,
                            ProveedorExiste = _proveedoresRuc.Contains(parsed.RucEmisor)
                        });
                    }
                    catch
                    {
                    }
                }
            }

            EnlazarConFacturasBd();
            GridFacturas.ItemsSource = _rows;
            LblStatus.Text = _rows.Count > 0 ? $"Total registros: {_rows.Count}" : "No se encontraron XML en el mes seleccionado.";
        }
        catch (Exception ex)
        {
            LblStatus.Text = "Error: " + ex.Message;
        }
    }

    private void EnlazarConFacturasBd()
    {
        foreach (var r in _rows)
        {
            var numeroSinGuiones = (r.NumeroFactura ?? "").Replace("-", "");
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
                r.CoNumero = string.Empty;
                r.PmNrosec = string.Empty;
            }
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

    private void BtnView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.DataContext is not SriReceivedRow row) return;
        if (string.IsNullOrWhiteSpace(row.XmlPath) || !File.Exists(row.XmlPath))
        {
            LblStatus.Text = "No existe el XML para esta fila.";
            return;
        }

        var win = new InvoiceDetailWindow(row)
        {
            Owner = this
        };
        win.Show();
    }

    private static int ParseMonthPrefix(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        var parts = name.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return 0;
        return int.TryParse(parts[0], out var m) ? m : 0;
    }
}
