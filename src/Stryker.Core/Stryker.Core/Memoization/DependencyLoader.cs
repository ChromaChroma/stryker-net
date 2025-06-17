using System.IO;
using System.Reflection;
using Microsoft.CodeAnalysis;

namespace Stryker.Core.Memoization;

public static class DependencyLoader
{
    public static string LoadNewtonsoftJson()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "Stryker.Newtonsoft.Json.dll";

        using (var stream = assembly.GetManifestResourceStream(resourceName))
        {
            if (stream == null) throw new FileNotFoundException("Embedded resource not found.");

            var tempFile = Path.Combine(Path.GetTempPath(), "Newtonsoft.Json.dll");
            using (var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
            {
                stream.CopyTo(fileStream);
            }

            return tempFile;
        }
    }


    public static Compilation AddDllReference(Compilation compilation, string dllPath)
    {
        var metadataReference = MetadataReference.CreateFromFile(dllPath);
        return compilation.AddReferences(metadataReference);
    }
}
