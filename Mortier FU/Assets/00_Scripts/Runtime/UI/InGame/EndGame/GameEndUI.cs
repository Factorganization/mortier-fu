using System;
using System.Collections.Generic;
using MortierFu.Shared;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace MortierFu
{
    [Serializable]
    public struct GameWinnerColor
    {
        public Color MainBackgroundColor;
        public Color SecondBackgroundColor;
        public Color GoldBannerColor;
        public Color ThirdBackgroundColor;
        public Color FourthBackgroundColor;
        public Color GoldenDotColor;
        public Color GoldenDotBackgroundColor;
        public Color ExplosionColor;
        public Color CheckerColor;
        public Color BlurColor;
        public Color ExplosionOutlineColor;
        public Color GoldenCircleShadowColor;
        public Color GoldenCircleColor;
        public Sprite GameWinnerNameSprite;
    }

    public class GameEndUI : MonoBehaviour
    {
        [Header("Game Winner Colors")] [SerializeField]
        private GameWinnerColor[] _gameWinnerColors;

        [Header("Game Winner UI Elements")] [SerializeField]
        private Image _mainBackgroundImage;
        [SerializeField] private Image _secondBackgroundImage;
        [SerializeField] private Image _goldBannerImage;
        [SerializeField] private Image _thirdBackgroundImage;
        [SerializeField] private Image _fourthBackgroundImage;
        [SerializeField] private Image _goldenDotImage;
        [SerializeField] private Image _goldenDotBackgroundImage;
        [SerializeField] private Image _explosionImage;
        [SerializeField] private Image _checkerImage;
        [SerializeField] private Image _blurImage;
        [SerializeField] private Image _explosionOutlineImage;
        [SerializeField] private Image _goldenCircleShadowImage;
        [SerializeField] private Image _goldenCircleColor;
        [SerializeField] private Image _gameWinnerNameImage;

        [Header("Winner UI Input")] [SerializeField]
        private Selectable _firstSelected;

        [Header("Buttons")] [SerializeField] private Button _returnToLobbyButton;
        [SerializeField] private Button _newGameButton;
        [SerializeField] private Button _mainMenuButton;

        private readonly UnityPlayerUISession _uiSession = new();

        private GameModeBase _gm;
        private GameService _gameService;
        private LobbyService _lobbyService;
        private MultiplayerEventSystem _eventSystem;
        private InputSystemUIInputModule _inputModule;

        private PlayerManager _winnerPlayer;

        private void Awake()
        {
            _gameService = ServiceManager.Instance?.Get<GameService>();
            _lobbyService = ServiceManager.Instance?.Get<LobbyService>();

            if (_gameService == null)
                Logs.LogWarning("[GameEndUI] GameService introuvable au démarrage.");

            if (_lobbyService == null)
                Logs.LogWarning("[GameEndUI] LobbyService introuvable au démarrage.");
        }

        private void OnEnable()
        {
            SubscribeGameMode();
            BindButtonEvents();
        }

        private void OnDisable()
        {
            UnsubscribeGameMode();
            EndWinnerUISession();
            UnbindButtonEvents();
        }

        private void OnDestroy()
        {
            UnsubscribeGameMode();
            EndWinnerUISession();
            UnbindButtonEvents();
        }

        private bool EnsureEventSystemReferences()
        {
            if (_eventSystem != null && _inputModule != null && _eventSystem.isActiveAndEnabled)
                return true;

            _eventSystem = EventSystem.current as MultiplayerEventSystem;
            _inputModule = _eventSystem != null ? _eventSystem.currentInputModule as InputSystemUIInputModule : null;

            return _eventSystem != null && _inputModule != null;
        }

        private void SubscribeGameMode()
        {
            UnsubscribeGameMode();

            if (_gameService == null)
                _gameService = ServiceManager.Instance?.Get<GameService>();

            _gm = GameService.CurrentGameMode as GameModeBase;

            if (_gm == null)
            {
                Logs.LogWarning(
                    "[GameEndUI] Impossible d'abonner GameModeBase (CurrentGameMode null ou type incorrect).");
                return;
            }

            _gm.OnGameEnded += SetWinner;
        }

        private void UnsubscribeGameMode()
        {
            if (_gm == null)
                return;

            _gm.OnGameEnded -= SetWinner;
            _gm = null;
        }

        private void BindButtonEvents()
        {
            UnbindButtonEvents();

            if (_returnToLobbyButton != null)
                _returnToLobbyButton.onClick.AddListener(OnClickReturnToLobby);

            if (_newGameButton != null)
                _newGameButton.onClick.AddListener(OnClickNewGame);

            if (_mainMenuButton != null)
                _mainMenuButton.onClick.AddListener(OnClickMainMenu);
        }

        private void UnbindButtonEvents()
        {
            if (_returnToLobbyButton != null)
                _returnToLobbyButton.onClick.RemoveListener(OnClickReturnToLobby);

            if (_newGameButton != null)
                _newGameButton.onClick.RemoveListener(OnClickNewGame);

            if (_mainMenuButton != null)
                _mainMenuButton.onClick.RemoveListener(OnClickMainMenu);
        }

        private void OnClickReturnToLobby()
        {
            EndWinnerUISession();
            _gameService?.ReturnToLobby();
        }

        private void OnClickNewGame()
        {
            EndWinnerUISession();
            _gameService?.RestartGame();
        }

        private void OnClickMainMenu()
        {
            EndWinnerUISession();
            _gameService?.ReturnToMainMenu();
        }

        private void SetWinner(int playerIndex)
        {
            PlayerManager winner = _gm != null ? _gm.GetWinnerPlayer() : null;

            if (!winner)
            {
                Logs.LogError("[GameEndUI] Impossible de récupérer le PlayerManager du gagnant.");
                return;
            }

            int winnerIndex = winner.Team != null ? winner.Team.Index : playerIndex;

            _winnerPlayer = winner;

            BeginWinnerUISession(winner);

            SteamManager.AddProgressToStat("GAME_PLAYED");

            _winnerPlayer.Character.WinRoundDance();
            
            ApplyWinnerTheme(winnerIndex);
        }

        private void BeginWinnerUISession(PlayerManager winner)
        {
            if (winner == null)
                return;

            if (!EnsureEventSystemReferences())
            {
                Logs.LogError(
                    "[GameEndUI] Impossible de démarrer la session UI : MultiplayerEventSystem ou InputSystemUIInputModule manquant.");
                return;
            }

            DisableAllPlayersUIInput();

            _uiSession.Begin(winner, _eventSystem, _inputModule, _firstSelected, PlayerControlContext.EndGame);

            if (_eventSystem != null)
            {
                _eventSystem.SetSelectedGameObject(null);

                if (_firstSelected != null)
                    _eventSystem.SetSelectedGameObject(_firstSelected.gameObject);
                else
                    Logs.LogWarning("[GameEndUI] _firstSelected n'est pas assigné dans l'Inspecteur.");
            }

            Logs.Log($"[GameEndUI] Menu gagnant contrôlé par le Joueur {winner.PlayerIndex + 1}.");
        }

        private void EndWinnerUISession()
        {
            _uiSession?.End();

            if (_eventSystem != null)
                _eventSystem.SetSelectedGameObject(null);

            RestoreAllPlayersUIInput();

            _winnerPlayer = null;
        }

        private void DisableAllPlayersUIInput()
        {
            if (_lobbyService == null)
                _lobbyService = ServiceManager.Instance?.Get<LobbyService>();

            IReadOnlyList<PlayerManager> players = _lobbyService?.GetPlayers();

            if (players == null)
                return;

            for (int i = 0; i < players.Count; i++)
            {
                PlayerManager player = players[i];

                if (player == null)
                    continue;

                player.SetUnityEventSystemUIActive(false);

                if (player != _winnerPlayer)
                    player.SetControlContext(PlayerControlContext.Loading);
            }
        }

        private void ApplyWinnerTheme(int winnerIndex)
        {
            if (_gameWinnerColors == null || winnerIndex < 0 || winnerIndex >= _gameWinnerColors.Length)
                return;
            
            GameWinnerColor gameWinnerColor = _gameWinnerColors[winnerIndex];

            _mainBackgroundImage.color = gameWinnerColor.MainBackgroundColor;
            _secondBackgroundImage.color = gameWinnerColor.SecondBackgroundColor;
            _goldBannerImage.color = gameWinnerColor.GoldBannerColor;
            _thirdBackgroundImage.color = gameWinnerColor.ThirdBackgroundColor;
            _fourthBackgroundImage.color = gameWinnerColor.FourthBackgroundColor;
            _goldenDotImage.color = gameWinnerColor.GoldenDotColor;
            _goldenDotBackgroundImage.color = gameWinnerColor.GoldenDotBackgroundColor;
            _explosionImage.color = gameWinnerColor.ExplosionColor;
            _checkerImage.color = gameWinnerColor.CheckerColor;
            _blurImage.color = gameWinnerColor.BlurColor;
            _explosionOutlineImage.color = gameWinnerColor.ExplosionOutlineColor;
            _goldenCircleShadowImage.color = gameWinnerColor.GoldenCircleShadowColor;
            _goldenCircleColor.color = gameWinnerColor.GoldenCircleColor;
            _gameWinnerNameImage.sprite = gameWinnerColor.GameWinnerNameSprite;
        }

        private void RestoreAllPlayersUIInput()
        {
            if (_lobbyService == null)
                _lobbyService = ServiceManager.Instance?.Get<LobbyService>();

            IReadOnlyList<PlayerManager> players = _lobbyService?.GetPlayers();

            if (players == null)
                return;

            for (int i = 0; i < players.Count; i++)
            {
                PlayerManager player = players[i];

                if (player == null)
                    continue;

                if (player == _winnerPlayer)
                    continue;

                player.SetUnityEventSystemUIActive(false); 
                player.SetControlContext(PlayerControlContext.Lobby);
            }
        }
    }
}