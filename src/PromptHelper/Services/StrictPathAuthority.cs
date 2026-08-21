using System;
using System.IO;
using System.Security;

namespace PromptHelper.Services;

internal enum StrictPathKind
{
    Missing,
    File,
    Directory
}

internal sealed record StrictPathProbe(
    StrictPathKind Kind,
    FileAttributes? Attributes);

internal sealed class StrictPathAuthority
{
    public StrictPathProbe Probe(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            FileAttributes attributes = File.GetAttributes(path);

            bool isDirectory = (attributes & FileAttributes.Directory) != 0;

            return new StrictPathProbe(
                isDirectory ? StrictPathKind.Directory : StrictPathKind.File,
                attributes);
        }
        catch (FileNotFoundException)
        {
            return new StrictPathProbe(StrictPathKind.Missing, null);
        }
        catch (DirectoryNotFoundException)
        {
            return new StrictPathProbe(StrictPathKind.Missing, null);
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (SecurityException)
        {
            throw;
        }
        catch (IOException)
        {
            // Do not reinterpret arbitrary I/O errors as Missing.
            throw;
        }
    }

    public bool RequireDirectory(string path)
    {
        StrictPathProbe result = Probe(path);

        return result.Kind switch
        {
            StrictPathKind.Directory => true,
            StrictPathKind.Missing => false,
            StrictPathKind.File => throw new InvalidDataException($"Expected a directory but found a file: '{path}'."),
            _ => throw new InvalidOperationException($"Unhandled strict path state: {result.Kind}.")
        };
    }
}
