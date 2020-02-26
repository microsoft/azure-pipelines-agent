namespace Microsoft.VisualStudio.Services.Agent.Listener.Diagnostics
{
    public interface IDiagnosticInfo
    {
        void Execute(ITerminal terminal);
    }
}
