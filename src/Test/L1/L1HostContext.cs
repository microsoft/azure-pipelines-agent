// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Reflection;

namespace Microsoft.VisualStudio.Services.Agent.Tests.L1.Worker
{
    public class L1HostContext : HostContext
    {
        public L1HostContext(HostType hostType, string logFile = null)
            : base(hostType, logFile)
        {
        }

        public T SetupService<T>(Type target) where T : class, IAgentService
        {
            if (!typeof(T).IsAssignableFrom(target))
            {
                throw new ArgumentException("The target type must implement the specified interface");
            }
            ServiceTypes.TryAdd(typeof(T), target);
            return GetService<T>();
        }

        public override string GetDirectory(WellKnownDirectory directory)
        {
            if (directory == WellKnownDirectory.Bin)
            {
                return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            }
            return base.GetDirectory(directory);
        }
    }
}