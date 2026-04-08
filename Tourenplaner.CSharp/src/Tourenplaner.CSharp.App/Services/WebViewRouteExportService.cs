using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Tourenplaner.CSharp.Domain.Models;

namespace Tourenplaner.CSharp.App.Services;

public sealed class WebViewRouteExportService
{
    public async Task<byte[]?> CaptureMapImageAsync(RouteExportSnapshot snapshot)
    {
        try
        {
            return await ExecuteInHiddenWebViewAsync(async webView =>
            {
                await webView.CoreWebView2.ExecuteScriptAsync(
                    $"window.gawelaSetExportData({BuildMapDataJson(snapshot)});");

                await WaitUntilAsync(
                    webView,
                    "window.gawelaExportReady === true",
                    TimeSpan.FromSeconds(10));

                await Task.Delay(700);

                using var stream = new MemoryStream();
                await webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
                return stream.ToArray();
            }, BuildMapHtml(), 1400, 900);
        }
        catch
        {
            // PDF export should still work even if map preview rendering fails.
            return null;
        }
    }

    public async Task ExportPdfAsync(string html, string outputPath)
    {
        try
        {
            await ExecuteInHiddenWebViewAsync(async webView =>
            {
                var success = false;
                try
                {
                    var settings = webView.CoreWebView2.Environment.CreatePrintSettings();
                    settings.Orientation = CoreWebView2PrintOrientation.Landscape;
                    settings.ScaleFactor = 1.0;
                    settings.ShouldPrintBackgrounds = true;
                    settings.ShouldPrintHeaderAndFooter = false;
                    settings.MarginTop = 0.35;
                    settings.MarginBottom = 0.35;
                    settings.MarginLeft = 0.35;
                    settings.MarginRight = 0.35;
                    success = await webView.CoreWebView2.PrintToPdfAsync(outputPath, settings);
                }
                catch (ArgumentException)
                {
                    // Some WebView2 runtimes reject custom print settings with "Value does not fall within the expected range".
                    success = await webView.CoreWebView2.PrintToPdfAsync(outputPath);
                }
                catch (InvalidOperationException)
                {
                    // Retry with default print settings as a compatibility fallback.
                    success = await webView.CoreWebView2.PrintToPdfAsync(outputPath);
                }

                if (!success)
                {
                    throw new InvalidOperationException("Die PDF-Datei konnte nicht erzeugt werden.");
                }

                return 0;
            }, html, 1600, 1000);
        }
        catch
        {
            if (await TryExportPdfViaEdgeAsync(html, outputPath))
            {
                return;
            }

            throw;
        }
    }

