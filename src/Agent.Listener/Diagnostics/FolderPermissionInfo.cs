using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Microsoft.VisualStudio.Services.Agent.Listener.Diagnostics
{
    class FolderPermissionInfo : IDiagnosticInfo
    {
        public void Execute(ITerminal terminal)
        {
            try
            {
                terminal.WriteLine("Checking for Read & Write Permissions");

                string currentDirName = Directory.GetCurrentDirectory();
                DirectoryInfo directoryInfo = new DirectoryInfo(currentDirName);
                terminal.WriteLine($"{directoryInfo.FullName.PadRight(c_padding)} {HasFolderWritePermission(terminal, currentDirName)}");

                DirectoryInfo[] folders = directoryInfo.GetDirectories();
                foreach (DirectoryInfo folder in folders)
                {
                    terminal.WriteLine($"{folder.FullName.PadRight(c_padding)} {HasFolderWritePermission(terminal, folder.FullName)}");
                }

                string[] files = Directory.GetFiles(currentDirName);
                foreach (string file in files)
                {
                    terminal.WriteLine($"{file.PadRight(c_padding)} {HasFileReadWritePermission(terminal, new FileInfo(file))}");
                }
            }
            catch (Exception ex)
            {
                terminal.WriteError(ex);
            }
        }

        // There isn't a cross-plat lookup to easily determine if a directory is writable
        // The easiest generic approach is to attempt the operation
        private bool HasFolderWritePermission(ITerminal terminal, string dirPath)
        {
            try
            {
                using (FileStream fs = File.Create(Path.Combine(dirPath, Path.GetRandomFileName()), 1, FileOptions.DeleteOnClose))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                terminal.WriteError(ex);
                return false;
            }
        }

        // There isn't a cross-plat lookup to easily determine read / write / lock permissions
        // The easiest generic approach is to attempt to open the file in ReadWrite mode
        private bool HasFileReadWritePermission(ITerminal terminal, FileInfo file)
        {
            try
            {
                using (FileStream stream = file.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    stream.Close();
                    return true;
                }
            }
            catch (Exception ex)
            {
                terminal.WriteError(ex);
                return false;
            }
        }

        private const int c_padding = 75;
    }
}
