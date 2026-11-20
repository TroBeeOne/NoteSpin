using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using static NoteSpin.GtkNative;

namespace NoteSpin
{
    internal static class Program
    {
        /// <param name="args">
        /// Optionally a .notespin file (a double-click in the file manager, or "Open with"): the page offers to import it.
        /// "--switch on|off [--bring] --after pid": NoteSpin restarting to turn portable on or off (see PortableSwitch).
        /// </param>
        static void Main(string[] args)
        {
            g_set_prgname("notespin");
            int argc = 0;
            IntPtr argv = IntPtr.Zero;
            gtk_init(ref argc, ref argv);
            gdk_set_program_class("NoteSpin");
            SynchronizationContext.SetSynchronizationContext(new GtkSyncContext());

            string file = null, switchTo = null;
            bool bring = false;
            int after = 0;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--switch" && i + 1 < args.Length) { switchTo = args[++i]; continue; }
                if (a == "--after" && i + 1 < args.Length) { int.TryParse(args[++i], out after); continue; }
                if (a == "--bring") { bring = true; continue; }
                if (file != null) continue;
                try { if (File.Exists(a)) file = Path.GetFullPath(a); }
                catch { }
            }

            Dictionary<string, object> switched = null;
            if (switchTo == "on" || switchTo == "off")
            {
                try { switched = PortableSwitch.Run(switchTo == "on", bring, after); }
                catch (Exception ex)
                {
                    ShowMessage(IntPtr.Zero,
                        (switchTo == "on" ? "NoteSpin couldn't be made portable." : "NoteSpin couldn't finish going back to this PC's storage.") +
                        "\n\n" + ex.Message, error: true);
                }
            }

            // Settings and the web view's files: ~/.local/share/NoteSpin, or NoteSpin Data beside NoteSpin when portable.
            // Notebooks: ~/Documents/NoteSpin (or NoteSpin Data/Notebooks), unless another folder is chosen.
            Places.Decide();
            string dataDir = Places.DataDir;
            Settings settings;
            Library lib;
            try
            {
                Directory.CreateDirectory(dataDir);
                settings = new Settings(dataDir);
                lib = new Library(dataDir, settings, Places.NotebooksDir);
            }
            catch (Exception ex)
            {
                ShowMessage(IntPtr.Zero, "NoteSpin couldn't open its data in\n" + dataDir + "\n\n" + ex.Message, error: true);
                return;
            }
            if (switched != null)
            {
                if (lib.Notice != null) foreach (var kv in lib.Notice) switched[kv.Key] = kv.Value;
                lib.Notice = switched;
            }

            using (settings)
            using (lib)
            {
                var main = new MainForm(dataDir, settings, lib, file);
                main.Show();
                gtk_main();
            }
        }
    }
}
