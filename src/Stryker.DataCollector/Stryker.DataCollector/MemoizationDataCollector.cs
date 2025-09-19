using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.DataCollection;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.DataCollector.InProcDataCollector;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.InProcDataCollector;

namespace Stryker.DataCollector
{
    [DataCollectorFriendlyName("StrykerMemoizationData")]
    [DataCollectorTypeUri("https://stryker-mutator.io/MemoizationDataCollector")]
    public class MemoizationDataCollector : InProcDataCollection
    {
        private IDataCollectionSink _dataSink;
        private Action<string> _logger;
        private ThrowingListener _throwingListener;
        private DefaultTraceListener _defaultTraceListener;


        private string _controlClassName;
        private Type _memoizationControlType;
        private MethodInfo _getMemoizationData;


        private bool _hitsAndMissesOn;

        public const string PropertyName = "Stryker.Memoization";
        public const string MemoizationLog = "MemoizationLog";


        public void SetLogger(Action<string> logger) => _logger = logger;
        public void Log(string message) => _logger?.Invoke(message);

        public IList<(string, bool, long, long, long, long, long, long)>[] RetrieveMemoizationData() =>
            (IList<(string, bool, long, long, long, long, long, long)>[])_getMemoizationData?.Invoke(null,
                new object[] { });


        // ===

        public void Initialize(IDataCollectionSink dataCollectionSink)
        {
            _dataSink = dataCollectionSink;
            _throwingListener = new ThrowingListener();
        }

        public void TestSessionStart(TestSessionStartArgs testSessionStartArgs)
        {
            _defaultTraceListener = Trace.Listeners.OfType<DefaultTraceListener>().FirstOrDefault();
            if (_defaultTraceListener != null)
            {
                Trace.Listeners.Remove(_defaultTraceListener);
            }

            Trace.Listeners.Add(_throwingListener);


            var configuration = testSessionStartArgs.Configuration;
            ReadConfiguration(configuration);

            // if assembly was not loaded yet, wait for assembly to load
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;

            // scan loaded assemblies, just in case the test assembly is already loaded
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic);

            foreach (var assembly in assemblies)
            {
                FindControlType(assembly);
            }
        }

        private void ReadConfiguration(string configuration)
        {
            var node = new XmlDocument();
            node.LoadXml(configuration);

            var nameSpaceNode = node.SelectSingleNode("//Parameters/MemoizationControl");
            if (nameSpaceNode?.Attributes != null)
            {
                _controlClassName = nameSpaceNode.Attributes["name"].Value;
            }

            var coverage = node.SelectSingleNode("//Parameters/HitsAndMisses");
            if (coverage != null)
            {
                _hitsAndMissesOn = true;
            }
        }

        private void OnAssemblyLoaded(object sender, AssemblyLoadEventArgs args)
        {
            var assembly = args.LoadedAssembly;
            FindControlType(assembly);
        }

        private void FindControlType(Assembly assembly)
        {
            if (_memoizationControlType != null)
            {
                return;
            }

            _memoizationControlType = assembly.DefinedTypes?.FirstOrDefault(t => t.FullName == _controlClassName);
            if (_memoizationControlType == null)
            {
                return;
            }

            // Get references to MemoizationControl field and set its values

            _getMemoizationData = _memoizationControlType.GetMethod("GetMemoizationData");
            var hitsAndMissesControlField = _memoizationControlType.GetField("CaptureMemoizationHitsAndMisses");
            if (_hitsAndMissesOn)
            {
                hitsAndMissesControlField.SetValue(null, true);
            }
            // var coverageControlField = _memoizationControlType.GetField("CaptureCoverage");
            // if (_coverageOn)
            // {
            //     coverageControlField.SetValue(null, true);
            // }
        }

        public void TestCaseStart(TestCaseStartArgs testCaseStartArgs)
        {
            // throw new System.NotImplementedException()
        }

        public void TestCaseEnd(TestCaseEndArgs testCaseEndArgs)
        {
            Log($"Test {testCaseEndArgs.DataCollectionContext.TestCase.FullyQualifiedName} ends.");

            if (!_hitsAndMissesOn) //And other publishing control booleans
            {
                return;
            }

            PublishCoverageData(testCaseEndArgs.DataCollectionContext);
        }

        private void PublishCoverageData(DataCollectionContext dataCollectionContext)
        {
            IList<(string, bool, long, long, long, long, long, long)>[] memoizationData = RetrieveMemoizationData();
            if (memoizationData == null)
            {
                //TODO return empty version of the structure of data sent (e.g. ';' for empty 2 series of csv)
                _dataSink.SendData(dataCollectionContext, PropertyName, ";");
                // _dataSink.SendData(dataCollectionContext, MemoizationLog,$"Test {dataCollectionContext.TestCase.DisplayName} ended. No memoization data found.");
                return;
            }

            var sb = new StringBuilder();
            foreach (var memoData in memoizationData[0])
            {
                var (memoId, isHit, timeTotal, timeToCheckSerializibility, timeToTryGetValue, timeToDeserialize,
                    timeToSerialize, timeToStore) = memoData;
                sb.AppendFormat("{0}†{1}†{2}†{3}†{4}†{5}†{6}†{7};",
                    memoId, isHit, timeTotal, timeToCheckSerializibility, timeToTryGetValue, timeToDeserialize,
                    timeToSerialize, timeToStore);
                // sb.Append("tester†true†-1†-1†-1†-1†-1†-1†;");
            }
            var stringData = sb.ToString();
            if (!string.IsNullOrEmpty(stringData))
            {
                _dataSink.SendData(dataCollectionContext, PropertyName, sb.ToString());
            }
        }


        public void TestSessionEnd(TestSessionEndArgs testSessionEndArgs)
        {
            Log($"TestSession ends.");
            Trace.Listeners.Remove(_throwingListener);
            if (_defaultTraceListener != null)
            {
                Trace.Listeners.Add(_defaultTraceListener);
            }
        }
    }
}
