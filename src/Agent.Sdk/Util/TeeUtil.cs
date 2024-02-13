// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    public class TeeUtil
    {
        private static readonly string TeeTempDir = "tee_temp_dir";

        private static readonly string TeePluginName = "TEE-CLC-14.135.0";

        private static readonly string TeeUrl = $"https://vstsagenttools.blob.core.windows.net/tools/tee/14_135_0/{TeePluginName}.zip";

        private string agentHomeDirectory;
        private string agentTempDirectory;
        private int downloadRetryCount;
        private Action<string> debug;
        private CancellationToken cancellationToken;

        public TeeUtil(
            string agentHomeDirectory,
            string agentTempDirectory,
            int providedDownloadRetryCount,
            Action<string> debug,
            CancellationToken cancellationToken
        )
        {
            this.agentHomeDirectory = agentHomeDirectory;
            this.agentTempDirectory = agentTempDirectory;
            this.downloadRetryCount = Math.Min(Math.Max(providedDownloadRetryCount, 3), 10);
            this.debug = debug;
            this.cancellationToken = cancellationToken;
        }

        // If TEE is not found in the working directory (externals/tee), tries to download and extract it with retries.
        public async Task DownloadTeeIfAbsent()
        {
            if (Directory.Exists(GetTeePath()))
            {
                return;
            }

            for (int downloadAttempt = 1; downloadAttempt <= downloadRetryCount; downloadAttempt++)
            {
                try
                {
                    debug($"Trying to download and extract TEE. Attempt: {downloadAttempt}");
                    await DownloadAndExtractTee();
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    debug("TEE download has been cancelled.");
                    break;
                }
                catch (Exception ex) when (downloadAttempt != downloadRetryCount)
                {
                    debug($"Failed to download and extract TEE. Error: {ex.ToString()}");
                    DeleteTee(); // Clean up files before next attempt
                }
            }
        }

        // Downloads TEE archive to the TEE temp directory.
        // Once downloaded, archive is extracted to the working TEE directory (externals/tee)
        // Sets required permissions for extracted files.
        private async Task DownloadAndExtractTee()
        {
            string tempDirectory = Path.Combine(agentTempDirectory, TeeTempDir);
            IOUtil.DeleteDirectory(tempDirectory, CancellationToken.None);
            Directory.CreateDirectory(tempDirectory);

            string zipPath = Path.Combine(tempDirectory, $"{Guid.NewGuid().ToString()}.zip");
            await DownloadTee(zipPath);

            debug($"Downloaded {zipPath}");

            string extractedTeePath = Path.Combine(tempDirectory, $"{Guid.NewGuid().ToString()}");
            ZipFile.ExtractToDirectory(zipPath, extractedTeePath);

            debug($"Extracted {zipPath} to {extractedTeePath}");

            string extractedTeeDestinationPath = GetTeePath();
            IOUtil.CopyDirectory(Path.Combine(extractedTeePath, TeePluginName), extractedTeeDestinationPath, cancellationToken);

            debug($"Copied TEE to {extractedTeeDestinationPath}");

            IOUtil.DeleteDirectory(tempDirectory, CancellationToken.None);

            // We have to set these files as executable because ZipFile.ExtractToDirectory does not set file permissions
            SetPermissions(Path.Combine(extractedTeeDestinationPath, "tf"), "a+x");
            SetPermissions(Path.Combine(extractedTeeDestinationPath, "native"), "a+x", recursive: true);
        }

        // Downloads TEE zip archive from the vsts blob store.
        // Logs download progress.
        private async Task DownloadTee(string zipPath)
        {
            #pragma warning disable SYSLIB0014
            using (var client = new WebClient())
            #pragma warning restore SYSLIB0014
            using (var registration = cancellationToken.Register(client.CancelAsync))
            {
                client.DownloadProgressChanged +=
                    (_, progressEvent) => debug($"TEE download progress: {progressEvent.ProgressPercentage}%.");
                await client.DownloadFileTaskAsync(new Uri(TeeUrl), zipPath);
            }
        }

        // Sets file permissions of a file or a folder.
        // Uses the following commands:
        // For non-recursive: chmod <permissions> <path>
        // For recursive: chmod -R <permissions> <path>
        private void SetPermissions(string path, string permissions, bool recursive = false)
        {
            var chmodProcessInfo = new ProcessStartInfo("chmod")
            {
                Arguments = $"{(recursive ? "-R" : "")} {permissions} {path}",
                UseShellExecute = false,
                RedirectStandardError = true
            };
            Process chmodProcess = Process.Start(chmodProcessInfo);
            chmodProcess.WaitForExit();

            string chmodStderr = chmodProcess.StandardError.ReadToEnd();
            if (chmodStderr.Length != 0 || chmodProcess.ExitCode != 0)
            {
                throw new Exception($"Failed to set {path} permissions to {permissions} (recursive: {recursive}). Exit code: {chmodProcess.ExitCode}; stderr: {chmodStderr}");
            }
        }

        // Cleanup function that removes everything from working and temporary TEE directories
        public void DeleteTee()
        {
            string teeDirectory = GetTeePath();
            IOUtil.DeleteDirectory(teeDirectory, CancellationToken.None);

            string tempDirectory = Path.Combine(agentTempDirectory, TeeTempDir);
            IOUtil.DeleteDirectory(tempDirectory, CancellationToken.None);

            debug($"Cleaned up {teeDirectory} and {tempDirectory}");
        }

        // Returns tee location: <agent home>/externals/tee
        private string GetTeePath()
        {
            return Path.Combine(agentHomeDirectory, "externals", "tee");
        }
    }
}
