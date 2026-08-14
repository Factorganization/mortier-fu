using Cysharp.Threading.Tasks;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using MortierFu.Shared;

namespace MortierFu.Analytics
{
    public partial class AnalyticsSystem : IGameSystem
    {
        private AnalyticsData _gameData;
        private int _currentRoundIndex = 0;
        private Dictionary<string, AnalyticsPlayerData> _currentRoundPlayers;
        private Dictionary<string, SO_Augment> _augmentPerPlayer = new ();
        private System.DateTime _gameStartTime;
        private Dictionary<string, int> _lastKnownScorePerPlayer = new();

        public bool IsInitialized { get; set; }
        public string GameId => _gameData?.gameId;

        public async UniTask OnInitialize()
        {
            RegisterEvents();
            RegisterAugmentEvents();
            CreateNewGameData();
            await InitializeAugmentTracking();
            IsInitialized = true;
        }

        private void CreateNewGameData()
        {
            _gameStartTime = System.DateTime.UtcNow;
            
            _lastKnownScorePerPlayer.Clear();
            _currentRoundIndex = 0;
            
            _gameData = new AnalyticsData()
            {
                gameId = System.Guid.NewGuid().ToString(),
                date = System.DateTime.UtcNow.ToString("yyyy-MM-ddT HH:mm:ss"),
                numberOfPlayers = ServiceManager.Instance.Get<LobbyService>().CurrentPlayerCount,
                gameVersion = Application.version,
                scoreToWin = (GameService.CurrentGameMode as GameModeBase)?.ScoreToWin ?? 0,
                officialGameVersion = !Application.isEditor ? "b.2.0.1" : "Editor",
                rounds = new AnalyticsRoundData[1000],
                winner = "",
                roundsPlayed = 0,
            };

            if (_augmentStats != null)
            {
                foreach (var entry in _augmentStats.Values)
                {
                    entry.timesShown = 0;
                    entry.timesPicked = 0;
                    entry.timesPickedByWinner = 0;
                    entry.winnerHadIt = false;
                }
            }

            StartNewRound();
        }
        
        private System.DateTime _roundStartTime;

        private void StartNewRound()
        {
            _roundStartTime = System.DateTime.UtcNow;
            _currentRoundPlayers = new Dictionary<string, AnalyticsPlayerData>();

            var currentRound = new AnalyticsRoundData()
            {
                roundNumber = _currentRoundIndex + 1,
                roundWinner = "",
                players = new List<AnalyticsPlayerData>()
            };

            _gameData.rounds[_currentRoundIndex] = currentRound;

            InitializePlayersForRound();
        }
        
        private void InitializePlayersForRound()
        {
            var lobbyService = ServiceManager.Instance.Get<LobbyService>();
            var players = lobbyService.GetPlayers();

            foreach (var player in players)
            {
                string playerId = GetPlayerIdFromCharacter(player);
                _augmentPerPlayer.TryGetValue(playerId, out var currentAugment);
                var playerData = new AnalyticsPlayerData()
                {
                    playerId = playerId,
                    rank = 0,
                    score = _lastKnownScorePerPlayer.TryGetValue(playerId, out var lastScore) ? lastScore : 0,
                    kills = 0,
                    selectedAugment = null,
                    selectedAugmentName = "-",
                    damageDealt = 0f,
                    damageTaken = 0f,
                    shotsFired = 0,
                    shotsHit = 0,
                    dashesPerformed = 0,
                    bumpsMade = 0,
                    stunsPerformed = 0,
                    stunsUnderwented = 0,
                    killerId = -1,
                    deathCause = E_DeathCause.Unknown
                };

                _currentRoundPlayers[playerId] = playerData;
            }
        }
        private string ShortenDeathCause(E_DeathCause cause)
        {
            return cause switch
            {
                E_DeathCause.BombshellExplosion => "Explosion",
                E_DeathCause.FallAfterExplosion => "FallExplosion",
                _ => cause.ToString()
            };
        }
        
        private void OnTriggerEndRound(TriggerEndRound endRound)
        {
            if (!IsInCombatPhase()) return;
            
            FinalizeCurrentRound(endRound.WinningTeam);

            _currentRoundIndex++;
            _gameData.roundsPlayed++;

            StartNewRound();
        }

        private void FinalizeCurrentRound(PlayerTeam winningTeam)
        {
            var currentRound = _gameData.rounds[_currentRoundIndex];

            currentRound.players = _currentRoundPlayers.Values.ToList();
            currentRound.roundDurationSeconds = (int)(System.DateTime.UtcNow - _roundStartTime).TotalSeconds; 
            
            if (winningTeam != null && winningTeam.Members.Count > 0)
                currentRound.roundWinner = GetPlayerIdFromCharacter(winningTeam.Members[0]);

            foreach (var player in currentRound.players)
            {
                player.deathCauseName = player.playerId == currentRound.roundWinner ? "Win" : ShortenDeathCause(player.deathCause);
            }

            AssignRanks(currentRound.players);
        }

        private void OnGameEndedHandler(int winnerPlayerIndex)
        {
            var duration = System.DateTime.UtcNow - _gameStartTime;
            _gameData.durationSeconds = (int)duration.TotalSeconds;

            FinalizeGame();

            SendGameOverviewToGoogleSheets().Forget();
            SendAugmentStatsToGoogleSheets().Forget();
            SendPlayerStatsToGoogleSheets().Forget();
            SendAllRoundsBatchToGoogleSheets().Forget();
            //SendAllRoundsOverviewToGoogleSheets().Forget();
        }

