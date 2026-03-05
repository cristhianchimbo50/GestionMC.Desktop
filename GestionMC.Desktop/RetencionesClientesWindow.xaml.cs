using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;
using GestionMC.Desktop.Configuration;
using GestionMC.Desktop.Infrastructure;
using GestionMC.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionMC.Desktop;

public partial class RetencionesClientesWindow : Window
{
    private readonly ISriRecibidosPaginadoService _receivedRetentionService;
    private readonly IStoragePathProvider _storagePathProvider;
    private readonly AppSettings _settings;
    private readonly Func<ISriSessionService> _sessionFactory;
    private readonly string _retentionBaseFolder;

    private readonly ObservableCollection<CustomerRetentionRow> _retentionRows = new();

    private Button DownloadButton => BtnDescargar;
    private Button GenerateSessionButton => BtnGenerarSesion;
    private ComboBox YearComboBox => CmbYear;
    private ComboBox MonthComboBox => CmbMonth;
    private DataGrid RetentionGrid => GridRetenciones;
    private TreeView DatesTree => TreeFechas;
    private TextBlock StatusLabel => LblStatus;

    public RetencionesClientesWindow()
    {
        InitializeComponent();
        _receivedRetentionService = AppHost.Services.GetRequiredService<ISriRecibidosPaginadoService>();
        _storagePathProvider = AppHost.Services.GetRequiredService<IStoragePathProvider>();
        _settings = AppHost.Services.GetRequiredService<AppSettings>();
        _sessionFactory = AppHost.Services.GetRequiredService<Func<ISriSessionService>>();
        _retentionBaseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GestionMC", "Retenciones clientes");
        Title = "Customer retentions";
        StatusLabel.Text = "Ready.";
        DownloadButton.Content = "Download";
        GenerateSessionButton.Content = "Save session";
        LoadOptions();
        Loaded += (_, _) => LoadTree();
        RetentionGrid.ItemsSource = _retentionRows;
        SetGridColumnHeaders();
    }

    private void LoadOptions()
    {
        var now = DateTime.Now;
        var years = Enumerable.Range(now.Year - 3, 7).ToList();
        YearComboBox.ItemsSource = years;
        YearComboBox.SelectedItem = now.Year;

        var months = Enumerable.Range(1, 12)
            .Select(m => new { Value = m, Name = CultureInfo.GetCultureInfo("en-US").DateTimeFormat.GetMonthName(m).ToUpperInvariant() })
            .ToList();
        MonthComboBox.ItemsSource = months;
        MonthComboBox.DisplayMemberPath = "Name";
        MonthComboBox.SelectedValuePath = "Value";
        MonthComboBox.SelectedValue = now.Month;
    }

    private void SetGridColumnHeaders()
    {
        if (RetentionGrid.Columns.Count >= 6)
        {
            RetentionGrid.Columns[0].Header = "Date";
            RetentionGrid.Columns[1].Header = "RUC";
            RetentionGrid.Columns[2].Header = "Customer";
            RetentionGrid.Columns[3].Header = "Receipt No.";
            RetentionGrid.Columns[4].Header = "Total amount";
            RetentionGrid.Columns[5].Header = "Supporting doc.";
        }
    }

    private void BtnDescargar_Click(object sender, RoutedEventArgs e) => DownloadButton_Click(sender, e);

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        DownloadButton.IsEnabled = false;
        StatusLabel.Text = "Downloading...";

