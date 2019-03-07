using System.Collections.Generic;
using System.Threading.Tasks;

namespace Agent.Plugins.TestFilePublisher
{
    public interface ITestFileFinder
    {
        Task<IEnumerable<string>> FindAsync(string pattern);
    }
    
}
