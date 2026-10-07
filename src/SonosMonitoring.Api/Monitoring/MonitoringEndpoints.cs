using Devbone.Frameworks.Ddd.Core;
using Devbone.Frameworks.Ddd.Results;
using IResult = Microsoft.AspNetCore.Http.IResult;
using SonosMonitoring.Application.Monitoring;
using SonosMonitoring.Contracts.Monitoring;
using SonosMonitoring.Domain.Monitoring;
using SonosMonitoring.Domain.Monitoring.Disable;
using SonosMonitoring.Domain.Monitoring.Enable;

namespace SonosMonitoring.Api.Monitoring;

/// <summary>Maps monitoring transport contracts to independent domain intentions and explicit projection reads.</summary>
public static class MonitoringEndpoints
{
    /// <summary>Registers the monitoring HTTP slice.</summary>
    /// <param name="app">The API host.</param>
    public static void MapMonitoring(this WebApplication app)
    {
        var group = app.MapGroup("/api/monitoring");
        group.MapPost("/{deviceKey}/enable", EnableAsync);
        group.MapPost("/{deviceKey}/disable", DisableAsync);
        group.MapGet("/{deviceKey}", FindAsync);
    }

    /// <summary>Accepts one retryable activation, returning the confirmed commit position.</summary>
    /// <param name="deviceKey">Device identity from the route.</param>
    /// <param name="request">Activation settings.</param>
    /// <param name="context">HTTP request and scoped services.</param>
    private static async Task<IResult> EnableAsync(string deviceKey, EnableMonitoringRequest request, HttpContext context)
    {
        var error = ValidateRequest(context, deviceKey, request.ExpectedToken, out var requestId, out var token);
        if (error is not null)
        {
            return error;
        }
        var executor = context.RequestServices.GetService<MonitoringExecutor>();
        if (executor is null)
        {
            return Problem("backend.disabled", 503);
        }
        var result = await executor.Handle([new EnableMonitoring(MonitoringIdentity.ForDevice(deviceKey), requestId!, deviceKey, request.IntervalSeconds, token)], context.RequestAborted);
        return MapResult(deviceKey, result);
    }

    /// <summary>Accepts one retryable deactivation of an existing policy.</summary>
    /// <param name="deviceKey">Device identity from the route.</param>
    /// <param name="request">Expected previous token.</param>
    /// <param name="context">HTTP request and scoped services.</param>
    private static async Task<IResult> DisableAsync(string deviceKey, DisableMonitoringRequest request, HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(request.ExpectedToken))
        {
            return Problem("monitoring.token.required", 400);
        }
        var error = ValidateRequest(context, deviceKey, request.ExpectedToken, out var requestId, out var token);
        if (error is not null)
        {
            return error;
        }
        var executor = context.RequestServices.GetService<MonitoringExecutor>();
        if (executor is null)
        {
            return Problem("backend.disabled", 503);
        }
        var result = await executor.Handle([new DisableMonitoring(MonitoringIdentity.ForDevice(deviceKey), requestId!, token)], context.RequestAborted);
        return MapResult(deviceKey, result);
    }

    /// <summary>Reads a projection and reports pending visibility when a requested watermark is not reached.</summary>
    /// <param name="deviceKey">Stable device identity.</param>
    /// <param name="minSequence">Optional position from a commit receipt.</param>
    /// <param name="context">HTTP request and cancellation.</param>
    private static async Task<IResult> FindAsync(string deviceKey, long? minSequence, HttpContext context)
    {
        if (DeviceMonitoring.ValidatePolicy(deviceKey, 15).Success == false || minSequence is < 0)
        {
            return Problem("monitoring.query.invalid", 400);
        }
        var query = context.RequestServices.GetService<IMonitoringQuery>();
        if (query is null)
        {
            return Problem("backend.disabled", 503);
        }
        var view = await query.FindAsync(deviceKey, context.RequestAborted);
        if ((minSequence ?? 0) > (view?.EventSequence ?? 0))
        {
            context.Response.Headers.RetryAfter = "1";
            return Problem("projection.pending", 202);
        }
        return view is null ? Problem("monitoring.not_found", 404) :
            Results.Ok(new MonitoringResponse(view.AggregateId, view.DeviceKey, view.Enabled, view.IntervalSeconds, view.EventSequence, view.UpdatedAtUtc)
            {
                ConcurrencyToken = context.RequestServices.GetRequiredService<MonitoringTokenReader>().Read(deviceKey, view.EventSequence),
            });
    }

    /// <summary>Checks transport preconditions without duplicating the aggregate's interval rule.</summary>
    /// <param name="context">Incoming headers.</param>
    /// <param name="deviceKey">Target identity.</param>
    /// <param name="expected">Optional serialized concurrency token.</param>
    /// <param name="requestId">Validated retry identity.</param>
    /// <param name="token">Validated expected token.</param>
    private static IResult? ValidateRequest(HttpContext context, string deviceKey, string? expected, out RequestId? requestId, out AggregateConcurrencyToken? token)
    {
        requestId = null;
        token = null;
        if (DeviceMonitoring.ValidatePolicy(deviceKey, 15).Success == false)
        {
            return Problem("monitoring.device.invalid", 400);
        }
        if (Guid.TryParseExact(context.Request.Headers["Idempotency-Key"].ToString(), "D", out var key) == false || key == Guid.Empty)
        {
            return Problem("request.idempotency_key.invalid", 400);
        }
        if (expected is not null && AggregateConcurrencyToken.TryParse(expected, out token) == false)
        {
            return Problem("monitoring.token.invalid", 400);
        }
        requestId = RequestId.With(key);
        return null;
    }

    /// <summary>Exposes safe error codes and commit metadata, never an internal exception.</summary>
    /// <param name="deviceKey">Device used to build the read URL.</param>
    /// <param name="result">Framework command result.</param>
    private static IResult MapResult(string deviceKey, IAggregateModificationResult result)
    {
        if (result.Success == false)
        {
            var error = result.Errors.First();
            return Problem(error.Code, error.Category switch
            {
                DomainErrorCategory.Validation => 400,
                DomainErrorCategory.Conflict or DomainErrorCategory.Concurrency => 409,
                DomainErrorCategory.Authorization => 403,
                DomainErrorCategory.NotFound => 404,
                _ => 503,
            });
        }
        var receipt = result.CommitReceipt ?? throw new InvalidOperationException("A successful monitoring action must return a commit receipt.");
        var url = $"/api/monitoring/{Uri.EscapeDataString(deviceKey)}?minSequence={receipt.EventSequence}";
        return Results.Accepted(url, new MonitoringAccepted(receipt.AggregateId, receipt.AggregateConcurrencyToken.ToString(),
            receipt.EventSequence, receipt.AlreadyCommitted, url));
    }

    /// <summary>Produces a stable, language-independent ProblemDetails response.</summary>
    /// <param name="code">Machine-readable error or wait code.</param>
    /// <param name="status">HTTP status.</param>
    private static IResult Problem(string code, int status) => Results.Problem(statusCode: status, title: code, extensions: new Dictionary<string, object?> { ["code"] = code });
}
