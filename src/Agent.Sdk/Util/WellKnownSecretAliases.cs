// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Agent.Sdk.Util
{
    public static class WellKnownSecretAliases
    {
        // Known configuration secrets
        public static readonly string ConfigurePassword = "Configure.Password";
        public static readonly string ConfigureProxyPassword = "Configure.ProxyPassword";
        public static readonly string ConfigureSslClientCert = "Configure.SslClientCert";
        public static readonly string ConfigureToken = "Configure.Token";
        public static readonly string ConfigureWindowsLogonPassword = "Configure.WindowsLogonPassword";
        public static readonly string RemovePassword = "Remove.Password";
        public static readonly string RemoveToken = "Remove.Token";

        // Other known origins for secrets
        public static readonly string GitSourceProviderAuthHeader = "GitSourceProvider.AuthHeader";
        public static readonly string TaskSetSecretCommand = "TaskSetSecretCommand";
        public static readonly string TaskSetVariableCommand = "TaskSetVariableCommand";
        public static readonly string TaskSetEndpointCommandAuthParameter = "TaskSetEndpointCommand.authParameter";
        public static readonly string UserSuppliedSecret = "UserSuppliedSecret";
        public static readonly string AddingMaskHint = "AddingMaskHint";
        public static readonly string SecureFileTicket = "SecureFileTicket";
        public static readonly string TerminalReadSecret = "Terminal.ReadSecret";
        public static readonly string ProxyPassword = "ProxyPassword";
        public static readonly string ClientCertificatePassword = "ClientCertificatePassword";

        // Secret regex aliases
        public static readonly string UrlSecretPattern = "RegexUrlSecretPattern";
        public static readonly string CredScanPatterns = "RegexCredScanPatterns";

        // Value encoder aliases
        public static readonly string JsonStringEscape = "ValueEncoderJsonStringEscape";
        public static readonly string UriDataEscape = "ValueEncoderUriDataEscape";
        public static readonly string BackslashEscape = "ValueEncoderBackslashEscape";
    }
}
