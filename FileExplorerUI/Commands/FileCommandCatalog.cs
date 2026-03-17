using System.Collections.Generic;

namespace FileExplorerUI.Commands;

public sealed class FileCommandCatalog
{
    private readonly IReadOnlyList<IFileCommandProvider> _providers;

    public FileCommandCatalog()
        : this(new IFileCommandProvider[]
        {
            new BaseFileCommandProvider(),
            new FileEntryMenuCommandProvider(),
            new DirectoryMenuCommandProvider(),
            new BackgroundMenuCommandProvider(),
            new ShortcutFileCommandProvider(),
            new ExecutableFileCommandProvider(),
            new ArchiveFileCommandProvider(),
            new PreviewFileCommandProvider()
        })
    {
    }

    public FileCommandCatalog(IReadOnlyList<IFileCommandProvider> providers)
    {
        _providers = providers;
    }

    public IReadOnlyList<FileCommandDescriptor> BuildCommands(FileCommandTarget target)
    {
        var commands = new List<FileCommandDescriptor>();
        var seen = new HashSet<string>();

        foreach (IFileCommandProvider provider in _providers)
        {
            if (!provider.CanHandle(target))
            {
                continue;
            }

            foreach (FileCommandDescriptor command in provider.GetCommands(target))
            {
                if (seen.Add(command.Id))
                {
                    commands.Add(command);
                }
            }
        }

        return commands;
    }
}
