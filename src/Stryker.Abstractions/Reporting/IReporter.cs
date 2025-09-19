using System.Collections.Generic;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Client;
using Stryker.Abstractions.Memoization;
using Stryker.Abstractions.ProjectComponents;

namespace Stryker.Abstractions.Reporting;

public interface IReporter
{
    // Will get called when the project has been mutated
    void OnMutantsCreated(IReadOnlyProjectComponent reportComponent, ITestProjectsInfo testProjectsInfo);
    // Will get called on start before first mutation is tested
    void OnStartMutantTestRun(IEnumerable<IReadOnlyMutant> mutantsToBeTested);
    // Will get called when a mutation has been tested
    void OnMutantTested(IReadOnlyMutant result);
    // Will get called when all mutations have been tested
    void OnAllMutantsTested(IReadOnlyProjectComponent reportComponent, ITestProjectsInfo testProjectsInfo);


    // Custom Extra lifecycle step for memoization data collection
    void OnMutantsOfProjectTested(IMetricDataCollection collector)
    {
        // Does nothing by default
    }
}
