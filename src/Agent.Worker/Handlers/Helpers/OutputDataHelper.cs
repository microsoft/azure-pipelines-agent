using System.Text.RegularExpressions;

namespace Agent.Worker.Handlers.Helpers;

public static class OutputDataHelper
{
    public static string RemoveAnsiColorsFromLine(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        return AnsiColorsRegex.Replace(input, string.Empty);
    }

    private static readonly Regex AnsiColorsRegex = new(@"\u001b\[[^m]*m", RegexOptions.Compiled);
}
