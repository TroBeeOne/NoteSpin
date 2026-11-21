using System.Collections.Generic;
#if NETFRAMEWORK
using System.Web.Script.Serialization;
#else
using System.Text.Encodings.Web;
using System.Text.Json;
#endif

namespace NoteSpin
{
    /// <summary>
    /// JSON helper shared between .NET Framework (JavaScriptSerializer) and modern .NET (System.Text.Json).
    /// Deserialises objects as Dictionary&lt;string, object&gt; on both platforms; arrays come back as object[] on
    /// .NET Framework and List&lt;object&gt; on modern .NET, so callers treat them as IEnumerable.
    /// </summary>
    internal static class JsonCodec
    {
#if NETFRAMEWORK
        static readonly JavaScriptSerializer Ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static string Serialize(object value) => Ser.Serialize(value);

        public static object Deserialize(string json) => Ser.DeserializeObject(json);
#else
        static readonly JsonSerializerOptions Opts = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            MaxDepth = 128,
        };

        public static string Serialize(object value) => JsonSerializer.Serialize(value, Opts);

        public static object Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            using (var doc = JsonDocument.Parse(json))
                return Unwrap(doc.RootElement);
        }

        static object Unwrap(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Object:
                    var d = new Dictionary<string, object>();
                    foreach (var p in e.EnumerateObject()) d[p.Name] = Unwrap(p.Value);
                    return d;
                case JsonValueKind.Array:
                    var list = new List<object>();
                    foreach (var item in e.EnumerateArray()) list.Add(Unwrap(item));
                    return list;
                case JsonValueKind.String:
                    return e.GetString();
                case JsonValueKind.Number:
                    if (e.TryGetInt64(out long l)) return l;
                    return e.GetDouble();
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
                default:
                    return null;
            }
        }
#endif
    }
}
