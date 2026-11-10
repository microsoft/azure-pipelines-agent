// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Reflection;
using Microsoft.VisualStudio.Services.Agent.Listener.Configuration;
using Xunit;

namespace Microsoft.VisualStudio.Services.Agent.Tests.Listener.Configuration
{
    public sealed class AadCredentialProviderL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Configuration")]
        public void InteractiveProviderIsRegistered()
        {
            Assert.Equal(
                typeof(AadInteractiveAccessToken),
                CredentialManager.CredentialTypes[Constants.Configuration.AADInteractive]);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Configuration")]
        public void AzureCliProviderIsRegistered()
        {
            Assert.Equal(
                typeof(AzureCliAccessToken),
                CredentialManager.CredentialTypes[Constants.Configuration.AzureCLI]);
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Configuration")]
        public void OnlyAadInteractiveProviderRequiresInteractiveConfiguration()
        {
            Assert.False(new AadDeviceCodeAccessToken().RequireInteractive);
            Assert.True(new AadInteractiveAccessToken().RequireInteractive);
            Assert.False(new AzureCliAccessToken().RequireInteractive);
        }

        [Theory]
        [InlineData("authorization_uri=https://login.microsoftonline.com/tenant-id", "https://login.microsoftonline.com/tenant-id")]
        [InlineData("realm=\"example\", authorization_uri=\"https://login.microsoftonline.com/tenant-id\", client_id=\"id\"", "https://login.microsoftonline.com/tenant-id")]
        [Trait("Level", "L0")]
        [Trait("Category", "Configuration")]
        public void TenantAuthorityIsParsedFromBearerChallenge(string parameter, string expected)
        {
            var headers = new List<AuthenticationHeaderValue>
            {
                new AuthenticationHeaderValue("Basic", "realm=\"example\""),
                new AuthenticationHeaderValue("Bearer", parameter),
            };

            (bool parsed, Uri authority) = TryGetTenantAuthorityUrl(headers);

            Assert.True(parsed);
            Assert.Equal(new Uri(expected), authority);
        }

        [Theory]
        [InlineData("authorization_uri=http://login.microsoftonline.com/tenant-id")]
        [InlineData("authorization_uri=not-a-uri")]
        [InlineData("realm=\"example\"")]
        [Trait("Level", "L0")]
        [Trait("Category", "Configuration")]
        public void InvalidTenantAuthorityIsRejected(string parameter)
        {
            var headers = new[]
            {
                new AuthenticationHeaderValue("Bearer", parameter),
            };

            (bool parsed, Uri authority) = TryGetTenantAuthorityUrl(headers);

            Assert.False(parsed);
            Assert.Null(authority);
        }

        [Theory]
        [InlineData("https://login.microsoftonline.com/tenant-id", "tenant-id")]
        [InlineData("https://login.microsoftonline.com/tenant-id/oauth2/authorize", "tenant-id")]
        [Trait("Level", "L0")]
        [Trait("Category", "Configuration")]
        public void TenantIdentifierIsParsedFromAuthority(string authority, string expected)
        {
            Assert.Equal(expected, GetTenantId(new Uri(authority)));
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Configuration")]
        public void TenantAuthorityWithoutIdentifierIsRejected()
        {
            Assert.Throws<TargetInvocationException>(
                () => GetTenantId(new Uri("https://login.microsoftonline.com")));
        }

        private static (bool Parsed, Uri Authority) TryGetTenantAuthorityUrl(
            IEnumerable<AuthenticationHeaderValue> headers)
        {
            Type discoveryType = typeof(AadDeviceCodeAccessToken).Assembly.GetType(
                "Microsoft.VisualStudio.Services.Agent.Listener.Configuration.AadAuthorityDiscovery",
                throwOnError: true);
            MethodInfo method = discoveryType.GetMethod(
                "TryGetTenantAuthorityUrl",
                BindingFlags.Static | BindingFlags.NonPublic);
            object[] arguments = { headers, null };

            bool parsed = (bool)method.Invoke(null, arguments);
            return (parsed, arguments[1] as Uri);
        }

        private static string GetTenantId(Uri authority)
        {
            Type discoveryType = typeof(AadDeviceCodeAccessToken).Assembly.GetType(
                "Microsoft.VisualStudio.Services.Agent.Listener.Configuration.AadAuthorityDiscovery",
                throwOnError: true);
            MethodInfo method = discoveryType.GetMethod(
                "GetTenantId",
                BindingFlags.Static | BindingFlags.NonPublic);

            return (string)method.Invoke(null, new object[] { authority });
        }
    }
}
