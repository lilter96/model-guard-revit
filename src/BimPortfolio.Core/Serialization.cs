using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BimPortfolio.Core;

public static class PlanJson
{
    const int MaximumCharacters = 8_000_000;
    static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
    {
        TypeNameHandling = TypeNameHandling.None,
        MaxDepth = 64,
        Formatting = Formatting.Indented,
        FloatParseHandling = FloatParseHandling.Double
    };
    public static string Write(RoutePlan plan)
    {
        Validate(plan);
        var json = JsonConvert.SerializeObject(plan, Settings);
        if (json.Length > MaximumCharacters) throw new JsonException("Plan exceeds the storage size limit.");
        return json;
    }
    public static RoutePlan Read(string json)
    {
        if (json == null || json.Length > MaximumCharacters) throw new ArgumentException("Plan is missing or exceeds the storage size limit.");
        JObject obj;
        using (var reader = new JsonTextReader(new System.IO.StringReader(json)) { MaxDepth = 64, DateParseHandling = DateParseHandling.None })
        {
            obj = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            while (reader.Read()) if (reader.TokenType != JsonToken.Comment) throw new JsonException("Trailing JSON content is not allowed.");
        }
        foreach (var node in obj.DescendantsAndSelf().OfType<JObject>())
            if (node.Properties().GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                throw new JsonException("Case-ambiguous JSON properties are not supported.");
        var token = Value(obj, "SchemaVersion");
        if (token == null || token.Type != JTokenType.Integer) throw new JsonException("Explicit schemaVersion is required.");
        var version = token.Value<int>();
        if (version < 1 || version > 3) throw new NotSupportedException("Unsupported schema version: " + version + ". Existing data must not be overwritten.");
        if (version == 1)
        {
            var migrated = new RoutePlan { ControllerUniqueId = (string?)Value(obj, "ControllerUniqueId") ?? "", CalculatedUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc) };
            var entries = Value(obj, "Routes") as JArray ?? throw new JsonException("v1 routes are required.");
            foreach (var item in entries)
            {
                var entry = item as JObject ?? throw new JsonException("Each legacy route must be an object.");
                var length = Value(entry, "LengthFeet");
                if (length == null || (length.Type != JTokenType.Float && length.Type != JTokenType.Integer)) throw new JsonException("lengthFeet is required.");
                var feet = length.Value<double>();
                if (!Point3.Finite(feet) || feet < 0) throw new JsonException("Invalid legacy length.");
                migrated.Routes.Add(new RouteResult
                {
                    DeviceUniqueId = (string?)Value(entry, "DeviceUniqueId") ?? "",
                    Mark = (string?)Value(entry, "Mark") ?? "",
                    Status = "Legacy",
                    CableMeters = feet * 0.3048
                });
            }
            Validate(migrated);
            return migrated;
        }
        if (!(Value(obj, "Routes") is JArray) || !(Value(obj, "Options") is JObject) || Value(obj, "ControllerUniqueId")?.Type != JTokenType.String)
            throw new JsonException("Plans require controllerUniqueId, options and routes.");
        if (version == 3 && Value(obj, "CalculationSignature")?.Type != JTokenType.String)
            throw new JsonException("v3 requires an explicit calculationSignature.");
        var plan = obj.ToObject<RoutePlan>(JsonSerializer.Create(Settings)) ?? throw new JsonException("Empty plan.");
        if (version == 2)
        {
            plan.SchemaVersion = 3;
            if (Value(obj, "CalculatedUtc") == null) plan.CalculatedUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);
            plan.CalculationSignature = "";
            // v2 has no geometry or snapshot hash. Preserve known lengths, never infer validity.
            foreach (var route in plan.Routes ?? new List<RouteResult>()) route.PathPoints = new List<Point3>();
        }
        Validate(plan);
        return plan;
    }
    static JToken? Value(JObject obj, string name) => obj.GetValue(name, StringComparison.OrdinalIgnoreCase);
    public static void Validate(RoutePlan plan)
    {
        if (plan == null || plan.SchemaVersion != 3 || string.IsNullOrWhiteSpace(plan.ControllerUniqueId) || plan.Options == null ||
            plan.Routes == null || plan.Notices == null || plan.Metrics == null || plan.CalculationSignature == null)
            throw new JsonException("Invalid plan.");
        plan.Options.Validate();
        if (plan.CalculationSignature.Length > 0 && !Regex.IsMatch(plan.CalculationSignature, "^[0-9a-f]{64}$"))
            throw new JsonException("Invalid snapshot signature.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in plan.Routes)
        {
            if (route == null || string.IsNullOrWhiteSpace(route.DeviceUniqueId) || route.DeviceUniqueId == plan.ControllerUniqueId ||
                !ids.Add(route.DeviceUniqueId) || route.Mark == null || route.NodeIds == null || route.TrayElementUniqueIds == null || route.PathPoints == null)
                throw new JsonException("Invalid route.");
            if (!new[] { "OK", "CableLimitExceeded", "Legacy", "Disconnected", "ControllerOutsideNetwork", "DeviceOutsideNetwork" }.Contains(route.Status))
                throw new JsonException("Unknown route status.");
            foreach (var length in new[] { route.PathMeters, route.CableMeters, route.AttachmentMeters })
                if (length.HasValue && (!Point3.Finite(length.Value) || length.Value < 0)) throw new JsonException("Invalid route length.");
            if (route.HasPath && (!route.PathMeters.HasValue || !route.CableMeters.HasValue)) throw new JsonException("Successful route requires lengths.");
            if (!route.HasPath && route.Status != "Legacy" && (route.PathMeters.HasValue || route.CableMeters.HasValue || route.PathPoints.Count > 0))
                throw new JsonException("Failed routes cannot contain a computed path.");
            if (route.PathPoints.Any(p => p == null || !p.IsFinite)) throw new JsonException("Invalid route geometry.");
            if (plan.CalculationSignature.Length > 0 && route.HasPath && route.PathPoints.Count == 0) throw new JsonException("Verified routes require geometry.");
        }
    }
}

public static class CsvReport
{
    public static string Routes(IEnumerable<RouteResult> routes) =>
        "DeviceUniqueId,Mark,Status,PathMeters,CableMeters,AttachmentMeters\r\n" +
        string.Join("\r\n", routes.Select(r => string.Join(",", new[] { Cell(r.DeviceUniqueId), Cell(r.Mark), Cell(r.Status), Number(r.PathMeters), Number(r.CableMeters), Number(r.AttachmentMeters) }))) + "\r\n";
    public static string Issues(IEnumerable<AuditIssue> issues) =>
        "ElementUniqueId,Code,Severity,Detail\r\n" +
        string.Join("\r\n", issues.Select(r => string.Join(",", new[] { Cell(r.ElementUniqueId), Cell(r.Code), Cell(r.Severity), Cell(r.Detail) }))) + "\r\n";
    static string Number(double? n) => n?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";
    public static string Cell(string s)
    {
        var trimmed = s.TrimStart();
        if (trimmed.Length > 0 && "=+-@".IndexOf(trimmed[0]) >= 0) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
