using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.Revit.DB;
using BimPortfolio.Core;

namespace BimPortfolio.Revit;

public sealed class RevitRoutingDocument : IRoutingSnapshotSource, IRoutePlanRepository
{
    readonly Document document;
    public RevitRoutingDocument(Document document) => this.document = document;
    public RoutingSnapshot Capture(string controllerUniqueId, IReadOnlyList<string> deviceUniqueIds, CancellationToken cancellation)
    {
        var controller = Read(controllerUniqueId);
        var devices = deviceUniqueIds.Select(Read).ToList();
        return new RoutingSnapshot(ModelReader.Graph(document, cancellation), controller, devices);
        DevicePoint Read(string id)
        {
            cancellation.ThrowIfCancellationRequested();
            var element = document.GetElement(id) as FamilyInstance ?? throw new InvalidOperationException("Selected equipment was deleted or is not a family instance: " + id);
            return ModelReader.Device(element);
        }
    }
    public RoutePlan? Load() => PlanStorage.Load(document);
    public void Save(RoutePlan plan) => PlanStorage.Save(document, plan);
}
