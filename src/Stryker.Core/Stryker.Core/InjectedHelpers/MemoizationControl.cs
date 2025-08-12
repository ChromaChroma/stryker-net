namespace Stryker
{
    public static class MemoizationControl
    {
        private static readonly System.Collections.Generic.Dictionary<string, object> MemoizationDict =
            new System.Collections.Generic.Dictionary<string, object>();

        // this attribute will be set by the Stryker Data Collector before each test
        public static bool CaptureCoverage;

        // check with: Stryker.MemoizationControl.RetrieveMemoization<T>(ID, FUNC)
        public static T RetrieveMemoization<T>(string id, System.Func<T> func)
        {
            // // Simply runs original code (should result in same mutation score as original)
            // return func.Invoke();


            if (CaptureCoverage)
            {
                return func.Invoke();
            }
            return func.Invoke();


            // if (MemoizationDict.TryGetValue(id, out var value))
            // {
            //     return (T)value;
            // }

            T result = func();
            MemoizationDict.Add(id, (object)result);
            return (T)result;
        }


        // check with: Stryker.MemoizationControl.GenerateMemoizationId(ID, PARAMS)
        public static string GenerateMemoizationId(string methodIdentifier, params object[] args)
            => methodIdentifier + "__" +
               string.Join(":-:", args.ToString()); //TODO maybe Hash these instead of ToString()


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
