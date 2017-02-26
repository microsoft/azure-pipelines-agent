using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    public class JobPrepareValidator
    {
        public void Validate(string hostType, int id, Tracing trace)
        {
            trace.Entering();
            trace.Info($"Entering JobPrepareValidator for hostType:{hostType}, id:{id}");

            bool disableValidation = false;
            bool.TryParse(Environment.GetEnvironmentVariable("CustomAgent.DisableJobPrepareValidator"), out disableValidation);

            if (!disableValidation)
            {
                trace.Info("Feature control turned on for JobPrepareValidator.");

                // Following line is to reject anything that is not release
                if (!string.Equals("release", hostType, System.StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception($"HostType:{hostType} for id:{id} not supported by this agent!");
                }

                ExecuteValidation(id, trace);

                trace.Info($"JobPrepareValidator successfully validated release:{id}");
                trace.Leaving();
            }
            else
            {
                trace.Info("Feature control turned off for JobPrepareValidator.");
            }
        }

        private void ExecuteValidation(int id, Tracing trace)
        {
            string storageContainerUrl = null;
            string approvalCheckUrl = null;

            try
            {
                // This should look like for example https://mystorageaccount.blob.core.usgovcloudapi.net/approvals
                storageContainerUrl = Environment.GetEnvironmentVariable("CustomAgent.StorageContainerUrl");

                // This should look like for example https://mystorageaccount.blob.core.usgovcloudapi.net/approvals/1234.txt
                approvalCheckUrl = $"{storageContainerUrl}/{id}.txt";

                using (var client = new HttpClient())
                {
                    var comments = client.GetStringAsync(approvalCheckUrl).Wait(TimeSpan.FromMinutes(1));
                    trace.Info($"Approval present for release: {id}. Approval:{comments}");
                }
            }
            catch (Exception ex)
            {
                string error = $"Approval required to run release:{id} in the deployment custom agent. StorageContainerUrl:{storageContainerUrl}, ApprovalCheckUrl:{approvalCheckUrl}. Exception: {ex.ToString()}";
                trace.Error(error);

                throw new Exception(error, ex);
            }
        }
    }
}
