using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Serialization;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.VisualStudio.Services.Agent.Util;

namespace Microsoft.VisualStudio.Services.Agent.Worker.Build
{
    [ServiceLocator(Default = typeof(SvnCommandManager))]
    public interface ISvnCommandManager : IAgentService
    {
        /// <summary>
        /// Initializes svn command path and execution environment
        /// </summary>
        /// <param name="context">The build commands' execution context</param>
        /// <param name="endpoint">The Subversion server endpoint providing URL, username/password, and untrasted certs acceptace information</param>
        /// <param name="cancellationToken">The cancellation token used to stop svn command execution</param>
        void Init(
            IExecutionContext context,
            ServiceEndpoint endpoint,
            CancellationToken cancellationToken);

        /// <summary>
        /// svn info URL --depth empty --revision <sourceRevision> --xml --username <user> --password <password> --non-interactive [--trust-server-cert]
        /// </summary>
        /// <param name="serverPath"></param>
        /// <param name="sourceRevision"></param>
        /// <returns></returns>
        Task<long> GetLatestRevisionAsync(string serverPath, string sourceRevision);

        /// <summary>
        /// Removes unused and duplicate mappings.
        /// </summary>
        /// <param name="allMappings"></param>
        /// <returns></returns>
        Dictionary<string, SvnMappingDetails> NormalizeMappings(List<SvnMappingDetails> allMappings);

        /// <summary>
        /// Normalizes path separator for server and local paths.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="pathSeparator"></param>
        /// <param name="altPathSeparator"></param>
        /// <returns></returns>
        string NormalizeRelativePath(string path, char pathSeparator, char altPathSeparator);

        /// <summary>
        /// Detects old mappings (if any) and refreshes the SVN working copies to match the new mappings.
        /// </summary>
        /// <param name="rootPath"></param>
        /// <param name="distinctMappings"></param>
        /// <param name="cleanRepository"></param>
        /// <param name="sourceBranch"></param>
        /// <param name="revision"></param>
        /// <returns></returns>
        Task<string> UpdateWorkspace(string rootPath, Dictionary<string, SvnMappingDetails> distinctMappings, bool cleanRepository, string sourceBranch, string revision);

    }

    public class SvnCommandManager : AgentService, ISvnCommandManager
    {
        public void Init(
            IExecutionContext context,
            ServiceEndpoint endpoint,
            CancellationToken cancellationToken)
        {
            // Validation.
            ArgUtil.NotNull(context, nameof(context));
            ArgUtil.NotNull(endpoint, nameof(endpoint));
            ArgUtil.NotNull(cancellationToken, nameof(cancellationToken));

            ArgUtil.NotNull(endpoint.Url, nameof(endpoint.Url));
            ArgUtil.Equal(true, endpoint.Url.IsAbsoluteUri, nameof(endpoint.Url.IsAbsoluteUri));
            ArgUtil.NotNull(endpoint.Data, nameof(endpoint.Data));

            ArgUtil.NotNull(endpoint.Authorization, nameof(endpoint.Authorization));
            ArgUtil.NotNull(endpoint.Authorization.Parameters, nameof(endpoint.Authorization.Parameters));
            ArgUtil.Equal(EndpointAuthorizationSchemes.UsernamePassword, endpoint.Authorization.Scheme, nameof(endpoint.Authorization.Scheme));

            _context = context;
            _endpoint = endpoint;
            _cancellationToken = cancellationToken;

            //find git in %Path%
            IWhichUtil whichTool = HostContext.GetService<IWhichUtil>();
            string svnPath = whichTool.Which("svn");

            if (string.IsNullOrEmpty(svnPath))
            {
                throw new Exception(StringUtil.Loc("SvnNotInstalled"));
            }
            else
            {
                _context.Debug($"Found svn installation path: {svnPath}.");
                _svn = svnPath;
            }

            // External providers may need basic auth or tokens
            endpoint.Authorization.Parameters.TryGetValue(EndpointAuthorizationParameters.Username, out _username);
            endpoint.Authorization.Parameters.TryGetValue(EndpointAuthorizationParameters.Password, out _password);

            _acceptUntrusted = endpoint.Data.ContainsKey(/* WellKnownEndpointData.SvnAcceptUntrustedCertificates */ "acceptUntrustedCerts") &&
            StringUtil.ConvertToBoolean(endpoint.Data[/* WellKnownEndpointData.SvnAcceptUntrustedCertificates */ "acceptUntrustedCerts"], defaultValue: false) || true;
        }

