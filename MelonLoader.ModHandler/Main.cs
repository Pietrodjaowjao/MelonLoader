﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ICSharpCode.SharpZipLib.Zip;
#pragma warning disable 0618

namespace MelonLoader
{
    public static class Main
    {
        internal static bool IsInitialized = false;
        internal static List<MelonMod> Mods = new List<MelonMod>();
        internal static MelonModGameAttribute CurrentGameAttribute = null;
        internal static bool IsVRChat = false;
        internal static bool IsBoneworks = false;
        internal static Type Il2CppObjectBaseType = null;
        internal static Assembly UnhollowerBaseLib = null;
        internal static Assembly Assembly_CSharp = null;
        private static bool ShouldCheckForUiManager = true;
        private static Type VRCUiManager = null;
        private static PropertyInfo VRCUiManager_Instance = null;

        private static void Initialize()
        {
            CurrentGameAttribute = new MelonModGameAttribute(Imports.GetCompanyName(), Imports.GetProductName());
            if (Imports.IsIl2CppGame())
                Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            if (!Imports.IsDebugMode()
#if !DEBUG
            && Environment.CommandLine.Contains("--melonloader.console")
#endif
            )
            {
                Console.Enabled = true;
                Console.Create();
            }

            if (Imports.IsIl2CppGame())
            {
                IsVRChat = CurrentGameAttribute.IsGame("VRChat", "VRChat");
                IsBoneworks = CurrentGameAttribute.IsGame("Stress Level Zero", "BONEWORKS");

                Assembly_CSharp = Assembly.Load("Assembly-CSharp");

                UnhollowerBaseLib = Assembly.Load("UnhollowerBaseLib");
                if (NETFrameworkFix.Assembly_op_Inequality(UnhollowerBaseLib, null))
                {
                    Il2CppObjectBaseType = UnhollowerBaseLib.GetType("UnhollowerBaseLib.Il2CppObjectBase");
                    UnhollowerSupport.FixLoggerEvents();
                }
            }

            MelonModLogger.Log("------------------------------");
            MelonModLogger.Log("Unity " + Imports.GetUnityVersion());
            MelonModLogger.Log("Developer: " + CurrentGameAttribute.Developer);
            MelonModLogger.Log("GameName: " + CurrentGameAttribute.GameName);
            MelonModLogger.Log("Version: " + Imports.GetGameVersion());
            MelonModLogger.Log("------------------------------");
            MelonModLogger.Log("Using v" + BuildInfo.Version + " Open-Beta");
            MelonModLogger.Log("------------------------------");

            bool no_mods = true;
            string modDirectory = Path.Combine(Environment.CurrentDirectory, "Mods");
            if (!Directory.Exists(modDirectory))
                Directory.CreateDirectory(modDirectory);
            else
            {
                // DLL
                string[] files = Directory.GetFiles(modDirectory, "*.dll");
                if (files.Length > 0)
                {
                    foreach (string s in files)
                    {
                        if (!File.Exists(s) || !s.EndsWith(".dll", true, null))
                            return;
                        try
                        {
                            byte[] data = File.ReadAllBytes(s);
                            if (data.Length > 0)
                            {
                                LoadAssembly(data);
                            }
                            else
                                MelonModLogger.LogError("Unable to load " + s);
                        }
                        catch (Exception e)
                        {
                            MelonModLogger.LogError("Unable to load " + s + ":\n" + e.ToString());
                        }
                        MelonModLogger.Log("------------------------------");
                    }
                    if (Mods.Count() > 0)
                        no_mods = false;
                }

                // ZIP
                if (Imports.IsIl2CppGame() || !Imports.IsOldMono())
                {
                    string[] zippedFiles = Directory.GetFiles(modDirectory, "*.zip");
                    if (zippedFiles.Length > 0)
                    {
                        foreach (string file in zippedFiles)
                        {
                            if (!File.Exists(file) || !file.EndsWith(".zip", true, null))
                                return;
                            try
                            {
                                using (var fileStream = File.OpenRead(file))
                                {
                                    using (var zipInputStream = new ZipInputStream(fileStream))
                                    {
                                        ZipEntry entry;
                                        while ((entry = zipInputStream.GetNextEntry()) != null)
                                        {
                                            if (Path.GetFileName(entry.Name).Length <= 0 || !Path.GetFileName(entry.Name).EndsWith(".dll"))
                                                continue;

                                            using (var unzippedFileStream = new MemoryStream())
                                            {
                                                int size = 0;
                                                byte[] buffer = new byte[4096];
                                                while (true)
                                                {
                                                    size = zipInputStream.Read(buffer, 0, buffer.Length);
                                                    if (size > 0)
                                                        unzippedFileStream.Write(buffer, 0, size);
                                                    else
                                                        break;
                                                }
                                                LoadAssembly(unzippedFileStream.ToArray());
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                MelonModLogger.LogError("Unable to load " + file + ":\n" + e.ToString());
                            }
                            MelonModLogger.Log("------------------------------");
                        }
                        if (Mods.Count() > 0)
                            no_mods = false;
                    }
                }
            }
            if (no_mods)
            {
                MelonModLogger.Log("No Mods Loaded!");
                MelonModLogger.Log("------------------------------");
            }
            else
                MelonModComponent.Create();
        }

        private static void LoadModsFromAssembly(Assembly assembly)
        {
            MelonModInfoAttribute modInfoAttribute = assembly.GetCustomAttributes(false).First(x => (x.GetType() == typeof(MelonModInfoAttribute))) as MelonModInfoAttribute;
            if ((modInfoAttribute != null) && (modInfoAttribute.ModType != null) && modInfoAttribute.ModType.IsSubclassOf(typeof(MelonMod)))
            {
                MelonModLogger.Log(modInfoAttribute.Name + (!string.IsNullOrEmpty(modInfoAttribute.Version) ? (" v" + modInfoAttribute.Version) : "") + (!string.IsNullOrEmpty(modInfoAttribute.Author) ? (" by " + modInfoAttribute.Author) : "") + (!string.IsNullOrEmpty(modInfoAttribute.DownloadLink) ? (" (" + modInfoAttribute.DownloadLink + ")") : ""));

                bool should_continue = false;
                bool isUniversal = false;
                MelonModGameAttribute[] modGameAttributes = assembly.GetCustomAttributes(typeof(MelonModGameAttribute), true) as MelonModGameAttribute[];
                int modGameAttributes_Count = modGameAttributes.Length;
                if (modGameAttributes_Count > 0)
                {
                    for (int i = 0; i < modGameAttributes_Count; i++)
                    {
                        MelonModGameAttribute modGameAttribute = modGameAttributes[i];
                        if (CurrentGameAttribute.IsCompatible(modGameAttribute))
                        {
                            isUniversal = CurrentGameAttribute.IsCompatibleBecauseUniversal(modGameAttribute);
                            should_continue = true;
                            break;
                        }
                    }
                }
                else
                {
                    isUniversal = true;
                    should_continue = true;
                }
                if (should_continue)
                {
                    try
                    {
                        MelonMod modInstance = Activator.CreateInstance(modInfoAttribute.ModType) as MelonMod;
                        if (modInstance != null)
                        {
                            modInstance.IsUniversal = isUniversal;
                            modInstance.InfoAttribute = modInfoAttribute;
                            if (modGameAttributes_Count > 0)
                                modInstance.GameAttributes = modGameAttributes;
                            else
                                modInstance.GameAttributes = null;
                            Mods.Add(modInstance);
                            MelonModLogger.LogModStatus((modGameAttributes_Count > 0) ? (isUniversal ? 0 : 1) : 2);
                        }
                        else
                            MelonModLogger.LogError("Unable to load Mod in " + assembly.GetName() + "! Failed to Create Instance!");
                    }
                    catch (Exception e) { MelonModLogger.LogError("Unable to load Mod in " + assembly.GetName() + "! " + e.ToString()); }
                }
                else
                    MelonModLogger.LogModStatus(3);
            }
        }

        private static void LoadAssembly(byte[] data)
        {
            Assembly asm = Assembly.Load(data);
            if (NETFrameworkFix.Assembly_op_Inequality(asm, null))
                LoadModsFromAssembly(asm);
            else
                MelonModLogger.LogError("Unable to load " + asm);
        }

        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            try { return assembly.GetTypes(); } catch (ReflectionTypeLoadException e) { MelonModLogger.LogError("An Error occured while getting Types from Assembly " + assembly.GetName().Name + ". Returning Types from Error.\n" + e); return e.Types.Where(t => t != null); }
        }

        internal static void OnApplicationStart()
        {
            if (Mods.Count() > 0)
                foreach (MelonMod mod in Mods)
                    try { mod.OnApplicationStart(); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
            IsInitialized = true;
        }

        internal static void OnLevelIsLoading()
        {
            if (IsInitialized)
            {
                if (Mods.Count() > 0)
                    foreach (MelonMod mod in Mods)
                        try { mod.OnLevelIsLoading(); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
            }
        }

        internal static void OnLevelWasLoaded(int level)
        {
            if (IsInitialized)
            {
                if (Mods.Count() > 0)
                    foreach (MelonMod mod in Mods)
                        try { mod.OnLevelWasLoaded(level); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
            }
        }

        internal static void OnLevelWasInitialized(int level)
        {
            if (IsInitialized && (Mods.Count() > 0))
                foreach (MelonMod mod in Mods)
                        try { mod.OnLevelWasInitialized(level); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
        }

        internal static void OnUpdate()
        {
            if (IsInitialized)
            {
                SceneManager.CheckForSceneChange();
                if (Imports.IsIl2CppGame() && IsVRChat)
                    VRChat_CheckUiManager();
                if (Mods.Count() > 0)
                    foreach (MelonMod mod in Mods)
                        try { mod.OnUpdate(); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
                if (Imports.IsIl2CppGame())
                    MelonCoroutines.Process();
            }
        }

        internal static void OnFixedUpdate()
        {
            if (IsInitialized)
            {
                if (Mods.Count() > 0)
                    foreach (MelonMod mod in Mods)
                        try { mod.OnFixedUpdate(); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
                if (Imports.IsIl2CppGame())
                    MelonCoroutines.ProcessWaitForFixedUpdate();
            }
        }

        internal static void OnLateUpdate()
        {
            if (IsInitialized && (Mods.Count() > 0))
                foreach (MelonMod mod in Mods)
                    try { mod.OnLateUpdate(); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
        }

        internal static void OnGUI()
        {
            if (IsInitialized)
            {
                if (Mods.Count() > 0)
                    foreach (MelonMod mod in Mods)
                        try { mod.OnGUI(); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
            }
        }

        internal static void OnApplicationQuit()
        {
            if (IsInitialized && (Mods.Count() > 0))
                foreach (MelonMod mod in Mods)
                    try { mod.OnApplicationQuit(); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
            ModPrefs.SaveConfig();
            if (Imports.IsIl2CppGame())
                NET_SDK.Harmony.Manager.UnpatchAll();
            Harmony.HarmonyInstance.UnpatchAllInstances();
        }

        internal static void OnModSettingsApplied()
        {
            if (IsInitialized && (Mods.Count() > 0))
                foreach (MelonMod mod in Mods)
                    try { mod.OnModSettingsApplied(); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
        }

        private static void VRChat_CheckUiManager()
        {
            if (IsInitialized && ShouldCheckForUiManager)
            {
                if (VRCUiManager == null)
                    VRCUiManager = Assembly_CSharp.GetType("VRCUiManager");
                if (VRCUiManager != null)
                {
                    if (VRCUiManager_Instance == null)
                        VRCUiManager_Instance = VRCUiManager.GetProperty("field_Protected_VRCUiManager_0");
                    if (VRCUiManager_Instance != null)
                    {
                        object returnval = VRCUiManager_Instance.GetValue(null, new object[0]);
                        if (returnval != null)
                        {
                            ShouldCheckForUiManager = false;
                            if (Mods.Count() > 0)
                                foreach (MelonMod mod in Mods)
                                    try { mod.VRChat_OnUiManagerInit(); } catch (Exception ex) { MelonModLogger.LogModError(ex.ToString(), mod.InfoAttribute.Name); }
                        }
                    }
                }
            }
        }
    }
}