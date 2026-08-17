using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MortierFu.Shared;
using UnityEngine;
using UnityEngine.UI;

namespace MortierFu
{
    public class GamePauseSystem : IGameSystem
    {
        private readonly HashSet<object> _pauseBlockers = new();

        private SaveService _saveService;

        public bool IsInitialized { get; set; }
        public bool IsPaused { get; private set; }
        private bool IsPauseBlocked => _pauseBlockers.Count > 0;
        public bool AreSettingsReady => TryResolveSaveService() && _saveService.Settings != null;

        public PlayerManager PauseOwner { get; private set; }

        public event Action<PlayerManager> Paused;
        public event Action Resumed;
        public event Action Canceled;

        public UniTask OnInitialize()
        {
            IsPaused = false;
            TryResolveSaveService();

            return UniTask.CompletedTask;
        }

        public void SetPauseBlocked(object owner, bool blocked)
        {
            if (owner is null)
                return;

            if (blocked)
            {
                _pauseBlockers.Add(owner);
                return;
            }

            _pauseBlockers.Remove(owner);
        }

        public void TogglePause(PlayerManager player)
        {
            if (IsPaused)
            {
                Resume();
                return;
            }

            if (IsPauseBlocked)
            {
                Logs.Log("[GamePauseSystem] Pause ignored because pause is currently blocked.");
                return;
            }

            Pause(player);
        }

        public void Resume()
        {
            if (!IsPaused)
                return;

            IsPaused = false;
            PauseOwner = null;

            Time.timeScale = 1f;
            Resumed?.Invoke();
        }

        public void Cancel() => Canceled?.Invoke();

        public void RestoreSettingsFromSave()
        {
            if (!TryGetSettings(out SettingsData settings))
            {
                Logs.LogWarning("[GamePauseSystem] Cannot restore settings because SaveService is not ready.");
                return;
            }

            Screen.fullScreen = settings.IsFullscreen;
            QualitySettings.vSyncCount = settings.IsVSyncEnabled ? 1 : 0;

            AudioService.SetVolume(AudioService.BusEnum.MASTER, settings.MasterVolume);
            AudioService.SetVolume(AudioService.BusEnum.MUSIC, settings.MusicVolume);
            AudioService.SetVolume(AudioService.BusEnum.SFX, settings.SfxVolume);
            AudioService.SetVolume(AudioService.BusEnum.AMBIENCE, settings.AmbienceVolume);
        }

        public void UpdateUIFromSave(Toggle fullscreenToggle, Toggle vsyncToggle, Slider masterVolumeSlider, Slider musicVolumeSlider, Slider sfxVolumeSlider, Slider ambienceSlider)
        {
            if (!TryGetSettings(out SettingsData settings))
            {
                Logs.LogWarning("[GamePauseSystem] Cannot update pause settings UI because SaveService is not ready.");
                return;
            }

            if (fullscreenToggle)
                fullscreenToggle.SetIsOnWithoutNotify(settings.IsFullscreen);

            if (vsyncToggle)
                vsyncToggle.SetIsOnWithoutNotify(settings.IsVSyncEnabled);

            if (masterVolumeSlider)
                masterVolumeSlider.SetValueWithoutNotify(settings.MasterVolume);

            if (musicVolumeSlider)
                musicVolumeSlider.SetValueWithoutNotify(settings.MusicVolume);

            if (sfxVolumeSlider)
                sfxVolumeSlider.SetValueWithoutNotify(settings.SfxVolume);
            
            if (ambienceSlider)
                ambienceSlider.SetValueWithoutNotify(settings.AmbienceVolume);
        }

        public void BindUIEvents(Toggle fullscreenToggle, Toggle vsyncToggle, Slider masterVolumeSlider, Slider musicVolumeSlider, Slider sfxVolumeSlider, Slider ambienceSlider)
        {
            if (fullscreenToggle)
            {
                fullscreenToggle.onValueChanged.RemoveListener(OnFullscreenChanged);
                fullscreenToggle.onValueChanged.AddListener(OnFullscreenChanged);
            }

            if (vsyncToggle)
            {
                vsyncToggle.onValueChanged.RemoveListener(OnVSyncChanged);
                vsyncToggle.onValueChanged.AddListener(OnVSyncChanged);
            }

            if (masterVolumeSlider)
            {
                masterVolumeSlider.onValueChanged.RemoveListener(OnMasterVolumeChanged);
                masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
            }

            if (musicVolumeSlider)
            {
                musicVolumeSlider.onValueChanged.RemoveListener(OnMusicVolumeChanged);
                musicVolumeSlider.onValueChanged.AddListener(OnMusicVolumeChanged);
            }

            if (sfxVolumeSlider)
            {
                sfxVolumeSlider.onValueChanged.RemoveListener(OnSfxVolumeChanged);
                sfxVolumeSlider.onValueChanged.AddListener(OnSfxVolumeChanged);
            }
            
            if (ambienceSlider)
            {
                ambienceSlider.onValueChanged.RemoveListener(OnAmbienceVolumeChanged);
                ambienceSlider.onValueChanged.AddListener(OnAmbienceVolumeChanged);
            }
        }

        public void SaveSettings()
        {
            if (!TryResolveSaveService())
            {
                Logs.LogWarning("[GamePauseSystem] Cannot save settings because SaveService is not ready.");
                return;
            }

            _saveService.SaveSettings().Forget();
        }

        private void Pause(PlayerManager player)
        {
            if (IsPaused)
                return;

            IsPaused = true;
            PauseOwner = player;

            Time.timeScale = 0f;
            Paused?.Invoke(player);
        }

        private void OnFullscreenChanged(bool value)
        {
            if (!TryGetSettings(out SettingsData settings))
                return;

            Screen.fullScreen = value;
            settings.IsFullscreen = value;
        }

        private void OnVSyncChanged(bool value)
        {
            if (!TryGetSettings(out SettingsData settings))
                return;

            QualitySettings.vSyncCount = value ? 1 : 0;
            settings.IsVSyncEnabled = value;
        }

        private void OnMasterVolumeChanged(float value)
        {
            if (!TryGetSettings(out SettingsData settings))
                return;

            settings.MasterVolume = value;
        }

        private void OnMusicVolumeChanged(float value)
        {
            if (!TryGetSettings(out SettingsData settings))
                return;

            settings.MusicVolume = value;
        }

        private void OnSfxVolumeChanged(float value)
        {
            if (!TryGetSettings(out SettingsData settings))
                return;

            settings.SfxVolume = value;
        }
        
        private void OnAmbienceVolumeChanged(float value)
        {
            if (!TryGetSettings(out SettingsData settings))
                return;

            settings.AmbienceVolume = value;
        }

        private bool TryGetSettings(out SettingsData settings)
        {
            settings = null;

            if (!TryResolveSaveService())
                return false;

            settings = _saveService.Settings;
            return settings != null;
        }

        private bool TryResolveSaveService()
        {
            if (_saveService == null)
                _saveService = ServiceManager.Instance?.Get<SaveService>();

            return _saveService != null && _saveService.IsInitialized && _saveService.Settings != null;
        }

        public void Dispose()
        {
            IsPaused = false;
            Time.timeScale = 1f;

            Paused = null;
            Resumed = null;
            Canceled = null;

            PauseOwner = null;
            _pauseBlockers.Clear();
        }
    }
}