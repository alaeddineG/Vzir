using System.Collections.Concurrent;

namespace Wkeel.Samples.Pipeline;

public sealed record Patient(Guid Id, string Name, bool Archived);

public sealed record GetPatient(Guid Id) : IRequest<Patient>;

public sealed record ArchivePatient(Guid Id, string Reason) : IOperation, IValidated
{
    public string? Validate() => string.IsNullOrWhiteSpace(Reason) ? "An archive needs a reason." : null;
}

internal sealed class PatientStore
{
    private readonly ConcurrentDictionary<Guid, Patient> _patients = new();

    public Guid Add(string name)
    {
        var id = Guid.NewGuid();
        _patients[id] = new Patient(id, name, Archived: false);
        return id;
    }

    public Patient Get(Guid id) => _patients[id];

    public void Archive(Guid id) => _patients[id] = _patients[id] with { Archived = true };
}

internal sealed class GetPatientHandler(PatientStore store) : IRequestHandler<GetPatient, Patient>
{
    public Task<Patient> Handle(GetPatient request, CancellationToken ct) => Task.FromResult(store.Get(request.Id));
}

internal sealed class ArchivePatientHandler(PatientStore store) : IOperationHandler<ArchivePatient>
{
    public Task Handle(ArchivePatient operation, CancellationToken ct)
    {
        store.Archive(operation.Id);
        return Task.CompletedTask;
    }
}
