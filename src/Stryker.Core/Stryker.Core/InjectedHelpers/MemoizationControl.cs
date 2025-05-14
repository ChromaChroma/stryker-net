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
    }
}
