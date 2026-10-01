namespace DeskKit.Platform;

/// <summary>Starts the application when the user signs in.</summary>
public interface IAutoStartService
{
    bool IsSupported { get; }

    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}

/// <summary>No-op implementation used on platforms without a start-up mechanism.</summary>
public sealed class NullAutoStartService : IAutoStartService
{
    public bool IsSupported => false;

    public bool IsEnabled => false;

    public void SetEnabled(bool enabled)
    {
    }
}
