using System.Reflection;

namespace LexiFlow.Updates;

public static class UpdateTrust
{
    // Public key only. Never load replacement trust keys from the feed or environment.
    public const string PublicKey = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEBh1/ud2XOYCP4RfJYYjA0cDMwH8oE+wlfHpuLLt/2TFY3OovIJ/4z2QS8GNaQy5pnPSwE+quqJIboJCsyTdPeg==";
    public static Version CurrentVersion => UpdateProtocol.ParseVersion(typeof(UpdateTrust).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "LexiFlow.UpdateVersion").Value!);
}
