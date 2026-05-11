using System.Diagnostics;
using Xunit;

namespace AgentHost.Indexing.Tests.Integration;

internal static class DockerTestHelper
{
    public static bool IsDockerAvailable()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = "version --format \"{{.Server.Version}}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
            {
                return false;
            }

            if (!process.WaitForExit(10000) || process.ExitCode != 0)
            {
                return false;
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

internal sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!DockerTestHelper.IsDockerAvailable())
        {
            Skip = "Requires Docker.";
        }
    }
}