        try
        {
            if (YearComboBox.SelectedItem is not int year)
            {
                StatusLabel.Text = "Select a year.";
                return;
            }

            var month = (MonthComboBox.SelectedValue as int?) ?? 0;
            if (month <= 0)
            {
                StatusLabel.Text = "Select a month.";
                return;
            }

            var storage = _storagePathProvider.GetStoragePath();
            var progress = new Progress<string>(msg => StatusLabel.Text = msg);

            var count = await Task.Run(() => _receivedRetentionService.DownloadRetencionesClientesAsync(storage, year, month, progress));
            StatusLabel.Text = count > 0
                ? $"Done. Downloaded {count} XML files."
                : "No data to download.";
            if (count > 0)
                LoadTree();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Error: " + ex.Message;
        }
        finally
        {
            DownloadButton.IsEnabled = true;
        }
    }

    private void BtnGenerarSesion_Click(object sender, RoutedEventArgs e) => GenerateSessionButton_Click(sender, e);

    private async void GenerateSessionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            GenerateSessionButton.IsEnabled = false;
            StatusLabel.Text = "Starting SRI session...";

            var session = _sessionFactory();
            await session.OpenLoginAutoSaveAndCloseAsync(_settings.SriCredentials.User, _settings.SriCredentials.Password);
            await session.DisposeAsync();

            StatusLabel.Text = "Session saved and browser closed. You can download now.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Error: " + ex.Message;
        }
        finally
        {
            GenerateSessionButton.IsEnabled = true;
        }
    }

    private void LoadTree()
    {
        DatesTree.Items.Clear();

        if (!Directory.Exists(_retentionBaseFolder))
        {
            StatusLabel.Text = "No retentions found.";
            return;
        }

        var folderInfos = Directory
            .GetFiles(_retentionBaseFolder, "*.xml", SearchOption.AllDirectories)
            .Select(f => BuildFolderInfo(f))
            .Where(x => x != null)
            .GroupBy(x => x!.Year)
            .OrderBy(g => g.Key);

        foreach (var yearGroup in folderInfos)
        {
            var yearItem = new TreeViewItem { Header = yearGroup.Key.ToString("D4") };

            foreach (var monthGroup in yearGroup.GroupBy(x => x!.Month).OrderBy(g => g.Key))
            {
                var monthHeader = monthGroup.First()?.MonthFolderName ?? monthGroup.Key.ToString();
                var monthItem = new TreeViewItem { Header = monthHeader };

                foreach (var supplierGroup in monthGroup.GroupBy(x => x!.Supplier).OrderBy(g => g.Key))
                {
                    var folderPath = supplierGroup.First()!.FolderPath;
                    var supplierItem = new TreeViewItem
                    {
                        Header = supplierGroup.Key,
                        Tag = folderPath
                    };
                    monthItem.Items.Add(supplierItem);
                }

                yearItem.Items.Add(monthItem);
            }

            DatesTree.Items.Add(yearItem);
        }

        ExpandCurrentMonth();
    }

    private void ExpandCurrentMonth()
    {
        var today = DateTime.Today;
        var targetYear = today.Year.ToString("D4");
        var targetMonthPrefix = today.Month.ToString("D2") + ".";

        foreach (TreeViewItem yearItem in DatesTree.Items)
        {
            if (!string.Equals(yearItem.Header?.ToString(), targetYear, StringComparison.Ordinal))
                continue;

            yearItem.IsExpanded = true;

            foreach (TreeViewItem monthItem in yearItem.Items)
            {
                var header = monthItem.Header?.ToString() ?? "";
                if (!header.StartsWith(targetMonthPrefix, StringComparison.Ordinal))
                    continue;

                monthItem.IsExpanded = true;
                if (monthItem.Items.Count > 0 && monthItem.Items[0] is TreeViewItem firstClient)
                {
                    firstClient.IsSelected = true;
                    firstClient.BringIntoView();
                }
                return;
            }
        }
    }

    private static FolderInfo? BuildFolderInfo(string filePath)
    {
        try
        {
            var clientFolder = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(clientFolder)) return null;

            var monthFolder = Path.GetDirectoryName(clientFolder);
            if (string.IsNullOrWhiteSpace(monthFolder)) return null;

            var yearFolder = Path.GetDirectoryName(monthFolder);
            if (string.IsNullOrWhiteSpace(yearFolder)) return null;

            var yearName = Path.GetFileName(yearFolder);
            var monthFolderName = Path.GetFileName(monthFolder);
            var clientName = Path.GetFileName(clientFolder);

            if (!int.TryParse(yearName, out var year))
                return null;

            var month = 0;
            if (!string.IsNullOrWhiteSpace(monthFolderName) && monthFolderName.Length >= 2 && int.TryParse(monthFolderName[..2], out var m))
                month = m;

            return new FolderInfo(year, month, clientName, monthFolderName ?? month.ToString("D2"), clientFolder);
        }
        catch
        {
            return null;
        }
    }

    private void TreeFechas_MouseDoubleClick(object sender, MouseButtonEventArgs e) => DatesTree_MouseDoubleClick(sender, e);

    private async void DatesTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DatesTree.SelectedItem is not TreeViewItem item)
            return;

        if (item.Tag is string folder)
            await LoadRetentionsFromFolder(folder);
    }

    private async Task LoadRetentionsFromFolder(string folder)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                StatusLabel.Text = "The selected folder does not exist.";
                return;
            }

            var files = Directory.GetFiles(folder, "*.xml", SearchOption.TopDirectoryOnly);
            var list = new List<CustomerRetentionRow>();

            foreach (var file in files)
            {
                var row = await Task.Run(() => ParseRetention(file));
                if (row != null)
                    list.Add(row);
            }

            list = list
                .OrderBy(r => r.DateValue ?? DateTime.MaxValue)
                .ThenBy(r => r.ReceiptNumber)
                .ToList();

            _retentionRows.Clear();
            foreach (var r in list)
                _retentionRows.Add(r);

            StatusLabel.Text = list.Count > 0
                ? $"Loaded {list.Count} XML files."
                : "No XML files in the selected folder.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Error: " + ex.Message;
        }
    }

    private void BtnVer_Click(object sender, RoutedEventArgs e) => ViewButton_Click(sender, e);

    private void ViewButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.DataContext is not CustomerRetentionRow row) return;
        if (!File.Exists(row.FilePath))
        {
            StatusLabel.Text = "XML file not found on disk.";
            return;
        }

        var win = new RetencionClienteDetailWindow(row.FilePath)
        {
            Owner = this
        };
        win.Show();
    }

    private static CustomerRetentionRow? ParseRetention(string path)
    {
        try
        {
            var doc = XDocument.Load(path);
            var autorizacion = doc.Descendants("autorizacion").FirstOrDefault();
            XDocument? innerDoc = null;

            if (autorizacion != null)
            {
                var cdata = autorizacion.Element("comprobante")?.Value;
                if (!string.IsNullOrWhiteSpace(cdata))
                    innerDoc = XDocument.Parse(cdata);
            }

            innerDoc ??= doc;

            var infoTrib = innerDoc.Descendants("infoTributaria").FirstOrDefault();
            var infoRet = innerDoc.Descendants("infoCompRetencion").FirstOrDefault();
            var docSustento = innerDoc.Descendants("docSustento").FirstOrDefault();

            var estab = infoTrib?.Element("estab")?.Value ?? "";
            var ptoEmi = infoTrib?.Element("ptoEmi")?.Value ?? "";
            var secuencial = infoTrib?.Element("secuencial")?.Value ?? "";
            var receiptNumber = string.IsNullOrWhiteSpace(estab) && string.IsNullOrWhiteSpace(ptoEmi) && string.IsNullOrWhiteSpace(secuencial)
                ? ""
                : $"{estab}-{ptoEmi}-{secuencial}";

            var fechaStr = infoRet?.Element("fechaEmision")?.Value ?? "";
            DateTime? dateValue = null;
            if (DateTime.TryParse(fechaStr, CultureInfo.GetCultureInfo("es-EC"), DateTimeStyles.None, out var f))
                dateValue = f;
            else if (DateTime.TryParse(fechaStr, out var f2))
                dateValue = f2;

            var ruc = infoTrib?.Element("ruc")?.Value ?? "";
            var cliente = infoTrib?.Element("razonSocial")?.Value
                          ?? infoTrib?.Element("nombreComercial")?.Value
                          ?? "";

            var totalAmount = infoRet?.Element("importeTotal")?.Value
                               ?? docSustento?.Element("importeTotal")?.Value
                               ?? "";

            var numDocSustento = docSustento?.Element("numDocSustento")?.Value
                                  ?? infoRet?.Descendants("numDocSustento").FirstOrDefault()?.Value
                                  ?? "";
            numDocSustento = FormatDocumentNumber(numDocSustento);

            return new CustomerRetentionRow
            {
                Date = dateValue?.ToString("yyyy-MM-dd") ?? fechaStr,
                DateValue = dateValue,
                TaxId = ruc,
                Customer = cliente,
                ReceiptNumber = receiptNumber,
                TotalAmount = totalAmount,
                SupportingDocumentNumber = numDocSustento,
                FilePath = path
            };
        }
        catch
        {
            return null;
        }
    }

    private static string FormatDocumentNumber(string num)
    {
        var clean = num?.Trim() ?? "";
        if (clean.Length >= 15)
            return $"{clean[..3]}-{clean.Substring(3, 3)}-{clean.Substring(6)}";
        if (clean.Length >= 12)
            return $"{clean[..3]}-{clean.Substring(3, 3)}-{clean.Substring(6)}";
        return clean;
    }

    private record FolderInfo(int Year, int Month, string Supplier, string MonthFolderName, string FolderPath);

    private class CustomerRetentionRow
    {
        public string Date { get; set; } = "";
        public DateTime? DateValue { get; set; }
        public string TaxId { get; set; } = "";
        public string Customer { get; set; } = "";
        public string ReceiptNumber { get; set; } = "";
        public string TotalAmount { get; set; } = "";
        public string SupportingDocumentNumber { get; set; } = "";
        public string FilePath { get; set; } = "";

        public string Fecha { get => Date; set => Date = value; }
        public DateTime? FechaDate { get => DateValue; set => DateValue = value; }
        public string Ruc { get => TaxId; set => TaxId = value; }
        public string Cliente { get => Customer; set => Customer = value; }
        public string NumeroComprobante { get => ReceiptNumber; set => ReceiptNumber = value; }
        public string ImporteTotal { get => TotalAmount; set => TotalAmount = value; }
        public string NumeroDocSustento { get => SupportingDocumentNumber; set => SupportingDocumentNumber = value; }
        public string Ruta { get => FilePath; set => FilePath = value; }
    }
}
