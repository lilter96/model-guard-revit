using System;
using System.Linq;
using BimPortfolio.Core;
using Newtonsoft.Json.Linq;
using Xunit;
namespace BimPortfolio.Core.Tests;
public class ReportTests
{
    [Fact]
    public void EmbeddedReportEscapesScriptTerminatorsAndRetainsStructuredData()
    {
        var graph = SegmentRoutingTests.Straight(); var controller = SegmentRoutingTests.D("c", 10); var device = SegmentRoutingTests.D("d", 50);
        device.Mark = "</script><script>alert('x')</script>";
        var snapshot = new RoutingSnapshot(graph, controller, new[] { device }); var plan = RoutePlanner.Calculate(graph, controller, snapshot.Devices, new());
        var html = HtmlReport.Create(snapshot, plan);
        Assert.DoesNotContain(device.Mark, html);
        var start = html.IndexOf("<script id=\"report-data\" type=\"application/json\">", StringComparison.Ordinal);
        start = html.IndexOf('>', start) + 1; var end = html.IndexOf("</script>", start, StringComparison.Ordinal);
        var json = JObject.Parse(html.Substring(start, end - start));
        Assert.Equal(device.Mark, (string?)json["Plan"]!["Routes"]![0]!["Mark"]);
        Assert.DoesNotContain("__REPORT_DATA__", html);
    }
}
