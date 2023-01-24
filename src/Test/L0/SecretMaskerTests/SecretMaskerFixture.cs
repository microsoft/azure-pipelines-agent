using System;
using Microsoft.TeamFoundation.DistributedTask.Logging;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public class SecretMaskerFixture : IDisposable
    {
        private bool disposedValue;

        public SecretMasker SecretMasker { get; set; }

        public SecretMaskerFixture()
        {
            SecretMasker = new SecretMasker();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    SecretMasker.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
