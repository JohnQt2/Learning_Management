using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;

namespace LMS.Core.API.Configuration;

public static class DotEnvConfiguration
{
    public static void AddLocalDotEnv(this WebApplicationBuilder builder)
    {
        if (!builder.Configuration.GetValue("DotEnv:Enabled", true)) return;
        var root = new DirectoryInfo(builder.Environment.ContentRootPath);
        var path = Path.Combine(root.FullName, ".env");
        for (var directory = root; directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "BE", "LMS.sln")) &&
                File.Exists(Path.Combine(directory.FullName, "FE", "package.json")))
            {
                path = Path.Combine(directory.FullName, ".env");
                break;
            }
        if (!File.Exists(path)) return;
        // Keep process environment and command-line overrides above file values.
        var sources = builder.Configuration.Sources;
        var index = sources.ToList().FindIndex(source => source is EnvironmentVariablesConfigurationSource env && env.Prefix is null);
        sources.Insert(index < 0 ? sources.Count : index, new MemoryConfigurationSource { InitialData = Parse(File.ReadAllLines(path)) });
    }

    public static IDictionary<string, string?> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var number = 0;
        foreach (var source in lines)
        {
            number++;
            var line = source.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();
            var separator = line.IndexOf('=');
            if (separator <= 0) throw InvalidLine(number);
            var key = line[..separator].Trim();
            if (!(char.IsAsciiLetter(key[0]) || key[0] == '_') || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
                throw InvalidLine(number);
            // Other entries may be consumed by Compose; never import VITE_* into the API.
            var value = line[(separator + 1)..].Trim();
            if (value.StartsWith('"') || value.StartsWith('\''))
            {
                var quote = value[0];
                var end = value.IndexOf(quote, 1);
                if (end < 0 || (value[(end + 1)..].Trim() is var tail && tail.Length > 0 && !tail.StartsWith('#')))
                    throw InvalidLine(number);
                value = value[1..end];
            }
            else
            {
                var comment = value.IndexOf(" #", StringComparison.Ordinal);
                if (comment >= 0) value = value[..comment].TrimEnd();
            }
            if (!key.StartsWith("VITE_", StringComparison.OrdinalIgnoreCase)) values[key.Replace("__", ":")] = value;
        }
        return values;
    }

    private static InvalidOperationException InvalidLine(int line) => new($"Invalid .env syntax on line {line}. Use NAME=value; secret values are not printed.");
}
