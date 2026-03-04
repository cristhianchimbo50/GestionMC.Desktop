using Microsoft.Playwright;
using System;
using System.IO;
using System.Threading.Tasks;

namespace GestionMC.Desktop.Services;

public class SriSessionService : ISriSessionService
{
    private readonly string _storageStatePath;

    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;

    private const string InicioNatUrl = "https://srienlinea.sri.gob.ec/sri-en-linea/inicio/NAT";
    private const string PerfilUrl = "https://srienlinea.sri.gob.ec/sri-en-linea/contribuyente/perfil";

    public SriSessionService(string storageStatePath)
    {
        _storageStatePath = storageStatePath;
    }

    public async Task OpenLoginAutoSaveAndCloseAsync(string ruc, string password)
    {
        await EnsureContextAsync();

        for (int attempt = 0; attempt < 4; attempt++)
        {
            await _page!.GotoAsync(InicioNatUrl);
            await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            if (!await IsPerfilReadyAsync(_page, ruc))
            {
                await ClickIniciarSesionAsync(_page);

                if (await IsKeycloakLoginFormAsync(_page))
                    await TryFillLoginAsync(_page, ruc, password);

                var okPerfil = await WaitForPerfilRucAsync(_page, ruc, 45000);
                if (!okPerfil)
                {
                    await _page.WaitForTimeoutAsync(900);
                    continue;
                }
            }

            await EnsureRecibidosViaMenuAsync(_page, ruc, password);

            await SaveSessionAndCloseAsync();
            return;
        }

        throw new InvalidOperationException("No se pudo iniciar sesión y abrir Recibidos desde el menú.");
    }

    private static async Task ClickIniciarSesionAsync(IPage page)
    {
        var loc = page.Locator("pre:has-text('Iniciar sesión')");
        if (await loc.CountAsync() == 0)
            loc = page.Locator("text=Iniciar sesión");

        if (await loc.CountAsync() == 0)
            return;

        await loc.First.ClickAsync(new LocatorClickOptions { Timeout = 20000 });
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(700);
    }

    private static async Task<bool> IsKeycloakLoginFormAsync(IPage page)
    {
        try
        {
            var u = page.Locator("#usuario");
            var p = page.Locator("#password");
            var b = page.Locator("#kc-login");
            return (await u.CountAsync() > 0) && (await p.CountAsync() > 0) && (await b.CountAsync() > 0);
        }
        catch
        {
            return false;
        }
    }

    private static async Task TryFillLoginAsync(IPage page, string ruc, string password)
    {
        await page.WaitForSelectorAsync("#usuario", new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForSelectorAsync("#password", new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForSelectorAsync("#kc-login", new PageWaitForSelectorOptions { Timeout = 30000 });

        await page.FillAsync("#usuario", ruc);
        await page.FillAsync("#password", password);
        await page.ClickAsync("#kc-login");

        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.WaitForTimeoutAsync(900);
    }

    private static async Task<bool> WaitForPerfilRucAsync(IPage page, string ruc, int timeoutMs)
    {
        var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);

        while (DateTime.UtcNow < end)
        {
            try
            {
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            }
            catch
            {
            }

            if (await IsPerfilReadyAsync(page, ruc))
                return true;

            await page.WaitForTimeoutAsync(650);
        }

        return false;
    }

    private static async Task<bool> IsPerfilReadyAsync(IPage page, string ruc)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(page.Url) &&
                page.Url.ToLowerInvariant().Contains("/sri-en-linea/contribuyente/perfil"))
            {
                var lbl = page.Locator("label.titulo-campo.titulo-perfil");
                if (await lbl.CountAsync() > 0)
                {
                    var text = (await lbl.First.InnerTextAsync())?.Trim() ?? "";
                    if (text.Contains(ruc, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            await page.GotoAsync(PerfilUrl);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            if (IsLoginRedirect(page.Url))
                return false;

            var lbl2 = page.Locator("label.titulo-campo.titulo-perfil");
            if (await lbl2.CountAsync() == 0) return false;

            var t2 = (await lbl2.First.InnerTextAsync())?.Trim() ?? "";
            return t2.Contains(ruc, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task EnsureRecibidosViaMenuAsync(IPage page, string ruc, string password)
    {
        await page.GotoAsync(PerfilUrl);
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        if (IsLoginRedirect(page.Url))
        {
            await ClickIniciarSesionAsync(page);
            if (await IsKeycloakLoginFormAsync(page))
                await TryFillLoginAsync(page, ruc, password);

            await page.GotoAsync(PerfilUrl);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            if (IsLoginRedirect(page.Url))
                throw new InvalidOperationException("No se pudo acceder al perfil para abrir el menú.");
        }

        var menuBtn = page.Locator("#sri-menu");
        if (await menuBtn.CountAsync() == 0)
            throw new InvalidOperationException("No se encontró el botón del menú (hamburguesa).");

        await menuBtn.First.ClickAsync(new LocatorClickOptions { Timeout = 20000 });
        await page.WaitForTimeoutAsync(600);

        var facturacion = page.Locator("a.ui-panelmenu-header-link:has-text('FACTURACIÓN ELECTRÓNICA')");
        await facturacion.First.ClickAsync(new LocatorClickOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(600);

        var recibidos = page.Locator("a.ui-menuitem-link:has-text('Comprobantes electrónicos recibidos')");
        if (await recibidos.CountAsync() == 0)
            recibidos = page.Locator("a[href*='accederAplicacion.jspa'][href*='redireccion=57']");

        if (await recibidos.CountAsync() == 0)
            throw new InvalidOperationException("No se encontró el enlace 'Comprobantes electrónicos recibidos'.");

        try
        {
            await page.RunAndWaitForNavigationAsync(async () =>
            {
                await recibidos.First.ClickAsync(new LocatorClickOptions { Timeout = 30000 });
            }, new PageRunAndWaitForNavigationOptions { Timeout = 60000, WaitUntil = WaitUntilState.NetworkIdle });
        }
        catch
        {
            await recibidos.First.ClickAsync(new LocatorClickOptions { Timeout = 30000 });
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }

        if (IsLoginRedirect(page.Url))
        {
            if (await IsKeycloakLoginFormAsync(page))
                await TryFillLoginAsync(page, ruc, password);

            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }

        var ano = page.Locator("#frmPrincipal\\:ano");
        await ano.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 60000 });
    }

    public async Task SaveSessionAndCloseAsync()
    {
        if (_context == null) throw new InvalidOperationException("No hay sesión abierta.");

        await _page!.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var folder = Path.GetDirectoryName(_storageStatePath);
        if (!string.IsNullOrWhiteSpace(folder))
            Directory.CreateDirectory(folder);

        await _context.StorageStateAsync(new BrowserContextStorageStateOptions
        {
            Path = _storageStatePath
        });

        await DisposeAsync();
    }

    private async Task EnsureContextAsync()
    {
        if (_context != null && _page != null) return;

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false
        });

        _context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            AcceptDownloads = true
        });

        _page = await _context.NewPageAsync();
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

    public async ValueTask DisposeAsync()
    {
        if (_context != null) await _context.CloseAsync();
        if (_browser != null) await _browser.CloseAsync();
        _playwright?.Dispose();

        _context = null;
        _browser = null;
        _playwright = null;
        _page = null;
    }
}
