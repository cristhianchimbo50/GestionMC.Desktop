using System;
using System.IO;

namespace GestionMC.Desktop.Services;

public interface IStoragePathProvider
{
    string GetStoragePath();
}

public class StoragePathProvider : IStoragePathProvider
{
    public string GetStoragePath()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GestionMC.Desktop"
        );

        Directory.CreateDirectory(folder);

        return Path.Combine(folder, "sri-storage.json");
    }
}
