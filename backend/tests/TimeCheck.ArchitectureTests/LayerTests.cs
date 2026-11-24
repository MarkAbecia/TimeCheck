using System.Reflection;
using AwesomeAssertions;
using NetArchTest.Rules;

namespace TimeCheck.ArchitectureTests;

public class LayerTests
{
    private static readonly Assembly Domain = typeof(TimeCheck.Domain.Common.BaseEntity).Assembly;
    private static readonly Assembly Application = typeof(TimeCheck.Application.Common.Dispatcher).Assembly;
    private static readonly Assembly Infrastructure = typeof(TimeCheck.Infrastructure.DependencyInjection).Assembly;

    [Fact]
    public void Domain_has_no_dependencies_on_other_layers_or_frameworks()
    {
        var result = Types.InAssembly(Domain).ShouldNot()
            .HaveDependencyOnAny("TimeCheck.Application", "TimeCheck.Infrastructure", "TimeCheck.Api", "TimeCheck.Worker",
                "Microsoft.EntityFrameworkCore", "Npgsql", "Microsoft.AspNetCore")
            .GetResult();
        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_hosts()
    {
        var result = Types.InAssembly(Application).ShouldNot()
            .HaveDependencyOnAny("TimeCheck.Infrastructure", "TimeCheck.Api", "TimeCheck.Worker", "Npgsql", "StackExchange.Redis", "Microsoft.AspNetCore", "Hangfire")
            .GetResult();
        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_hosts()
    {
        var result = Types.InAssembly(Infrastructure).ShouldNot()
            .HaveDependencyOnAny("TimeCheck.Api", "TimeCheck.Worker", "Microsoft.AspNetCore")
            .GetResult();
        result.IsSuccessful.Should().BeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Domain_entities_have_no_public_setters()
    {
        var offenders = Domain.GetTypes()
            .Where(t => t.Namespace == "TimeCheck.Domain.Entities" && !t.IsNested) // nested records are value objects
            .SelectMany(t => t.GetProperties().Where(p => p.SetMethod?.IsPublic == true).Select(p => $"{t.Name}.{p.Name}"))
            .ToList();
        offenders.Should().BeEmpty();
    }

    [Fact]
    public void Mvc_controllers_are_not_used_so_auth_rules_stay_in_one_place()
    {
        // Endpoints are minimal APIs; the anonymous-endpoint rule is checked against the live endpoint table
        // in TimeCheck.Api.IntegrationTests (AuthorizationCoverageTests).
        Types.InAssembly(typeof(TimeCheck.Api.Endpoints.Endpoints).Assembly).That().HaveNameEndingWith("Controller").GetTypes().Should().BeEmpty();
    }
}
