using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BimPortfolio.Core;

public sealed class NetworkAttachment
{
    public string NodeId { get; set; } = "";
    public string EdgeId { get; set; } = "";
    public double Fraction { get; set; }
    public Point3 Position { get; set; } = new Point3();
    public double DistanceMeters { get; set; }
    public string ElementUniqueId { get; set; } = "";
    public NetworkAttachment Copy() => new NetworkAttachment
    {
        NodeId = NodeId,
        EdgeId = EdgeId,
        Fraction = Fraction,
        Position = Position.Copy(),
        DistanceMeters = DistanceMeters,
        ElementUniqueId = ElementUniqueId
    };
}

// A balanced bounding-volume hierarchy over segments + connector points.
// Queries prune by point-to-AABB lower bounds; topology is never inferred by proximity.
public sealed class NetworkSpatialIndex
{
    sealed class Primitive
    {
        public string Key = "";
        public string NodeId = "";
        public string EdgeId = "";
        public string ElementId = "";
        public Point3 A = new Point3();
        public Point3 B = new Point3();
        public double Center(int axis) => (Coordinate(A, axis) + Coordinate(B, axis)) / 2;
    }
    sealed class Branch
    {
        public double[] Min = new double[3];
        public double[] Max = new double[3];
        public Branch? Left;
        public Branch? Right;
        public Primitive[]? Leaf;
        public double DistanceSquared(Point3 point)
        {
            double result = 0;
            for (var axis = 0; axis < 3; axis++)
            {
                var c = Coordinate(point, axis);
                var delta = c < Min[axis] ? Min[axis] - c : c > Max[axis] ? c - Max[axis] : 0;
                result += delta * delta;
            }
            return result;
        }
    }
    readonly Branch? root;
    public NetworkSpatialIndex(TrayGraph graph, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        graph.Validate();
        var nodes = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var primitives = graph.Nodes.Select(n => new Primitive
        {
            Key = "N:" + n.Id,
            NodeId = n.Id,
            ElementId = n.ElementUniqueId,
            A = n.Position.Copy(),
            B = n.Position.Copy()
        }).ToList();
        primitives.AddRange(graph.Edges.Where(e => e.SupportsInteriorAttachment).Select(e => new Primitive
        {
            Key = "E:" + e.Id,
            EdgeId = e.Id,
            ElementId = e.ElementUniqueId,
            A = nodes[e.From].Position.Copy(),
            B = nodes[e.To].Position.Copy()
        }));
        if (primitives.Count > 0) root = Build(primitives.ToArray(), cancellation);
    }
    public NetworkAttachment? Nearest(Point3 point, double maximumMeters, CancellationToken cancellation = default)
    {
        if (point == null || !point.IsFinite || !Point3.Finite(maximumMeters) || maximumMeters <= 0)
            throw new ArgumentException("Nearest query requires finite coordinates and a positive finite radius.");
        cancellation.ThrowIfCancellationRequested();
        if (root == null) return null;
        var bestSquared = maximumMeters * maximumMeters;
        Primitive? best = null;
        double fraction = 0;
        Point3? projection = null;
        Search(root);
        return best == null ? null : new NetworkAttachment
        {
            NodeId = best.NodeId,
            EdgeId = best.EdgeId,
            Fraction = fraction,
            Position = projection!,
            DistanceMeters = Math.Sqrt(bestSquared),
            ElementUniqueId = best.ElementId
        };
        void Search(Branch branch)
        {
            cancellation.ThrowIfCancellationRequested();
            if (branch.DistanceSquared(point) > bestSquared) return;
            if (branch.Leaf != null)
            {
                foreach (var primitive in branch.Leaf)
                {
                    var dx = primitive.B.X - primitive.A.X;
                    var dy = primitive.B.Y - primitive.A.Y;
                    var dz = primitive.B.Z - primitive.A.Z;
                    var squared = dx * dx + dy * dy + dz * dz;
                    var t = squared == 0 ? 0 : Math.Max(0, Math.Min(1,
                        ((point.X - primitive.A.X) * dx + (point.Y - primitive.A.Y) * dy + (point.Z - primitive.A.Z) * dz) / squared));
                    var candidate = Point3.Lerp(primitive.A, primitive.B, t);
                    var distance = point.DistanceSquaredTo(candidate);
                    if (distance > bestSquared) continue;
                    if (best != null && distance == bestSquared && string.CompareOrdinal(primitive.Key, best.Key) >= 0) continue;
                    best = primitive; bestSquared = distance; fraction = t; projection = candidate;
                }
                return;
            }
            var left = branch.Left!;
            var right = branch.Right!;
            if (left.DistanceSquared(point) <= right.DistanceSquared(point)) { Search(left); Search(right); }
            else { Search(right); Search(left); }
        }
    }
    static Branch Build(Primitive[] items, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var branch = new Branch();
        for (var axis = 0; axis < 3; axis++)
        {
            branch.Min[axis] = items.Min(i => Math.Min(Coordinate(i.A, axis), Coordinate(i.B, axis)));
            branch.Max[axis] = items.Max(i => Math.Max(Coordinate(i.A, axis), Coordinate(i.B, axis)));
        }
        if (items.Length <= 8) { branch.Leaf = items; return branch; }
        var splitAxis = Enumerable.Range(0, 3).OrderByDescending(a => branch.Max[a] - branch.Min[a]).First();
        Array.Sort(items, (a, b) =>
        {
            var order = a.Center(splitAxis).CompareTo(b.Center(splitAxis));
            return order != 0 ? order : string.CompareOrdinal(a.Key, b.Key);
        });
        var mid = items.Length / 2;
        var left = new Primitive[mid];
        var right = new Primitive[items.Length - mid];
        Array.Copy(items, 0, left, 0, left.Length);
        Array.Copy(items, mid, right, 0, right.Length);
        branch.Left = Build(left, cancellation);
        branch.Right = Build(right, cancellation);
        return branch;
    }
    static double Coordinate(Point3 p, int axis) => axis == 0 ? p.X : axis == 1 ? p.Y : p.Z;
}