        private void FinalizeGame()
        {
            GameService.CurrentGameMode.IsGameOver(out var playerWins);

            if (playerWins != null)
            {
                string winnerId = GetPlayerIdFromCharacter(playerWins.Members[0]);
                _gameData.winner = winnerId;
                
                FinalizeAugmentAnalytics(winnerId);
            }

            if (_augmentStats != null) _gameData.augmentStats = _augmentStats.Values.ToArray();

            AggregateFinalStats();
            ExportToExcel();
        }

        private void AggregateFinalStats()
        {
            var statsByPlayer = new Dictionary<string, AnalyticsFinalPlayerStats>();

            _gameData.totalBombshellKills = 0;
            _gameData.totalSuicides = 0;
            _gameData.totalPushKills = 0;
            _gameData.totalSelfFalls = 0;

            for (int i = 0; i < _gameData.roundsPlayed; i++)
            {
                var round = _gameData.rounds[i];
                if (round?.players == null) continue;

                foreach (var player in round.players)
                {
                    if (!statsByPlayer.TryGetValue(player.playerId, out var stats))
                    {
                        stats = new AnalyticsFinalPlayerStats { playerId = player.playerId };
                        statsByPlayer[player.playerId] = stats;
                    }

                    stats.kills += player.kills;
                    stats.dashesPerformed += player.dashesPerformed;
                    stats.bumpsMade += player.bumpsMade;
                    stats.shotsFired += player.shotsFired;
                    stats.shotsHit += player.shotsHit;
                    stats.damageDealt += player.damageDealt;
                    stats.damageTaken += player.damageTaken;
                    stats.stunsPerformed += player.stunsPerformed;
                    stats.stunsUnderwented += player.stunsUnderwented;

                    bool killedBySomeoneElse = player.killerId != -1
                        && player.killerId.ToString() != player.playerId;

                    if (player.deathCause == E_DeathCause.BombshellExplosion || player.deathCause == E_DeathCause.FallAfterExplosion)
                    {
                        if (killedBySomeoneElse) _gameData.totalBombshellKills++;
                        else _gameData.totalSuicides++;
                    }
                    else if (player.deathCause == E_DeathCause.Fall || player.deathCause == E_DeathCause.VehicleCrash)
                    {
                        if (killedBySomeoneElse) _gameData.totalPushKills++;
                        else _gameData.totalSelfFalls++;
                    }
                }
            }

            var gameMode = GameService.CurrentGameMode as GameModeBase;
            if (gameMode != null)
            {
                foreach (var team in gameMode.Teams)
                {
                    int teamScore = team.Score;

                    foreach (var member in team.Members)
                    {
                        if (member?.Character == null) continue;

                        string playerId = GetPlayerIdFromCharacter(member);

                        if (!statsByPlayer.TryGetValue(playerId, out var stats))
                        {
                            stats = new AnalyticsFinalPlayerStats { playerId = playerId };
                            statsByPlayer[playerId] = stats;
                        }

                        stats.score = teamScore;
                        
                        var characterStats = member.Character.Stats;
                        if (characterStats != null)
                        {
                            stats.playerStats = new AnalyticsPlayerStats
                            {
                                maxHealth = characterStats.MaxHealth.Value,
                                moveSpeed = characterStats.MoveSpeed.Value,
                                bombshellDamage = characterStats.BombshellDamage.Value,
                                bombshellImpactRadius = characterStats.BombshellImpactRadius.Value,
                                bombshellSpeed = characterStats.BombshellSpeed.Value,
                                fireRate = characterStats.FireRate.Value,
                                shotRange = characterStats.ShotRange.Value,
                                dashCharges = characterStats.DashCharges.Value,
                                dashCooldown = characterStats.DashCooldown.Value,
                                dashDistance = characterStats.DashForce.Value,
                                strikePushForce = characterStats.StrikePushForce.Value,
                                strikeStunDuration = characterStats.StrikeKnockbackDuration.Value
                            };
                        }
                    }
                }
            }
            else
            {
                Logs.LogWarning("[AnalyticsSystem] Impossible de caster CurrentGameMode en GameModeBase.");
            }

            _gameData.finalPlayerStats = statsByPlayer.Values
                .OrderBy(s => s.playerId)
                .Take(4)
                .ToArray();
        }

        private bool ShouldSkipAnalyticsInEditor()
        {
            #if UNITY_EDITOR
            return Application.isEditor && !UnityEditor.EditorPrefs.GetBool("AnalyticsInEditor", false);
            #else
            return false;
            #endif
        }

        private void ExportToExcel()
        {
            if (ShouldSkipAnalyticsInEditor())
            {
                Logs.Log("Analytics export skipped in editor.");
                return;
            }

            Logs.Log($"Exporting game data: {_gameData.gameId}");

            string json = JsonUtility.ToJson(_gameData, true);
            Logs.Log(json);

            string path = System.IO.Path.Combine(Application.persistentDataPath,
                $"GameData_{_gameData.gameId}.json");
            System.IO.File.WriteAllText(path, json);
            Logs.Log($"Data saved to: {path}");
        }

        public void Dispose()
        {
            DeregisterEvents();
            DeregisterAugmentEvents();

            if (_gameData != null && _gameData.roundsPlayed > 0)
            {
                ExportToExcel();
            }
        }
    }
}