    private static async Task<T> ExecuteInHiddenWebViewAsync<T>(Func<WebView2, Task<T>> action, string html, double width, double height)
    {
        var tcs = new TaskCompletionSource<T>();
        var window = new Window
        {
            Width = width,
            Height = height,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Left = -20000,
            Top = -20000,
            AllowsTransparency = false,
            ShowActivated = false,
            Content = new Grid()
        };

        var webView = new WebView2
        {
            Width = width,
            Height = height
        };

        ((Grid)window.Content).Children.Add(webView);

        try
        {
            window.Show();
            await webView.EnsureCoreWebView2Async();
            var navTcs = new TaskCompletionSource<bool>();
            void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                navTcs.TrySetResult(e.IsSuccess);
            }

            webView.CoreWebView2.NavigationCompleted += Handler;
            webView.NavigateToString(html);
            var navigationOk = await navTcs.Task;
            webView.CoreWebView2.NavigationCompleted -= Handler;

            if (!navigationOk)
            {
                throw new InvalidOperationException("Die Exportansicht konnte nicht geladen werden.");
            }

            await Task.Delay(300);
            return await action(webView);
        }
        finally
        {
            try
            {
                webView.Dispose();
            }
            catch
            {
            }

            window.Close();
        }
    }

    private static async Task WaitUntilAsync(WebView2 webView, string predicateScript, TimeSpan timeout)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            var raw = await webView.CoreWebView2.ExecuteScriptAsync(predicateScript);
            if (raw.Contains("true", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException("Die Kartenansicht konnte nicht rechtzeitig für den Export vorbereitet werden.");
    }

    private static string BuildMapDataJson(RouteExportSnapshot snapshot)
    {
        var sanitizedStops = snapshot.Stops
            .Where(x => IsFinite(x.Latitude) && IsFinite(x.Longitude))
            .ToList();
        var simplifiedGeometry = SimplifyGeometry(snapshot.GeometryPoints, maxPoints: 420);

        var payload = new
        {
            company = snapshot.Company is null
                ? null
                : new
                {
                    name = snapshot.Company.Name,
                    address = snapshot.Company.Address,
                    lat = snapshot.Company.Latitude,
                    lon = snapshot.Company.Longitude
                },
            stops = sanitizedStops.Select(x => new
            {
                position = x.Position,
                label = x.Label,
                name = x.Name,
                address = x.Address,
                lat = x.Latitude,
                lon = x.Longitude
            }),
            geometry = simplifiedGeometry.Select(x => new
            {
                lat = x.Latitude,
                lon = x.Longitude
            })
        };

        return JsonSerializer.Serialize(payload);
    }

    private static List<GeoPoint> SimplifyGeometry(IReadOnlyList<GeoPoint> points, int maxPoints)
    {
        if (points is null || points.Count == 0)
        {
            return new List<GeoPoint>();
        }

        var finite = points.Where(x => IsFinite(x.Latitude) && IsFinite(x.Longitude)).ToList();
        if (finite.Count <= maxPoints)
        {
            return finite;
        }

        var result = new List<GeoPoint>(maxPoints);
        var step = (finite.Count - 1d) / (maxPoints - 1d);
        for (var i = 0; i < maxPoints; i++)
        {
            var idx = (int)Math.Round(i * step, MidpointRounding.AwayFromZero);
            idx = Math.Clamp(idx, 0, finite.Count - 1);
            result.Add(finite[idx]);
        }

        return result;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static async Task<bool> TryExportPdfViaEdgeAsync(string html, string outputPath)
    {
        var edgePath = ResolveEdgePath();
        if (string.IsNullOrWhiteSpace(edgePath))
        {
            return false;
        }

        var tempHtmlPath = Path.Combine(Path.GetTempPath(), $"tourenplaner-export-{Guid.NewGuid():N}.html");
        try
        {
            await File.WriteAllTextAsync(tempHtmlPath, html);
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            var psi = new ProcessStartInfo
            {
                FileName = edgePath,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--headless=new");
            psi.ArgumentList.Add("--disable-gpu");
            psi.ArgumentList.Add("--allow-file-access-from-files");
            psi.ArgumentList.Add($"--print-to-pdf={outputPath}");
            psi.ArgumentList.Add(new Uri(tempHtmlPath).AbsoluteUri);

            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            var waitTask = process.WaitForExitAsync();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(45));
            var completed = await Task.WhenAny(waitTask, timeoutTask);
            if (completed != waitTask)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                return false;
            }

            await waitTask;
            if (process.ExitCode != 0)
            {
                return false;
            }

            var file = new FileInfo(outputPath);
            return file.Exists && file.Length > 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(tempHtmlPath))
                {
                    File.Delete(tempHtmlPath);
                }
            }
            catch
            {
            }
        }
    }

    private static string? ResolveEdgePath()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string BuildMapHtml()
    {
        return """
               <!doctype html>
               <html>
               <head>
                 <meta charset="utf-8" />
                 <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                 <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css" />
                 <style>
                   html, body, #map { height: 100%; margin: 0; padding: 0; }
                   body { font-family: Segoe UI, sans-serif; background: #fff; }
                   #map { background: #f8fafc; }
                 </style>
               </head>
               <body>
                 <div id="map"></div>
                 <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
                 <script>
                   const map = L.map('map', {
                     zoomControl: false,
                     attributionControl: false,
                     zoomSnap: 0.1,
                     zoomDelta: 0.5
                   }).setView([47.3769, 8.5417], 10);
                   const tileLayer = L.tileLayer('https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png', {
                     maxZoom: 20,
                     subdomains: 'abcd'
                   }).addTo(map);

                   let layerGroup = L.layerGroup().addTo(map);
                   window.gawelaExportReady = false;

                   function createMarkerIcon(label) {
                     return L.divIcon({
                       className: 'gawela-export-marker',
                       html: `<div style="width:26px;height:26px;border-radius:13px;background:#2563eb;color:#fff;font-size:12px;line-height:26px;text-align:center;border:2px solid #fff;box-shadow:0 0 0 1px #1e3a8a;font-weight:700;">${label}</div>`,
                       iconSize: [26, 26],
                       iconAnchor: [13, 13]
                     });
                   }

                   function createCompanyIcon() {
                     return L.divIcon({
                       className: 'gawela-export-company',
                       html: `<div style="width:30px;height:30px;border-radius:15px;background:#16a34a;color:#fff;font-size:15px;line-height:30px;text-align:center;border:2px solid #fff;box-shadow:0 0 0 1px #166534;">H</div>`,
                       iconSize: [30, 30],
                       iconAnchor: [15, 15]
                     });
                   }

                   window.gawelaSetExportData = function(data) {
                     window.gawelaExportReady = false;
                     layerGroup.clearLayers();
                     const bounds = [];

                     if (data.company && typeof data.company.lat === 'number' && typeof data.company.lon === 'number') {
                       L.marker([data.company.lat, data.company.lon], { icon: createCompanyIcon() })
                         .addTo(layerGroup)
                         .bindTooltip(data.company.name || 'Firma', { permanent: false });
                       bounds.push([data.company.lat, data.company.lon]);
                     }

                     const path = [];
                     (data.stops || []).forEach(stop => {
                       if (typeof stop.lat !== 'number' || typeof stop.lon !== 'number') {
                         return;
                       }

                       path.push([stop.lat, stop.lon]);
                       bounds.push([stop.lat, stop.lon]);
                       L.marker([stop.lat, stop.lon], { icon: createMarkerIcon(stop.label || '?') })
                         .addTo(layerGroup)
                         .bindTooltip(`<b>${stop.label || '?'}</b> ${stop.name || ''}<br/>${stop.address || ''}`);
                     });

                     const geometry = (data.geometry || []).map(point => [point.lat, point.lon]);
                     const finalPath = geometry.length > 1 ? geometry : path;
                     if (finalPath.length > 1) {
                       L.polyline(finalPath, { color: '#2563eb', weight: 5, opacity: 0.9 }).addTo(layerGroup);
                       finalPath.forEach(point => bounds.push(point));
                     }

                     if (bounds.length > 0) {
                       map.fitBounds(bounds, { padding: [18, 18], maxZoom: 16 });
                     }

                     setTimeout(() => {
                       map.invalidateSize();
                       window.gawelaExportReady = true;
                     }, 1400);
                   };
                 </script>
               </body>
               </html>
               """;
    }
}
