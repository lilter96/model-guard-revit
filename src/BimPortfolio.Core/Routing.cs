using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace BimPortfolio.Core;

public sealed class DevicePoint
{
    public string UniqueId { get; set; } = "";
    public string Mark { get; set; } = "";
    public Point3 Position { get; set; } = new Point3();
}

public sealed class RouteOptions
{
    public double MaxAttachmentMeters { get; set; } = 3;
    public double ReservePercent { get; set; } = 10;
    public double TerminationAllowanceMeters { get; set; } = 1;
    // Zero disables the user-specified length limit; no manufacturer limit is invented.
    public double MaxCableMeters { get; set; }
    public void Validate()
    {
        if (!Point3.Finite(MaxAttachmentMeters) || MaxAttachmentMeters <= 0 ||
           !Point3.Finite(ReservePercent) || ReservePercent < 0 || ReservePercent > 100 ||
           !Point3.Finite(TerminationAllowanceMeters) || TerminationAllowanceMeters < 0 ||
           !Point3.Finite(MaxCableMeters) || MaxCableMeters < 0)
            throw new ArgumentException("Attachment must be positive; reserve 0–100%; allowance and cable limit nonnegative. Values must be finite.");
    }
    public RouteOptions Copy() => new RouteOptions
    {
        MaxAttachmentMeters = MaxAttachmentMeters,
        ReservePercent = ReservePercent,
        TerminationAllowanceMeters = TerminationAllowanceMeters,
        MaxCableMeters = MaxCableMeters
    };
}

public sealed class RouteResult
{
    public string DeviceUniqueId { get; set; } = "";
    public string Mark { get; set; } = "";
    public string Status { get; set; } = "";
    public double? PathMeters { get; set; }
    public double? CableMeters { get; set; }
    public double? AttachmentMeters { get; set; }
    public NetworkAttachment? DeviceAttachment { get; set; }
    public List<string> TrayElementUniqueIds { get; set; } = new List<string>();
    public List<string> NodeIds { get; set; } = new List<string>();
    // Device -> controller, including both direct attachment leads.
    public List<Point3> PathPoints { get; set; } = new List<Point3>();
    [Newtonsoft.Json.JsonIgnore]
    public bool HasPath => Status == "OK" || Status == "CableLimitExceeded";
}

public sealed class RoutingMetrics
{
    public int NodeCount { get; set; }
    public int EdgeCount { get; set; }
    public int VirtualNodeCount { get; set; }
    public int ComponentCount { get; set; }
    public long CalculationMilliseconds { get; set; }
}

public sealed class RoutePlan
{
    public int SchemaVersion { get; set; } = 3;
    public string ControllerUniqueId { get; set; } = "";
    public string ControllerMark { get; set; } = "";
    public DateTime CalculatedUtc { get; set; } = DateTime.UtcNow;
    public string CalculationSignature { get; set; } = "";
    public RouteOptions Options { get; set; } = new RouteOptions();
    public RoutingMetrics Metrics { get; set; } = new RoutingMetrics();
    public NetworkAttachment? ControllerAttachment { get; set; }
    public List<NetworkNotice> Notices { get; set; } = new List<NetworkNotice>();
    public List<RouteResult> Routes { get; set; } = new List<RouteResult>();
}

