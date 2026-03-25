using System.Net;
using System.Text;

namespace Tourenplaner.CSharp.App.Services;

public static class TourPdfHtmlBuilder
{
    public static string Build(RouteExportSnapshot snapshot, string? mapImageBase64Png)
    {
        var title = string.IsNullOrWhiteSpace(snapshot.TourName) ? "Tour-Export" : snapshot.TourName.Trim();
        var meta = BuildMeta(snapshot);
        var stops = string.Join(Environment.NewLine, snapshot.Stops.Select(BuildStopHtml));
        var mapContent = string.IsNullOrWhiteSpace(mapImageBase64Png)
            ? "<div class=\"map-fallback\">Kartenbild konnte fuer diesen Export nicht erzeugt werden.</div>"
            : $"<img class=\"map-image\" src=\"data:image/png;base64,{mapImageBase64Png}\" alt=\"Tourkarte\" />";

        return $$"""
                 <!doctype html>
                 <html lang="de">
                 <head>
                   <meta charset="utf-8" />
                   <title>{{Html(title)}}</title>
                   <style>
                     @page { size: A4 landscape; margin: 14mm; }
                     * { box-sizing: border-box; }
                     body {
                       margin: 0;
                       font-family: "Segoe UI", Arial, sans-serif;
                       color: #0f172a;
                       background: #ffffff;
                     }
                     .page {
                       display: flex;
                       flex-direction: column;
                       gap: 18px;
                     }
                     .header {
                       display: flex;
                       justify-content: space-between;
                       align-items: flex-start;
                       gap: 24px;
                       border-bottom: 2px solid #e2e8f0;
                       padding-bottom: 12px;
                     }
                     .title {
                       font-size: 28px;
                       font-weight: 700;
                       color: #0f172a;
                       margin: 0;
                     }
                     .subtitle {
                       margin-top: 6px;
                       font-size: 14px;
                       color: #475569;
                     }
                     .meta {
                       min-width: 300px;
                       display: grid;
                       grid-template-columns: 130px 1fr;
                       gap: 8px 12px;
                       font-size: 13px;
                     }
                     .meta-label {
                       color: #475569;
                       font-weight: 600;
                     }
                     .content {
                       display: grid;
                       grid-template-columns: 0.95fr 1.35fr;
                       gap: 18px;
                       min-height: 520px;
                     }
                     .panel {
                       border: 1px solid #cbd5e1;
                       border-radius: 14px;
                       padding: 16px;
                       background: #ffffff;
                     }
                     .panel-title {
                       font-size: 16px;
                       font-weight: 700;
                       margin: 0 0 12px 0;
                       color: #0f172a;
                     }
                     .stops {
                       display: flex;
                       flex-direction: column;
                       gap: 10px;
                     }
                     .stop {
                       display: grid;
                       grid-template-columns: 34px 1fr;
                       gap: 10px;
                       padding: 10px 12px;
                       border-radius: 12px;
                       background: #f8fafc;
                       border: 1px solid #e2e8f0;
                     }
                     .marker {
                       width: 28px;
                       height: 28px;
                       border-radius: 14px;
                       background: #2563eb;
                       color: #ffffff;
                       display: flex;
                       align-items: center;
                       justify-content: center;
                       font-weight: 700;
                       font-size: 13px;
                       margin-top: 2px;
                     }
                     .stop-name {
                       font-size: 14px;
                       font-weight: 700;
                       color: #0f172a;
                       margin-bottom: 4px;
                     }
                     .stop-address,
                     .stop-extra {
                       font-size: 12px;
                       color: #334155;
                       line-height: 1.45;
                       white-space: pre-line;
                     }
                     .map-shell {
                       display: flex;
                       align-items: stretch;
                       justify-content: center;
                       min-height: 100%;
                       border-radius: 12px;
                       overflow: hidden;
                       background: #f8fafc;
                       border: 1px solid #e2e8f0;
                     }
                     .map-image {
                       width: 100%;
                       height: 100%;
                       object-fit: contain;
                       display: block;
                       background: #ffffff;
                     }
                     .map-fallback {
                       width: 100%;
                       min-height: 520px;
                       display: flex;
                       align-items: center;
                       justify-content: center;
                       padding: 24px;
                       text-align: center;
                       color: #64748b;
                       font-size: 14px;
                     }
                   </style>
                 </head>
                 <body>
                   <div class="page">
                     <div class="header">
                       <div>
                         <h1 class="title">{{Html(title)}}</h1>
                         <div class="subtitle">Tour-Export fuer die aktuell geladene Route</div>
                       </div>
                       <div class="meta">
                         {{meta}}
                       </div>
                     </div>
                     <div class="content">
                       <div class="panel">
                         <div class="panel-title">Stoppliste</div>
                         <div class="stops">
                           {{stops}}
                         </div>
                       </div>
                       <div class="panel">
                         <div class="panel-title">Kartenansicht</div>
                         <div class="map-shell">
                           {{mapContent}}
                         </div>
                       </div>
                     </div>
                   </div>
                 </body>
                 </html>
                 """;
    }

    private static string BuildMeta(RouteExportSnapshot snapshot)
    {
        var entries = new List<(string Label, string Value)>
        {
            ("Tour", string.IsNullOrWhiteSpace(snapshot.TourName) ? "Aktuelle Route" : snapshot.TourName.Trim()),
            ("Datum", string.IsNullOrWhiteSpace(snapshot.TourDate) ? "n/a" : snapshot.TourDate.Trim()),
            ("Start", string.IsNullOrWhiteSpace(snapshot.StartTime) ? "n/a" : snapshot.StartTime.Trim()),
            ("Stopps", snapshot.Stops.Count.ToString())
        };

        if (!string.IsNullOrWhiteSpace(snapshot.VehicleLabel))
        {
            entries.Add(("Fahrzeug", snapshot.VehicleLabel!.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(snapshot.TrailerLabel))
        {
            entries.Add(("Anhänger", snapshot.TrailerLabel!.Trim()));
        }

        return string.Join(Environment.NewLine, entries.Select(x =>
            $"<div class=\"meta-label\">{Html(x.Label)}</div><div>{Html(x.Value)}</div>"));
    }

    private static string BuildStopHtml(RouteExportStopInfo stop)
    {
        var extras = new List<string>();
        if (!string.IsNullOrWhiteSpace(stop.OrderNumber))
        {
            extras.Add($"Auftrag: {stop.OrderNumber.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(stop.TimeWindow))
        {
            extras.Add($"Zeitfenster: {stop.TimeWindow.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(stop.Arrival))
        {
            extras.Add($"ETA: {stop.Arrival.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(stop.WeightText))
        {
            extras.Add($"Gewicht: {stop.WeightText.Trim()}");
        }

        var extraHtml = extras.Count == 0
            ? string.Empty
            : $"<div class=\"stop-extra\">{Html(string.Join(" | ", extras))}</div>";

        return $$"""
                 <div class="stop">
                   <div class="marker">{{Html(stop.Label)}}</div>
                   <div>
                     <div class="stop-name">{{Html(stop.Name)}}</div>
                     <div class="stop-address">{{Html(stop.Address)}}</div>
                     {{extraHtml}}
                   </div>
                 </div>
                 """;
    }

    private static string Html(string? value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }
}
