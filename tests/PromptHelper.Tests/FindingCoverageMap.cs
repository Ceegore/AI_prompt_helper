using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PromptHelper.Tests;

/// <summary>
/// The canonical finding-to-test coverage authority (tools/FindingCoverageMap.json).
/// </summary>
/// <remarks>
/// CRUU15-009: a sentinel list cannot prove its own completeness. This map is checked against
/// an authority outside itself — the finding IDs named by the checked-in audit reports — so a
/// finding that loses its coverage fails the gate instead of disappearing quietly.
/// </remarks>
internal sealed record FindingCoverageMap(
    IReadOnlyDictionary<string, string> GatedReports,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Findings,
    IReadOnlyDictionary<string, IReadOnlyList<string>> RequiredProductionSymbols)
{
    public static string MapPath => RepositoryTestPaths.RequireFile("tools", "FindingCoverageMap.json");

    public static FindingCoverageMap Load() => Parse(File.ReadAllText(MapPath));

    public static FindingCoverageMap Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        var reports = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in root.GetProperty("gatedReports").EnumerateObject())
        {
            reports[property.Name] = property.Value.GetString()
                ?? throw new InvalidDataException($"gatedReports.{property.Name} is not a string.");
        }

        var findings = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (JsonProperty property in root.GetProperty("findings").EnumerateObject())
        {
            var tests = new List<string>();
            foreach (JsonElement test in property.Value.EnumerateArray())
            {
                tests.Add(test.GetString()
                    ?? throw new InvalidDataException($"findings.{property.Name} contains a non-string entry."));
            }

            findings[property.Name] = tests;
        }

        var requiredSymbols = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (root.TryGetProperty("requiredProductionSymbols", out JsonElement symbolsRoot))
        {
            foreach (JsonProperty property in symbolsRoot.EnumerateObject())
            {
                requiredSymbols[property.Name] = property.Value.EnumerateArray()
                    .Select(value => value.GetString()
                        ?? throw new InvalidDataException(
                            $"requiredProductionSymbols.{property.Name} contains a non-string entry."))
                    .ToArray();
            }
        }

        return new FindingCoverageMap(reports, findings, requiredSymbols);
    }

    /// <summary>
    /// The finding IDs that require coverage, read from the audit reports themselves rather
    /// than from this map — the whole point of the gate.
    /// </summary>
    public IReadOnlySet<string> ReadRequiredFindingIdsFromReports()
    {
        var required = new SortedSet<string>(StringComparer.Ordinal);

        foreach (KeyValuePair<string, string> report in GatedReports)
        {
            string reportPath = Path.Combine(RepositoryTestPaths.Root, report.Value);
            string text = File.ReadAllText(reportPath);

            foreach (System.Text.RegularExpressions.Match match in
                     System.Text.RegularExpressions.Regex.Matches(text, $@"{report.Key}-\d{{3}}"))
            {
                required.Add(match.Value);
            }
        }

        return required;
    }
}
