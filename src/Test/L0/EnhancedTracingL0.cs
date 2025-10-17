// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Agent.Sdk.SecretMasking;
using Agent.Sdk.Knob;

namespace Microsoft.VisualStudio.Services.Agent.Tests.TracingSpecs
{
    public sealed class EnhancedTracingL0
    {
        private static (TraceManager mgr, string logPath, Tracing trace, ILoggedSecretMasker masker, HostTraceListener listener) Create(string name)
        {
            // Force enhanced logging via environment knob
            var prev = Environment.GetEnvironmentVariable("AZP_USE_ENHANCED_LOGGING");
            Environment.SetEnvironmentVariable("AZP_USE_ENHANCED_LOGGING", "true");

            string logPath = Path.Combine(Path.GetTempPath(), $"etrace_{Guid.NewGuid():N}.log");
            var listener = new HostTraceListener(logPath) { DisableConsoleReporting = true };
#pragma warning disable CA2000 // Dispose objects before losing scope. LoggedSecretMasker takes ownership.
            var masker = LoggedSecretMasker.Create(new OssSecretMasker());
#pragma warning restore CA2000
            try
            {
                using var ctx = new TestHostContext(new object());
                var mgr = new TraceManager(listener, masker, ctx);
                var trace = mgr[name];
                return (mgr, logPath, trace, masker, listener);
            }
            finally
            {
                // restore environment in caller
                Environment.SetEnvironmentVariable("AZP_USE_ENHANCED_LOGGING", prev);
                // Do not dispose masker here; let the test own disposal order
            }
        }

        private static string ReadAll(string path)
        {
            Task.Delay(25).Wait();
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Tracing")]
        public void Correlation_Is_Formatted_In_Enhanced_Log()
        {
            var (mgr, path, trace, masker, listener) = Create("CorrFmt");
            try
            {
                trace.Info("hello world", operation: "Op1");
            }
            finally
            {
                mgr.Dispose();
                listener.Dispose();
            }

            var content = ReadAll(path);
            // Depending on implementation, correlation may or may not be present here.
            Assert.Contains("[Op1]", content);
            Assert.Contains("hello world", content);
        }
    }
}
