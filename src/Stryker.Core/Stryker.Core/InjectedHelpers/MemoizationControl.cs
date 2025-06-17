namespace Stryker
{

    public static class MemoizationControl
    {
        // check with: Stryker.MemoizationControl.GetMemoization<T>(ID)
        public static T GetMemoization<T>(string id)
        {
            return default(T);
        }

        // check with: Stryker.MemoizationControl.StoreMemoization<T>(ID, VALUE)
        public static T StoreMemoization<T>(string id, T value)
        {
            return value;
        }

        // check with: Stryker.MemoizationControl.GenerateMemoizationId(ID, PARAMS)
        public static string GenerateMemoizationId(string methodIdentifier, params object[] args)
            => methodIdentifier + "__" + string.Join(":-:", args.ToString()); //TODO maybe Hash these instead of ToString()


    }
}
