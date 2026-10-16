// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text;

namespace Agent.Sdk
{
    public sealed class ExternalOutputOptions
    {
        public bool EnableVsoCommands { get; set; }  // Indicates whether allowed VSO commands are enabled or all VSO commands are blocked
        public IReadOnlyCollection<string> AllowedVsoCommands { get; set; }  // Specifies the list of allowed VSO commands
    }

    public static class ExternalOutputFilter
    {
        private const string Marker = "##vso[";
        private const string NeutralizedMarker = "##_vso[";
        private const int MaxCommandHeaderLength = 256;

        public static IReadOnlyCollection<string> DefaultAllowedVsoCommands { get; } =
            Array.AsReadOnly(new[] { "task.debug", "task.setprogress" });

        public static string Filter(string line, ExternalOutputOptions options)
        {
            if (line == null)
            {
                line = string.Empty;
            }

            if (options == null)
            {
                options = new ExternalOutputOptions();
            }

            HashSet<string> allowedCommands = ResolveAllowedCommands(options);
            StringBuilder filtered = null;
            int position = 0;

            while (position < line.Length)
            {
                int markerIndex = line.IndexOf(Marker, position, StringComparison.Ordinal);
                if (markerIndex < 0)
                {
                    break;
                }

                if (filtered == null)
                {
                    filtered = new StringBuilder(line.Length + 1);
                }

                filtered.Append(line, position, markerIndex - position);

                int commandStart = markerIndex + Marker.Length;
                filtered.Append(
                    options.EnableVsoCommands && IsCommandAllowed(line, commandStart, allowedCommands)
                        ? Marker
                        : NeutralizedMarker);

                position = commandStart;
            }

            if (filtered == null)
            {
                return line;
            }

            filtered.Append(line, position, line.Length - position);
            return filtered.ToString();
        }

        private static HashSet<string> ResolveAllowedCommands(ExternalOutputOptions options)
        {
            if (!options.EnableVsoCommands)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            IReadOnlyCollection<string> commands =
                options.AllowedVsoCommands ?? DefaultAllowedVsoCommands;

            return new HashSet<string>(commands, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsCommandAllowed(
            string line,
            int commandStart,
            HashSet<string> allowedCommands)
        {
            int commandEnd = -1;

            for (int index = commandStart; index < line.Length; index++)
            {
                char current = line[index];
                if (current == '\r' || current == '\n')
                {
                    return false;
                }

                if (current == ' ' || current == ']')
                {
                    commandEnd = index;
                    break;
                }
            }

            if (commandEnd < 0)
            {
                return false;
            }

            string rawCommand = line.Substring(commandStart, commandEnd - commandStart);
            if (Encoding.UTF8.GetByteCount(rawCommand) > MaxCommandHeaderLength)
            {
                return false;
            }

            string[] parts = rawCommand.Split(
                new[] { '.' },
                StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
            {
                return false;
            }

            return allowedCommands.Contains($"{parts[0]}.{parts[1]}");
        }
    }
}
