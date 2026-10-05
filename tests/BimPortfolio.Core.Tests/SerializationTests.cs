using System;
using System.Linq;
using BimPortfolio.Core;
using Newtonsoft.Json;
using Xunit;
namespace BimPortfolio.Core.Tests;
public class SerializationTests
{
    [Fact]
    public void RoundTripRetainsRouteSettingsIdsAndLengths()
    {
        var original = RoutePlanner.Calculate(GraphTests.Graph(2, (0, 1, 1)), new DevicePoint { UniqueId = "c" }, new[] { new DevicePoint { UniqueId = "d", Position = new Point3(1, 0, 0) } }, new RouteOptions { ReservePercent = 20 });
        var result = PlanJson.Read(PlanJson.Write(original));
        Assert.Equal(original.Routes.Single().CableMeters, result.Routes.Single().CableMeters); Assert.Equal(20, result.Options.ReservePercent); Assert.Equal("c", result.ControllerUniqueId);
    }
    [Fact]
    public void MigratesLegacyFeetWithoutInventingPath()
    {
        var legacy = """{"schemaVersion":1,"controllerUniqueId":"c","routes":[{"deviceUniqueId":"d","mark":"D1","lengthFeet":10}]}""";
        var migrated = PlanJson.Read(legacy); Assert.Equal(3, migrated.SchemaVersion); Assert.Equal(DateTime.MinValue, migrated.CalculatedUtc); Assert.Equal("Legacy", migrated.Routes.Single().Status);
        Assert.Equal(3.048, migrated.Routes.Single().CableMeters); Assert.Null(migrated.Routes.Single().PathMeters);
        Assert.Equal("Legacy", PlanJson.Read(PlanJson.Write(migrated)).Routes.Single().Status);
    }
    [Fact] public void RejectsUnknownVersionSoHostCannotOverwriteNewerData() => Assert.Throws<NotSupportedException>(() => PlanJson.Read("""{"schemaVersion":4}"""));
    [Fact]
    public void RejectsMissingVersionMalformedJsonAndDuplicateKeys()
    {
        Assert.Throws<JsonException>(() => PlanJson.Read("{}"));
        Assert.Throws<JsonReaderException>(() => PlanJson.Read("broken"));
        Assert.Throws<JsonReaderException>(() => PlanJson.Read("""{"schemaVersion":2,"schemaVersion":1}"""));
    }
    [Fact]
    public void RejectsCaseAmbiguousVersionAndMissingCollections()
    {
        Assert.Throws<JsonException>(() => PlanJson.Read("""{"schemaVersion":2,"SchemaVersion":3}"""));
        Assert.Throws<JsonException>(() => PlanJson.Read("""{"schemaVersion":2,"controllerUniqueId":"c"}"""));
    }
    [Fact]
    public void RejectsInvalidLengthsAndUnrecognisedStatuses()
    {
        var p = new RoutePlan { ControllerUniqueId = "c" }; p.Routes.Add(new RouteResult { DeviceUniqueId = "d", Status = "OK", PathMeters = 1, CableMeters = -2 });
        Assert.Throws<JsonException>(() => PlanJson.Write(p)); p.Routes[0].CableMeters = 2; p.Routes[0].Status = "Surprise"; Assert.Throws<JsonException>(() => PlanJson.Write(p));
    }
    [Fact]
    public void CsvEscapesQuotesNewlinesAndFormulas()
    {
        Assert.Equal("\"a\"\"b,c\nline\"", CsvReport.Cell("a\"b,c\nline"));
        Assert.Equal("\"'=SUM(1,2)\"", CsvReport.Cell("=SUM(1,2)"));
        Assert.StartsWith("\"'", CsvReport.Cell("   @command"));
    }
}
