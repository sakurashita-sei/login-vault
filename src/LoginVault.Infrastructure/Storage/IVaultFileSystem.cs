namespace LoginVault.Infrastructure.Storage;

/// <summary>The few file operations the store needs, so tests can inject failures while still exercising real I/O.</summary>
public interface IVaultFileSystem
{
    bool FileExists(string path);

    long GetFileLength(string path);

    byte[] ReadAllBytes(string path);

    /// <summary>Creates a new file (fails if it exists), writes the content and flushes it to the device.</summary>
    void WriteNewFileDurably(string path, ReadOnlySpan<byte> content);

    /// <summary>Atomically replaces <paramref name="destinationPath"/> with <paramref name="sourcePath"/>, keeping the old destination as <paramref name="backupPath"/>.</summary>
    void ReplaceFile(string sourcePath, string destinationPath, string backupPath);

    /// <summary>Moves without overwriting.</summary>
    void MoveFile(string sourcePath, string destinationPath);

    void CopyFile(string sourcePath, string destinationPath, bool overwrite);

    /// <summary>Deletes the file; a missing file is not an error.</summary>
    void DeleteFile(string path);

    void CreateDirectory(string path);

    IReadOnlyList<string> ListFiles(string directory, string searchPattern);
}

/// <summary>Real file system implementation.</summary>
public sealed class PhysicalFileSystem : IVaultFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public long GetFileLength(string path) => new FileInfo(path).Length;

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public void WriteNewFileDurably(string path, ReadOnlySpan<byte> content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        stream.Write(content);
        stream.Flush(flushToDisk: true);
    }

    public void ReplaceFile(string sourcePath, string destinationPath, string backupPath) =>
        File.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors: true);

    public void MoveFile(string sourcePath, string destinationPath) => File.Move(sourcePath, destinationPath, overwrite: false);

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite) => File.Copy(sourcePath, destinationPath, overwrite);

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public IReadOnlyList<string> ListFiles(string directory, string searchPattern) =>
        Directory.Exists(directory) ? Directory.GetFiles(directory, searchPattern) : Array.Empty<string>();
}
