namespace FMODWrapper
{
    // ─────────────────────────────────────────────────────────────────────────
    //  EVENTS
    //  Централизованные пути событий FMOD Studio.
    //  Редактируйте вручную или генерируйте из FMOD Studio (File → Export GUIDs).
    // ─────────────────────────────────────────────────────────────────────────

    public static class Events
    {
        public static class SFX
        {
            public const string Explosion = "event:/SFX/Explosion";
            public const string Footstep = "event:/SFX/Footstep";
            public const string GunShot = "event:/SFX/GunShot";
            public const string UIClick = "event:/SFX/UI/Click";
            public const string UIHover = "event:/SFX/UI/Hover";
        }

        public static class Music
        {
            public const string MainMenu = "event:/Music/MainMenu";
            public const string Combat = "event:/Music/Combat";
            public const string Ambient = "event:/Music/Ambient";
        }

        public static class Voice
        {
            public const string PlayerHurt = "event:/Voice/Player/Hurt";
            public const string PlayerDie = "event:/Voice/Player/Die";
        }

        public static class Snapshots
        {
            public const string Paused = "snapshot:/Paused";
            public const string UnderWater = "snapshot:/UnderWater";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  PARAMS
    //  Имена параметров FMOD (глобальных и локальных).
    // ─────────────────────────────────────────────────────────────────────────

    public static class Params
    {
        // Глобальные
        public const string MusicIntensity = "MusicIntensity";
        public const string AmbienceArea = "AmbienceArea";
        public const string DayCycle = "DayCycle";

        // Локальные (привязаны к конкретным событиям)
        public const string FootstepSurface = "Surface";
        public const string ExplosionSize = "Size";
        public const string WeaponType = "WeaponType";
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  BANKS
    //  Имена банков (без расширения .bank).
    //
    //  Core   — постоянно в памяти, добавьте в Manager.coreBanks в Inspector.
    //  Zone   — загружаются асинхронно через Manager.LoadBankAsync().
    // ─────────────────────────────────────────────────────────────────────────

    public static class Banks
    {
        // Core (никогда не выгружаются)
        public const string Master = "Master";
        public const string MasterStrings = "Master.strings";
        public const string Core = "Core";

        // Зональные
        public const string City_Slums   = "City_Slums";
        public const string Forest_North = "Forest_North";
        public const string Desert_South = "Desert_South";

        // Уровневые
        public const string Level_01 = "Level_01";
        public const string Level_02 = "Level_02";
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  BUSES & VCAS
    // ─────────────────────────────────────────────────────────────────────────

    public static class Buses
    {
        public const string Master = "bus:/";
        public const string Music  = "bus:/Music";
        public const string SFX    = "bus:/SFX";
        public const string Voice  = "bus:/Voice";
    }

    public static class VCAs
    {
        public const string Master = "vca:/Master";
        public const string Music  = "vca:/Music";
        public const string SFX    = "vca:/SFX";
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  CONFIG
    //  Централизованные настройки аудио-подсистемы.
    // ─────────────────────────────────────────────────────────────────────────

    public static class Config
    {
        /// <summary>
        /// Настройки Distance Culling (Architecture Doc §2.3).
        /// Используются компонентом <see cref="SampleDataCuller"/>.
        /// </summary>
        public static class Distance
        {
            /// <summary>
            /// Дистанция, при превышении которой Sample Data выгружается из RAM.
            /// Источники за этим порогом переходят в режим Virtual Voice (нулевой CPU).
            /// </summary>
            public const float UnloadThreshold = 50f;

            /// <summary>
            /// Дистанция повторной загрузки Sample Data.
            /// Должна быть меньше <see cref="UnloadThreshold"/> — создаёт гистерезис
            /// и предотвращает частые load/unload при движении по границе зоны.
            /// </summary>
            public const float LoadThreshold = 40f;

            /// <summary>
            /// Интервал проверки дистанции (секунды).
            /// Проверка каждый кадр не нужна — 0.5 с достаточно.
            /// </summary>
            public const float CheckInterval = 0.5f;
        }

        /// <summary>
        /// Приоритеты голосов (Architecture Doc §2.4).
        /// При превышении Real Channel Count FMOD убивает голоса с низким приоритетом.
        /// </summary>
        public static class Priority
        {
            /// <summary>Выстрел игрока, критичные UI-звуки. Никогда не убивается.</summary>
            public const int Highest = 0;

            /// <summary>Выстрелы врагов, взрывы вблизи.</summary>
            public const int High = 1;

            /// <summary>Шаги NPC, фоновые реакции (умолчание FMOD).</summary>
            public const int Normal = 2;

            /// <summary>Шаги дальних врагов, фоновые звуки. Убивается первым.</summary>
            public const int Low = 3;
        }
    }
}
