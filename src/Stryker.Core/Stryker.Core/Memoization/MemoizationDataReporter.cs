using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Stryker.Abstractions;
using Stryker.Abstractions.Memoization;
using Stryker.Abstractions.Options;
using Stryker.Abstractions.ProjectComponents;
using Stryker.Abstractions.Reporting;
using Stryker.Core.Reporters.Json;

namespace Stryker.Core.Memoization;

public class MemoizationDataReporter : IReporter
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Converters = {
        //     new SourceFileConverter(),
        //     new JsonMutantConverter(),
        //     new LocationConverter(),
        //     new PositionConverter(),
        //     new JsonTestFileConverter(),
        //     new JsonTestConverter()
        // },
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IStrykerOptions _options;
    private readonly IFileSystem _fileSystem;
    private readonly IAnsiConsole _console;

    private readonly List<IMetricDataCollection> _metricDataCollections = new();


    public MemoizationDataReporter(IStrykerOptions options, IFileSystem fileSystem = null, IAnsiConsole console = null)
    {
        _options = options;
        _fileSystem = fileSystem ?? new FileSystem();
        _console = console ?? AnsiConsole.Console;
    }


    public void OnMutantsOfProjectTested(IMetricDataCollection collector)
    {
        _metricDataCollections.Add(collector);
    }


    public void OnAllMutantsTested(IReadOnlyProjectComponent reportComponent, ITestProjectsInfo testProjectsInfo)
    {
        // Todo Combine metric data collection and write, like originally below here
        // var mutationReport = JsonReport.Build(_options, reportComponent, testProjectsInfo);
        var metricDataReport = _metricDataCollections;

        var filename = _options.ReportFileName + "__metric_data" + ".json";
        var reportPath = Path.Combine(_options.ReportPath, filename);
        var reportUri = "file://" + reportPath.Replace("\\", "/");

        WriteReportToJsonFile(reportPath);

        var green = new Style(Color.Green);
        _console.WriteLine();
        _console.WriteLine("Your json report has been generated at:", green);

        if (_console.Profile.Capabilities.Links)
        {
            // We must print the report path as the link text because on some terminals links might be supported but not actually clickable: https://github.com/spectreconsole/spectre.console/issues/764
            _console.WriteLine(reportPath, green.Combine(new Style(link: reportUri)));
        }
        else
        {
            _console.WriteLine(reportUri, green);
        }
    }

    private List<MetricResultReport> BuildReport()
    {
        return _metricDataCollections.Select(mdc => mdc.CreateMetricResultReport()).ToList();
    }


    private void WriteReportToJsonFile(string filePath)
    {
        var reportData = BuildReport();

        _fileSystem.Directory.CreateDirectory(Path.GetDirectoryName(filePath));
        // using var file = _fileSystem.File.Create(filePath);
        // using var writer = new Utf8JsonWriter(file, new JsonWriterOptions { Indented = Options.WriteIndented });
        // JsonSerializer.Serialize(writer, reportData, Options);

        using var file = _fileSystem.File.Create(filePath);
        using var writer = new Utf8JsonWriter(file, new JsonWriterOptions { Indented = Options.WriteIndented });

        writer.WriteStartObject();
        var data = reportData[0];

        writer.WriteString("TestRunId", data.TestRunId);
        writer.WriteNumber("MutationScore", data.MutationScore);
        writer.WriteNumber("TotalMemoizationInjectionCalls", data.TotalMemoizationInjectionCalls);
        writer.WriteNumber("TotalUniqueMemoizationInjectionCalls", data.TotalUniqueMemoizationInjectionCalls);
        writer.WriteNumber("TotalHits", data.TotalHits);
        writer.WriteNumber("TotalMisses", data.TotalMisses);
        writer.WriteNumber("HitMissRatio", data.HitMissRatio);


        // Stream the large collection
        writer.WritePropertyName("RawEntries");
        writer.WriteStartArray();

        foreach (var entry in data.RawEntries)
        {
            JsonSerializer.Serialize(writer, entry, Options);
            // Optional flush periodically to control memory use
            if (writer.BytesPending > 10_000_000)
            {
                writer.Flush();
            }
        }

        writer.WriteEndArray();


        writer.WritePropertyName("NotMemoizedReasons");
        writer.WriteStartArray();

        foreach (var reason in data.NotMemoizedReasons)
        {
            writer.WriteStartObject();

            writer.WriteString("Type", reason.Type.ToString());
            writer.WriteString("MemoizationLevel", reason.MemoizationLevel.ToString());
            writer.WriteString("Reason", reason.Reason);
            if (!string.IsNullOrEmpty(reason.NodeCodeString))
                writer.WriteString("NodeCodeString", reason.NodeCodeString);

            writer.WriteEndObject();

            // Optional: flush periodically for very large lists
            if (writer.BytesPending > 10_000_000)
                writer.Flush();
        }

        writer.WriteEndArray();


        writer.WritePropertyName("MemoizationProcessTimings");
        JsonSerializer.Serialize(writer, data.MemoizationProcessTimings, Options);

        writer.WriteEndObject();
        writer.Flush();
    }


    public void OnMutantTested(IReadOnlyMutant result)
    {
        // memoizationData.Add();
    }

    public void OnMutantsCreated(IReadOnlyProjectComponent reportComponent, ITestProjectsInfo testProjectsInfo)
    {
        // Ignore
    }

    public void OnStartMutantTestRun(IEnumerable<IReadOnlyMutant> mutantsToBeTested)
    {
        // Ignore
    }
}
