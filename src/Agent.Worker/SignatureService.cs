using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker
{
    [ServiceLocator(Default = typeof(SignatureService))]
    public interface ISignatureService : IAgentService
    {
        Task<Boolean> VerifyAsync(Definition definition, CancellationToken token);
    }

    public class SignatureService : AgentService, ISignatureService
    {
        public async Task<Boolean> VerifyAsync(Definition definition, CancellationToken token)
        {
            ArgUtil.NotNull(definition, nameof(definition));

            // This is used for the Checkout task.
            // We can consider it verified since it's embedded in the Agent code.
            if (String.IsNullOrEmpty(definition.ZipPath))
            {
                return true;
            }

            // Find NuGet
            String nugetPath = WhichUtil.Which("nuget", require: true);

            var configurationStore = HostContext.GetService<IConfigurationStore>();
            AgentSettings settings = configurationStore.GetSettings();
            SignatureVerificationSettings verificationSettings = settings.SignatureVerification;
            String taskZipPath = definition.ZipPath;
            String taskNugetPath = definition.ZipPath.Replace(".zip", ".nupkg");

            // Rename .zip to .nupkg
            File.Move(taskZipPath, taskNugetPath);

            String arguments = $"verify -Signatures \"{taskNugetPath}\" -Verbosity Detailed";

            if (verificationSettings?.Fingerprints != null && verificationSettings.Fingerprints.Count > 0)
            {
                String fingerprint = String.Join(";", verificationSettings.Fingerprints);
                arguments += $" -CertificateFingerprint {fingerprint}";
            }

            // Run nuget verify
            using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
            {
                int exitCode = await processInvoker.ExecuteAsync(workingDirectory: HostContext.GetDirectory(WellKnownDirectory.Root),
                                                                 fileName: nugetPath,
                                                                 arguments: arguments,
                                                                 environment: null,
                                                                 requireExitCodeZero: false,
                                                                 outputEncoding: null,
                                                                 killProcessOnCancel: false,
                                                                 cancellationToken: token);

                // Rename back to zip
                File.Move(taskNugetPath, taskZipPath);

                if (exitCode != 0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