public static class RoutePlanner
{
    public static RoutePlan Calculate(TrayGraph graph, DevicePoint controller, IEnumerable<DevicePoint> devices,
        RouteOptions options, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        options.Validate(); graph.Validate(); ValidateDevice(controller);
        var stopwatch = Stopwatch.StartNew();
        var input = devices.ToList();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var device in input)
        {
            ValidateDevice(device);
            if (!ids.Add(device.UniqueId) || device.UniqueId == controller.UniqueId)
                throw new ArgumentException("Devices must be unique and distinct from the controller.");
        }
        input = input.OrderBy(d => d.UniqueId, StringComparer.Ordinal).ToList();
        var result = new RoutePlan
        {
            ControllerUniqueId = controller.UniqueId,
            ControllerMark = controller.Mark,
            Options = options.Copy(),
            CalculationSignature = CalculationFingerprint.Compute(graph, controller, input, options, cancellation),
            Notices = graph.Notices.ToList(),
            Metrics = new RoutingMetrics { NodeCount = graph.Nodes.Count, EdgeCount = graph.Edges.Count }
        };
        var index = new NetworkSpatialIndex(graph, cancellation);
        var root = index.Nearest(controller.Position, options.MaxAttachmentMeters, cancellation);
        var taps = input.ToDictionary(d => d.UniqueId, d => index.Nearest(d.Position, options.MaxAttachmentMeters, cancellation), StringComparer.Ordinal);
        var split = GraphSubdivision.Split(graph, taps.Values.Concat(new[] { root }), cancellation);
        result.ControllerAttachment = root?.Copy();
        result.Metrics.VirtualNodeCount = split.Nodes.Count - graph.Nodes.Count;
        result.Metrics.ComponentCount = ComponentCount(split);
        var tree = root == null ? null : Dijkstra.From(split, root.NodeId, cancellation);
        var nodes = split.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        foreach (var device in input)
        {
            cancellation.ThrowIfCancellationRequested();
            var terminal = taps[device.UniqueId];
            var route = new RouteResult { DeviceUniqueId = device.UniqueId, Mark = device.Mark, DeviceAttachment = terminal?.Copy() };
            result.Routes.Add(route);
            if (root == null) { route.Status = "ControllerOutsideNetwork"; continue; }
            if (terminal == null) { route.Status = "DeviceOutsideNetwork"; continue; }
            var path = tree!.PathTo(terminal.NodeId);
            if (path == null) { route.Status = "Disconnected"; continue; }
            var attachment = root.DistanceMeters + terminal.DistanceMeters;
            var length = path.LengthMeters + attachment;
            if (!Point3.Finite(length)) throw new ArgumentException("Route length overflow.");
            var cable = (decimal)length * (1 + (decimal)options.ReservePercent / 100) + (decimal)options.TerminationAllowanceMeters;
            route.PathMeters = length;
            route.CableMeters = (double)(decimal.Ceiling(cable * 100) / 100);
            route.AttachmentMeters = attachment;
            route.Status = options.MaxCableMeters > 0 && route.CableMeters > options.MaxCableMeters ? "CableLimitExceeded" : "OK";
            route.NodeIds = path.NodeIds.Reverse().ToList();
            route.TrayElementUniqueIds = path.Edges.Select(e => e.ElementUniqueId)
                .Concat(route.NodeIds.Select(id => nodes[id].ElementUniqueId)).Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToList();
            route.PathPoints = Trace(path, nodes, controller.Position, device.Position);
        }
        result.Metrics.CalculationMilliseconds = stopwatch.ElapsedMilliseconds;
        return result;
    }
    static List<Point3> Trace(PathResult path, Dictionary<string, GraphNode> nodes, Point3 controller, Point3 device)
    {
        var points = new List<Point3> { controller.Copy(), nodes[path.NodeIds[0]].Position.Copy() };
        for (var i = 0; i < path.Edges.Count; i++)
        {
            var edge = path.Edges[i];
            var geometry = edge.Geometry.Count == 0
                ? new[] { nodes[path.NodeIds[i]].Position, nodes[path.NodeIds[i + 1]].Position }
                : edge.From == path.NodeIds[i] ? (IEnumerable<Point3>)edge.Geometry : edge.Geometry.AsEnumerable().Reverse();
            foreach (var point in geometry) if (points[points.Count - 1].DistanceTo(point) > 1e-9) points.Add(point.Copy());
        }
        if (points[points.Count - 1].DistanceTo(device) > 1e-9) points.Add(device.Copy());
        points.Reverse();
        return points;
    }
    static int ComponentCount(TrayGraph graph)
    {
        var parent = graph.Nodes.ToDictionary(n => n.Id, n => n.Id, StringComparer.Ordinal);
        string Root(string id) { while (parent[id] != id) { parent[id] = parent[parent[id]]; id = parent[id]; } return id; }
        foreach (var edge in graph.Edges) { var a = Root(edge.From); var b = Root(edge.To); if (a != b) parent[a] = b; }
        return parent.Keys.ToList().Select(Root).Distinct(StringComparer.Ordinal).Count();
    }
    static void ValidateDevice(DevicePoint device)
    {
        if (device == null || string.IsNullOrWhiteSpace(device.UniqueId) || device.Mark == null || device.Position == null || !device.Position.IsFinite)
            throw new ArgumentException("Device requires ID, mark and finite position.");
    }
}
