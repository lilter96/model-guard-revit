using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace BimPortfolio.Core;

public static class HtmlReport
{
    public static string Create(RoutingSnapshot snapshot, RoutePlan plan, IReadOnlyList<AuditIssue>? issues = null)
    {
        PlanJson.Validate(plan);
        var payload = new { Plan = plan, Graph = snapshot.Graph, Controller = snapshot.Controller, Devices = snapshot.Devices, Issues = issues ?? Array.Empty<AuditIssue>() };
        // Escaping HTML in JSON prevents a parameter value from terminating the data script element.
        var json = JsonConvert.SerializeObject(payload, new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            StringEscapeHandling = StringEscapeHandling.EscapeHtml
        });
        using (var stream = typeof(HtmlReport).Assembly.GetManifestResourceStream("BimPortfolio.Core.route-report.html") ?? throw new InvalidOperationException("Report template is missing."))
        using (var reader = new StreamReader(stream)) return reader.ReadToEnd().Replace("__REPORT_DATA__", json);
    }
}
