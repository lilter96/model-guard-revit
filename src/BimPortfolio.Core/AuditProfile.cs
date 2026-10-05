using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BimPortfolio.Core;

public enum ParameterScope { Instance, Type, InstanceOrType }
public enum ParameterKind { Any, String, Integer, Double, ElementId }
public enum ParameterReadState { Found, Missing, Ambiguous, WrongScope }

public sealed class ParameterValue
{
    public ParameterReadState State { get; set; }
    public ParameterKind Kind { get; set; }
    public string Text { get; set; } = "";
    public double? Number { get; set; }
    public bool HasValue { get; set; }
}
public sealed class ParameterRule
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string SharedParameterGuid { get; set; } = "";
    public ParameterScope Scope { get; set; } = ParameterScope.InstanceOrType;
    public ParameterKind Kind { get; set; } = ParameterKind.Any;
    public bool Required { get; set; } = true;
    public string Pattern { get; set; } = "";
    public List<string> AllowedValues { get; set; } = new List<string>();
    // Numeric ranges deliberately apply only to integer parameters, avoiding implicit unit conversions.
    public double? MinimumInteger { get; set; }
    public double? MaximumInteger { get; set; }
    public List<string> CategoryKeys { get; set; } = new List<string>();
    public bool Applies(ElementSnapshot element) => CategoryKeys.Count == 0 || CategoryKeys.Contains(element.CategoryKey, StringComparer.Ordinal);
}
public sealed class ParameterReferenceRule
{
    public string ParameterRuleId { get; set; } = "";
    public string TargetCategoryKey { get; set; } = "";
}
public sealed class AuditProfile
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "Custom audit";
    public List<ParameterRule> Parameters { get; set; } = new List<ParameterRule>();
    public List<ParameterReferenceRule> References { get; set; } = new List<ParameterReferenceRule>();
    public void Validate()
    {
        if (SchemaVersion != 1) throw new NotSupportedException("Unsupported audit profile version.");
        if (string.IsNullOrWhiteSpace(Name) || Parameters == null || References == null || Parameters.Count > 128)
            throw new ArgumentException("Profile requires a name and at most 128 parameter rules.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in Parameters)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id) ||
                (string.IsNullOrWhiteSpace(rule.Name) && string.IsNullOrWhiteSpace(rule.SharedParameterGuid)) ||
                rule.Pattern == null || rule.Pattern.Length > 512 || rule.AllowedValues == null || rule.CategoryKeys == null ||
                !Enum.IsDefined(typeof(ParameterScope), rule.Scope) || !Enum.IsDefined(typeof(ParameterKind), rule.Kind))
                throw new ArgumentException("Invalid or duplicate parameter rule.");
            if (!string.IsNullOrEmpty(rule.SharedParameterGuid) && (!Guid.TryParse(rule.SharedParameterGuid, out var guid) || guid == Guid.Empty))
                throw new ArgumentException("Shared parameter GUID is invalid: " + rule.Id);
            if (rule.Pattern.Length > 0) _ = new Regex(rule.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
            foreach (var bound in new[] { rule.MinimumInteger, rule.MaximumInteger })
                if (bound.HasValue && (!Point3.Finite(bound.Value) || bound.Value != Math.Truncate(bound.Value))) throw new ArgumentException("Integer bounds must be finite integers.");
            if ((rule.MinimumInteger.HasValue || rule.MaximumInteger.HasValue) && rule.Kind != ParameterKind.Integer)
                throw new ArgumentException("Numeric ranges require Integer kind.");
            if (rule.MinimumInteger.HasValue && rule.MaximumInteger.HasValue && rule.MinimumInteger > rule.MaximumInteger)
                throw new ArgumentException("Minimum exceeds maximum.");
        }
        foreach (var reference in References)
            if (reference == null || !ids.Contains(reference.ParameterRuleId) || string.IsNullOrWhiteSpace(reference.TargetCategoryKey))
                throw new ArgumentException("Reference rule requires a known parameter rule and a target category.");
    }
    public static AuditProfile FromNames(IEnumerable<string> names) => new AuditProfile
    {
        Parameters = names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).Distinct(StringComparer.Ordinal)
            .Select(n => new ParameterRule { Id = n, Name = n }).ToList()
    };
}
public static class AuditProfileJson
{
    public static AuditProfile Read(string json)
    {
        if (json == null || json.Length > 256_000) throw new ArgumentException("Audit profile is missing or too large.");
        var obj = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        foreach (var node in obj.DescendantsAndSelf().OfType<JObject>())
            if (node.Properties().GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new JsonException("Ambiguous profile property.");
        var version = obj.GetValue("SchemaVersion", StringComparison.OrdinalIgnoreCase);
        if (version == null || version.Type != JTokenType.Integer) throw new JsonException("Explicit profile schemaVersion is required.");
        if (version.Value<int>() != 1) throw new NotSupportedException("Unsupported audit profile version.");
        var profile = obj.ToObject<AuditProfile>(JsonSerializer.Create(new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None, MaxDepth = 32 })) ?? throw new JsonException("Empty profile.");
        profile.Validate(); return profile;
    }
}

