using System;
using System.Collections;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace MortierFu
{
    public sealed class LobbySettingsPanel : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private GameObject _root;

        [Header("Data")]
        [SerializeField] private LobbyMatchSettingsData _matchSettingsData;

        [Header("Settings Event System")]
        [SerializeField] private EventSystem _settingsEventSystem;
        [SerializeField] private InputSystemUIInputModule _settingsInputModule;

        [Header("Unity UI")]
        [SerializeField] private Selectable _firstSelected;
        [SerializeField] private UISelectedScrollFollower _scrollFollower;
        [SerializeField] private TextMeshPro _gameModeText;
        
        [Header("Settings Items")]
        [SerializeField] private UIMatchSelectableItemBase[] _settingsItems;

        [Header("Settings GameMode Custom Pannel")]
        [SerializeField] private float panelGameModeOpenXPos;
        [SerializeField] private float panelGameModeCloseXPos;
        [SerializeField] private float panelGameModeDurationTween = 0.2f;
        [SerializeField] private Ease panelGameModeEaseTween = Ease.InBounce;
        
        [Header("Optional")]
        [SerializeField] private TEMP_LobbyRecommendedScoreDisplay _recommendedScoreDisplay;

        private readonly UnityPlayerUISession _uiSession = new();

        private PlayerManager _activePlayer;
        private Action<PlayerManager> _onClosed;
        private Coroutine _selectionRoutine;
        private GamePauseSystem _gamePauseSystem;

        private int _currentPlayerCount = 1;
        private bool _isOpen;
        private bool _isOpening;
        
        private MultiplayerEventSystem _globalEventSystem; 
        private InputSystemUIInputModule _globalInputModule;
        
        private Tween _panelGameModeTween;
        
        private void Awake()
        {
            if (_root)
                _root.SetActive(false);
         
            _globalEventSystem = (MultiplayerEventSystem)(_globalEventSystem ? _globalEventSystem : EventSystem.current);
            _globalInputModule = _globalInputModule ? _globalInputModule : _globalEventSystem?.GetComponent<InputSystemUIInputModule>();
            
            SetSettingsEventSystemActive(false);
        }

        private void OnEnable()
        {
            if (!_matchSettingsData)
                return;
            
            _matchSettingsData.OnChanged -= Refresh;
            _matchSettingsData.OnChanged += Refresh;
        }

        private void Start()
        {
            _gamePauseSystem = SystemManager.Instance.Get<GamePauseSystem>();
            _gamePauseSystem.Resumed += RestartLobbySetting;
        }

        private void OnDisable()
        {
            if (_matchSettingsData)
                _matchSettingsData.OnChanged -= Refresh;

            StopSelectionRoutine();
            _uiSession.End();
            SetSettingsEventSystemActive(false);
        }

        private void OnDestroy()
        {
            _gamePauseSystem.Resumed -= RestartLobbySetting;
            StopSelectionRoutine();
            _uiSession.End();
            SetSettingsEventSystemActive(false);
        }

        public void Open(PlayerManager player, Action<PlayerManager> onClosed)
        {
            if (!player)
                return;

            StopSelectionRoutine();

            _activePlayer = player;
            _onClosed = onClosed;
            _currentPlayerCount = GetCurrentLobbyPlayerCount();

            _isOpen = true;
            _isOpening = true;

            if (_matchSettingsData)
                _matchSettingsData.ApplyRecommendedForPlayerCount(_currentPlayerCount);

            if (_root)
                _root.SetActive(true);

            SetSettingsEventSystemActive(true);

            BindItems();

            _scrollFollower.gameObject.SetActive(_matchSettingsData.SelectedRuleset.AllowEditing);
            
            Refresh();
            
            _uiSession.Begin(player, _settingsEventSystem, _settingsInputModule, _firstSelected, PlayerControlContext.LobbySettingsOwner);

            _selectionRoutine = StartCoroutine(SelectFirstWhenReady());
            
            AudioService.PlayOneShot(AudioService.FMODEvents.SFX_Augment_Showcase, transform.position);
        }

        public void Close() => CloseInternal(notifyClosed: false);

        public void CloseFromUI() => CloseInternal(notifyClosed: true);

        public void ValidateCurrentSelection()
        {
            if (!_isOpen || _isOpening)
                return;

            if (!_settingsEventSystem)
                return;

            GameObject selected = _settingsEventSystem.currentSelectedGameObject;

            if (!selected)
            {
                SelectFirstAvailable();
                return;
            }

            UIMatchSelectableItemBase item = selected.GetComponent<UIMatchSelectableItemBase>();

            if (!item || !item.CanReceiveSelection)
                SelectFirstAvailable();
        }

        private void RestartLobbySetting()
        {
            if (!_isOpen) return;
            
            PlayerManager tempPlayer = _activePlayer;
            Action<PlayerManager> onClosed = _onClosed;
            CloseInternal(false);
                
            _uiSession.End();
            Open(tempPlayer, onClosed);
        }
        
        public bool SelectRelativeTo(UIMatchSelectableItemBase currentItem, int direction)
        {
            if (currentItem == null || _settingsItems == null || _settingsItems.Length == 0 || direction == 0)
                return false;

            int currentIndex = Array.IndexOf(_settingsItems, currentItem);

            if (currentIndex < 0)
                return false;

            int index = currentIndex + direction;

            while (index >= 0 && index < _settingsItems.Length)
            {
                UIMatchSelectableItemBase candidate = _settingsItems[index];

                if (candidate && candidate.CanReceiveSelection)
                {
                    SelectItem(candidate);
                    return true;
                }

                index += direction;
            }

            return false;
        }

        private IEnumerator SelectFirstWhenReady()
        {
            yield return null;

            Canvas.ForceUpdateCanvases();

            _scrollFollower?.ResetToTop();

            if (_firstSelected is UIMatchSelectableItemBase firstItem && firstItem.CanReceiveSelection)
                SelectItem(firstItem);
            else
                SelectFirstAvailable();

            _scrollFollower?.FollowSelectedNow();

            _isOpening = false;
            _selectionRoutine = null;
        }

        private void SelectFirstAvailable()
        {
            if (_settingsItems == null)
                return;

            for (int i = 0; i < _settingsItems.Length; i++)
            {
                UIMatchSelectableItemBase item = _settingsItems[i];

                if (!item || !item.CanReceiveSelection)
                    continue;

                SelectItem(item);
                return;
            }

            if (_settingsEventSystem)
                _settingsEventSystem.SetSelectedGameObject(null);
        }

        private void SelectItem(UIMatchSelectableItemBase item)
        {
            if (!_settingsEventSystem || !item)
                return;
            
            _settingsEventSystem.SetSelectedGameObject(null);
            _settingsEventSystem.SetSelectedGameObject(item.gameObject);

            _scrollFollower?.FollowSelectedNow();
        }

        private void CloseInternal(bool notifyClosed)
        {
            StopSelectionRoutine();

            PlayerManager activePlayer = _activePlayer;
            Action<PlayerManager> onClosed = _onClosed;

            _uiSession.End();
            SetSettingsEventSystemActive(false);

            if (_root)
                _root.SetActive(false);

            _activePlayer = null;
            _onClosed = null;
            _isOpen = false;
            _isOpening = false;

            if (notifyClosed && activePlayer)
                onClosed?.Invoke(activePlayer);
            
            AudioService.PlayOneShot(AudioService.FMODEvents.SFX_UI_Return, transform.position);
        }

        private void BindItems()
        {
            if (_settingsItems == null)
                return;

            for (int i = 0; i < _settingsItems.Length; i++)
            {
                if (_settingsItems[i])
                    _settingsItems[i].Bind(this, _matchSettingsData, _currentPlayerCount);
            }
        }
        
        
        private void Refresh()
        {
            if (_settingsItems != null)
            {
                for (int i = 0; i < _settingsItems.Length; i++)
                {
                    if (_settingsItems[i])
                        _settingsItems[i].Refresh();
                }
            }

            _recommendedScoreDisplay?.Refresh(_matchSettingsData ? _matchSettingsData.ScoreToWin : 0);
            _gameModeText.text = _matchSettingsData.SelectedRuleset.DisplayName;
            
            HandleAnimationForCustom(isCustom: _matchSettingsData.SelectedRuleset.AllowEditing);
            
            ValidateCurrentSelection();
        }

        private void HandleAnimationForCustom(bool isCustom)
        {
            if (!_scrollFollower.gameObject.activeInHierarchy)
                _scrollFollower.gameObject.SetActive(true);
            
            if (_isOpening && isCustom)
                _scrollFollower.gameObject.SetActive(true);
            
            if (_isOpening && !isCustom)
                _scrollFollower.gameObject.SetActive(false);
            
            _panelGameModeTween.Stop();
            
            RectTransform target = _scrollFollower.transform as RectTransform;
                
            float endValueYPos = isCustom ? panelGameModeOpenXPos : panelGameModeCloseXPos;
                
            if (target != null) _panelGameModeTween = Tween.LocalPositionX(
                target,
                endValueYPos,
                panelGameModeDurationTween,
                panelGameModeEaseTween);
        }

        private void SetSettingsEventSystemActive(bool active)
        {
            if (active)
            {
                if (_globalInputModule)
                    _globalInputModule.enabled = false;

                if (_globalEventSystem)
                    _globalEventSystem.enabled = false;

                if (_settingsEventSystem)
                    _settingsEventSystem.enabled = true;

                if (_settingsInputModule)
                    _settingsInputModule.enabled = true;

                return;
            }

            if (_settingsEventSystem)
            {
                _settingsEventSystem.SetSelectedGameObject(null);
                _settingsEventSystem.enabled = false;
            }

            if (_settingsInputModule)
                _settingsInputModule.enabled = false;

            if (_globalEventSystem)
                _globalEventSystem.enabled = true;

            if (_globalInputModule)
                _globalInputModule.enabled = true;
        }

        private void StopSelectionRoutine()
        {
            if (_selectionRoutine == null)
                return;

            StopCoroutine(_selectionRoutine);
            _selectionRoutine = null;
        }

        private static int GetCurrentLobbyPlayerCount()
        {
            LobbyService lobbyService = ServiceManager.Instance?.Get<LobbyService>();

            if (lobbyService == null)
                return 1;

            return Mathf.Max(1, lobbyService.CurrentPlayerCount);
        }
    }
}