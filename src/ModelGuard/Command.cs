using BimPortfolio.Presentation;
using System;
using System.Linq;
using System.Globalization;
using System.IO;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BimPortfolio.Core;
using BimPortfolio.Revit;
using BimPortfolio.UI;
using Microsoft.Win32;
namespace ModelGuard;
[Transaction(TransactionMode.Manual)]
public sealed class Command : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        try
        {
            var ui = data.Application.ActiveUIDocument; if (!CommandHelpers.Valid(ui)) { TaskDialog.Show("ModelGuard", Strings.Get("NoDocument")); return Result.Cancelled; }
            var owner = data.Application.MainWindowHandle; var settings = new SettingsViewModel(false); if (!Dialogs.Settings(settings, owner)) return Result.Cancelled;
            var required = settings.Required.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).ToList();
            var profile = string.IsNullOrWhiteSpace(settings.ProfilePath) ? AuditProfile.FromNames(required) : AuditProfileJson.Read(File.ReadAllText(settings.ProfilePath));
            while (true)
            {
                var snapshots = ModelReader.AuditElements(ui.Document, profile); var issues = ProfileAuditor.Inspect(snapshots, profile);
                var rows = issues.Select(i => new ReportRow { UniqueId = i.ElementUniqueId, Code = Strings.Status(i.Code), StatusKey = i.Code, Severity = i.Severity, Detail = i.Detail }).ToList();
                var vm = new ReportViewModel(string.Format(CultureInfo.CurrentCulture, Strings.Get("AuditSummary"), snapshots.Count, issues.Count, profile.Name), rows);
                var action = Dialogs.Report(vm, owner);
                if (action == ReportAction.None) break;
                try
                {
                    if (action == ReportAction.Select && vm.Selected != null) CommandHelpers.Show(ui, vm.Selected.UniqueId);
                    if (action == ReportAction.Export) { var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "ModelGuard.csv" }; if (dialog.ShowDialog() == true) CommandHelpers.WriteCsv(dialog.FileName, CsvReport.Issues(issues)); }
                    if (action == ReportAction.Preview)
                    {
                        // Number against all marks in the document, not just the audited categories.
                        var auditedIds = new System.Collections.Generic.HashSet<string>(snapshots.Select(s => s.UniqueId), StringComparer.Ordinal);
                        var allMarks = new FilteredElementCollector(ui.Document).WhereElementIsNotElementType().ToElements()
                            .Where(e => !auditedIds.Contains(e.UniqueId))
                            .Select(e => new ElementSnapshot { UniqueId = e.UniqueId, Mark = e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "", CanWriteMark = false });
                        var changes = ModelAuditor.ProposeMissingMarks(snapshots.Concat(allMarks), settings.Prefix);
                        var previewRows = changes.Select(c => new ReportRow { UniqueId = c.ElementUniqueId, Code = Strings.Get("PreviewCode"), Detail = c.NewMark }).ToList();
                        var preview = new ReportViewModel(string.Format(CultureInfo.CurrentCulture, Strings.Get("PreviewSummary"), changes.Count), previewRows);
                        var decision = Dialogs.Report(preview, owner, preview: true);
                        if (decision == ReportAction.Apply) { MarkWriter.Apply(ui.Document, changes); TaskDialog.Show("ModelGuard", string.Format(CultureInfo.CurrentCulture, Strings.Get("Applied"), changes.Count)); }
                        if (decision == ReportAction.Select && preview.Selected != null) CommandHelpers.Show(ui, preview.Selected.UniqueId);
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
                catch (Exception error) { OperationLog.Error("ModelGuard/" + action, error); TaskDialog.Show(Strings.Get("Error"), error.Message); }

            }
            return Result.Succeeded;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
        catch (Exception ex) { OperationLog.Error("ModelGuard/audit", ex); message = ex.Message; TaskDialog.Show(Strings.Get("Error"), ex.Message); return Result.Failed; }
    }
}
