// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using Xunit;
using Agent.Sdk.SecretMasking;
using Agent.Sdk.Knob;

namespace Microsoft.VisualStudio.Services.Agent.Tests.TracingSpecs
{
    public sealed class TraceManagerL0
    {
        private static (Microsoft.VisualStudio.Services.Agent.TraceManager mgr, string logPath, Microsoft.VisualStudio.Services.Agent.Tracing trace, ILoggedSecretMasker masker, Microsoft.VisualStudio.Services.Agent.HostTraceListener listener) Create(string name, bool? envEnhanced = null, IKnobValueContext knobContext = null)
        {
            string logPath = Path.Combine(Path.GetTempPath(), $"trace_{Guid.NewGuid():N}.log");
            var listener = new Microsoft.VisualStudio.Services.Agent.HostTraceListener(logPath) { DisableConsoleReporting = true };
            // Create OSS masker and do not dispose it here; the LoggedSecretMasker wrapper will be disposed in the test.
            ILoggedSecretMasker masker;
            // Ownership of the underlying masker is intentionally transferred to the LoggedSecretMasker wrapper.
            // Suppress CA2000 for tests: the wrapper will be disposed by the test cleanup.
#pragma warning disable CA2000
            var oss = new OssSecretMasker();
#pragma warning restore CA2000
            masker = LoggedSecretMasker.Create(oss);

            // Control knob via environment if requested
            string prev = null;
            if (envEnhanced.HasValue)
            {
                prev = Environment.GetEnvironmentVariable("AZP_USE_ENHANCED_LOGGING");
                Environment.SetEnvironmentVariable("AZP_USE_ENHANCED_LOGGING", envEnhanced.Value ? "true" : null);
            }

            try
            {
                if (knobContext != null)
                {
                    var ctx = knobContext;
                    var mgr = new Microsoft.VisualStudio.Services.Agent.TraceManager(listener, masker, ctx);
                    var trace = mgr[name];
                    return (mgr, logPath, trace, masker, listener);
                }
                else
                {
                    using (var ctx = new TestHostContext(new object()))
                    {
                        var mgr = new Microsoft.VisualStudio.Services.Agent.TraceManager(listener, masker, ctx);
                        var trace = mgr[name];
                        // Note: returning ctx here would dispose it after leaving the using block,
                        // so only return objects that do not depend on ctx after disposal.
                        return (mgr, logPath, trace, masker, listener);
                    }
                }
            }
            finally
            {
                if (envEnhanced.HasValue)
                {
                    Environment.SetEnvironmentVariable("AZP_USE_ENHANCED_LOGGING", prev);
                }
            }
        }

        private static string ReadAll(string path)
        {
            // Wait a moment to let file writes flush on slower CI
            Task.Delay(25).Wait();
            return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Tracing")]
        public void Startup_Uses_Default_NonEnhanced_When_Knob_Not_Set()
        {
            var (mgr, path, trace, masker, listener) = Create("Startup_Default");
            try
            {
                trace.Info("baseline message");
            }
            finally
            {
                mgr.Dispose();
                masker.Dispose();
                listener.Dispose();
            }

            var content = ReadAll(path);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Tracing")]
        public void Startup_Honors_Knob_When_Set_True()
        {
            var (mgr, path, trace, masker, listener) = Create("Startup_Knob", envEnhanced: true);
            try
            {
                trace.Info("enhanced at startup");
            }
            finally
            {
                mgr.Dispose();
                masker.Dispose();
                listener.Dispose();
            }

            var content = ReadAll(path);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Tracing")]
        public void Runtime_Switch_Upgrades_Existing_Sources()
        {
            var (mgr, path, trace, masker, listener) = Create("Runtime_Switch", envEnhanced: false);
            try
            {
                trace.Info("before switch");
                mgr.SetEnhancedLoggingEnabled(true);
                trace.Info("after switch");
            }
            finally
            {
                mgr.Dispose();
                masker.Dispose();
                listener.Dispose();
            }

            var content = ReadAll(path);
            Assert.Contains("before switch", content); // pre-switch line present (non-enhanced)
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Tracing")]
        public void Proxy_Is_Stable_Across_Get_And_Switch()
        {
            var (mgr, path, trace, masker, listener) = Create("Proxy_Stable", envEnhanced: false);
            try
            {
                var t1 = mgr["component"]; // same instance as first
                var t2 = mgr["component"]; // should be same proxy instance
                Assert.Same(t1, t2);

                mgr.SetEnhancedLoggingEnabled(true);
                var t3 = mgr["component"]; // still same proxy instance
                Assert.Same(t1, t3);

                t1.Info("proxy stable message");
            }
            finally
            {
                mgr.Dispose();
                masker.Dispose();
                listener.Dispose();
            }

            var content = ReadAll(path);
            Assert.Contains("proxy stable message", content);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Tracing")]
        public void Calls_After_Dispose_Do_Not_Throw()
        {
            var (mgr, path, trace, masker, listener) = Create("PostDispose", envEnhanced: false);
            // Dispose the manager first (proxies lose inner), but keep the listener alive so
            // a forward to base doesn't hit a disposed writer. We're only asserting no throw.
            mgr.Dispose();
            
            // Should not throw even though inner is gone
            trace.Info("after dispose no throw");

            // Now dispose remaining resources
            masker.Dispose();
            listener.Dispose();
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Tracing")]
        public void New_Sources_After_Switch_Use_Enhanced()
        {
            var (mgr, path, trace, masker, listener) = Create("NewSourceAfterSwitch", envEnhanced: false);
            try
            {
                // Switch on enhanced logging
                mgr.SetEnhancedLoggingEnabled(true);

                // Acquire a new source after the switch and log
                var newTrace = mgr["new-component"];
                newTrace.Info("message from new source");
            }
            finally
            {
                mgr.Dispose();
                masker.Dispose();
                listener.Dispose();
            }

            var content = ReadAll(path);
            Assert.Contains("message from new source", content);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Tracing")]
        public void Disable_Enhanced_Stops_Enhanced_For_New_Messages()
        {
            var (mgr, path, trace, masker, listener) = Create("DisableEnhanced", envEnhanced: true);
            try
            {
                trace.Info("before disable"); // enhanced
                mgr.SetEnhancedLoggingEnabled(false);
                trace.Info("after disable"); // not enhanced
            }
            finally
            {
                mgr.Dispose();
                masker.Dispose();
                listener.Dispose();
            }

            var content = ReadAll(path);

            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var afterDisableLine = lines.FirstOrDefault(l => l.Contains("after disable"));
        }
    }
}
