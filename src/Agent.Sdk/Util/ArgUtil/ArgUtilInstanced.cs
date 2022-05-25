using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Microsoft.VisualStudio.Services.Agent.Util
{
    public class ArgUtilInstanced : IArgUtilInstanced
    {
        public ArgUtilInstanced() { }

        public virtual void Directory([ValidatedNotNull] string directory, string name)
        {
            ArgUtil.NotNullOrEmpty(directory, name);
            if (!System.IO.Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException(
                    message: StringUtil.Loc("DirectoryNotFound", directory));
            }
        }

        public void Equal<T>(T expected, T actual, string name)
        {
            if (object.ReferenceEquals(expected, actual))
            {
                return;
            }

            if (object.ReferenceEquals(expected, null) ||
                !expected.Equals(actual))
            {
                throw new ArgumentOutOfRangeException(
                    paramName: name,
                    actualValue: actual,
                    message: $"{name} does not equal expected value. Expected '{expected}'. Actual '{actual}'.");
            }
        }

        public virtual void File(string fileName, string name)
        {
            ArgUtil.NotNullOrEmpty(fileName, name);
            if (!System.IO.File.Exists(fileName))
            {
                throw new FileNotFoundException(
                    message: StringUtil.Loc("FileNotFound", fileName),
                    fileName: fileName);
            }
        }

        public void NotNull([ValidatedNotNull] object value, string name)
        {
            if (object.ReferenceEquals(value, null))
            {
                throw new ArgumentNullException(name);
            }
        }

        public void NotNullOrEmpty([ValidatedNotNull] string value, string name)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentNullException(name);
            }
        }

        public void ListNotNullOrEmpty<T>([ValidatedNotNull] IEnumerable<T> value, string name)
        {
            if (object.ReferenceEquals(value, null))
            {
                throw new ArgumentNullException(name);
            }
            else if (!value.Any())
            {
                throw new ArgumentException(message: $"{name} must have at least one item.", paramName: name);
            }
        }

        public void NotEmpty(Guid value, string name)
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentNullException(name);
            }
        }

        public void Null(object value, string name)
        {
            if (!object.ReferenceEquals(value, null))
            {
                throw new ArgumentException(message: $"{name} should be null.", paramName: name);
            }
        }
    }
}
