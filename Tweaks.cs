using System;
using System.IO;
using HarmonyLib;
using System.Linq;
using UnityEngine;
using System.Reflection;
using Rnr = Tweaks.Runner;
using System.Xml.Serialization;
using System.Collections.Generic;
using static UnityModManagerNet.UnityModManager;

namespace Tweaks
{
    #region Publics
    public class Tweak
    {
        static Tweak()
        {
            Tweaks = new Dictionary<Type, Tweak>();
        }
        public static Dictionary<Type, Tweak> Tweaks { get; }
        public static ModEntry TweakEntry { get; internal set; }
        public void Log(object obj)
            => TweakEntry.Logger.Log($"[{Runner.Metadata.Name}] {obj}");
        public void RegisterInnerTweak(Type tweakType)
        {
            var innerTweaks = Runner.InnerTweaks;
            if (innerTweaks.Any())
                innerTweaks.Last().Last = false;
            var tweak = Rnr.InitTweak(tweakType, out var settings);
            innerTweaks.Add(new TweakRunner(tweak, settings, true));
        }
        public void Enable()
        {
            Runner.Enable();
        }
        public void Disable()
        {
            Runner.Disable();
        }
        public virtual string Name { get; }
        public virtual string Description { get; }
        public virtual void OnGUI() { }
        public virtual void OnPatch() { }
        public virtual void OnUnpatch() { }
        public virtual void OnEnable() { }
        public virtual void OnDisable() { }
        public virtual void OnUpdate() { }
        public virtual void OnHideGUI() { }
        private TweakRunner Runner { get; set; }
    }
    public class TweakSettings : ModSettings
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
    public static class Runner
    {
        static Runner()
        {
            Types = new List<Type>();
            OnHarmony = new Harmony("onHarmony");
            Runners = new List<TweakRunner>();
            RunnersDict = new Dictionary<Type, TweakRunner>();
            OT = typeof(Runner).GetMethod(nameof(Runner.OnToggle), (BindingFlags)15420);
            OG = typeof(Runner).GetMethod(nameof(Runner.OnGUI), (BindingFlags)15420);
            OS = typeof(Runner).GetMethod(nameof(Runner.OnSaveGUI), (BindingFlags)15420);
            OH = typeof(Runner).GetMethod(nameof(Runner.OnHideGUI), (BindingFlags)15420);
            OU = typeof(Runner).GetMethod(nameof(Runner.OnUpdate), (BindingFlags)15420);
            R = typeof(Tweak).GetProperty("Runner", (BindingFlags)15420);
        }
        private static readonly MethodInfo OT;
        private static readonly MethodInfo OG;
        private static readonly MethodInfo OS;
        private static readonly MethodInfo OH;
        private static readonly MethodInfo OU;
        private static readonly PropertyInfo R;
        private static Harmony OnHarmony { get; }
        public static void Run(ModEntry modEntry, bool preGUI = false)
        {
            Tweak.TweakEntry = modEntry;
            SyncSettings.Load(modEntry);
            TweakTypes = modEntry.Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(Tweak)) && !t.IsNested).ToList();
            if (modEntry.OnToggle == null)
                modEntry.OnToggle = (m, v) => OnToggle(v);
            else OnHarmony.Patch(modEntry.OnToggle.Method, postfix: new HarmonyMethod(OT));
            if (modEntry.OnGUI == null)
                modEntry.OnGUI = (m) => OnGUI();
            else
            {
                if (preGUI)
                    OnHarmony.Patch(modEntry.OnGUI.Method, new HarmonyMethod(OG));
                else OnHarmony.Patch(modEntry.OnGUI.Method, postfix: new HarmonyMethod(OG));
            }
            if (modEntry.OnHideGUI == null)
                modEntry.OnHideGUI = (m) => OnHideGUI();
            else OnHarmony.Patch(modEntry.OnHideGUI.Method, postfix: new HarmonyMethod(OH));
            if (modEntry.OnSaveGUI == null)
                modEntry.OnSaveGUI = (m) => OnSaveGUI();
            else OnHarmony.Patch(modEntry.OnSaveGUI.Method, postfix: new HarmonyMethod(OS));
            if (modEntry.OnUpdate == null)
                modEntry.OnUpdate = (m, dt) => OnUpdate();
            else OnHarmony.Patch(modEntry.OnUpdate.Method, postfix: new HarmonyMethod(OU));
        }
        private static List<Type> TweakTypes { get; set; }
        private static Dictionary<Type, TweakRunner> RunnersDict { get; }
        private static List<TweakRunner> Runners { get; }
        private static List<Type> Types { get; }
        private static void Start()
        {
            foreach (Type tweakType in TweakTypes.OrderBy(t => t.GetCustomAttribute<TweakAttribute>().Name).OrderBy(t => t.GetCustomAttribute<TweakAttribute>().Priority))
                RegisterTweak(tweakType, false);
            Runners.ForEach(runner =>
            {
                var tweak = runner.Tweak;
                SyncSettings.Sync(tweak.GetType(), tweak);
                SyncTweak.Sync(tweak.GetType(), tweak);
                if (runner.Metadata.PatchesType != null)
                {
                    SyncSettings.Sync(runner.Metadata.PatchesType, tweak);
                    SyncTweak.Sync(runner.Metadata.PatchesType, tweak);
                }
            });
            Runners.ForEach(runner => runner.Start());
        }
        private static void Stop()
        {
            Runners.ForEach(runner => runner.Stop());
            Runners.Clear();
            OnSaveGUI();
        }
        private static bool OnToggle(bool value)
        {
            if (value)
                Start();
            else Stop();
            return true;
        }
        private static void OnHideGUI()
        {
            Runners.ForEach(runner => runner.OnHideGUI());
        }
        private static void OnGUI()
        {
            Runners.ForEach(runner => runner.OnGUI());
        }
        private static void OnSaveGUI()
            => SyncSettings.Save(Tweak.TweakEntry);
        private static void OnUpdate()
        {
            Runners.ForEach(runner => runner.OnUpdate());
        }
        public static void RegisterTweak(Type tweakType, bool sync = true)
            => RegisterTweakInternal(tweakType, null, false, sync);
        internal static void RegisterTweakInternal(Type tweakType, TweakRunner outerRunner, bool last, bool sync = true)
        {
            try
            {
                if (tweakType.BaseType != typeof(Tweak) && outerRunner == null) return;
                if (!TweakTypes.Contains(tweakType)) TweakTypes.Add(tweakType);
                if (Types.Contains(tweakType)) return;
                Tweak tweak = InitTweak(tweakType, out var settings);
                TweakRunner runner = new TweakRunner(tweak, settings, last, outerRunner);
                if (outerRunner != null)
                    outerRunner.InnerTweaks.Add(runner);
                var nestedTypes = tweakType.GetNestedTypes((BindingFlags)15420).Where(t => t.IsSubclassOf(typeof(Tweak)));
                if (nestedTypes.Any())
                {
                    var lastType = nestedTypes.Last();
                    foreach (Type type in nestedTypes)
                        RegisterTweakInternal(type, runner, type == lastType, sync);
                }
                R.SetValue(tweak, runner);
                if (outerRunner == null)
                    Runners.Add(runner);
                if (sync)
                {
                    SyncSettings.Sync(tweak.GetType(), tweak);
                    SyncTweak.Sync(tweak.GetType(), tweak);
                    if (runner.Metadata.PatchesType != null)
                    {
                        SyncSettings.Sync(runner.Metadata.PatchesType, tweak);
                        SyncTweak.Sync(runner.Metadata.PatchesType, tweak);
                    }
                }
                Types.Add(tweakType);
            }
            catch (Exception e)
            {
                Tweak.TweakEntry.Logger.Log(tweakType.ToString());
                throw e;
            }
        }
        internal static Tweak InitTweak(Type tweakType, out TweakSettings settings)
        {
            ConstructorInfo constructor = tweakType.GetConstructor(new Type[] { });
            Tweak tweak = (Tweak)constructor.Invoke(null);
            TweakAttribute attr = tweakType.GetCustomAttribute<TweakAttribute>();
            if (attr == null)
                throw new NullReferenceException("Cannot Find Tweak Metadata! (TweakAttribute)");
            if (!(attr.SettingsType != null && SyncSettings.Settings.TryGetValue(attr.SettingsType, out settings)))
                settings = new TweakSettings();
            return tweak;
        }
        public static void UnregisterTweak(Type tweakType)
        {
            var runner = Runners.Find(r => r.Tweak.GetType() == tweakType);
            runner.Stop();
            Runners.Remove(runner);
        }
    }
    //SimpleGUILayout
    public static class SGL
    {
        public static void BI(float indentSize = 20f)
        {
            BH();
            S(indentSize);
            BV();
        }
        public static void EI()
        {
            EV();
            EH();
        }
        public static Color CRgbS(Color color)
        {
            float oldR = Mathf.Round(color.r * 255);
            float oldG = Mathf.Round(color.g * 255);
            float oldB = Mathf.Round(color.b * 255);
            float newR = NS("R:", oldR, 0, 255, 300f, 1, 40f);
            float newG = NS("G:", oldG, 0, 255, 300f, 1, 40f);
            float newB = NS("B:", oldB, 0, 255, 300f, 1, 40f);
            if (oldR != newR || oldG != newG || oldB != newB)
            {
                return new Color(newR / 255, newR / 255, newR / 255);
            }
            return color;
        }
        public static Color CRgbaS(Color color)
        {
            float oldR = Mathf.Round(color.r * 255);
            float oldG = Mathf.Round(color.g * 255);
            float oldB = Mathf.Round(color.b * 255);
            float oldA = Mathf.Round(color.a * 255);
            float newR = NS("R:", oldR, 0, 255, 300f, 1, 40f);
            float newG = NS("G:", oldG, 0, 255, 300f, 1, 40f);
            float newB = NS("B:", oldB, 0, 255, 300f, 1, 40f);
            float newA = NS("A:", oldA, 0, 255, 300f, 1, 40f);
            if (oldR != newR || oldG != newG || oldB != newB || oldA != newA)
            {
                return new Color(newR / 255, newR / 255, newR / 255, newA / 255);
            }
            return color;
        }
        public static (Color, Color) CRgbSP(Color color1, Color color2)
        {
            float newR1, newR2, newG1, newG2, newB1, newB2;
            float oldR1 = Mathf.Round(color1.r * 255);
            float oldG1 = Mathf.Round(color1.g * 255);
            float oldB1 = Mathf.Round(color1.b * 255);
            float oldR2 = Mathf.Round(color2.r * 255);
            float oldG2 = Mathf.Round(color2.g * 255);
            float oldB2 = Mathf.Round(color2.b * 255);
            (newR1, newR2) = NSP("R:", "R:", oldR1, oldR2, 0, 255, 300f, 1, 40f);
            (newG1, newG2) = NSP("G:", "G:", oldG1, oldG2, 0, 255, 300f, 1, 40f);
            (newB1, newB2) = NSP("B:", "B:", oldB1, oldB2, 0, 255, 300f, 1, 40f);
            if (oldR1 != newR1 || oldG1 != newG1 || oldB1 != newB1)
            {
                color1 = new Color(newR1 / 255, newG1 / 255, newB1 / 255);
            }
            if (oldR2 != newR2 || oldG2 != newG2 || oldB2 != newB2)
            {
                color2 = new Color(newR2 / 255, newG2 / 255, newB2 / 255);
            }
            return (color1, color2);
        }
        public static (Color, Color) CRgbaSP(Color color1, Color color2)
        {
            float newR1, newR2, newG1, newG2, newB1, newB2, newA1, newA2;
            float oldR1 = Mathf.Round(color1.r * 255);
            float oldG1 = Mathf.Round(color1.g * 255);
            float oldB1 = Mathf.Round(color1.b * 255);
            float oldA1 = Mathf.Round(color1.a * 255);
            float oldR2 = Mathf.Round(color2.r * 255);
            float oldG2 = Mathf.Round(color2.g * 255);
            float oldB2 = Mathf.Round(color2.b * 255);
            float oldA2 = Mathf.Round(color2.a * 255);
            (newR1, newR2) = NSP("R:", "R:", oldR1, oldR2, 0, 255, 300f, 1, 40f);
            (newG1, newG2) = NSP("G:", "G:", oldG1, oldG2, 0, 255, 300f, 1, 40f);
            (newB1, newB2) = NSP("B:", "B:", oldB1, oldB2, 0, 255, 300f, 1, 40f);
            (newA1, newA2) = NSP("A:", "A:", oldA1, oldA2, 0, 255, 300f, 1, 40f);
            if (oldR1 != newR1 || oldG1 != newG1 || oldB1 != newB1 || oldA1 != newA1)
            {
                color1 = new Color(newR1 / 255, newG1 / 255, newB1 / 255, newA1 / 255);
            }
            if (oldR2 != newR2 || oldG2 != newG2 || oldB2 != newB2 || oldA2 != newA2)
            {
                color2 = new Color(newR2 / 255, newG2 / 255, newB2 / 255, newA2 / 255);
            }
            return (color1, color2);
        }
        public static float NS(string name, float value, float leftValue, float rightValue, float sliderWidth, float roundNearest = 0, float labelWidth = 0, string valueFormat = "{0}")
        {
            BH();
            float newValue =
                NSC(
                    name,
                    value,
                    leftValue,
                    rightValue,
                    sliderWidth,
                    roundNearest,
                    labelWidth,
                    valueFormat);
            EH();
            return newValue;
        }
        public static (float, float) NSP(string name1, string name2, float value1, float value2, float leftValue, float rightValue, float sliderWidth, float roundNearest = 0, float labelWidth = 0, string valueFormat = "{0}")
        {
            BH();
            float newValue1 =
                NSC(
                    name1,
                    value1,
                    leftValue,
                    rightValue,
                    sliderWidth,
                    roundNearest,
                    labelWidth,
                    valueFormat);
            float newValue2 =
                NSC(
                    name2,
                    value2,
                    leftValue,
                    rightValue,
                    sliderWidth,
                    roundNearest,
                    labelWidth,
                    valueFormat);
            EH();
            return (newValue1, newValue2);
        }
        private static float NSC(string name, float value, float leftValue, float rightValue, float sliderWidth, float roundNearest = 0, float labelWidth = 0, string valueFormat = "{0}")
        {
            if (labelWidth == 0)
            {
                L(name);
                S(4f);
            }
            else
            {
                L(name, W(labelWidth));
            }
            float newValue =
                HS(
                    value, leftValue, rightValue, W(sliderWidth));
            if (roundNearest != 0)
            {
                newValue = Mathf.Round(newValue / roundNearest) * roundNearest;
            }
            S(8f);
            L(string.Format(valueFormat, newValue), W(40f));
            FS();
            return newValue;
        }
        public static string NTF(string name, string value, float fieldWidth, float labelWidth = 0)
        {
            BH();
            string newValue = NTFC(name, value, fieldWidth, labelWidth);
            EH();
            return newValue;
        }
        public static (string, string) NTFP(string name1, string name2, string value1, string value2, float fieldWidth, float labelWidth = 0)
        {
            BH();
            string newValue1 = NTFC(name1, value1, fieldWidth, labelWidth);
            string newValue2 = NTFC(name2, value2, fieldWidth, labelWidth);
            EH();
            return (newValue1, newValue2);
        }
        private static string NTFC(string name, string value, float fieldWidth, float labelWidth = 0)
        {
            if (labelWidth == 0)
            {
                L(name);
                S(4f);
            }
            else L(name, W(labelWidth));
            string newValue = TF(value, W(fieldWidth));
            FS();
            return newValue;
        }
        public static bool TL<T>(List<T> list, ref int selectedIndex, Func<T, string> nameFunc)
        {
            bool changed = false;
            int moveUp = -1, moveDown = -1;
            for (int i = 0; i < list.Count; i++)
            {
                T curr = list[i];
                string name = nameFunc.Invoke(curr);
                BH();
                BH();
                if (Btn("▲") && i > 0)
                {
                    moveUp = i;
                }
                if (Btn("▼") && i < list.Count - 1)
                {
                    moveDown = i;
                }
                EH();
                S(8f);
                if (SGL.T(selectedIndex == i, name) && selectedIndex != i)
                {
                    selectedIndex = i;
                    changed = true;
                }
                FS();
                EH();
            }
            if (moveUp != -1)
            {
                changed = true;
                T temp = list[moveUp];
                list[moveUp] = list[moveUp - 1];
                list[moveUp - 1] = temp;
                if (moveUp - 1 == selectedIndex)
                {
                    selectedIndex++;
                }
                else if (moveUp == selectedIndex)
                {
                    selectedIndex--;
                }
            }
            else if (moveDown != -1)
            {
                changed = true;
                T temp = list[moveDown];
                list[moveDown] = list[moveDown + 1];
                list[moveDown + 1] = temp;
                if (moveDown + 1 == selectedIndex)
                {
                    selectedIndex--;
                }
                else if (moveDown == selectedIndex)
                {
                    selectedIndex++;
                }
            }
            return changed;
        }
        public static void HL(float thickness, float length = 0f)
        {
            Box(GUIContent.none, new GUIStyle()
            {
                margin = new RectOffset(8, 8, 4, 4),
                padding = new RectOffset(),
                fixedHeight = thickness,
                fixedWidth = length,
                normal = {
                    background = Texture2D.whiteTexture,
                },
            });
        }
        public static void BA(Rect screenRect, string text, GUIStyle style) => GUILayout.BeginArea(screenRect, text, style);
        public static void BA(Rect screenRect, Texture image, GUIStyle style) => GUILayout.BeginArea(screenRect, image, style);
        public static void BA(Rect screenRect, GUIContent content, GUIStyle style) => GUILayout.BeginArea(screenRect, content, style);
        public static void BA(Rect screenRect, Texture image) => GUILayout.BeginArea(screenRect, image);
        public static void BA(Rect screenRect, string text) => GUILayout.BeginArea(screenRect, text);
        public static void BA(Rect screenRect) => GUILayout.BeginArea(screenRect);
        public static void BA(Rect screenRect, GUIStyle style) => GUILayout.BeginArea(screenRect, style);
        public static void BA(Rect screenRect, GUIContent content) => GUILayout.BeginArea(screenRect, content);
        public static void BH(params GUILayoutOption[] options) => GUILayout.BeginHorizontal(options);
        public static void BH(GUIStyle style, params GUILayoutOption[] options) => GUILayout.BeginHorizontal(style, options);
        public static void BH(string text, GUIStyle style, params GUILayoutOption[] options) => GUILayout.BeginHorizontal(text, style, options);
        public static void BH(Texture image, GUIStyle style, params GUILayoutOption[] options) => GUILayout.BeginHorizontal(image, style, options);
        public static void BH(GUIContent content, GUIStyle style, params GUILayoutOption[] options) => GUILayout.BeginHorizontal(content, style, options);
        public static Vector2 BSV(Vector2 scrollPosition, params GUILayoutOption[] options) => GUILayout.BeginScrollView(scrollPosition, options);
        public static Vector2 BSV(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical, params GUILayoutOption[] options) => GUILayout.BeginScrollView(scrollPosition, alwaysShowHorizontal, alwaysShowVertical, options);
        public static Vector2 BSV(Vector2 scrollPosition, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, params GUILayoutOption[] options) => GUILayout.BeginScrollView(scrollPosition, horizontalScrollbar, verticalScrollbar, options);
        public static Vector2 BSV(Vector2 scrollPosition, GUIStyle style) => GUILayout.BeginScrollView(scrollPosition, style);
        public static Vector2 BSV(Vector2 scrollPosition, GUIStyle style, params GUILayoutOption[] options) => GUILayout.BeginScrollView(scrollPosition, style, options);
        public static Vector2 BSV(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, params GUILayoutOption[] options) => GUILayout.BeginScrollView(scrollPosition, alwaysShowHorizontal, alwaysShowVertical, horizontalScrollbar, verticalScrollbar, options);
        public static Vector2 BSV(Vector2 scrollPosition, bool alwaysShowHorizontal, bool alwaysShowVertical, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar, GUIStyle background, params GUILayoutOption[] options) => GUILayout.BeginScrollView(scrollPosition, alwaysShowHorizontal, alwaysShowVertical, horizontalScrollbar, verticalScrollbar, background, options);
        public static void BV(Texture image, GUIStyle style, params GUILayoutOption[] options) => GUILayout.BeginVertical(image, style, options);
        public static void BV(string text, GUIStyle style, params GUILayoutOption[] options) => GUILayout.BeginVertical(text, style, options);
        public static void BV(GUIContent content, GUIStyle style, params GUILayoutOption[] options) => GUILayout.BeginVertical(content, style, options);
        public static void BV(params GUILayoutOption[] options) => GUILayout.BeginVertical(options);
        public static void BV(GUIStyle style, params GUILayoutOption[] options) => GUILayout.BeginVertical(style, options);
        public static void Box(string text, params GUILayoutOption[] options) => GUILayout.Box(text, options);
        public static void Box(string text, GUIStyle style, params GUILayoutOption[] options) => GUILayout.Box(text, style, options);
        public static void Box(GUIContent content, GUIStyle style, params GUILayoutOption[] options) => GUILayout.Box(content, style, options);
        public static void Box(Texture image, params GUILayoutOption[] options) => GUILayout.Box(image, options);
        public static void Box(Texture image, GUIStyle style, params GUILayoutOption[] options) => GUILayout.Box(image, style, options);
        public static void Box(GUIContent content, params GUILayoutOption[] options) => GUILayout.Box(content, options);
        public static bool Btn(Texture image, params GUILayoutOption[] options) => GUILayout.Button(image, options);
        public static bool Btn(string text, params GUILayoutOption[] options) => GUILayout.Button(text, options);
        public static bool Btn(GUIContent content, params GUILayoutOption[] options) => GUILayout.Button(content, options);
        public static bool Btn(Texture image, GUIStyle style, params GUILayoutOption[] options) => GUILayout.Button(image, style, options);
        public static bool Btn(GUIContent content, GUIStyle style, params GUILayoutOption[] options) => GUILayout.Button(content, style, options);
        public static bool Btn(string text, GUIStyle style, params GUILayoutOption[] options) => GUILayout.Button(text, style, options);
        public static void EA() => GUILayout.EndArea();
        public static void EH() => GUILayout.EndHorizontal();
        public static void ESV() => GUILayout.EndScrollView();
        public static void EV() => GUILayout.EndVertical();
        public static GUILayoutOption EH(bool expand) => GUILayout.ExpandHeight(expand);
        public static GUILayoutOption EW(bool expand) => GUILayout.ExpandWidth(expand);
        public static void FS() => GUILayout.FlexibleSpace();
        public static GUILayoutOption H(float height) => GUILayout.Height(height);
        public static float HSb(float value, float size, float leftValue, float rightValue, GUIStyle style, params GUILayoutOption[] options) => GUILayout.HorizontalScrollbar(value, size, leftValue, rightValue, style, options);
        public static float HSb(float value, float size, float leftValue, float rightValue, params GUILayoutOption[] options) => GUILayout.HorizontalScrollbar(value, size, leftValue, rightValue, options);
        public static float HS(float value, float leftValue, float rightValue, params GUILayoutOption[] options) => GUILayout.HorizontalSlider(value, leftValue, rightValue, options);
        public static float HS(float value, float leftValue, float rightValue, GUIStyle slider, GUIStyle thumb, params GUILayoutOption[] options) => GUILayout.HorizontalSlider(value, leftValue, rightValue, slider, thumb, options);
        public static void L(Texture image, params GUILayoutOption[] options) => GUILayout.Label(image, options);
        public static void L(string text, params GUILayoutOption[] options) => GUILayout.Label(text, options);
        public static void L(GUIContent content, params GUILayoutOption[] options) => GUILayout.Label(content, options);
        public static void L(Texture image, GUIStyle style, params GUILayoutOption[] options) => GUILayout.Label(image, style, options);
        public static void L(string text, GUIStyle style, params GUILayoutOption[] options) => GUILayout.Label(text, style, options);
        public static void L(GUIContent content, GUIStyle style, params GUILayoutOption[] options) => GUILayout.Label(content, style, options);
        public static GUILayoutOption MxH(float maxHeight) => GUILayout.MaxHeight(maxHeight);
        public static GUILayoutOption MxW(float maxWidth) => GUILayout.MaxWidth(maxWidth);
        public static GUILayoutOption MnH(float minHeight) => GUILayout.MinHeight(minHeight);
        public static GUILayoutOption MnW(float minWidth) => GUILayout.MinWidth(minWidth);
        public static string PF(string password, char maskChar, params GUILayoutOption[] options) => GUILayout.PasswordField(password, maskChar, options);
        public static string PF(string password, char maskChar, int maxLength, params GUILayoutOption[] options) => GUILayout.PasswordField(password, maskChar, maxLength, options);
        public static string PF(string password, char maskChar, GUIStyle style, params GUILayoutOption[] options) => GUILayout.PasswordField(password, maskChar, style, options);
        public static string PF(string password, char maskChar, int maxLength, GUIStyle style, params GUILayoutOption[] options) => GUILayout.PasswordField(password, maskChar, maxLength, style, options);
        public static bool RB(Texture image, GUIStyle style, params GUILayoutOption[] options) => GUILayout.RepeatButton(image, style, options);
        public static bool RB(string text, params GUILayoutOption[] options) => GUILayout.RepeatButton(text, options);
        public static bool RB(Texture image, params GUILayoutOption[] options) => GUILayout.RepeatButton(image, options);
        public static bool RB(GUIContent content, GUIStyle style, params GUILayoutOption[] options) => GUILayout.RepeatButton(content, style, options);
        public static bool RB(string text, GUIStyle style, params GUILayoutOption[] options) => GUILayout.RepeatButton(text, style, options);
        public static bool RB(GUIContent content, params GUILayoutOption[] options) => GUILayout.RepeatButton(content, options);
        public static int SG(int selected, GUIContent[] contents, int xCount, GUIStyle style, params GUILayoutOption[] options) => GUILayout.SelectionGrid(selected, contents, xCount, style, options);
        public static int SG(int selected, string[] texts, int xCount, params GUILayoutOption[] options) => GUILayout.SelectionGrid(selected, texts, xCount, options);
        public static int SG(int selected, Texture[] images, int xCount, GUIStyle style, params GUILayoutOption[] options) => GUILayout.SelectionGrid(selected, images, xCount, style, options);
        public static int SG(int selected, string[] texts, int xCount, GUIStyle style, params GUILayoutOption[] options) => GUILayout.SelectionGrid(selected, texts, xCount, style, options);
        public static int SG(int selected, GUIContent[] content, int xCount, params GUILayoutOption[] options) => GUILayout.SelectionGrid(selected, content, xCount, options);
        public static int SG(int selected, Texture[] images, int xCount, params GUILayoutOption[] options) => GUILayout.SelectionGrid(selected, images, xCount, options);
        public static void S(float pixels) => GUILayout.Space(pixels);
        public static string TA(string text, GUIStyle style, params GUILayoutOption[] options) => GUILayout.TextArea(text, style, options);
        public static string TA(string text, params GUILayoutOption[] options) => GUILayout.TextArea(text, options);
        public static string TA(string text, int maxLength, params GUILayoutOption[] options) => GUILayout.TextArea(text, maxLength, options);
        public static string TA(string text, int maxLength, GUIStyle style, params GUILayoutOption[] options) => GUILayout.TextArea(text, maxLength, style, options);
        public static string TF(string text, GUIStyle style, params GUILayoutOption[] options) => GUILayout.TextField(text, style, options);
        public static string TF(string text, int maxLength, params GUILayoutOption[] options) => GUILayout.TextField(text, maxLength, options);
        public static string TF(string text, params GUILayoutOption[] options) => GUILayout.TextField(text, options);
        public static string TF(string text, int maxLength, GUIStyle style, params GUILayoutOption[] options) => GUILayout.TextField(text, maxLength, style, options);
        public static bool T(bool value, Texture image, params GUILayoutOption[] options) => GUILayout.Toggle(value, image, options);
        public static bool T(bool value, string text, params GUILayoutOption[] options) => GUILayout.Toggle(value, text, options);
        public static bool T(bool value, GUIContent content, params GUILayoutOption[] options) => GUILayout.Toggle(value, content, options);
        public static bool T(bool value, Texture image, GUIStyle style, params GUILayoutOption[] options) => GUILayout.Toggle(value, image, style, options);
        public static bool T(bool value, GUIContent content, GUIStyle style, params GUILayoutOption[] options) => GUILayout.Toggle(value, content, style, options);
        public static bool T(bool value, string text, GUIStyle style, params GUILayoutOption[] options) => GUILayout.Toggle(value, text, style, options);
        public static int Tb(int selected, string[] texts, params GUILayoutOption[] options) => GUILayout.Toolbar(selected, texts, options);
        public static int Tb(int selected, GUIContent[] contents, params GUILayoutOption[] options) => GUILayout.Toolbar(selected, contents, options);
        public static int Tb(int selected, string[] texts, GUIStyle style, params GUILayoutOption[] options) => GUILayout.Toolbar(selected, texts, style, options);
        public static int Tb(int selected, Texture[] images, GUIStyle style, params GUILayoutOption[] options) => GUILayout.Toolbar(selected, images, style, options);
        public static int Tb(int selected, Texture[] images, GUIStyle style, GUI.ToolbarButtonSize buttonSize, params GUILayoutOption[] options) => GUILayout.Toolbar(selected, images, style, buttonSize, options);
        public static int Tb(int selected, GUIContent[] contents, GUIStyle style, params GUILayoutOption[] options) => GUILayout.Toolbar(selected, contents, style, options);
        public static int Tb(int selected, GUIContent[] contents, GUIStyle style, GUI.ToolbarButtonSize buttonSize, params GUILayoutOption[] options) => GUILayout.Toolbar(selected, contents, style, buttonSize, options);
        public static int Tb(int selected, GUIContent[] contents, bool[] enabled, GUIStyle style, params GUILayoutOption[] options) => GUILayout.Toolbar(selected, contents, enabled, style, options);
        public static int Tb(int selected, GUIContent[] contents, bool[] enabled, GUIStyle style, GUI.ToolbarButtonSize buttonSize, params GUILayoutOption[] options) => GUILayout.Toolbar(selected, contents, enabled, style, buttonSize, options);
        public static int Tb(int selected, Texture[] images, params GUILayoutOption[] options) => GUILayout.Toolbar(selected, images, options);
        public static int Tb(int selected, string[] texts, GUIStyle style, GUI.ToolbarButtonSize buttonSize, params GUILayoutOption[] options) => GUILayout.Toolbar(selected, texts, style, buttonSize, options);
        public static float VSb(float value, float size, float topValue, float bottomValue, GUIStyle style, params GUILayoutOption[] options) => GUILayout.VerticalScrollbar(value, size, topValue, bottomValue, style, options);
        public static float VSb(float value, float size, float topValue, float bottomValue, params GUILayoutOption[] options) => GUILayout.VerticalScrollbar(value, size, topValue, bottomValue, options);
        public static float VS(float value, float leftValue, float rightValue, params GUILayoutOption[] options) => GUILayout.VerticalSlider(value, leftValue, rightValue, options);
        public static float VS(float value, float leftValue, float rightValue, GUIStyle slider, GUIStyle thumb, params GUILayoutOption[] options) => GUILayout.VerticalSlider(value, leftValue, rightValue, slider, thumb, options);
        public static GUILayoutOption W(float width) => GUILayout.Width(width);
        public static Rect Wnd(int id, Rect screenRect, GUI.WindowFunction func, GUIContent content, GUIStyle style, params GUILayoutOption[] options) => GUILayout.Window(id, screenRect, func, content, style, options);
        public static Rect Wnd(int id, Rect screenRect, GUI.WindowFunction func, string text, GUIStyle style, params GUILayoutOption[] options) => GUILayout.Window(id, screenRect, func, text, style, options);
        public static Rect Wnd(int id, Rect screenRect, GUI.WindowFunction func, GUIContent content, params GUILayoutOption[] options) => GUILayout.Window(id, screenRect, func, content, options);
        public static Rect Wnd(int id, Rect screenRect, GUI.WindowFunction func, Texture image, params GUILayoutOption[] options) => GUILayout.Window(id, screenRect, func, image, options);
        public static Rect Wnd(int id, Rect screenRect, GUI.WindowFunction func, string text, params GUILayoutOption[] options) => GUILayout.Window(id, screenRect, func, text, options);
        public static Rect Wnd(int id, Rect screenRect, GUI.WindowFunction func, Texture image, GUIStyle style, params GUILayoutOption[] options) => GUILayout.Window(id, screenRect, func, image, style, options);
    }
    #endregion
    #region Internals
    internal class TweakRunner
    {
        public static GUIStyle Expan;
        public static GUIStyle Enabl;
        public static GUIStyle Enabl_Label;
        public static GUIStyle Descr;
        public static bool StyleInitialized = false;
        public Tweak Tweak { get; }
        public TweakRunner OuterTweak { get; }
        public List<TweakRunner> InnerTweaks { get; }
        public TweakAttribute Metadata { get; }
        public TweakSettings Settings { get; }
        public List<TweakPatch> Patches { get; }
        public Harmony Harmony { get; }
        public bool Inner { get; }
        public bool Last { get; internal set; }
        public TweakRunner(Tweak tweak, TweakSettings settings, bool last, TweakRunner outerTweak = null) : this(tweak, tweak.GetType().GetCustomAttribute<TweakAttribute>(), settings, last, outerTweak) { }
        public TweakRunner(Tweak tweak, TweakAttribute attr, TweakSettings settings, bool last, TweakRunner outerTweak = null)
        {
            Type tweakType = tweak.GetType();
            Tweak = tweak;
            Metadata = attr;
            Settings = settings;
            Patches = new List<TweakPatch>();
            if (!string.IsNullOrEmpty(tweak.Name))
                Metadata.Name = tweak.Name;
            if (!string.IsNullOrEmpty(tweak.Description))
                Metadata.Description = tweak.Description;
            Harmony = new Harmony($"Tweaks.{Metadata.Name}");
            InnerTweaks = new List<TweakRunner>();
            OuterTweak = outerTweak;
            Inner = outerTweak != null;
            Last = last;
            if (Metadata.PatchesType != null)
                AddPatches(Metadata.PatchesType);
            AddPatches(tweakType);
            Patches = Patches.OrderBy(t => t.Priority).ToList();
            if (Metadata.MustNotBeDisabled)
                Settings.IsEnabled = true;
            Tweak.Tweaks.Add(tweakType, tweak);
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
            if (Metadata.PatchesType != null)
                foreach (Type type in GetNestedTypes(Metadata.PatchesType))
                    Harmony.CreateClassProcessor(type).Patch();
            foreach (Type type in GetNestedTypes(Tweak.GetType()))
                Harmony.CreateClassProcessor(type).Patch();
            foreach (var patch in Patches)
            {
                if (patch.Prefix)
                    Harmony.Patch(patch.Target, new HarmonyMethod(patch.Patch));
                else Harmony.Patch(patch.Target, postfix: new HarmonyMethod(patch.Patch));
            }
            Tweak.OnPatch();
            InnerTweaks.ForEach(runner => runner.Enable());
        }
        public void Disable()
        {
            if (Metadata.MustNotBeDisabled) return;
            Tweak.OnDisable();
            Harmony.UnpatchAll(Harmony.Id);
            Tweak.OnUnpatch();
            InnerTweaks.ForEach(runner => runner.Disable());
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
                Enabl_Label = new GUIStyle(GUI.skin.label)
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
            bool newIsExpanded;
            bool newIsEnabled = false;
            if (Metadata.MustNotBeDisabled)
            {
                newIsExpanded = GUILayout.Toggle(Settings.IsExpanded, Settings.IsEnabled ? (Settings.IsExpanded ? "◢" : "▶") : "", Expan);
                GUILayout.Label(Metadata.Name, Enabl_Label);
                GUILayout.Label("-");
                GUILayout.Label(Metadata.Description, Descr);
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            else
            {
                newIsExpanded = GUILayout.Toggle(Settings.IsExpanded, Settings.IsEnabled ? (Settings.IsExpanded ? "◢" : "▶") : "", Expan);
                newIsEnabled = GUILayout.Toggle(Settings.IsEnabled, Metadata.Name, Enabl);
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
                InnerTweaks.ForEach(runner => runner.OnGUI());
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
            InnerTweaks.ForEach(runner => runner.OnUpdate());
        }
        public void OnHideGUI()
        {
            if (Settings.IsEnabled)
                Tweak.OnHideGUI();
            InnerTweaks.ForEach(runner => runner.OnHideGUI());
        }
        private void AddPatches(Type patchesType)
        {
            void AddPatches(Type t)
            {
                foreach (MethodInfo method in t.GetMethods((BindingFlags)15420))
                {
                    IEnumerable<TweakPatch> patches = method.GetCustomAttributes<TweakPatch>(true);
                    foreach (TweakPatch patch in patches)
                    {
                        if (patch.IsValid)
                        {
                            patch.Patch = method;
                            if (patch.Target == null)
                                patch.Target = TweakPatch.FindMethod(patch.Patch.Name.Replace(patch.Splitter, '.'), patch.MethodType, false);
                            if (patch.Target == null)
                            {
                                if (patch.ThrowOnNull)
                                    throw new NullReferenceException("Cannot Patch Due To Target Is Null!");
                                else continue;
                            }
                            Patches.Add(patch);
                        }
                    }
                }
            }
            AddPatches(patchesType);
            foreach (Type type in GetNestedTypes(patchesType))
                AddPatches(type);
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
    #endregion
    #region Attributes
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public class TweakPatch : Attribute
    {
        public static int Version = (int)AccessTools.Field(typeof(GCNS), "releaseNumber").GetValue(null);
        internal MethodInfo Patch { get; set; }
        public bool Prefix { get; set; } = false;
        public string PatchId { get; set; }
        public int Priority { get; set; }
        public int MinVersion { get; set; } = -1;
        public int MaxVersion { get; set; } = -1;
        public bool ThrowOnNull { get; set; } = false;
        public GSCS MethodType { get; }
        public char Splitter { get; } = '_';
        public MethodBase Target { get; internal set; }
        internal TweakPatch(MethodBase target)
            => Target = target;
        public TweakPatch(Type type, string name, params Type[] parameterTypes) : this(type, name, GSCS.None, parameterTypes) { }
        public TweakPatch(Type type, string name, GSCS methodType, params Type[] parameterTypes) : this(methodType)
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
        public TweakPatch(string fullName, GSCS methodType = GSCS.None) : this(methodType)
            => Target = FindMethod(fullName, MethodType, false);
        public TweakPatch(GSCS methodType = GSCS.None)
            => MethodType = methodType;
        public bool IsValid => (MinVersion == -1 || Version >= MinVersion) && (MaxVersion == -1 || Version <= MaxVersion);
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
    [AttributeUsage(AttributeTargets.Class)]
    public class TweakAttribute : Attribute
    {
        public TweakAttribute(string name = "", string desc = "")
        {
            Name = name;
            Description = desc;
        }
        public string Name { get; internal set; }
        public string Description { get; internal set; }
        public Type PatchesType { get; set; }
        public Type SettingsType { get; set; }
        public bool MustNotBeDisabled { get; set; }
        public int Priority { get; set; }
    }
    public class SyncSettings : Attribute
    {
        public static Dictionary<Type, TweakSettings> Settings = new Dictionary<Type, TweakSettings>();
        public static void Load(ModEntry modEntry)
        {
            foreach (var type in modEntry.Assembly.GetTypes())
            {
                if (!type.IsSubclassOf(typeof(TweakSettings))) continue;
                Register(modEntry, type);
            }
        }
        public static void Register(ModEntry modEntry, Type settingsType)
        {
            MethodInfo load = typeof(ModSettings).GetMethod(nameof(ModSettings.Load), (BindingFlags)15420, null, new Type[] { typeof(ModEntry) }, null);
            try { Settings[settingsType] = (TweakSettings)load.MakeGenericMethod(settingsType).Invoke(null, new object[] { modEntry }); }
            catch { Settings[settingsType] = (TweakSettings)Activator.CreateInstance(settingsType); }
        }
        public static void Sync(Type type, object instance = null)
        {
            foreach (var field in type.GetFields((BindingFlags)15420))
            {
                SyncSettings sync = field.GetCustomAttribute<SyncSettings>();
                if (sync != null)
                    field.SetValue(instance, Settings[field.FieldType]);
            }
            foreach (var prop in type.GetProperties((BindingFlags)15420))
            {
                SyncSettings sync = prop.GetCustomAttribute<SyncSettings>();
                if (sync != null)
                    prop.SetValue(instance, Settings[prop.PropertyType]);
            }
        }
        public static void Save(ModEntry modEntry)
        {
            foreach (var setting in Settings.Values)
                setting.Save(modEntry);
        }
    }
    public class SyncTweak : Attribute
    {
        public static void Sync(Type type, object instance = null)
        {
            foreach (var field in type.GetFields((BindingFlags)15420))
            {
                SyncTweak sync = field.GetCustomAttribute<SyncTweak>();
                if (sync != null)
                    field.SetValue(instance, Tweak.Tweaks[field.FieldType]);
            }
            foreach (var prop in type.GetProperties((BindingFlags)15420))
            {
                SyncTweak sync = prop.GetCustomAttribute<SyncTweak>();
                if (sync != null)
                    prop.SetValue(instance, Tweak.Tweaks[prop.PropertyType]);
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
    #endregion
}
