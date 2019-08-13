using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    public class TarUtils
    {
        private readonly bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public async Task<string> CreateTar(AgentTaskPluginExecutionContext context, string path, CancellationToken cancellationToken)
        {
            var archiveFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + "archive.tar");
            var processTcs = new TaskCompletionSource<int>();
            using (var cancelSource = new CancellationTokenSource())
            using (var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cancelSource.Token))
            using (var process = new Process())
            {
                process.StartInfo.FileName = "tar";
                process.StartInfo.Arguments = $"-cf {archiveFile} -C {path} .";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardInput = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
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
                    process.Kill();
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
                        await Task.WhenAll(readStdOut, readStdError, processTcs.Task);
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
                    // Delete archive file.
                    if (File.Exists(archiveFile))
                    {
                        File.Delete(archiveFile);
                    }
                    foreach (string line in output)
                    {
                        context.Error(line);
                    }
                    ExceptionDispatchInfo.Capture(e).Throw();
                }
            }
            return archiveFile;
        }

        public async Task<bool> DownloadTar(AgentTaskPluginExecutionContext context, DedupManifestArtifactClient dedupManifestClient, DedupIdentifier dedupId, string targetDirectory, CancellationToken cancellationToken)
        {
            var processTcs = new TaskCompletionSource<int>();

            using (var cancelSource = new CancellationTokenSource())
            using (var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cancelSource.Token))
            using (var process = new Process())
            {
                process.StartInfo.FileName = isWindows ? "7z" : "tar";
                process.StartInfo.Arguments = isWindows ? $"x -si -aoa -o{targetDirectory} -ttar" : $"-xf - -C {targetDirectory}";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardInput = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.EnableRaisingEvents = true;
                process.Exited += (sender, args) =>
                {
                    cancelSource.Cancel();
                    processTcs.SetResult(process.ExitCode);
                };

                context.Info($"Starting '{process.StartInfo.FileName}' with arguments '{process.StartInfo.Arguments}'...");
                process.Start();

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
                Task downloadTask = Task.Run(async () =>
                {
                    try
                    {
                        await dedupManifestClient.DownloadToStreamAsync(dedupId, process.StandardInput.BaseStream, proxyUri: null, linkedSource.Token);
                        process.StandardInput.BaseStream.Close();
                    }
                    catch (Exception e)
                    {
                        process.Kill();
                        ExceptionDispatchInfo.Capture(e).Throw();
                    }
                });

                // Our goal is to always have the process ended or killed by the time we exit the function.
                try
                {
                    using (cancellationToken.Register(() => process.Kill()))
                    {
                        // readStdOut and readStdError should only fail if the process dies
                        // processTcs.Task cannot fail as we only call SetResult on processTcs
                        // downloadTask *can* fail, but when it does, it will also kill the process
                        await Task.WhenAll(readStdOut, readStdError, processTcs.Task, downloadTask);
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
                    foreach (string line in output)
                    {
                        context.Info(line);
                    }
                    ExceptionDispatchInfo.Capture(e).Throw();
                }
            }

            return true;
        }
    }
}
