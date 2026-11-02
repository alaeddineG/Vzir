using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Wkeel;
using Wkeel.Samples.Pipeline;

await using var provider = new ServiceCollection()
    .AddLogging(logging => logging.AddSimpleConsole(console => console.SingleLine = true))
    .AddSingleton<PatientStore>()
    .AddSingleton<PatientCache>()
    .AddSingleton<AuditTrail>()
    .AddWkeel(w => w
        .ScanAssemblyContaining<GetPatient>()            // finds both handlers, internal ones included
        .Intercept(typeof(LoggingInterceptor<,>))        // open generic, every message, outermost
        .Intercept(typeof(ValidationInterceptor<,>))     // open generic, only IValidated messages
        .Intercept<GetPatient, Patient, GetPatientCache>()    // closed, one request
        .Intercept<ArchivePatient, ArchivePatientAudit>()     // closed, one operation
        .ValidateOnBuild())                              // a missing or duplicate handler fails here
    .BuildServiceProvider();

await using var scope = provider.CreateAsyncScope();
var wkeel = scope.ServiceProvider.GetRequiredService<IWkeel>();
var ct = CancellationToken.None;
var id = provider.GetRequiredService<PatientStore>().Add("Amina");

// The first read reaches the handler, the second is answered by GetPatientCache.
await wkeel.Execute(new GetPatient(id), ct);
await wkeel.Execute(new GetPatient(id), ct);

// Passes validation, gets audited and reaches the handler.
await wkeel.Execute(new ArchivePatient(id, "Moved abroad"), ct);

// Stopped by ValidationInterceptor: nothing inside it runs.
try
{
    await wkeel.Execute(new ArchivePatient(id, Reason: ""), ct);
}
catch (ValidationException exception)
{
    scope.ServiceProvider.GetRequiredService<ILogger<Program>>().LogWarning("Rejected: {Reason}", exception.Message);
}
