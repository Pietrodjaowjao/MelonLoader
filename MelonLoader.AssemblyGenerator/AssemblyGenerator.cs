using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using MelonLoader.TinyJSON;

namespace MelonLoader.AssemblyGenerator
{
    internal static class Main
    {
        private static string GameAssembly_Path = null;
        private static string MSCORLIB_Path = null;
        internal static string BaseFolder = null;
        internal static string AssemblyFolder = null;
        private static Package UnityDependencies = new Package();
        private static Executable_Package Il2CppDumper = new Executable_Package();
        private static Executable_Package Il2CppAssemblyUnhollower = new Executable_Package();
        private static string localConfigPath = null;
        private static LocalConfig localConfig = new LocalConfig();
        private static Il2CppConfig il2cppConfig = new Il2CppConfig();

        internal static bool Initialize(string unityVersion, string gameRoot, string gameDataDir)
        {
            PreSetup(gameRoot);
            if (AssemblyGenerateCheck(unityVersion))
            {
                Logger.Log("Assembly Generation Needed!");
                if (!AssemblyGenerate(gameRoot, unityVersion, gameDataDir))
                    return false;
                Cleanup();
                Logger.Log("Assembly Generation was Successful!");
            }
            return true;
        }

        private static void PreSetup(string gameRoot)
        {
            GameAssembly_Path = Path.Combine(gameRoot, "GameAssembly.dll");

            AssemblyFolder = Path.Combine(gameRoot, "MelonLoader", "Managed");

            MSCORLIB_Path = Path.Combine(AssemblyFolder, "mscorlib.dll");
            
            BaseFolder = SetupDirectory(Path.Combine(Path.Combine(Path.Combine(gameRoot, "MelonLoader"), "Dependencies"), "AssemblyGenerator"));
            
            Il2CppDumper.BaseFolder = SetupDirectory(Path.Combine(BaseFolder, "Il2CppDumper"));
            Il2CppDumper.OutputDirectory = SetupDirectory(Path.Combine(Il2CppDumper.BaseFolder, "DummyDll"));
            Il2CppDumper.FileName = "Il2CppDumper.exe";

            Il2CppAssemblyUnhollower.BaseFolder = SetupDirectory(Path.Combine(BaseFolder, "Il2CppAssemblyUnhollower"));
            Il2CppAssemblyUnhollower.OutputDirectory = SetupDirectory(Path.Combine(Il2CppAssemblyUnhollower.BaseFolder, "Output"));
            Il2CppAssemblyUnhollower.FileName = "AssemblyUnhollower.exe";

            UnityDependencies.BaseFolder = SetupDirectory(Path.Combine(BaseFolder, "UnityDependencies"));

            localConfigPath = Path.Combine(BaseFolder, "config.json");
            if (File.Exists(localConfigPath))
                localConfig = Decoder.Decode(File.ReadAllText(localConfigPath)).Make<LocalConfig>();
        }

        private static bool AssemblyGenerateCheck(string unityVersion)
        {
            if (Program.Force_Regenerate || (localConfig.UnityVersion != unityVersion) || (localConfig.DumperVersion != ExternalToolVersions.Il2CppDumperVersion) || (localConfig.UnhollowerVersion != ExternalToolVersions.Il2CppAssemblyUnhollowerVersion))
                return true;
            string game_assembly_hash = null;
            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(GameAssembly_Path))
                {
                    var hash = md5.ComputeHash(stream);
                    game_assembly_hash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            if (string.IsNullOrEmpty(localConfig.GameAssemblyHash) || (game_assembly_hash != localConfig.GameAssemblyHash))
                return true;
            return false;
        }

        private static void DownloadDependencies(string unityVersion)
        {
            Logger.Log("Downloading Il2CppDumper");
            DownloaderAndUnpacker.Run(ExternalToolVersions.Il2CppDumperUrl, ExternalToolVersions.Il2CppDumperVersion, localConfig.DumperVersion, Il2CppDumper.BaseFolder);
            localConfig.DumperVersion = ExternalToolVersions.Il2CppDumperVersion;
            localConfig.Save(localConfigPath);

            Logger.Log("Downloading Il2CppAssemblyUnhollower");
            DownloaderAndUnpacker.Run(ExternalToolVersions.Il2CppAssemblyUnhollowerUrl, ExternalToolVersions.Il2CppAssemblyUnhollowerVersion, localConfig.UnhollowerVersion,  Il2CppAssemblyUnhollower.BaseFolder);
            localConfig.UnhollowerVersion = ExternalToolVersions.Il2CppAssemblyUnhollowerVersion;
            localConfig.Save(localConfigPath);

            Logger.Log("Downloading Unity Dependencies");
            try
            {
                DownloaderAndUnpacker.Run($"{ExternalToolVersions.UnityDependenciesBaseUrl}{unityVersion}.zip", unityVersion, localConfig.UnityVersion, UnityDependencies.BaseFolder);
                localConfig.UnityVersion = unityVersion;
                localConfig.Save(localConfigPath);
            }
            catch (Exception ex)
            {
                Logger.LogError("Can't download Unity Dependencies, Unstripping will NOT be done!");
                Logger.Log(ex.ToString());
            }
        }

