// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.VisualStudio.Services.Agent.Listener.Configuration;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Listener.Configuration
{
    public sealed class ArgumentValidatorTestsL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "ArgumentValidator")]
        public void ServerUrlValidator()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Assert.True(Validators.ServerUrlValidator("http://servername"));
                Assert.False(Validators.ServerUrlValidator("Fail"));
                Assert.False(Validators.ServerUrlValidator("ftp://servername"));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "ArgumentValidator")]
        public void AuthSchemeValidator()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Assert.True(Validators.AuthSchemeValidator("pat"));
                Assert.False(Validators.AuthSchemeValidator("Fail"));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "ArgumentValidator")]
        public void NonEmptyValidator()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Assert.True(Validators.NonEmptyValidator("test"));
                Assert.False(Validators.NonEmptyValidator(string.Empty));
            }
        }


        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "ArgumentValidator")]
        [Trait("SkipOn", "darwin")]
        [Trait("SkipOn", "linux")]
        public void WindowsLogonAccountValidator()
        {
            using (TestHostContext hc = new TestHostContext(this))
            {
                Assert.False(Validators.NTAccountValidator(string.Empty));

                // English/Default local service account
                bool foundAccount = Validators.NTAccountValidator(@"NT AUTHORITY\LOCAL SERVICE");

                if(!foundAccount)
                {
                    // German local service account
                    foundAccount = Validators.NTAccountValidator(@"NT-AUTORITÄT\LOKALER DIENST");

                    if (!foundAccount)
                    {
                        // French local service account
                        foundAccount = Validators.NTAccountValidator(@"AUTORITE NT\SERVICE LOCAL");

                        if (!foundAccount)
                        {
                            // Italian local service account
                            foundAccount = Validators.NTAccountValidator(@"NT AUTHORITY\SERVIZIO LOCALE");

                            if (!foundAccount)
                            {
                                // Spanish local service account
                                foundAccount = Validators.NTAccountValidator(@"NT AUTHORITY\SERVICIO LOC");

                                // Account name in rest of the languages is the same as in English
                            }
                        }
                    }
                }

                Assert.True(foundAccount, @"Wasn't able to validate local service account ""NT AUTHORITY\LOCAL SERVICE"" (tried English/Default name, German name, French name, Italian name and Spanish name)");

            }
        }
    }
}