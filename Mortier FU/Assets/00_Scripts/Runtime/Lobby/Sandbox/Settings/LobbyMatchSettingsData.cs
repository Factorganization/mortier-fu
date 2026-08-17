using System;
using MortierFu.Shared;
using UnityEngine;

namespace MortierFu
{
    public sealed class LobbyMatchSettingsData : MonoBehaviour
    {
        [Header("Rulesets")]
        [SerializeField] private SO_MatchRulesetLibrary _rulesetLibrary;
        [SerializeField] private SO_MatchRuleset _selectedRuleset;

        [Header("Runtime")]
        [SerializeField] private MatchConfig _config = MatchConfig.Default;

        private bool _hasAppliedConfig;
        private bool _hasCustomChanges;
        private int _lastAppliedPlayerCount = -1;

        public event Action OnChanged;

        public SO_MatchRulesetLibrary RulesetLibrary => _rulesetLibrary;
        public SO_MatchRuleset SelectedRuleset => _selectedRuleset;
        public MatchConfig Config => _config;

        public bool CanEditCurrentConfig => _selectedRuleset && _selectedRuleset.AllowEditing;

        public int ScoreToWin => _config.ScoreToWin;
        public float RaceTimeLimit => _config.RaceTimeLimit;
        public bool EqualizeDropRateForAllRarities => _config.EqualizeDropRateForAllRarities;
        public float HealthMultiplier => _config.HealthMultiplier;
        public bool DisableStrikes => _config.DisableStrikes;
        public float StrikeMultiplier => _config.StrikeMultiplier;
        public bool DisableGhosts => _config.DisableGhosts;
        public bool DisableSelfDamage => _config.DisableSelfDamage;

        private void Awake()
        {
            _config.Clamp();
        }

        public void ApplyRecommendedForPlayerCount(int playerCount)
        {
            playerCount = Mathf.Max(1, playerCount);

            if (_hasCustomChanges)
                return;

            if (_hasAppliedConfig && _lastAppliedPlayerCount == playerCount && _selectedRuleset)
                return;

            SO_MatchRuleset ruleset = _selectedRuleset ? _selectedRuleset : _rulesetLibrary?.GetFirstNonCustomRuleset();

            if (!ruleset)
            {
                _config = MatchConfig.Default;
                _config.Clamp();
                _hasAppliedConfig = true;
                _lastAppliedPlayerCount = playerCount;
                OnChanged?.Invoke();
                return;
            }

            SetRuleset(ruleset, playerCount, preserveCurrentCustomConfig: false);
        }

        public void SelectRulesetByOffset(int offset, int playerCount)
        {
            if (_rulesetLibrary == null || _rulesetLibrary.Count == 0)
                return;

            int currentIndex = _rulesetLibrary.GetIndexOf(_selectedRuleset);

            if (currentIndex < 0)
                currentIndex = 0;

            int nextIndex = WrapIndex(currentIndex + offset, _rulesetLibrary.Count);
            SO_MatchRuleset ruleset = _rulesetLibrary.Get(nextIndex);

            SetRuleset(ruleset, playerCount, preserveCurrentCustomConfig: true);
            
            AudioService.PlayOneShot(AudioService.FMODEvents.SFX_UI_Navigate, transform.position);
        }

        public void SetRuleset(SO_MatchRuleset ruleset, int playerCount, bool preserveCurrentCustomConfig)
        {
            if (!ruleset)
                return;

            _selectedRuleset = ruleset;

            bool keepCurrentConfig = ruleset.IsCustom && preserveCurrentCustomConfig && _hasAppliedConfig;

            if (!keepCurrentConfig)
                _config = ruleset.GetConfigForPlayerCount(playerCount);

            _config.Clamp();

            _hasAppliedConfig = true;
            _hasCustomChanges = ruleset.IsCustom;
            _lastAppliedPlayerCount = Mathf.Max(1, playerCount);

            Logs.Log($"[LobbyMatchSettingsData] Ruleset set to {_selectedRuleset.DisplayName}.");

            OnChanged?.Invoke();
        }

