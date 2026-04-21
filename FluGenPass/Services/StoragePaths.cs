using System.IO;

namespace FluGenPass.Services;

public static class StoragePaths
{
    private const string AppFolderName = "FluGenPass";

    public static string GetAppDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppFolderName
        );
    }
}