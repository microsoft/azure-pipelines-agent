using System;
using Agent.Sdk;
using Agent.Sdk.Knob;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    public class UtilKnobValueContext : IKnobValueContext
    {
        private static UtilKnobValueContext _instance;

        protected UtilKnobValueContext()
        {
        }

        public static UtilKnobValueContext Instance()
        {
            if (_instance == null)
            {
                _instance = new UtilKnobValueContext();
            }

            return _instance;
        }

        public string GetVariableValueOrDefault(string variableName)
        {
            throw new NotSupportedException("Method not supported for Microsoft.VisualStudio.Services.Agent.Util.UtilKnobValueContext");
        }

        public IScopedEnvironment GetScopedEnvironment()
        {
            return new SystemEnvironment();
        }
    }
}