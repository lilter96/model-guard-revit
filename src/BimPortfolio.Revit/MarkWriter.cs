using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using BimPortfolio.Core;
namespace BimPortfolio.Revit;
public static class MarkWriter
{
    public static void Apply(Document doc, IReadOnlyList<MarkChange> changes)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("Document is read-only.");
        if (changes.Count == 0) return;
        var existing = new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements()
            .Select(e => e.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString()?.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!);
        var occupied = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase); var ids = new HashSet<string>(StringComparer.Ordinal);
        var parameters = new List<(Parameter Parameter, string Value)>();
        foreach (var change in changes)
        {
            if (!ids.Add(change.ElementUniqueId) || string.IsNullOrWhiteSpace(change.NewMark) || !occupied.Add(change.NewMark.Trim())) throw new InvalidOperationException("Invalid or conflicting mark proposal.");
            var element = doc.GetElement(change.ElementUniqueId) ?? throw new InvalidOperationException("An element was deleted.");
            RevitTransaction.RequireWritable(doc, element);
            var p = element.get_Parameter(BuiltInParameter.ALL_MODEL_MARK);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.String || (p.AsString() ?? "") != change.OldMark || !string.IsNullOrWhiteSpace(change.OldMark))
                throw new InvalidOperationException("Element changed or mark is not writable: " + element.Name);
            parameters.Add((p, change.NewMark));
        }
        RevitTransaction.Run(doc, "ModelGuard: fill missing marks", () =>
        {
            foreach (var entry in parameters)
                if (!entry.Parameter.Set(entry.Value)) throw new InvalidOperationException("Revit rejected a mark change.");
        });
    }
}
