using Microsoft.Extensions.DependencyInjection;
using GestionMC.Desktop.Infrastructure;
using GestionMC.Desktop.Models;
using GestionMC.Desktop.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GestionMC.Desktop;

public partial class InvoiceDetailWindow : Window
{
    private readonly string _xmlPath;
    private readonly string _coNumero;
    private readonly string _proveedorNombre;
    private readonly string _numeroFactura;
    private readonly IOracleFacturaPagoService _facturaPagoService;

    public InvoiceDetailWindow(SriReceivedRow row)
    {
        InitializeComponent();
        GridItems.PreviewKeyDown += GridItems_PreviewKeyDown;

        _xmlPath = row.XmlPath;
        _coNumero = row.CoNumero;
        _proveedorNombre = row.RazonSocialEmisor;
        _numeroFactura = row.NumeroFactura;

        _facturaPagoService = AppHost.Services.GetRequiredService<IOracleFacturaPagoService>();

        LoadXml();
    }

    private void LoadXml()
    {
        var xmlContent = File.ReadAllText(_xmlPath);
        var (header, items) = SriXmlParser.ParseFacturaAutorizada(xmlContent, _xmlPath);

        var groupedItems = items
            .Select((item, index) => new { item, index, key = NormalizeCodeKey(item) })
            .GroupBy(x => string.IsNullOrWhiteSpace(x.key) ? $"__NO_KEY__{x.index}" : x.key, x => x.item)
            .Select(g => BuildAggregatedItem(g))
            .ToList();

        var numeroFacturaSinGuiones = (header.NumeroFactura ?? string.Empty).Replace("-", "");

        TxtRazonSocial.Text = header.RazonSocialEmisor;
        TxtNombreComercial.Text = header.NombreComercialEmisor;
        TxtRuc.Text = header.RucEmisor;
        TxtFactura.Text = numeroFacturaSinGuiones;
        TxtFechaEmision.Text = header.FechaEmision.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        TxtTotal.Text = header.Total.ToString("0.##", CultureInfo.InvariantCulture);

        TxtAutorizacion.Text = header.NumeroAutorizacion;
        TxtFechaAutorizacion.Text = header.FechaAutorizacion.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        TxtClave.Text = header.ClaveAcceso;

        GridItems.ItemsSource = groupedItems;
        AdjustGridItemsHeight(groupedItems.Count);

        GridTotals.ItemsSource = new[]
        {
            new { Label = "SUBTOTAL 15%", Value = header.Subtotal15 },
            new { Label = "SUBTOTAL NO OBJETO DE IVA", Value = header.SubtotalNoObjetoIva },
            new { Label = "SUBTOTAL EXENTO DE IVA", Value = header.SubtotalExentoIva },
            new { Label = "SUBTOTAL SIN IMPUESTOS", Value = header.SubtotalSinImpuestos },
            new { Label = "TOTAL DESCUENTO", Value = header.TotalDescuento },
            new { Label = "IVA 15%", Value = header.Iva15 },
            new { Label = "VALOR TOTAL", Value = header.Total }
        };
    }

    private void AdjustGridItemsHeight(int rowCount)
    {
        if (GridItems == null)
            return;

        const int maxRowsVisible = 10;
        var visibleRows = Math.Min(Math.Max(rowCount, 1), maxRowsVisible);

        double rowHeight = GridItems.RowHeight > 0 ? GridItems.RowHeight : 25;
        double headerHeight = double.IsNaN(GridItems.ColumnHeaderHeight) ? 25 : GridItems.ColumnHeaderHeight;

        GridItems.Height = headerHeight + (rowHeight * visibleRows) + 2;
    }

