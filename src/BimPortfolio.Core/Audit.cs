using System;
using System.Collections.Generic;
using System.Linq;
namespace BimPortfolio.Core;
public sealed class ElementSnapshot
{
    public string UniqueId { get; set; } = "";
    public string CategoryKey { get; set; } = "";
    public Dictionary<string, ParameterValue> TypedParameters { get; set; } = new Dictionary<string, ParameterValue>(StringComparer.Ordinal);
    public string Category { get; set; } = "";
    public string Family { get; set; } = "";
    public string Type { get; set; } = "";
    public string Mark { get; set; } = "";
    public Dictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public bool CanWriteMark { get; set; }
}
public sealed class AuditIssue
{
    public string ElementUniqueId { get; set; } = "";
    public string Code { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Severity { get; set; } = "Warning";
}
public sealed class MarkChange
{
    public string ElementUniqueId { get; set; } = "";
    public string OldMark { get; set; } = "";
    public string NewMark { get; set; } = "";
}
public static class ModelAuditor
{
    public static List<AuditIssue> Inspect(IEnumerable<ElementSnapshot> elements, IEnumerable<string> requiredParameters)
    {
        var input = Validate(elements); var required = requiredParameters.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal).ToList();
        var issues = new List<AuditIssue>();
        foreach (var e in input)
        {
            if (string.IsNullOrWhiteSpace(e.Mark)) issues.Add(Issue(e, "MissingMark", e.CanWriteMark ? "Writable" : "ReadOnly"));
            if (string.IsNullOrWhiteSpace(e.Family) || string.IsNullOrWhiteSpace(e.Type)) issues.Add(Issue(e, "MissingFamilyType", e.Category));
            foreach (var p in required) if (!e.Parameters.TryGetValue(p, out var v) || string.IsNullOrWhiteSpace(v)) issues.Add(Issue(e, "MissingParameter", p));
        }
        foreach (var group in input.Where(e => !string.IsNullOrWhiteSpace(e.Mark)).GroupBy(e => e.Mark.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var e in group) issues.Add(Issue(e, "DuplicateMark", group.Key));
        return issues.OrderBy(i => i.Code, StringComparer.Ordinal).ThenBy(i => i.ElementUniqueId, StringComparer.Ordinal).ToList();
    }
    public static List<MarkChange> ProposeMissingMarks(IEnumerable<ElementSnapshot> elements, string prefix = "ACS-")
    {
        if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("Prefix is required.");
        var input = Validate(elements); var used = new HashSet<string>(input.Select(e => e.Mark.Trim()), StringComparer.OrdinalIgnoreCase);
        var changes = new List<MarkChange>(); var n = 1;
        foreach (var e in input.Where(e => string.IsNullOrWhiteSpace(e.Mark) && e.CanWriteMark).OrderBy(e => e.UniqueId, StringComparer.Ordinal))
        {
            string mark; do { mark = prefix + (n++).ToString("D3", System.Globalization.CultureInfo.InvariantCulture); } while (!used.Add(mark));
            changes.Add(new MarkChange { ElementUniqueId = e.UniqueId, OldMark = e.Mark, NewMark = mark });
        }
        return changes;
    }
    static List<ElementSnapshot> Validate(IEnumerable<ElementSnapshot> elements)
    {
        var input = elements.ToList(); var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in input) if (e == null || string.IsNullOrWhiteSpace(e.UniqueId) || !ids.Add(e.UniqueId) || e.Mark == null || e.Parameters == null || e.TypedParameters == null) throw new ArgumentException("Invalid or duplicate element snapshot.");
        return input;
    }
    static AuditIssue Issue(ElementSnapshot e, string code, string detail) => new AuditIssue { ElementUniqueId = e.UniqueId, Code = code, Detail = detail };
}
