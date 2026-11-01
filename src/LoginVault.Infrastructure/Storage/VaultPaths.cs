namespace LoginVault.Infrastructure.Storage;

/// <summary>File and directory names inside a data directory.</summary>
public sealed class VaultPaths
{
    public const string VaultFileName = "vault.dat";
    public const string BackupDirectoryName = "backups";
    public const string GenerationSearchPattern = "vault-*.dat";
    public const string PreRestoreSearchPattern = "pre-restore-*.dat";
    public const string TempSearchPattern = VaultFileName + ".*.tmp";

    public VaultPaths(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        DataDirectory = Path.GetFullPath(dataDirectory);
        VaultFile = Path.Combine(DataDirectory, VaultFileName);
        BackupDirectory = Path.Combine(DataDirectory, BackupDirectoryName);
    }

    public string DataDirectory { get; }

    public string VaultFile { get; }

    public string BackupDirectory { get; }

    /// <summary>A unique temporary file next to the vault; only encrypted bytes are ever written to it.</summary>
    public string NewTempFile() => Path.Combine(DataDirectory, $"{VaultFileName}.{Guid.NewGuid():N}.tmp");

    /// <summary>
    /// Generation backup name. The UTC timestamp prefix plus a per-process sequence makes lexical order
    /// equal chronological order even when several saves fall into the same millisecond.
    /// </summary>
    public string NewGenerationFile(DateTimeOffset now, int sequence) =>
        Path.Combine(BackupDirectory, $"vault-{Stamp(now)}-{sequence:D6}-{Guid.NewGuid().ToString("N")[..8]}.dat");

    public string NewPreRestoreFile(DateTimeOffset now) =>
        Path.Combine(BackupDirectory, $"pre-restore-{Stamp(now)}-{Guid.NewGuid().ToString("N")[..8]}.dat");

    /// <summary>Reads the UTC timestamp back from a generation file name; false for names this version did not produce.</summary>
    public static bool TryParseGenerationStamp(string path, out DateTimeOffset stamp)
    {
        stamp = default;
        var name = Path.GetFileName(path);
        const string prefix = "vault-";
        const int stampLength = 19; // yyyyMMddTHHmmssfffZ
        if (name is null || !name.StartsWith(prefix, StringComparison.Ordinal) || name.Length < prefix.Length + stampLength)
        {
            return false;
        }

        return DateTimeOffset.TryParseExact(
            name.AsSpan(prefix.Length, stampLength),
            "yyyyMMdd'T'HHmmssfff'Z'",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out stamp);
    }

    private static string Stamp(DateTimeOffset now) => now.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", System.Globalization.CultureInfo.InvariantCulture);
}
