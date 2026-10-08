using System.Reflection;

namespace LexiFlow.Updates;

// Test-only trust compiled into fixture binaries; production UpdateTrust is NOT linked here.
internal static class UpdateTrust
{
    internal static string Metadata(string name) => typeof(UpdateTrust).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == name).Value!;
    public static string PublicKey => Metadata("TestPublicKey");
    public static Version CurrentVersion => UpdateProtocol.ParseVersion(Metadata("TestVersion"));
}
