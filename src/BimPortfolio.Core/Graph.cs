using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BimPortfolio.Core;

public sealed class Point3
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public Point3() { }
    public Point3(double x, double y, double z) { X = x; Y = y; Z = z; }
    public double DistanceTo(Point3 p) => Math.Sqrt(DistanceSquaredTo(p));
    public double DistanceSquaredTo(Point3 p) => (X - p.X) * (X - p.X) + (Y - p.Y) * (Y - p.Y) + (Z - p.Z) * (Z - p.Z);
    public Point3 Copy() => new Point3(X, Y, Z);
    public bool IsFinite => Finite(X) && Finite(Y) && Finite(Z);
    internal static bool Finite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);
    public static Point3 Lerp(Point3 a, Point3 b, double fraction) => new Point3(
        a.X + (b.X - a.X) * fraction, a.Y + (b.Y - a.Y) * fraction, a.Z + (b.Z - a.Z) * fraction);
}

public sealed class GraphNode
{
    public string Id { get; set; } = "";
    public string ElementUniqueId { get; set; } = "";
    public Point3 Position { get; set; } = new Point3();
}

public sealed class GraphEdge
{
    public string Id { get; set; } = "";
    public string From { get; set; } = "";
    public string To { get; set; } = "";
    public string ElementUniqueId { get; set; } = "";
    public double LengthMeters { get; set; }
    public bool SupportsInteriorAttachment { get; set; }
    // Oriented From -> To. Empty means the chord between endpoint coordinates.
    public List<Point3> Geometry { get; set; } = new List<Point3>();
}

public sealed class NetworkNotice
{
    public string Code { get; set; } = "";
    public string ElementUniqueId { get; set; } = "";
    public string Detail { get; set; } = "";
}

public sealed class TrayGraph
{
    public List<GraphNode> Nodes { get; set; } = new List<GraphNode>();
    public List<GraphEdge> Edges { get; set; } = new List<GraphEdge>();
    public List<NetworkNotice> Notices { get; set; } = new List<NetworkNotice>();

    public void Validate()
    {
        if (Nodes == null || Edges == null || Notices == null)
            throw new ArgumentException("Graph collections must not be null.");
        var nodes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        foreach (var node in Nodes)
        {
            if (node == null || string.IsNullOrWhiteSpace(node.Id) || nodes.ContainsKey(node.Id) ||
                node.Position == null || !node.Position.IsFinite)
                throw new ArgumentException("Nodes require unique IDs and finite coordinates.");
            nodes.Add(node.Id, node);
        }
        var edgeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in Edges)
        {
            if (edge == null || !nodes.ContainsKey(edge.From) || !nodes.ContainsKey(edge.To) ||
                !Point3.Finite(edge.LengthMeters) || edge.LengthMeters < 0 || edge.Geometry == null ||
                edge.Geometry.Any(p => p == null || !p.IsFinite))
                throw new ArgumentException("Edges require known nodes and nonnegative finite lengths.");
            if (!string.IsNullOrEmpty(edge.Id) && !edgeIds.Add(edge.Id))
                throw new ArgumentException("Explicit edge IDs must be unique.");
            var a = nodes[edge.From].Position;
            var b = nodes[edge.To].Position;
            if (edge.Geometry.Count > 0 && (edge.Geometry.Count < 2 ||
                edge.Geometry[0].DistanceTo(a) > 1e-5 || edge.Geometry[edge.Geometry.Count - 1].DistanceTo(b) > 1e-5))
                throw new ArgumentException("Edge geometry must be oriented From -> To and match its endpoints.");
            if (edge.SupportsInteriorAttachment && (string.IsNullOrWhiteSpace(edge.Id) ||
                string.IsNullOrWhiteSpace(edge.ElementUniqueId) || edge.LengthMeters <= 1e-9 ||
                Math.Abs(a.DistanceTo(b) - edge.LengthMeters) > 1e-5 || edge.Geometry.Count > 2))
                throw new ArgumentException("Interior attachment requires an identified straight tray segment with Euclidean length.");
        }
    }
}

