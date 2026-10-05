using System;
using System.Collections.Generic;
using System.Linq;
using BimPortfolio.Core;
using Xunit;
namespace BimPortfolio.Core.Tests;
public class GraphTests
{
    internal static TrayGraph Graph(int n, params (int A, int B, double Length)[] edges) => new TrayGraph
    {
        Nodes = Enumerable.Range(0, n).Select(i => new GraphNode { Id = i.ToString(), ElementUniqueId = "tray-" + i, Position = new Point3(i, 0, 0) }).ToList(),
        Edges = edges.Select(e => new GraphEdge { From = e.A.ToString(), To = e.B.ToString(), LengthMeters = e.Length }).ToList()
    };
    [Fact]
    public void ChoosesShorterIndirectPathAndReconstructsOrder()
    {
        var result = Dijkstra.From(Graph(4, (0, 1, 10), (0, 2, 2), (2, 1, 1), (1, 3, 2)), "0").PathTo("3")!;
        Assert.Equal(5, result.LengthMeters); Assert.Equal(new[] { "0", "2", "1", "3" }, result.NodeIds);
    }
    [Fact]
    public void SupportsUndirectedZeroWeightCyclesAndUnreachableNodes()
    {
        var tree = Dijkstra.From(Graph(4, (0, 1, 0), (1, 2, 0), (2, 0, 0)), "2");
        Assert.Equal(0, tree.PathTo("0")!.LengthMeters); Assert.Null(tree.PathTo("3")); Assert.Single(tree.PathTo("2")!.NodeIds);
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidWeights(double weight) => Assert.Throws<ArgumentException>(() => Dijkstra.From(Graph(2, (0, 1, weight)), "0"));
    [Fact]
    public void RejectsUnknownNodesDuplicateIdsAndInvalidCoordinates()
    {
        Assert.Throws<ArgumentException>(() => Dijkstra.From(Graph(1, (0, 2, 1)), "0"));
        var g = Graph(2); g.Nodes[1].Id = "0"; Assert.Throws<ArgumentException>(() => g.Validate());
        g = Graph(1); g.Nodes[0].Position.X = double.NaN; Assert.Throws<ArgumentException>(() => g.Validate());
        Assert.Throws<ArgumentException>(() => Dijkstra.From(Graph(1), "missing"));
    }
    [Fact]
    public void MatchesFloydWarshallOnSeededGraphs()
    {
        var random = new Random(4281);
        for (var trial = 0; trial < 30; trial++)
        {
            const int n = 14; var arcs = new List<(int, int, double)>(); var reference = new double[n, n];
            for (var i = 0; i < n; i++) for (var j = 0; j < n; j++) reference[i, j] = i == j ? 0 : double.PositiveInfinity;
            for (var i = 0; i < n; i++) for (var j = i + 1; j < n; j++) if (random.NextDouble() < 0.18) { var length = random.Next(0, 20); arcs.Add((i, j, length)); reference[i, j] = reference[j, i] = length; }
            for (var k = 0; k < n; k++) for (var i = 0; i < n; i++) for (var j = 0; j < n; j++) reference[i, j] = Math.Min(reference[i, j], reference[i, k] + reference[k, j]);
            var g = Graph(n, arcs.ToArray());
            for (var source = 0; source < n; source++)
            {
                var tree = Dijkstra.From(g, source.ToString());
                for (var target = 0; target < n; target++)
                {
                    var path = tree.PathTo(target.ToString());
                    if (double.IsPositiveInfinity(reference[source, target])) Assert.Null(path);
                    else Assert.Equal(reference[source, target], path!.LengthMeters, 8);
                }
            }
        }
    }
}