        public async Task<string> UpdateWorkspace(
            string rootPath,
            Dictionary<string, SvnMappingDetails> distinctMappings,
            bool cleanRepository,
            string sourceBranch,
            string revision)
        {
            if (cleanRepository)
            {
                RecreateDirectory(rootPath);
            }

            Dictionary<string, Uri> oldMappings = await GetOldMappings(rootPath);
            _context.Debug($"oldMappings.Count: {oldMappings.Count}");
            oldMappings.ToList().ForEach(p => _context.Debug($"   {p.Key}: {p.Value}"));

            Dictionary<string, SvnMappingDetails> newMappings = BuildNewMappings(rootPath, sourceBranch, distinctMappings);
            _context.Debug($"newMappings.Count: {newMappings.Count}");
            newMappings.ToList().ForEach(p => _context.Debug($"    [{p.Key}] ServerPath: {p.Value.ServerPath}, LocalPath: {p.Value.LocalPath}, Depth: {p.Value.Depth}, Revision: {p.Value.Revision}, IgnoreExternals: {p.Value.IgnoreExternals}"));

            CleanUpSvnWorkspace(oldMappings, newMappings);

            long maxRevision = 0;

            foreach (SvnMappingDetails mapping in newMappings.Values)
            {
                long mappingRevision = await GetLatestRevisionAsync(mapping.ServerPath, revision);
                if (mappingRevision > maxRevision)
                {
                    maxRevision = mappingRevision;
                }
            }

            await UpdateToRevisionAsync(oldMappings, newMappings, maxRevision);

            return maxRevision > 0 ? maxRevision.ToString() : "HEAD";
        }

        private void RecreateDirectory(string path)
        {
            if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly).Any())
            {
                IOUtil.DeleteDirectory(path, _cancellationToken);
            }

            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        private async Task<Dictionary<string, Uri>> GetOldMappings(string rootPath)
        {
            if (File.Exists(rootPath))
            {
                throw new Exception(StringUtil.Loc("SvnFileAlreadyExists", rootPath));
            }

            Dictionary<string, Uri> mappings = new Dictionary<string, Uri>();

            if (Directory.Exists(rootPath))
            {
                foreach (string workingDirectoryPath in GetSvnWorkingCopyPaths(rootPath))
                {

                    Uri url = await GetRootUrlAsync(workingDirectoryPath);

                    if (url != null)
                    {
                        mappings.Add(workingDirectoryPath, url);
                    }

                }
            }

            return mappings;
        }

        private List<string> GetSvnWorkingCopyPaths(string rootPath)
        {
            if (Directory.Exists(Path.Combine(rootPath, ".svn")))
            {
                return new List<string>() { rootPath };
            }
            else
            {
                ConcurrentStack<string> candidates = new ConcurrentStack<string>();

                Directory.EnumerateDirectories(rootPath, "*", SearchOption.TopDirectoryOnly)
                    .AsParallel()
                    .ForAll(fld => candidates.PushRange(GetSvnWorkingCopyPaths(fld).ToArray()));

                return candidates.ToList();
            }
        }

        private Dictionary<string, SvnMappingDetails> BuildNewMappings(string rootPath, string sourceBranch, Dictionary<string, SvnMappingDetails> distinctMappings)
        {
            Dictionary<string, SvnMappingDetails> mappings = new Dictionary<string, SvnMappingDetails>();

            if (distinctMappings != null && distinctMappings.Count > 0)
            {
                foreach (KeyValuePair<string, SvnMappingDetails> mapping in distinctMappings)
                {
                    SvnMappingDetails mappingDetails = mapping.Value;
                    string serverPath = mapping.Key;
                    string localPath = mappingDetails.LocalPath;

                    string uri = BuildSvnUri(serverPath);
                    string absoluteLocalPath = Path.Combine(rootPath, localPath);

                    SvnMappingDetails newMappingDetails = new SvnMappingDetails();

                    newMappingDetails.ServerPath = uri;
                    newMappingDetails.LocalPath = absoluteLocalPath;
                    newMappingDetails.Revision = mappingDetails.Revision;
                    newMappingDetails.Depth = mappingDetails.Depth;
                    newMappingDetails.IgnoreExternals = mappingDetails.IgnoreExternals;

                    mappings.Add(absoluteLocalPath, newMappingDetails);
                }
            }
            else
            {
                string uri = BuildSvnUri(sourceBranch);

                SvnMappingDetails newMappingDetails = new SvnMappingDetails();
                newMappingDetails.ServerPath = uri;
                newMappingDetails.LocalPath = rootPath;
                newMappingDetails.Revision = "HEAD";
                newMappingDetails.Depth = 3;  //Infinity
                newMappingDetails.IgnoreExternals = true;

                mappings.Add(rootPath, newMappingDetails);
            }

            return mappings;
        }

