using Microsoft.AspNetCore.Components;
using SonosMonitoring.Web.Connection;

namespace SonosMonitoring.Web.Components.Shared;

/// <summary>Refreshes a visible live page sequentially and cancels polling when navigation disposes it.</summary>
public abstract class LivePageBase : ComponentBase, IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _polling;

    /// <summary>Gets or sets the scoped HTTP data boundary.</summary>
    [Inject] protected MonitoringApiClient Api { get; set; } = null!;
    /// <summary>Gets the page cancellation token.</summary>
    protected CancellationToken Lifetime => this._lifetime.Token;
    /// <summary>Gets whether a refresh is already running.</summary>
    protected bool Busy { get; private set; }
    /// <summary>Gets or sets the last safe failure category.</summary>
    protected string? Error { get; set; }
    /// <summary>Gets or sets whether periodic refresh is enabled.</summary>
    protected bool AutoRefresh { get; set; } = true;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await this.RefreshAsync();
        this._polling = this.PollAsync();
    }

    /// <summary>Loads one page's data without overlapping user-initiated and timer refreshes.</summary>
    protected async Task RefreshAsync()
    {
        if (this.Busy || this._lifetime.IsCancellationRequested)
        {
            return;
        }
        this.Busy = true;
        try
        {
            await this.LoadAsync();
        }
        finally
        {
            this.Busy = false;
        }
    }

    /// <summary>Reads the contracts needed by the current page.</summary>
    protected abstract Task LoadAsync();

    /// <summary>Waits between completed requests instead of issuing overlapping requests.</summary>
    private async Task PollAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            while (await timer.WaitForNextTickAsync(this.Lifetime))
            {
                if (this.AutoRefresh)
                {
                    await this.InvokeAsync(async () => { await this.RefreshAsync(); this.StateHasChanged(); });
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Navigation ends the page's timer and any in-flight HTTP read.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await this._lifetime.CancelAsync();
        if (this._polling is not null)
        {
            await this._polling;
        }
        this._lifetime.Dispose();
        GC.SuppressFinalize(this);
    }
}
