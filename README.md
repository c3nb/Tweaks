# Tweaks

## 예시
Main.cs
```cs
public class Main
{
    public static void Load(UnityModManager.ModEntry modEntry)
    {
        //Setup All Tweaks in Current Mod Assembly.
        Tweak.Setup(modEntry);
    }
}
```

TestTweaks.cs
```cs
[Tweak("TestTweak", "Only Test", PatchesType = typeof(TTPatches), SettingsType = typeof(TTSettings))]
public class TestTweak : Tweak
{
    [SyncSettings] //Sync TweakSettings
    public TTSettings Settings { get; set; }
    public override void OnEnable()
    {
        Logger.Log("Nice");
    }
    public override void OnDisable()
    {
        Logger.Log("Nice");
    }
    public override void OnGUI()
    {
        GUILayout.Label("Nice");
    }
    public override void OnHideGUI()
    {
        Logger.Log("Nice");
    }
    public override void OnUpdate()
    {
        //TODO
    }
}
public class TTSettings : Tweak.Settings
{
    //Any settings..
}
public class TTPatches
{
    //Any patches..
}
```
## 결과
![Result](Result.png)
## BASED ON
https://github.com/PizzaLovers007/AdofaiTweaks/blob/master/AdofaiTweaks/Core/Tweak.cs
https://github.com/PizzaLovers007/AdofaiTweaks/blob/master/AdofaiTweaks/Core/TweakRunner.cs
https://github.com/PizzaLovers007/AdofaiTweaks/blob/master/AdofaiTweaks/Core/TweakSettings.cs
https://github.com/PizzaLovers007/AdofaiTweaks/blob/master/AdofaiTweaks/Core/SettingsSynchronizer.cs
https://github.com/PizzaLovers007/AdofaiTweaks/blob/master/AdofaiTweaks/Core/Attributes/RegisterTweakAttribute.cs
https://github.com/PizzaLovers007/AdofaiTweaks/blob/master/AdofaiTweaks/Core/Attributes/SyncTweakSettingsAttribute.cs
