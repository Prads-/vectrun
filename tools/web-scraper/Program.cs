using System.Text.Json;
using Microsoft.Playwright;

// Support both CLI usage (args[0]) and pipeline usage (JSON via stdin)
string url;
if (args.Length > 0)
{
    url = args[0];
}
else
{
    var stdin = await Console.In.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(stdin))
    {
        Console.Error.WriteLine("Usage: web-scraper <url>  OR  echo '{\"url\":\"...\"}' | web-scraper");
        return 1;
    }
    var json = JsonSerializer.Deserialize<JsonElement>(stdin);
    if (!json.TryGetProperty("url", out var urlProp))
    {
        Console.Error.WriteLine("JSON input must contain a 'url' field.");
        return 1;
    }
    url = urlProp.GetString() ?? string.Empty;
}

if (string.IsNullOrWhiteSpace(url))
{
    Console.Error.WriteLine("URL must not be empty.");
    return 1;
}

// Normalize bare URLs (no scheme) to https://
if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
{
    url = "https://" + url;
}

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
{
    Headless = true,
    Args = new[]
    {
        "--disable-blink-features=AutomationControlled",
        "--no-sandbox",
        "--disable-dev-shm-usage",
    }
});

var context = await browser.NewContextAsync(new BrowserNewContextOptions
{
    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
    ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
    ExtraHTTPHeaders = new Dictionary<string, string>
    {
        ["Accept-Language"] = "en-US,en;q=0.9",
        ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8",
    }
});

// Hide webdriver flag from JS
await context.AddInitScriptAsync("Object.defineProperty(navigator, 'webdriver', { get: () => undefined })");

var page = await context.NewPageAsync();

await page.GotoAsync(url, new PageGotoOptions
{
    WaitUntil = WaitUntilState.Load,
    Timeout = 60_000,
});

// Give JS-rendered content a chance to settle, but don't hard-fail on
// analytics-heavy sites that never reach full network idle.
try
{
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 25_000 });
}
catch (TimeoutException) { /* best-effort — content already loaded */ }

var html = await page.ContentAsync();
Console.WriteLine(html);

return 0;
