// Linux headless adaptation of the RazorGenerator.MsBuild RazorCodeGen task.
//
// Why this exists: RazorGenerator.MsBuild's RazorCodeGen task is compiled against
// Microsoft.Build.Utilities.v4.0 (the .NET Framework MSBuild), which dotnet's Core MSBuild
// cannot load (error MSB4062). On the client's Windows machines the package target works
// unchanged; this driver exists so the Linux build can run the same RazorGenerator.Core
// engine and produce byte-equivalent obj/CodeGen output. The steps mirror the task's
// ExecuteCore() implementation 1:1 (see
// https://github.com/RazorGenerator/RazorGenerator/blob/master/RazorGenerator.MsBuild/RazorGenerator.cs).
//
// Usage:
//   mono RazorGenDriver.exe --project-root <root> --codegen-dir <dir> [--root-namespace <ns>]
// It discovers **/*.cshtml under project-root (excluding obj/ and bin/), same file set the
// MSBuild target's _ResolveRazorFiles computes from the project's Content/None items.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RazorGenerator.Core;

namespace RazorGenDriver
{
    internal static class Program
    {
        private static readonly Regex _namespaceRegex = new Regex(@"($|\.)(\d)");

        private static int Main(string[] args)
        {
            string projectRoot = null;
            string codeGenDir = null;
            string rootNamespace = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (i + 1 >= args.Length)
                    break;
                switch (args[i])
                {
                    case "--project-root":
                        projectRoot = args[++i];
                        break;
                    case "--codegen-dir":
                        codeGenDir = args[++i];
                        break;
                    case "--root-namespace":
                        rootNamespace = args[++i];
                        break;
                }
            }

            if (projectRoot == null || codeGenDir == null)
            {
                Console.Error.WriteLine(
                    "Usage: RazorGenDriver.exe --project-root <root> --codegen-dir <dir> [--root-namespace <ns>]");
                return 2;
            }

            return Execute(projectRoot, codeGenDir, rootNamespace) ? 0 : 1;
        }

        private static bool Execute(string projectRoot, string codeGenDir, string rootNamespace)
        {
            string[] files = Directory.GetFiles(projectRoot, "*.cshtml", SearchOption.AllDirectories)
                .Where(f => f.IndexOf(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                           StringComparison.OrdinalIgnoreCase) < 0)
                .Where(f => f.IndexOf(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                           StringComparison.OrdinalIgnoreCase) < 0)
                .ToArray();

            if (files.Length == 0)
                return true;

            using (var hostManager = new HostManager(projectRoot))
            {
                foreach (string filePath in files)
                {
                    string projectRelativePath = GetProjectRelativePath(filePath, projectRoot);
                    string itemNamespace = GetNamespace(projectRelativePath, rootNamespace);

                    string outputPath = Path.Combine(codeGenDir, projectRelativePath.TrimStart(Path.DirectorySeparatorChar)) + ".cs";
                    if (!RequiresRecompilation(filePath, outputPath))
                    {
                        Console.WriteLine("Skipping {0}: {1} is already up to date", filePath, outputPath);
                        continue;
                    }
                    EnsureDirectory(outputPath);

                    var host = hostManager.CreateHost(filePath, projectRelativePath, itemNamespace);

                    bool hasErrors = false;
                    host.Error += (o, eventArgs) =>
                    {
                        Console.Error.WriteLine("RazorGenerator error: {0}", eventArgs.ErrorMessage);
                        hasErrors = true;
                    };

                    try
                    {
                        string result = host.GenerateCode();
                        if (!hasErrors)
                            File.WriteAllText(outputPath, result);
                    }
                    catch (Exception exception)
                    {
                        Console.Error.WriteLine(exception.ToString());
                        return false;
                    }

                    if (hasErrors)
                        return false;

                    Console.WriteLine("Generated {0}", outputPath);
                }
            }

            return true;
        }

        private static string GetNamespace(string projectRelativePath, string rootNamespace)
        {
            string directory = Path.GetDirectoryName(projectRelativePath);
            string itemNamespace = directory.Trim(Path.DirectorySeparatorChar);
            if (string.IsNullOrEmpty(itemNamespace))
                return rootNamespace;

            var stringBuilder = new StringBuilder(itemNamespace.Length);
            foreach (char c in itemNamespace)
            {
                if (c == Path.DirectorySeparatorChar)
                    stringBuilder.Append('.');
                else if (!char.IsLetterOrDigit(c))
                    stringBuilder.Append('_');
                else
                    stringBuilder.Append(c);
            }
            itemNamespace = _namespaceRegex.Replace(stringBuilder.ToString(), "$1_$2");

            if (!string.IsNullOrEmpty(rootNamespace))
                itemNamespace = rootNamespace + "." + itemNamespace;
            return itemNamespace;
        }

        private static string GetProjectRelativePath(string filePath, string projectRoot)
        {
            if (filePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                return filePath.Substring(projectRoot.Length);
            return filePath;
        }

        private static bool RequiresRecompilation(string filePath, string outputPath)
        {
            if (!File.Exists(outputPath))
                return true;
            return File.GetLastWriteTimeUtc(filePath) > File.GetLastWriteTimeUtc(outputPath);
        }

        private static void EnsureDirectory(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }
    }
}
