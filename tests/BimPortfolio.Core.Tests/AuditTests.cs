using System;
using System.Linq;
using BimPortfolio.Core;
using Xunit;
namespace BimPortfolio.Core.Tests;
public class AuditTests
{
    static ElementSnapshot E(string id, string mark, bool writable = true) => new ElementSnapshot { UniqueId = id, Mark = mark, Family = "Door", Type = "900", CanWriteMark = writable };
    [Fact]
    public void ReportsEveryDuplicateAfterTrimmingAndIgnoringCase()
    {
        var issues = ModelAuditor.Inspect(new[] { E("a", "D-1"), E("b", " d-1 ") }, Array.Empty<string>());
        Assert.Equal(2, issues.Count); Assert.All(issues, i => Assert.Equal("DuplicateMark", i.Code));
    }
    [Fact]
    public void ChecksMissingAndEmptyRequiredValues()
    {
        var element = E("a", ""); element.Parameters["ACS_System"] = " ";
        var issues = ModelAuditor.Inspect(new[] { element }, new[] { "ACS_System", "ACS_System", "Missing" });
        Assert.Equal(3, issues.Count); Assert.Equal(2, issues.Count(i => i.Code == "MissingParameter"));
    }
    [Fact]
    public void ProposalSkipsExistingMarksReadOnlyAndOccupiedNumbers()
    {
        var elements = new[] { E("c", " "), E("occupied", "acs-001"), E("readonly", "", false), E("b", ""), E("named", "CUSTOM") };
        var proposal = ModelAuditor.ProposeMissingMarks(elements);
        Assert.Equal(new[] { "b", "c" }, proposal.Select(p => p.ElementUniqueId)); Assert.Equal(new[] { "ACS-002", "ACS-003" }, proposal.Select(p => p.NewMark));
        Assert.Equal(" ", elements[0].Mark); // Pure planning must not mutate snapshots.
    }
    [Fact]
    public void ApplyingProposalAndRerunningIsIdempotent()
    {
        var elements = new[] { E("b", ""), E("a", "") };
        foreach (var change in ModelAuditor.ProposeMissingMarks(elements)) elements.Single(e => e.UniqueId == change.ElementUniqueId).Mark = change.NewMark;
        Assert.Empty(ModelAuditor.ProposeMissingMarks(elements));
    }
    [Fact] public void RejectsDuplicateSnapshots() => Assert.Throws<ArgumentException>(() => ModelAuditor.ProposeMissingMarks(new[] { E("a", ""), E("a", "") }));
}
