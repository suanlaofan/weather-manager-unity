using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace WeatherManager
{
    // The game ships English-only; the former Chinese language option was removed.
    public enum AppLanguage
    {
        English
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
            { "region_inline.town", "town" },
            { "region_inline.forest", "forest" },
            { "region_inline.farmland", "farmlands" },
            { "region_inline.highland", "highlands" },
            { "region_inline.lowland", "river valley" },
            { "region_inline.river_channel", "river channel" },
            { "region_inline.coastal_lake", "coastal lake" },
            { "region_inline.water", "open water" },
            { "hazard.forest", "forest wildfire" },
            { "hazard.farmland", "field fire" },
            { "hazard.highland", "highland scrub fire" },
            { "hazard.lowland", "lowland fire" },
            { "hazard.town", "town fire" },
            { "hazard.water", "surface fire" },
            { "event.lightning_ignited", "{0} lightning strike ignited a {1}" },
            { "event.lightning_wet_suppressed", "Lightning hit wet ground in the {0} - ignition suppressed" },
            { "event.lightning_highland_rockfall", "Highland lightning impact triggered rockfall risk" },
            { "event.lightning_damaged", "Lightning strike caused damage in the {0}" },
            { "event.rain_extinguished_fire", "Rain extinguished a fire - steam burst" },
            { "event.tornado_formed", "A tornado formed from sustained wind" },
            { "event.rain_extinguished_fire_tornado", "Sustained rain extinguished a fire tornado" },
            { "event.fire_tornado_formed", "The tornado became a fire tornado" },
            { "event.rain_extinguished_structure", "Rain extinguished a burning structure" },
            { "event.collapse_snow", "{0} snow load collapsed a structure" },
            { "event.collapse_flood", "{0} floodwater undermined a structure" },
            { "event.collapse_wind", "Extreme wind tore down a structure in the {0}" },
            { "event.collapse_fire", "{0} fire destroyed a structure" },
            { "event.collapse_lightning", "Lightning impact destroyed a structure in the {0}" },
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
            { "event.fire_generic", "Fire is spreading across the {0}" },
            { "event.highland_landslide", "Highland runoff triggered a landslide and rockfall" },
            { "validation.localization", "Localization verified: weather, regions, events, toast and reset" }
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
            if (current == language) return;
            current = language;
            LanguageChanged?.Invoke(current);
        }

        public static string Text(string key)
        {
            EnsureInitialized();
            return English.TryGetValue(key, out var value) ? value : key;
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

        // Lowercase region names for use mid-sentence, so capitalized region labels do not appear inside English sentences.
        public static LocalizedArgument BiomeInline(BiomeSample biome)
        {
            if (string.Equals(biome.Name, "River Channel", StringComparison.OrdinalIgnoreCase))
                return new LocalizedArgument("region_inline.river_channel");
            if (string.Equals(biome.Name, "Coastal Lake", StringComparison.OrdinalIgnoreCase))
                return new LocalizedArgument("region_inline.coastal_lake");
            var suffix = biome.Type == BiomeType.Lowland ? "lowland" : biome.Type.ToString().ToLowerInvariant();
            return new LocalizedArgument("region_inline." + suffix);
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
            current = AppLanguage.English;
        }
    }
}
