// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

﻿using System.Collections.Generic;
using System.Threading.Tasks;

namespace Agent.Plugins.Log.TestFilePublisher
{
    public interface ITestFileFinder
    {
        Task<IEnumerable<string>> FindAsync(IList<string> patterns);
    }
}
