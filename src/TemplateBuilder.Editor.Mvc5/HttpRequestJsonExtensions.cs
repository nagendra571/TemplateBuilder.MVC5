using System.IO;
using System.Threading.Tasks;
using System.Web;
using Newtonsoft.Json;

namespace TemplateBuilder.Editor.Mvc5;

public static class HttpRequestJsonExtensions
{
    public static async Task<T> ReadJsonBodyAsync<T>(this HttpRequestBase request)
    {
        var stream = request.InputStream;
        if (stream.CanSeek && stream.Length > 0 && stream.Position >= stream.Length)
            stream.Position = 0;
        using var reader = new StreamReader(stream);
        var body = await reader.ReadToEndAsync();
        return JsonConvert.DeserializeObject<T>(body)!;
    }
}
