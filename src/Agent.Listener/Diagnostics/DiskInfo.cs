using System.IO;

namespace Microsoft.VisualStudio.Services.Agent.Listener.Diagnostics
{
    class DiskInfo : IDiagnosticInfo
    {
        public void Execute(ITerminal terminal)
        {
            string root = Path.GetPathRoot(System.Reflection.Assembly.GetEntryAssembly().Location);
            terminal.WriteLine($"Agent running on Drive {root}");

            try
            {
                DriveInfo[] allDrives = DriveInfo.GetDrives();
                foreach (DriveInfo d in allDrives)
                {
                    terminal.WriteLine($"Drive {d.Name}");
                    terminal.WriteLine($"  Drive type: {d.DriveType}");
                    if (d.IsReady == true)
                    {
                        terminal.WriteLine($"  Volume label: {d.VolumeLabel}");
                        terminal.WriteLine($"  File system: {d.DriveFormat}");
                        terminal.WriteLine(string.Format("  Available space to current user:{0, 15:N0} KB", d.AvailableFreeSpace / c_kb));
                        terminal.WriteLine(string.Format("  Total available space:          {0, 15:N0} KB", d.TotalFreeSpace / c_kb));
                        terminal.WriteLine(string.Format("  Total size of drive:            {0, 15:N0} KB", d.TotalSize / c_kb));
                    }
                    else
                    {
                        terminal.WriteLine($"  Drive is not Ready");
                    }
                }
            }
            catch (IOException ex)
            {
                terminal.WriteError(ex);
            }
            catch (System.UnauthorizedAccessException ex)
            {
                terminal.WriteError(ex);
            }
        }

        private const int c_kb = 1024;
    }
}
