using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.DataCollection;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.InProcDataCollector;

namespace Stryker.DataCollector
{
    public struct DataCollectorOptions
    {
        public bool UseCoverageCollector { get; set; }
        public bool UseMemoizationDataCollection { get; set; }
    }

    public class DataCollectorSettingsFactory
    {
        private const string TemplateForDataCollectorsConfiguration =
            @"
            <InProcDataCollectionRunSettings>
                <InProcDataCollectors>
                    {0}
                </InProcDataCollectors>
            </InProcDataCollectionRunSettings>
            ";

        private const string TemplateForInProcCollectorConfiguration =
            @"
            <InProcDataCollector {0}>
                <Configuration>{1}</Configuration>
            </InProcDataCollector>
            ";

        public static string GetVsTestDataCollectorSettings(
            bool needCoverage,
            bool trackMemoizationData,
            IEnumerable<(int mutant, IEnumerable<Guid> coveringTests)> mutantTestsMap,
            string helperNameSpace,
            DataCollectorOptions options
        )
        {
            if (!needCoverage && !trackMemoizationData)
            {
                return string.Empty;
            }

            var dataCollectorsSb = new StringBuilder();

            if (options.UseCoverageCollector)
            {
                var dataCollectorAttributes = GenerateDataCollectorXMLAttributes<CoverageCollector>();

                var configuration = new StringBuilder();
                configuration.Append("<Parameters>");
                // XML parameters

                if (needCoverage)
                {
                    configuration.Append("<Coverage/>");
                }

                if (mutantTestsMap != null)
                {
                    foreach (var (mutant, coveringTests) in mutantTestsMap)
                    {
                        configuration.AppendFormat("<Mutant id='{0}' tests='{1}'/>", mutant,
                            coveringTests == null ? "" : string.Join(",", coveringTests));
                    }
                }

                configuration.Append($"<MutantControl name='{helperNameSpace}.MutantControl'/>");

                //
                configuration.Append("</Parameters>");
                dataCollectorsSb.Append(
                    string.Format(TemplateForInProcCollectorConfiguration, dataCollectorAttributes, configuration)
                );
            }

            if (options.UseMemoizationDataCollection)
            {
                var dataCollectorAttributes = GenerateDataCollectorXMLAttributes<MemoizationDataCollector>();

                var configuration = new StringBuilder();
                configuration.Append("<Parameters>");
                // XML parameters

                if (trackMemoizationData)
                {
                    configuration.Append("<HitsAndMisses/>");
                }

                configuration.Append($"<MemoizationControl name='{helperNameSpace}.MemoizationControl'/>");
                //
                configuration.Append("</Parameters>");
                dataCollectorsSb.Append(
                    string.Format(TemplateForInProcCollectorConfiguration, dataCollectorAttributes, configuration)
                );
            }


            return string.Format(TemplateForDataCollectorsConfiguration, dataCollectorsSb);
        }

        private static string GenerateDataCollectorXMLAttributes<T>() where T : InProcDataCollection
        {
            var codeBase = typeof(T).GetTypeInfo().Assembly.Location;
            var qualifiedName = typeof(T).AssemblyQualifiedName;
            var friendlyName = typeof(T).ExtractAttribute<DataCollectorFriendlyNameAttribute>()
                .FriendlyName;
            // ReSharper disable once PossibleNullReferenceException
            var uri = (typeof(T)
                    .GetTypeInfo()
                    .GetCustomAttributes(typeof(DataCollectorTypeUriAttribute), false)
                    .First() as DataCollectorTypeUriAttribute
                ).TypeUri;
            return
                $"friendlyName=\"{friendlyName}\" uri=\"{uri}\" codebase=\"{codeBase}\" assemblyQualifiedName=\"{qualifiedName}\"";
        }
    }
}
