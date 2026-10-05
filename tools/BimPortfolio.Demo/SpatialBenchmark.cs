using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using BimPortfolio.Core;
using Newtonsoft.Json;

internal static class SpatialBenchmark
{
    public static int Run(string output)
    {
        const int side = 60, queryCount = 1500;
        var graph = new TrayGraph();
        for (var x = 0; x < side; x++) for (var y = 0; y < side; y++) graph.Nodes.Add(new() { Id = $"{x},{y}", ElementUniqueId = $"tray-{x}-{y}", Position = new(x * 5, y * 5, 0) });
        var nodes = graph.Nodes.ToDictionary(n => n.Id); var segments = new List<(Point3 A, Point3 B)>();
        for (var x = 0; x < side; x++) for (var y = 0; y < side; y++)
            {
                if (x + 1 < side) Add($"{x},{y}", $"{x + 1},{y}");
                if (y + 1 < side) Add($"{x},{y}", $"{x},{y + 1}");
            }
        void Add(string a, string b)
        {
            var id = a + "->" + b;
            graph.Edges.Add(new() { Id = id, ElementUniqueId = id, From = a, To = b, LengthMeters = 5, SupportsInteriorAttachment = true });
            segments.Add((nodes[a].Position, nodes[b].Position));
        }
        var random = new Random(61842);
        var queries = Enumerable.Range(0, queryCount).Select(_ => new Point3(random.NextDouble() * (side - 1) * 5, random.NextDouble() * (side - 1) * 5, .1 + random.NextDouble())).ToArray();
        var timer = Stopwatch.StartNew(); var index = new NetworkSpatialIndex(graph); var buildMs = timer.Elapsed.TotalMilliseconds;
        double Linear(Point3 point)
        {
            var best = double.PositiveInfinity;
            foreach (var (a, b) in segments)
            {
                var dx = b.X - a.X; var dy = b.Y - a.Y; var dz = b.Z - a.Z;
                var t = Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy + (point.Z - a.Z) * dz) / (dx * dx + dy * dy + dz * dz), 0, 1);
                var ex = point.X - a.X - t * dx; var ey = point.Y - a.Y - t * dy; var ez = point.Z - a.Z - t * dz;
                best = Math.Min(best, ex * ex + ey * ey + ez * ez);
            }
            return Math.Sqrt(best);
        }
        // Warm both paths before measuring. Compare results independently of timings.
        foreach (var q in queries.Take(20)) { index.Nearest(q, 1000); Linear(q); }
        var maximumError = queries.Max(q => Math.Abs(index.Nearest(q, 1000)!.DistanceMeters - Linear(q)));
        if (maximumError > 1e-8) throw new InvalidOperationException("Index disagrees with exhaustive search.");
        double Measure(Action action)
        {
            var measurements = new double[3];
            for (var trial = 0; trial < 3; trial++) { timer.Restart(); action(); measurements[trial] = timer.Elapsed.TotalMilliseconds; }
            Array.Sort(measurements); return measurements[1];
        }
        double checksum = 0;
        var indexedMs = Measure(() => { foreach (var q in queries) checksum += index.Nearest(q, 1000)!.DistanceMeters; });
        var linearMs = Measure(() => { foreach (var q in queries) checksum += Linear(q); });
        var devices = queries.Take(500).Select((p, i) => new DevicePoint { UniqueId = "device-" + i, Mark = "RD-" + i, Position = p }).ToList();
        timer.Restart(); var plan = RoutePlanner.Calculate(graph, new() { UniqueId = "controller", Position = new(0, 0, 0) }, devices, new()); var routingMs = timer.Elapsed.TotalMilliseconds;
        var result = new
        {
            MeasuredUtc = DateTime.UtcNow,
            Runtime = Environment.Version.ToString(),
            OS = Environment.OSVersion.ToString(),
            NodeCount = graph.Nodes.Count,
            SegmentCount = graph.Edges.Count,
            QueryCount = queryCount,
            BuildMilliseconds = buildMs,
            IndexedQueryMedianMilliseconds = indexedMs,
            ExhaustiveQueryMedianMilliseconds = linearMs,
            QuerySpeedup = linearMs / indexedMs,
            MaximumDistanceError = maximumError,
            FullRoutingDeviceCount = devices.Count,
            FullRoutingMilliseconds = routingMs,
            SuccessfulRoutes = plan.Routes.Count(r => r.HasPath),
            Checksum = checksum,
            Method = "seed 61842; warmup; median of 3 batches; elapsed-time microbenchmark, not a Revit-host benchmark"
        };
        var destination = Path.GetFullPath(output); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, JsonConvert.SerializeObject(result, Formatting.Indented));
        Console.WriteLine($"{graph.Edges.Count} segments / {queryCount} queries: index {indexedMs:0.##} ms vs exhaustive {linearMs:0.##} ms ({linearMs / indexedMs:0.##}x).");
        Console.WriteLine($"Full routing: {devices.Count} devices in {routingMs:0.##} ms. Evidence: {destination}");
        return 0;
    }
}
