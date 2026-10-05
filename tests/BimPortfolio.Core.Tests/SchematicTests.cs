using System;
using System.Linq;
using BimPortfolio.Core;
using Xunit;

namespace BimPortfolio.Core.Tests;
public class SchematicTests
{
    [Fact]
    public void PaginationIncludesEveryDeviceExactlyOnce()
    {
        var devices = Enumerable.Range(0, 43).Select(i => SegmentRoutingTests.D("d" + i, i + 1)).ToArray();
        var plan = RoutePlanner.Calculate(SegmentRoutingTests.Straight(), SegmentRoutingTests.D("c", 0), devices, new());
        var pages = SchematicLayout.Build(plan);
        Assert.Equal(3, pages.Count); Assert.Equal(43, pages.Sum(p => p.DeviceUniqueIds.Count));
        Assert.Equal(43, pages.SelectMany(p => p.DeviceUniqueIds).Distinct().Count());
        Assert.All(pages.SelectMany(p => p.Lines), line => Assert.True(line.Start.DistanceTo(line.End) > 0));
    }
    [Fact] public void EmptyPlanStillProducesAHeaderPage() => Assert.Single(SchematicLayout.Build(new RoutePlan { ControllerUniqueId = "c" }));
}
