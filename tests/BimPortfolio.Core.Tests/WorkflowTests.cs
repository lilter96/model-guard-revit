using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using BimPortfolio.Core;
using Xunit;

namespace BimPortfolio.Core.Tests;
public class WorkflowTests
{
    sealed class Adapter : IRoutingSnapshotSource, IRoutePlanRepository
    {
        public RoutingSnapshot Snapshot = new(SegmentRoutingTests.Straight(), SegmentRoutingTests.D("c", 10), new[] { SegmentRoutingTests.D("d", 50) });
        public int Writes;
        public RoutingSnapshot Capture(string controller, IReadOnlyList<string> devices, CancellationToken cancellation) => Snapshot;
        public RoutePlan? Load() => null;
        public void Save(RoutePlan plan) => Writes++;
    }
    [Fact]
    public void CurrentPlanCanBeSavedThroughTheRepositoryPort()
    {
        var adapter = new Adapter(); var workflow = new RoutingWorkflow(adapter, adapter); var plan = workflow.Calculate("c", new[] { "d" }, new());
        Assert.Equal(PlanFreshness.Current, workflow.Freshness(plan)); workflow.Save(plan); Assert.Equal(1, adapter.Writes);
    }
    [Fact]
    public void MovedEquipmentBlocksRepositoryWrite()
    {
        var adapter = new Adapter(); var workflow = new RoutingWorkflow(adapter, adapter); var plan = workflow.Calculate("c", new[] { "d" }, new());
        adapter.Snapshot.Devices[0].Position.Y = 2;
        Assert.Equal(PlanFreshness.Stale, workflow.Freshness(plan)); Assert.Throws<InvalidOperationException>(() => workflow.Save(plan)); Assert.Equal(0, adapter.Writes);
    }
    [Fact]
    public void ChangedSettingsAndTopologyInvalidateSignature()
    {
        var adapter = new Adapter(); var workflow = new RoutingWorkflow(adapter, adapter); var plan = workflow.Calculate("c", new[] { "d" }, new());
        plan.Options.ReservePercent = 20; Assert.Equal(PlanFreshness.Stale, workflow.Freshness(plan));
        plan.Options.ReservePercent = 10; adapter.Snapshot.Graph.Edges.Clear(); Assert.Equal(PlanFreshness.Stale, workflow.Freshness(plan));
    }
    [Fact]
    public void LegacyPlanHasUnknownFreshnessAndCannotBeSavedAsVerified()
    {
        var adapter = new Adapter(); var workflow = new RoutingWorkflow(adapter, adapter); var plan = workflow.Calculate("c", new[] { "d" }, new()); plan.CalculationSignature = "";
        Assert.Equal(PlanFreshness.Unknown, workflow.Freshness(plan)); Assert.Throws<InvalidOperationException>(() => workflow.Save(plan)); Assert.Equal(0, adapter.Writes);
    }
    [Fact]
    public void FingerprintIsIndependentOfCollectionAndEdgeOrientation()
    {
        var graph = SegmentRoutingTests.Straight(); graph.Edges[0].Geometry = new() { graph.Nodes[0].Position.Copy(), graph.Nodes[1].Position.Copy() };
        var c = SegmentRoutingTests.D("c", 10); var devices = new[] { SegmentRoutingTests.D("d", 50), SegmentRoutingTests.D("e", 70) };
        var first = CalculationFingerprint.Compute(graph, c, devices, new());
        graph.Nodes.Reverse(); graph.Edges[0].From = "b"; graph.Edges[0].To = "a"; graph.Edges[0].Geometry.Reverse();
        Assert.Equal(first, CalculationFingerprint.Compute(graph, c, Enumerable.Reverse(devices), new()));
    }
    [Fact]
    public void V2MigrationPreservesLengthsButDoesNotInventSignatureOrGeometry()
    {
        var plan = PlanJson.Read("""{"schemaVersion":2,"controllerUniqueId":"c","options":{},"routes":[{"deviceUniqueId":"d","status":"OK","pathMeters":10,"cableMeters":12}]}""");
        Assert.Equal(3, plan.SchemaVersion); Assert.Equal(DateTime.MinValue, plan.CalculatedUtc); Assert.Equal(12, plan.Routes.Single().CableMeters); Assert.Empty(plan.CalculationSignature); Assert.Empty(plan.Routes.Single().PathPoints);
    }
}
