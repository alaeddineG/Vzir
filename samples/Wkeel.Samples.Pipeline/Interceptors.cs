using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Wkeel.Samples.Pipeline;

/// <summary>Open generic with no constraints, so it wraps every executed message.</summary>
internal sealed class LoggingInterceptor<TMessage, TResult>(ILogger<TMessage> log) : IInterceptor<TMessage, TResult>
{
    public async Task<TResult> Intercept(TMessage message, Next<TResult> next, CancellationToken ct)
    {
        log.LogInformation("Executing {Message}", message);
        var result = await next();
        log.LogInformation("Executed {Message}", typeof(TMessage).Name);
        return result;
    }
}

/// <summary>Implemented by messages that can check themselves.</summary>
public interface IValidated
{
    /// <summary>Returns what is wrong with the message, or null when it is valid.</summary>
    string? Validate();
}

public sealed class ValidationException(string message) : Exception(message);

/// <summary>The constraint scopes this to <see cref="IValidated"/> messages. Others skip it without any registration.</summary>
internal sealed class ValidationInterceptor<TMessage, TResult> : IInterceptor<TMessage, TResult>
    where TMessage : IValidated
{
    public Task<TResult> Intercept(TMessage message, Next<TResult> next, CancellationToken ct) =>
        message.Validate() is { } problem ? throw new ValidationException(problem) : next();
}

internal sealed class PatientCache
{
    public ConcurrentDictionary<Guid, Patient> Entries { get; } = new();
}

/// <summary>Closed over one request. Short-circuits on a hit by not calling next.</summary>
internal sealed class GetPatientCache(PatientCache cache, ILogger<GetPatientCache> log) : IInterceptor<GetPatient, Patient>
{
    public async Task<Patient> Intercept(GetPatient message, Next<Patient> next, CancellationToken ct)
    {
        if (cache.Entries.TryGetValue(message.Id, out var cached))
        {
            log.LogInformation("Cache hit for {Id}", message.Id);
            return cached;
        }

        return cache.Entries[message.Id] = await next();
    }
}

internal sealed class AuditTrail
{
    public ConcurrentQueue<string> Entries { get; } = new();
}

/// <summary>Closed over one operation. Operations have no result, so this uses the dedicated interface.</summary>
internal sealed class ArchivePatientAudit(AuditTrail audit, ILogger<ArchivePatientAudit> log) : IOperationInterceptor<ArchivePatient>
{
    public async Task Intercept(ArchivePatient operation, Next next, CancellationToken ct)
    {
        await next();
        audit.Entries.Enqueue($"{operation.Id}: {operation.Reason}");
        log.LogInformation("Audited archive of {Id}", operation.Id);
    }
}