public static class ProfileAuditor
{
    public static List<AuditIssue> Inspect(IEnumerable<ElementSnapshot> elements, AuditProfile profile)
    {
        profile.Validate();
        var snapshots = elements.ToList();
        var issues = ModelAuditor.Inspect(snapshots, Array.Empty<string>());
        foreach (var rule in profile.Parameters)
        {
            Regex? pattern = rule.Pattern.Length == 0 ? null : new Regex(rule.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
            foreach (var element in snapshots.Where(rule.Applies))
            {
                var found = element.TypedParameters.TryGetValue(rule.Id, out var value);
                if (!found || value!.State == ParameterReadState.Missing)
                { if (rule.Required) Add(element, "MissingParameter", rule.Id); continue; }
                if (value!.State == ParameterReadState.Ambiguous) { Add(element, "AmbiguousParameter", rule.Id); continue; }
                if (value.State == ParameterReadState.WrongScope) { Add(element, "ParameterScopeMismatch", rule.Id); continue; }
                if (rule.Kind != ParameterKind.Any && value.Kind != rule.Kind) { Add(element, "ParameterTypeMismatch", rule.Id + ": " + value.Kind + " → " + rule.Kind); continue; }
                if (!value.HasValue || (value.Kind == ParameterKind.String && string.IsNullOrWhiteSpace(value.Text)))
                { if (rule.Required) Add(element, "EmptyParameter", rule.Id); continue; }
                if (rule.AllowedValues.Count > 0 && !rule.AllowedValues.Contains(value.Text.Trim(), StringComparer.OrdinalIgnoreCase)) Add(element, "ParameterNotAllowed", rule.Id);
                if (pattern != null)
                {
                    try { if (!pattern.IsMatch(value.Text)) Add(element, "ParameterPatternMismatch", rule.Id); }
                    catch (RegexMatchTimeoutException) { Add(element, "RuleEvaluationTimeout", rule.Id); }
                }
                if (rule.MinimumInteger.HasValue || rule.MaximumInteger.HasValue)
                    if (!value.Number.HasValue || !Point3.Finite(value.Number.Value) ||
                       (rule.MinimumInteger.HasValue && value.Number < rule.MinimumInteger) ||
                       (rule.MaximumInteger.HasValue && value.Number > rule.MaximumInteger)) Add(element, "ParameterOutOfRange", rule.Id);
            }
        }
        foreach (var reference in profile.References)
        {
            var rule = profile.Parameters.Single(p => p.Id == reference.ParameterRuleId);
            var targets = snapshots.Where(e => e.CategoryKey == reference.TargetCategoryKey && !string.IsNullOrWhiteSpace(e.Mark))
                .GroupBy(e => e.Mark.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (var element in snapshots.Where(rule.Applies))
            {
                if (!element.TypedParameters.TryGetValue(rule.Id, out var value) || value.State != ParameterReadState.Found || !value.HasValue || string.IsNullOrWhiteSpace(value.Text)) continue;
                if (!targets.TryGetValue(value.Text.Trim(), out var count)) Add(element, "UnresolvedReference", rule.Id + " → " + value.Text);
                else if (count > 1) Add(element, "AmbiguousReference", rule.Id + " → " + value.Text);
            }
        }
        return issues.OrderBy(i => i.Code, StringComparer.Ordinal).ThenBy(i => i.ElementUniqueId, StringComparer.Ordinal).ToList();
        void Add(ElementSnapshot element, string code, string detail) => issues.Add(new AuditIssue
        {
            ElementUniqueId = element.UniqueId,
            Code = code,
            Detail = detail,
            Severity = "Error"
        });
    }
}
