using System;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using BimPortfolio.Core;
namespace BimPortfolio.Revit;
public static class PlanStorage
{
    public static readonly Guid SchemaId = new Guid("f3ad12b8-1bdf-4e8b-9e7b-468831919de2");
    static Schema? Existing() => Schema.Lookup(SchemaId);
    static Schema Create()
    {
        var existing = Existing(); if (existing != null) return existing;
        var b = new SchemaBuilder(SchemaId); b.SetSchemaName("AccessRoutePlan"); b.SetReadAccessLevel(AccessLevel.Public); b.SetWriteAccessLevel(AccessLevel.Public);
        b.AddSimpleField("Json", typeof(string)); return b.Finish();
    }
    static DataStorage? Find(Document doc, Schema schema)
    {
        var stores = new FilteredElementCollector(doc).OfClass(typeof(DataStorage)).Cast<DataStorage>().Where(d => d.GetEntity(schema).IsValid()).ToList();
        if (stores.Count > 1) throw new InvalidOperationException("Multiple route stores found. Resolve duplication before saving.");
        return stores.SingleOrDefault();
    }
    public static RoutePlan? Load(Document doc)
    {
        var schema = Existing(); if (schema == null) return null; var store = Find(doc, schema);
        return store == null ? null : PlanJson.Read(store.GetEntity(schema).Get<string>(schema.GetField("Json")));
    }
    public static void Save(Document doc, RoutePlan plan)
    {
        if (doc.IsReadOnly) throw new InvalidOperationException("Document is read-only.");
        // Load before overwriting: malformed/future versions block writes.
        Load(doc); var json = PlanJson.Write(plan);
        RevitTransaction.Run(doc, "AccessRoute: save verified route plan", () =>
        {
            var schema = Create();
            var store = Find(doc, schema) ?? DataStorage.Create(doc);
            RevitTransaction.RequireWritable(doc, store);
            var entity = new Entity(schema);
            entity.Set(schema.GetField("Json"), json);
            store.SetEntity(entity);
        });
    }
}
