using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net.Http;
using System.Text;
using System.Web;

namespace Agent.Sdk.Util
{
    class AzureInstanceMetadataProvider : IDisposable
    {
        private HttpClient _client;
        private const string _version = "2021-02-01";
        private const string _azureMetadataEndpoint = "http://169.254.169.254/metadata";

        public AzureInstanceMetadataProvider()
        {
            _client = new HttpClient();
        }

        public void Dispose()
        {
            _client?.Dispose();
            _client = null;
        }

        private HttpRequestMessage BuildRequest(string url, Dictionary<string, string> parameters)
        {
            UriBuilder builder = new UriBuilder(url);

            NameValueCollection queryParameters = HttpUtility.ParseQueryString(builder.Query);

            if (!parameters.ContainsKey("api-version"))
            {
                parameters.Add("api-version", _version);
            }

            foreach (KeyValuePair<string, string> entry in parameters)
            {
                queryParameters[entry.Key] = entry.Value;
            }

            builder.Query = queryParameters.ToString();

            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, builder.Uri);
            request.Headers.Add("Metadata", "true");

            return request;
        }

        public string GetMetadata(string category, Dictionary<string, string> parameters)
        {
            if (_client == null)
            {
                throw new ObjectDisposedException(nameof(AzureInstanceMetadataProvider));
            }

            using HttpRequestMessage request = BuildRequest($"{_azureMetadataEndpoint}/{category}", parameters);
            HttpResponseMessage response = _client.SendAsync(request).Result;

            if (!response.IsSuccessStatusCode)
            {
                string errorText = response.Content.ReadAsStringAsync().Result;
                throw new Exception($"Error retrieving metadata category { category }. Received status {response.StatusCode}: {errorText}");
            }

            return response.Content.ReadAsStringAsync().Result;
        }

        public bool HasMetadata()
        {
            try
            {
                return GetMetadata("instance", new Dictionary<string, string> { ["format"] = "text" }) != null;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
