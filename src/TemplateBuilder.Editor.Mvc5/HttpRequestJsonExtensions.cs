using System.IO;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json;

namespace TemplateBuilder.Editor.Mvc5;

public static class HttpRequestJsonExtensions
{
    public static async Task<T> ReadJsonBodyAsync<T>(this HttpRequestBase request)
    {
        using var reader = new StreamReader(request.InputStream);
        var body = await reader.ReadToEndAsync();
        return JsonConvert.DeserializeObject<T>(body)!;
    }
}