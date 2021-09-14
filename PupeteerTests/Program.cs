using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using PuppeteerSharp;

namespace PupeteerTests
{
    /// <summary>
    /// Article content assumptions:
    ///     - Pages are relatively static - no lazy loading of content, no fancy redirects, etc...
    ///     - Fonts are loaded from external URLs
    /// 
    /// Puppeteer using the URL conversion strategy gives us the best chance for success here. While we've seen the Chrome
    /// process create higher fidelity PDFs (see the starwars output), this isn't always the case (see the custom fonts output)
    /// and occasionally doesn't work well at all (see the quake output).
    /// 
    /// Additionally, if issues do arise, we have more flexibility with Puppeteer than we do with the Chrome process given we can 
    /// fine tune some of the browser options.
    /// 
    /// Additional details regarding both processes:
    /// Chrome
    ///     - Will need fairly strong error handling, and background logic to make sure we are appropriately closing our chrome processes.
    ///     - Chrome adds margins to it's PDFs, as well as text at the top and bottom of the pages. Uncertain if that can be removed
    ///     - Chome is much slower compared to Puppeteer, but fidelity is occasionally higher
    /// 
    /// Puppeteer
    ///     - Occasionally see issues with elements that act as links. We see issues in the Starwars site and the fontspace custom fonts
    /// 
    /// CLOSING THOUGHTS
    /// - For article content, let's roll with puppeteer and do some heavy testing
    /// - For HtmlArchive content, if we ever need to handle it, we should not create PDF renditions, and only create preview images.
    /// 
    /// OPEN QUESTION
    /// - Why are we bothering with the PDF here? Is a preview good enough?
    /// </summary>
    class Program
    {
        private static Browser _browser;
        private const string ArticleApiToken = "eyJhbGciOiJSUzI1NiIsImtpZCI6IkI1NjdBNDM0ODBCM0I5MEI5OEE1NEU3Q0FEMTA3RjZCODU5QjIyQTIiLCJ0eXAiOiJhdCtqd3QiLCJ4NXQiOiJ0V2VrTklDenVRdVlwVTU4clJCX2E0V2JJcUkifQ.eyJuYmYiOjE2MzE2MzE3MzUsImV4cCI6MTYzMTY1MzMzNSwiaXNzIjoiaHR0cHM6Ly9hdXRoLWRldi5zZWlzbWljLWRldi5jb20vdGVuYW50cy91bml2ZXJzYWxwbGF5ZXIzIiwiYXVkIjpbImh0dHBzOi8vYXV0aC1kZXYuc2Vpc21pYy1kZXYuY29tL3RlbmFudHMvdW5pdmVyc2FscGxheWVyMy9yZXNvdXJjZXMiLCJwbGF5ZXJzZXJ2aWNlIl0sImNsaWVudF9pZCI6IjdhODk5MGZiLWI5MjAtNDUxMC1iZmM1LTYzYzk3Njg1YjM4NyIsInRlbmFudCI6InVuaXZlcnNhbHBsYXllcjMiLCJ0ZW5hbnRfZnFkbiI6InVuaXZlcnNhbHBsYXllcjMuc2Vpc21pYy1kZXYuY29tIiwidGVuYW50X2lkIjoiZGRiNzU4MDMtNTQyZC00ZGM3LWExNjEtZDQ4MGVkNGQ0M2I3IiwidG9rZW5fc291cmNlIjoidGVuYW50IiwiY2xpZW50X25hbWUiOiJQbGF5ZXIgU2VydmljZSBUZXN0IENsaWVudCIsImp0aSI6IjhxbjZCdE11R3VLX2tCYUdMUG9tY3ciLCJzY29wZSI6WyJwbGF5ZXJzZXJ2aWNlX3ZpZXdfYWxsIl19.Rs-FZSggyPdyuMgXHzeNhKtLdDnrMmZ2wK0oqsGH6DNm8a0V40xN1lQEf8w8ibs3IaroxFwSWVF0z5HRX106hUuY-gVenGEa9xjPlcpxwM76eBdcSYGQt5EOC8o9-pjhArCT3PD4cqD8IgJGcggDh8l0KGm9aXNPe82dTTwh7DwjFWORfC8My8nef4xzGjJqw2IDLB30uOI9QoCUfWAlMeuQerH2DlTNSCRnRGAqPgrHFBm8ruZt7OrVhK-jzyeNejnd_1xxlUy9zaY8cfXeI2eBb59nayG4GOiRZjAvhIR0cgocYXHmXw2dUszyIisvKKIHku9C6XZWNA7S5cDmUg";
        private const string ChromePath = @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe";
        private static string InputDirectory;
        private static string OutputDirectory;
        private static readonly HttpClient HttpClient = new HttpClient();
        private static readonly DateTime TestRunStart = DateTime.UtcNow;
        private static readonly Stopwatch Stopwatch = new Stopwatch();
        private const string ArticleApiUrl = "https://article-dev01-westus-az.seismic-dev.com/api/article/v3/tenants/universalplayer3/rendition/article/3748ab75-ad78-4368-8946-f0fe99a3b2a5";
        private const string CustomFontsUrl = "https://fonts.google.com"; // https://www.fontspace.com/category/custom
        private const string StarWarsUrl = "https://www.starwars.com";
        private const string QuakeUrl = "https://quake.seismic.com";
        static async Task Main(string[] args)
        {
            // Set JWT for HTTP Client
            HttpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ArticleApiToken);

