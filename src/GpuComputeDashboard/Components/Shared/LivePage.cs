using Microsoft.AspNetCore.Components;

namespace GpuComputeDashboard.Components.Shared;

/// <summary>Lightweight 5-second UI refresh; telemetry is sampled centrally by a hosted service.</summary>
public abstract class LivePage : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource stop = new();

    protected override async Task OnInitializedAsync()
    {
        await RefreshAsync();
        _ = UpdateLoopAsync();
    }

    protected virtual Task RefreshAsync() => Task.CompletedTask;

    private async Task UpdateLoopAsync()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stop.Token);
                await RefreshAsync();
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose() { stop.Cancel(); stop.Dispose(); GC.SuppressFinalize(this); }
}
