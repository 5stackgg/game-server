using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FiveStack.Utilities;

// One set of options for everything the practice plugin exchanges with the
// panel. There is deliberately no custom converter here: the API owns the wire
// shapes, and the types in FiveStack.Entities.Practice that carry its spelling
// serialize as written.
public static class PracticeJson
{
    public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    // The trajectory artifact is stored gzipped and streamed back exactly as it
    // is stored, so a response body is bytes and only sometimes text.
    public static string Text(byte[] body)
    {
        if (body.Length < 2 || body[0] != 0x1F || body[1] != 0x8B)
        {
            return Encoding.UTF8.GetString(body);
        }

        using var compressed = new MemoryStream(body);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var plain = new MemoryStream();

        gzip.CopyTo(plain);

        return Encoding.UTF8.GetString(plain.ToArray());
    }
}
