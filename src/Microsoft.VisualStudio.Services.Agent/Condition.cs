using Microsoft.VisualStudio.Services.Agent.Util;
using System;
using System.Collections.Generic;

namespace Microsoft.VisualStudio.Services.Agent
{
    public sealed partial class Condition
    {
        private readonly List<Token> _tokens = new List<Token>();
        private readonly string _raw; // Raw condition string.
        private Node _root;

        public Condition(string condition)
        {
            _raw = condition;
            CreateTokens();
            CreateTree();
        }

        public bool Evaluate()
        {
            return _root != null ? _root.GetValueAsBool() : true;
        }
    }
}