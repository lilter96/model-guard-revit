using BimPortfolio.Presentation;
using System.Reflection;
using Autodesk.Revit.UI;
using BimPortfolio.UI;
namespace ModelGuard;
public sealed class App : IExternalApplication
{
    public Result OnStartup(UIControlledApplication app)
    {
        var panel = app.CreateRibbonPanel("ModelGuard");
        var data = new PushButtonData("ModelGuardCommand", Strings.Get("AuditButton"), Assembly.GetExecutingAssembly().Location, "ModelGuard.Command");
        var button = (PushButton)panel.AddItem(data); button.Image = RibbonIcons.Create(false,16);
        button.LargeImage = RibbonIcons.Create(false,32);
        button.ToolTip = Strings.Get("AuditTip"); return Result.Succeeded;
    }
    public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;
}
