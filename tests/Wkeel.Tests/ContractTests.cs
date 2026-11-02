namespace Wkeel.Tests;

public sealed class ContractTests
{
    // Coverage instrumentation adds a netstandard reference, which is not a package dependency.
    private static bool IsRuntime(string assembly) =>
        assembly == "netstandard" || assembly.StartsWith("System.", StringComparison.Ordinal);

    [Fact]
    public void CancellationToken_is_required_on_every_verb_and_handler_method()
    {
        Type[] types =
        [
            typeof(IWkeel),
            typeof(IOperationHandler<>),
            typeof(IRequestHandler<,>),
            typeof(INoticeHandler<>),
            typeof(IInterceptor<,>),
            typeof(IOperationInterceptor<>),
        ];
        var methods = types.SelectMany(type => type.GetMethods()).ToArray();

        Assert.NotEmpty(methods);
        Assert.All(methods, method =>
        {
            var token = Assert.Single(method.GetParameters(), parameter => parameter.ParameterType == typeof(CancellationToken));
            Assert.False(token.HasDefaultValue);
        });
    }

    [Fact]
    public void Abstractions_depend_on_nothing_but_the_runtime()
    {
        var references = typeof(IWkeel).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name!);

        Assert.All(references, name => Assert.True(IsRuntime(name), $"Unexpected dependency: {name}"));
    }

    [Fact]
    public void Runtime_depends_only_on_abstractions_and_DI_abstractions()
    {
        var references = typeof(WkeelBuilder).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name!);

        Assert.All(references, name => Assert.True(
            IsRuntime(name) || name is "Wkeel.Abstractions" or "Microsoft.Extensions.DependencyInjection.Abstractions",
            $"Unexpected dependency: {name}"));
    }

    [Fact]
    public void Abstractions_stay_under_fifteen_public_types() =>
        Assert.True(typeof(IWkeel).Assembly.GetExportedTypes().Length < 15);

    [Fact]
    public void Runtime_exposes_only_its_registration_surface() =>
        Assert.Equal(
            [typeof(NoticeOptions), typeof(WkeelBuilder), typeof(WkeelServiceCollectionExtensions)],
            typeof(WkeelBuilder).Assembly.GetExportedTypes().OrderBy(type => type.Name, StringComparer.Ordinal));
}
