using GestionMC.Desktop.Configuration;
using GestionMC.Desktop.Models;
using GestionMC.Desktop.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace GestionMC.Desktop.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly AppSettings _settings;
    private readonly ISriRecibidosService _recibidosService;
    private readonly IOracleProveedorService _proveedorService;
    private readonly IOracleFacturaPagoService _facturaPagoService;
    private readonly IStoragePathProvider _storagePathProvider;
    private readonly Func<ISriSessionService> _sriSessionFactory;
    private readonly DisabledProvidersStore _disabledStore;

    private ISriSessionService? _sriSession;
    private HashSet<string> _proveedoresRuc = new(StringComparer.OrdinalIgnoreCase);
    private List<FacturaPagoRow> _facturasBd = new();

    private string _status = "Listo.";
    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<SriReceivedRow> Rows { get; } = new();

    public IEnumerable<int> YearOptions { get; }
    public IEnumerable<int> MonthOptions { get; }
    public IEnumerable<int> DayOptions { get; }

    private int _selectedYear;
    public int SelectedYear
    {
        get => _selectedYear;
        set
        {
            if (_selectedYear == value) return;
            _selectedYear = value;
            OnPropertyChanged();
            RefreshCommands();
        }
    }

    private int _selectedMonth;
    public int SelectedMonth
    {
        get => _selectedMonth;
        set
        {
            if (_selectedMonth == value) return;
            _selectedMonth = value;
            OnPropertyChanged();
            RefreshCommands();
        }
    }

    private int _selectedDay;
    public int SelectedDay
    {
        get => _selectedDay;
        set
        {
            if (_selectedDay == value) return;
            _selectedDay = value;
            OnPropertyChanged();
            RefreshCommands();
        }
    }

    public ICommand OpenSriCommand { get; }
    public ICommand SaveSriSessionCommand { get; }
    public ICommand DownloadAllCommand { get; }

    public event Action? DownloadCompleted;

    public MainViewModel(
        AppSettings settings,
        ISriRecibidosService recibidosService,
        IOracleProveedorService proveedorService,
        IOracleFacturaPagoService facturaPagoService,
        IStoragePathProvider storagePathProvider,
        Func<ISriSessionService> sriSessionFactory,
        DisabledProvidersStore disabledStore)
    {
        _settings = settings;
        _recibidosService = recibidosService;
        _proveedorService = proveedorService;
        _facturaPagoService = facturaPagoService;
        _storagePathProvider = storagePathProvider;
        _sriSessionFactory = sriSessionFactory;
        _disabledStore = disabledStore;

        var now = DateTime.Now;
        YearOptions = Enumerable.Range(now.Year - 3, 7).ToList();
        MonthOptions = Enumerable.Range(1, 12).ToList();
        DayOptions = Enumerable.Range(1, 31).ToList();

        _selectedYear = now.Year;
        _selectedMonth = now.Month;
        _selectedDay = now.Day;

        OpenSriCommand = new RelayCommand(OpenSriAsync);
        SaveSriSessionCommand = new RelayCommand(SaveSriSessionAsync, () => _sriSession != null);
        DownloadAllCommand = new RelayCommand(DownloadAllAsync, CanDownload);
    }

    public string GetStoragePath() => _storagePathProvider.GetStoragePath();

    public bool ProveedorExiste(string? ruc) => _proveedoresRuc.Contains(ruc ?? "");

    public async Task OpenSriAsync()
    {
        try
        {
            var storagePath = GetStoragePath();
            _sriSession = _sriSessionFactory();

            Status = "Iniciando sesión en SRI...";
            await _sriSession.OpenLoginAutoSaveAndCloseAsync(_settings.SriCredentials.User, _settings.SriCredentials.Password);

            _sriSession = null;
            Status = "Sesión guardada y navegador cerrado. Puedes descargar.";
        }
        catch (Exception ex)
        {
            _sriSession = null;
            Status = "Error: " + ex.Message;
        }
    }

    public async Task SaveSriSessionAsync()
    {
        try
        {
            if (_sriSession == null)
            {
                Status = "Acción deshabilitada.";
                return;
            }

            await _sriSession.SaveSessionAndCloseAsync();
            _sriSession = null;
            Status = "Sesión guardada.";
        }
        catch (Exception ex)
        {
            Status = "Error: " + ex.Message;
        }
    }

    public async Task DownloadAllAsync((int year, int month, int day) date)
    {
        try
        {
            var storagePath = GetStoragePath();
            if (!File.Exists(storagePath))
            {
                Status = "No existe sesión guardada. Inicia sesión primero.";
                return;
            }

            Rows.Clear();
            Status = "Descargando XML y construyendo listado...";

            var data = await _recibidosService.DownloadAllByDateAsync(storagePath, date.year, date.month, date.day);

            data = data
                .Where(r => !_disabledStore.IsDisabled(r.RucEmisor))
                .ToList();

            await CargarProveedoresAsync();
            await CargarFacturasBdAsync();

            foreach (var r in data)
            {
                r.ProveedorExiste = ProveedorExiste(r.RucEmisor);

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
                }

                Rows.Add(r);
            }

            Status = "Proceso finalizado.";
            DownloadCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            Status = "Error: " + ex.Message;
        }
    }

    public Task DownloadAllAsync()
    {
        if (!CanDownload()) return Task.CompletedTask;
        return DownloadAllAsync((SelectedYear, SelectedMonth, SelectedDay));
    }

    public async Task LoadLocalAsync(int year, int month, int day)
    {
        try
        {
            var folder = BuildXmlFolder(year, month, day);

            if (!Directory.Exists(folder))
            {
                Rows.Clear();
                Status = "No hay XML descargados para la fecha seleccionada.";
                return;
            }

            Rows.Clear();
            Status = "Cargando XML locales...";

            await CargarProveedoresAsync();
            await CargarFacturasBdAsync();

            var result = new List<SriReceivedRow>();

            foreach (var file in Directory.GetFiles(folder, "*.xml"))
            {
                try
                {
                    var xml = await File.ReadAllTextAsync(file);
                    var parsed = SriXmlParser.ParseFacturaAutorizada(xml, file).header;

                    if (_disabledStore.IsDisabled(parsed.RucEmisor))
                        continue;

                    result.Add(new SriReceivedRow
                    {
                        NumeroFactura = parsed.NumeroFactura,
                        RazonSocialEmisor = parsed.RazonSocialEmisor,
                        RucEmisor = parsed.RucEmisor,
                        FechaEmision = parsed.FechaEmision.ToString("yyyy-MM-dd"),
                        XmlPath = file,
                        ClaveAcceso = parsed.ClaveAcceso,
                        ProveedorExiste = ProveedorExiste(parsed.RucEmisor)
                    });
                }
                catch
                {
                }
            }

            result = result.OrderBy(r => r.NumeroFactura).ToList();

            for (int i = 0; i < result.Count; i++)
            {
                var r = result[i];
                r.Nro = i + 1;

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
                }

                Rows.Add(r);
            }

            Status = result.Count > 0 ? "XML cargados desde disco." : "No se encontraron XML para la fecha seleccionada.";
        }
        catch (Exception ex)
        {
            Status = "Error: " + ex.Message;
        }
    }

    private async Task CargarProveedoresAsync()
    {
        try
        {
            Status = "Cargando proveedores desde Oracle...";

            var proveedores = await Task.Run(() => _proveedorService.GetProveedores());

            _proveedoresRuc = new HashSet<string>(proveedores.Select(p => p.RucCi), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Status = "No se pudieron cargar proveedores: " + ex.Message;
            _proveedoresRuc = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task CargarFacturasBdAsync()
    {
        try
        {
            Status = "Cargando facturas de BD...";

            _facturasBd = await Task.Run(() => _facturaPagoService.GetFacturasPago());
        }
        catch (Exception ex)
        {
            Status = "No se pudieron cargar facturas BD: " + ex.Message;
            _facturasBd = new List<FacturaPagoRow>();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool CanDownload()
    {
        return SelectedYear > 0 && SelectedMonth > 0 && SelectedDay > 0;
    }

    private void RefreshCommands()
    {
        if (DownloadAllCommand is RelayCommand r)
            r.RaiseCanExecuteChanged();
        if (SaveSriSessionCommand is RelayCommand s)
            s.RaiseCanExecuteChanged();
    }

    private static string BuildXmlFolder(int year, int month, int day)
    {
        var monthName = CultureInfo.GetCultureInfo("es-ES").DateTimeFormat.GetMonthName(month).ToUpperInvariant();
        var monthFolder = $"{month:D2}. {monthName}";
        var dayFolder = $"{day:D2}-{month:D2}-{year:D4}";

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GestionMC",
            "Xml",
            year.ToString("D4"),
            monthFolder,
            dayFolder
        );
    }
}