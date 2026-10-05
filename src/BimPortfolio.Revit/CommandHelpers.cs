using System;
using System.IO;
using System.Text;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
namespace BimPortfolio.Revit;
public static class CommandHelpers
{
    public static void Show(UIDocument uiDoc, string uniqueId)
    {
        var element = uiDoc.Document.GetElement(uniqueId) ?? throw new InvalidOperationException("Element no longer exists.");
        uiDoc.Selection.SetElementIds(new[] { element.Id }); uiDoc.ShowElements(element.Id);
    }
    public static void WriteCsv(string path, string content) => File.WriteAllText(path, content, new UTF8Encoding(true));
    public static bool Valid(UIDocument? doc) => doc != null && !doc.Document.IsFamilyDocument;
}
