using System;
using Cysharp.Threading.Tasks;
using MortierFu.Shared;
using PrimeTween;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MortierFu
{
    public sealed class MenuManager : MonoBehaviour
    {
        [Header("Main Menu References")]
        [field: SerializeField] public MainMenuPanel MainMenuPanel { get; private set; }
        [field: SerializeField] public Button PlayButton { get; private set; }
        [field: SerializeField] public Button CreditsButton { get; private set; }
        [field: SerializeField] public Button SettingsButton { get; private set; }
        [field: SerializeField] public Button QuitButton { get; private set; }

        [Header("Contact Buttons")]
        [field: SerializeField] public Button DiscordButton { get; private set; }
        [field: SerializeField] public Button SteamButton { get; private set; }
        [field: SerializeField] public Button MailButton { get; private set; }

        [Header("Animated Elements")]
        [SerializeField] private GameObject[] _animatedCharacterElements;
        [SerializeField] private GameObject _betaTest;

        [Header("Animation")]
        [SerializeField] private Image _background;
        [SerializeField] private Image _logo;

        [SerializeField] private Ease _backgroundEase = Ease.OutQuad;
        [SerializeField] private Ease _logoEase = Ease.OutQuad;
        [SerializeField] private Ease _playButtonEase = Ease.OutQuad;
        [SerializeField] private Ease _creditsButtonEase = Ease.OutQuad;
        [SerializeField] private Ease _settingsButtonEase = Ease.OutQuad;
        [SerializeField] private Ease _quitButtonEase = Ease.OutQuad;
        [SerializeField] private Ease _contactEase = Ease.OutQuad;

        [SerializeField] private float _backgroundEaseDuration = 1.5f;
        [SerializeField] private float _logoEaseDuration = 1.5f;
        [SerializeField] private float _playButtonEaseDuration = 0.5f;
        [SerializeField] private float _creditsButtonEaseDuration = 0.5f;
        [SerializeField] private float _settingsButtonEaseDuration = 0.5f;
        [SerializeField] private float _quitButtonEaseDuration = 0.5f;
        [SerializeField] private float _circleTransitionDuration = 1f;
        [SerializeField] private float _contactEaseDuration = 0.7f;
        [SerializeField] private float _contactScale = 1.25f;
        
        [SerializeField] private float _buttonAnimationDuration = 0.3f;

        [Header("Settings References")]
        [field: SerializeField] public SettingsPanel SettingsPanel { get; private set; }

        [Header("Credits References")]
        [field: SerializeField] public CreditsPanel CreditsPanel { get; private set; }
        [field: SerializeField] public Button YoutubeSoulSoundsButton { get; private set; }
        [field: SerializeField] public Button SpotifySoulSoundsButton { get; private set; }
        [field: SerializeField] public Button AppleMusicSoulSoundsButton { get; private set; }

        [Header("External Links")]
        [SerializeField] private string _discordURL = "https://discord.gg/CcWBymc99u";
        [SerializeField] private string _steamStorePageURL = "Salam";
        [SerializeField] private string _mailURL = "https://mail.google.com/mail/u/0/?view=cm&fs=1&to=gokaboom.gunpowder@gmail.com";
        [SerializeField] private string _appleMusicSoulSoundsURL = "https://music.apple.com/us/artist/soul-sounds/1871810462";
        [SerializeField] private string _spotifySoulSoundsURL = "https://open.spotify.com/intl-fr/artist/3vxxsIwNoLdayq8epiWFCf";
        [SerializeField] private string _youtubeSoulSoundsURL = "https://www.youtube.com/channel/UCfU9o1w-oERA9k1asa4d8zw";

        [Header("Utils")]
        [SerializeField] private MainMenuCameraManager _cameraManager;
        [SerializeField] private float _delayBeforeMainMenuShow = 1.5f;

        private EventSystem _eventSystem;
        private GameService _gameService;
        private InputSystemUIInputModule _uiInputModule;
        private InputAction _cancelAction;

        private bool _isLoadingLobby;
        private bool _isTransitioning;

        private void Awake()
        {
            CheckActivePanels();

            AudioService audioService = ServiceManager.Instance?.Get<AudioService>();
            audioService?.StartMusic(AudioService.FMODEvents.MUS_MainMenu).Forget();
            
            _isTransitioning = false;
        }

        private void Start()
        {
            _eventSystem = EventSystem.current;
            _gameService = ServiceManager.Instance?.Get<GameService>();

            if (!_eventSystem)
            {
                Logs.LogError("[MenuManager] No EventSystem found in the scene.", this);
                return;
            }

            ResolveUiInputModule();
            BindGlobalCancelAction();
            BindButtons();

            CircleTransition.Instance.OpenAsync(_circleTransitionDuration).Forget();
            ShowMainMenuAfterDelay(_delayBeforeMainMenuShow).Forget();

            PlayerInputBridge.Instance?.CanJoin(false);
        }

        private void OnDestroy()
        {
            UnbindGlobalCancelAction();
            UnbindButtons();
        }

        private async UniTask AnimateMainMenu()
        {
            if (_background)
                _background.gameObject.SetActive(true);

            if (_logo)
                _logo.gameObject.SetActive(true);

            SetInitialScale(PlayButton, Vector3.zero);
            SetInitialScale(CreditsButton, Vector3.zero);
            SetInitialScale(SettingsButton, Vector3.zero);
            SetInitialScale(QuitButton, Vector3.zero);
            SetInitialScale(DiscordButton, Vector3.zero);
            SetInitialScale(SteamButton, Vector3.zero);
            SetInitialScale(MailButton, Vector3.zero);

            if (_betaTest)
                _betaTest.transform.localScale = Vector3.zero;

            if (_background && _logo)
            {
                await Tween.Alpha(_background, 1f, _backgroundEaseDuration, _backgroundEase)
                    .Group(Tween.Alpha(_logo, 1f, _logoEaseDuration, _logoEase));
            }

            if (PlayButton && CreditsButton && SettingsButton && QuitButton)
            {
                await Tween.Scale(PlayButton.transform, 1.5f, _playButtonEaseDuration, _playButtonEase)
                    .Group(Tween.Scale(CreditsButton.transform, 1.5f, _creditsButtonEaseDuration, _creditsButtonEase))
                    .Group(Tween.Scale(SettingsButton.transform, 1.5f, _settingsButtonEaseDuration, _settingsButtonEase))
                    .Group(Tween.Scale(QuitButton.transform, 1.5f, _quitButtonEaseDuration, _quitButtonEase));
            }

            Sequence contactSequence = Sequence.Create();

            if (DiscordButton)
                contactSequence = contactSequence.Group(Tween.Scale(DiscordButton.transform, _contactScale, _contactEaseDuration, _contactEase));

            if (SteamButton)
                contactSequence = contactSequence.Group(Tween.Scale(SteamButton.transform, _contactScale, _contactEaseDuration, _contactEase));

            if (MailButton)
                contactSequence = contactSequence.Group(Tween.Scale(MailButton.transform, _contactScale, _contactEaseDuration, _contactEase));

            if (_betaTest)
                contactSequence = contactSequence.Group(Tween.Scale(_betaTest.transform, 1.1f, Mathf.Max(0.1f, _contactEaseDuration - 0.4f), _contactEase));

            await contactSequence;

            SelectButton(PlayButton);
        }

        private void BindButtons()
        {
            if (PlayButton)
                PlayButton.onClick.AddListener(LoadLobbyScene);

            if (SettingsButton)
                SettingsButton.onClick.AddListener(OpenSettingsPanel);

            if (CreditsButton)
                CreditsButton.onClick.AddListener(OpenCreditsPanel);

            if (QuitButton)
                QuitButton.onClick.AddListener(QuitGame);

            if (DiscordButton)
                DiscordButton.onClick.AddListener(OpenDiscordInvite);

            if (SteamButton)
                SteamButton.onClick.AddListener(OpenSteamPage);

            if (MailButton)
                MailButton.onClick.AddListener(OpenMail);

            if (AppleMusicSoulSoundsButton)
                AppleMusicSoulSoundsButton.onClick.AddListener(OpenAppleMusicSoulSounds);

            if (SpotifySoulSoundsButton)
                SpotifySoulSoundsButton.onClick.AddListener(OpenSpotifySoulSounds);

            if (YoutubeSoulSoundsButton)
                YoutubeSoulSoundsButton.onClick.AddListener(OpenYoutubeSoulSounds);
        }

        private void UnbindButtons()
        {
            if (PlayButton)
                PlayButton.onClick.RemoveListener(LoadLobbyScene);

            if (SettingsButton)
                SettingsButton.onClick.RemoveListener(OpenSettingsPanel);

            if (CreditsButton)
                CreditsButton.onClick.RemoveListener(OpenCreditsPanel);

            if (QuitButton)
                QuitButton.onClick.RemoveListener(QuitGame);

            if (DiscordButton)
                DiscordButton.onClick.RemoveListener(OpenDiscordInvite);

            if (SteamButton)
                SteamButton.onClick.RemoveListener(OpenSteamPage);

            if (MailButton)
                MailButton.onClick.RemoveListener(OpenMail);

            if (AppleMusicSoulSoundsButton)
                AppleMusicSoulSoundsButton.onClick.RemoveListener(OpenAppleMusicSoulSounds);

            if (SpotifySoulSoundsButton)
                SpotifySoulSoundsButton.onClick.RemoveListener(OpenSpotifySoulSounds);

            if (YoutubeSoulSoundsButton)
                YoutubeSoulSoundsButton.onClick.RemoveListener(OpenYoutubeSoulSounds);
        }

        private void ResolveUiInputModule()
        {
            _uiInputModule = _eventSystem.GetComponent<InputSystemUIInputModule>();

            if (!_uiInputModule)
            {
                Logs.LogWarning("[MenuManager] InputSystemUIInputModule is missing.", this);
                return;
            }

            _uiInputModule.enabled = true;

            EnableAction(_uiInputModule.point);
            EnableAction(_uiInputModule.leftClick);
            EnableAction(_uiInputModule.rightClick);
            EnableAction(_uiInputModule.middleClick);
            EnableAction(_uiInputModule.scrollWheel);
            EnableAction(_uiInputModule.move);
            EnableAction(_uiInputModule.submit);
            EnableAction(_uiInputModule.cancel);
        }

        private void BindGlobalCancelAction()
        {
            UnbindGlobalCancelAction();

            if (!_uiInputModule)
                return;

            InputActionReference cancelActionRef = _uiInputModule.cancel;

            if (cancelActionRef == null || cancelActionRef.action == null)
            {
                Logs.LogWarning("[MenuManager] UI cancel action reference is missing on InputSystemUIInputModule.", this);
                return;
            }

            _cancelAction = cancelActionRef.action;

            _cancelAction.performed += HandleGlobalCancelPerformed;
            _cancelAction.Enable();
        }

        private void UnbindGlobalCancelAction()
        {
            if (_cancelAction == null)
                return;

            _cancelAction.performed -= HandleGlobalCancelPerformed;
            _cancelAction = null;
        }

        private void HandleGlobalCancelPerformed(InputAction.CallbackContext context)
        {
            if (_isLoadingLobby)
                return;

            TryCancelCurrentPanel();
        }

        private async UniTask ShowMainMenuAfterDelay(float delay)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(delay));

            if (MainMenuPanel)
                MainMenuPanel.Show();

            await AnimateMainMenu();
        }

        private void LoadLobbyScene() => LoadLobbySceneAsync().Forget();
        
        private async UniTask LoadLobbySceneAsync()
        {
            if (_isLoadingLobby || _isTransitioning)
                return;
            
            _isTransitioning = true;
            _isLoadingLobby = true;
            
            await UniTask.Delay(TimeSpan.FromSeconds(_buttonAnimationDuration));
            
            _gameService?.LoadLobbySceneAsync().Forget();
        }

        private void OpenSettingsPanel() => OpenSettingsPanelAsync().Forget();
        
        private async UniTask OpenSettingsPanelAsync()
        {
            if (_isLoadingLobby || _isTransitioning)
                return;

            _isTransitioning = true;
            
            AudioService.PlayOneShot(AudioService.FMODEvents.SFX_UI_Select);
            
            await UniTask.Delay(TimeSpan.FromSeconds(_buttonAnimationDuration));
            
            MainMenuPanel?.Hide();
            SettingsPanel?.Show();

            SetCharactersVisible(false);
        }

        private void CloseSettingsPanel()
        {
            AudioService.PlayOneShot(AudioService.FMODEvents.SFX_UI_Return);
            
            SettingsPanel?.Hide();
            MainMenuPanel?.Show();

            SetCharactersVisible(true);
            SelectButton(SettingsButton);
        }

        private void OpenCreditsPanel() => OpenCreditsPanelAsync().Forget();
        
        private async UniTask OpenCreditsPanelAsync()
        {
            if (_isLoadingLobby || _isTransitioning)
                return;

            _isTransitioning = true;

            await UniTask.Delay(TimeSpan.FromSeconds(_buttonAnimationDuration));
            
            MainMenuPanel?.Hide();
            CreditsPanel?.Show();

            SetCharactersVisible(false);

            Cursor.lockState = CursorLockMode.Confined;
            Cursor.visible = true;
        }

        private void CloseCreditsPanel()
        {
            AudioService.PlayOneShot(AudioService.FMODEvents.SFX_UI_Return);
            
            CreditsPanel?.Hide();
            MainMenuPanel?.Show();

            SetCharactersVisible(true);

            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;

            SelectButton(CreditsButton);
        }

        private void QuitGame() => QuitGameAsync().Forget();
        
        private async UniTask QuitGameAsync()
        {
            if (_isTransitioning)
                return;

            _isTransitioning = true;
            
            
            await UniTask.Delay(TimeSpan.FromSeconds(_buttonAnimationDuration));
            Application.Quit();

#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#endif
        }

        private bool TryCancelCurrentPanel()
        {
            Logs.Log("[MenuManager] Cancel triggered.");

            if (SettingsPanel && SettingsPanel.IsVisible())
            {
                _isTransitioning = false;
                CloseSettingsPanel();
                return true;
            }

            if (CreditsPanel && CreditsPanel.IsVisible())
            {
                _isTransitioning = false;
                CloseCreditsPanel();
                return true;
            }

            return false;
        }

        private void OpenDiscordInvite() => Application.OpenURL(_discordURL);

        private void OpenSteamPage() => Application.OpenURL(_steamStorePageURL);

        private void OpenMail() => Application.OpenURL(_mailURL);

        private void OpenAppleMusicSoulSounds() => Application.OpenURL(_appleMusicSoulSoundsURL);

        private void OpenSpotifySoulSounds() => Application.OpenURL(_spotifySoulSoundsURL);

        private void OpenYoutubeSoulSounds() => Application.OpenURL(_youtubeSoulSoundsURL);

        private void SetCharactersVisible(bool visible)
        {
            if (_animatedCharacterElements == null)
                return;

            for (int i = 0; i < _animatedCharacterElements.Length; i++)
            {
                if (_animatedCharacterElements[i])
                    _animatedCharacterElements[i].SetActive(visible);
            }
        }

        private void SelectButton(Selectable selectable)
        {
            if (!_eventSystem || !selectable)
                return;

            if (!selectable.gameObject.activeInHierarchy || !selectable.IsInteractable())
                return;

            _eventSystem.SetSelectedGameObject(null);
            _eventSystem.SetSelectedGameObject(selectable.gameObject);
        }

        private static void SetInitialScale(Button button, Vector3 scale)
        {
            if (button)
                button.transform.localScale = scale;
        }

        private static void EnableAction(InputActionReference actionReference)
        {
            InputAction action = actionReference ? actionReference.action : null;
            action?.Enable();
        }

        private void CheckActivePanels()
        {
            if (SettingsPanel && !SettingsPanel.gameObject.activeSelf)
                SettingsPanel.gameObject.SetActive(true);

            if (CreditsPanel && !CreditsPanel.gameObject.activeSelf)
                CreditsPanel.gameObject.SetActive(true);
        }
    }
}