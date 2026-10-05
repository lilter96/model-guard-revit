using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using BimPortfolio.Core;
namespace BimPortfolio.Presentation;
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
public sealed class DelegateCommand : ICommand
{
    readonly Action run; readonly Func<bool> can;
    public DelegateCommand(Action run, Func<bool>? can = null) { this.run = run; this.can = can ?? (() => true); }
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => can();
    public void Execute(object? parameter) { if (can()) run(); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
public sealed class SettingsViewModel : Observable
{
    public bool IsRoute { get; }
    public string Attachment { get; set; }
    public string Reserve { get; set; }
    public string Allowance { get; set; }
    public string CableLimit { get; set; }
    string profilePath = "";
    public string ProfilePath { get => profilePath; set { profilePath = value; Changed(); } }
    public DelegateCommand BrowseProfile { get; }
    public event Action? BrowseProfileRequested;
    public string Required { get; set; } = "";
    public string Prefix { get; set; } = "ACS-";
    string error = ""; public string Error { get => error; private set { error = value; Changed(); } }
    public RouteOptions Options { get; private set; } = new RouteOptions();
    public DelegateCommand Accept { get; }
    public event Action? Accepted;
    public SettingsViewModel(bool route, RouteOptions? initial = null)
    {
        IsRoute = route; initial = initial ?? new RouteOptions();
        BrowseProfile = new DelegateCommand(() => BrowseProfileRequested?.Invoke());
        CableLimit = initial.MaxCableMeters.ToString(CultureInfo.CurrentCulture);
        Attachment = initial.MaxAttachmentMeters.ToString(CultureInfo.CurrentCulture);
        Reserve = initial.ReservePercent.ToString(CultureInfo.CurrentCulture);
        Allowance = initial.TerminationAllowanceMeters.ToString(CultureInfo.CurrentCulture);
        Accept = new DelegateCommand(() =>
        {
            try
            {
                if (IsRoute)
                {
                    Options = new RouteOptions { MaxAttachmentMeters = Parse(Attachment), ReservePercent = Parse(Reserve), TerminationAllowanceMeters = Parse(Allowance), MaxCableMeters = Parse(CableLimit) }; Options.Validate();
                }
                else if (string.IsNullOrWhiteSpace(Prefix)) throw new ArgumentException();
                Error = ""; Accepted?.Invoke();
            }
            catch (ArgumentException) { Error = Strings.Get("InvalidSettings"); }
            catch (FormatException) { Error = Strings.Get("InvalidSettings"); }
            catch (OverflowException) { Error = Strings.Get("InvalidSettings"); }
        });
    }
    static double Parse(string value) => double.Parse(value.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
}
public sealed class ReportRow
{
    public string UniqueId { get; set; } = ""; public string Code { get; set; } = "";
    public string StatusKey { get; set; } = "";
    public bool HasPath { get; set; }
    public string Severity { get; set; } = "";
    public string Detail { get; set; } = ""; public double? Length { get; set; }
}
public enum ReportAction { None, Save, Export, Select, Preview, Apply, Html, Schematic, Highlight }
public sealed class ReportViewModel : Observable
{
    public string Summary { get; }
    public IReadOnlyList<ReportRow> Rows { get; }
    string search = "";
    public string Search { get => search; set { search = value; Changed(); Changed(nameof(FilteredRows)); } }
    public IEnumerable<ReportRow> FilteredRows => Rows.Where(row => string.IsNullOrWhiteSpace(Search) ||
        (row.UniqueId + " " + row.Code + " " + row.Detail).IndexOf(Search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
    public DelegateCommand Html { get; }
    public DelegateCommand Schematic { get; }
    public DelegateCommand Highlight { get; }
    ReportRow? selected;
    public ReportRow? Selected { get => selected; set { selected = value; Changed(); Select.Refresh(); Highlight.Refresh(); } }
    public ReportAction Action { get; private set; }
    public DelegateCommand Save { get; }
    public DelegateCommand Export { get; }
    public DelegateCommand Select { get; }
    public DelegateCommand Preview { get; }
    public DelegateCommand Apply { get; }
    public event Action? Finished;
    public ReportViewModel(string summary, IReadOnlyList<ReportRow> rows)
    {
        Summary = summary; Rows = rows;
        Html = new DelegateCommand(() => Finish(ReportAction.Html));
        Schematic = new DelegateCommand(() => Finish(ReportAction.Schematic), () => Rows.Count > 0);
        Highlight = new DelegateCommand(() => Finish(ReportAction.Highlight), () => Selected?.HasPath == true);
        Save = new DelegateCommand(() => Finish(ReportAction.Save));
        Export = new DelegateCommand(() => Finish(ReportAction.Export)); Select = new DelegateCommand(() => Finish(ReportAction.Select), () => Selected != null);
        Preview = new DelegateCommand(() => Finish(ReportAction.Preview)); Apply = new DelegateCommand(() => Finish(ReportAction.Apply), () => Rows.Count > 0);
    }
    void Finish(ReportAction action) { Action = action; Finished?.Invoke(); }
}
