// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Agent.Listener.Configuration;
using Agent.Sdk.Knob;
using Microsoft.VisualStudio.Services.Agent.Util;
using Microsoft.VisualStudio.Services.Common;
using System;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.VisualStudio.Services.Agent.Listener.Configuration
{
    /// <summary>
    /// Manages an RSA key for the agent using the most appropriate store for the target platform.
    /// </summary>
    [ServiceLocator(
        PreferredOnWindows = typeof(RSAEncryptedFileKeyManager),
        Default = typeof(RSAFileKeyManager)
    )]
    public interface IRSAKeyManager : IAgentService
    {
        /// <summary>
        /// Creates a new <c>RSA</c> instance for the current agent. If a key file is found then the current
        /// key is returned to the caller.
        /// </summary>
        /// <returns>An <c>RSA</c> instance representing the key for the agent</returns>
        RSA CreateKey(bool enableAgentKeyStoreInNamedContainer, bool useCng);

        /// <summary>
        /// Deletes the RSA key managed by the key manager.
        /// </summary>
        void DeleteKey();

        /// <summary>
        /// Gets the <c>RSA</c> instance currently stored by the key manager. 
        /// </summary>
        /// <returns>An <c>RSA</c> implementation representing the key for the agent</returns>
        /// <exception cref="CryptographicException">No key exists in the store</exception>
        RSA GetKey();
    }

    public static class IRSAKeyManagerExtensions
    {
        public static async Task<(bool useNamedContainer, bool useCng)> GetStoreAgentTokenInNamedContainerFF(this IRSAKeyManager _, IHostContext hostContext, global::Agent.Sdk.ITraceWriter trace, AgentSettings agentSettings, VssCredentials creds, CancellationToken cancellationToken = default)
        {
            var useNamedContainer = AgentKnobs.StoreAgentKeyInCSPContainer.GetValue(UtilKnobValueContext.Instance()).AsBoolean();
            var useCng = AgentKnobs.AgentKeyUseCng.GetValue(UtilKnobValueContext.Instance()).AsBoolean();

            if (useNamedContainer || useCng)
            {
                return (useNamedContainer, useCng);
            }

            var featureFlagProvider = hostContext.GetService<IFeatureFlagProvider>();
            var enableAgentKeyStoreInNamedContainerFF = (await featureFlagProvider.GetFeatureFlagWithCred(hostContext, "DistributedTask.Agent.StoreAgentTokenInNamedContainer", trace, agentSettings, creds, cancellationToken)).EffectiveState == "On";
            var useCngFF = (await featureFlagProvider.GetFeatureFlagWithCred(hostContext, "DistributedTask.Agent.UseCng", trace, agentSettings, creds, cancellationToken)).EffectiveState == "On";

            return (enableAgentKeyStoreInNamedContainerFF, useCngFF);
        }

        public static (bool useNamedContainer, bool useCng) GetStoreAgentTokenConfig(this IRSAKeyManager _)
        {
            var useNamedContainer = AgentKnobs.StoreAgentKeyInCSPContainer.GetValue(UtilKnobValueContext.Instance()).AsBoolean();
            var useCng = AgentKnobs.AgentKeyUseCng.GetValue(UtilKnobValueContext.Instance()).AsBoolean();

            return (useNamedContainer, useCng);
        }
    }

    // Newtonsoft 10 is not working properly with dotnet RSAParameters class
    // RSAParameters has fields marked as [NonSerialized] which cause we loss those fields after serialize to JSON
    // https://github.com/JamesNK/Newtonsoft.Json/issues/1517
    // https://github.com/dotnet/corefx/issues/23847
    // As workaround, we create our own RSAParameters class without any [NonSerialized] attributes.
    [Serializable]
    internal class RSAParametersSerializable : ISerializable
    {
        private const string containerNameMemberName = "ContainerName";
        private const string useCngMemberName = "UseCng";
        private bool _useCng;
        private string _containerName;
        private RSAParameters _rsaParameters;

        public RSAParameters RSAParameters
        {
            get
            {
                return _rsaParameters;
            }
        }

        public RSAParametersSerializable(string containerName, bool useCng, RSAParameters rsaParameters)
        {
            _containerName = containerName;
            _useCng = useCng;
            _rsaParameters = rsaParameters;
        }

        private RSAParametersSerializable()
        {
        }

        public string ContainerName { get { return _containerName; } set { _containerName = value; } }

        public bool UseCng { get { return _useCng; } set { _useCng = value; } }

        public byte[] D { get { return _rsaParameters.D; } set { _rsaParameters.D = value; } }

        public byte[] DP { get { return _rsaParameters.DP; } set { _rsaParameters.DP = value; } }

        public byte[] DQ { get { return _rsaParameters.DQ; } set { _rsaParameters.DQ = value; } }

        public byte[] Exponent { get { return _rsaParameters.Exponent; } set { _rsaParameters.Exponent = value; } }

        public byte[] InverseQ { get { return _rsaParameters.InverseQ; } set { _rsaParameters.InverseQ = value; } }

        public byte[] Modulus { get { return _rsaParameters.Modulus; } set { _rsaParameters.Modulus = value; } }

        public byte[] P { get { return _rsaParameters.P; } set { _rsaParameters.P = value; } }

        public byte[] Q { get { return _rsaParameters.Q; } set { _rsaParameters.Q = value; } }

        public RSAParametersSerializable(SerializationInfo information, StreamingContext context)
        {
            bool hasContainerNameMember = false;
            bool hasUseCngMember = false;
            var e = information.GetEnumerator();
            while (e.MoveNext())
            {
                if (e.Name == containerNameMemberName)
                {
                    hasContainerNameMember = true;
                }

                if (e.Name == useCngMemberName)
                {
                    hasUseCngMember = true;
                }
            }

            _containerName = "";
            _useCng = false;

            if (hasContainerNameMember)
            {
                _containerName = (string)information.GetValue(containerNameMemberName, typeof(string));
            }

            if (hasUseCngMember)
            {
                _useCng = (bool)information.GetValue(useCngMemberName, typeof(bool));
            }

            _rsaParameters = new RSAParameters()
            {
                D = (byte[])information.GetValue("d", typeof(byte[])),
                DP = (byte[])information.GetValue("dp", typeof(byte[])),
                DQ = (byte[])information.GetValue("dq", typeof(byte[])),
                Exponent = (byte[])information.GetValue("exponent", typeof(byte[])),
                InverseQ = (byte[])information.GetValue("inverseQ", typeof(byte[])),
                Modulus = (byte[])information.GetValue("modulus", typeof(byte[])),
                P = (byte[])information.GetValue("p", typeof(byte[])),
                Q = (byte[])information.GetValue("q", typeof(byte[]))
            };
        }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(containerNameMemberName, _containerName);
            info.AddValue(useCngMemberName, _useCng);
            info.AddValue("d", _rsaParameters.D);
            info.AddValue("dp", _rsaParameters.DP);
            info.AddValue("dq", _rsaParameters.DQ);
            info.AddValue("exponent", _rsaParameters.Exponent);
            info.AddValue("inverseQ", _rsaParameters.InverseQ);
            info.AddValue("modulus", _rsaParameters.Modulus);
            info.AddValue("p", _rsaParameters.P);
            info.AddValue("q", _rsaParameters.Q);
        }
    }
}