        private static bool AssemblyGenerate(string gameRoot, string unityVersion, string gameDataDir)
        {
            DownloadDependencies(unityVersion);
            
            FixIl2CppDumperConfig();

            Logger.Log("Executing Il2CppDumper...");
            if (!Il2CppDumper.Execute(new string[] {
                GameAssembly_Path,
                Path.Combine(gameDataDir, "il2cpp_data", "Metadata", "global-metadata.dat")
            }))
            {
                Logger.LogError("Failed to Execute Il2CppDumper!");
                return false;
            }

            Logger.Log("Executing Il2CppAssemblyUnhollower...");
            if (!Il2CppAssemblyUnhollower.Execute(new string[] {
                ("--input=" + Il2CppDumper.OutputDirectory),
                ("--output=" + Il2CppAssemblyUnhollower.OutputDirectory),
                ("--mscorlib=" + MSCORLIB_Path),
                ("--unity=" + UnityDependencies.BaseFolder),
                "--blacklist-assembly=Mono.Security",
                "--blacklist-assembly=Newtonsoft.Json",
                "--blacklist-assembly=Valve.Newtonsoft.Json"
            }))
            {
                Logger.LogError("Failed to Execute Il2CppAssemblyUnhollower!");
                return false;
            }

            using (var md5 = MD5.Create())
            {
                using (var stream = File.OpenRead(GameAssembly_Path))
                {
                    var hash = md5.ComputeHash(stream);
                    localConfig.GameAssemblyHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                }
            }
            localConfig.Save(localConfigPath);

            return true;
        }

        private static void Cleanup()
        {
            if (localConfig.OldFiles.Count() > 0)
            {
                for (int i = 0; i < localConfig.OldFiles.Count(); i++)
                {
                    string oldFile = localConfig.OldFiles[i];
                    if (!string.IsNullOrEmpty(oldFile))
                    {
                        string oldFilePath = Path.Combine(AssemblyFolder, oldFile);
                        if (File.Exists(oldFilePath))
                            File.Delete(oldFilePath);
                    }
                }
                localConfig.OldFiles.Clear();
            }
            string[] files = Directory.GetFiles(Il2CppAssemblyUnhollower.OutputDirectory, "*.dll", SearchOption.TopDirectoryOnly);
            if (files.Length > 0)
            {
                for (int i = 0; i < files.Count(); i++)
                {
                    string file = files[i];
                    if (!string.IsNullOrEmpty(file))
                    {
                        string filename = Path.GetFileName(file);
                        localConfig.OldFiles.Add(filename);
                        File.Copy(file, Path.Combine(AssemblyFolder, filename), true);
                    }
                }
            }
            Directory.Delete(Il2CppAssemblyUnhollower.OutputDirectory, true);
            localConfig.Save(localConfigPath);
        }

        private static string SetupDirectory(string path) { if (!Directory.Exists(path)) Directory.CreateDirectory(path); return path; }
        private static void FixIl2CppDumperConfig() => File.WriteAllText(Path.Combine(Il2CppDumper.BaseFolder, "config.json"), Encoder.Encode(il2cppConfig, EncodeOptions.NoTypeHints | EncodeOptions.PrettyPrint));
    }

    internal class Package
    {
        internal string Version = null;
        internal string BaseFolder = null;
    }

    internal class Executable_Package : Package
    {
        internal string FileName = null;
        internal string OutputDirectory = null;

        private static void OverrideAppDomainBase(string @base)
        {
            var appDomainBase = ((AppDomainSetup)typeof(AppDomain).GetProperty("FusionStore", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(AppDomain.CurrentDomain, new object[0]));
            appDomainBase.ApplicationBase = @base;
            Directory.SetCurrentDirectory(@base);
        }

        internal bool Execute(string[] argv)
        {
            string assembly_path = Path.Combine(BaseFolder, FileName);
            if (File.Exists(assembly_path))
            {
                var originalCwd = AppDomain.CurrentDomain.BaseDirectory;
                OverrideAppDomainBase(BaseFolder + Path.DirectorySeparatorChar);
                var generatorProcessInfo = new ProcessStartInfo(assembly_path);
                generatorProcessInfo.Arguments = String.Join(" ", argv.Where(s => !String.IsNullOrEmpty(s)).Select(it => $"\"{it}\""));
                generatorProcessInfo.UseShellExecute = false;
                generatorProcessInfo.RedirectStandardOutput = true;
                generatorProcessInfo.CreateNoWindow = true;
                var process = Process.Start(generatorProcessInfo);
                if (process == null)
                {
                    Logger.LogError("Unable to Start " + FileName + "!");
                    OverrideAppDomainBase(originalCwd);
                }
                else
                {
                    var stdout = process.StandardOutput;
                    while (!stdout.EndOfStream)
                    {
                        var line = stdout.ReadLine();
                        Logger.Log(line);
                    }
                    while (!process.HasExited)
                        Thread.Sleep(100);
                    OverrideAppDomainBase(originalCwd);
                    return (process.ExitCode == 0);
                }
            }
            return false;
        }
    }

    internal class Il2CppConfig
    {
        public bool DumpMethod = true;
        public bool DumpField = true;
        public bool DumpProperty = true;
        public bool DumpAttribute = true;
        public bool DumpFieldOffset = false;
        public bool DumpMethodOffset = false;
        public bool DumpTypeDefIndex = false;
        public bool GenerateDummyDll = true;
        public bool GenerateScript = false;
        public bool RequireAnyKey = false;
        public bool ForceIl2CppVersion = false;
        public float ForceVersion = 24.3f;
    }

    internal class LocalConfig
    {
        public string UnityVersion = null;
        public string GameAssemblyHash = null;
        public string DumperVersion = null;
        public string UnhollowerVersion = null;
        public List<string> OldFiles = new List<string>();
        public void Save(string path) => File.WriteAllText(path, Encoder.Encode(this, EncodeOptions.NoTypeHints | EncodeOptions.IncludePublicProperties | EncodeOptions.PrettyPrint));
    }
}
