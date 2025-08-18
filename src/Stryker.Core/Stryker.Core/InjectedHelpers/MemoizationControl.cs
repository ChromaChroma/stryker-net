using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace Stryker
{
    public static class MemoizationControl
    {
        private static readonly System.Collections.Generic.Dictionary<string, object> MemoizationDict =
            new System.Collections.Generic.Dictionary<string, object>();

        // this attribute will be set by the Stryker Data Collector before each test
        public static bool CaptureCoverage;

        static MemoizationControl()
        {
            var currentNamespace = typeof(MemoizationControl).Namespace;
            var assembly = typeof(MemoizationControl).Assembly;
            var mutantControlType = assembly.GetType($"{currentNamespace}.MutantControl");
            if (mutantControlType != null)
            {
                var captureCoverageField = mutantControlType.GetField("CaptureCoverage");
                if (captureCoverageField != null)
                {
                    CaptureCoverage = (bool)captureCoverageField.GetValue(null)!; // static field, so null instance
                }
            }


            // while (!Debugger.IsAttached)
            // {
            //     Thread.Sleep(100);
            // }
            // Debugger.Break();
        }

        // check with: Stryker.MemoizationControl.RetrieveMemoization<T>(ID, FUNC, PRED)
        public static T RetrieveMemoization<T>(string id, System.Func<T> func, System.Func<bool> predicate = null)
        {
            // // Simply runs original code (should result in same mutation score as original)
            // return func.Invoke();

            // Debugger.Break();

            if (CaptureCoverage || (predicate != null && predicate.Invoke()))
            {
                // Debugger.Break();
                return func.Invoke();
            }
            // return func.Invoke();

            // Debugger.Break();
            if (MemoizationDict.TryGetValue(id, out var value))
            {
                return (T)value;
            }

            T result = func.Invoke();
            MemoizationDict.Add(id, result);
            return result;
        }


        // check with: Stryker.MemoizationControl.GenerateMemoizationId(ID, PARAMS)
        public static string GenerateMemoizationId(string methodIdentifier, params object[] args)
            => methodIdentifier + "__" + string.Join(
                ":-:",
                GetSerializableArgs(args)
            );

        private static IEnumerable<string> GetSerializableArgs(object[] args)
        {
            foreach (var arg in args)
            {
                if (arg == null)
                {
                    yield return "null";
                }
                else if (arg is string || arg.GetType().IsPrimitive)
                {
                    yield return arg.ToString();
                }
                else
                {
                    // Fallback: use type name and hash code to avoid deep serialization
                    yield return $"{arg.GetType().FullName}:{arg.GetHashCode()}";
                }
            }
        }


        // // check with: Stryker.MemoizationControl.GenerateMemoizationId(ID, PARAMS)
        // public static string GenerateMemoizationId(string methodIdentifier, params object[] args) => $"{methodIdentifier}__{ToHash(args)}";
        //
        // private static string ToHash<T>(IEnumerable<T> items)
        // {
        //     var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = false });
        //     var bytes = Encoding.UTF8.GetBytes(json);
        //     var hash = SHA256.HashData(bytes);
        //     return Convert.ToBase64String(hash);
        // }
        //


        // // check with: Stryker.MemoizationControl.GetMemoization<T>(ID)
        // public static T GetMemoization<T>(string id)
        // {
        //     return default(T);
        // }

        // check with: Stryker.MemoizationControl.StoreMemoization<T>(ID, VALUE)
        // public static T StoreMemoization<T>(string id, T value)
        // {
        //     return value;
        // }
    }
}
