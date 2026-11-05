using System.Diagnostics;
using GpuComputeDashboard.Models;

namespace GpuComputeDashboard.Services;

/// <summary>Runs a fixed executable with an argument list; never invokes a shell.</summary>
public sealed class ProcessRunner
{
    public async Task<CommandResult> RunAsync(string executable, IEnumerable<string> args,
        CancellationToken cancellationToken = default, int timeoutSeconds = 5)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            return new(process.ExitCode, await outputTask, await errorTask);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
            or OperationCanceledException or InvalidOperationException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            return new(-1, "", ex is OperationCanceledException ? "Command timed out or was cancelled." : ex.Message);
        }
    }
}
