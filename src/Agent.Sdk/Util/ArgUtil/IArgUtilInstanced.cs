using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    public interface IArgUtilInstanced
    {
        public void Directory([ValidatedNotNull] string directory, string name);

        public void Equal<T>(T expected, T actual, string name);

        public void File(string fileName, string name);

        public void NotNull([ValidatedNotNull] object value, string name);

        public void NotNullOrEmpty([ValidatedNotNull] string value, string name);

        public void ListNotNullOrEmpty<T>([ValidatedNotNull] IEnumerable<T> value, string name);
        public void NotEmpty(Guid value, string name);
        public void Null(object value, string name);
    }
}
