using Agent.Listener.Diagnostics;
using System;
using System.Collections.Generic;

namespace Microsoft.VisualStudio.Services.Agent.Listener.Diagnostics
{
    public class DiagnosticTests
    {
        public DiagnosticTests(ITerminal terminal)
        {
            m_terminal = terminal;

            m_diagnosticSuites = new List<DiagnosticSuite>
            {
                new DiagnosticSuite()
                {
                    SuiteName = "Networking",
                    DiagnosticInfo = new List<IDiagnosticInfo>
                    {
                        new MtuInfo()
                    },
                    DiagnosticTests = new List<IDiagnosticTest>
                    {
                        new DnsTest(),
                        new PingTest(),
                    }
                },
                new DiagnosticSuite()
                {
                    SuiteName = "Disk Health",
                    DiagnosticInfo = new List<IDiagnosticInfo>
                    {
                        new DiskInfo(),
                        new FolderPermissionInfo(),
                    },
                },
            };
        }

        public void Execute()
        {
            foreach (var suite in m_diagnosticSuites)
            {
                m_terminal.WriteLine($"----- Diagnostics for {suite.SuiteName} -----");
                bool result = true;
                if (suite.DiagnosticTests != null)
                {
                    foreach (var test in suite.DiagnosticTests)
                    {
                        string testName = test.GetType().Name;
                        m_terminal.WriteLine(string.Format("*** {0} ***", testName));
                        try
                        {
                            if (!test.Execute(m_terminal))
                            {
                                result = false;
                                m_terminal.WriteError(string.Format("*** {0} Failed ***", testName));
                            }
                            else
                            {
                                m_terminal.WriteLine(string.Format("*** {0} Succeeded ***", testName));
                            }
                        }
                        catch (Exception ex)
                        {
                            result = false;
                            m_terminal.WriteError(ex);
                            m_terminal.WriteError(string.Format("***  {0} Failed ***", testName));
                        }
                        m_terminal.WriteLine(string.Empty);
                    }
                }

                foreach (var info in suite.DiagnosticInfo)
                {
                    string infoName = info.GetType().Name;
                    m_terminal.WriteLine(string.Format("*** {0} ***", infoName));
                    try
                    {
                        info.Execute(m_terminal);
                        m_terminal.WriteLine(string.Format("*** {0} Completed ***", infoName));
                    }
                    catch (Exception ex)
                    {
                        m_terminal.WriteError(ex);
                        m_terminal.WriteError(string.Format("*** {0} Failed ***", infoName));
                    }
                    m_terminal.WriteLine(string.Empty);
                }

                if (suite.DiagnosticTests != null)
                {
                    if (result)
                    {
                        m_terminal.WriteLine($"{suite.SuiteName} Diagnostics tests were successful!");
                    }
                    else
                    {
                        m_terminal.WriteLine($"{suite.SuiteName} 1 or more diagnostics tests FAILED!");
                    }
                }

                m_terminal.WriteLine(string.Empty);
                m_terminal.WriteLine(string.Empty);
            }
        }

        private List<DiagnosticSuite> m_diagnosticSuites;
        private ITerminal m_terminal;
    }
}
