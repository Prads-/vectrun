using System.Text.Json;
using Microsoft.Playwright;

// Support both CLI usage (args[0]) and pipeline usage (JSON via stdin)
string url;
bool extractText = false;

if (args.Length > 0)
{
    url = args[0];
    extractText = args.Length > 1 && args[1].Equals("--extract-text", StringComparison.OrdinalIgnoreCase);
}
else
{
    var stdin = await Console.In.ReadToEndAsync();
    if (string.IsNullOrWhiteSpace(stdin))
    {
        Console.Error.WriteLine("Usage: web-scraper <url> [--extract-text]  OR  echo '{\"url\":\"...\"}' | web-scraper");
        return 1;
    }
    var json = JsonSerializer.Deserialize<JsonElement>(stdin);
    if (!json.TryGetProperty("url", out var urlProp))
    {
        Console.Error.WriteLine("JSON input must contain a 'url' field.");
        return 1;
    }
    url = urlProp.GetString() ?? string.Empty;
    extractText = json.TryGetProperty("extractText", out var et) && et.ValueKind == JsonValueKind.True;
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

if (extractText)
{
    // Remove noise elements from the live DOM before extracting text.
    // script/style/noscript contain code or hidden text; template elements are inert.
    // Working on the live DOM is fine in a headless context.
    // Note: innerText is layout-dependent and only works on attached nodes, so we
    // cannot use it on a cloneNode — we strip in-place instead.
    var text = await page.EvaluateAsync<string>(@"() => {
        document.querySelectorAll('script, style, noscript, template').forEach(e => e.remove());
        return document.body.innerText;
    }");
    Console.WriteLine(text);
}
else
{
    var html = await page.ContentAsync();
    Console.WriteLine(html);
}

return 0;
