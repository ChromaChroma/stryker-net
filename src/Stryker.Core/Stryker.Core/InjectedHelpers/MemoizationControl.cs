
using System.Collections.Generic;

namespace Stryker;

public static class MemoizationControl
{
    private static System.Collections.Generic.Dictionary<string, object> _coveredMutants = new System.Collections.Generic.Dictionary<string, object>();


    // check with: Stryker.MemoizationControl.RetrieveMemoization<T>(ID, FUNC)
    public static T RetrieveMemoization<T>(string id, System.Func<T> func)
    {
        // Dictionary<string, object> dict = new Dictionary<string, object>()
        // return value;
        return func.Invoke();
    }



    // // check with: Stryker.MemoizationControl.GetMemoization<T>(ID)
    // public static T GetMemoization<T>(string id)
    // {
    //     return default(T);
    // }

    // check with: Stryker.MemoizationControl.StoreMemoization<T>(ID, VALUE)
    public static T StoreMemoization<T>(string id, T value)
    {
        return value;
    }

    // check with: Stryker.MemoizationControl.GenerateMemoizationId(ID, PARAMS)
    public static string GenerateMemoizationId(string methodIdentifier, params object[] args)
        => methodIdentifier + "__" + string.Join(":-:", args.ToString()); //TODO maybe Hash these instead of ToString()



}
