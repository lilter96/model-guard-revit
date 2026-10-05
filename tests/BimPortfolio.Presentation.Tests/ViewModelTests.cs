using System;
using System.Globalization;
using System.Linq;
using BimPortfolio.Presentation;
using Xunit;

namespace BimPortfolio.Presentation.Tests;
public class ViewModelTests
{
    [Theory]
    [InlineData("1.5")]
    [InlineData("1,5")]
    public void SettingsAcceptDotOrCommaWithoutDependingOnOperatingSystemCulture(string input)
    {
        var vm = new SettingsViewModel(true) { Attachment = input, Reserve = "10", Allowance = "1", CableLimit = "100" };
        var accepted = 0; vm.Accepted += () => accepted++;
        vm.Accept.Execute(null);
        Assert.Equal(1, accepted); Assert.Equal(1.5, vm.Options.MaxAttachmentMeters); Assert.Equal(100, vm.Options.MaxCableMeters);
    }
    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("oops")]
    public void InvalidAttachmentDoesNotCloseDialog(string input)
    {
        var vm = new SettingsViewModel(true) { Attachment = input }; var closed = false; vm.Accepted += () => closed = true;
        vm.Accept.Execute(null); Assert.False(closed); Assert.NotEmpty(vm.Error);
    }
    [Fact]
    public void AuditRequiresNonemptyMarkPrefix()
    {
        var vm = new SettingsViewModel(false) { Prefix = " " }; var closed = false; vm.Accepted += () => closed = true;
        vm.Accept.Execute(null); Assert.False(closed); Assert.NotEmpty(vm.Error);
    }
    [Fact]
    public void EmptyPreviewCannotBeAppliedEvenThroughProgrammaticCommandExecution()
    {
        var vm = new ReportViewModel("", Array.Empty<ReportRow>());
        Assert.False(vm.Apply.CanExecute(null)); vm.Apply.Execute(null); Assert.Equal(ReportAction.None, vm.Action);
    }
    [Fact]
    public void RouteHighlightRequiresSelectedRouteWithGeometry()
    {
        var good = new ReportRow { UniqueId = "a", HasPath = true }; var bad = new ReportRow { UniqueId = "b", HasPath = false };
        var vm = new ReportViewModel("", new[] { good, bad }); Assert.False(vm.Highlight.CanExecute(null));
        vm.Selected = bad; Assert.False(vm.Highlight.CanExecute(null)); vm.Selected = good; Assert.True(vm.Highlight.CanExecute(null));
        vm.Highlight.Execute(null); Assert.Equal(ReportAction.Highlight, vm.Action);
    }
    [Fact]
    public void FilteringDoesNotChangeOriginalRowsOrMatchingCaseRules()
    {
        var rows = new[] { new ReportRow { UniqueId = "a", Detail = "RD-01" }, new ReportRow { UniqueId = "b", Detail = "RD-02" } };
        var vm = new ReportViewModel("", rows) { Search = " rd-02 " };
        Assert.Equal("b", Assert.Single(vm.FilteredRows).UniqueId); Assert.Equal(2, vm.Rows.Count);
    }
    [Theory]
    [InlineData("ru-RU", "Закрыть")]
    [InlineData("en-US", "Close")]
    public void LocalizedResourcesWorkWithoutWpf(string language, string expected)
    {
        var before = CultureInfo.CurrentUICulture;
        try { CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language); Assert.Equal(expected, Strings.Get("Close")); }
        finally { CultureInfo.CurrentUICulture = before; }
    }
}
