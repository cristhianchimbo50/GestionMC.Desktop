using Microsoft.Extensions.DependencyInjection;
using GestionMC.Desktop.Infrastructure;
using GestionMC.Desktop.Models;
using GestionMC.Desktop.ViewModels;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GestionMC.Desktop.Services;

namespace GestionMC.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly DisabledProvidersStore _disabledStore;

    public MainWindow()
    {
        InitializeComponent();
        var sp = AppHost.Services;
        _vm = sp.GetRequiredService<MainViewModel>();
        _disabledStore = sp.GetRequiredService<DisabledProvidersStore>();
        _vm.DownloadCompleted += LoadFechasExtraidas;
        DataContext = _vm;
        LoadFechasExtraidas();
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
        win.Show();
    }

    private void BtnOpenProveedores_Click(object sender, RoutedEventArgs e)
    {
        var win = new ProveedoresWindow();
        win.Show();
    }

    private void BtnOpenFacturasBd_Click(object sender, RoutedEventArgs e)
    {
        var win = new FacturasPagoWindow();
        win.Show();
    }

    private void BtnOpenDisabledProviders_Click(object sender, RoutedEventArgs e)
    {
        var win = new DisabledProvidersWindow(_disabledStore);
        win.Owner = this;
        win.ShowDialog();
        _ = _vm.LoadLocalAsync(_vm.SelectedYear, _vm.SelectedMonth, _vm.SelectedDay);
    }

    private void GridRecibidos_ContextMenuOpened(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GridRecibidos.SelectedItem is not SriReceivedRow row)
            {
                MiToggleProveedor.IsEnabled = false;
                return;
            }

            var ruc = (row.RucEmisor ?? "").Trim();
            if (string.IsNullOrWhiteSpace(ruc))
            {
                MiToggleProveedor.IsEnabled = false;
                return;
            }

            var isDisabled = _disabledStore.IsDisabled(ruc);
            MiToggleProveedor.IsEnabled = true;
            MiToggleProveedor.Header = isDisabled ? "Activar proveedor" : "Desactivar proveedor";
        }
        catch
        {
            MiToggleProveedor.IsEnabled = false;
        }
    }

    private void MiToggleProveedor_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (GridRecibidos.SelectedItem is not SriReceivedRow row)
                return;

            var ruc = (row.RucEmisor ?? "").Trim();
            if (string.IsNullOrWhiteSpace(ruc))
                return;

            var razon = (row.RazonSocialEmisor ?? "").Trim();
            _disabledStore.Toggle(ruc, razon);

            _ = _vm.LoadLocalAsync(_vm.SelectedYear, _vm.SelectedMonth, _vm.SelectedDay);
        }
        catch
        {
        }
    }

    private void LoadFechasExtraidas()
    {
        try
        {
            TreeFechas.Items.Clear();

            var baseFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GestionMC.Desktop",
                "Xml");

            if (!Directory.Exists(baseFolder)) return;

            var culture = new CultureInfo("es-ES");
            var now = DateTime.Now;

            var years = Directory.GetDirectories(baseFolder)
                .OrderByDescending(y => y)
                .ToList();

            foreach (var yearDir in years)
            {
                var yearName = Path.GetFileName(yearDir);
                var yearItem = new TreeViewItem { Header = yearName };

                var monthDirs = Directory.GetDirectories(yearDir)
                    .Select(m => new { Path = m, Order = ParseMonthPrefix(Path.GetFileName(m)) })
                    .OrderByDescending(x => x.Order)
                    .ToList();

                foreach (var monthDir in monthDirs)
                {
                    var monthName = Path.GetFileName(monthDir.Path);
                    var monthItem = new TreeViewItem { Header = monthName };

                    var dayDirs = Directory.GetDirectories(monthDir.Path)
                        .Select(d =>
                        {
                            var folderName = Path.GetFileName(d);
                            var ok = DateTime.TryParseExact(folderName, "dd-MM-yyyy", culture, DateTimeStyles.None, out var dt);
                            return new { Path = d, Date = ok ? dt : (DateTime?)null };
                        })
                        .OrderByDescending(d => d.Date ?? DateTime.MinValue)
                        .ToList();

                    foreach (var dayDir in dayDirs)
                    {
                        var folderName = Path.GetFileName(dayDir.Path);

                        if (dayDir.Date == null)
                        {
                            monthItem.Items.Add(new TreeViewItem { Header = folderName, Tag = dayDir.Path });
                            continue;
                        }

                        var dt = dayDir.Date.Value;
                        var fecha = dt.ToString("dd/MM/yyyy", culture);
                        var dow = culture.DateTimeFormat.GetDayName(dt.DayOfWeek).ToUpperInvariant();
                        var display = $"{fecha} {dow}";

                        monthItem.Items.Add(new TreeViewItem { Header = display, Tag = dayDir.Path });
                    }

                    if (monthItem.Items.Count > 0)
                        yearItem.Items.Add(monthItem);

                    if (int.TryParse(yearName, out var yInt) && yInt == now.Year && monthDir.Order == now.Month)
                    {
                        yearItem.IsExpanded = true;
                        monthItem.IsExpanded = true;
                    }
                }

                if (yearItem.Items.Count > 0)
                    TreeFechas.Items.Add(yearItem);
            }
        }
        catch
        {
        }
    }

    private static int ParseMonthPrefix(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        var parts = name.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return 0;
        if (int.TryParse(parts[0], out var m)) return m;
        return 0;
    }

    private async void TreeFechas_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (TreeFechas.SelectedItem is not TreeViewItem item)
                return;

            if (item.Tag is not string folder || !Directory.Exists(folder))
                return;

            var culture = new CultureInfo("es-ES");
            var dayName = Path.GetFileName(folder);

            if (!DateTime.TryParseExact(dayName, "dd-MM-yyyy", culture, DateTimeStyles.None, out var date))
                return;

            var monthDir = Path.GetDirectoryName(folder);
            var yearDir = Path.GetDirectoryName(monthDir ?? string.Empty);

            if (string.IsNullOrWhiteSpace(monthDir) || string.IsNullOrWhiteSpace(yearDir))
                return;

            var monthName = Path.GetFileName(monthDir);
            var yearName = Path.GetFileName(yearDir);

            if (!int.TryParse(yearName, out var year))
                return;

            var month = ParseMonthPrefix(monthName);
            if (month <= 0)
                return;

            _vm.SelectedYear = year;
            _vm.SelectedMonth = month;
            _vm.SelectedDay = date.Day;

            await _vm.LoadLocalAsync(year, month, date.Day);
        }
        catch
        {
        }
    }
}