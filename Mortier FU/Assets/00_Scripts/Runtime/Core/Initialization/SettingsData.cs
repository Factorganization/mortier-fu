namespace MortierFu
{
    [System.Serializable]
    public class SettingsData
    {
        public bool IsFullscreen = true;
        public bool IsVSyncEnabled = true;
        public float MasterVolume = 1f;
        public float MusicVolume = 1f;
        public float SfxVolume = 1f;
        public float AmbienceVolume = 1f;

        public static SettingsData CreateDefault() => new SettingsData();
    }
}