            // Initialize input directory to save HTML content to
            InputDirectory = $"{Directory.GetCurrentDirectory()}/input";
            if (Directory.Exists(InputDirectory))
            {
                foreach (var file in Directory.GetFiles(InputDirectory))
                {
                    File.Delete(file);
                }
            }
            else
            {
                Directory.CreateDirectory(InputDirectory);
            }

            Console.WriteLine($"Input Directory: {InputDirectory}");

            OutputDirectory = $"{Directory.GetCurrentDirectory()}/output/{TestRunStart.ToFileTimeUtc()}";
            Directory.CreateDirectory(OutputDirectory);
            Console.WriteLine($"Output Directory: {OutputDirectory}");

            // Initialize Puppeteer
            var browserFetcher = new BrowserFetcher(new BrowserFetcherOptions());
            await browserFetcher.DownloadAsync();
            _browser = await Puppeteer.LaunchAsync(new LaunchOptions { DefaultViewport = ViewPortOptions.Default });

            //ConvertArticleApiChrome_Url(); <--- Do not know how to inject JWT Header to authenticate against article service
            await RunConversionTest("Article_Chrome_Html", () => RunChromeHtmlConversion(ArticleApiUrl, $"{InputDirectory}/article.html", $"{OutputDirectory}/CHROME_ArticleApi_Html.pdf"));
            await RunConversionTest("Article_Puppeteer_Url", () => RunPuppeteerUrlConversion(ArticleApiUrl, $"{OutputDirectory}/PUPPETEER_ArticleApi_Url.pdf"));
            await RunConversionTest("Article_Puppeteer_Html", () => RunPuppeteerHtmlConversion(ArticleApiUrl, $"{OutputDirectory}/PUPPETEER_ArticleApi_Html.pdf"));

            await RunConversionTest("CustomFonts_Chrome_Url", () => RunChromeUrlConversion(CustomFontsUrl, $"{OutputDirectory}/CHROME_CustomFonts_Url.pdf"));
            await RunConversionTest("CustomFonts_Chrome_Html", () => RunChromeHtmlConversion(CustomFontsUrl, $"{InputDirectory}/customfonts.html", $"{OutputDirectory}/CHROME_CustomFonts_Html.pdf"));
            await RunConversionTest("CustomFonts_Puppeteer_Url", () => RunPuppeteerUrlConversion(CustomFontsUrl, $"{OutputDirectory}/PUPPETEER_CustomFonts_Url.pdf"));
            await RunConversionTest("CustomFonts_Puppeteer_Html", () => RunPuppeteerHtmlConversion(CustomFontsUrl, $"{OutputDirectory}/PUPPETEER_CustomFonts_Html.pdf"));

            await RunConversionTest("StarWars_Chrome_Url", () => RunChromeUrlConversion(StarWarsUrl, $"{OutputDirectory}/CHROME_StarWars_Url.pdf"));
            await RunConversionTest("StarWars_Chrome_Html", () => RunChromeHtmlConversion(StarWarsUrl, $"{InputDirectory}/starwars.html", $"{OutputDirectory}/CHROME_StarWars_Html.pdf"));
            await RunConversionTest("StarWars_Puppeteer_Url", () => RunPuppeteerUrlConversion(StarWarsUrl, $"{OutputDirectory}/PUPPETEER_StarWars_Url.pdf"));
            await RunConversionTest("StarWars_Puppeteer_Html", () => RunPuppeteerHtmlConversion(StarWarsUrl, $"{OutputDirectory}/PUPPETEER_StarWars_Html.pdf"));

