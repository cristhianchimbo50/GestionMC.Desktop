using Microsoft.Playwright;
using GestionMC.Desktop.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace GestionMC.Desktop.Services;

public interface ISriRecibidosPaginadoService
{
    Task<List<SriReceivedRow>> DownloadAllPagedAsync(string storageStatePath, int year, int month, int day, string tipoComprobanteValue);
    Task<int> DownloadRetencionesClientesAsync(string storageStatePath, int year, int month, IProgress<string>? progress = null);
}

public class SriRecibidosPaginadoService : ISriRecibidosPaginadoService
{
    private const string RecibidosUrl =
        "https://srienlinea.sri.gob.ec/comprobantes-electronicos-internet/pages/consultas/recibidos/comprobantesRecibidos.jsf?&contextoMPT=https://srienlinea.sri.gob.ec/tuportal-internet&pathMPT=Facturaci%C3%B3n%20Electr%C3%B3nica&actualMPT=Comprobantes%20electr%C3%B3nicos%20recibidos%20&linkMPT=%2Fcomprobantes-electronicos-internet%2Fpages%2Fconsultas%2Frecibidos%2FcomprobantesRecibidos.jsf%3F&esFavorito=S";

    private readonly DisabledProvidersStore _disabledStore;

    public SriRecibidosPaginadoService(DisabledProvidersStore disabledStore)
    {
        _disabledStore = disabledStore;
    }

