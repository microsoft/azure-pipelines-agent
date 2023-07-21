// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;

namespace Agent.Sdk
{
    public interface IScopedEnvironment
    {
        IDictionary GetEnvironmentVariables();
        void SetEnvironmentVariable(string key, string value);
        string GetEnvironmentVariable(string key);
    }

    public class SystemEnvironment : IScopedEnvironment
    {
        public IDictionary GetEnvironmentVariables()
        {
            return Environment.GetEnvironmentVariables();
        }

        public void SetEnvironmentVariable(string key, string value)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        public string GetEnvironmentVariable(string key)
        {
            return Environment.GetEnvironmentVariable(key);
        }
    }

    public class LocalEnvironment : IScopedEnvironment
    {
        private Dictionary<string, string> _delegate = null;

        public LocalEnvironment() : this(null)
        {

        }

        public LocalEnvironment(Dictionary<string, string> data)
        {
            _delegate = data;
            if (_delegate == null)
            {
                _delegate = new Dictionary<string, string>();
            }
        }

        public IDictionary GetEnvironmentVariables()
        {
            // we have to return a new collection here because this method
            // is used in foreach statements that modify the environment. This 
            // is allowed from Environment class since the methods are not typed to a
            // a single object.
            return new Dictionary<string, string>(_delegate);
        }

        public void SetEnvironmentVariable(string key, string value)
        {
            _delegate[key] = value;
        }

        public string GetEnvironmentVariable(string key)
        {
            return _delegate.GetValueOrDefault(key);
        }
    }
}