using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace NoteSpin
{
    /// <summary>
    /// Optional per-user XDG file association on Linux: registers *.notespin (application/x-notespin)
    /// and a .desktop entry under ~/.local/share so double-clicking a backup in the file manager
    /// opens it in this copy of NoteSpin. Needs no admin rights and removes cleanly when turned off.
    /// </summary>
    internal static class FileAssoc
    {
        const string MimeType = "application/x-notespin";
        static string LocalShare => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        static string DesktopFile => Path.Combine(LocalShare, "applications", "notespin.desktop");
        static string MimeFile => Path.Combine(LocalShare, "mime", "packages", "notespin.xml");
        static string IconFile => Path.Combine(LocalShare, "icons", "hicolor", "256x256", "apps", "notespin.png");
        static string LegacyIconFile => Path.Combine(LocalShare, "icons", "notespin.ico");

        static string Exe
        {
            get
            {
                string launcher = Path.Combine(Places.ExeDir, "NoteSpin");
                return File.Exists(launcher) ? launcher : (Environment.ProcessPath ?? launcher);
            }
        }
        static string ExecLine => $"Exec=\"{Exe}\" %f";

        public static bool IsSet()
        {
            try
            {
                if (!File.Exists(DesktopFile) || !File.Exists(MimeFile)) return false;
                string txt = File.ReadAllText(DesktopFile);
                return txt.Contains(ExecLine, StringComparison.Ordinal);
            }
            catch { return false; }
        }

        public static void Set(bool on)
        {
            if (on)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MimeFile));
                Directory.CreateDirectory(Path.GetDirectoryName(DesktopFile));
                Directory.CreateDirectory(Path.GetDirectoryName(IconFile));

                byte[] ico = WebAssets.Favicon();
                byte[] png = GtkNative.ExtractPngFromIco(ico);
                if (png != null && png.Length > 0) File.WriteAllBytes(IconFile, png);
                TryDelete(LegacyIconFile);

                string mimeXml = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<mime-info xmlns=""http://www.freedesktop.org/standards/shared-mime-info"">
  <mime-type type=""{MimeType}"">
    <comment>NoteSpin notebook</comment>
    <glob pattern=""*{Notebook.Ext}""/>
  </mime-type>
</mime-info>
";
                File.WriteAllText(MimeFile, mimeXml, new UTF8Encoding(false));

                string desktop = $@"[Desktop Entry]
Type=Application
Name=NoteSpin
Comment=Sticky notes on a spinning card wheel
{ExecLine}
Icon={IconFile}
Terminal=false
Categories=Utility;Office;
MimeType={MimeType};
StartupWMClass=notespin
";
                File.WriteAllText(DesktopFile, desktop, new UTF8Encoding(false));
            }
            else
            {
                TryDelete(DesktopFile);
                TryDelete(MimeFile);
                TryDelete(IconFile);
                TryDelete(LegacyIconFile);
            }

            RunQuiet("update-mime-database", Path.Combine(LocalShare, "mime"));
            RunQuiet("update-desktop-database", Path.Combine(LocalShare, "applications"));
            RunQuiet("gtk-update-icon-cache", Path.Combine(LocalShare, "icons", "hicolor"));
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        static void RunQuiet(string tool, string arg)
        {
            try
            {
                var psi = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                psi.ArgumentList.Add(arg);
                using (var p = Process.Start(psi)) p?.WaitForExit(5000);
            }
            catch { }
        }
    }
}
