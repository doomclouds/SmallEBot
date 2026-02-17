namespace SmallEBot.Services.Workspace;

/// <summary>Virtual filesystem backed by a real directory at .agents/vfs under the app run directory.</summary>
public sealed class VirtualFileSystem : IVirtualFileSystem
{
    private readonly string _root;

    public VirtualFileSystem()
    {
        _root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".agents", "vfs"));
        Directory.CreateDirectory(_root);
    }

    /// <inheritdoc />
    public string GetRootPath()
    {
        if (!Directory.Exists(_root))
            Directory.CreateDirectory(_root);
        return _root;
    }
}
