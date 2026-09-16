// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Services.Agent.Listener.Configuration;
using Moq;
using Xunit;
using System.Security.Principal;
using Microsoft.VisualStudio.Services.Agent;
using Microsoft.VisualStudio.Services.Agent.Tests;
using Test.L0.Listener.Configuration.Mocks;
using System.ComponentModel;

namespace Test.L0.Listener.Configuration
{
    [Trait("SkipOn", "darwin")]
    [Trait("SkipOn", "linux")]
    public class NativeWindowsServiceHelperL0
    {

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "ConfigurationManagement")]
        public void EnsureGetDefaultServiceAccountShouldReturnNetworkServiceAccount()
        {
            using (TestHostContext tc = new TestHostContext(this, "EnsureGetDefaultServiceAccountShouldReturnNetworkServiceAccount"))
            {
                Tracing trace = tc.GetTrace();

                trace.Info("Creating an instance of the NativeWindowsServiceHelper class");
                var windowsServiceHelper = new NativeWindowsServiceHelper();

                trace.Info("Trying to get the Default Service Account when a BuildRelease Agent is being configured");
                var defaultServiceAccount = windowsServiceHelper.GetDefaultServiceAccount();
                var defaultServiceAccountName = defaultServiceAccount.ToString().ToUpper();

                // English/Default network service account
                bool isDefaultServiceAccountName = defaultServiceAccountName.Equals(@"NT AUTHORITY\NETWORK SERVICE");

                if (!isDefaultServiceAccountName)
                {
                    // German network service account
                    isDefaultServiceAccountName = defaultServiceAccountName.Equals(@"NT-AUTORITÄT\NETZWERKDIENST");

                    if (!isDefaultServiceAccountName)
                    {
                        // French network service account
                        isDefaultServiceAccountName = defaultServiceAccountName.Equals(@"AUTORITE NT\SERVICE RÉSEAU");

                        if (!isDefaultServiceAccountName)
                        {
                            // Italian network service account
                            isDefaultServiceAccountName = defaultServiceAccountName.Equals(@"NT AUTHORITY\SERVIZIO DI RETE");

                            if (!isDefaultServiceAccountName)
                            {
                                // Spanish network service account
                                isDefaultServiceAccountName = defaultServiceAccountName.Equals(@"NT AUTHORITY\SERVICIO DE RED");

                                // Account name in rest of the languages is the same as in English
                            }
                        }
                    }
                }

                Assert.True(isDefaultServiceAccountName, "If agent is getting configured as build-release agent, default service accout should be 'NT AUTHORITY\\NETWORK SERVICE' or its localized counterpart (tried English/Default name, German name, French name, Italian name and Spanish name).");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "ConfigurationManagement")]
        public void EnsureGetDefaultAdminServiceAccountShouldReturnLocalSystemAccount()
        {
            using (TestHostContext tc = new TestHostContext(this, "EnsureGetDefaultAdminServiceAccountShouldReturnLocalSystemAccount"))
            {
                Tracing trace = tc.GetTrace();

                trace.Info("Creating an instance of the NativeWindowsServiceHelper class");
                var windowsServiceHelper = new NativeWindowsServiceHelper();

                trace.Info("Trying to get the Default Service Account when a DeploymentAgent is being configured");
                var defaultServiceAccount = windowsServiceHelper.GetDefaultAdminServiceAccount();
                var defaultServiceAccountName = defaultServiceAccount.ToString().ToUpper();

                // English/Default network service account
                bool isDefaultServiceAccountName = defaultServiceAccountName.Equals(@"NT AUTHORITY\SYSTEM");

                if (!isDefaultServiceAccountName)
                {
                    // German network service account
                    isDefaultServiceAccountName = defaultServiceAccountName.Equals(@"NT-AUTORITÄT\SYSTEM");

                    if (!isDefaultServiceAccountName)
                    {
                        // French network service account
                        isDefaultServiceAccountName = defaultServiceAccountName.Equals(@"AUTORITE NT\SYSTEM");

                        // Account name in Italian, Spanish and rest of the languages is the same as in English
                    }
                }

                Assert.True(isDefaultServiceAccountName, "If agent is getting configured as deployment agent, default service accout should be 'NT AUTHORITY\\SYSTEM' or its localized counterpart (tried English/Default name, German name and French name).");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "ConfigurationManagement")]
        public void EnsureIsManagedServiceAccount_TrueForManagedAccount()
        {
            using (TestHostContext tc = new TestHostContext(this, "EnsureIsManagedServiceAccount_TrueForManagedAccount"))
            {
                Tracing trace = tc.GetTrace();

                trace.Info("Creating an instance of the MockNativeWindowsServiceHelper class");
                var windowsServiceHelper = new MockNativeWindowsServiceHelper();
                windowsServiceHelper.ShouldAccountBeManagedService = true;
                var isManagedServiceAccount = windowsServiceHelper.IsManagedServiceAccount("managedServiceAccount$");

                Assert.True(isManagedServiceAccount, "Account should be properly determined as managed service");
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "ConfigurationManagement")]
        public void EnsureIsManagedServiceAccount_FalseForNonManagedAccount()
        {
            using (TestHostContext tc = new TestHostContext(this, "EnsureIsManagedServiceAccount_TrueForManagedAccount"))
            {
                Tracing trace = tc.GetTrace();

                trace.Info("Creating an instance of the MockNativeWindowsServiceHelper class");
                var windowsServiceHelper = new MockNativeWindowsServiceHelper();
                windowsServiceHelper.ShouldAccountBeManagedService = false;
                var isManagedServiceAccount = windowsServiceHelper.IsManagedServiceAccount("managedServiceAccount$");

                Assert.True(!isManagedServiceAccount, "Account should be properly determined as not managed service");
            }
        }
    }
}
