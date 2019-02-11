// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agent.Plugins.Log.TestResultParser.Contracts;
using Microsoft.VisualStudio.Services.CustomerIntelligence.WebApi;
using Microsoft.VisualStudio.Services.WebPlatform;
using Newtonsoft.Json;

namespace Agent.Plugins.Log.TestResultParser.Plugin
{
    public class TelemetryDataCollector : ITelemetryDataCollector
    {
        private readonly ITraceLogger _logger;
        private readonly CustomerIntelligenceHttpClient _httpClient;
        private const string CumulativeTelemetryFeatureName = "ConsolidatedTelemetry";
        private readonly object publishLockNode = new object();
        private ConcurrentDictionary<string, object> _properties = new ConcurrentDictionary<string, object>();

        public string Area => "TestResultParser";

        public TelemetryDataCollector(IClientFactory clientFactory, ITraceLogger logger)
        {
            _logger = logger;
            _httpClient = clientFactory.GetClient<CustomerIntelligenceHttpClient>();
        }

        /// <inheritdoc />
        public void AddToCumulativeTelemetry(string subArea, string property, object value, bool aggregate = false)
        {
            var propertKey = !string.IsNullOrEmpty(subArea) ? $"{subArea}:{property}" : property;

            try
            {
                // If key does not exist or aggregate option is false add value blindly
                if (!aggregate || !_properties.ContainsKey(propertKey))
                {
                    _properties[propertKey] = value;
                    return;
                }

                // If key exists and the value is a list, assume that existing value is a list and concat them
                if (value is IList)
                {
                    foreach (var element in (value as IList))
                    {
                        (_properties[propertKey] as IList).Add(element);
                    }
                    return;
                }

                // If key exists and is a list add new items to list
                if (_properties[propertKey] is IList)
                {
                    (_properties[propertKey] as IList).Add(value);
                    return;
                }

                // If the key exists and value is integer or double arithmetically add them
                if (_properties[propertKey] is int)
                {
                    _properties[propertKey] = (int)_properties[propertKey] + (int)value;
                }
                else if (_properties[propertKey] is double)
                {
                    _properties[propertKey] = (double)_properties[propertKey] + (double)value;
                }
                else
                {
                    // If unknown type just blindly set value
                    _properties[propertKey] = value;
                }
            }
            catch (Exception e)
            {
                _logger.Warning($"TelemetryDataCollector.AddToCumulativeTelemetry: Failed to add {value} with key {propertKey} due to {e}");
            }
        }

        public Task PublishCumulativeTelemetryAsync()
        {
            lock (publishLockNode)
            {
                var ciEvent = new CustomerIntelligenceEvent
                {
                    Area = Area,
                    Feature = CumulativeTelemetryFeatureName,
                    Properties = _properties.ToDictionary(entry => entry.Key, entry => entry.Value)
                };

                _logger.Info(JsonConvert.SerializeObject(_properties));

                // This is to ensure that the single ci event is never fired more than once.
                _properties.Clear();

                return _httpClient.PublishEventsAsync(new[] { ciEvent });
            }
        }

        /// <inheritdoc />
        public Task PublishTelemetryAsync(string feature, Dictionary<string, object> properties)
        {
            var ciEvent = new CustomerIntelligenceEvent
            {
                Area = Area,
                Feature = feature,
                Properties = properties
            };

            return _httpClient.PublishEventsAsync(new[] { ciEvent });
        }
    }
}
