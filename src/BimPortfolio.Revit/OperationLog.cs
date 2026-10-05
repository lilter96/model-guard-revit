using System;
using System.IO;
using Newtonsoft.Json;
namespace BimPortfolio.Revit;
public static class OperationLog
{
    public static void Error(string operation, Exception error)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BimPortfolio", "logs");
            Directory.CreateDirectory(folder);
            var record = new { Utc = DateTime.UtcNow, Operation = operation, ErrorType = error.GetType().FullName, Message = error.Message };
            File.AppendAllText(Path.Combine(folder, DateTime.UtcNow.ToString("yyyy-MM-dd") + ".jsonl"), JsonConvert.SerializeObject(record) + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
