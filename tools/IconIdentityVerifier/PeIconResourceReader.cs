using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace IconIdentityVerifier;

public static class PeIconResourceReader
{
    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
    private const uint LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020;

    private static readonly IntPtr RT_ICON = new(3);
    private static readonly IntPtr RT_GROUP_ICON = new(14);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryExW(string lpLibFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr hLibModule);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindResourceW(IntPtr hModule, IntPtr lpName, IntPtr lpType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr hResData);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool EnumResourceNamesW(IntPtr hModule, IntPtr lpszType, EnumResNameProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumResNameProc(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);

    /// <summary>
    /// Backward-compatible single-group accessor: returns the frames for the executable's
    /// first <c>RT_GROUP_ICON</c> resource only. Prefer <see cref="ExtractAndReadAllGroups"/>
    /// when the executable might carry more than one icon group (CRUU14-012).
    /// </summary>
    public static Dictionary<(int Width, int Height), string> ExtractAndReadFrames(string exePath)
    {
        var groups = ExtractAndReadAllGroups(exePath);
        return groups[0].Frames;
    }

    /// <summary>
    /// Enumerates every <c>RT_GROUP_ICON</c> resource in the executable and returns the
    /// decoded frame hashes for each. Earlier versions of this reader stopped enumeration
    /// after the first group found by <c>EnumResourceNamesW</c>, so an executable carrying a
    /// second icon group (e.g. from an embedded control or a differently-sourced resource)
    /// would never have that group's content verified at all. See CRUU14-012.
    /// </summary>
    public static IReadOnlyList<(string GroupName, Dictionary<(int Width, int Height), string> Frames)> ExtractAndReadAllGroups(string exePath)
    {
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException("EXE file not found.", exePath);
        }

        IntPtr hModule = LoadLibraryExW(
            exePath,
            IntPtr.Zero,
            LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);

        if (hModule == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to load executable '{exePath}'.");
        }

        try
        {
            var groupNames = new List<IntPtr>();
            EnumResourceNamesW(hModule, RT_GROUP_ICON, (hMod, type, name, lParam) =>
            {
                groupNames.Add(name);
                return true; // Keep enumerating: collect every group, not just the first.
            }, IntPtr.Zero);

            if (groupNames.Count == 0)
            {
                throw new InvalidOperationException($"No RT_GROUP_ICON resources found in '{exePath}'.");
            }

            var results = new List<(string, Dictionary<(int, int), string>)>();
            foreach (IntPtr groupName in groupNames)
            {
                string label = groupName.ToInt64() is >= 0 and <= ushort.MaxValue
                    ? $"#{groupName}"
                    : Marshal.PtrToStringUni(groupName) ?? groupName.ToString();
                results.Add((label, ReadGroup(hModule, groupName)));
            }

            return results;
        }
        finally
        {
            FreeLibrary(hModule);
        }
    }

    private static Dictionary<(int Width, int Height), string> ReadGroup(IntPtr hModule, IntPtr groupName)
    {
            IntPtr hResInfo = FindResourceW(hModule, groupName, RT_GROUP_ICON);
            if (hResInfo == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to find RT_GROUP_ICON resource.");
            }

            IntPtr hResData = LoadResource(hModule, hResInfo);
            IntPtr pGroupData = LockResource(hResData);
            uint groupSize = SizeofResource(hModule, hResInfo);

            if (pGroupData == IntPtr.Zero || groupSize < 6)
            {
                throw new InvalidOperationException("Invalid RT_GROUP_ICON resource data.");
            }

            byte[] groupBytes = new byte[groupSize];
            Marshal.Copy(pGroupData, groupBytes, 0, (int)groupSize);

            using var groupReader = new BinaryReader(new MemoryStream(groupBytes));
            ushort reserved = groupReader.ReadUInt16();
            ushort type = groupReader.ReadUInt16();
            ushort count = groupReader.ReadUInt16();

            if (type != 1 || count == 0)
            {
                throw new InvalidOperationException("Invalid icon group header in executable.");
            }

            // Read group icon directory entries
            var entries = new List<(byte Width, byte Height, byte ColorCount, byte Reserved, ushort Planes, ushort BitCount, uint BytesInRes, ushort Id)>();
            for (int i = 0; i < count; i++)
            {
                byte w = groupReader.ReadByte();
                byte h = groupReader.ReadByte();
                byte cc = groupReader.ReadByte();
                byte res = groupReader.ReadByte();
                ushort planes = groupReader.ReadUInt16();
                ushort bpp = groupReader.ReadUInt16();
                uint bytesInRes = groupReader.ReadUInt32();
                ushort id = groupReader.ReadUInt16();
                entries.Add((w, h, cc, res, planes, bpp, bytesInRes, id));
            }

            // Reconstruct ICO stream
            using var icoStream = new MemoryStream();
            using var icoWriter = new BinaryWriter(icoStream);

            // Write 6-byte header
            icoWriter.Write((ushort)0);
            icoWriter.Write((ushort)1);
            icoWriter.Write(count);

            uint imageOffset = (uint)(6 + (16 * count));

            // Write 16-byte entries
            foreach (var e in entries)
            {
                icoWriter.Write(e.Width);
                icoWriter.Write(e.Height);
                icoWriter.Write(e.ColorCount);
                icoWriter.Write(e.Reserved);
                icoWriter.Write(e.Planes);
                icoWriter.Write(e.BitCount);
                icoWriter.Write(e.BytesInRes);
                icoWriter.Write(imageOffset);

                imageOffset += e.BytesInRes;
            }

            // Write RT_ICON payload bytes
            foreach (var e in entries)
            {
                IntPtr hIconRes = FindResourceW(hModule, new IntPtr(e.Id), RT_ICON);
                if (hIconRes == IntPtr.Zero)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to find RT_ICON #{e.Id}.");
                }

                IntPtr hIconData = LoadResource(hModule, hIconRes);
                IntPtr pIconData = LockResource(hIconData);
                uint iconSize = SizeofResource(hModule, hIconRes);

                byte[] iconPayload = new byte[iconSize];
                Marshal.Copy(pIconData, iconPayload, 0, (int)iconSize);

                icoWriter.Write(iconPayload);
            }

            icoStream.Position = 0;
            return IcoReader.ReadFrames(icoStream);
    }
}
