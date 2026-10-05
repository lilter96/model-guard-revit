using System;
using System.Collections.Generic;
using System.Linq;
using BimPortfolio.Core;
using Xunit;

namespace BimPortfolio.Core.Tests;
public class ProfileTests
{
    static ElementSnapshot E(string id, string category = "OST_Doors") => new() { UniqueId = id, Mark = id, Family = "F", Type = "T", CategoryKey = category };
    static ParameterValue Value(string text, ParameterKind kind = ParameterKind.String) => new() { State = ParameterReadState.Found, HasValue = true, Kind = kind, Text = text };
    [Theory]
    [InlineData(ParameterReadState.Missing, "MissingParameter")]
    [InlineData(ParameterReadState.Ambiguous, "AmbiguousParameter")]
    [InlineData(ParameterReadState.WrongScope, "ParameterScopeMismatch")]
    public void ExplainsReadFailuresInsteadOfChoosingArbitraryValues(ParameterReadState state, string code)
    {
        var element = E("a"); element.TypedParameters["p"] = new() { State = state };
        var profile = AuditProfile.FromNames(new[] { "p" }); Assert.Equal(code, Assert.Single(ProfileAuditor.Inspect(new[] { element }, profile)).Code);
    }
    [Fact]
    public void TypeAllowedValueAndPatternRulesAreIndependent()
    {
        var profile = new AuditProfile { Parameters = new() { new() { Id = "system", Name = "ACS_System", Kind = ParameterKind.String, Pattern = "^ACS-[0-9]+$", AllowedValues = new() { "ACS-1" } } } };
        var element = E("a"); element.TypedParameters["system"] = Value("ACS-2");
        Assert.Equal("ParameterNotAllowed", Assert.Single(ProfileAuditor.Inspect(new[] { element }, profile)).Code);
        element.TypedParameters["system"] = Value("wrong"); Assert.Equal(2, ProfileAuditor.Inspect(new[] { element }, profile).Count);
        element.TypedParameters["system"] = Value("42", ParameterKind.Integer); Assert.Equal("ParameterTypeMismatch", Assert.Single(ProfileAuditor.Inspect(new[] { element }, profile)).Code);
    }
    [Fact]
    public void IntegerRangesAndCategoryFiltersAreRespected()
    {
        var profile = new AuditProfile { Parameters = new() { new() { Id = "port", Name = "Port", Kind = ParameterKind.Integer, MinimumInteger = 1, MaximumInteger = 8, CategoryKeys = new() { "OST_Doors" } } } };
        var door = E("a"); door.TypedParameters["port"] = Value("9", ParameterKind.Integer); door.TypedParameters["port"].Number = 9;
        var other = E("b", "OST_ElectricalEquipment");
        Assert.Equal("ParameterOutOfRange", Assert.Single(ProfileAuditor.Inspect(new[] { door, other }, profile)).Code);
    }
    [Fact]
    public void ReferencesDistinguishMissingAndAmbiguousEquipment()
    {
        var profile = new AuditProfile { Parameters = new() { new() { Id = "controller", Name = "ACS_Controller", Required = false, CategoryKeys = new() { "OST_Doors" } } }, References = new() { new() { ParameterRuleId = "controller", TargetCategoryKey = "OST_ElectricalEquipment" } } };
        var door = E("door"); door.TypedParameters["controller"] = Value("CTRL-01");
        Assert.Equal("UnresolvedReference", Assert.Single(ProfileAuditor.Inspect(new[] { door }, profile)).Code);
        var controller = E("CTRL-01", "OST_ElectricalEquipment"); Assert.Empty(ProfileAuditor.Inspect(new[] { door, controller }, profile));
        var duplicate = E("other", "OST_ElectricalEquipment"); duplicate.Mark = "CTRL-01";
        Assert.Contains(ProfileAuditor.Inspect(new[] { door, controller, duplicate }, profile), i => i.Code == "AmbiguousReference");
    }
    [Fact]
    public void RejectsInvalidGuidUnsupportedVersionAndUnitAmbiguousRanges()
    {
        Assert.Throws<NotSupportedException>(() => AuditProfileJson.Read("""{"schemaVersion":2}"""));
        var profile = AuditProfile.FromNames(new[] { "p" }); profile.Parameters[0].SharedParameterGuid = "invalid"; Assert.Throws<ArgumentException>(() => profile.Validate());
        profile.Parameters[0].SharedParameterGuid = ""; profile.Parameters[0].Kind = ParameterKind.Double; profile.Parameters[0].MinimumInteger = 1; Assert.Throws<ArgumentException>(() => profile.Validate());
    }
    [Fact]
    public void PathologicalRegexIsBoundedAndReported()
    {
        var profile = new AuditProfile { Parameters = new() { new() { Id = "p", Name = "p", Pattern = "^(a+)+$" } } };
        var element = E("a"); element.TypedParameters["p"] = Value(new string('a', 30000) + "!");
        Assert.Equal("RuleEvaluationTimeout", Assert.Single(ProfileAuditor.Inspect(new[] { element }, profile)).Code);
    }
}
