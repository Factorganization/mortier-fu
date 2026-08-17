using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MortierFu.Shared;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MortierFu
{
    public class RaceUI : MonoBehaviour
    {
        [SerializeField] private PlayerConfirmationUI _playerConfirmationUI;
        [SerializeField] private RacePressureUI _racePressureUI;
        [SerializeField] private AugmentSummaryUI _augmentSummaryUI;
        
        [Header("RaceExplanation")]
        [SerializeField] private GameObject _explanation;
        [SerializeField] private Image _explanationImage;
        [SerializeField] private  TextMeshProUGUI _explanationText;
        [SerializeField] private TextMeshProUGUI _explanationTitle;

        private ConfirmationService _confirmationService;
        private AugmentSelectionSystem _augmentSelectionSystem;
        private LobbyService _lobbyService;
        private LevelSystem _levelSystem;

        private GameModeBase _gm;
        private bool _isSubscribedToAugmentSelectionSystem;

        private void Awake()
        {
            //SIMON
            _levelSystem = SystemManager.Instance.Get<LevelSystem>();
            //
            _confirmationService = ServiceManager.Instance.Get<ConfirmationService>();
            _lobbyService = ServiceManager.Instance.Get<LobbyService>();
            HideRuntimeUI();
        }

        private void OnEnable()
        {
            SubscribeGameMode();
            SubscribeConfirmationService();
            TrySubscribeAugmentSelectionSystem(logIfMissing: false);
        }

        private void Start()
        {
            TrySubscribeAugmentSelectionSystem(logIfMissing: true);
        }

        private void OnDisable()
        {
            UnsubscribeGameMode();
            UnsubscribeConfirmationService();
            UnsubscribeAugmentSelectionSystem();

            HideRuntimeUI();
        }

        private void OnDestroy()
        {
            UnsubscribeGameMode();
            UnsubscribeConfirmationService();
            UnsubscribeAugmentSelectionSystem();
        }

        private void SubscribeGameMode()
        {
            UnsubscribeGameMode();

            _gm = GameService.CurrentGameMode as GameModeBase;

            if (_gm is null)
                return;

            _gm.OnRaceEndedUI += PlayAugmentSummary;
            _gm.OnRacePlayerConfirmation += ShowRaceExplanation;
        }

        private void UnsubscribeGameMode()
        {
            if (_gm is null)
                return;

            _gm.OnRaceEndedUI -= PlayAugmentSummary;
            //SIMON
            _gm.OnRacePlayerConfirmation -= ShowRaceExplanation;
            //
            _gm = null;
        }
        
        //SIMON active l'explication et affiche la bonne explication selon le type de race
        private void ShowRaceExplanation()
        {
            if (_explanation == null)
                return;
            _explanation.SetActive(true);
            
            var raceModeDefinition = _levelSystem.CurrentRaceReporter.RaceModeDefinition;

            if (_explanationText != null) 
                _explanationText.text = raceModeDefinition.RaceExplanationText;

            if (_explanationTitle != null) 
                _explanationTitle.text = raceModeDefinition.RaceExplanationTitle;

            if (_explanationImage != null) 
                _explanationImage.sprite = raceModeDefinition.RaceExplanationImage;
        }
        //

        private void SubscribeConfirmationService()
        {
            if (_confirmationService is null)
            {
                Logs.LogError($"[RaceUI] No ConfirmationService found for {gameObject.name}.", this);
                return;
            }

            _confirmationService.OnStartConfirmation += HandleStartConfirmation;
            _confirmationService.OnPlayerConfirmed += HandlePlayerConfirmed;
            _confirmationService.OnPlayerConfirmedAgain += HandlePlayerConfirmedAgain;
            _confirmationService.OnPlayerReleasedAgain += HandlePlayerRealeseAgain;
            _confirmationService.OnAllPlayersConfirmed += HandleAllPlayersConfirmed;
        }

        private void UnsubscribeConfirmationService()
        {
            if (_confirmationService is null)
                return;

            _confirmationService.OnStartConfirmation -= HandleStartConfirmation;
            _confirmationService.OnPlayerConfirmed -= HandlePlayerConfirmed;
            _confirmationService.OnPlayerConfirmedAgain -= HandlePlayerConfirmedAgain;
            _confirmationService.OnPlayerReleasedAgain -= HandlePlayerRealeseAgain;
            _confirmationService.OnAllPlayersConfirmed -= HandleAllPlayersConfirmed;
        }

        private void TrySubscribeAugmentSelectionSystem(bool logIfMissing)
        {
            if (_isSubscribedToAugmentSelectionSystem)
                return;

            _augmentSelectionSystem ??= SystemManager.Instance.Get<AugmentSelectionSystem>();

            if (_augmentSelectionSystem is null)
            {
                if (logIfMissing)
                    Logs.LogError($"[RaceUI] No AugmentSelectionSystem found for {gameObject.name}.", this);

                return;
            }

            _augmentSelectionSystem.OnPressureStart += HandlePressureStart;
            _augmentSelectionSystem.OnPressureStop += HandlePressureStop;

            _isSubscribedToAugmentSelectionSystem = true;
        }

        private void UnsubscribeAugmentSelectionSystem()
        {
            if (!_isSubscribedToAugmentSelectionSystem)
                return;

            if (_augmentSelectionSystem is not null)
            {
                _augmentSelectionSystem.OnPressureStart -= HandlePressureStart;
                _augmentSelectionSystem.OnPressureStop -= HandlePressureStop;
            }

            _isSubscribedToAugmentSelectionSystem = false;
        }

        private void HideRuntimeUI()
        {
            if (_playerConfirmationUI)
                _playerConfirmationUI.gameObject.SetActive(false);

            if (_racePressureUI)
                _racePressureUI.gameObject.SetActive(false);

            if (_augmentSummaryUI)
                _augmentSummaryUI.gameObject.SetActive(false);
        }

        private void HandleStartConfirmation(int activePlayerCount)
        {
            if (!_playerConfirmationUI)
                return;

            _playerConfirmationUI.gameObject.SetActive(true);
            _playerConfirmationUI.ShowConfirmation(activePlayerCount);
        }

        private void HandlePlayerConfirmed(int playerIndex)
        {
            if (!_playerConfirmationUI)
                return;

            _playerConfirmationUI.NotifyPlayerConfirmed(playerIndex);
        }

        private void HandlePlayerConfirmedAgain(int playerIndex)
        {
            if (!_playerConfirmationUI)
                return;
            
            _playerConfirmationUI.NotifyConfirmedPlayerPressedAgain(playerIndex);
        }

        private void HandlePlayerRealeseAgain(int playerIndex)
        {
            if (!_playerConfirmationUI)
                return;
            
            Logs.LogWarning("here");
            _playerConfirmationUI.NotifyPlayerReleaseSpam(playerIndex);
        }

        private void HandleAllPlayersConfirmed()
        {
            if (!_playerConfirmationUI)
                return;

            _playerConfirmationUI.OnConfirmation();
        }

        private void HandlePressureStart(float duration)
        {
            if (!_racePressureUI)
                return;

            _racePressureUI.gameObject.SetActive(true);
            _racePressureUI.StartVignettePressure(duration);
        }

        private void HandlePressureStop()
        {
            if (!_racePressureUI)
                return;

            _racePressureUI.StopVignettePressure();
            _racePressureUI.gameObject.SetActive(false);
            
            if (!_explanation)
                return;
            _explanation.SetActive(false);
           
        }

        private async UniTask PlayAugmentSummary(
            UniTask canHideTask,
            Action onRevealComplete,   
            Action requestSkip,
            CancellationToken cancellationToken
        )
        {
            if (!_augmentSummaryUI)
                return;

            if (_lobbyService is null)
            {
                Logs.LogError("[RaceUI] Cannot play augment summary because LobbyService is missing.", this);
                return;
            }

            TrySubscribeAugmentSelectionSystem(logIfMissing: true);

            if (_augmentSelectionSystem is null)
                return;

            cancellationToken.ThrowIfCancellationRequested();

            _augmentSummaryUI.gameObject.SetActive(true);

            try
            {
                var players = _lobbyService.GetPlayers();

                List<List<SO_Augment>> playerAugments = new();

                foreach (var player in players)
                {
                    player.SetControlContext(PlayerControlContext.AugmentRaceSummary);

                    if (player &&
                        player.Character &&
                        _augmentSelectionSystem.PickedAugments.TryGetValue(player.Character, out var augments))
                    {
                        playerAugments.Add(augments);
                    }
                    else
                    {
                        playerAugments.Add(new List<SO_Augment>());
                    }
                }

                await _augmentSummaryUI.AnimatePlayerImagesWithAugments(
                    playerAugments,
                    canHideTask,
                    onRevealComplete,  
                    requestSkip,
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                if (_augmentSummaryUI)
                    _augmentSummaryUI.gameObject.SetActive(false);
            }
        }
    }
}