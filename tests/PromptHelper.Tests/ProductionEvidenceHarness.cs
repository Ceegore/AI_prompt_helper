using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PromptHelper.Services;

namespace PromptHelper.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class ProductionSymbolEvidenceAttribute(string symbol) : Attribute
{
    public string Symbol { get; } = symbol;
}

/// <summary>
/// Wraps every test method in the class. Tests without evidence metadata execute unchanged;
/// attributed tests automatically fail after execution unless every declared production
/// symbol was observed at runtime.
/// </summary>
internal sealed class ProductionEvidenceTestClassAttribute : TestClassAttribute
{
    public override TestMethodAttribute GetTestMethodAttribute(TestMethodAttribute testMethodAttribute) =>
        new AutomaticProductionEvidenceTestMethodAttribute(testMethodAttribute);
}

#pragma warning disable MSTEST0057 // Constructed by the class wrapper; never applied at a source location.
internal sealed class AutomaticProductionEvidenceTestMethodAttribute : TestMethodAttribute
{
    private readonly TestMethodAttribute _inner;

    internal AutomaticProductionEvidenceTestMethodAttribute(TestMethodAttribute inner)
    {
        _inner = inner;
    }

    public override async Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        string[] required = testMethod.MethodInfo
            .GetCustomAttributes(typeof(ProductionSymbolEvidenceAttribute), inherit: true)
            .Cast<ProductionSymbolEvidenceAttribute>()
            .Select(attribute => attribute.Symbol)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (required.Length == 0)
        {
            return await _inner.ExecuteAsync(testMethod).ConfigureAwait(false);
        }

        var hits = new HashSet<string>(StringComparer.Ordinal);
        Action<string>? previous = ProductionRuntimeEvidence.SinkForTests;
        ProductionRuntimeEvidence.SinkForTests = symbol =>
        {
            hits.Add(symbol);
            previous?.Invoke(symbol);
        };

        TestResult[] results;
        try
        {
            results = await _inner.ExecuteAsync(testMethod).ConfigureAwait(false);
        }
        finally
        {
            ProductionRuntimeEvidence.SinkForTests = previous;
        }

        string[] missing = required.Where(symbol => !hits.Contains(symbol)).ToArray();
        if (missing.Length == 0)
        {
            return results;
        }

        string message =
            $"Automatic production evidence failed for '{testMethod.TestMethodName}'. " +
            $"Missing runtime hit(s): {string.Join(", ", missing)}.";
        foreach (TestResult result in results)
        {
            result.Outcome = UnitTestOutcome.Failed;
            result.TestFailureException = new AssertFailedException(message);
        }

        return results;
    }
}
#pragma warning restore MSTEST0057
