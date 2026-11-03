using System.IO;
using System.Reflection;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;
using Tourenplaner.CSharp.App.Services;

namespace Tourenplaner.CSharp.Tests.Application;

public sealed class WebViewRouteExportServiceTests
{
    [Fact]
    public async Task ExportDocumentLoadsAndPrintsBeyondNavigateToStringLimit()
    {
        // A separate profile prevents collisions with a running installed app
        // or WebView2 processes left by an earlier testhost.
        var previousProfile = Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER");
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER",
            Path.Combine(Path.GetTempPath(), "tourenplaner-export-tests", Guid.NewGuid().ToString("N")));
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                var outputPath = Path.Combine(Path.GetTempPath(), $"tour-export-test-{Guid.NewGuid():N}.pdf");
                string? documentPath = null;
                try
                {
                    // All WebViews share the application's cached COM environment
                    // and must therefore stay on the same STA dispatcher.
                    foreach (var paddingLength in new[] { 100, 3 * 1024 * 1024 })
                    {
                        var html = "<!doctype html><meta charset='utf-8'><h1>Tour Rapperswil-Jona</h1><!--"
                            + new string('x', paddingLength) + "-->";
                        var method = typeof(WebViewRouteExportService)
                            .GetMethod("ExecuteInHiddenWebViewAsync", BindingFlags.NonPublic | BindingFlags.Static)!
                            .MakeGenericMethod(typeof(bool));
                        Func<WebView2, Task<bool>> print = async webView =>
                        {
                            documentPath = new Uri(webView.CoreWebView2.Source).LocalPath;
                            Assert.Contains("Rapperswil-Jona", await webView.CoreWebView2.ExecuteScriptAsync("document.body.innerText"));
                            if (paddingLength > 2 * 1024 * 1024)
                            {
                                // Reproduce the original error in the same installed runtime.
                                Assert.Throws<ArgumentException>(() => webView.NavigateToString(html));
                            }
                            return await webView.CoreWebView2.PrintToPdfAsync(outputPath);
                        };
                        Assert.True(await (Task<bool>)method.Invoke(null, [print, html, 800d, 600d])!);
                        Assert.False(File.Exists(documentPath));
                        var bytes = await File.ReadAllBytesAsync(outputPath);
                        Assert.True(bytes.Length > 100);
                        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));

                        // Also exercise the public export, including custom print settings.
                        await new WebViewRouteExportService().ExportPdfAsync(html, outputPath);
                        bytes = await File.ReadAllBytesAsync(outputPath);
                        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(bytes, 0, 5));
                    }
                    completion.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
                finally
                {
                    File.Delete(outputPath);
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(120));
        }
        finally
        {
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", previousProfile);
        }
    }
}
