using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Stryker
{
    internal static class DebugControl
    {

#if NET5_0_OR_GREATER
        static void DebugAttach()
        {
            if (!Debugger.IsAttached)
            {
                Debugger.Launch();
            }

            if (System.Diagnostics.Debugger.IsAttached)
            {
                System.Diagnostics.Debugger.Break();
            }
        }
#else
    static void DebugAttach()
    {
        if (!Debugger.IsAttached)
        {
            Debugger.Launch();
        }
        if (System.Diagnostics.Debugger.IsAttached)
        {
            System.Diagnostics.Debugger.Break();
        }
    }
#endif

    }
}
// {
//     Console.WriteLine("Waiting for debugger to attach");
//     while (!Debugger.IsAttached)
//     {
//         Thread.Sleep(100);
//     }
//     Console.WriteLine("Debugger attached");
//     Debugger.Break();
// }
