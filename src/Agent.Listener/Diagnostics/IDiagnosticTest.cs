namespace Microsoft.VisualStudio.Services.Agent.Listener.Diagnostics
{
    public interface IDiagnosticTest
    {
        bool Execute(ITerminal terminal);
    }
}
