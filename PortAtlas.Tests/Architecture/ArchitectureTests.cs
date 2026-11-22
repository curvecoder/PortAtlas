using AwesomeAssertions;
using Mono.Cecil;
using NetArchTest.Rules;
using PortAtlas.Core.Models;
using Xunit;

namespace PortAtlas.Tests.Architecture;

public sealed class ArchitectureTests
{
    [Fact]
    public void Dependencies_WhenCheckingCoreAssembly_DoesNotDependOnCli()
    {
        // Arrange
        var coreAssembly = typeof(NetworkAuditEntry).Assembly;

        // Act
        var result = Types.InAssembly(coreAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("PortAtlas.Cli")
            .GetResult();

        // Assert
        result.IsSuccessful.Should().BeTrue();
    }

    // Read from the IL rather than the sources: a call hidden in a lambda, an iterator or a property initializer is
    // found as well, in the code the compiler actually emits. Under coverage, Coverlet adds its own tracker type to the
    // assembly, which reads the clock: only the types of the project are inspected.
    [Fact]
    public void CoreAssembly_WhenInspectingItsCalls_ReadsTheClockOnlyThroughTimeProvider()
    {
        // Arrange
        string[] clockTypes = ["System.DateTime", "System.DateTimeOffset"];
        string[] clockGetters = ["get_Now", "get_UtcNow", "get_Today"];
        using var module = ModuleDefinition.ReadModule(typeof(NetworkAuditEntry).Assembly.Location);

        // Act
        var callers = module.GetTypes()
            .Where(type => type.FullName.StartsWith("PortAtlas.", StringComparison.Ordinal))
            .SelectMany(type => type.Methods)
            .Where(method => method.HasBody && method.Body.Instructions.Any(instruction =>
                instruction.Operand is MethodReference called &&
                clockTypes.Contains(called.DeclaringType.FullName) &&
                clockGetters.Contains(called.Name)))
            .Select(method => method.FullName)
            .ToList();

        // Assert
        callers.Should().BeEmpty("the core reads the time through an injected TimeProvider, so that tests can set it");
    }

    [Fact]
    public void NamingConvention_WhenInspectingCoreTypes_DoesNotEndWithForbiddenGenericSuffixes()
    {
        // Arrange
        var forbiddenSuffixes = new[] { "Helper", "Helpers", "Utils", "Util", "Manager", "Common" };
        var types = typeof(NetworkAuditEntry).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.Name.StartsWith("<", StringComparison.Ordinal))
            .ToList();

        // Act & Assert
        types.Should().NotBeEmpty();
        foreach (var type in types)
        {
            foreach (var suffix in forbiddenSuffixes)
            {
                type.Name.Should().NotEndWith(suffix, $"Class '{type.FullName}' violates Rule 3 (No generic suffixes).");
            }
        }
    }

    [Fact]
    public void NamingConvention_WhenInspectingAllTypes_DoesNotContainAiBuzzwords()
    {
        // Arrange
        var aiBuzzwords = new[] { "Robust", "Seamless", "Smart", "Resilient", "Advanced", "Comprehensive", "Ultimate" };
        var types = typeof(NetworkAuditEntry).Assembly.GetTypes()
            .Where(t => !t.Name.StartsWith("<", StringComparison.Ordinal))
            .ToList();

        // Act & Assert
        types.Should().NotBeEmpty();
        foreach (var type in types)
        {
            foreach (var buzzword in aiBuzzwords)
            {
                type.Name.Should().NotContain(buzzword, $"Type '{type.FullName}' violates Rule 6 (No AI buzzwords).");
            }
        }
    }

    [Fact]
    public void NamingConvention_WhenInspectingTestMethods_FollowsStrictThreePartNamingPattern()
    {
        // Arrange
        var testAssembly = typeof(ArchitectureTests).Assembly;
        var testMethods = testAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            .Where(m => m.GetCustomAttributes(typeof(FactAttribute), inherit: true).Length > 0 ||
                        m.GetCustomAttributes(typeof(TheoryAttribute), inherit: true).Length > 0)
            .ToList();

        // Act & Assert
        testMethods.Should().NotBeEmpty();
        foreach (var method in testMethods)
        {
            var parts = method.Name.Split('_');
            parts.Length.Should().Be(3, $"Test method '{method.DeclaringType?.Name}.{method.Name}' must strictly follow 'Method_Condition_ExpectedResult' pattern (Rule 7).");
        }
    }
}
