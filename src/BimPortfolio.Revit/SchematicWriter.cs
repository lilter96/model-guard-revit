using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using BimPortfolio.Core;

namespace BimPortfolio.Revit;

public static class SchematicWriter
{
    static readonly Guid OwnershipId = new Guid("ad0c4e97-75c1-43a7-a553-9e8aa817e5ac");
    static Schema Ownership()
    {
        var schema = Schema.Lookup(OwnershipId);
        if (schema != null) return schema;
        var builder = new SchemaBuilder(OwnershipId);
        builder.SetSchemaName("AccessRouteGeneratedArtifact");
        builder.SetReadAccessLevel(AccessLevel.Public); builder.SetWriteAccessLevel(AccessLevel.Public);
        builder.AddSimpleField("Controller", typeof(string)); builder.AddSimpleField("Role", typeof(string));
        return builder.Finish();
    }
    static void Tag(Element element, Schema schema, string controller, string role)
    {
        var entity = new Entity(schema);
        entity.Set(schema.GetField("Controller"), controller); entity.Set(schema.GetField("Role"), role);
        element.SetEntity(entity);
    }
    static bool Owned(Element? element, Schema schema, string controller, string role)
    {
        if (element == null) return false;
        var entity = element.GetEntity(schema);
        return entity.IsValid() && entity.Get<string>(schema.GetField("Controller")) == controller && entity.Get<string>(schema.GetField("Role")) == role;
    }
    static XYZ Point(Point3 p) => new XYZ(p.X / 0.3048, p.Y / 0.3048, 0);
    public static IReadOnlyList<ViewDrafting> Generate(Document document, RoutePlan plan, string subtitle, string retiredCaption)
    {
        var pages = SchematicLayout.Build(plan, 18, subtitle);
        var type = new FilteredElementCollector(document).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .FirstOrDefault(t => t.ViewFamily == ViewFamily.Drafting) ?? throw new InvalidOperationException("No drafting view type is available.");
        var textType = document.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);
        if (textType == ElementId.InvalidElementId) throw new InvalidOperationException("No text note type is available.");
        var views = new List<ViewDrafting>();
        RevitTransaction.Run(document, "AccessRoute: regenerate connection schematics", () =>
        {
            var schema = Ownership();
            var allViews = new FilteredElementCollector(document).OfClass(typeof(ViewDrafting)).Cast<ViewDrafting>().ToList();
            foreach (var page in pages)
            {
                var role = "Page:" + page.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var matches = allViews.Where(v => Owned(v, schema, plan.ControllerUniqueId, role)).ToList();
                if (matches.Count > 1) throw new InvalidOperationException("Duplicate generated schematic pages found.");
                var view = matches.SingleOrDefault();
                if (view == null)
                {
                    view = ViewDrafting.Create(document, type.Id); view.Scale = 1;
                    // Name only identifies the view for humans; ownership always uses schema metadata.
                    view.Name = "AccessRoute " + plan.ControllerUniqueId.Substring(0, Math.Min(8, plan.ControllerUniqueId.Length)) + " " + (page.Index + 1) + " " + Guid.NewGuid().ToString("N").Substring(0, 4);
                    Tag(view, schema, plan.ControllerUniqueId, role);
                }
                RevitTransaction.RequireWritable(document, view);
                ClearGenerated(view, schema, plan.ControllerUniqueId);
                foreach (var line in page.Lines)
                {
                    var curve = document.Create.NewDetailCurve(view, Line.CreateBound(Point(line.Start), Point(line.End)));
                    Tag(curve, schema, plan.ControllerUniqueId, "Annotation");
                }
                foreach (var label in page.Labels)
                {
                    var note = TextNote.Create(document, view.Id, Point(label.Position), label.WidthMeters / 0.3048, label.Text, textType);
                    Tag(note, schema, plan.ControllerUniqueId, "Annotation");
                }
                views.Add(view);
            }
            // Obsolete owned pages retain human annotations and their view; only generated content is retired.
            foreach (var oldView in allViews)
            {
                var entity = oldView.GetEntity(schema);
                if (!entity.IsValid() || entity.Get<string>(schema.GetField("Controller")) != plan.ControllerUniqueId || views.Any(v => v.Id == oldView.Id)) continue;
                RevitTransaction.RequireWritable(document, oldView);
                ClearGenerated(oldView, schema, plan.ControllerUniqueId);
                var note = TextNote.Create(document, oldView.Id, XYZ.Zero, .28 / 0.3048, retiredCaption, textType);
                Tag(note, schema, plan.ControllerUniqueId, "Annotation");
            }
        });
        return views;
        void ClearGenerated(ViewDrafting view, Schema schema, string controller)
        {
            var generated = new FilteredElementCollector(document, view.Id).WhereElementIsNotElementType()
                .Where(e => Owned(e, schema, controller, "Annotation")).ToList();
            foreach (var element in generated)
            {
                RevitTransaction.RequireWritable(document, element);
                // Never allow a generated curve deletion to cascade into a user-owned dimension.
                if (element.GetDependentElements(null).Any(id => !Owned(document.GetElement(id), schema, controller, "Annotation")))
                    throw new InvalidOperationException("A generated annotation has user-owned dependents. Remove that dependency before regenerating.");
            }
            if (generated.Count > 0) document.Delete(generated.Select(e => e.Id).ToList());
        }
    }
}