    public async Task<List<SriReceivedRow>> DownloadAllPagedAsync(string storageStatePath, int year, int month, int day, string tipoComprobanteValue)
    {
        var monthFolder = GetMonthFolder(year, month);
        var existingByClave = IndexExistingXmlByClave(monthFolder);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false
        });

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            StorageStatePath = storageStatePath,
            AcceptDownloads = true
        });

        var page = await context.NewPageAsync();

        async Task ReopenAsync()
        {
            await context.CloseAsync();
            context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                StorageStatePath = storageStatePath,
                AcceptDownloads = true
            });
            page = await context.NewPageAsync();
            await page.GotoAsync(RecibidosUrl);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            if (IsLoginRedirect(page.Url))
                throw new InvalidOperationException("Sesión inválida o expirada. Inicia sesión y guarda la sesión nuevamente.");
        }

        await page.GotoAsync(RecibidosUrl);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        if (IsLoginRedirect(page.Url))
            throw new InvalidOperationException("Sesión inválida o expirada. Inicia sesión y guarda la sesión nuevamente.");

        var sriRows = new List<SriReceivedRow>();
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var today = DateTime.Today;
        if (year == today.Year && month == today.Month)
            daysInMonth = Math.Min(daysInMonth, today.Day - 1);

        if (daysInMonth <= 0)
            return new List<SriReceivedRow>();

        for (int dayIndex = 1; dayIndex <= daysInMonth; dayIndex++)
        {
            await SelectIfExistsAsync(page, "#frmPrincipal\\:ano", year.ToString(CultureInfo.InvariantCulture));
            await SelectIfExistsAsync(page, "#frmPrincipal\\:mes", month.ToString(CultureInfo.InvariantCulture));
            await SelectIfExistsAsync(page, "#frmPrincipal\\:dia", dayIndex.ToString(CultureInfo.InvariantCulture));
            await SelectIfExistsAsync(page, "#frmPrincipal\\:tipoComprobante", tipoComprobanteValue);

            await ClickBuscarAsync(page);

            if (await HasNoDataWarningAsync(page))
                continue;

            await WaitUntilResultsWithoutCaptchaAsync(page);
            await EnsureResultsPanelAsync(page, year, month, dayIndex, tipoComprobanteValue);
            await SetPageSizeAsync(page, 75);

            while (true)
            {
                var tableRows = page.Locator("#frmPrincipal\\:tablaCompRecibidos table tbody tr");
                var count = await tableRows.CountAsync();

                for (int i = 0; i < count; i++)
                {
                    var row = tableRows.Nth(i);
                    var cells = row.Locator("td");
                    var cellCount = await cells.CountAsync();
                    if (cellCount < 6) continue;

                    var emisorText = (await cells.Nth(1).InnerTextAsync()).Trim();
                    var parts = emisorText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var ruc = parts.Length > 0 ? parts[0] : "";
                    var razon = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "";

                    if (_disabledStore.IsDisabled(ruc))
                        continue;

                    var clave = (await cells.Nth(3).InnerTextAsync()).Trim();
                    var fechaEmi = (await cells.Nth(5).InnerTextAsync()).Trim();

                    sriRows.Add(new SriReceivedRow
                    {
                        Nro = sriRows.Count + 1,
                        RowIndex = i,
                        RucEmisor = ruc,
                        RazonSocialEmisor = razon,
                        ClaveAcceso = clave,
                        FechaEmision = fechaEmi
                    });

                    if (existingByClave.ContainsKey(clave))
                        continue;

                    var xmlLinkSelector = $"#frmPrincipal\\:tablaCompRecibidos\\:{i}\\:lnkXml";

                    var download = await page.RunAndWaitForDownloadAsync(async () =>
                    {
                        await page.Locator(xmlLinkSelector).ClickAsync();
                    });

                    var dayFolder = GetDayFolder(monthFolder, year, month, dayIndex);
                    var tempPath = Path.Combine(dayFolder, $"{clave}.xml");
                    await download.SaveAsAsync(tempPath);

                    var xmlContent = await File.ReadAllTextAsync(tempPath);
                    var parsed = SriXmlParser.ParseFacturaAutorizada(xmlContent, tempPath).header;

                    if (_disabledStore.IsDisabled(parsed.RucEmisor))
                    {
                        File.Delete(tempPath);
                        continue;
                    }

                    var finalPath = BuildFinalPath(dayFolder, parsed.NumeroFactura, clave);
                    File.Move(tempPath, finalPath, true);
                    existingByClave[clave] = finalPath;
                }

                var hasNext = await ClickNextPageAsync(page);
                if (!hasNext) break;
                await WaitUntilResultsWithoutCaptchaAsync(page);
            }
        }

        var result = new List<SriReceivedRow>();

        foreach (var file in Directory.GetFiles(monthFolder, "*.xml", SearchOption.AllDirectories))
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
                    ClaveAcceso = parsed.ClaveAcceso
                });
            }
            catch
            {
            }
        }

        result = result
            .OrderBy(r => DateTime.TryParse(r.FechaEmision, out var dt) ? dt : DateTime.MaxValue)
            .ThenBy(r => r.NumeroFactura)
            .ToList();

        for (int i = 0; i < result.Count; i++)
            result[i].Nro = i + 1;

        return result;
    }

    public async Task<int> DownloadRetencionesClientesAsync(string storageStatePath, int year, int month, IProgress<string>? progress = null)
    {
        var baseFolder = GetRetencionesBaseFolder();
        var existingByClave = IndexExistingRetencionesByClave(baseFolder);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false
        });

        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            StorageStatePath = storageStatePath,
            AcceptDownloads = true
        });

        var page = await context.NewPageAsync();

        await page.GotoAsync(RecibidosUrl);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        if (IsLoginRedirect(page.Url))
            throw new InvalidOperationException("Sesión inválida o expirada. Inicia sesión y guarda la sesión nuevamente.");

        var totalDownloads = 0;
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var today = DateTime.Today;
        if (year == today.Year && month == today.Month)
            daysInMonth = Math.Min(daysInMonth, today.Day - 1);

        if (daysInMonth <= 0)
        {
            progress?.Report("No hay días disponibles (solo hasta el día anterior al actual).");
            return totalDownloads;
        }

        for (int dayIndex = 1; dayIndex <= daysInMonth; dayIndex++)
        {
            progress?.Report($"Consultando {dayIndex:D2}/{month:D2}/{year:D4}...");

            await SelectIfExistsAsync(page, "#frmPrincipal\\:ano", year.ToString(CultureInfo.InvariantCulture));
            await SelectIfExistsAsync(page, "#frmPrincipal\\:mes", month.ToString(CultureInfo.InvariantCulture));
            await SelectIfExistsAsync(page, "#frmPrincipal\\:dia", dayIndex.ToString(CultureInfo.InvariantCulture));
            await SelectIfExistsAsync(page, "#frmPrincipal\\:tipoComprobante", "6");
            await SelectIfExistsAsync(page, "#frmPrincipal\\:cmbTipoComprobante", "6");

            await ClickBuscarAsync(page);

            if (await HasFutureDateErrorAsync(page))
            {
                progress?.Report("La fecha ingresada debe ser menor a la fecha actual. Proceso detenido.");
                break;
            }

            if (await HasNoDataWarningAsync(page))
            {
                progress?.Report("Sin datos para los parámetros ingresados, se pasa al siguiente día.");
                continue;
            }

            await WaitUntilResultsWithoutCaptchaAsync(page);
            await EnsureResultsPanelAsync(page, year, month, dayIndex, "6");
            await SetPageSizeAsync(page, 75);
            await ClickFirstPageAsync(page);

            while (true)
            {
                var tableRows = page.Locator("#frmPrincipal\\:tablaCompRecibidos table tbody tr");
                var count = await tableRows.CountAsync();

                for (int i = 0; i < count; i++)
                {
                    var row = tableRows.Nth(i);
                    var cells = row.Locator("td");
                    var cellCount = await cells.CountAsync();
                    if (cellCount < 6) continue;

                    var emisorText = (await cells.Nth(1).InnerTextAsync()).Trim();
                    var parts = emisorText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var ruc = parts.Length > 0 ? parts[0] : "";
                    var razon = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "";

                    if (_disabledStore.IsDisabled(ruc))
                        continue;

                    var tipoSerieText = (await cells.Nth(2).InnerTextAsync()).Trim();
                    var numeroRetencion = ExtractNumeroRetencion(tipoSerieText);

                    var clave = (await cells.Nth(3).InnerTextAsync()).Trim();
                    if (existingByClave.ContainsKey(clave))
                        continue;

                    var xmlLinkSelector = $"#frmPrincipal\\:tablaCompRecibidos\\:{i}\\:lnkXml";

                    var download = await page.RunAndWaitForDownloadAsync(async () =>
                    {
                        await page.Locator(xmlLinkSelector).ClickAsync();
                    });

                    var monthFolder = GetRetencionMonthFolder(baseFolder, ruc, razon, year, month);
                    var fileName = BuildRetencionFileName(tipoSerieText, numeroRetencion);
                    var finalPath = Path.Combine(monthFolder, fileName);

                    await download.SaveAsAsync(finalPath);

                    existingByClave[clave] = finalPath;
                    totalDownloads++;
                    progress?.Report($"Guardado {Path.GetFileName(finalPath)}");
                }

                var hasNext = await ClickNextPageAsync(page);
                if (!hasNext) break;
                await WaitUntilResultsWithoutCaptchaAsync(page);
            }
        }

        progress?.Report($"Proceso finalizado. Descargados {totalDownloads} XML.");
        return totalDownloads;
    }

    private static async Task ClickBuscarAsync(IPage page)
    {
        await page.Locator("#frmPrincipal\\:btnBuscar").ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(800);
    }

    private static async Task<bool> ClickNextPageAsync(IPage page)
    {
        var next = page.Locator("#frmPrincipal\\:tablaCompRecibidos_paginator_bottom .ui-paginator-next");
        if (await next.CountAsync() == 0) return false;
        if (await next.First.GetAttributeAsync("class") is string cls && cls.Contains("ui-state-disabled"))
            return false;

        await next.First.ClickAsync(new LocatorClickOptions { Timeout = 20000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(600);
        return true;
    }

    private static async Task ClickFirstPageAsync(IPage page)
    {
        var first = page.Locator("#frmPrincipal\\:tablaCompRecibidos_paginator_bottom .ui-paginator-first");
        if (await first.CountAsync() == 0) return;
        if (await first.First.GetAttributeAsync("class") is string cls && cls.Contains("ui-state-disabled"))
            return;

        await first.First.ClickAsync(new LocatorClickOptions { Timeout = 20000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(400);
    }

    private static async Task<bool> HasFutureDateErrorAsync(IPage page)
    {
        var err = page.Locator(".ui-messages-error-summary, #frmMessages\\:messages .ui-messages-error-summary, .ui-message-error");
        if (await err.CountAsync() == 0) return false;

        var text = (await err.First.InnerTextAsync())?.Trim().ToLowerInvariant() ?? string.Empty;
        return text.Contains("fecha ingresada debe ser menor a la fecha actual")
               || text.Contains("fecha ingresada debe ser menor");
    }

    private static async Task<bool> HasNoDataWarningAsync(IPage page)
    {
        var warn = page.Locator(".ui-messages-warn-summary, #frmMessages\\:messages .ui-messages-warn-summary, #formMessages\\:messages .ui-messages-warn-summary");
        if (await warn.CountAsync() == 0) return false;

        var text = (await warn.First.InnerTextAsync())?.Trim().ToLowerInvariant() ?? string.Empty;
        return text.Contains("no existen datos para los parámetros")
            || text.Contains("no existen datos")
            || text.Contains("sin datos");
    }

    private static async Task SetPageSizeAsync(IPage page, int size)
    {
        var sel = page.Locator("#frmPrincipal\\:tablaCompRecibidos_paginator_bottom select");
        if (await sel.CountAsync() == 0) return;
        try
        {
            await sel.SelectOptionAsync(size.ToString(CultureInfo.InvariantCulture));
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await page.WaitForTimeoutAsync(400);
        }
        catch
        {
        }
    }

    private static async Task<bool> WaitUntilResultsWithoutCaptchaAsync(IPage page, int maxCaptchaRetriesBeforeFail = 0)
    {
        var captchaCount = 0;
        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (IsLoginRedirect(page.Url))
                throw new InvalidOperationException("La sesión se perdió durante la consulta. Inicia sesión nuevamente.");

            var hasCaptcha = await IsCaptchaPresentAsync(page);
            var hasWarn = await HasCaptchaWarningAsync(page);

            if (!hasCaptcha && !hasWarn)
                return true;

            captchaCount++;
            if (maxCaptchaRetriesBeforeFail > 0 && captchaCount > maxCaptchaRetriesBeforeFail)
                return false;

            await ClickBuscarAsync(page);
            await page.WaitForTimeoutAsync(900);
        }

        throw new InvalidOperationException("No se pudo continuar porque el SRI sigue mostrando captcha/captcha incorrecta.");
    }

    private static async Task EnsureResultsPanelAsync(IPage page, int year, int month, int day, string tipo)
    {
        const int maxAttempts = 4;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var panel = page.Locator("#frmPrincipal\\:panelListaComprobantes");

            try
            {
                await panel.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 3000
                });

                return;
            }
            catch (PlaywrightException)
            {
            }

            await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60000 });

            if (IsLoginRedirect(page.Url))
                throw new InvalidOperationException("La sesión se perdió al recargar la página de resultados.");

            await SelectIfExistsAsync(page, "#frmPrincipal\\:ano", year.ToString(CultureInfo.InvariantCulture));
            await SelectIfExistsAsync(page, "#frmPrincipal\\:mes", month.ToString(CultureInfo.InvariantCulture));
            await SelectIfExistsAsync(page, "#frmPrincipal\\:dia", day.ToString(CultureInfo.InvariantCulture));
            await SelectIfExistsAsync(page, "#frmPrincipal\\:tipoComprobante", tipo);

            await ClickBuscarAsync(page);
            await WaitUntilResultsWithoutCaptchaAsync(page);
        }

        throw new InvalidOperationException("No se pudo cargar la lista de comprobantes después de varios reintentos.");
    }

    private static async Task<bool> IsCaptchaPresentAsync(IPage page)
    {
        var captchaSelectors = new[]
        {
            "input[id*='captcha']",
            "input[name*='captcha']",
            "img[id*='captcha']",
            "img[src*='captcha']",
            "text=/captcha/i"
        };

        foreach (var sel in captchaSelectors)
        {
            var loc = page.Locator(sel);
            try
            {
                if (await loc.CountAsync() > 0 && await loc.First.IsVisibleAsync())
                    return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private static async Task<bool> HasCaptchaWarningAsync(IPage page)
    {
        try
        {
            var selectors = new[]
            {
                "#formMessages\\:messages .ui-messages-warn-summary",
                "#frmMessages\\:messages .ui-messages-warn-summary",
                "div[id$=':messages'] .ui-messages-warn-summary",
                "div.ui-messages .ui-messages-warn-summary"
            };

            foreach (var s in selectors)
            {
                var warnLoc = page.Locator(s);
                if (await warnLoc.CountAsync() == 0) continue;
                if (!await warnLoc.First.IsVisibleAsync()) continue;

                var text = (await warnLoc.First.InnerTextAsync())?.Trim().ToLowerInvariant() ?? "";
                if (text.Contains("captcha"))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static async Task SelectIfExistsAsync(IPage page, string selector, string value)
    {
        var loc = page.Locator(selector);
        if (await loc.CountAsync() == 0) return;

        try
        {
            await loc.SelectOptionAsync(new SelectOptionValue { Value = value });
        }
        catch
        {
            await loc.SelectOptionAsync(new SelectOptionValue { Label = value });
        }
    }

    private static string GetMonthFolder(int year, int month)
    {
        var monthName = CultureInfo.GetCultureInfo("es-ES").DateTimeFormat.GetMonthName(month).ToUpperInvariant();
        var monthFolder = $"{month:D2}. {monthName}";

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GestionMC",
            "Xml",
            year.ToString("D4"),
            monthFolder
        );

        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string GetRetencionesBaseFolder()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GestionMC",
            "Retenciones clientes");

        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string GetRetencionMonthFolder(string baseFolder, string ruc, string razon, int year, int month)
    {
        var monthName = CultureInfo.GetCultureInfo("es-ES").DateTimeFormat.GetMonthName(month).ToUpperInvariant();
        var monthFolderName = $"{month:D2}. {monthName}";
        var clienteFolder = SanitizeFileName(string.IsNullOrWhiteSpace(razon) ? "SIN_CLIENTE" : razon);

        var folder = Path.Combine(baseFolder, year.ToString("D4"), monthFolderName, clienteFolder);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string GetDayFolder(string monthFolder, int year, int month, int day)
    {
        var dayFolderName = $"{day:D2}-{month:D2}-{year:D4}";
        var folder = Path.Combine(monthFolder, dayFolderName);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static Dictionary<string, string> IndexExistingXmlByClave(string folder)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(folder, "*.xml", SearchOption.AllDirectories))
        {
            var name = Path.GetFileNameWithoutExtension(file);

            var idx = name.LastIndexOf('_');
            if (idx >= 0 && idx + 1 < name.Length)
            {
                var clave = name[(idx + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(clave) && !dict.ContainsKey(clave))
                    dict[clave] = file;
            }

            if (!dict.ContainsKey(name))
                dict[name] = file;
        }

        return dict;
    }

    private static string BuildFinalPath(string folder, string numeroFactura, string claveAcceso)
    {
        var safeNumero = SanitizeFileName(numeroFactura);
        var safeClave = SanitizeFileName(claveAcceso);

        var fileName = $"{safeNumero}_{safeClave}.xml";
        return Path.Combine(folder, fileName);
    }

    private static bool IsLoginRedirect(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;

        var u = url.ToLowerInvariant();

        if (u.Contains("/auth/realms/")) return true;
        if (u.Contains("openid-connect")) return true;
        if (u.Contains("protocol/openid-connect")) return true;
        if (u.Contains("login")) return true;

        return false;
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "SIN_NOMBRE";

        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');

        return name.Trim();
    }

    private static string BuildRetencionFileName(string tipoSerieText, string? numeroRetencion)
    {
        var baseName = !string.IsNullOrWhiteSpace(tipoSerieText)
            ? tipoSerieText
            : numeroRetencion;

        var safeName = string.IsNullOrWhiteSpace(baseName)
            ? "SIN_NUMERO"
            : SanitizeFileName(baseName);

        return $"{safeName}.xml";
    }

    private static string? ExtractNumeroRetencion(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = Regex.Match(text, @"\d{3}-\d{3}-\d+");
        return match.Success ? match.Value : null;
    }

    private static Dictionary<string, string> IndexExistingRetencionesByClave(string folder)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(folder, "*.xml", SearchOption.AllDirectories))
        {
            try
            {
                var doc = XDocument.Load(file);
                var aut = doc.Descendants("autorizacion").FirstOrDefault();
                XDocument? inner = null;

                if (aut != null)
                {
                    var cdata = aut.Element("comprobante")?.Value;
                    if (!string.IsNullOrWhiteSpace(cdata))
                        inner = XDocument.Parse(cdata);
                }

                inner ??= doc;
                var clave = inner.Descendants("claveAcceso").FirstOrDefault()?.Value;

                if (!string.IsNullOrWhiteSpace(clave) && !dict.ContainsKey(clave))
                    dict[clave] = file;
            }
            catch
            {
            }
        }

        return dict;
    }
}
