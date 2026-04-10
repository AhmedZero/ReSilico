using Avalonia.Controls;
using Avalonia.Platform.Storage;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ReSilico.UI.Services;

public class FileDialogService(Func<Window?> getWindow) : IFileDialogService
{
    public async Task<string?> PickOpenFileAsync(string title, params FileFilter[] filters)
    {
        var window = getWindow();
        if (window is null) return null;

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = filters
                .Select(f => new FilePickerFileType(f.Name) { Patterns = f.Patterns })
                .ToList()
        };

        var result = await window.StorageProvider.OpenFilePickerAsync(options);
        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickOpenFolderAsync(string title)
    {
        var window = getWindow();
        if (window is null) return null;

        var result = await window.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = title });

        return result.Count > 0 ? result[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(
        string title, string defaultExtension, params FileFilter[] filters)
    {
        var window = getWindow();
        if (window is null) return null;

        var options = new FilePickerSaveOptions
        {
            Title = title,
            DefaultExtension = defaultExtension,
            FileTypeChoices = filters
                .Select(f => new FilePickerFileType(f.Name) { Patterns = f.Patterns })
                .ToList()
        };

        var result = await window.StorageProvider.SaveFilePickerAsync(options);
        return result?.TryGetLocalPath();
    }
}
