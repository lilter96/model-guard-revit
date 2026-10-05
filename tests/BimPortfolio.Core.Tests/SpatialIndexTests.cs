using System;
using System.Collections.Generic;
using System.Linq;
using BimPortfolio.Core;
using Xunit;

namespace BimPortfolio.Core.Tests;
public class SpatialIndexTests
{
    [Fact]
    public void IndexedNearestMatchesBruteForceForSeededThreeDimensionalSegments()
    {
        var random = new Random(741); var graph = new TrayGraph();
        var lines = new List<(Point3 A, Point3 B)>();
        for (var i = 0; i < 150; i++)
        {
            Point3 P() => new(random.NextDouble() * 100, random.NextDouble() * 100, random.NextDouble() * 20);
            var a = P(); var b = P(); lines.Add((a, b));
            graph.Nodes.Add(new() { Id = $"{i}a", ElementUniqueId = i.ToString(), Position = a }); graph.Nodes.Add(new() { Id = $"{i}b", ElementUniqueId = i.ToString(), Position = b });
            graph.Edges.Add(new() { Id = i.ToString(), From = $"{i}a", To = $"{i}b", ElementUniqueId = i.ToString(), LengthMeters = a.DistanceTo(b), SupportsInteriorAttachment = true });
        }
        var index = new NetworkSpatialIndex(graph);
        for (var i = 0; i < 300; i++)
        {
            var p = new Point3(random.NextDouble() * 120 - 10, random.NextDouble() * 120 - 10, random.NextDouble() * 30);
            var expected = lines.Min(line =>
            {
                var a = line.A; var b = line.B;
                var direction = new[] { b.X - a.X, b.Y - a.Y, b.Z - a.Z };
                var delta = new[] { p.X - a.X, p.Y - a.Y, p.Z - a.Z };
                var denominator = direction.Sum(d => d * d);
                var t = Math.Clamp(direction.Zip(delta, (d, v) => d * v).Sum() / denominator, 0, 1);
                return p.DistanceTo(new(a.X + t * direction[0], a.Y + t * direction[1], a.Z + t * direction[2]));
            });
            Assert.Equal(expected, index.Nearest(p, 1000)!.DistanceMeters, 8);
            var limited = index.Nearest(p, Math.Max(.001, expected * .9));
            if (expected > .001) Assert.Null(limited);
        }
    }
    [Fact]
    public void EndpointProjectionClampsOutsideTheSegment()
    {
        var result = new NetworkSpatialIndex(SegmentRoutingTests.Straight(10)).Nearest(new(15, 1, 0), 6)!;
        Assert.Equal(1, result.Fraction); Assert.Equal(10, result.Position.X); Assert.Equal(Math.Sqrt(26), result.DistanceMeters, 8);
    }
    [Fact]
    public void ReversingInputCollectionsDoesNotChangeAttachment()
    {
        var graph = SegmentRoutingTests.Straight(); var first = new NetworkSpatialIndex(graph).Nearest(new(50, 2, 0), 3)!;
        graph.Nodes.Reverse(); graph.Edges.Reverse(); var second = new NetworkSpatialIndex(graph).Nearest(new(50, 2, 0), 3)!;
        Assert.Equal(first.EdgeId, second.EdgeId); Assert.Equal(first.Position.X, second.Position.X);
    }
}
