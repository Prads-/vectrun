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

// Reject non-HTML content types before navigating (PDFs, ZIPs, etc.)
var lowerUrl = url.ToLowerInvariant().Split('?')[0];
if (lowerUrl.EndsWith(".pdf") || lowerUrl.EndsWith(".zip") || lowerUrl.EndsWith(".exe") ||
    lowerUrl.EndsWith(".docx") || lowerUrl.EndsWith(".xlsx") || lowerUrl.EndsWith(".pptx"))
{
    Console.Error.WriteLine($"Skipped: URL points to a downloadable file, not a web page ({url})");
    Console.WriteLine($"[Skipped: not a web page — {url}]");
    return 1;
}

try
{
    await page.GotoAsync(url, new PageGotoOptions
    {
        WaitUntil = WaitUntilState.Load,
        Timeout = 60_000,
    });
}
catch (PlaywrightException ex) when (ex.Message.Contains("Download is starting"))
{
    Console.Error.WriteLine($"Skipped: URL triggered a file download, not a web page ({url})");
    Console.WriteLine($"[Skipped: URL is a file download, not a web page — {url}]");
    return 1;
}

// Give JS-rendered content a chance to settle, but don't hard-fail on
// analytics-heavy sites that never reach full network idle.
try
{
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 25_000 });
}
catch (TimeoutException) { /* best-effort — content already loaded */ }

if (extractText)
{
    string text;

    if (url.Contains("google.com/search", StringComparison.OrdinalIgnoreCase))
    {
        // For Google search results pages, extract structured results directly from
        // the DOM rather than using innerText. This gives clean title / URL / snippet
        // tuples and avoids the breadcrumb URL display format entirely.
        text = await page.EvaluateAsync<string>(@"() => {
            const seen = new Set();
            const results = [];

            document.querySelectorAll('h3').forEach(h3 => {
                const title = h3.innerText.trim();
                if (!title) return;

                // Walk up the DOM to find the enclosing <a> that holds the result URL.
                let a = h3.closest('a');
                if (!a) a = h3.parentElement && h3.parentElement.querySelector('a');
                if (!a) a = h3.parentElement && h3.parentElement.parentElement &&
                              h3.parentElement.parentElement.querySelector('a');

                const href = a && a.href ? a.href : '';
                // Skip Google-internal links, anchors, and duplicates.
                if (!href || href.includes('google.com') || href.startsWith('/') || seen.has(href)) return;
                seen.add(href);

                // Walk up from h3 looking for an ancestor whose next sibling holds
                // the snippet. This is purely structural — no class names or data
                // attributes — so it survives Google's obfuscation churn.
                let node = h3;
                let snippet = '';
                for (let i = 0; i < 8 && node && node !== document.body; i++) {
                    node = node.parentElement;
                    if (!node) break;
                    let sib = node.nextElementSibling;
                    while (sib) {
                        const t = (sib.innerText || '').trim();
                        if (t.length > 40 && t !== title && !t.startsWith('http')) {
                            snippet = t.substring(0, 400);
                            break;
                        }
                        sib = sib.nextElementSibling;
                    }
                    if (snippet) break;
                }

                results.push(title + '\n' + href + (snippet ? '\n' + snippet : ''));
            });

            // Fall back to innerText if the DOM structure didn't yield results
            // (e.g. Google served a CAPTCHA or a layout we don't recognise).
            if (results.length === 0) {
                document.querySelectorAll('script, style, noscript, template').forEach(e => e.remove());
                return document.body.innerText;
            }

            return results.join('\n\n');
        }");
    }
    else
    {
        // For all other pages remove noise elements and return visible text.
        text = await page.EvaluateAsync<string>(@"() => {
            document.querySelectorAll('script, style, noscript, template').forEach(e => e.remove());
            return document.body.innerText;
        }");
    }

    Console.WriteLine(text);
}
else
{
    var html = await page.ContentAsync();
    Console.WriteLine(html);
}

return 0;
