using System.Globalization;
using System.Resources;
namespace BimPortfolio.Presentation;
public static class Strings
{
    static readonly ResourceManager Resources = new ResourceManager("BimPortfolio.Presentation.Resources.Strings", typeof(Strings).Assembly);
    public static string Get(string key) => Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;
    public static string Status(string code) => Get(code);
}
