using System;
using System.IO;
using System.Windows.Forms;

namespace NoteSpin
{
    internal static class Program
    {
        /// <param name="args">Optionally a .notespin file (a double-click in Explorer, or "Open with"): the page offers to import it.</param>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
                MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, "NoteSpin", MessageBoxButtons.OK, MessageBoxIcon.Error);

            string file = null;
            foreach (string a in args)
            {
                try { if (File.Exists(a)) { file = Path.GetFullPath(a); break; } }
                catch { }
            }

            // The app's own settings live per PC in %LOCALAPPDATA%\NoteSpin (NOTESPIN_DATA overrides it, for testing)
            string testData = Environment.GetEnvironmentVariable("NOTESPIN_DATA");
            string dataDir = string.IsNullOrWhiteSpace(testData)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoteSpin")
                : testData;

            // Notebooks go in Documents\NoteSpin, which OneDrive backs up when it backs up Documents. A test data
            // folder keeps its own notebooks; NOTESPIN_NOTEBOOKS stands in for Documents\NoteSpin in tests.
            string notebooksDir = Environment.GetEnvironmentVariable("NOTESPIN_NOTEBOOKS");
            if (string.IsNullOrWhiteSpace(notebooksDir))
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                notebooksDir = string.IsNullOrWhiteSpace(testData) && !string.IsNullOrEmpty(docs)
                    ? Path.Combine(docs, "NoteSpin")
                    : Path.Combine(dataDir, "Notebooks");
            }

            Settings settings;
            Library lib;
            try
            {
                Directory.CreateDirectory(dataDir);
                settings = new Settings(dataDir);
                lib = new Library(dataDir, settings, notebooksDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("NoteSpin couldn't open its data in\n" + dataDir + "\n\n" + ex.Message, "NoteSpin", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            using (settings)
            using (lib)
                Application.Run(new MainForm(dataDir, settings, lib, file));
        }
    }
}
