// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    public class DeploymentTypeNotDeterminedException : Exception
    {
        public DeploymentTypeNotDeterminedException() {}

        public DeploymentTypeNotDeterminedException(string message) : base(message) {}

        public DeploymentTypeNotDeterminedException(string message, Exception inner) : base(message, inner) {}
    }

    public class DeploymentTypeNotRecognizedException : Exception
    {
        public DeploymentTypeNotRecognizedException() {}

        public DeploymentTypeNotRecognizedException(string message) : base(message) {}

        public DeploymentTypeNotRecognizedException(string message, Exception inner) : base(message, inner) {}
    }

    public class ServerUtil
    {
        private DeploymentFlags _deploymentType;
        private Tracing _trace;

        public ServerUtil(Tracing trace = null)
        {
            _trace = trace;
        }

        /// <summary>
        /// Returns true if server deployment type is Hosted.
        /// An exception will be thrown if the type was not determined before.
        /// </summary>
        public bool IsDeploymentTypeHostedIfDetermined()
        {
            switch (_deploymentType)
            {
                case DeploymentFlags.Hosted:
                    return true;
                case DeploymentFlags.OnPremises:
                    return false;
                case DeploymentFlags.None:
                    throw new DeploymentTypeNotDeterminedException($"Deployment type has not been determined.");
                default:
                    throw new DeploymentTypeNotRecognizedException($"Unable to recognize deployment type: '{_deploymentType}'");
            }
        }

        /// <summary>
        /// Returns true if server deployment type was determined; otherwise, returns false and makes IsHosted equals to false.
        /// Makes IsHosted equals to true if server deployment type was determined as Hosted; otherwise, makes IsHosted equals to false.
        /// </summary>
        public bool TryGetDeploymentType(out bool IsHosted)
        {
            try
            {
                IsHosted = IsDeploymentTypeHostedIfDetermined();
                return true;
            }
            catch (DeploymentTypeNotDeterminedException)
            {
                IsHosted = false;
                return false;
            }
        }

        /// <summary>
        /// Determine server deployment type based on connection data (Hosted/OnPremises) if it has not been determined yet.
        /// </summary>
        public async Task DetermineDeploymentType(string serverUrl, VssCredentials credentials, ILocationServer locationServer)
        {
            // Check if deployment type has not been determined yet
            if (_deploymentType == DeploymentFlags.None)
            {
                // Determine the service deployment type based on connection data. (Hosted/OnPremises)
                var connectionData = await GetConnectionData(serverUrl, credentials, locationServer);
                _deploymentType = connectionData.DeploymentType;
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA2000:Dispose objects before losing scope", MessageId = "locationServer")]
        private async Task<Location.ConnectionData> GetConnectionData(string serverUrl, VssCredentials credentials, ILocationServer locationServer)
        {
            VssConnection connection = VssUtil.CreateConnection(new Uri(serverUrl), credentials, trace: _trace);
            await locationServer.ConnectAsync(connection);
            return await locationServer.GetConnectionDataAsync();
        }
    }
}
