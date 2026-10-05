using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using BimPortfolio.Core;
using ParameterValue = BimPortfolio.Core.ParameterValue;
namespace BimPortfolio.Revit;
public static class ModelReader
{
    static Point3 Point(XYZ xyz) => new Point3(xyz.X * 0.3048, xyz.Y * 0.3048, xyz.Z * 0.3048);
    public static DevicePoint Device(FamilyInstance element)
    {
        if (!(element.Location is LocationPoint location)) throw new InvalidOperationException("A selected family must have a point location: " + element.Name);
        return new DevicePoint { UniqueId = element.UniqueId, Mark = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? element.Name, Position = Point(location.Point) };
    }
    public static TrayGraph Graph(Document document, CancellationToken cancellation = default)
    {
        var filter = new ElementMulticategoryFilter(new[] { BuiltInCategory.OST_CableTray, BuiltInCategory.OST_CableTrayFitting });
        var elements = new FilteredElementCollector(document).WherePasses(filter).WhereElementIsNotElementType().ToElements();
        var graph = new TrayGraph(); var connectors = new Dictionary<string, Connector>(StringComparer.Ordinal);
        var added = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in elements)
        {
            cancellation.ThrowIfCancellationRequested();
            ConnectorManager? manager = (e as CableTray)?.ConnectorManager ?? (e as FamilyInstance)?.MEPModel?.ConnectorManager;
            if (manager == null)
            {
                graph.Notices.Add(new NetworkNotice { Code = "MissingConnectorManager", ElementUniqueId = e.UniqueId });
                continue;
            }
            var physical = manager.Connectors.Cast<Connector>().Where(c => c.ConnectorType == ConnectorType.End).OrderBy(c => c.Id).ToList();
            foreach (var c in physical) { var id = Key(c); graph.Nodes.Add(new GraphNode { Id = id, ElementUniqueId = e.UniqueId, Position = Point(c.Origin) }); connectors.Add(id, c); }
            for (var i = 0; i < physical.Count; i++) for (var j = i + 1; j < physical.Count; j++)
                {
                    var curve = (e as CableTray)?.Location as LocationCurve;
                    var length = curve != null ? curve.Curve.Length * 0.3048 : physical[i].Origin.DistanceTo(physical[j].Origin) * 0.3048;
                    var geometry = curve == null ? new List<Point3>() : curve.Curve.Tessellate().Select(Point).ToList();
                    if (geometry.Count > 0 && geometry[0].DistanceTo(Point(physical[i].Origin)) > geometry[geometry.Count - 1].DistanceTo(Point(physical[i].Origin))) geometry.Reverse();
                    if (geometry.Count > 0) { geometry[0] = Point(physical[i].Origin); geometry[geometry.Count - 1] = Point(physical[j].Origin); }
                    var canAttach = curve?.Curve is Line && Math.Abs(Point(physical[i].Origin).DistanceTo(Point(physical[j].Origin)) - length) <= 1e-5;
                    Add(Key(physical[i]), Key(physical[j]), length, e.UniqueId, canAttach, geometry);
                    if (curve != null && !canAttach) graph.Notices.Add(new NetworkNotice { Code = "ConnectorOnlyAttachment", ElementUniqueId = e.UniqueId, Detail = "Nonlinear tray geometry" });
                }
        }
        foreach (var pair in connectors)
        {
            var c = pair.Value; if (!c.IsConnected) continue;
            foreach (Connector other in c.AllRefs)
            {
                if (other.Owner.Id == c.Owner.Id || other.ConnectorType != ConnectorType.End || !connectors.ContainsKey(Key(other))) continue;
                if (!c.IsConnectedTo(other)) continue;
                Add(pair.Key, Key(other), c.Origin.DistanceTo(other.Origin) * 0.3048, "", false, new List<Point3>());
            }
        }
        graph.Validate(); return graph;
        void Add(string a, string b, double length, string elementId, bool attachable, List<Point3> geometry)
        {
            var key = string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
            if (added.Add(key)) graph.Edges.Add(new GraphEdge { Id = key, From = a, To = b, LengthMeters = length, ElementUniqueId = elementId, SupportsInteriorAttachment = attachable, Geometry = geometry });
        }
    }
    static string Key(Connector c) => c.Owner.UniqueId + ":" + c.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public static List<ElementSnapshot> AuditElements(Document doc, IEnumerable<string> required)
    {
        var parameters = required.Distinct(StringComparer.Ordinal).ToList();
        var categories = new List<BuiltInCategory> { BuiltInCategory.OST_Doors, BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_ElectricalFixtures, BuiltInCategory.OST_DataDevices, BuiltInCategory.OST_CommunicationDevices, BuiltInCategory.OST_SpecialityEquipment };
        // Some discipline categories depend on the host release; include them when present.
        foreach (var name in new[] { "OST_SecurityDevices", "OST_FireAlarmDevices" })
            if (Enum.TryParse(name, out BuiltInCategory category) && !categories.Contains(category)) categories.Add(category);
        var filter = new ElementMulticategoryFilter(categories);
        return new FilteredElementCollector(doc).WherePasses(filter).WhereElementIsNotElementType().OfType<FamilyInstance>().Select(e =>
        {
            var mark = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            var snapshot = new ElementSnapshot { UniqueId = e.UniqueId, Category = e.Category?.Name ?? "", CategoryKey = CategoryKey(e), Family = e.Symbol?.FamilyName ?? "", Type = e.Symbol?.Name ?? "", Mark = mark?.AsString() ?? "", CanWriteMark = mark != null && !mark.IsReadOnly && mark.StorageType == StorageType.String };
            foreach (var name in parameters)
            {
                // Named checks allow instance or type values, but never pick an ambiguous duplicate.
                var matches = e.GetParameters(name); if (matches.Count == 0 && e.Symbol != null) matches = e.Symbol.GetParameters(name);
                if (matches.Count == 1) snapshot.Parameters[name] = matches[0].StorageType == StorageType.String ? matches[0].AsString() ?? "" : matches[0].AsValueString() ?? "";
            }
            return snapshot;
        }).ToList();
    }
    static string CategoryKey(Element element)
    {
        if (element.Category == null) return "";
#if REVIT2021 || REVIT2022 || REVIT2023
        return ((BuiltInCategory)element.Category.Id.IntegerValue).ToString();
#else
        return ((BuiltInCategory)element.Category.Id.Value).ToString();
#endif
    }
    public static List<ElementSnapshot> AuditElements(Document document, AuditProfile profile)
    {
        profile.Validate();
        var snapshots = AuditElements(document, Array.Empty<string>());
        foreach (var snapshot in snapshots)
        {
            var instance = (FamilyInstance)document.GetElement(snapshot.UniqueId);
            foreach (var rule in profile.Parameters.Where(rule => rule.Applies(snapshot)))
                snapshot.TypedParameters[rule.Id] = ReadParameter(instance, rule);
        }
        return snapshots;
    }
    static ParameterValue ReadParameter(FamilyInstance instance, ParameterRule rule)
    {
        IList<Parameter> Find(Element? element)
        {
            if (element == null) return new List<Parameter>();
            if (!string.IsNullOrEmpty(rule.SharedParameterGuid))
            {
                var parameter = element.get_Parameter(Guid.Parse(rule.SharedParameterGuid));
                return parameter == null ? new List<Parameter>() : new List<Parameter> { parameter };
            }
            return element.GetParameters(rule.Name);
        }
        var instances = Find(instance);
        var types = Find(instance.Symbol);
        var selected = rule.Scope == ParameterScope.Type ? types : instances;
        if (rule.Scope == ParameterScope.InstanceOrType && selected.Count == 0) selected = types;
        if (selected.Count == 0)
        {
            var elsewhere = rule.Scope == ParameterScope.Type ? instances : types;
            return new ParameterValue { State = elsewhere.Count > 0 && rule.Scope != ParameterScope.InstanceOrType ? ParameterReadState.WrongScope : ParameterReadState.Missing };
        }
        if (selected.Count != 1) return new ParameterValue { State = ParameterReadState.Ambiguous };
        var p = selected[0];
        var value = new ParameterValue { State = ParameterReadState.Found, HasValue = p.HasValue };
        switch (p.StorageType)
        {
            case StorageType.String: value.Kind = ParameterKind.String; value.Text = p.AsString() ?? ""; break;
            case StorageType.Integer: value.Kind = ParameterKind.Integer; value.Number = p.AsInteger(); value.Text = p.AsInteger().ToString(System.Globalization.CultureInfo.InvariantCulture); break;
            case StorageType.Double: value.Kind = ParameterKind.Double; value.Number = p.AsDouble(); value.Text = p.AsValueString() ?? ""; break;
            case StorageType.ElementId: value.Kind = ParameterKind.ElementId; value.HasValue = value.HasValue && p.AsElementId() != ElementId.InvalidElementId; value.Text = p.AsValueString() ?? ""; break;
            default: value.Kind = ParameterKind.Any; value.HasValue = false; break;
        }
        return value;
    }

}
