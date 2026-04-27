namespace FMODWrapper
{
    public static class Events
    {
        public static class Sfx
        {
            // public const string Explosion = "event:/SFX/Explosion";
            // public const string Footstep  = "event:/SFX/Footstep";
            // public const string GunShot   = "event:/SFX/GunShot";
            // public const string UIClick   = "event:/SFX/UI/Click";
            // public const string UIHover   = "event:/SFX/UI/Hover";
        }

        public static class Music
        {
            // public const string MainMenu = "event:/Music/MainMenu";
            // public const string Combat   = "event:/Music/Combat";
            // public const string Ambient  = "event:/Music/Ambient";
        }

        public static class Voice
        {
            // public const string PlayerHurt = "event:/Voice/Player/Hurt";
            // public const string PlayerDie  = "event:/Voice/Player/Die";
        }

        public static class Snapshots
        {
            // public const string Paused     = "snapshot:/Paused";
            // public const string UnderWater = "snapshot:/UnderWater";
        }
    }

    public static class Params
    {
        // public const string MusicIntensity   = "MusicIntensity";
        // public const string AmbienceArea     = "AmbienceArea";
        // public const string DayCycle         = "DayCycle";
        
        // public const string FootstepSurface  = "Surface";
        // public const string ExplosionSize    = "Size";
        // public const string WeaponType       = "WeaponType";
    }

    public static class Banks
    {
        public const string Master        = "Master";
        public const string MasterStrings = "Master.strings";
        // public const string Core          = "Core";
        
        // public const string CitySlums    = "City_Slums";
        // public const string ForestNorth  = "Forest_North";
        // public const string DesertSouth  = "Desert_South";
        
        // public const string Level01      = "Level_01";
        // public const string Level02      = "Level_02";
    }

    public static class Buses
    {
        public const string Master = "bus:/";
        // public const string Music  = "bus:/Music";
        // public const string Sfx    = "bus:/SFX";
        // public const string Voice  = "bus:/Voice";
    }

    public static class Vcas
    {
        // public const string Master = "vca:/Master";
        // public const string Music  = "vca:/Music";
        // public const string Sfx    = "vca:/SFX";
    }

    public static class Config
    {
        public static class Distance
        {
            public const float UnloadThreshold = 50f;
            public const float LoadThreshold   = 30f;
            public const float CheckInterval   = 0.5f;
        }

        public static class Priority
        {
            public const int Highest = 0;
            public const int High    = 1;
            public const int Normal  = 2;
            public const int Low     = 3;
        }
    }
}
