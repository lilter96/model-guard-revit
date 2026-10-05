using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BimPortfolio.Core;

public sealed class SchematicLine
{
    public Point3 Start { get; set; } = new Point3();
    public Point3 End { get; set; } = new Point3();
}
public sealed class SchematicLabel
{
    public Point3 Position { get; set; } = new Point3();
    public double WidthMeters { get; set; }
    public string Text { get; set; } = "";
}
public sealed class SchematicPage
{
    public int Index { get; set; }
    public List<SchematicLine> Lines { get; } = new List<SchematicLine>();
    public List<SchematicLabel> Labels { get; } = new List<SchematicLabel>();
    public List<string> DeviceUniqueIds { get; } = new List<string>();
}
// Layout coordinates are metres on paper, independent of Revit and view scale.
public static class SchematicLayout
{
    public static List<SchematicPage> Build(RoutePlan plan, int rowsPerPage = 18, string subtitle = "Logical connections • cable estimates")
    {
        PlanJson.Validate(plan);
        if (rowsPerPage < 1 || rowsPerPage > 30) throw new ArgumentOutOfRangeException(nameof(rowsPerPage));
        var routes = plan.Routes.OrderBy(r => r.Mark, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.DeviceUniqueId, StringComparer.Ordinal).ToList();
        var pages = new List<SchematicPage>();
        for (var start = 0; start < Math.Max(1, routes.Count); start += rowsPerPage)
        {
            var page = new SchematicPage { Index = pages.Count }; pages.Add(page);
            page.Labels.Add(new SchematicLabel
            {
                Position = new Point3(0, 0, 0),
                WidthMeters = .28,
                Text = "AccessRoute / " + plan.ControllerMark + " / " + (page.Index + 1).ToString(CultureInfo.InvariantCulture)
            });
            page.Labels.Add(new SchematicLabel
            {
                Position = new Point3(0, -.012, 0),
                WidthMeters = .28,
                Text = subtitle + " • " + plan.CalculatedUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
            });
            var subset = routes.Skip(start).Take(rowsPerPage).ToList();
            if (subset.Count == 0) continue;
            Line(.02, -.03, .02, -.042 - (subset.Count - 1) * .028);
            for (var row = 0; row < subset.Count; row++)
            {
                var route = subset[row]; var y = -.042 - row * .028;
                page.DeviceUniqueIds.Add(route.DeviceUniqueId);
                Line(.02, y, .09, y);
                Line(.09, y - .008, .26, y - .008); Line(.26, y - .008, .26, y + .008);
                Line(.26, y + .008, .09, y + .008); Line(.09, y + .008, .09, y - .008);
                var mark = route.Mark.Length <= 48 ? route.Mark : route.Mark.Substring(0, 45) + "…";
                page.Labels.Add(new SchematicLabel
                {
                    Position = new Point3(.095, y + .006, 0),
                    WidthMeters = .16,
                    Text = mark + " / " + route.Status + " / " + (route.CableMeters?.ToString("0.##", CultureInfo.InvariantCulture) ?? "—") + " m"
                });
            }
            void Line(double x1, double y1, double x2, double y2) => page.Lines.Add(new SchematicLine { Start = new Point3(x1, y1, 0), End = new Point3(x2, y2, 0) });
        }
        return pages;
    }
}
