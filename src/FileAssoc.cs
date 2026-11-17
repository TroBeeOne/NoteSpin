using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace NoteSpin
{
    /// <summary>
    /// Optional: lets Explorer open .notespin files with this NoteSpin.exe (double-click → the import choice).
    /// Written for the current user only (HKCU), so it needs no admin rights; only done when asked, in Settings.
    /// </summary>
    internal static class FileAssoc
    {
        const string ProgId = "NoteSpin.Notebook";
        const string ExtKey = @"Software\Classes\" + Notebook.Ext;
        const string ProgKey = @"Software\Classes\" + ProgId;

        [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);
        const int SHCNE_ASSOCCHANGED = 0x08000000;

        static string Exe => System.Reflection.Assembly.GetExecutingAssembly().Location;
        static string Command => $"\"{Exe}\" \"%1\"";

        /// <summary>True when .notespin files open with this copy of NoteSpin.exe.</summary>
        public static bool IsSet()
        {
            try
            {
                using (var ext = Registry.CurrentUser.OpenSubKey(ExtKey))
                using (var cmd = Registry.CurrentUser.OpenSubKey(ProgKey + @"\shell\open\command"))
                    return (ext?.GetValue("") as string) == ProgId &&
                           string.Equals(cmd?.GetValue("") as string, Command, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public static void Set(bool on)
        {
            if (on)
            {
                using (var ext = Registry.CurrentUser.CreateSubKey(ExtKey)) ext.SetValue("", ProgId);
                using (var prog = Registry.CurrentUser.CreateSubKey(ProgKey))
                {
                    prog.SetValue("", "NoteSpin notebook");
                    using (var icon = prog.CreateSubKey("DefaultIcon")) icon.SetValue("", $"\"{Exe}\",0");
                    using (var cmd = prog.CreateSubKey(@"shell\open\command")) cmd.SetValue("", Command);
                }
            }
            else
            {
                bool empty = false;
                using (var ext = Registry.CurrentUser.OpenSubKey(ExtKey, true))
                {
                    if ((ext?.GetValue("") as string) == ProgId) ext.DeleteValue("", false);
                    using (var owp = ext?.OpenSubKey("OpenWithProgids", true))
                    {
                        owp?.DeleteValue(ProgId, false);
                        if (owp != null && owp.ValueCount == 0 && owp.SubKeyCount == 0) ext.DeleteSubKey("OpenWithProgids", false);
                    }
                    empty = ext != null && ext.ValueCount == 0 && ext.SubKeyCount == 0;
                }
                if (empty) Registry.CurrentUser.DeleteSubKey(ExtKey, false);   // nothing else uses it: leave no trace
                Registry.CurrentUser.DeleteSubKeyTree(ProgKey, false);
            }
            SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
        }
    }
}
