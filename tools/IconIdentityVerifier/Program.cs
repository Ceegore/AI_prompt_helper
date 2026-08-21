using System;
using System.Collections.Generic;
using System.IO;

namespace IconIdentityVerifier;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("  IconIdentityVerifier compare-ico <expected.ico> <actual.ico>");
            Console.Error.WriteLine("  IconIdentityVerifier compare-exe <expected.ico> <PromptHelper.exe>");
            return 1;
        }

        string command = args[0].ToLowerInvariant();
        string expectedIcoPath = args[1];
        string targetPath = args[2];

        try
        {
            if (!File.Exists(expectedIcoPath))
            {
                Console.Error.WriteLine($"Expected ICO file not found: {expectedIcoPath}");
                return 1;
            }

            if (!File.Exists(targetPath))
            {
                Console.Error.WriteLine($"Target file not found: {targetPath}");
                return 1;
            }

            var expectedFrames = IcoReader.ReadFrames(expectedIcoPath);
            Dictionary<(int Width, int Height), string> targetFrames;

            switch (command)
            {
                case "compare-ico":
                    targetFrames = IcoReader.ReadFrames(targetPath);
                    break;
                case "compare-exe":
                    targetFrames = PeIconResourceReader.ExtractAndReadFrames(targetPath);
                    break;
                default:
                    Console.Error.WriteLine($"Unknown command '{command}'.");
                    return 1;
            }

            bool allMatched = true;

            foreach (int size in IcoReader.MandatorySizes)
            {
                var key = (size, size);
                if (!expectedFrames.TryGetValue(key, out string? expectedHash))
                {
                    Console.Error.WriteLine($"Missing mandatory size {size}x{size} in expected ICO.");
                    allMatched = false;
                    continue;
                }

                if (!targetFrames.TryGetValue(key, out string? targetHash))
                {
                    Console.Error.WriteLine($"Missing mandatory size {size}x{size} in target.");
                    allMatched = false;
                    continue;
                }

                if (!string.Equals(expectedHash, targetHash, StringComparison.OrdinalIgnoreCase))
                {
                    Console.Error.WriteLine($"Mismatch for {size}x{size}: expected {expectedHash}, got {targetHash}");
                    allMatched = false;
                }
                else
                {
                    Console.WriteLine($"Matched {size}x{size}: {expectedHash}");
                }
            }

            if (!allMatched)
            {
                Console.Error.WriteLine("Icon identity verification failed.");
                return 1;
            }

            Console.WriteLine("Icon identity verified successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error during icon identity verification: {ex.Message}");
            return 1;
        }
    }
}
