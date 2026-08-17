using Cysharp.Threading.Tasks;
using MortierFu.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace MortierFu
{
    public class SettingsPanel : UIPanel
    {
        [Header("Toggles")]
        [SerializeField] private Toggle _fullscreenToggle;
        [SerializeField] private Toggle _vsyncToggle;

        [Header("Sliders")]
        [SerializeField] private Slider _masterVolumeSlider;
        [SerializeField] private Slider _musicVolumeSlider;
        [SerializeField] private Slider _sfxVolumeSlider;
        [SerializeField] private Slider _ambienceVolumeSlider;

        private SaveService _saveService;
        private bool _eventsBound;

        private void Awake()
        {
            _saveService = ServiceManager.Instance?.Get<SaveService>();

            if (_saveService == null)
            {
                Logs.LogError("[SettingsPanel] SaveService is missing.", this);
                return;
            }

            RestoreSettingsFromSave();
            UpdateUIFromSave();
            BindUIEvents();
        }

        private void Start() => base.Hide();

        private void OnDestroy() => UnbindUIEvents();

        public override void Show()
        {
            UpdateUIFromSave();
            base.Show();
        }

        public override void Hide()
        {
            base.Hide();
            SaveSettings();
        }

        private void RestoreSettingsFromSave()
        {
            if (_saveService == null)
                return;

            SettingsData settings = _saveService.Settings;

            Screen.fullScreen = settings.IsFullscreen;
            QualitySettings.vSyncCount = settings.IsVSyncEnabled ? 1 : 0;

            AudioService.SetVolume(AudioService.BusEnum.MASTER, settings.MasterVolume);
            AudioService.SetVolume(AudioService.BusEnum.MUSIC, settings.MusicVolume);
            AudioService.SetVolume(AudioService.BusEnum.SFX, settings.SfxVolume);
        }

        private void UpdateUIFromSave()
        {
            if (_saveService == null)
                return;

            SettingsData settings = _saveService.Settings;

            if (_fullscreenToggle)
                _fullscreenToggle.SetIsOnWithoutNotify(settings.IsFullscreen);

            if (_vsyncToggle)
                _vsyncToggle.SetIsOnWithoutNotify(settings.IsVSyncEnabled);

            if (_masterVolumeSlider)
                _masterVolumeSlider.SetValueWithoutNotify(settings.MasterVolume);

            if (_musicVolumeSlider)
                _musicVolumeSlider.SetValueWithoutNotify(settings.MusicVolume);

            if (_sfxVolumeSlider)
                _sfxVolumeSlider.SetValueWithoutNotify(settings.SfxVolume);
            
            if (_ambienceVolumeSlider)
                _ambienceVolumeSlider.SetValueWithoutNotify(settings.AmbienceVolume);
        }

        private void BindUIEvents()
        {
            if (_eventsBound)
                return;

            if (_fullscreenToggle)
                _fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);

            if (_vsyncToggle)
                _vsyncToggle.onValueChanged.AddListener(OnVSyncChanged);

            if (_masterVolumeSlider)
                _masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);

            if (_musicVolumeSlider)
                _musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);

            if (_sfxVolumeSlider)
                _sfxVolumeSlider.onValueChanged.AddListener(OnSfxVolumeChanged);
            
            if (_ambienceVolumeSlider)
                _ambienceVolumeSlider.onValueChanged.AddListener(OnAmbienceVolumeChanged);

            _eventsBound = true;
        }

        private void UnbindUIEvents()
        {
            if (!_eventsBound)
                return;

            if (_fullscreenToggle)
                _fullscreenToggle.onValueChanged.RemoveListener(OnFullscreenChanged);

            if (_vsyncToggle)
                _vsyncToggle.onValueChanged.RemoveListener(OnVSyncChanged);

            if (_masterVolumeSlider)
                _masterVolumeSlider.onValueChanged.RemoveListener(OnMasterVolumeChanged);

            if (_musicVolumeSlider)
                _musicVolumeSlider.onValueChanged.RemoveListener(OnMusicVolumeChanged);

            if (_sfxVolumeSlider)
                _sfxVolumeSlider.onValueChanged.RemoveListener(OnSfxVolumeChanged);
            
            if (_ambienceVolumeSlider)
                _ambienceVolumeSlider.onValueChanged.RemoveListener(OnAmbienceVolumeChanged);

            _eventsBound = false;
        }

        private void OnFullscreenChanged(bool value)
        {
            if (_saveService == null)
                return;

            Screen.fullScreen = value;
            _saveService.Settings.IsFullscreen = value;

            AudioService.PlayOneShot(AudioService.FMODEvents.SFX_UI_Tick);
        }

        private void OnVSyncChanged(bool value)
        {
            if (_saveService == null)
                return;

            QualitySettings.vSyncCount = value ? 1 : 0;
            _saveService.Settings.IsVSyncEnabled = value;

            AudioService.PlayOneShot(AudioService.FMODEvents.SFX_UI_Tick);
        }

        private void OnMasterVolumeChanged(float value)
        {
            if (_saveService == null)
                return;

            _saveService.Settings.MasterVolume = value;
            AudioService.SetVolume(AudioService.BusEnum.MASTER, value);
            AudioService.PlayOneShot(AudioService.FMODEvents.SFX_UI_Slider);
        }

        private void OnMusicVolumeChanged(float value)
        {
            if (_saveService == null)
                return;

            _saveService.Settings.MusicVolume = value;
            AudioService.SetVolume(AudioService.BusEnum.MUSIC, value);
            AudioService.PlayOneShot(AudioService.FMODEvents.SFX_UI_Slider);
        }

        private void OnSfxVolumeChanged(float value)
        {
            if (_saveService == null)
                return;

            _saveService.Settings.SfxVolume = value;
            AudioService.SetVolume(AudioService.BusEnum.SFX, value);
            AudioService.PlayOneShot(AudioService.FMODEvents.SFX_UI_Slider);
        }
        
        private void OnAmbienceVolumeChanged(float value)
        {
            if (_saveService == null)
                return;

            _saveService.Settings.AmbienceVolume = value;
            AudioService.SetVolume(AudioService.BusEnum.SFX, value);
            AudioService.PlayOneShot(AudioService.FMODEvents.SFX_UI_Slider);
        }

        private void SaveSettings() => _saveService?.SaveSettings().Forget();
    }
}