        public async Task<long> GetLatestRevisionAsync(string serverPath, string sourceRevision)
        {
            _context.Debug($@"Get latest revision of: '{_endpoint.Url.AbsoluteUri}' at or before: '{sourceRevision}'.");
            string xml = await RunPorcelainCommandAsync("info", 
                                                        serverPath, 
                                                        "--depth", "empty", 
                                                        "--revision", sourceRevision, 
                                                        "--xml");

            // Deserialize the XML.
            // The command returns a non-zero exit code if the source revision is not found.
            // The assertions performed here should never fail.
            XmlSerializer serializer = new XmlSerializer(typeof(SvnInfo));
            ArgUtil.NotNullOrEmpty(xml, nameof(xml));

            using (StringReader reader = new StringReader(xml))
            {
                SvnInfo info = serializer.Deserialize(reader) as SvnInfo;
                ArgUtil.NotNull(info, nameof(info));
                ArgUtil.NotNull(info.Entries, nameof(info.Entries));
                ArgUtil.Equal(1, info.Entries.Length, nameof(info.Entries.Length));

                long revision = 0;
                long.TryParse(info.Entries[0].Commit?.Revision?? sourceRevision, out revision);

                return revision;
            }
        }
        private async Task<Uri> GetRootUrlAsync(string localPath)
        {
            _context.Debug($@"Get URL for: '{localPath}'.");
            try
            {
                string xml = await RunPorcelainCommandAsync("info",
                                                            localPath,
                                                            "--depth", "empty",
                                                            "--xml");

                // Deserialize the XML.
                // The command returns a non-zero exit code if the local path is not a working copy.
                // The assertions performed here should never fail.
                XmlSerializer serializer = new XmlSerializer(typeof(SvnInfo));
                ArgUtil.NotNullOrEmpty(xml, nameof(xml));

                using (StringReader reader = new StringReader(xml))
                {
                    SvnInfo info = serializer.Deserialize(reader) as SvnInfo;
                    ArgUtil.NotNull(info, nameof(info));
                    ArgUtil.NotNull(info.Entries, nameof(info.Entries));
                    ArgUtil.Equal(1, info.Entries.Length, nameof(info.Entries.Length));

                    return new Uri(info.Entries[0].Url);
                }
            }
            catch (ProcessExitCodeException)
            {
                return null;
            }
        }

        private async Task UpdateToRevisionAsync(Dictionary<string, Uri> oldMappings, Dictionary<string, SvnMappingDetails> newMappings, long maxRevision)
        {
            foreach (KeyValuePair<string, SvnMappingDetails> mapping in newMappings)
            {
                string localPath = mapping.Key;
                SvnMappingDetails mappingDetails = mapping.Value;
                Uri serverUri = new Uri(mappingDetails.ServerPath);

                string effectiveRevision = EffectiveRevision(mappingDetails.Revision, maxRevision);

                if (!Directory.Exists(Path.Combine(localPath, ".svn")))
                {
                    _context.Debug(String.Format(
                        "Checking out with depth: {0}, revision: {1}, ignore externals: {2}",
                        mappingDetails.Depth,
                        effectiveRevision,
                        mappingDetails.IgnoreExternals));

                    await CheckoutAsync(mappingDetails, effectiveRevision);
                }
                else if (oldMappings.ContainsKey(localPath) && oldMappings[localPath].Equals(serverUri))
                {
                    _context.Debug(String.Format(
                        "Updating with depth: {0}, revision: {1}, ignore externals: {2}",
                        mappingDetails.Depth,
                        mappingDetails.Revision,
                        mappingDetails.IgnoreExternals));

                    await UpdateAsync(mappingDetails, effectiveRevision);
                }
                else
                {
                    _context.Debug(String.Format(
                        "Switching to {0}  with depth: {1}, revision: {2}, ignore externals: {3}",
                        mappingDetails.ServerPath,
                        mappingDetails.Depth,
                        mappingDetails.Revision,
                        mappingDetails.IgnoreExternals));

                    await SwitchAsync(mappingDetails, effectiveRevision);
                }
            }
        }

