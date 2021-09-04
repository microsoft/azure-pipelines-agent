// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.Services.Content.Common.Telemetry;

namespace Microsoft.VisualStudio.Services.Agent.Tests
{
    public class TestTelemetrySender : ITelemetrySender
    {
        private bool startThrows;
        private bool stopThrows;
        private bool sendThrows;
        protected Action<TelemetryRecord> sendCallback;

        public readonly ConcurrentBag<TelemetryRecord> RecordsSent;
        public bool Started { get; private set; }
        public bool StoppedSuccessfully { get; private set; }

        public TestTelemetrySender() : this(startThrows: false, stopThrows: false, sendThrows: false, sendCallback: null)
        {
        }

        public TestTelemetrySender(bool startThrows = false, bool stopThrows = false, bool sendThrows = false, Action<TelemetryRecord> sendCallback = null)
        {
            this.startThrows = startThrows;
            this.stopThrows = stopThrows;
            this.sendThrows = sendThrows;
            this.sendCallback = sendCallback;

            this.RecordsSent = new ConcurrentBag<TelemetryRecord>();
        }

        private void AddRecord(TelemetryRecord record)
        {
            RecordsSent.Add(record);
            sendCallback(record);
        }

        public IEnumerable<ActionTelemetryRecord> ActionTelemetryRecords
        {
            get { return RecordsSent.OfType<ActionTelemetryRecord>().ToArray(); }
        }

        public IEnumerable<ErrorTelemetryRecord> ErrorTelemetryRecords
        {
            get { return RecordsSent.OfType<ErrorTelemetryRecord>().ToArray(); }
        }

        public void SendActionTelemetry(ActionTelemetryRecord actionTelemetry)
        {
            CheckStarted();

            if (sendThrows)
            {
                throw new ApplicationException(nameof(SendActionTelemetry));
            }

            AddRecord(actionTelemetry);
        }

        public void SendErrorTelemetry(ErrorTelemetryRecord errorTelemetry)
        {
            CheckStarted();

            if (sendThrows)
            {
                throw new ApplicationException(nameof(SendErrorTelemetry));
            }

            AddRecord(errorTelemetry);
        }

        public void SendRecord(TelemetryRecord record)
        {
            CheckStarted();

            if (sendThrows)
            {
                throw new ApplicationException(nameof(SendErrorTelemetry));
            }

            AddRecord(record);
        }

        public void StartSender()
        {
            if (startThrows)
            {
                throw new ApplicationException(nameof(StartSender));
            }

            Started = true;
            StoppedSuccessfully = false;
        }

        public void StopSender()
        {
            if (stopThrows)
            {
                throw new ApplicationException(nameof(StopSender));
            }

            Started = false;
            StoppedSuccessfully = true;
        }

        protected void CheckStarted()
        {
            if (!this.Started)
            {
                throw new InvalidOperationException($"This {nameof(ITelemetrySender)} has not been started");
            }
        }
    }
}
