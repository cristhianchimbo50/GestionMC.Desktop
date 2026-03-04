using Microsoft.Playwright;
using GestionMC.Desktop.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GestionMC.Desktop.Services;

public class SriRecibidosService : ISriRecibidosService
{
    private const string RecibidosUrl =
        "https://srienlinea.sri.gob.ec/comprobantes-electronicos-internet/pages/consultas/recibidos/comprobantesRecibidos.jsf?&contextoMPT=https://srienlinea.sri.gob.ec/tuportal-internet&pathMPT=Facturaci%F3n%20Electr%F3nica&actualMPT=Comprobantes%20electr%F3nicos%20recibidos%20&linkMPT=%2Fcomprobantes-electronicos-internet%2Fpages%2Fconsultas%2Frecibidos%2FcomprobantesRecibidos.jsf%3F&esFavorito=S";

    private readonly DisabledProvidersStore _disabledStore;

    public SriRecibidosService(DisabledProvidersStore disabledStore)
    {
        _disabledStore = disabledStore;
    }

    public async Task<List<SriReceivedRow>> DownloadAllByDateAsync(string storageStatePath, int year, int month, int day)
    {
        var folder = GetXmlFolder(year, month, day);

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

        await SelectIfExistsAsync(page, "#frmPrincipal\\:ano", year.ToString(CultureInfo.InvariantCulture));
        await SelectIfExistsAsync(page, "#frmPrincipal\\:mes", month.ToString(CultureInfo.InvariantCulture));
        await SelectIfExistsAsync(page, "#frmPrincipal\\:dia", day.ToString(CultureInfo.InvariantCulture));
        await SelectIfExistsAsync(page, "#frmPrincipal\\:tipoComprobante", "Factura");

        await ClickBuscarAsync(page);
        await WaitUntilResultsWithoutCaptchaAsync(page);

        var panel = await EnsureResultsPanelAsync(page, year, month, day);

        var tableRows = panel.Locator("table tbody tr");
        var count = await tableRows.CountAsync();

        var sriRows = new List<SriReceivedRow>();

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
                Nro = i + 1,
                RowIndex = i,
                RucEmisor = ruc,
                RazonSocialEmisor = razon,
                ClaveAcceso = clave,
                FechaEmision = fechaEmi
            });
        }

        var existingByClave = IndexExistingXmlByClave(folder);

        for (int i = 0; i < sriRows.Count; i++)
        {
            var row = sriRows[i];

            if (existingByClave.ContainsKey(row.ClaveAcceso))
                continue;

            if (_disabledStore.IsDisabled(row.RucEmisor))
                continue;

            var xmlLinkSelector = $"#frmPrincipal\\:tablaCompRecibidos\\:{row.RowIndex}\\:lnkXml";

            var download = await page.RunAndWaitForDownloadAsync(async () =>
            {
                await page.Locator(xmlLinkSelector).ClickAsync();
            });

            var tempPath = Path.Combine(folder, $"{row.ClaveAcceso}.xml");
            await download.SaveAsAsync(tempPath);

            var xmlContent = await File.ReadAllTextAsync(tempPath);
            var parsed = SriXmlParser.ParseFacturaAutorizada(xmlContent, tempPath).header;

            if (_disabledStore.IsDisabled(parsed.RucEmisor))
            {
                File.Delete(tempPath);
                continue;
            }

            var finalPath = BuildFinalPath(folder, parsed.NumeroFactura, row.ClaveAcceso);

            File.Move(tempPath, finalPath, true);

            existingByClave[row.ClaveAcceso] = finalPath;
        }

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
                    XmlPath = file
                });
            }
            catch
            {
                continue;
            }
        }

        result = result.OrderBy(r => r.NumeroFactura).ToList();

        for (int i = 0; i < result.Count; i++)
            result[i].Nro = i + 1;

        return result;
    }

    private static async Task ClickBuscarAsync(IPage page)
    {
        await page.Locator("#frmPrincipal\\:btnBuscar").ClickAsync();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(800);
    }

    private static async Task WaitUntilResultsWithoutCaptchaAsync(IPage page)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (IsLoginRedirect(page.Url))
                throw new InvalidOperationException("La sesión se perdió durante la consulta. Inicia sesión nuevamente.");

            var hasCaptcha = await IsCaptchaPresentAsync(page);
            var hasWarn = await HasCaptchaWarningAsync(page);

            if (!hasCaptcha && !hasWarn)
                return;

            await ClickBuscarAsync(page);
            await page.WaitForTimeoutAsync(900);
        }

        throw new InvalidOperationException("No se pudo continuar porque el SRI sigue mostrando captcha/captcha incorrecta.");
    }

    private static async Task<ILocator> EnsureResultsPanelAsync(IPage page, int year, int month, int day)
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

                return panel;
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
            await SelectIfExistsAsync(page, "#frmPrincipal\\:tipoComprobante", "Factura");

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

    private static string GetXmlFolder(int year, int month, int day)
    {
        var monthName = CultureInfo.GetCultureInfo("es-ES").DateTimeFormat.GetMonthName(month).ToUpperInvariant();
        var monthFolder = $"{month:D2}. {monthName}";
        var dayFolder = $"{day:D2}-{month:D2}-{year:D4}";

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GestionMC.Desktop",
            "Xml",
            year.ToString("D4"),
            monthFolder,
            dayFolder
        );

        Directory.CreateDirectory(folder);
        return folder;
    }

    private static Dictionary<string, string> IndexExistingXmlByClave(string folder)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(folder, "*.xml"))
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
}