        private string EffectiveRevision(string mappingRevision, long maxRevision)
        {
            if (!mappingRevision.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
            {
                // A specific revision has been requested in mapping
                return mappingRevision;
            }
            else if (maxRevision == 0)
            {
                // Tip revision
                return "HEAD";
            }
            else
            {
                return maxRevision.ToString();
            }
        }

        private void CleanUpSvnWorkspace(Dictionary<string, Uri> oldMappings, Dictionary<string, SvnMappingDetails> newMappings)
        {
            oldMappings.Where(m => !newMappings.ContainsKey(m.Key))
                .AsParallel()
                .ForAll(m => IOUtil.DeleteDirectory(m.Key, CancellationToken.None));
        }

        /// <summary>
        /// svn update localPath --depth empty --revision <sourceRevision> --xml --username lin --password ideafix --non-interactive [--trust-server-cert]
        /// </summary>
        /// <param name="mapping"></param>
        /// <returns></returns>
        private async Task UpdateAsync(SvnMappingDetails mapping, string effectiveRevision)
        {
            _context.Debug($@"Update '{mapping.LocalPath}'.");
            await RunCommandAsync("update", 
                                  mapping.LocalPath,
                                  "--revision", effectiveRevision,
                                  "--depth", ToDepthArgument(mapping.Depth),
                                  mapping.IgnoreExternals ? "--ignore-externals" : null);
        }

        /// <summary>
        /// svn switch localPath --depth empty --revision <sourceRevision> --xml --username lin --password ideafix --non-interactive [--trust-server-cert]
        /// </summary>
        /// <param name="mapping"></param>
        /// <returns></returns>
        private async Task SwitchAsync(SvnMappingDetails mapping, string effectiveRevision)
        {
            _context.Debug($@"Switch '{mapping.LocalPath}' to '{mapping.ServerPath}'.");
            await RunCommandAsync("switch",
                                  mapping.ServerPath,
                                  mapping.LocalPath,
                                  "--revision", effectiveRevision,
                                  "--depth", ToDepthArgument(mapping.Depth),
                                  mapping.IgnoreExternals ? "--ignore-externals" : null);
        }

        /// <summary>
        /// svn checkout localPath --depth empty --revision <sourceRevision> --xml --username lin --password ideafix --non-interactive [--trust-server-cert]
        /// </summary>
        /// <param name="mapping"></param>
        /// <returns></returns>
        private async Task CheckoutAsync(SvnMappingDetails mapping, string effectiveRevision)
        {
            _context.Debug($@"Checkout '{mapping.ServerPath}' to '{mapping.LocalPath}'.");
            await RunCommandAsync("checkout",
                                  mapping.ServerPath,
                                  mapping.LocalPath,
                                  "--revision", effectiveRevision,
                                  "--depth", ToDepthArgument(mapping.Depth),
                                  mapping.IgnoreExternals ? "--ignore-externals" : null);
        }

        private string BuildSvnUri(string serverPath)
        {
            StringBuilder sb = new StringBuilder(_endpoint.Url.ToString());

            if (!string.IsNullOrEmpty(serverPath))
            {
                if (sb[sb.Length - 1] != '/')
                {
                    sb.Append('/');
                }
                sb.Append(serverPath);
            }

            return sb.Replace('\\', '/').ToString();
        }

        private string FormatArgumentsWithDefaults(params string[] args)
        {
            // Format each arg.
            List < string> formattedArgs = new List<string>();
            foreach (string arg in args ?? new string[0])
            {
                if (!string.IsNullOrEmpty(arg))
                {
                    // Validate the arg.
                    if (arg.IndexOfAny(new char[] { '"', '\r', '\n' }) >= 0)
                    {
                        throw new Exception(StringUtil.Loc("InvalidCommandArg", arg));
                    }

                    // Add the arg.
                    formattedArgs.Add(QuotedArgument(arg));
                }
            }

            // Add the common parameters.
            if (_acceptUntrusted)
            {
                formattedArgs.Add("--trust-server-cert");
            }

            if (!string.IsNullOrWhiteSpace(_username))
            {
                formattedArgs.Add("--username");
                formattedArgs.Add(QuotedArgument(_username));
            }

            if (!string.IsNullOrWhiteSpace(_password))
            {
                formattedArgs.Add("--password");
                formattedArgs.Add(QuotedArgument(_password));
            }

            formattedArgs.Add("--non-interactive");

            return string.Join(" ", formattedArgs);
        }

        private string QuotedArgument(string arg)
        {
            char quote = '\"';
            char altQuote = '\'';

            if (arg.IndexOf(quote) > -1)
            {
                quote = '\'';
                altQuote = '\"';
            }

            return (arg.IndexOfAny(new char[] { ' ', altQuote}) ==-1) ? arg : $"{quote}{arg}{quote}";
        }

        private string ToDepthArgument(int depth)
        {
            switch (depth)
            {
            case 0:
                return "empty";
            case 1:
                return "files";
            case 2:
                return "immediates";
            default:
                return "infinity";
            }
        }

        private async Task RunCommandAsync(params string[] args)
        {
            // Validation.
            ArgUtil.NotNull(args, nameof(args));
            ArgUtil.NotNull(_context, nameof(_context));

            // Invoke tf.
            using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
            {
                var outputLock = new object();
                processInvoker.OutputDataReceived += (object sender, DataReceivedEventArgs e) =>
                {
                    lock (outputLock)
                    {
                        _context.Output(e.Data);
                    }
                };
                processInvoker.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
                {
                    lock (outputLock)
                    {
                        _context.Output(e.Data);
                    }
                };
                string arguments = FormatArgumentsWithDefaults(args);
                _context.Command($@"{_svn} {arguments}");
                await processInvoker.ExecuteAsync(
                    workingDirectory: IOUtil.GetWorkPath(HostContext),
                    fileName: _svn,
                    arguments: arguments,
                    environment: null,
                    requireExitCodeZero: true,
                    cancellationToken: _cancellationToken);
            }
        }

        private async Task<string> RunPorcelainCommandAsync(params string[] args)
        {
            // Validation.
            ArgUtil.NotNull(args, nameof(args));
            ArgUtil.NotNull(_context, nameof(_context));

            // Invoke tf.
            using (var processInvoker = HostContext.CreateService<IProcessInvoker>())
            {
                var output = new List<string>();
                var outputLock = new object();
                processInvoker.OutputDataReceived += (object sender, DataReceivedEventArgs e) =>
                {
                    lock (outputLock)
                    {
                        _context.Debug(e.Data);
                        output.Add(e.Data);
                    }
                };
                processInvoker.ErrorDataReceived += (object sender, DataReceivedEventArgs e) =>
                {
                    lock (outputLock)
                    {
                        _context.Debug(e.Data);
                        output.Add(e.Data);
                    }
                };
                string arguments = FormatArgumentsWithDefaults(args);
                _context.Debug($@"{_svn} {arguments}");
                // TODO: Test whether the output encoding needs to be specified on a non-Latin OS.
                try
                {
                    await processInvoker.ExecuteAsync(
                        workingDirectory: IOUtil.GetWorkPath(HostContext),
                        fileName: _svn,
                        arguments: arguments,
                        environment: null,
                        requireExitCodeZero: true,
                        cancellationToken: _cancellationToken);
                }
                catch (ProcessExitCodeException)
                {
                    // The command failed. Dump the output and throw.
                    output.ForEach(x => _context.Output(x ?? string.Empty));
                    throw;
                }

                // Note, string.join gracefully handles a null element within the IEnumerable<string>.
                return string.Join(Environment.NewLine, output);
            }
        }

        public Dictionary<string, SvnMappingDetails> NormalizeMappings(List<SvnMappingDetails> allMappings)
        {
            // We use Ordinal comparer because SVN is case sensetive and keys in the dictionary are URLs.
            Dictionary<string, SvnMappingDetails> distinctMappings = new Dictionary<string, SvnMappingDetails>(StringComparer.Ordinal);

            foreach (SvnMappingDetails map in allMappings)
            {
                string localPath = NormalizeRelativePath(map.LocalPath, Path.DirectorySeparatorChar, '/');
                string serverPath = NormalizeRelativePath(map.ServerPath, '/', '\\');

                if (string.IsNullOrEmpty(serverPath))
                {
                    _context.Debug(StringUtil.Loc("SvnEmptyServerPath", localPath));
                    _context.Debug(StringUtil.Loc("SvnMappingIgnored"));

                    distinctMappings.Clear();
                    distinctMappings.Add(string.Empty, map);
                    break;
                }

                if (string.IsNullOrEmpty(localPath))
                {
                    localPath = NormalizeRelativePath(serverPath, Path.DirectorySeparatorChar, '/');
                }

                if (!distinctMappings.ContainsKey(serverPath))
                {
                    if (!distinctMappings.ContainsValue(map))
                    {
                        distinctMappings.Add(serverPath, map);
                    }
                    else
                    {
                        _context.Debug(StringUtil.Loc("SvnMappingDuplicateLocal", localPath));
                    }
                }
                else
                {
                    _context.Debug(StringUtil.Loc("SvnMappingDuplicateServer", serverPath));
                }
            }

            return distinctMappings;
        }

        public string NormalizeRelativePath(string path, char pathSeparator, char altPathSeparator)
        {
            string relativePath = (path ?? string.Empty).Replace(altPathSeparator, pathSeparator);
            relativePath = relativePath.Trim(pathSeparator, ' ');

            if (relativePath.Contains(":") || relativePath.Contains(".."))
            {
                throw new Exception(StringUtil.Loc("SvnIncorrectRelativePath", relativePath));
            }

            return relativePath;
        }


        // The cancellation token used to stop svn command execution
        private CancellationToken _cancellationToken;

        // The Subversion server endpoint providing URL, username/password, and untrasted certs acceptace information
        private ServiceEndpoint _endpoint;

        // The build commands' execution context
        private IExecutionContext _context;

        // The svn command line utility location
        private string _svn;

        // The svn user name from SVN repository connection endpoint
        private string _username;

        // The svn user password from SVN repository connection endpoint
        private string _password;

        // The acceptUntrustedCerts property from SVN repository connection endpoint
        private bool _acceptUntrusted;
    }

