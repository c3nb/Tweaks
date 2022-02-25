using System;
using System.IO;
using HarmonyLib;
using System.Linq;
using UnityEngine;
using System.Reflection;
using System.Xml.Serialization;
using System.Collections.Generic;
using static UnityModManagerNet.UnityModManager;

namespace Tweaks
{
    [AttributeUsage(AttributeTargets.Class)]
    public class TweakAttribute : Attribute
    {
        public TweakAttribute(string name, string desc = "")
        {
            Name = name;
            Description = desc;
        }
        public string Name { get; }
        public string Description { get; }
        public Type PatchesType { get; set; }
        public Type SettingsType { get; set; }
    }
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class SyncSettings : Attribute
    {
        public static Dictionary<Type, Tweak.Settings> Settings = new Dictionary<Type, Tweak.Settings>();
        public static void Load(ModEntry modEntry)
        {
            MethodInfo load = typeof(ModSettings).GetMethod(nameof(ModSettings.Load), (BindingFlags)15420, null, new Type[] { typeof(ModEntry) }, null);
            foreach (var type in modEntry.Assembly.GetTypes())
            {
                if (!type.IsSubclassOf(typeof(Tweak.Settings))) continue;
                try { Settings[type] = (Tweak.Settings)load.Invoke(null, new object[] { modEntry }); }
                catch { Settings[type] = (Tweak.Settings)Activator.CreateInstance(type); }
            }
        }
        public static void Sync(Tweak tweak)
        {
            void Sync(Type type)
            {
                foreach (var field in type.GetFields((BindingFlags)15420))
                {
                    SyncSettings sync = field.GetCustomAttribute<SyncSettings>();
                    if (sync != null)
                        field.SetValue(tweak, Settings[field.FieldType]);
                }
                foreach (var prop in type.GetProperties((BindingFlags)15420))
                {
                    SyncSettings sync = prop.GetCustomAttribute<SyncSettings>();
                    if (sync != null)
                        prop.SetValue(tweak, Settings[prop.PropertyType]);
                }
            }
            Type tType;
            Sync(tType = tweak.GetType());
            TweakAttribute attr;
            if ((attr = tType.GetCustomAttribute<TweakAttribute>())?.PatchesType != null)
                Sync(attr.PatchesType);
        }
        public static void Save(ModEntry modEntry)
        {
            foreach (var setting in Settings.Values)
                setting.Save(modEntry);
        }
    }
    public class Tweak
    {
        public static ModEntry TweakEntry { get; private set; }
        public static ModEntry.ModLogger Logger { get; private set; }
        internal static IEnumerable<Type> TweakTypes { get; private set; }
        internal static List<Runner> Runners { get; private set; }
        public static void Setup(ModEntry modEntry, bool setOnToggleOnGUIOnSaveGUI = true)
        {
            TweakEntry = modEntry;
            Logger = modEntry.Logger;
            TweakTypes = modEntry.Assembly.GetTypes().Where(t => t.GetCustomAttribute<TweakAttribute>() != null).OrderBy(t => t.GetCustomAttribute<TweakAttribute>().Name);
            Runners = new List<Runner>();
            SyncSettings.Load(modEntry);
            if (setOnToggleOnGUIOnSaveGUI)
            {
                modEntry.OnToggle = (mod, value) => OnToggle(value);
                modEntry.OnGUI = mod => TweaksGUI();
                modEntry.OnSaveGUI = mod => Save();
            }
        }
        public static bool OnToggle(bool value)
        {
            if (value)
                Start();
            else Stop();
            return true;
        }
        public static void Start()
        {
            foreach (Type tweakType in TweakTypes)
            {
                ConstructorInfo constructor = tweakType.GetConstructor(new Type[] { });
                Tweak tweak = (Tweak)constructor.Invoke(null);
                TweakAttribute attr = tweakType.GetCustomAttribute<TweakAttribute>();
                Settings settings = null;
                if (attr.SettingsType != null && SyncSettings.Settings.TryGetValue(attr.SettingsType, out Settings setting))
                    settings = setting;
                else settings = new Settings();
                Runner runner = new Runner(tweak, settings, tweakType == TweakTypes.Last());
                Runners.Add(runner);
                SyncSettings.Sync(tweak);
            }
            Runners.ForEach(runner => runner.Start());
        }
        public static void Stop()
        {
            Runners.ForEach(runner => runner.Stop());
            Runners.Clear();
            Save();
        }
        public static void Save()
            => SyncSettings.Save(TweakEntry);
        public static void TweaksGUI()
        {
            foreach (Runner runner in Runners)
                runner.OnGUI();
        }
        public static void BeginIndent(float indentSize = 20f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(indentSize);
            GUILayout.BeginVertical();
        }
        public static void EndIndent()
        {
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }
        public virtual void OnGUI() { }
        public virtual void OnEnable() { }
        public virtual void OnDisable() { }
        public virtual void OnUpdate() { }
        public virtual void OnHideGUI() { }
        internal class Runner
        {
            public static GUIStyle Expan;
            public static GUIStyle Enabl;
            public static GUIStyle Descr;
            public static bool StyleInitialized = false;
            public Tweak Tweak { get; }
            public TweakAttribute Metadata { get; }
            public Settings Settings { get; }
            public List<PatchAttribute> Patches { get; }
            public Harmony Harmony { get; }
            public bool Last { get; }
            public Runner(Tweak tweak, Settings settings, bool last)
            {
                Tweak = tweak;
                Metadata = tweak.GetType().GetCustomAttribute<TweakAttribute>();
                Settings = settings;
                Patches = new List<PatchAttribute>();
                Harmony = new Harmony($"Tweaks.{Metadata.Name}");
                if (Metadata.PatchesType != null)
                    AddPatches(Metadata.PatchesType);
                AddPatches(tweak.GetType());
                Last = last;
            }
            public void Start()
            {
                if (Settings.IsEnabled)
                    Enable();
            }
            public void Stop()
            {
                if (Settings.IsEnabled)
                    Disable();
            }
            public void Enable()
            {
                Tweak.OnEnable();
                foreach (var patch in Patches)
                {
                    if (patch.Target == null)
                        patch.Target = PatchAttribute.FindMethod(patch.Patch.Name.Replace(patch.Splitter, '.'), patch.MethodType, false);
                    if (patch.Prefix)
                        Harmony.Patch(patch.Target, new HarmonyMethod(patch.Patch));
                    else Harmony.Patch(patch.Target, postfix: new HarmonyMethod(patch.Patch));
                }
            }
            public void Disable()
            {
                if (!Settings.IsEnabled) return;
                Tweak.OnDisable();
                Harmony.UnpatchAll(Harmony.Id);
            }
            public void OnGUI()
            {
                if (!StyleInitialized)
                {
                    Expan = new GUIStyle()
                    {
                        fixedWidth = 10,
                        normal = new GUIStyleState() { textColor = Color.white },
                        fontSize = 15,
                        margin = new RectOffset(4, 2, 6, 6),
                    };
                    Enabl = new GUIStyle(GUI.skin.toggle)
                    {
                        margin = new RectOffset(0, 4, 4, 4),
                    };
                    Descr = new GUIStyle(GUI.skin.label)
                    {
                        fontStyle = FontStyle.Italic,
                    };
                    StyleInitialized = true;
                }
                GUILayout.BeginHorizontal();
                bool newIsExpanded = GUILayout.Toggle(Settings.IsExpanded, Settings.IsEnabled ? (Settings.IsExpanded ? "◢" : "▶") : "", Expan);
                bool newIsEnabled = GUILayout.Toggle(Settings.IsEnabled, Metadata.Name, Enabl);
                GUILayout.Label("-");
                GUILayout.Label(Metadata.Description, Descr);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                if (newIsEnabled != Settings.IsEnabled)
                {
                    Settings.IsEnabled = newIsEnabled;
                    if (newIsEnabled)
                    {
                        Enable();
                        newIsExpanded = true;
                    }
                    else Disable();
                }
                if (newIsExpanded != Settings.IsExpanded)
                {
                    Settings.IsExpanded = newIsExpanded;
                    if (!newIsExpanded)
                        Tweak.OnHideGUI();
                }
                if (Settings.IsExpanded && Settings.IsEnabled)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Space(24f);
                    GUILayout.BeginVertical();
                    Tweak.OnGUI();
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                    if (!Last)
                        GUILayout.Space(12f);
                }
            }
            public void OnUpdate()
            {
                if (Settings.IsEnabled) 
                    Tweak.OnUpdate();
            }
            public void OnHideGUI()
            {
                if (Settings.IsEnabled)
                    Tweak.OnHideGUI();
            }
            public void AddPatches(Type patchesType)
            {
                foreach (Type type in GetNestedTypes(patchesType))
                {
                    foreach (MethodInfo method in type.GetMethods((BindingFlags)15420))
                    {
                        PatchAttribute patch = method.GetCustomAttribute<PatchAttribute>();
                        if (patch != null)
                        {
                            if (patch.IsValid)
                            {
                                patch.Patch = method;
                                Patches.Add(patch);
                            }
                        }
                    }
                }
            }
            public static List<Type> GetNestedTypes(Type type)
            {
                void GetNestedTypes(Type ty, List<Type> toContain)
                {
                    foreach (Type t in ty.GetNestedTypes((BindingFlags)15420))
                    {
                        toContain.Add(t);
                        GetNestedTypes(t, toContain);
                    }
                }
                var container = new List<Type>();
                GetNestedTypes(type, container);
                return container;
            }
        }
        public class Settings : ModSettings
        {
            public bool IsEnabled { get; set; }
            public bool IsExpanded { get; set; }
            public override string GetPath(ModEntry modEntry)
                => Path.Combine(modEntry.Path, GetType().Name + ".xml");
            public override void Save(ModEntry modEntry)
            {
                var filepath = GetPath(modEntry);
                try
                {
                    using (var writer = new StreamWriter(filepath))
                    {
                        var serializer = new XmlSerializer(GetType());
                        serializer.Serialize(writer, this);
                    }
                }
                catch (Exception e)
                {
                    modEntry.Logger.Error($"Can't save {filepath}.");
                    modEntry.Logger.LogException(e);
                }
            }
        }
        public class PatchAttribute : Attribute
        {
            public static int Version = (int)AccessTools.Field(typeof(GCNS), "releaseNumber").GetValue(null);
            public MethodBase Target { get; internal set; }
            internal MethodInfo Patch { get; set; }
            public bool Prefix { get; set; } = false;
            public GSCS MethodType { get; }
            public char Splitter { get; } = '_';
            public int MinVersion { get; set; } = -1;
            public int MaxVersion { get; set; } = -1;
            internal PatchAttribute(MethodBase target)
                => Target = target;
            public PatchAttribute(Type type, string name, params Type[] parameterTypes) : this(type, name, GSCS.None, parameterTypes) { }
            public PatchAttribute(Type type, string name, GSCS methodType, params Type[] parameterTypes) : this(methodType)
            {
                if (methodType != GSCS.None)
                {
                    switch (methodType)
                    {
                        case GSCS.Getter:
                            var prop = type.GetProperty(name, (BindingFlags)15420);
                            Target = prop.GetGetMethod(true);
                            break;
                        case GSCS.Setter:
                            prop = type.GetProperty(name, (BindingFlags)15420);
                            Target = prop.GetSetMethod(true);
                            break;
                        case GSCS.Constructor:
                            Target = type.GetConstructor((BindingFlags)15420, null, parameterTypes, null);
                            break;
                        case GSCS.StaticConstructor:
                            Target = type.TypeInitializer;
                            break;
                    }
                }
                else Target = parameterTypes.Any() ? type.GetMethod(name, (BindingFlags)15420, null, parameterTypes, null) : type.GetMethod(name, (BindingFlags)15420);
            }
            public PatchAttribute(string fullName, GSCS methodType = GSCS.None) : this(methodType)
                => Target = FindMethod(fullName, MethodType, true);
            public PatchAttribute(GSCS methodType = GSCS.None)
                => MethodType = methodType;
            public bool IsValid => Version >= MinVersion && Version <= MaxVersion && Target != null;
            public static MethodBase FindMethod(string fullName, GSCS methodType = GSCS.None, bool filterProp = true, bool throwOnNull = false)
            {
                var split = fullName.Split('.');
                if ((split[0] == "get" || split[0] == "set") && filterProp)
                {
                    var array = new string[split.Length - 1];
                    Array.Copy(split, 1, array, 0, array.Length);
                    split = array;
                }
                var paramBraces = (string)null;
                if (fullName.Contains("("))
                    split = fullName.Replace(paramBraces = fullName.Substring(fullName.IndexOf('(')), "").Split('.');
                var method = split.Last();
                var type = fullName.Replace($".{(method.Contains("ctor") ? $".{method}" : method)}{paramBraces}", "");
                var isParam = false;
                var parameterTypes = new List<Type>();
                if (paramBraces != null)
                {
                    isParam = true;
                    var parametersString = paramBraces.Replace("(", "").Replace(")", "");
                    if (string.IsNullOrWhiteSpace(parametersString))
                        goto Skip;
                    var parameterSplit = parametersString.Split(',');
                    parameterTypes = parameterSplit.Select(s => AccessTools.TypeByName(s)).ToList();
                }
            Skip:
                var decType = AccessTools.TypeByName(type);
                if (decType == null && throwOnNull)
                    throw new NullReferenceException($"Cannot Find Type! ({type})");
                var parameterArr = parameterTypes.ToArray();
                var result = (MethodBase)null;
                if (methodType != GSCS.None)
                {
                    var prop = decType.GetProperty(method, (BindingFlags)15420);
                    switch (methodType)
                    {
                        case GSCS.Getter:
                            result = prop.GetGetMethod(true);
                            break;
                        case GSCS.Setter:
                            result = prop.GetSetMethod(true);
                            break;
                    }
                }
                else
                {
                    if (method == "ctor")
                        result = decType.GetConstructor((BindingFlags)15420, null, parameterArr, null);
                    else if (method == "cctor")
                        result = decType.TypeInitializer;
                    else
                        result = isParam ? decType.GetMethod(method, (BindingFlags)15420, null, parameterTypes.ToArray(), null) : decType.GetMethod(method, (BindingFlags)15420);
                }
                if (result == null && throwOnNull)
                    throw new NullReferenceException($"Cannot Find Method! ({method})");
                return result;
            }
        }
    }
    public enum GSCS
    {
        None,
        Getter,
        Setter,
        Constructor,
        StaticConstructor
    }
}
