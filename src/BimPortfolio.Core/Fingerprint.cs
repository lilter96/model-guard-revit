using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace BimPortfolio.Core;

public static class CalculationFingerprint
{
    public static string Compute(TrayGraph graph, DevicePoint controller, IEnumerable<DevicePoint> devices,
        RouteOptions options, CancellationToken cancellation = default)
    {
        graph.Validate(); options.Validate();
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                writer.Write("AccessRoute/snapshot/1");
                writer.Write(graph.Nodes.Count);
                foreach (var node in graph.Nodes.OrderBy(n => n.Id, StringComparer.Ordinal))
                {
                    cancellation.ThrowIfCancellationRequested();
                    writer.Write(node.Id); writer.Write(node.ElementUniqueId); WritePoint(writer, node.Position);
                }
                var edges = graph.Edges.Select(e => new { Edge = e, From = Min(e.From, e.To), To = Max(e.From, e.To) })
                    .OrderBy(e => e.From, StringComparer.Ordinal).ThenBy(e => e.To, StringComparer.Ordinal)
                    .ThenBy(e => e.Edge.Id, StringComparer.Ordinal).ThenBy(e => e.Edge.LengthMeters).ToList();
                writer.Write(edges.Count);
                foreach (var item in edges)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var edge = item.Edge;
                    writer.Write(item.From); writer.Write(item.To); writer.Write(edge.Id); writer.Write(edge.ElementUniqueId);
                    WriteDouble(writer, edge.LengthMeters); writer.Write(edge.SupportsInteriorAttachment);
                    writer.Write(edge.Geometry.Count);
                    var geometry = item.From == edge.From ? (IEnumerable<Point3>)edge.Geometry : edge.Geometry.AsEnumerable().Reverse();
                    foreach (var point in geometry) WritePoint(writer, point);
                }
                WriteDevice(writer, controller);
                var endpoints = devices.OrderBy(d => d.UniqueId, StringComparer.Ordinal).ToList();
                writer.Write(endpoints.Count);
                foreach (var device in endpoints) WriteDevice(writer, device);
                WriteDouble(writer, options.MaxAttachmentMeters); WriteDouble(writer, options.ReservePercent);
                WriteDouble(writer, options.TerminationAllowanceMeters); WriteDouble(writer, options.MaxCableMeters);
            }
            cancellation.ThrowIfCancellationRequested();
            using (var hash = SHA256.Create()) return string.Concat(hash.ComputeHash(stream.ToArray()).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
        }
    }
    static void WriteDevice(BinaryWriter writer, DevicePoint device)
    { writer.Write(device.UniqueId); writer.Write(device.Mark); WritePoint(writer, device.Position); }
    static void WritePoint(BinaryWriter writer, Point3 point)
    { WriteDouble(writer, point.X); WriteDouble(writer, point.Y); WriteDouble(writer, point.Z); }
    static void WriteDouble(BinaryWriter writer, double value) => writer.Write(value == 0 ? 0 : value);
    static string Min(string a, string b) => string.CompareOrdinal(a, b) <= 0 ? a : b;
    static string Max(string a, string b) => string.CompareOrdinal(a, b) >= 0 ? a : b;
}

public enum PlanFreshness { Current, Stale, Unknown }

public sealed class RoutingSnapshot
{
    public TrayGraph Graph { get; }
    public DevicePoint Controller { get; }
    public IReadOnlyList<DevicePoint> Devices { get; }
    public RoutingSnapshot(TrayGraph graph, DevicePoint controller, IReadOnlyList<DevicePoint> devices)
    { Graph = graph; Controller = controller; Devices = devices; }
}

// Ports contain domain DTOs only. The Revit adapter and test doubles implement them.
public interface IRoutingSnapshotSource
{
    RoutingSnapshot Capture(string controllerUniqueId, IReadOnlyList<string> deviceUniqueIds, CancellationToken cancellation);
}
public interface IRoutePlanRepository
{
    RoutePlan? Load();
    void Save(RoutePlan plan);
}

public sealed class RoutingRun
{
    public RoutingSnapshot Snapshot { get; }
    public RoutePlan Plan { get; }
    public RoutingRun(RoutingSnapshot snapshot, RoutePlan plan) { Snapshot = snapshot; Plan = plan; }
}

public sealed class RoutingWorkflow
{
    readonly IRoutingSnapshotSource source;
    readonly IRoutePlanRepository repository;
    public RoutingWorkflow(IRoutingSnapshotSource source, IRoutePlanRepository repository)
    { this.source = source; this.repository = repository; }
    public RoutePlan Calculate(string controller, IReadOnlyList<string> devices, RouteOptions options, CancellationToken cancellation = default)
    {
        return Run(controller, devices, options, cancellation).Plan;
    }
    public RoutingRun Run(string controller, IReadOnlyList<string> devices, RouteOptions options, CancellationToken cancellation = default)
    {
        options.Validate();
        var snapshot = source.Capture(controller, devices, cancellation);
        return new RoutingRun(snapshot, RoutePlanner.Calculate(snapshot.Graph, snapshot.Controller, snapshot.Devices, options, cancellation));
    }
    public PlanFreshness Freshness(RoutePlan plan, CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(plan.CalculationSignature)) return PlanFreshness.Unknown;
        var snapshot = source.Capture(plan.ControllerUniqueId, plan.Routes.Select(r => r.DeviceUniqueId).ToList(), cancellation);
        return plan.CalculationSignature == CalculationFingerprint.Compute(snapshot.Graph, snapshot.Controller, snapshot.Devices, plan.Options, cancellation)
            ? PlanFreshness.Current : PlanFreshness.Stale;
    }
    public void Save(RoutePlan plan, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        PlanJson.Validate(plan);
        if (Freshness(plan, cancellation) != PlanFreshness.Current)
            throw new InvalidOperationException("The model changed or the plan has no verified snapshot. Recalculate before saving.");
        // Repository performs its own schema-version and transaction checks.
        repository.Save(plan);
    }
}