    private void BtnVerRetencion_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!_facturaPagoService.ExisteCompra(_coNumero))
            {
                MessageBox.Show("Compra no registrada.", "Ver compra", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var data = _facturaPagoService.GetRetencionDetalle(_coNumero);

            if (data.Count == 0)
            {
                MessageBox.Show("Compra no registrada.", "Ver compra", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var win = new RetencionDetalleWindow(_coNumero, data, _proveedorNombre, _numeroFactura)
            {
                Owner = this
            };

            win.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error al obtener la compra: " + ex.Message, "Ver compra", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void GridItems_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GridItems.SelectedItem == null)
        {
            var dep = (DependencyObject)e.OriginalSource;
            while (dep != null && dep is not DataGridRow)
                dep = VisualTreeHelper.GetParent(dep);

            if (dep is DataGridRow row)
                GridItems.SelectedItem = row.Item;
        }

        GridItems.SelectionUnit = DataGridSelectionUnit.FullRow;
    }

    private static string NormalizeCodeKey(SriInvoiceItem item)
    {
        var principal = item.CodigoPrincipal?.Trim();
        var aux = item.CodigoAuxiliar?.Trim();
        var key = string.IsNullOrWhiteSpace(principal) ? aux : principal;
        return key?.ToUpperInvariant() ?? string.Empty;
    }

    private static decimal SafeDivide(decimal numerator, decimal denominator)
    {
        return denominator == 0 ? 0 : numerator / denominator;
    }

    private static SriInvoiceItem BuildAggregatedItem(IGrouping<string, SriInvoiceItem> group)
    {
        var first = group.First();

        var cantidadSum = group.Sum(x => x.Cantidad);

        var totalLineaSum = group.Sum(x => x.TotalLinea);
        var descuentoTotalSum = group.Sum(x => x.Descuento);
        var baseImponibleSum = group.Sum(x => x.BaseImponible);
        var ivaSum = group.Sum(x => x.Iva);

        var precioUnitarioXml = SafeDivide(group.Sum(x => x.PrecioUnitario * x.Cantidad), cantidadSum);
        var precioUnitarioConDesc = SafeDivide(totalLineaSum, cantidadSum);

        var descuentoUnitario = precioUnitarioXml - precioUnitarioConDesc;

        return new SriInvoiceItem
        {
            CodigoPrincipal = first.CodigoPrincipal,
            CodigoAuxiliar = first.CodigoAuxiliar,
            Descripcion = first.Descripcion,

            Cantidad = cantidadSum,

            PrecioUnitario = precioUnitarioXml,
            PrecioUnitarioConDescuento = precioUnitarioConDesc,

            Descuento = descuentoTotalSum,
            DescuentoUnitario = descuentoUnitario,

            BaseImponible = baseImponibleSum,
            Iva = ivaSum,

            TotalLinea = totalLineaSum
        };
    }

    private static bool TryParseDecimal(string text, out decimal value)
    {
        text = (text ?? string.Empty).Trim();

        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            return true;

        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out value))
            return true;

        return false;
    }

    private static string FormatForClipboard(string text)
    {
        if (TryParseDecimal(text, out var dec))
            return dec.ToString("0.##", CultureInfo.InvariantCulture);

        return (text ?? string.Empty).Trim();
    }

    private void GridItems_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            CopySelectionToClipboard();
        }
    }

    private void CopySelectionToClipboard()
    {
        // Copiar solo la celda actual (o la primera seleccionada) para evitar copiar filas completas
        DataGridCellInfo? cellInfo = GridItems.CurrentCell;

        if (cellInfo == null || cellInfo.Value.Column == null)
        {
            if (GridItems.SelectedCells == null || GridItems.SelectedCells.Count == 0)
                return;

            cellInfo = GridItems.SelectedCells[0];
        }

        var cell = cellInfo.Value;
        if (cell.Column == null || cell.Item == null)
            return;

        var raw = cell.Column.GetCellContent(cell.Item);

        string text = raw switch
        {
            TextBlock tb => tb.Text,
            TextBox tx => tx.Text,
            _ => cell.Column.OnCopyingCellClipboardContent(cell.Item)?.ToString() ?? ""
        };

        var finalText = FormatForClipboard(text);

        var dataObj = new DataObject();
        dataObj.SetData(DataFormats.UnicodeText, finalText);
        dataObj.SetData(DataFormats.Text, finalText);
        dataObj.SetData(DataFormats.OemText, finalText);

        Clipboard.SetDataObject(dataObj, true);
    }
}