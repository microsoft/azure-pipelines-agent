// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

using Agent.Sdk;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(GitManager))]
    public interface IGitManager : IAgentService
    {
        Task DownloadAsync(IExecutionContext executionContext, string version = "2.39.1");
    }

    public class GitManager : AgentService, IGitManager
    {
        private const int timeout = 180;
        private const int defaultFileStreamBufferSize = 4096;

        public async Task DownloadAsync(IExecutionContext executionContext, string version = "2.39.1")
        {
            Trace.Entering();
            ArgUtil.NotNull(executionContext, nameof(executionContext));
            ArgUtil.NotNullOrEmpty(version, nameof(version));

            Uri gitUrl = GitStore.GetDownloadUrl(version);
            var gitFileName = gitUrl.Segments[^1];
            var externalsFolder = HostContext.GetDirectory(WellKnownDirectory.Externals);
            var gitExternalsPath = Path.Combine(externalsFolder, $"git-{version}");
            var gitPath = Path.Combine(gitExternalsPath, gitFileName);

            if (File.Exists(gitPath))
            {
                executionContext.Debug($"Git instance {gitFileName} already exists.");
                return;
            }

            var tempDirectory = Path.Combine(externalsFolder, "git_download_temp");
            Directory.CreateDirectory(tempDirectory);
            var downloadGitPath = Path.ChangeExtension(Path.Combine(tempDirectory, gitFileName), ".completed");

            if (File.Exists(downloadGitPath))
            {
                executionContext.Debug($"Git intance {version} already downloaded.");
                return;
            }

            Trace.Info($@"Git zip file will be downloaded and saved as ""{downloadGitPath}""");

            int retryDelay = 10000;
            int retryLimit = 3;
            int retryCount = 0;

            while (true)
            {
                using CancellationTokenSource downloadToken = new(TimeSpan.FromSeconds(timeout));
                using var downloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(downloadToken.Token, executionContext.CancellationToken);

                try
                {
                    using HttpClient client = new();
                    using Stream stream = await client.GetStreamAsync(gitUrl);
                    using FileStream fs = new(downloadGitPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: defaultFileStreamBufferSize, useAsync: true);

                    await stream.CopyToAsync(fs);
                    Trace.Info("Finished Git downloading.");
                    await fs.FlushAsync(downloadCancellation.Token);
                    fs.Close();
                    break;
                }
                catch (OperationCanceledException) when (executionContext.CancellationToken.IsCancellationRequested)
                {
                    Trace.Info($"Git download has been cancelled.");
                    throw;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    Trace.Info("Failed to download Git");
                    Trace.Error(ex);

                    if (retryCount > retryLimit)
                    {
                        Trace.Info($"Retry limit to download Git has been reached.");
                        break;
                    }
                    else
                    {
                        Trace.Info("Retry Git download in 10 seconds.");
                        await Task.Delay(retryDelay, executionContext.CancellationToken);
                    }
                }
            }

            ZipFile.ExtractToDirectory(downloadGitPath, gitExternalsPath);
            File.WriteAllText(downloadGitPath, DateTime.UtcNow.ToString());
            Trace.Info("Git has been extracted and cleaned up");
        }
    }

    internal class GitStore
    {
        private static readonly string baseUrl = "https://vstsagenttools.blob.core.windows.net/tools/mingit";
        private static readonly string bit = PlatformUtil.BuiltOnX86 ? "32" : "64";
        internal static Uri GetDownloadUrl(string version = "2.39.1")
        {
            return new Uri($"{baseUrl}/{version}/MinGit-{version}-{bit}-bit.zip");
        }
    }
}
