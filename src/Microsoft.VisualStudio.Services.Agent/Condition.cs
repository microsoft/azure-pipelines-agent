using System.Collections.Generic;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent
{
    public sealed partial class Condition
    {
        private readonly IHostContext _context;
        private readonly Tracing _trace;
        private readonly List<Token> _tokens = new List<Token>();
        private readonly string _raw; // Raw condition string.
        private Node _root; // Parse tree.

        public Condition(IHostContext context, string condition)
        {
            ArgUtil.NotNull(context, nameof(context));
            _context = context;
            _trace = _context.GetTrace(nameof(Condition));
            _raw = condition;
            _trace.Info($"Parsing condition: {_raw}");
            CreateTokens();
            CreateTree();
            Evaluate();
        }

        public bool Result { get; private set; }

        private void Evaluate()
        {
            _trace.Entering();
            Result = _root != null ? _root.GetValueAsBool() : true;
            _trace.Info($"Result: {Result}");
        }
    }
}