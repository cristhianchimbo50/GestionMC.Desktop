using Microsoft.Playwright;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GestionMC.Desktop.Services;

public interface ISriEmitidosService
{
    Task<int> DownloadRetencionesByMonthAsync(string storageStatePath, int year, int month, IProgress<string>? progress = null);
}

public class SriEmitidosService : ISriEmitidosService
{
    private const string MenuUrl = "https://srienlinea.sri.gob.ec/comprobantes-electronicos-internet/pages/consultas/menu.jsf?&contextoMPT=https://srienlinea.sri.gob.ec/tuportal-internet&pathMPT=Facturaci%C3%B3n%20Electr%C3%B3nica%20%2F%20Producci%C3%B3n&actualMPT=Consultas%20&linkMPT=%2Fcomprobantes-electronicos-internet%2Fpages%2Fconsultas%2Fmenu.jsf%3F&esFavorito=S";

    public async Task<int> DownloadRetencionesByMonthAsync(string storageStatePath, int year, int month, IProgress<string>? progress = null)
    {
        var totalDownloads = 0;
        var folder = GetRetencionFolder(year, month);

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

        progress?.Report("Abriendo menú de consultas...");
        await page.GotoAsync(MenuUrl);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        if (IsLoginRedirect(page.Url))
            throw new InvalidOperationException("Sesión inválida o expirada. Inicia sesión y guarda la sesión nuevamente.");

        await NavigateToEmitidosAsync(page, progress);

        var daysInMonth = DateTime.DaysInMonth(year, month);
        var culture = CultureInfo.GetCultureInfo("es-EC");

        for (int day = 1; day <= daysInMonth; day++)
        {
            var date = new DateTime(year, month, day);
            var dateStr = date.ToString("dd/MM/yyyy", culture);

            progress?.Report($"Consultando {dateStr}...");

            await SetDateAsync(page, dateStr);
            await SelectIfExistsAsync(page, "#frmPrincipal\\:cmbTipoComprobante", "6");
            await ClickBuscarAsync(page);

            var hasFutureDateError = await HasFutureDateErrorAsync(page);
            if (hasFutureDateError)
            {
                progress?.Report("La fecha ingresada debe ser menor a la fecha actual. Se detiene la búsqueda.");
                break;
            }

            var noData = await HasNoDataAsync(page);
            if (noData)
                continue;

            var downloads = await DownloadAllPdfAsync(page, folder, dateStr);
            totalDownloads += downloads;
        }

        if (totalDownloads > 0)
            MergePdfs(folder, year, month);

        progress?.Report($"Proceso finalizado. Descargados {totalDownloads} PDF.");
        return totalDownloads;
    }

    private static async Task<bool> HasFutureDateErrorAsync(IPage page)
    {
        var err = page.Locator(".ui-messages-error-summary, #frmMessages\\:messages .ui-messages-error-summary, .ui-message-error");
        if (await err.CountAsync() == 0) return false;

        var text = (await err.First.InnerTextAsync())?.Trim().ToLowerInvariant() ?? string.Empty;
        return text.Contains("fecha ingresada debe ser menor a la fecha actual")
               || text.Contains("fecha ingresada debe ser menor");
    }

    private static async Task NavigateToEmitidosAsync(IPage page, IProgress<string>? progress)
    {
        var emitidosLink = page.Locator("a:has-text('Comprobantes electrónicos emitidos')");
        if (await emitidosLink.CountAsync() == 0)
            emitidosLink = page.Locator("a[onclick*='consultaDocumentoForm:j_idt22']");

        if (await emitidosLink.CountAsync() == 0)
            throw new InvalidOperationException("No se encontró el enlace 'Comprobantes electrónicos emitidos' en la pantalla de consultas.");

        progress?.Report("Abriendo 'Comprobantes electrónicos emitidos'...");

        try
        {
            await page.RunAndWaitForNavigationAsync(async () =>
            {
                await emitidosLink.First.ClickAsync(new LocatorClickOptions { Timeout = 30000 });
            }, new PageRunAndWaitForNavigationOptions { Timeout = 60000, WaitUntil = WaitUntilState.NetworkIdle });
        }
        catch
        {
            await emitidosLink.First.ClickAsync(new LocatorClickOptions { Timeout = 30000 });
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }
    }

    private static async Task SetDateAsync(IPage page, string date)
    {
        var candidates = new[]
        {
            "#frmPrincipal\\:calendarFechaDesde_input",
            "#frmPrincipal\\:calFechaDesde_input",
            "#frmPrincipal\\:calFechaHasta_input",
            "input[id*='calendarFechaDesde']",
            "input[id*='fechaDesde']",
            "input[type='text'][id*='fecha']"
        };

        foreach (var sel in candidates)
        {
            var box = page.Locator(sel);
            if (await box.CountAsync() == 0) continue;

            await box.First.FillAsync("");
            await box.First.FillAsync(date);
            return;
        }
    }