        public bool IsSettingEditable(MatchSettingId settingId)
        {
            if (!CanEditCurrentConfig)
                return false;

            if (settingId == MatchSettingId.StrikeMultiplier && _config.DisableStrikes)
                return false;

            return true;
        }

        public int GetInt(MatchSettingId settingId)
        {
            return settingId switch
            {
                MatchSettingId.ScoreToWin => _config.ScoreToWin,
                _ => 0
            };
        }

        public float GetFloat(MatchSettingId settingId)
        {
            return settingId switch
            {
                MatchSettingId.RaceTimeLimit => _config.RaceTimeLimit,
                MatchSettingId.HealthMultiplier => _config.HealthMultiplier,
                MatchSettingId.StrikeMultiplier => _config.StrikeMultiplier,
                _ => 0f
            };
        }

        public bool GetBool(MatchSettingId settingId)
        {
            return settingId switch
            {
                MatchSettingId.EqualizeDropRateForAllRarities => _config.EqualizeDropRateForAllRarities,
                MatchSettingId.DisableStrikes => _config.DisableStrikes,
                MatchSettingId.DisableGhosts => _config.DisableGhosts,
                MatchSettingId.DisableSelfDamage => _config.DisableSelfDamage,
                _ => false
            };
        }

        public void SetInt(MatchSettingId settingId, int value)
        {
            if (!IsSettingEditable(settingId))
                return;

            switch (settingId)
            {
                case MatchSettingId.ScoreToWin:
                    _config.ScoreToWin = value;
                    break;

                default:
                    return;
            }

            MarkCustomAndNotify();
            AudioService.PlayOneShot(AudioService.FMODEvents.SFX_UI_Slider, transform.position);
        }

        public void SetFloat(MatchSettingId settingId, float value)
        {
            if (!IsSettingEditable(settingId))
                return;

            switch (settingId)
            {
                case MatchSettingId.RaceTimeLimit:
                    _config.RaceTimeLimit = value;
                    break;

                case MatchSettingId.HealthMultiplier:
                    _config.HealthMultiplier = value;
                    break;

                case MatchSettingId.StrikeMultiplier:
                    _config.StrikeMultiplier = value;
                    break;

                default:
                    return;
            }

            MarkCustomAndNotify();
            AudioService.PlayOneShot(AudioService.FMODEvents.SFX_UI_Slider, transform.position);
        }

        public void SetBool(MatchSettingId settingId, bool value)
        {
            if (!IsSettingEditable(settingId))
                return;

            switch (settingId)
            {
                case MatchSettingId.EqualizeDropRateForAllRarities:
                    _config.EqualizeDropRateForAllRarities = value;
                    break;

                case MatchSettingId.DisableStrikes:
                    _config.DisableStrikes = value;
                    break;

                case MatchSettingId.DisableGhosts:
                    _config.DisableGhosts = value;
                    break;

                case MatchSettingId.DisableSelfDamage:
                    _config.DisableSelfDamage = value;
                    break;
                default:
                    return;
            }

            MarkCustomAndNotify();
            AudioService.PlayOneShot(AudioService.FMODEvents.SFX_UI_Tick, transform.position);
        }

        public MatchConfig ToMatchConfig()
        {
            MatchConfig config = _config;
            config.Clamp();
            return config;
        }

        private void MarkCustomAndNotify()
        {
            _config.Clamp();

            SO_MatchRuleset customRuleset = _rulesetLibrary?.GetCustomRuleset();

            if (customRuleset)
                _selectedRuleset = customRuleset;

            _hasCustomChanges = true;

            Logs.Log("[LobbyMatchSettingsData] Custom match config updated.");

            OnChanged?.Invoke();
        }

        private static int WrapIndex(int value, int count)
        {
            if (count <= 0)
                return 0;

            value %= count;

            if (value < 0)
                value += count;

            return value;
        }
    }
}