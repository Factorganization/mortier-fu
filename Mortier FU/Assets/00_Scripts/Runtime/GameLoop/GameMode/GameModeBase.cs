using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using MortierFu.Analytics;
using MortierFu.Shared;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MortierFu
{
    public abstract class GameModeBase : IGameMode
    {
        private List<PlayerTeam> teams;
        private List<RoundInfo> RoundHistory { get; } = new();
        private RoundInfo _currentRound;
        public ReadOnlyCollection<PlayerTeam> Teams { get; private set; }

        private List<PlayerCharacter> alivePlayers;
        public ReadOnlyCollection<PlayerCharacter> AlivePlayers { get; private set; }

        private PlayerTeam gameVictor;
        private GameState currentState;

        private ScoreController _scoreController;
        private RoundController _roundController;
        private PlayerSpawnController _playerSpawnController;
        private AugmentRaceController _augmentRaceController;
        private PreviousRoundWinnerRaceSizeController _previousRoundWinnerRaceSizeController;
        private RoundStartController _roundStartController;
        private RaceRuntimeController _raceRuntimeController;
        private RoundWinnerPresentationController _roundWinnerPresentationController;
        private ScorePhaseController _scorePhaseController;
        private PlayerTeamSetupController _teamSetupController;
        private GameModeDependencies _dependencies;
        private AugmentRaceTutorialController _augmentRaceTutorialController;

        // Dependencies
        private LobbyService lobbyService => _dependencies?.LobbyService;
        private AudioService audioService => _dependencies?.AudioService;

        private AugmentSelectionSystem augmentSelectionSys => _dependencies?.AugmentSelectionSystem;
        private LevelSystem levelSystem => _dependencies?.LevelSystem;
        private BombshellSystem bombshellSys => _dependencies?.BombshellSystem;
        private GhostSystem _ghostSystem => _dependencies?.GhostSystem;
        private CameraSystem cameraSystem => _dependencies?.CameraSystem;
        private AnalyticsSystem analyticsSystem => _dependencies?.AnalyticsSystem;
        
        private CountdownTimer timer;

        private AsyncOperationHandle<SO_GameModeData> _dataHandle;
        private AsyncOperationHandle<SO_GameFlowSettings> _flowSettingsHandle;
        private CancellationTokenSource _gameplayCancellation;

        private bool _isRaceMapLoaded;
        private bool _isArenaMapLoaded;
        private bool _isRaceScenePrepared;

        public SO_GameModeData Data => _dataHandle.Result;
        public SO_GameFlowSettings FlowSettings => _flowSettingsHandle.Result;
        public GameState CurrentGameState => currentState;
        public ScoreController ScoreController => _scoreController;
        public AugmentRaceTutorialController TutorialController => _augmentRaceTutorialController;

        public virtual int MinPlayerCount => Data.MinPlayerCount;
        public virtual int MaxPlayerCount => Data.MaxPlayerCount;

        public MatchConfig MatchConfig { get; private set; } = MatchConfig.Default;

        public int ScoreToWin => MatchConfig.ScoreToWin;

        public bool IsReady
        {
            get
            {
                var players = lobbyService?.GetPlayers();

                if (players == null)
                    return false;

                return players.Count >= MinPlayerCount && players.Count <= MaxPlayerCount;
            }
        }

        public int CurrentRoundCount => _currentRound.RoundIndex;

        /// EVENTS
        public event Action<GameState> OnGameStateChanged;

        public event Action<PlayerManager, PlayerManager> OnPlayerKilled;
        public event Action OnGameStarted;
        public event Action<RoundInfo> OnRoundStarted;
        public event Action<RoundInfo> OnRoundGameplayStarted;
        public event Func<CancellationToken, UniTask> OnRoundStartPresentationAsync;
        public event Action OnScoreDisplayOver;
        public event Action<RoundInfo> OnRoundEnded;
        public event Func<RoundInfo, CancellationToken, UniTask> OnRoundEndedAsync;
        public event Action OnRaceStart;
        public event Func<CancellationToken, UniTask> OnAugmentRaceStartPresentationAsync;
        public event Action OnRacePlayerConfirmation;
        public event Func<UniTask, Action, Action, CancellationToken, UniTask> OnRaceEndedUI;
        public event Action<int> OnGameEnded;

        public Action<AugmentRaceTutorialController> OnTutorialRaceControllerInit; //dégeulasse

        public virtual async UniTask Initialize()
        {
            _dependencies = GameModeDependencies.ResolveServices();

            if (!_dependencies.HasRequiredServices())
                Logs.LogError("[GameModeBase] Missing required services.");

            _dataHandle = await AddressablesUtils.LazyLoadAsset<SO_GameModeData>("DA_GM_FFA");
            _flowSettingsHandle = await AddressablesUtils.LazyLoadAsset<SO_GameFlowSettings>("DA_GameFlowSettings");

            timer = new CountdownTimer(0f);

            _roundStartController = new RoundStartController(timer, FlowSettings, roundInfo => OnRoundStarted?.Invoke(roundInfo));

            Logs.Log("Game mode initialized successfully.");
        }

        private List<PlayerTeam> CreateTeamsForMatch(IReadOnlyList<PlayerManager> players) => _teamSetupController.CreateFreeForAllTeams(players);

        private void ResolveGameplayDependencies()
        {
            _dependencies.ResolveGameplaySystems();

            if (!_dependencies.HasRequiredGameplaySystems())
                Logs.LogError("[GameModeBase] Missing required gameplay systems.");
        }

        private void CreateTeams()
        {
            _teamSetupController = new PlayerTeamSetupController();

            var players = lobbyService.GetPlayers();
            
            
            teams = CreateTeamsForMatch(players);
            Teams = teams.AsReadOnly();

            alivePlayers = new List<PlayerCharacter>();
            AlivePlayers = new ReadOnlyCollection<PlayerCharacter>(alivePlayers);
        }

        private void CreateControllers()
        {
            _playerSpawnController = new PlayerSpawnController(teams, levelSystem); _raceRuntimeController = new RaceRuntimeController();

            _roundWinnerPresentationController = new RoundWinnerPresentationController();

            _scorePhaseController = new ScorePhaseController(() => _roundStartController.StopCountdown(), () => OnScoreDisplayOver?.Invoke());
            
            _augmentRaceTutorialController = new AugmentRaceTutorialController(this);
            
            _augmentRaceController = new AugmentRaceController(teams, augmentSelectionSys, _playerSpawnController, SetPlayersControlContext, () => OnRaceStart?.Invoke(), 
                () => _raceRuntimeController?.GetAugmentPickers(), augmentCount => _raceRuntimeController?.BuildAugmentLayout(augmentCount));
            
            _roundController = new RoundController(teams, alivePlayers);
            _roundController.OnPlayerDied += HandleRoundPlayerDied;
            _roundController.OnPlayerKilled += HandleRoundPlayerKilled;

            AlivePlayers = _roundController.AlivePlayers;

            _scoreController = new ScoreController(Data, teams, ScoreToWin, analyticsSystem);

            _previousRoundWinnerRaceSizeController = new PreviousRoundWinnerRaceSizeController();
        }

        public virtual async UniTask StartGame()
        {
            GhostTutorialSession.Clear();
            
            ResolveGameplayDependencies();
            
            CreateTeams();

            CreateControllers();

            if (!IsReady)
            {
                Logs.LogWarning("Not enough players or too many players for this gamemode ! Falling back to playground.");

                await levelSystem.LoadArenaMap();

                InitializeRound();
                return;
            }

            _currentRound = new RoundInfo();
            gameVictor = null;

            _gameplayCancellation = new CancellationTokenSource();
            
            ForEachCurrentPlayerCharacter(character => character.ClearAugments());
            
            GameplayLoop(_gameplayCancellation.Token).Forget();

            Logs.Log("Starting the game...");
            
            if (lobbyService.Players.Count == 4)
            {
                SteamManager.UnlockAchievement("START_FOUR_PLAYERS");
            }
        }

        private async UniTask RunAugmentRacePhaseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            await EnsureRaceScenePreparedAsync(cancellationToken);
            
            ServiceManager.Instance.Get<SceneService>().HideLoadingScreen();
            
            try
            {
                augmentSelectionSys?.SetMatchConfig(MatchConfig);
                augmentSelectionSys?.SetCurrentRaceNumber(GetCurrentAugmentRaceNumber());
                
                await _augmentRaceTutorialController.WaitForTutorialCheck(cancellationToken); //TODO ADD TUTO
                
                await _augmentRaceController.PrepareSelectionAsync(cancellationToken, FlowSettings.AugmentStartShowcaseDelay);
                await _raceRuntimeController.AfterShowcaseCompleted(cancellationToken);
                
                cancellationToken.ThrowIfCancellationRequested();

                audioService.SetPhase(0, AudioService.PhaseType.TWICE);

                await RunAugmentRaceStartPresentationAsync(cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                UpdateGameState(GameState.AugmentRace);
                OnRacePlayerConfirmation?.Invoke();
                
                _raceRuntimeController?.BeginGameplay();

                float raceDuration = _raceRuntimeController != null ? _raceRuntimeController.GetRaceDuration(MatchConfig.RaceTimeLimit) : MatchConfig.RaceTimeLimit;

                _augmentRaceController.StartRaceTimer(raceDuration);
                
                await _augmentRaceController.WaitUntilSelectionOverAsync(cancellationToken);

                _augmentRaceController.EndSelection();

                EndRace();

                _raceRuntimeController?.EndRace();
                
                _previousRoundWinnerRaceSizeController?.Clear();

                EnablePlayerGravity(false);

                await RunAugmentSummaryAndOptionalArenaPreloadAsync(cancellationToken);
            }
            finally
            {
                _raceRuntimeController?.EndRace();
                _previousRoundWinnerRaceSizeController?.Clear();
            }
        }

        private async UniTask WaitUntilRoundOverAsync(CancellationToken cancellationToken)
        {
            while (_roundController is { OneTeamStanding: false })
            {
                cancellationToken.ThrowIfCancellationRequested();

                await UniTask.Yield();

                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private async UniTask RunRoundEndPresentationAsync(CancellationToken cancellationToken)
        {
            if (OnRoundEndedAsync == null)
                return;

            foreach (Delegate @delegate in OnRoundEndedAsync.GetInvocationList())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var handler = (Func<RoundInfo, CancellationToken, UniTask>)@delegate;
                await handler.Invoke(_currentRound, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private async UniTask RunRoundPhaseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await EnsureArenaMapLoadedAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            InitializeRound();

            await RunRoundStartPresentationAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            
            SetPlayersControlContext(PlayerControlContext.RoundGameplay);
            OnRoundGameplayStarted?.Invoke(_currentRound);

            await WaitUntilRoundOverAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            UpdateGameState(GameState.EndRound);

            InitializeEndRound();

            await UniTask.Delay(TimeSpan.FromSeconds(FlowSettings.CameraZoomOnWinnerDuration + FlowSettings.ShowScoreboardDelayFactor), cancellationToken: cancellationToken);

            var matchWillEnd = IsGameOver(out gameVictor);

            await RunRoundEndPresentationAndOptionalRacePreloadAsync(cancellationToken);
            
            audioService.StopAmbiance().Forget();
            audioService.ClearAllMapInstances();

            await PrepareRaceSceneUnderScoreboardCoverAsync(!matchWillEnd, cancellationToken);
            
            cancellationToken.ThrowIfCancellationRequested();
        }

        private async UniTask RunMatchLoopAsync(CancellationToken cancellationToken)
        {
            while (currentState != GameState.EndGame)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await RunAugmentRacePhaseAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (currentState == GameState.EndGame)
                    break;

                await RunRoundPhaseAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsGameOver(out gameVictor)) continue;
                
                Logs.Log($"Game Over! Team {gameVictor.Index} wins!");
                UpdateGameState(GameState.EndGame);
            }
        }

        private async UniTaskVoid GameplayLoop(CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                UpdateGameState(GameState.StartGame);
                OnGameStarted?.Invoke();

                audioService.StartMusic(AudioService.FMODEvents.MUS_Gameplay).Forget();

                await RunMatchLoopAsync(cancellationToken);
                
                // cancel transition
                
                cancellationToken.ThrowIfCancellationRequested();

                await EndGame();
            }
            catch (OperationCanceledException)
            {
                Logs.Log("[GameModeBase] Gameplay coroutine canceled.");
            }
        }

        public bool IsGameOver(out PlayerTeam victor)
        {
            if (_scoreController != null) return _scoreController.IsGameOver(out victor);
            victor = null;
            return false;
        }

        private void EnablePlayerGravity(bool enabled = true) => _playerSpawnController?.SetPlayerGravity(enabled);

        private void SpawnPlayers() => _playerSpawnController?.SpawnPlayers(_currentRound.RoundIndex);

        private void SetPlayersControlContext(PlayerControlContext context)
        {
            foreach (var team in teams)
            {
                foreach (var member in team.Members)
                {
                    member.SetControlContext(context);
                }
            }

#if UNITY_EDITOR
            if (EditorPrefs.GetBool("DummyDebugToolEnabled", true))
            {
                //PlayerInputSwapper.Instance.UpdateActivePlayer();
            }
#endif
        }

        private void ResetPlayers() => _playerSpawnController?.ResetPlayers();

        private void InitializeRound()
        {
            UpdateGameState(GameState.RoundCountdown);

            _currentRound = new RoundInfo
            {
                RoundIndex = RoundHistory.Count + 1,
                WinningTeam = teams.FirstOrDefault(t => t.Rank == 1)
            };

            RoundHistory.Add(_currentRound);

            ResetPlayers();
            SpawnPlayers();
            ApplyMatchConfigToPlayers();
            ActivatePlayerAugmentsForRound();
            EnablePlayerGravity();

            SetPlayersControlContext(PlayerControlContext.RoundCountdown);

            _roundController.BeginRound();

            var groupMembers = AlivePlayers.Select(player => player.transform).ToArray();
            cameraSystem.Controller.PopulateTargetGroup(groupMembers);

            _roundStartController.StartCountdown(_currentRound);
        }

        private void InitializeEndRound()
        {
            _roundStartController.StopCountdown();
            _roundController.EndRound();

            bombshellSys.ClearActiveBombshells();

            EventBus<TriggerEndRound>.Raise(new TriggerEndRound()
            {
                WinningTeam = _roundController.WinningTeam
            });

            SetPlayersControlContext(PlayerControlContext.RoundEnded);

            EvaluateScores();

            _currentRound.WinningTeam = _roundController.WinningTeam;

            cameraSystem.Controller.EndFightCameraMovement(_currentRound.WinningTeam.Members[0].Character.transform, FlowSettings.CameraZoomOnWinnerDuration, _gameplayCancellation?.Token ?? CancellationToken.None).Forget();
            
            _roundWinnerPresentationController.PresentWinner(_currentRound.WinningTeam);

            audioService.SetPhase(1, AudioService.PhaseType.AMBIENCE);
            
            OnRoundEnded?.Invoke(_currentRound);
        }

        private void EvaluateScores()
        {
            if (_scoreController == null)
            {
                Logs.LogError("[GameModeBase] Cannot evaluate scores because ScoreController is null.");
                return;
            }

            gameVictor = _scoreController.EvaluateScores();
        }

        private void HideScores() => _scorePhaseController?.HideScores();

        private void StartRace()
        {
            _raceRuntimeController?.PrepareRace(CreateRaceModeContext(), FlowSettings ? FlowSettings.DefaultRaceModeDefinition : null);

            _augmentRaceController.BeginRace(_currentRound.RoundIndex);
            ResetPlayersForRace();
        }
        
        private RaceModeContext CreateRaceModeContext()
        {
            return new RaceModeContext
            {
                Teams = Teams,
                PreviousRoundWinnerTeam = _currentRound.WinningTeam,

                LevelSystem = levelSystem,
                PlayerSpawnController = _playerSpawnController,
                AugmentSelectionSystem = augmentSelectionSys,

                SetAllPlayersControlContext = SetPlayersControlContext,
                ApplyBullySize = (character, size) => _previousRoundWinnerRaceSizeController?.Apply(character, size, applyControlContext: false),
                ClearBullySize = () => _previousRoundWinnerRaceSizeController?.Clear()
            };
        }
        
        private void EndRace()
        {
            UpdateGameState(GameState.EndAugmentRace);

            audioService.StopAmbiance();
            audioService.ClearAllMapInstances();

            _augmentRaceController.EndRace();
        }

        public int GetWinnerPlayerIndex()
        {
            PlayerManager winner = GetWinnerPlayer();

            if (winner)
                return winner.PlayerIndex;

            Logs.LogWarning("[GameModeBase] GetWinnerPlayerIndex called but no winner player was found.");
            return -1;
        }
        
        public PlayerManager GetWinnerPlayer()
        {
            PlayerTeam winnerTeam = gameVictor;

            if (winnerTeam == null && IsGameOver(out PlayerTeam resolvedVictor))
                winnerTeam = resolvedVictor;

            if (winnerTeam == null)
                winnerTeam = _currentRound.WinningTeam;

            PlayerManager winner = GetTeamPrimaryPlayer(winnerTeam);

            if (winner)
                return winner;

            Logs.LogWarning("[GameModeBase] GetWinnerPlayer called but no winner player was found.");
            return null;
        }
        
        private Vector2 GetWinnerScreenPosition()
        {
            PlayerManager winner = GetWinnerPlayer();

            if (winner == null || winner.Character == null)
            {
                Logs.LogWarning("[GameModeBase] Cannot compute winner screen position: no winner character found. Using screen center.");
                return new Vector2(0.5f, 0.5f);
            }

            Camera cam = cameraSystem?.Controller != null ? cameraSystem.Controller.GetComponentInChildren<Camera>() : Camera.main;

            if (cam == null)
            {
                Logs.LogWarning("[GameModeBase] Cannot compute winner screen position: no camera found. Using screen center.");
                return new Vector2(0.5f, 0.5f);
            }

            Transform headAnchor = winner.Character.CustomizationVisual != null ? winner.Character.CustomizationVisual.HeadAnchor : null;
            Vector3 headWorldPos = headAnchor != null ? headAnchor.position : winner.Character.transform.position + Vector3.up * 1.6f;

            Vector3 viewportPos = cam.WorldToViewportPoint(headWorldPos);

            return new Vector2(viewportPos.x, viewportPos.y);
        }
        private PlayerManager GetTeamPrimaryPlayer(PlayerTeam team)
        {
            if (team?.Members == null || team.Members.Count == 0)
                return null;

            PlayerManager player = team.Members[0];

            return player ? player : null;
        }

        public int GetTeamPrimaryPlayerIndex(PlayerTeam team)
        {
            PlayerManager player = GetTeamPrimaryPlayer(team);

            return player ? player.PlayerIndex : -1;
        }

        private async UniTask EndGame()
        {
            Vector2 winnerScreenPos = GetWinnerScreenPosition();

            await CircleTransition.Instance.CloseAsync(FlowSettings.RoundTransitionDuration, winnerScreenPos);

            await levelSystem.LoadWinGameMap();

            _playerSpawnController?.SpawnGameWinner(gameVictor);

            audioService.StartMusic(AudioService.FMODEvents.MUS_Victory).Forget();
            SetPlayersControlContext(PlayerControlContext.EndGame);
            OnGameEnded?.Invoke(GetWinnerPlayerIndex());
            Logs.Log("Game has ended.");

            await CircleTransition.Instance.OpenAsync(FlowSettings.RoundTransitionDuration, winnerScreenPos);
        }
        private async UniTask RunAugmentRaceStartPresentationAsync(CancellationToken cancellationToken)
        {
            if (OnAugmentRaceStartPresentationAsync == null)
                return;

            foreach (Delegate @delegate in OnAugmentRaceStartPresentationAsync.GetInvocationList())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var handler = (Func<CancellationToken, UniTask>)@delegate;
                await handler.Invoke(cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private async UniTask RunRoundStartPresentationAsync(CancellationToken cancellationToken)
        {
            if (OnRoundStartPresentationAsync == null)
                return;

            foreach (Delegate @delegate in OnRoundStartPresentationAsync.GetInvocationList())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var handler = (Func<CancellationToken, UniTask>)@delegate;
                await handler.Invoke(cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private void UpdateGameState(GameState newState)
        {
            currentState = newState;
            OnGameStateChanged?.Invoke(newState);
        }

        public void SetMatchConfig(MatchConfig config)
        {
            config.Clamp();

            MatchConfig = config;

            _scoreController?.SetScoreToWin(config.ScoreToWin);
        }
        
        private void ApplyMatchConfigToPlayers() => ForEachCurrentPlayerCharacter(character => character.ApplyMatchConfig(MatchConfig));

        public void SetScoreToWin(int score) => SetMatchConfig(new MatchConfig(score));

        public virtual void Update() => _raceRuntimeController?.Tick(Time.deltaTime);

        public virtual void Dispose()
        {
            _gameplayCancellation?.Cancel();
            _gameplayCancellation?.Dispose();
            _gameplayCancellation = null;

            if (_dataHandle.IsValid())
                Addressables.Release(_dataHandle);

            if (_flowSettingsHandle.IsValid())
                Addressables.Release(_flowSettingsHandle);

            if (_roundController != null)
            {
                _roundController.OnPlayerDied -= HandleRoundPlayerDied;
                _roundController.OnPlayerKilled -= HandleRoundPlayerKilled;
                _roundController.Dispose();
                _roundController = null;
            }

            _roundStartController?.Dispose();
            _roundStartController = null;

            _raceRuntimeController?.Dispose();
            _raceRuntimeController = null;
            
            _scoreController = null;
            _playerSpawnController = null;
            _augmentRaceController = null;
            _previousRoundWinnerRaceSizeController?.Clear();
            _previousRoundWinnerRaceSizeController = null;
            _roundWinnerPresentationController = null;
            _scorePhaseController = null;
            _teamSetupController = null;

            teams?.Clear();
            alivePlayers?.Clear();
            RoundHistory?.Clear();

            timer?.Dispose();
            timer = null;

            _dependencies = null;
        }

        private void HandleRoundPlayerDied(PlayerCharacter character)
        {
            if (character == null)
                return;

            cameraSystem?.Controller?.RemoveTarget(character.transform);
        }

        private void HandleRoundPlayerKilled(PlayerManager killer, PlayerManager victim) => OnPlayerKilled?.Invoke(killer, victim);

        private async UniTask EnsureRaceMapLoadedAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_isRaceMapLoaded && levelSystem.IsRaceMap())
                return;

            SetPlayersControlContext(PlayerControlContext.Loading);

            await levelSystem.LoadRaceMap();

            _isRaceMapLoaded = true;
            _isArenaMapLoaded = false;
            _isRaceScenePrepared = false;
           
            await cameraSystem.Controller.ApplyRaceCameraMapConfigAsync(cancellationToken);  
            await UniTask.Delay(TimeSpan.FromSeconds(FlowSettings.RacePreloadDelay), cancellationToken: cancellationToken);
            
            cancellationToken.ThrowIfCancellationRequested();
        }

        private async UniTask EnsureArenaMapLoadedAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_isArenaMapLoaded)
                return;

            await levelSystem.LoadArenaMap();

            _isArenaMapLoaded = true;
            _isRaceMapLoaded = false;
            _isRaceScenePrepared = false;

            cancellationToken.ThrowIfCancellationRequested();
        }

        private async UniTask PreloadArenaMapDuringAugmentSummaryAsync(CancellationToken cancellationToken)
        {
            if (!FlowSettings)
                return;

            cancellationToken.ThrowIfCancellationRequested();
            
            SetPlayersControlContext(PlayerControlContext.Loading);

            await levelSystem.LoadArenaMap();

            _isArenaMapLoaded = true;
            _isRaceMapLoaded = false;
            _isRaceScenePrepared = false;

            cancellationToken.ThrowIfCancellationRequested();
        }

        private async UniTask RunAugmentSummaryPresentationAsync(UniTask canHideTask, Action onRevealComplete, Action requestSkip, CancellationToken cancellationToken)
        {
            if (OnRaceEndedUI == null)
                return;

            foreach (Delegate @delegate in OnRaceEndedUI.GetInvocationList())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var handler = (Func<UniTask, Action, Action, CancellationToken, UniTask>)@delegate;
                await handler.Invoke(canHideTask, onRevealComplete, requestSkip, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        private async UniTask RunAugmentSummaryAndOptionalArenaPreloadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var summarySkipCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var revealCompleteCts = new UniTaskCompletionSource();
            var canHideCts = new UniTaskCompletionSource();

            UniTask presentationTask = RunAugmentSummaryPresentationAsync(canHideCts.Task, () => revealCompleteCts.TrySetResult(), summarySkipCts.Cancel, cancellationToken);

            await revealCompleteCts.Task;
            cancellationToken.ThrowIfCancellationRequested();

            await WaitOrSkip(FlowSettings.AugmentSummaryDuration, summarySkipCts.Token);
            cancellationToken.ThrowIfCancellationRequested();

            await CircleTransition.Instance.CloseAsync(FlowSettings.RaceTransitionDuration);

            canHideCts.TrySetResult();
            await presentationTask;

            await PreloadArenaMapDuringAugmentSummaryAsync(cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            cancellationToken.ThrowIfCancellationRequested();
        }

        private async UniTask WaitOrSkip(float seconds, CancellationToken skipToken)
        {
            try
            {
                await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: skipToken);
            }
            catch (OperationCanceledException)
            { }
        }

        private async UniTask RunRoundEndPresentationAndOptionalRacePreloadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            UniTask presentationTask = RunRoundEndPresentationAsync(cancellationToken);

            await presentationTask;

            cancellationToken.ThrowIfCancellationRequested();
        }

        private void PrepareRaceSceneAfterMapLoaded()
        {
            if (_isRaceScenePrepared)
                return; 
            
            _ghostSystem.ClearAllGhostElements();
            
            EnablePlayerGravity(false);

            audioService.SetPhase(1, AudioService.PhaseType.TWICE);

            UpdateGameState(GameState.AugmentIntro);

            StartRace();

            _isRaceScenePrepared = true;
        }

        private async UniTask EnsureRaceScenePreparedAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await EnsureRaceMapLoadedAsync(cancellationToken);

            PrepareRaceSceneAfterMapLoaded();

            cancellationToken.ThrowIfCancellationRequested();
        }

        private async UniTask PrepareRaceSceneUnderScoreboardCoverAsync(bool shouldPrepare, CancellationToken cancellationToken)
        {
            if (!shouldPrepare)
                return;

            if (!FlowSettings)
                return;

            cancellationToken.ThrowIfCancellationRequested();

            await CircleTransition.Instance.CloseAsync(FlowSettings.RoundTransitionDuration);
            
            SetPlayersControlContext(PlayerControlContext.Loading);
            
            await levelSystem.LoadRaceMap();

            _isRaceMapLoaded = true;
            _isArenaMapLoaded = false;
            _isRaceScenePrepared = false;

            await cameraSystem.Controller.ApplyRaceCameraMapConfigAsync(cancellationToken);  
            
            PrepareRaceSceneAfterMapLoaded();

            cancellationToken.ThrowIfCancellationRequested();
            
            HideScores();
            
            await UniTask.Delay(TimeSpan.FromSeconds(FlowSettings.RacePreloadDelay), cancellationToken: cancellationToken);
            
            await CircleTransition.Instance.OpenAsync(FlowSettings.RoundTransitionDuration);
            
        }

        private void ActivatePlayerAugmentsForRound()=> ForEachCurrentPlayerCharacter(character => character.ActivateRoundAugments());

        private void ResetPlayersForRace() => ForEachCurrentPlayerCharacter(character => character.ResetForRace());
        
        private void ForEachCurrentPlayerCharacter(Action<PlayerCharacter> action)
        {
            if (action == null)
                return;

            var players = lobbyService?.GetPlayers();

            if (players == null)
                return;

            for (var i = 0; i < players.Count; i++)
            {
                var character = players[i].Character;

                if (character)
                    action.Invoke(character);
            }
        }
        
        private int GetCurrentAugmentRaceNumber() => _currentRound.RoundIndex + 1;

        //en sah c'est un peu vilain mais c'est appelé une fois donc en perf osef
        public List<PlayerTeam> GetPlayerTeamsWinnersOrder() => _scoreController.GetOrderWinners(Teams); 
    }
}