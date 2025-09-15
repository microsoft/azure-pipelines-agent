using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk;
using Agent.Sdk.Knob;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    public static class VsoTaskLibManager
    {
        /// <summary>
        /// Downloads and installs vso-task-lib at runtime if not already present
        /// </summary>
        /// <param name="executionContext">The execution context</param>
        /// <returns>Task representing the async operation</returns>
        public static async Task DownloadVsoTaskLibAsync(IExecutionContext executionContext)
        {
            ArgUtil.NotNull(executionContext, nameof(executionContext));
            string externalsPath = Path.Combine(executionContext.GetVariableValueOrDefault("Agent.HomeDirectory"), Constants.Path.ExternalsDirectory);
            ArgUtil.NotNull(externalsPath, nameof(externalsPath));

            string vsoTaskLibExternalsPath = Path.Combine(externalsPath, "vso-task-lib");
            var retryOptions = new RetryOptions() { CurrentCount = 0, Limit = 3 };

            if (!Directory.Exists(vsoTaskLibExternalsPath))
            {
                const string vsoTaskLibDownloadUrl = "https://vstsagenttools.blob.core.windows.net/tools/vso-task-lib/0.5.5/vso-task-lib.tar.gz";
                string tempVsoTaskLibDirectory = Path.Combine(externalsPath, "vso-task-lib_download_temp");

                await DownloadAsync(executionContext, vsoTaskLibDownloadUrl, tempVsoTaskLibDirectory, vsoTaskLibExternalsPath, retryOptions);
            }
            else
            {
                executionContext.Debug($"vso-task-lib download already exists at {vsoTaskLibExternalsPath}.");
            }
        }

        public static async Task DownloadAsync(IExecutionContext executionContext, string blobUrl, string tempDirectory, string extractPath, IRetryOptions retryOptions)
        {
            Directory.CreateDirectory(tempDirectory);
            Directory.CreateDirectory(extractPath);
            string downloadPath = Path.ChangeExtension(Path.Combine(tempDirectory, "download"), ".tar.gz");
            string toolName = new DirectoryInfo(extractPath).Name;

            const int timeout = 180;
            const int bufferSize = 4096;
            const int retryDelay = 10000;

            using var downloadCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
            using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(downloadCts.Token, executionContext.CancellationToken);
            var cancellationToken = linkedTokenSource.Token;

            using var handler = executionContext.GetHostContext().CreateHttpClientHandler();
            using var httpClient = new HttpClient(handler);

            for (; retryOptions.CurrentCount < retryOptions.Limit; retryOptions.CurrentCount++)
            {
                try
                {
                    executionContext.Debug($"Downloading {toolName} (attempt {retryOptions.CurrentCount + 1}/{retryOptions.Limit}).");
                    using var stream = await httpClient.GetStreamAsync(blobUrl, cancellationToken);
                    using var fs = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true);
                    await stream.CopyToAsync(fs, cancellationToken);
                    executionContext.Debug($"Finished downloading {toolName}.");
                    await fs.FlushAsync(cancellationToken);
                    ExtractTarGz(downloadPath, extractPath, executionContext, toolName);
                    executionContext.Debug($"{toolName} has been extracted and cleaned up");
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    executionContext.Debug($"{toolName} download has been cancelled.");
                    throw;
                }
                catch (Exception ex)
                {
                    if (retryOptions.CurrentCount + 1 == retryOptions.Limit)
                    {
                        IOUtil.DeleteDirectory(tempDirectory, CancellationToken.None);
                        executionContext.Error($"Retry limit for {toolName} download has been exceeded.");
                        executionContext.Error(ex);
                        return;
                    }
                    executionContext.Debug($"Failed to download {toolName}: {ex.Message}");
                    executionContext.Debug($"Retry {toolName} download in 10 seconds.");
                    await Task.Delay(retryDelay, cancellationToken);
                }
            }
            IOUtil.DeleteDirectory(tempDirectory, CancellationToken.None);
            executionContext.Debug($"{toolName} download directory has been cleaned up.");
        }

        /// <summary>
        /// Extracts a .tar.gz file to the specified directory using the tar command.
        /// </summary>
        private static void ExtractTarGz(string tarGzPath, string extractPath, IExecutionContext executionContext, string toolName)
        {
            Directory.CreateDirectory(extractPath);
            executionContext.Debug($"Extracting {toolName} using tar...");
            using (var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "tar",
                    Arguments = $"-xzf \"{tarGzPath}\" -C \"{extractPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            })
            {
                process.Start();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    var error = process.StandardError.ReadToEnd();
                    executionContext.Error($"tar extraction failed: {error}");
                    throw new Exception($"tar extraction failed: {error}");
                }
            }
        }
    }
}
