using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.IO;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(CacheDirectoryManager))]
    public interface ICacheDirectoryManager : IAgentService
    {
        void InitializeTempDirectory(IExecutionContext jobContext);
        void CleanupTempDirectory(IExecutionContext jobContext);
    }

    public sealed class CacheDirectoryManager : AgentService
    {
        public void InitializeTempDirectory(IExecutionContext jobContext)
        {
            ArgUtil.NotNull(jobContext, nameof(jobContext));

            // TEMP and TMP on Windows
            // TMPDIR on Linux
            bool skipOverwriteTemp = false;
            if (bool.TryParse(Environment.GetEnvironmentVariable("VSTS_NOTOVERWRITE_TEMP") ?? string.Empty, out skipOverwriteTemp) && skipOverwriteTemp)
            {
                jobContext.Debug($"Skipping overwrite %TEMP% environment variable");
            }
            else
            {
                string tempDirectory = Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Work), Constants.Path.TempDirectory);
                jobContext.Debug($"Cleaning temp folder: {tempDirectory}");
                try
                {
                    IOUtil.DeleteDirectory(tempDirectory, contentsOnly: true, cancellationToken: jobContext.CancellationToken);
                }
                catch (Exception ex)
                {
                    Trace.Error("Failed cleaning one or more temp file");
                    Trace.Error(ex);
                }
                finally
                {
                    // make sure folder exists
                    Directory.CreateDirectory(tempDirectory);
                }

#if OS_WINDOWS
                jobContext.Debug($"SET TMP={tempDirectory}");
                jobContext.Debug($"SET TEMP={tempDirectory}");                
                Environment.SetEnvironmentVariable("TMP", tempDirectory);
                Environment.SetEnvironmentVariable("TEMP", tempDirectory);
#else
                jobContext.Debug($"SET TMPDIR={tempDirectory}");
                Environment.SetEnvironmentVariable("TMPDIR", tempDirectory);
#endif
            }
        }

        public void CleanupTempDirectory(IExecutionContext jobContext)
        {
            ArgUtil.NotNull(jobContext, nameof(jobContext));

            string tempDirectory = Path.Combine(HostContext.GetDirectory(WellKnownDirectory.Work), Constants.Path.TempDirectory);
            bool skipOverwriteTemp = false;
            if (bool.TryParse(Environment.GetEnvironmentVariable("VSTS_NOTOVERWRITE_TEMP") ?? string.Empty, out skipOverwriteTemp) && skipOverwriteTemp)
            {
                jobContext.Debug($"Skipping cleanup temp folder: {tempDirectory}");
            }
            else
            {
                jobContext.Debug($"Cleaning temp folder: {tempDirectory}");
                try
                {
                    IOUtil.DeleteDirectory(tempDirectory, contentsOnly: true, cancellationToken: jobContext.CancellationToken);
                }
                catch (Exception ex)
                {
                    Trace.Error("Failed cleaning one or more temp file");
                    Trace.Error(ex);
                }
            }
        }
    }
}
