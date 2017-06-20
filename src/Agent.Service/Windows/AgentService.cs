using System.ServiceProcess;
using System.Threading.Tasks;

namespace AgentService
{
    public partial class AgentService : ServiceBase
    {   
        private Task RunningLoop { get; set; }

        private AgentListener _agentListener;

        public AgentService(string serviceName)
        {            
            InitializeComponent();
            base.ServiceName = serviceName;

            _agentListener = new AgentListener(ExecutionMode.Service);
        }

        protected override void OnStart(string[] args)
        {
            RunningLoop = Task.Run(
                () =>
                    {
                        EventLogger.WriteInfo("Starting VSTS Agent Service");
                        ExitCode = _agentListener.Run();
                    });
        }        

        protected override void OnStop()
        {
            if (_agentListener != null)
            { 
                _agentListener.Stop();
                return;
            }
            //otherwise log?
        }
        

        // private void WriteError(int exitCode)
        // {
        //     String diagFolder = GetDiagnosticFolderPath();
        //     String eventText = String.Format(
        //         CultureInfo.InvariantCulture,
        //         "The AgentListener process failed to start successfully. It exited with code {0}. Check the latest Agent log files in {1} for more information.",
        //         exitCode,
        //         diagFolder);

        //     EventLogger.WriteToEventLog(eventText, EventLogEntryType.Error);
        // }        
    }
}
