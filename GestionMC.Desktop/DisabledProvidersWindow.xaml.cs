using GestionMC.Desktop.Services;
using System;
using System.Linq;
using System.Windows;

namespace GestionMC.Desktop;

public partial class DisabledProvidersWindow : Window
{
    private readonly DisabledProvidersStore _store;

    public DisabledProvidersWindow(DisabledProvidersStore store)
    {
        InitializeComponent();
        _store = store;
        LoadData();
    }

    private void LoadData()
    {
        var all = _store.GetAll().OrderByDescending(x => x.Disabled).ThenBy(x => x.Ruc).ToList();
        GridDisabled.ItemsSource = all;
        LblInfo.Text = $"Total: {all.Count} | Deshabilitados: {all.Count(x => x.Disabled)}";
    }

    private DisabledProviderEntry? Selected()
    {
        return GridDisabled.SelectedItem as DisabledProviderEntry;
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        try { LoadData(); } catch { }
    }

    private void BtnEnable_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var row = Selected();
            if (row == null) return;
            _store.SetDisabled(row.Ruc, row.RazonSocial, false);
            LoadData();
        }
        catch
        {
        }
    }

    private void BtnDisable_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var row = Selected();
            if (row == null) return;
            _store.SetDisabled(row.Ruc, row.RazonSocial, true);
            LoadData();
        }
        catch
        {
        }
    }
}