public sealed class PathResult
{
    public double LengthMeters { get; }
    public IReadOnlyList<string> NodeIds { get; }
    public IReadOnlyList<GraphEdge> Edges { get; }
    public PathResult(double length, IReadOnlyList<string> nodes, IReadOnlyList<GraphEdge> edges)
    { LengthMeters = length; NodeIds = nodes; Edges = edges; }
}

// Binary heap kept in Core because netstandard2.0 has no PriorityQueue.
internal sealed class MinHeap
{
    readonly List<(string Node, double Distance)> items = new List<(string, double)>();
    public int Count => items.Count;
    static bool Less((string Node, double Distance) a, (string Node, double Distance) b) =>
        a.Distance < b.Distance || (a.Distance == b.Distance && string.CompareOrdinal(a.Node, b.Node) < 0);
    public void Push(string node, double distance)
    {
        var item = (node, distance);
        items.Add(item);
        var i = items.Count - 1;
        while (i > 0)
        {
            var parent = (i - 1) / 2;
            if (!Less(item, items[parent])) break;
            items[i] = items[parent];
            i = parent;
        }
        items[i] = item;
    }
    public (string Node, double Distance) Pop()
    {
        var result = items[0];
        var last = items[items.Count - 1];
        items.RemoveAt(items.Count - 1);
        if (items.Count == 0) return result;
        var i = 0;
        while (i * 2 + 1 < items.Count)
        {
            var child = i * 2 + 1;
            if (child + 1 < items.Count && Less(items[child + 1], items[child])) child++;
            if (!Less(items[child], last)) break;
            items[i] = items[child];
            i = child;
        }
        items[i] = last;
        return result;
    }
}

public sealed class ShortestPathTree
{
    readonly string source;
    readonly Dictionary<string, double> distances;
    readonly Dictionary<string, (string Node, GraphEdge Edge)> previous;
    internal ShortestPathTree(string source, Dictionary<string, double> distances,
        Dictionary<string, (string Node, GraphEdge Edge)> previous)
    { this.source = source; this.distances = distances; this.previous = previous; }
    public PathResult? PathTo(string target)
    {
        if (!distances.TryGetValue(target, out var length)) return null;
        var nodes = new List<string> { target };
        var edges = new List<GraphEdge>();
        var current = target;
        while (current != source)
        {
            var step = previous[current];
            edges.Add(step.Edge);
            current = step.Node;
            nodes.Add(current);
        }
        nodes.Reverse();
        edges.Reverse();
        return new PathResult(length, nodes, edges);
    }
}

public static class Dijkstra
{
    public static ShortestPathTree From(TrayGraph graph, string source, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        graph.Validate();
        var adjacent = graph.Nodes.ToDictionary(n => n.Id, n => new List<(string Node, GraphEdge Edge)>(), StringComparer.Ordinal);
        if (!adjacent.ContainsKey(source)) throw new ArgumentException("Source does not exist.");
        foreach (var edge in graph.Edges)
        {
            adjacent[edge.From].Add((edge.To, edge));
            adjacent[edge.To].Add((edge.From, edge));
        }
        // Stable edge order also makes equal-cost path selection reproducible.
        foreach (var list in adjacent.Values) list.Sort((a, b) =>
        {
            var order = string.CompareOrdinal(a.Node, b.Node);
            return order != 0 ? order : string.CompareOrdinal(a.Edge.Id, b.Edge.Id);
        });
        var distances = new Dictionary<string, double>(StringComparer.Ordinal) { { source, 0 } };
        var previous = new Dictionary<string, (string Node, GraphEdge Edge)>(StringComparer.Ordinal);
        var queue = new MinHeap();
        queue.Push(source, 0);
        while (queue.Count > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            var entry = queue.Pop();
            if (entry.Distance > distances[entry.Node]) continue;
            foreach (var arc in adjacent[entry.Node])
            {
                var candidate = entry.Distance + arc.Edge.LengthMeters;
                if (!Point3.Finite(candidate)) throw new ArgumentException("Path length overflow.");
                if (distances.TryGetValue(arc.Node, out var old) && candidate >= old) continue;
                distances[arc.Node] = candidate;
                previous[arc.Node] = (entry.Node, arc.Edge);
                queue.Push(arc.Node, candidate);
            }
        }
        return new ShortestPathTree(source, distances, previous);
    }
}