internal static class GraphSubdivision
{
    public static TrayGraph Split(TrayGraph input, IEnumerable<NetworkAttachment?> attachments, CancellationToken cancellation)
    {
        var graph = new TrayGraph
        {
            Nodes = input.Nodes.Select(n => new GraphNode { Id = n.Id, ElementUniqueId = n.ElementUniqueId, Position = n.Position.Copy() }).ToList(),
            Notices = input.Notices.ToList()
        };
        var nodes = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var byEdge = attachments.Where(a => a != null && !string.IsNullOrEmpty(a.EdgeId))
            .Cast<NetworkAttachment>().GroupBy(a => a.EdgeId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        foreach (var edge in input.Edges)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!byEdge.TryGetValue(edge.Id, out var taps)) { graph.Edges.Add(edge); continue; }
            var cuts = new List<(double Fraction, string Id)> { (0, edge.From) };
            foreach (var tap in taps.OrderBy(t => t.Fraction))
            {
                if (tap.Fraction <= 1e-12) { tap.NodeId = edge.From; continue; }
                if (tap.Fraction >= 1 - 1e-12) { tap.NodeId = edge.To; continue; }
                var last = cuts[cuts.Count - 1];
                if (Math.Abs(last.Fraction - tap.Fraction) <= 1e-12) { tap.NodeId = last.Id; continue; }
                var id = "@tap/" + edge.Id + "/" + cuts.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (nodes.ContainsKey(id)) throw new ArgumentException("Virtual attachment ID conflicts with an input node.");
                var node = new GraphNode { Id = id, ElementUniqueId = edge.ElementUniqueId, Position = tap.Position.Copy() };
                nodes.Add(id, node); graph.Nodes.Add(node); cuts.Add((tap.Fraction, id)); tap.NodeId = id;
            }
            cuts.Add((1, edge.To));
            for (var i = 1; i < cuts.Count; i++)
            {
                var from = cuts[i - 1]; var to = cuts[i];
                graph.Edges.Add(new GraphEdge
                {
                    Id = edge.Id + "/part/" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    From = from.Id,
                    To = to.Id,
                    ElementUniqueId = edge.ElementUniqueId,
                    LengthMeters = edge.LengthMeters * (to.Fraction - from.Fraction),
                    Geometry = new List<Point3> { nodes[from.Id].Position.Copy(), nodes[to.Id].Position.Copy() }
                });
            }
        }
        graph.Validate(); return graph;
    }
}
