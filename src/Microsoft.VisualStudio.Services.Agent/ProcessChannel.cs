// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;

namespace Microsoft.VisualStudio.Services.Agent
{
    public delegate void StartProcessDelegate(string host,int port);

    public enum MessageType
    {
        NotInitialized = -1,
        NewJobRequest = 1,
        CancelRequest = 2,
        AgentShutdown = 3,
        OperatingSystemShutdown = 4
    }

    public struct WorkerMessage
    {
        public MessageType MessageType;
        public string Body;
        public WorkerMessage(MessageType messageType, string body)
        {
            MessageType = messageType;
            Body = body;
        }
    }

    [ServiceLocator(Default = typeof(ProcessChannel))]
    public interface IProcessChannel : IDisposable, IAgentService
    {
        void StartServer(StartProcessDelegate startProcess, bool disposeClient = true);
        void StartClient(string host, int port);
        Task SendAsync(MessageType messageType, string body, CancellationToken cancellationToken);
        Task<WorkerMessage> ReceiveAsync(CancellationToken cancellationToken);
    }

    public sealed class ProcessChannel : AgentService, IProcessChannel
    {
        private TcpListener _server;
        private TcpClient _client;
        private StreamString _writeStream;
        private StreamString _readStream;

        public void StartServer(StartProcessDelegate startProcess, bool disposeLocalClientHandle = true)
        {
            _server = new TcpListener(IPAddress.Loopback, 0);
            _server.Start();
            startProcess(((IPEndPoint)_server.LocalEndpoint).Address.ToString(), ((IPEndPoint)_server.LocalEndpoint).Port);
            _client = _server.AcceptTcpClient();
            _writeStream = new StreamString(_client.GetStream());
        }

        public void StartClient(string host, int port)
        {
            _client = new TcpClient(host,port);
            _readStream = new StreamString(_client.GetStream());
        }

        public async Task SendAsync(MessageType messageType, string body, CancellationToken cancellationToken)
        {
            await _writeStream.WriteInt32Async((int)messageType, cancellationToken);
            await _writeStream.WriteStringAsync(body, cancellationToken);
        }

        public async Task<WorkerMessage> ReceiveAsync(CancellationToken cancellationToken)
        {
            WorkerMessage result = new WorkerMessage(MessageType.NotInitialized, string.Empty);
            result.MessageType = (MessageType)await _readStream.ReadInt32Async(cancellationToken);
            result.Body = await _readStream.ReadStringAsync(cancellationToken);
            return result;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                _server?.Stop();
                _client?.Close();
            }
        }
    }
}
