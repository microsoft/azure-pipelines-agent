using Microsoft.VisualStudio.Services.ServiceEndpoints.WebApi;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    public static class MaskingUtil
    {
        /// <summary>
        /// Returns true if endpoint authorization parameter with provided key is a secret
        /// Masks all keys except the specific fields - for which we know that they don't contain secrets
        /// </summary>
        /// <param name="key">Key to check</param>
        /// <returns>Returns true if key is a secret</returns>
        public static bool IsEndpointAuthorizationParametersSecret(string key)
        {
            var excludedAuthParams = new string[]{
                EndpointAuthorizationParameters.IdToken,
                EndpointAuthorizationParameters.Role,
                EndpointAuthorizationParameters.Scope,
                EndpointAuthorizationParameters.TenantId,
                EndpointAuthorizationParameters.IssuedAt,
                EndpointAuthorizationParameters.ExpiresAt,
                EndpointAuthorizationParameters.Audience,
                EndpointAuthorizationParameters.AuthenticationType,
                EndpointAuthorizationParameters.AuthorizationType,
                EndpointAuthorizationParameters.AccessTokenType,
                EndpointAuthorizationParameters.AccessTokenFetchingMethod,
                EndpointAuthorizationParameters.UseWindowsSecurity,
                EndpointAuthorizationParameters.Unsecured,
                EndpointAuthorizationParameters.OAuthAccessTokenIsSupplied,
                EndpointAuthorizationParameters.Audience,
                EndpointAuthorizationParameters.CompleteCallbackPayload,
                EndpointAuthorizationParameters.AcceptUntrustedCertificates
            };

            foreach (var authParam in excludedAuthParams)
            {
                if (String.Equals(key, authParam, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
