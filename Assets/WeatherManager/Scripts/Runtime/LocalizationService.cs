using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace WeatherManager
{
    public enum AppLanguage
    {
        English,
        Chinese
    }

    public readonly struct LocalizedArgument
    {
        public LocalizedArgument(string key)
        {
            Key = key;
        }

        public string Key { get; }
    }

    public readonly struct LocalizedMessage
    {
        public LocalizedMessage(string key, object[] arguments)
        {
            Key = key;
            Arguments = arguments ?? Array.Empty<object>();
        }

        public string Key { get; }
        public object[] Arguments { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(Key);
        public string Resolve() => LocalizationService.Format(Key, Arguments);
    }

    public static class LocalizationService
    {
        private const string PreferenceKey = "WeatherManager.Language.v1";
        private static readonly Dictionary<string, string> English = new Dictionary<string, string>
        {
            { "app.title", "WEATHER\nMANAGER" },
            { "label.season", "SEASON" },
            { "label.light", "LIGHT" },
            { "label.city_status", "CITY STATUS" },
            { "label.current", "CURRENT" },
            { "label.event_log", "EVENT LOG" },
            { "label.no_incidents", "No active incidents" },
            { "weather.wind", "Wind" },
            { "weather.rain", "Rain" },
            { "weather.electric", "Lightning" },
            { "weather.snow", "Snow" },
            { "weather.heat", "Heat" },
            { "season.spring", "Spring" },
            { "season.summer", "Summer" },
            { "season.autumn", "Autumn" },
            { "season.winter", "Winter" },
            { "season.spring.short", "Sp" },
            { "season.summer.short", "Su" },
            { "season.autumn.short", "Au" },
            { "season.winter.short", "Wi" },
            { "phase.day", "Day" },
            { "phase.night", "Night" },
            { "state.ready", "READY" },
            { "state.casting", "CASTING" },
            { "state.paused", "PAUSED" },
            { "button.sound", "Sound" },
            { "button.muted", "Muted" },
            { "button.reset", "Reset" },
            { "stats.population", "Population  {0} / {1}\nInjured  {2}    Lost  {3}" },
            { "stats.fires", "Fires  {0}" },
            { "stats.flood", "Flood  {0:0.0}%" },
            { "stats.destroyed", "Destroyed  {0}/{1}" },
            { "stats.tornadoes", "Tornadoes  {0}" },
            { "stats.grid_offline", "GRID OFFLINE" },
            { "stats.grid_stable", "Grid stable" },
            { "severity.danger", "DANGER" },
            { "severity.warning", "WARN" },
            { "severity.info", "INFO" },
            { "toast.world_restored", "World restored" },
            { "toast.language_changed", "Language switched to English" },
            { "region.town", "Town" },
            { "region.forest", "Forest" },
            { "region.farmland", "Farmland" },
            { "region.highland", "Highland" },
            { "region.lowland", "River Valley" },
            { "region.river_channel", "River Channel" },
            { "region.coastal_lake", "Coastal Lake" },
            { "region.water", "Water" },
            { "hazard.forest", "forest wildfire" },
            { "hazard.farmland", "field fire" },
            { "hazard.highland", "highland scrub fire" },
            { "hazard.lowland", "lowland fire" },
            { "hazard.town", "town fire" },
            { "hazard.water", "surface fire" },
            { "event.lightning_ignited", "{0} lightning strike ignited a {1}" },
            { "event.lightning_wet_suppressed", "Lightning struck wet {0} ground - ignition suppressed" },
            { "event.lightning_highland_rockfall", "Highland lightning impact triggered rockfall risk" },
            { "event.lightning_damaged", "Lightning strike damaged the {0}" },
            { "event.rain_extinguished_fire", "Rain extinguished a fire - steam burst" },
            { "event.tornado_formed", "A tornado formed from sustained wind" },
            { "event.rain_extinguished_fire_tornado", "Sustained rain extinguished a fire tornado" },
            { "event.fire_tornado_formed", "The tornado became a fire tornado" },
            { "event.rain_extinguished_structure", "Rain extinguished a burning structure" },
            { "event.collapse_snow", "{0} snow load collapsed a structure" },
            { "event.collapse_flood", "{0} floodwater undermined a structure" },
            { "event.collapse_wind", "Extreme wind tore down a {0} structure" },
            { "event.collapse_fire", "{0} fire destroyed a structure" },
            { "event.collapse_lightning", "Lightning impact destroyed a {0} structure" },
            { "event.collapse_generic", "A structure collapsed" },
            { "event.flood_lowland", "Lowland floodwater is advancing through the river valley" },
            { "event.flood_farmland", "Farmland is waterlogged - mud is slowing movement" },
            { "event.flood_town", "Town drains are overloaded - streets are flooding" },
            { "event.flood_generic", "Floodwater is advancing through the {0}" },
            { "event.snow_load", "Heavy snow load is stressing roofs and highland trees" },
            { "event.town_blackout", "Town power grid offline - district lights failed" },
            { "event.fire_forest", "Forest wildfire is spreading between trees" },
            { "event.fire_farmland", "Farmland drought ignited a fast-moving grass fire" },
            { "event.fire_town", "Town fire is spreading across dry blocks" },
            { "event.fire_highland", "Highland scrub fire is climbing the slope" },
            { "event.fire_lowland", "A lowland fire is struggling against wet ground" },
            { "event.fire_generic", "Fire is spreading through the {0}" },
            { "event.highland_landslide", "Highland runoff triggered a landslide and rockfall" },
            { "validation.localization", "Localization verified: weather, regions, events, toast and reset" }
        };

        private static readonly Dictionary<string, string> Chinese = new Dictionary<string, string>
        {
            { "app.title", "天气\n管理员" },
            { "label.season", "季节" },
            { "label.light", "昼夜" },
            { "label.city_status", "城市状态" },
            { "label.current", "当前" },
            { "label.event_log", "事件记录" },
            { "label.no_incidents", "暂无灾害事件" },
            { "weather.wind", "风" },
            { "weather.rain", "雨" },
            { "weather.electric", "雷" },
            { "weather.snow", "雪" },
            { "weather.heat", "热" },
            { "season.spring", "春季" },
            { "season.summer", "夏季" },
            { "season.autumn", "秋季" },
            { "season.winter", "冬季" },
            { "season.spring.short", "春" },
            { "season.summer.short", "夏" },
            { "season.autumn.short", "秋" },
            { "season.winter.short", "冬" },
            { "phase.day", "白天" },
            { "phase.night", "夜晚" },
            { "state.ready", "就绪" },
            { "state.casting", "施法中" },
            { "state.paused", "已暂停" },
            { "button.sound", "声音" },
            { "button.muted", "静音" },
            { "button.reset", "重置" },
            { "stats.population", "人口  {0} / {1}\n受伤  {2}    伤亡  {3}" },
            { "stats.fires", "火灾  {0}" },
            { "stats.flood", "积水  {0:0.0}%" },
            { "stats.destroyed", "损毁  {0}/{1}" },
            { "stats.tornadoes", "龙卷风  {0}" },
            { "stats.grid_offline", "电网中断" },
            { "stats.grid_stable", "电网稳定" },
            { "severity.danger", "危险" },
            { "severity.warning", "警告" },
            { "severity.info", "信息" },
            { "toast.world_restored", "世界已完整恢复" },
            { "toast.language_changed", "语言已切换为中文" },
            { "region.town", "城镇" },
            { "region.forest", "森林" },
            { "region.farmland", "农田" },
            { "region.highland", "高地" },
            { "region.lowland", "河谷低地" },
            { "region.river_channel", "河道" },
            { "region.coastal_lake", "沿海湖泊" },
            { "region.water", "水域" },
            { "hazard.forest", "森林野火" },
            { "hazard.farmland", "农田火灾" },
            { "hazard.highland", "高地灌木火灾" },
            { "hazard.lowland", "低地火灾" },
            { "hazard.town", "城镇火灾" },
            { "hazard.water", "地表火灾" },
            { "event.lightning_ignited", "{0}遭雷击并引燃{1}" },
            { "event.lightning_wet_suppressed", "雷击落在湿润的{0}，未发生点燃" },
            { "event.lightning_highland_rockfall", "高地雷击引发落石风险" },
            { "event.lightning_damaged", "雷击损伤了{0}" },
            { "event.rain_extinguished_fire", "降雨扑灭火点并产生蒸汽" },
            { "event.tornado_formed", "持续强风形成了龙卷风" },
            { "event.rain_extinguished_fire_tornado", "持续降雨扑灭了火龙卷" },
            { "event.fire_tornado_formed", "龙卷风升级为火龙卷" },
            { "event.rain_extinguished_structure", "降雨扑灭了建筑火灾" },
            { "event.collapse_snow", "{0}积雪超载导致建筑坍塌" },
            { "event.collapse_flood", "{0}洪水冲蚀导致建筑坍塌" },
            { "event.collapse_wind", "极端强风摧毁了{0}建筑" },
            { "event.collapse_fire", "{0}火灾摧毁了建筑" },
            { "event.collapse_lightning", "雷击摧毁了{0}建筑" },
            { "event.collapse_generic", "一座建筑发生坍塌" },
            { "event.flood_lowland", "低地洪水正沿河谷推进" },
            { "event.flood_farmland", "农田积水形成泥泞，移动速度下降" },
            { "event.flood_town", "城镇排水超载，道路开始积水" },
            { "event.flood_generic", "洪水正在{0}推进" },
            { "event.snow_load", "厚重积雪正威胁屋顶和高地树木" },
            { "event.town_blackout", "城镇停电，街区照明中断" },
            { "event.fire_forest", "森林野火正在树木间蔓延" },
            { "event.fire_farmland", "农田干旱引发快速蔓延的草火" },
            { "event.fire_town", "城镇火灾正在干燥街区蔓延" },
            { "event.fire_highland", "高地灌木火灾正沿坡面上行" },
            { "event.fire_lowland", "低地湿润地表正在抑制火势" },
            { "event.fire_generic", "火势正在{0}蔓延" },
            { "event.highland_landslide", "高地径流引发滑坡和落石" },
            { "validation.localization", "本地化验证通过：天气、区域、事件、提示与重置" }
        };

        private static bool initialized;
        private static AppLanguage current;

        public static event Action<AppLanguage> LanguageChanged;

        public static AppLanguage Current
        {
            get
            {
                EnsureInitialized();
                return current;
            }
        }

        public static void SetLanguage(AppLanguage language, bool persist = true)
        {
            EnsureInitialized();
            if (persist)
            {
                PlayerPrefs.SetString(PreferenceKey, language == AppLanguage.Chinese ? "zh" : "en");
                PlayerPrefs.Save();
            }
            if (current == language) return;
            current = language;
            LanguageChanged?.Invoke(current);
        }

        public static string Text(string key)
        {
            EnsureInitialized();
            var table = current == AppLanguage.Chinese ? Chinese : English;
            if (table.TryGetValue(key, out var value)) return value;
            return English.TryGetValue(key, out value) ? value : key;
        }

        public static string Format(string key, params object[] arguments)
        {
            var template = Text(key);
            if (arguments == null || arguments.Length == 0) return template;
            var resolved = new object[arguments.Length];
            for (var index = 0; index < arguments.Length; index++)
            {
                resolved[index] = arguments[index] is LocalizedArgument token ? Text(token.Key) : arguments[index];
            }
            return string.Format(CultureInfo.InvariantCulture, template, resolved);
        }

        public static LocalizedMessage Message(string key, params object[] arguments)
        {
            return new LocalizedMessage(key, arguments);
        }

        public static LocalizedArgument Weather(WeatherType weather)
        {
            return new LocalizedArgument("weather." + weather.ToString().ToLowerInvariant());
        }

        public static LocalizedArgument Season(SeasonType season, bool shortLabel = false)
        {
            return new LocalizedArgument("season." + season.ToString().ToLowerInvariant() + (shortLabel ? ".short" : string.Empty));
        }

        public static LocalizedArgument Phase(DayPhase phase)
        {
            return new LocalizedArgument("phase." + phase.ToString().ToLowerInvariant());
        }

        public static LocalizedArgument Biome(BiomeSample biome)
        {
            if (string.Equals(biome.Name, "River Channel", StringComparison.OrdinalIgnoreCase))
                return new LocalizedArgument("region.river_channel");
            if (string.Equals(biome.Name, "Coastal Lake", StringComparison.OrdinalIgnoreCase))
                return new LocalizedArgument("region.coastal_lake");
            return Biome(biome.Type);
        }

        public static LocalizedArgument Biome(BiomeType biome)
        {
            var suffix = biome == BiomeType.Lowland ? "lowland" : biome.ToString().ToLowerInvariant();
            return new LocalizedArgument("region." + suffix);
        }

        public static LocalizedArgument Hazard(BiomeType biome)
        {
            var suffix = biome == BiomeType.Lowland ? "lowland" : biome.ToString().ToLowerInvariant();
            return new LocalizedArgument("hazard." + suffix);
        }

        private static void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            if (PlayerPrefs.HasKey(PreferenceKey))
            {
                current = string.Equals(PlayerPrefs.GetString(PreferenceKey), "zh", StringComparison.OrdinalIgnoreCase)
                    ? AppLanguage.Chinese
                    : AppLanguage.English;
                return;
            }

            current = Application.systemLanguage == SystemLanguage.ChineseSimplified
                || Application.systemLanguage == SystemLanguage.ChineseTraditional
                ? AppLanguage.Chinese
                : AppLanguage.English;
        }
    }
}
