
using System.Management;
using System.Runtime.Versioning;

namespace Agent.Sdk.Util;

[SupportedOSPlatform("windows")]
public class WmiUtil
{
    public static Task<List<ManagementBaseObject>> QueryGet(string query, CancellationToken cancellationToken)
    {
        var output = new List<ManagementBaseObject>();
        var completionSource = new TaskCompletionSource<List<ManagementBaseObject>>();

        var observer = new ManagementOperationObserver();
        observer.ObjectReady += (sender, obj) =>
        {
            output.Add(obj.NewObject);
        };
        observer.Completed += (sender, e) =>
        {
            switch (e.Status)
            {
                case ManagementStatus.CallCanceled:
                    completionSource.SetCanceled(cancellationToken);
                    break;

                case ManagementStatus.NoError:
                    completionSource.SetResult(output);
                    break;

                default:
                    completionSource.SetException(new Exception($"WMI Get Query failed with status {e.Status}"));
                    break;
            }
        };

        cancellationToken.Register(() =>
        {
            observer.Cancel();
        });

        using var searcher = new ManagementObjectSearcher(query);
        searcher.Get(observer);

        return completionSource.Task;
    }
}
