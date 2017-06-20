using System;
using System.Diagnostics;

namespace AgentService
{
    public class EventLogger
    {
        public const string EventSourceName = "VstsAgentService";

        public static void WriteInfo(string message)
        {
            WriteToEventLog(message, EventLogEntryType.Information);
        }

        public static void WriteException(Exception exception)
        {
            WriteToEventLog(exception.ToString(), EventLogEntryType.Error);
        }
        
        public static void WriteToEventLog(string eventText, EventLogEntryType entryType)
        {
            EventLog.WriteEntry(EventSourceName, eventText, entryType, 100);
        }
    }
}