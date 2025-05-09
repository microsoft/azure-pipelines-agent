using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Agent.Worker;
using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk.Knob;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    public interface IRetryOptions
    {
        int CurrentCount { get; set; }
        int Limit { get; init; }
    }

    public record RetryOptions : IRetryOptions
    {
        public int CurrentCount { get; set; }
        public int Limit { get; init; }
    }

    public static class TfManager
    {
        public static async Task DownloadLegacyTfToolsAsync(IExecutionContext executionContext)
        {
            ArgUtil.NotNull(executionContext, nameof(executionContext));
            string externalsPath = Path.Combine(executionContext.GetVariableValueOrDefault("Agent.HomeDirectory"), Constants.Path.ExternalsDirectory);
            ArgUtil.NotNull(externalsPath, nameof(externalsPath));

            string tfLegacyExternalsPath = Path.Combine(externalsPath, "tf-legacy");
            var retryOptions = new RetryOptions() { CurrentCount = 0, Limit = 3 };

            if (!Directory.Exists(tfLegacyExternalsPath))
            {
                string tfDownloadUrl;
                if (!AgentKnobs.RollbackToDefaultTfExe.GetValue(executionContext).AsBoolean())
                {
                    tfDownloadUrl = "https://vstsagenttools.blob.core.windows.net/tools/vstsom/m153_47c0856d/vstsom.zip";
                    executionContext.Debug("Using the legacy version of tf.exe");
                }
                else
                {
                    tfDownloadUrl = "https://vstsagenttools.blob.core.windows.net/tools/vstsom/m153_47c0856d_adhoc/vstsom.zip";
                    executionContext.Debug("Using the default version of tf.exe");
                }
                string tempTfDirectory = Path.Combine(externalsPath, "tf_download_temp");

                await DownloadAsync(executionContext, tfDownloadUrl, tempTfDirectory, tfLegacyExternalsPath, retryOptions);
            }
            else
            {
                executionContext.Debug($"tf-legacy download already exists at {tfLegacyExternalsPath}.");
            }

            string vstsomLegacyExternalsPath = Path.Combine(externalsPath, "vstsom-legacy");

            if (!Directory.Exists(vstsomLegacyExternalsPath))
            {
                string vstsomDownloadUrl;
                if (!AgentKnobs.RollbackToDefaultTfExe.GetValue(executionContext).AsBoolean())
                {
                    vstsomDownloadUrl = "https://vstsagenttools.blob.core.windows.net/tools/vstsom/m122_887c6659/vstsom.zip";
                    executionContext.Debug("Using the legacy version of vstsom");
                }
                else
                {
                    vstsomDownloadUrl = "https://vstsagenttools.blob.core.windows.net/tools/vstsom/m153_47c0856d_adhoc/vstsom.zip";
                    executionContext.Debug("Using the default version of vstsom");
                }
                string tempVstsomDirectory = Path.Combine(externalsPath, "vstsom_download_temp");

                await DownloadAsync(executionContext, vstsomDownloadUrl, tempVstsomDirectory, vstsomLegacyExternalsPath, retryOptions);
            }
            else
            {
                executionContext.Debug($"vstsom-legacy download already exists at {vstsomLegacyExternalsPath}.");
            }

            string vstsHostLegacyExternalsPath = Path.Combine(externalsPath, "vstshost-legacy");

            if (!Directory.Exists(vstsHostLegacyExternalsPath))
            {
                const string vstsHostDownloadUrl = "https://vstsagenttools.blob.core.windows.net/tools/vstshost/m122_887c6659/vstshost.zip";
                string tempVstsHostDirectory = Path.Combine(externalsPath, "vstshost_download_temp");

                await DownloadAsync(executionContext, vstsHostDownloadUrl, tempVstsHostDirectory, vstsHostLegacyExternalsPath, retryOptions);
            }
            else
            {
                executionContext.Debug($"vstshost-legacy download already exists at {vstsHostLegacyExternalsPath}.");
            }
        }

        public static async Task DownloadAsync(IExecutionContext executionContext, string blobUrl, string tempDirectory, string extractPath, IRetryOptions retryOptions)
        {
            Directory.CreateDirectory(tempDirectory);
            string downloadPath = Path.ChangeExtension(Path.Combine(tempDirectory, "download"), ".completed");
            string toolName = new DirectoryInfo(extractPath).Name;

            const int timeout = 180;
            const int defaultFileStreamBufferSize = 4096;
            const int retryDelay = 10000;

            try
            {
                using CancellationTokenSource downloadCts = new(TimeSpan.FromSeconds(timeout));
                using CancellationTokenSource linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(downloadCts.Token, executionContext.CancellationToken);
                CancellationToken cancellationToken = linkedTokenSource.Token;

                using HttpClient httpClient = new();
                using Stream stream = await httpClient.GetStreamAsync(blobUrl, cancellationToken);
                using FileStream fs = new(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: defaultFileStreamBufferSize, useAsync: true);

                while (retryOptions.CurrentCount < retryOptions.Limit)
                {
                    try
                    {
                        executionContext.Debug($"Retry options: {retryOptions.ToString()}.");
                        await stream.CopyToAsync(fs, cancellationToken);
                        executionContext.Debug($"Finished downloading {toolName}.");
                        await fs.FlushAsync(cancellationToken);
                        fs.Close();
                        break;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        executionContext.Debug($"{toolName} download has been cancelled.");
                        throw;
                    }
                    catch (Exception)
                    {
                        retryOptions.CurrentCount++;

                        if (retryOptions.CurrentCount == retryOptions.Limit)
                        {
                            IOUtil.DeleteDirectory(tempDirectory, CancellationToken.None);
                            executionContext.Error($"Retry limit for {toolName} download has been exceeded.");
                            return;
                        }

                        executionContext.Debug($"Failed to download {toolName}");
                        executionContext.Debug($"Retry {toolName} download in 10 seconds.");
                        await Task.Delay(retryDelay, cancellationToken);
                    }
                }

                executionContext.Debug($"Extracting {toolName}...");
                ZipFile.ExtractToDirectory(downloadPath, extractPath);
                File.WriteAllText(downloadPath, DateTime.UtcNow.ToString());
                executionContext.Debug($"{toolName} has been extracted and cleaned up");
            }
            catch (Exception ex)
            {
                executionContext.Error(ex);
            }
            finally
            {
                IOUtil.DeleteDirectory(tempDirectory, CancellationToken.None);
                executionContext.Debug($"{toolName} download directory has been cleaned up.");
            }
        }
    }
}
