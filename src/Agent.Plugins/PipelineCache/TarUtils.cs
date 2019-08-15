using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Agent.Sdk;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;

namespace Agent.Plugins.PipelineCache
{
    public static class TarUtils
    {
        private readonly static bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private const string archiveFileName = "archive.tar";

        /// <summary>
        /// Will archive files in the input path into a TAR file.
        /// </summary>
        /// <returns>The path to the TAR.</returns>
        public static async Task<string> ArchiveFilesToTarAsync(
            AgentTaskPluginExecutionContext context,
            string inputPath,
            CancellationToken cancellationToken)
        {
            var archiveFile = Path.Combine(Path.GetTempPath(), archiveFileName);
            if (File.Exists(archiveFile))
            {
                File.Delete(archiveFile);
            }
            var processFileName = "tar";
            var processArguments = $"-cf {archiveFile} -C {inputPath} .";

            Action actionOnFailure = () =>
            {
                // Delete archive file.
                if (File.Exists(archiveFile))
                {
                    File.Delete(archiveFile);
                }
            };

            await RunProcessAsync(
                context,
                processFileName,
                processArguments,
                // no additional tasks on create are required to run whilst running the TAR process
                (Process process, CancellationToken ct) => Task.CompletedTask,
                actionOnFailure,
                cancellationToken);
            return archiveFile;
        }

        /// <summary>
        /// This will download the dedup into stdin stream while extracting the TAR simulataneously (piped). This is done by
        /// starting the download through a Task and starting the TAR/7z process which is reading from STDIN.
        /// </summary>
        /// <remarks>
        /// Windows will use 7z to extract the TAR file (only if 7z is installed on the machine and is part of PATH variables). 
        /// Non-Windows machines will extract TAR file using the 'tar' command'.
        /// </remarks>
        public static Task DownloadAndExtractTarAsync(
            AgentTaskPluginExecutionContext context,
            Manifest manifest,
            DedupManifestArtifactClient dedupManifestClient,
            string targetDirectory,
            CancellationToken cancellationToken)
        {
            ValidateTarManifest(manifest);
            DedupIdentifier dedupId = DedupIdentifier.Create(manifest.Items.Single(i => i.Path == $"/{archiveFileName}").Blob.Id);
            bool does7zExists = isWindows ? CheckIf7ZExists() : false;
            string processFileName = (isWindows && does7zExists) ? "7z" : "tar";
            string processArguments = (isWindows && does7zExists) ? $"x -si -aoa -o{targetDirectory} -ttar" : $"-xf - -C {targetDirectory}";

            Func<Process, CancellationToken, Task> downloadTaskFunc =
                (process, ct) =>
                Task.Run(async () => {
                    try
                    {
                        await dedupManifestClient.DownloadToStreamAsync(dedupId, process.StandardInput.BaseStream, proxyUri: null, cancellationToken: ct);
                        process.StandardInput.BaseStream.Close();
                    }
                    catch (Exception e)
                    {
                        process.Kill();
                        ExceptionDispatchInfo.Capture(e).Throw();
                    }
                });

            return RunProcessAsync(
                context,
                processFileName,
                processArguments,
                downloadTaskFunc,
                () => { },
                cancellationToken);
        }

        private static async Task RunProcessAsync(
            AgentTaskPluginExecutionContext context,
            string processFileName,
            string processArguments,
            Func<Process, CancellationToken, Task> additionalTaskToExecuteWhilstRunningProcess,
            Action actionOnFailure,
            CancellationToken cancellationToken)
        {
            var processTcs = new TaskCompletionSource<int>();
            using (var cancelSource = new CancellationTokenSource())
            using (var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cancelSource.Token))
            using (var process = new Process())
            {
                SetProcessStartInfo(process, processFileName, processArguments);
                process.EnableRaisingEvents = true;
                process.Exited += (sender, args) =>
                {
                    cancelSource.Cancel();
                    processTcs.SetResult(process.ExitCode);
                };

                try
                {
                    context.Debug($"Starting '{process.StartInfo.FileName}' with arguments '{process.StartInfo.Arguments}'...");
                    process.Start();
                }
                catch (Exception e)
                {
                    ExceptionDispatchInfo.Capture(e).Throw();
                }

                var output = new List<string>();
                Task readLines(string prefix, StreamReader reader) => Task.Run(async () =>
                {
                    string line;
                    while (null != (line = await reader.ReadLineAsync()))
                    {
                        lock (output)
                        {
                            output.Add($"{prefix}{line}");
                        }
                    }
                });
                Task readStdOut = readLines("stdout: ", process.StandardOutput);
                Task readStdError = readLines("stderr: ", process.StandardError);

                // Our goal is to always have the process ended or killed by the time we exit the function.
                try
                {
                    using (cancellationToken.Register(() => process.Kill()))
                    {
                        // readStdOut and readStdError should only fail if the process dies
                        // processTcs.Task cannot fail as we only call SetResult on processTcs
                        IEnumerable<Task> tasks = new List<Task>
                        {
                            readStdOut,
                            readStdError,
                            processTcs.Task,
                            additionalTaskToExecuteWhilstRunningProcess(process, linkedSource.Token)
                        };
                        await Task.WhenAll(tasks);
                    }

                    int exitCode = await processTcs.Task;

                    if (exitCode == 0)
                    {
                        context.Output($"Process exit code: {exitCode}");
                        foreach (string line in output)
                        {
                            context.Output(line);
                        }
                    }
                    else
                    {
                        throw new Exception($"Process returned non-zero exit code: {exitCode}");
                    }
                }
                catch (Exception e)
                {
                    actionOnFailure();
                    foreach (string line in output)
                    {
                        context.Error(line);
                    }
                    ExceptionDispatchInfo.Capture(e).Throw();
                }
            }
        }

        private static void SetProcessStartInfo(Process process, string processFileName, string processArguments)
        {
            process.StartInfo.FileName = processFileName;
            process.StartInfo.Arguments = processArguments;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
        }

        private static void ValidateTarManifest(Manifest manifest)
        {
            if (manifest == null || manifest.Items.Count() != 1 || !manifest.Items.Any(i => i.Path == $"/{archiveFileName}"))
            {
                throw new ArgumentException($"Manifest containing a tar cannot have more than one item.");
            }
        }

        private static bool CheckIf7ZExists()
        {
            using (var process = new Process())
            {
                process.StartInfo.FileName = "7z";
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.RedirectStandardOutput = true;
                try
                {
                    process.Start();
                }
                catch
                {
                    return false;
                }
                return true;
            }
        }
    }
}