    private static async Task ClickBuscarAsync(IPage page)
    {
        var searchButtons = new[]
        {
            "#frmPrincipal\\:btnBuscar",
            "#frmPrincipal\\:btnConsultar",
            "button:has-text('Consultar')",
            "span:has-text('Consultar')"
        };

        foreach (var sel in searchButtons)
        {
            var loc = page.Locator(sel);
            if (await loc.CountAsync() == 0) continue;

            // If the selector is the span, click its parent button
            if (sel.StartsWith("span:"))
            {
                var span = loc.First;
                var btn = span.Locator("xpath=ancestor::button[1]");
                if (await btn.CountAsync() > 0)
                {
                    await btn.First.ClickAsync(new LocatorClickOptions { Timeout = 30000 });
                }
                else
                {
                    await span.ClickAsync(new LocatorClickOptions { Timeout = 30000 });
                }
            }
            else
            {
                await loc.First.ClickAsync(new LocatorClickOptions { Timeout = 30000 });
            }

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await page.WaitForTimeoutAsync(800);
            return;
        }
    }

    private static async Task<bool> HasNoDataAsync(IPage page)
    {
        var warn = page.Locator(".ui-messages-warn-summary, #frmMessages\\:messages .ui-messages-warn-summary, .ui-message-warn");
        if (await warn.CountAsync() > 0 && await warn.First.IsVisibleAsync())
        {
            var text = (await warn.First.InnerTextAsync())?.Trim().ToLowerInvariant() ?? string.Empty;
            if (text.Contains("no existen datos"))
                return true;
        }

        var table = page.Locator("table tbody tr");
        if (await table.CountAsync() == 0)
            return true;

        return false;
    }

    private static async Task<int> DownloadAllPdfAsync(IPage page, string folder, string dateDisplay)
    {
        var count = 0;
        var pdfIcons = page.Locator("img[src*='pdf.gif']");
        var total = await pdfIcons.CountAsync();

        for (int i = 0; i < total; i++)
        {
            var icon = pdfIcons.Nth(i);
            var row = icon.Locator("xpath=ancestor::tr");
            var rowText = (await row.InnerTextAsync())?.Replace("\n", " ") ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(dateDisplay) && !rowText.Contains(dateDisplay, StringComparison.OrdinalIgnoreCase))
            {
                var altDate = DateTime.TryParseExact(dateDisplay, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                    ? dt.ToString("yyyy-MM-dd")
                    : null;

                if (altDate == null || !rowText.Contains(altDate, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            var numero = ExtractNumero(rowText) ?? $"{dateDisplay.Replace('/', '-')}-ret-{i + 1:000}";
            var safeName = SanitizeFileName(numero) + ".pdf";
            var target = Path.Combine(folder, safeName);

            if (File.Exists(target))
                continue;

            var download = await page.RunAndWaitForDownloadAsync(async () =>
            {
                await icon.ClickAsync();
            });

            await download.SaveAsAsync(target);
            count++;
        }

        return count;
    }

    private static void MergePdfs(string folder, int year, int month)
    {
        var files = Directory.GetFiles(folder, "*.pdf")
            .OrderBy(f => ExtractNumero(Path.GetFileNameWithoutExtension(f)) ?? Path.GetFileName(f))
            .ToList();

        if (files.Count == 0) return;

        var monthName = CultureInfo.GetCultureInfo("es-ES").DateTimeFormat.GetMonthName(month).ToUpperInvariant();
        var outputPath = Path.Combine(folder, $"Retenciones_{monthName}_{year:D4}.pdf");
        using var outputDoc = new PdfDocument();

        foreach (var file in files)
        {
            using var input = PdfReader.Open(file, PdfDocumentOpenMode.Import);
            for (int i = 0; i < input.PageCount; i++)
            {
                outputDoc.AddPage(input.Pages[i]);
            }
        }

        outputDoc.Save(outputPath);
    }

    private static async Task ClickIfExistsAsync(IPage page, string selector)
    {
        var loc = page.Locator(selector);
        if (await loc.CountAsync() == 0) return;
        await loc.First.ClickAsync(new LocatorClickOptions { Timeout = 20000 });
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

    private static string GetRetencionFolder(int year, int month)
    {
        var monthName = CultureInfo.GetCultureInfo("es-ES").DateTimeFormat.GetMonthName(month).ToUpperInvariant();
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GestionMC",
            "Retenciones",
            year.ToString("D4"),
            $"{month:D2}. {monthName}");

        Directory.CreateDirectory(folder);
        return folder;
    }

    private static bool IsLoginRedirect(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;
        var u = url.ToLowerInvariant();
        if (u.Contains("/auth/realms/")) return true;
        if (u.Contains("openid-connect")) return true;
        if (u.Contains("login")) return true;
        return false;
    }

    private static string? ExtractNumero(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Regex.Match(text, @"\d{3}-\d{3}-\d+");
        if (m.Success) return m.Value;
        return null;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
