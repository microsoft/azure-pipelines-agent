// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.VisualStudio.Services.Agent
{

    [AttributeUsage(AttributeTargets.Interface, Inherited = false, AllowMultiple = false)]
    public sealed class ServiceLocatorAttribute : Attribute
    {
        public Type Default { get; set; }
        public Type PreferredOnWindows { get; set; }
        public Type PreferredOnMacOS { get; set; }
        public Type PreferredOnLinux { get; set; }
    }

    public interface IAgentService
    {
        void Initialize(IHostContext context);
    }

    public abstract class AgentService
    {
        protected IHostContext HostContext { get; private set; }
        protected Tracing Trace { get; private set; }

        public string TraceName 
        {
            get 
            {
                return GetType().Name;
            }
        }

        public virtual void Initialize(IHostContext hostContext)
        {
            HostContext = hostContext;
            Trace = HostContext.GetTrace(TraceName);
            Trace.Entering();
        }
    }
}