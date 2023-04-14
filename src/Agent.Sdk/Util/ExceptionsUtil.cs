// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net.Sockets;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Agent.Sdk.Util
{
    public class ExceptionsUtil
    {
        public static void HandleAggregateException(AggregateException e, Action<string> traceErrorAction)
        {
            traceErrorAction("One or several exceptions have been occurred.");

            foreach (var ex in ((AggregateException)e).Flatten().InnerExceptions)
            {
                traceErrorAction(ex.ToString());
            }
        }

        public static void HandleSocketException(SocketException e, string url, Action<string> traceErrorAction)
        {
            traceErrorAction("SocketException occurred.");
            traceErrorAction(e.Message);
            traceErrorAction($"Verify whether you have (network) access to {url}");
            traceErrorAction($"URLs the agent need communicate with - {BlobStoreWarningInfoProvider.GetAllowListLinkForCurrentPlatform()}");
        }
    }
}
