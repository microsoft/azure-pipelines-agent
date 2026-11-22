// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Security.Cryptography;

namespace Microsoft.VisualStudio.Services.Agent.Listener
{
    public enum ServiceScriptStatus
    {
        Match,
        Different,
        Missing
    }

    public static class ServiceScriptInspector
    {
        public static ServiceScriptStatus GetStatus(string serviceScript, string shippedScript)
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(serviceScript);
            }
            catch (FileNotFoundException)
            {
                return ServiceScriptStatus.Missing;
            }
            catch (DirectoryNotFoundException)
            {
                return ServiceScriptStatus.Missing;
            }

            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            {
                return ServiceScriptStatus.Different;
            }

            using var service = File.OpenRead(serviceScript);
            using var shipped = File.OpenRead(shippedScript);
            if (service.Length != shipped.Length)
            {
                return ServiceScriptStatus.Different;
            }

            byte[] serviceHash = SHA256.HashData(service);
            byte[] shippedHash = SHA256.HashData(shipped);
            return CryptographicOperations.FixedTimeEquals(serviceHash, shippedHash)
                ? ServiceScriptStatus.Match
                : ServiceScriptStatus.Different;
        }
    }
}
