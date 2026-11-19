using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

namespace NoteSpin
{
    internal static class Program
    {
        /// <param name="args">
        /// Optionally a .notespin file (a double-click in Explorer, or "Open with"): the page offers to import it.
        /// "--switch on|off [--bring] --after pid": NoteSpin restarting to turn portable on or off (see PortableSwitch).
        /// </param>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
                MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, "NoteSpin", MessageBoxButtons.OK, MessageBoxIcon.Error);

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
                    MessageBox.Show((switchTo == "on" ? "NoteSpin couldn't be made portable." : "NoteSpin couldn't finish going back to this PC's storage.") +
                        "\n\n" + ex.Message, "NoteSpin", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            // Settings and the web view's files: %LOCALAPPDATA%\NoteSpin, or NoteSpin Data beside the exe when
            // portable. Notebooks: Documents\NoteSpin (or NoteSpin Data\Notebooks), unless another folder is chosen.
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
                MessageBox.Show("NoteSpin couldn't open its data in\n" + dataDir + "\n\n" + ex.Message, "NoteSpin", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (switched != null)
            {
                // said when the page starts, along with anything the notebooks folder has to say
                if (lib.Notice != null) foreach (var kv in lib.Notice) switched[kv.Key] = kv.Value;
                lib.Notice = switched;
            }
            using (settings)
            using (lib)
                Application.Run(new MainForm(dataDir, settings, lib, file));
        }
    }
}
