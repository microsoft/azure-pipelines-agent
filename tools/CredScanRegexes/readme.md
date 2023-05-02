# CredScan Regex Extractor

CredScan, an internal Microsoft tool, detects credentials using a variety of mechanisms.
Its goal is to keep those credentials from ending up in the wrong place: a Git repository, test logs, etc.
It would be perfect for helping scrub build & release logs.

CredScan is not available publicly, so we cannot ship it with the Pipelines agent.
We have secured permission to ship a small subset of CredScan -- its set of battle-tested regexes for detecting common credential formats.
This tool extracts the regexes from CredScan's knowledge base and outputs them in a format suitable for shipping with the agent.

Requirements:
- .NET 6.0 or higher
- The [Azure Artifacts credential provider](https://github.com/Microsoft/artifacts-credprovider)
- Access to the "msazure" org's [CredScanSDK feed](https://msazure.visualstudio.com/One/_packaging?_a=feed&feed=CredScanSDK)

To run this tool:
- Extract the credential provider to the right place on your system
- `dotnet build --interactive` (so you get prompted to auth to the feed)
- `dotnet run > ../../src/Microsoft.VisualStudio.Services.Agent/AdditionalMaskingRegexes.CredScan.cs`

You **must** check in the generated code after running this tool.
At the time of writing, the target file is `src/Microsoft.VisualStudio.Services.Agent/AdditionalMaskingRegexes.CredScan.cs`.