    ////////////////////////////////////////////////////////////////////////////////
    // svn info data objects
    ////////////////////////////////////////////////////////////////////////////////
    [XmlRoot(ElementName = "info", Namespace = "")]
    public sealed class SvnInfo
    {
        [XmlElement(ElementName = "entry", Namespace = "")]
        public SvnInfoEntry[] Entries { get; set; }
    }

    public sealed class SvnInfoEntry
    {
        [XmlAttribute(AttributeName = "kind", Namespace = "")]
        public string Kind { get; set; }

        [XmlAttribute(AttributeName = "path", Namespace = "")]
        public string Path { get; set; }

        [XmlAttribute(AttributeName = "revision", Namespace = "")]
        public string Revision { get; set; }

        [XmlElement(ElementName = "url", Namespace = "")]
        public string Url { get; set; }

        [XmlElement(ElementName = "relative-url", Namespace = "")]
        public string RelativeUrl { get; set; }

        [XmlElement(ElementName = "repository", Namespace = "")]
        public SvnInfoRepository[] Repository { get; set; }

        [XmlElement(ElementName = "wc-info", Namespace = "", IsNullable = true)]
        public SvnInfoWorkingCopy[] WorkingCopyInfo { get; set; }

        [XmlElement(ElementName = "commit", Namespace = "")]
        public SvnInfoCommit Commit { get; set; }
    }

    public sealed class SvnInfoRepository
    {
        [XmlElement(ElementName = "wcroot-abspath", Namespace = "")]
        public string AbsPath { get; set; }

        [XmlElement(ElementName = "schedule", Namespace = "")]
        public string Schedule { get; set; }

        [XmlElement(ElementName = "depth", Namespace = "")]
        public string Depth { get; set; }
    }

    public sealed class SvnInfoWorkingCopy
    {
        [XmlElement(ElementName = "root", Namespace = "")]
        public string Root { get; set; }

        [XmlElement(ElementName = "uuid", Namespace = "")]
        public Guid Uuid { get; set; }
    }

    public sealed class SvnInfoCommit
    {
        [XmlAttribute(AttributeName = "revision", Namespace = "")]
        public string Revision { get; set; }

        [XmlElement(ElementName = "author", Namespace = "")]
        public string Author { get; set; }

        [XmlElement(ElementName = "date", Namespace = "")]
        public string Date { get; set; }
    }
}