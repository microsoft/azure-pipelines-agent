using Microsoft.VisualStudio.Services.Agent.Listener.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace Test.L0.Listener.Configuration.Mocks
{
    /// <summary>
    /// Mock class for NativeWindowsServiceHelper
    /// Use this to mock any functions of this class
    /// </summary>
    public class MockNativeWindowsServiceHelper : NativeWindowsServiceHelper
    {
        public bool ShouldAccountBeManagedService { get; set; }
        public bool ShouldErrorHappenDuringManagedServiceAccoutCheck { get; set; }
        public override uint CheckNetIsServiceAccount(string ServerName, string AccountName, out bool isServiceAccount)
        {
            isServiceAccount = this.ShouldAccountBeManagedService;
            if (ShouldErrorHappenDuringManagedServiceAccoutCheck)
            {
                return 1;
            }
            else
            {
                return 0;
            }
        }
    }
}