            await RunConversionTest("Quake_Chrome_Url", () => RunChromeUrlConversion(QuakeUrl, $"{OutputDirectory}/CHROME_Quake_Url.pdf"));
            await RunConversionTest("Quake_Chrome_Html", () => RunChromeHtmlConversion(QuakeUrl, $"{InputDirectory}/quake.html", $"{OutputDirectory}/CHROME_Quake_Html.pdf"));
            await RunConversionTest("Quake_Puppeteer_Url", () => RunPuppeteerUrlConversion(QuakeUrl, $"{OutputDirectory}/PUPPETEER_Quake_Url.pdf"));
            await RunConversionTest("Quake_Puppeteer_Html", () => RunPuppeteerHtmlConversion(QuakeUrl, $"{OutputDirectory}/PUPPETEER_Quake_Html.pdf"));

            await _browser.CloseAsync();
        }

        /// <summary>
        /// Takes a URL and feeds it to Puppeteer. Then creates a PDF out of it
        /// </summary>
        private static async Task RunPuppeteerUrlConversion(string url, string outputPath)
        {
            await using var page = await _browser.NewPageAsync();
            if (url.Contains("seismic"))
            {
                await page.SetExtraHttpHeadersAsync(new Dictionary<string, string> {
                    {"Authorization", $"Bearer {ArticleApiToken}"}
                });
            }
            await page.SetViewportAsync(new ViewPortOptions
            {
                Width = 1240,
                Height = 1754
            });
            await page.GoToAsync(url, new NavigationOptions
            {
                WaitUntil = new WaitUntilNavigation[] { WaitUntilNavigation.Networkidle0 }
            });
            await page.EvaluateFunctionHandleAsync("() => document.fonts.ready === true");
            await page.PdfAsync(outputPath);
            await page.CloseAsync();
        }

        /// <summary>
        /// Takes a URL, downloads the HTML to memory, and feeds the HTML content to Puppeteer. Then creates a PDF out of it
        /// 
        /// This would mimic HtmlArchive, where we don't have the original URL, rather we only have the HTML file
        /// </summary>
        private static async Task RunPuppeteerHtmlConversion(string url, string outputPath)
        {
            var html = await HttpClient.GetStringAsync(url);

            await using var page = await _browser.NewPageAsync();
            await page.SetViewportAsync(new ViewPortOptions
            {
                Width = 1240,
                Height = 1754
            });
            await page.SetContentAsync(html, new NavigationOptions
            {
                WaitUntil = new WaitUntilNavigation[] { WaitUntilNavigation.Networkidle0 }
            });
            await page.EvaluateFunctionHandleAsync("() => document.fonts.ready === true");
            await page.PdfAsync(outputPath);
            await page.CloseAsync();
        }

        /// <summary>
        /// Takes a URL and feeds it to Chrome. Then creates a PDF out of it
        /// </summary>
        private static async Task RunChromeUrlConversion(string url, string outputPath)
        {
            using (var p = new Process())
            {
                p.StartInfo.FileName = ChromePath;
                p.StartInfo.Arguments = $"--headless --print-to-pdf={outputPath} {url}";
                p.Start();
                p.WaitForExit();
            }
        }

        /// <summary>
        /// Takes a URL, downloads the HTML to memory, and feeds the HTML content to Chrome. Then creates a PDF out of it
        /// 
        /// This would mimic HtmlArchive, where we don't have the original URL, rather we only have the HTML file
        /// </summary>
        private static async Task RunChromeHtmlConversion(string url, string inputPath, string outputPath)
        {
            var html = await HttpClient.GetStringAsync(url);
            await File.WriteAllTextAsync(inputPath, html);

            using (var p = new Process())
            {
                p.StartInfo.FileName = ChromePath;
                p.StartInfo.Arguments = $"--headless --print-to-pdf={outputPath} {inputPath}";
                p.Start();
                p.WaitForExit();
            }
        }

        private static async Task RunConversionTest(string conversionName, Func<Task> conversion)
        {
            Console.WriteLine($"Starting {conversionName} Conversion");
            Stopwatch.Reset();
            Stopwatch.Start();
            await conversion();
            Stopwatch.Stop();
            Console.WriteLine($"Finished {conversionName} Conversion in {Stopwatch.ElapsedMilliseconds}ms");
        }
    }
}
