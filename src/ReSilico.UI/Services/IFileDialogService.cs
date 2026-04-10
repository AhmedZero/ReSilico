using System.Threading.Tasks;

namespace ReSilico.UI.Services;

public interface IFileDialogService
{
    Task<string?> PickOpenFileAsync(string title, params FileFilter[] filters);
    Task<string?> PickOpenFolderAsync(string title);
    Task<string?> PickSaveFileAsync(string title, string defaultExtension, params FileFilter[] filters);
}

public readonly record struct FileFilter(string Name, string[] Patterns);
