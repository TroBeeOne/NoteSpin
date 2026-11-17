using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace NoteSpin
{
    /// <summary>The pages and scripts compiled into the exe (the project's web\ folder).</summary>
    internal static class WebAssets
    {
        static readonly Assembly Asm = typeof(WebAssets).Assembly;
        static readonly Dictionary<string, string> Names = BuildIndex();

        static Dictionary<string, string> BuildIndex()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string n in Asm.GetManifestResourceNames())
            {
                string p = n.Replace('\\', '/');
                if (p.StartsWith("web/", StringComparison.Ordinal)) map[p.Substring(4)] = n;
            }
            return map;
        }

        public static byte[] Get(string path)
        {
            if (!Names.TryGetValue(path, out string name)) return null;
            using (var s = Asm.GetManifestResourceStream(name))
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        public static Icon AppIcon()
        {
            using (var s = Asm.GetManifestResourceStream("favicon.ico")) return s == null ? null : new Icon(s);
        }

        public static byte[] Favicon()
        {
            using (var s = Asm.GetManifestResourceStream("favicon.ico"))
            using (var ms = new MemoryStream()) { s?.CopyTo(ms); return ms.ToArray(); }
        }

        public static string MimeType(string path)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".html": return "text/html; charset=utf-8";
                case ".js": return "text/javascript; charset=utf-8";
                case ".css": return "text/css; charset=utf-8";
                case ".json": return "application/json; charset=utf-8";
                case ".txt": return "text/plain; charset=utf-8";
                case ".svg": return "image/svg+xml";
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": case ".jfif": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".webp": return "image/webp";
                case ".ico": return "image/x-icon";
                case ".bmp": return "image/bmp";
                case ".avif": return "image/avif";
                case ".tif": case ".tiff": return "image/tiff";
                case ".heic": return "image/heic";
                case ".heif": return "image/heif";
                case ".woff2": return "font/woff2";
                case ".woff": return "font/woff";
                default: return "application/octet-stream";
            }
        }
    }
}
