using System;
using System.Linq;
using System.Threading;
using BimPortfolio.Core;
using Xunit;

namespace BimPortfolio.Core.Tests;
public class SegmentRoutingTests
{
    internal static TrayGraph Straight(double length = 100) => new TrayGraph
    {
        Nodes = new() { new() { Id = "a", ElementUniqueId = "tray", Position = new(0, 0, 0) }, new() { Id = "b", ElementUniqueId = "tray", Position = new(length, 0, 0) } },
        Edges = new() { new() { Id = "segment", From = "a", To = "b", ElementUniqueId = "tray", LengthMeters = length, SupportsInteriorAttachment = true } }
    };
    internal static DevicePoint D(string id, double x, double y = 0, double z = 0) => new() { UniqueId = id, Mark = id, Position = new(x, y, z) };
    [Fact]
    public void AttachesToMidpointEvenWhenBothConnectorsAreOutOfRange()
    {
        var plan = RoutePlanner.Calculate(Straight(), D("controller", 50, 1), new[] { D("reader", 60, 1) }, new RouteOptions());
        var route = Assert.Single(plan.Routes);
        Assert.Equal("OK", route.Status); Assert.Equal(12, route.PathMeters!.Value, 8); Assert.Equal(10, route.DeviceAttachment!.Fraction * 100 - 50);
        Assert.Equal(2, plan.Metrics.VirtualNodeCount); Assert.Equal(2, route.AttachmentMeters);
        Assert.Equal(60, route.PathPoints.First().X); Assert.Equal(50, route.PathPoints.Last().X);
    }
    [Fact]
    public void MultipleTapsOnOneEdgeUseExactSubsegmentsAndDoNotMutateInput()
    {
        var graph = Straight();
        var plan = RoutePlanner.Calculate(graph, D("c", 10), new[] { D("d1", 20), D("d2", 30), D("d3", 20) }, new RouteOptions { ReservePercent = 0, TerminationAllowanceMeters = 0 });
        Assert.Equal(new double?[] { 10, 20, 10 }, plan.Routes.Select(r => r.PathMeters));
        Assert.Equal(3, plan.Metrics.VirtualNodeCount); Assert.Equal(2, graph.Nodes.Count); Assert.Single(graph.Edges);
    }
    [Fact]
    public void GeometricallyCrossingSegmentsRemainDisconnected()
    {
        var graph = Straight(10);
        graph.Nodes.Add(new() { Id = "c", ElementUniqueId = "other", Position = new(5, -5, 0) });
        graph.Nodes.Add(new() { Id = "d", ElementUniqueId = "other", Position = new(5, 5, 0) });
        graph.Edges.Add(new() { Id = "crossing", From = "c", To = "d", ElementUniqueId = "other", LengthMeters = 10, SupportsInteriorAttachment = true });
        var plan = RoutePlanner.Calculate(graph, D("controller", 2, .1), new[] { D("reader", 5, 3) }, new RouteOptions { MaxAttachmentMeters = 1 });
        Assert.Equal("Disconnected", plan.Routes.Single().Status); Assert.Equal(2, plan.Metrics.ComponentCount);
    }
    [Fact]
    public void ThreeDimensionalProjectionAccountsForVerticalSegments()
    {
        var graph = Straight(10); graph.Nodes[1].Position = new(0, 0, 10);
        var route = RoutePlanner.Calculate(graph, D("c", 0, 1, 2), new[] { D("d", 0, 1, 8) }, new RouteOptions()).Routes.Single();
        Assert.Equal(8, route.PathMeters); Assert.Equal(8, route.DeviceAttachment!.Position.Z);
    }
    [Fact]
    public void CableLimitReportsAnIssueButRetainsUsablePath()
    {
        var route = RoutePlanner.Calculate(Straight(), D("c", 10), new[] { D("d", 50) }, new RouteOptions { MaxCableMeters = 30 }).Routes.Single();
        Assert.Equal("CableLimitExceeded", route.Status); Assert.True(route.HasPath); Assert.NotEmpty(route.PathPoints);
    }
    [Fact]
    public void ResultOptionsDoNotAliasMutableInputs()
    {
        var options = new RouteOptions(); var plan = RoutePlanner.Calculate(Straight(), D("c", 10), new[] { D("d", 50) }, options);
        options.ReservePercent = 99; Assert.Equal(10, plan.Options.ReservePercent);
    }
    [Fact]
    public void CancellationIsObservedBeforeCalculation()
    {
        var token = new CancellationToken(true);
        Assert.Throws<OperationCanceledException>(() => RoutePlanner.Calculate(Straight(), D("c", 10), new[] { D("d", 50) }, new(), token));
    }
    [Fact]
    public void ParallelEdgesRestoreTheChosenEdgeGeometry()
    {
        var graph = Straight(10); graph.Edges[0].SupportsInteriorAttachment = false;
        graph.Edges.Add(new()
        {
            Id = "detour",
            From = "a",
            To = "b",
            ElementUniqueId = "detour",
            LengthMeters = 30,
            Geometry = new() { new(0, 0, 0), new(0, 10, 0), new(10, 10, 0), new(10, 0, 0) }
        });
        var path = Dijkstra.From(graph, "a").PathTo("b")!;
        Assert.Equal("segment", path.Edges.Single().Id);
        var route = RoutePlanner.Calculate(graph, D("c", 0), new[] { D("d", 10) }, new()).Routes.Single();
        Assert.DoesNotContain(route.PathPoints, p => p.Y == 10);
    }
}
