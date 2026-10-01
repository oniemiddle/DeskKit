using Avalonia.Media.Imaging;

namespace DeskKit.Core.Abstractions;

/// <summary>Loads the icon the shell shows for a file, folder or shortcut.</summary>
public interface IShellIconLoader
{
    bool IsSupported { get; }

    /// <summary>
    /// Returns the shell's icon for <paramref name="path"/>, or null when the
    /// shell has none (a URL, a missing file, or an unsupported platform).
    /// Results are cached for the lifetime of the process.
    /// </summary>
    Task<Bitmap?> LoadAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>Implementation used where the shell offers no icons.</summary>
public sealed class NullShellIconLoader : IShellIconLoader
{
    public bool IsSupported => false;

    public Task<Bitmap?> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        Task.FromResult<Bitmap?>(null);
}
