using System;
using System.Linq;
using BimPortfolio.Core;
using Xunit;
namespace BimPortfolio.Core.Tests;
public class RoutingTests
{
    static DevicePoint Device(string id, double x) => new DevicePoint { UniqueId = id, Mark = id, Position = new Point3(x, 0, 0) };
    [Fact]
    public void IncludesBothAttachmentsReserveAndTerminationAllowance()
    {
        var g = GraphTests.Graph(2, (0, 1, 10)); g.Nodes[1].Position.X = 10;
        var route = RoutePlanner.Calculate(g, Device("controller", -1), new[] { Device("device", 12) }, new RouteOptions()).Routes.Single();
        Assert.Equal("OK", route.Status); Assert.Equal(13, route.PathMeters); Assert.Equal(15.3, route.CableMeters);
        Assert.Equal(new[] { "1", "0" }, route.NodeIds);
    }
    [Theory]
    [InlineData(0, 20, "DeviceOutsideNetwork")]
    [InlineData(20, 0, "ControllerOutsideNetwork")]
    public void DoesNotInventLongAttachments(double controller, double device, string expected)
    {
        var route = RoutePlanner.Calculate(GraphTests.Graph(1), Device("c", controller), new[] { Device("d", device) }, new RouteOptions()).Routes.Single();
        Assert.Equal(expected, route.Status); Assert.Null(route.CableMeters);
    }
    [Fact]
    public void SeparatesDisconnectedNetworks()
    {
        var g = GraphTests.Graph(2); g.Nodes[1].Position.X = 20;
        var route = RoutePlanner.Calculate(g, Device("c", 0), new[] { Device("d", 20) }, new RouteOptions()).Routes.Single();
        Assert.Equal("Disconnected", route.Status); Assert.Null(route.PathMeters);
    }
    [Fact]
    public void EmptyGraphReturnsExplicitFailure()
    {
        var plan = RoutePlanner.Calculate(GraphTests.Graph(0), Device("c", 0), new[] { Device("d", 0) }, new RouteOptions());
        Assert.Equal("ControllerOutsideNetwork", plan.Routes.Single().Status);
    }
    [Fact]
    public void RejectsDuplicateDevicesControllerAsDeviceAndInvalidOptions()
    {
        var g = GraphTests.Graph(1);
        Assert.Throws<ArgumentException>(() => RoutePlanner.Calculate(g, Device("c", 0), new[] { Device("d", 0), Device("d", 1) }, new RouteOptions()));
        Assert.Throws<ArgumentException>(() => RoutePlanner.Calculate(g, Device("c", 0), new[] { Device("c", 0) }, new RouteOptions()));
        Assert.Throws<ArgumentException>(() => new RouteOptions { ReservePercent = double.NaN }.Validate());
        Assert.Throws<ArgumentException>(() => new RouteOptions { ReservePercent = 101 }.Validate());
    }
    [Fact]
    public void RoundingDoesNotAddCentimetresFromBinaryFloatingPointDust()
    {
        var g = GraphTests.Graph(2, (0, 1, 6)); g.Nodes[1].Position.X = 6;
        var route = RoutePlanner.Calculate(g, Device("c", 0), new[] { Device("d", 6) }, new RouteOptions { TerminationAllowanceMeters = 0 }).Routes.Single();
        Assert.Equal(6.6, route.CableMeters);
    }
    [Fact]
    public void EqualDistanceAttachmentUsesStableNodeId()
    {
        var graph = GraphTests.Graph(2, (0, 1, 1)); graph.Nodes[0].Position.X = -1; graph.Nodes[1].Position.X = 1;
        var plan = RoutePlanner.Calculate(graph, Device("c", 0), new[] { Device("d", -1) }, new RouteOptions());
        Assert.Equal(new[] { "0" }, plan.Routes.Single().NodeIds);
    }
}
