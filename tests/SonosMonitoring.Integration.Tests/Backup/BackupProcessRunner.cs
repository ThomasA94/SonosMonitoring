using System.Diagnostics;
using System.Text;

namespace SonosMonitoring.Integration.Tests.Backup;

/// <summary>Runs backup tools with argument-safe process creation and bounded output capture.</summary>
internal static class BackupProcessRunner
{
    private const int MAXIMUM_CAPTURED_CHARACTERS = 8192;

    /// <summary>Runs one external process and reports bounded diagnostics on failure.</summary>
    /// <param name="fileName">Executable name resolved by the operating system.</param>
    /// <param name="arguments">Arguments passed individually without shell interpretation.</param>
    /// <param name="workingDirectory">Repository root used as the process working directory.</param>
    /// <param name="timeout">Maximum process runtime.</param>
    /// <returns>Bounded standard output from a successful process.</returns>
    /// <exception cref="InvalidOperationException">The process could not start or returned a nonzero code.</exception>
    /// <exception cref="TimeoutException">The process exceeded its runtime budget.</exception>
    public static async Task<string> RunCheckedAsync(string fileName, IEnumerable<string> arguments,
        string workingDirectory, TimeSpan timeout)
    {
        ProcessStartInfo startInfo = new(fileName)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = new() { StartInfo = startInfo };
        if (process.Start() == false)
        {
            throw new InvalidOperationException($"Could not start required process '{fileName}'.");
        }

        Task<string> standardOutput = ReadBoundedAsync(process.StandardOutput);
        Task<string> standardError = ReadBoundedAsync(process.StandardError);
        using CancellationTokenSource budget = new(timeout);
        try
        {
            await process.WaitForExitAsync(budget.Token);
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw new TimeoutException($"Process '{fileName}' exceeded the {timeout.TotalSeconds:0}-second backup test budget.");
        }

        string output = await standardOutput;
        string error = await standardError;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Process '{fileName}' exited with code {process.ExitCode}. Output: {output} Error: {error}");
        }

        return output;
    }

    /// <summary>Consumes the full stream while retaining only a bounded diagnostic prefix.</summary>
    /// <param name="reader">Redirected process output stream.</param>
    /// <returns>A bounded text excerpt with a marker when output was truncated.</returns>
    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        StringBuilder captured = new();
        char[] buffer = new char[1024];
        bool truncated = false;
        while (true)
        {
            int count = await reader.ReadAsync(buffer);
            if (count == 0)
            {
                break;
            }

            int remaining = MAXIMUM_CAPTURED_CHARACTERS - captured.Length;
            if (remaining > 0)
            {
                captured.Append(buffer, 0, Math.Min(count, remaining));
            }
            if (count > remaining)
            {
                truncated = true;
            }
        }

        return truncated ? captured.Append(" [output truncated]").ToString() : captured.ToString();
    }
}
