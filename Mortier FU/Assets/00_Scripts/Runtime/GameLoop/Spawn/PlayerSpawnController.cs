using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MortierFu
{
    public sealed class PlayerSpawnController
    {
        public delegate Transform PlayerSpawnResolver(PlayerTeam team, PlayerManager player, int racerIndex, Transform fallbackSpawnPoint);

        private readonly IReadOnlyList<PlayerTeam> _teams;
        private readonly LevelSystem _levelSystem;

        private PlayerSpawnResolver _spawnResolver;

        public PlayerSpawnController(IReadOnlyList<PlayerTeam> teams, LevelSystem levelSystem)
        {
            _teams = teams ?? throw new ArgumentNullException(nameof(teams));
            _levelSystem = levelSystem ?? throw new ArgumentNullException(nameof(levelSystem));
        }

        public void SetSpawnResolver(PlayerSpawnResolver spawnResolver)
        {
            _spawnResolver = spawnResolver;
        }

        public void ClearSpawnResolver()
        {
            _spawnResolver = null;
        }

        public void ResetPlayers()
        {
            foreach (var team in _teams)
            {
                foreach (var member in team.Members)
                {
                    member.Character?.Reset();
                }
            }
        }

        public void SetPlayerGravity(bool enabled)
        {
            foreach (var team in _teams)
            {
                foreach (var member in team.Members)
                {
                    if (member.Character == null)
                        continue;

                    if (member.Character.Controller == null)
                        continue;

                    if (member.Character.Controller.rigidbody == null)
                        continue;

                    member.Character.Controller.rigidbody.useGravity = enabled;
                }
            }
        }

        public void SpawnPlayers(int roundIndex)
        {
            bool opposite = roundIndex % 2 == 0;
            int spawnIndex = opposite ? _teams.Sum(team => team.Members.Count) - 1 : 0;

            int racerIndex = 0;

            foreach (var team in _teams.OrderByDescending(team => team.Rank))
            {
                foreach (var member in team.Members)
                {
                    Transform fallbackSpawnPoint = GetSpawnPointFor(team, spawnIndex);
                    int currentRacerIndex = team.Rank == 1 ? -1 : racerIndex;

                    Transform spawnPoint = ResolveSpawnPointFor(team, member, currentRacerIndex, fallbackSpawnPoint);

                    member.SpawnInGame(spawnPoint.position, spawnPoint.rotation);

                    if (team.Rank != 1)
                        racerIndex++;

                    if (opposite)
                        spawnIndex--;
                    else
                        spawnIndex++;
                }
            }
        }

        public void SpawnWinnerTeam(PlayerTeam winnerTeam)
        {
            if (winnerTeam == null)
                return;

            Transform spawnPoint = _levelSystem.GetRoundWinnerSpawnPoint();

            foreach (var member in winnerTeam.Members)
            {
                member.SpawnInGame(spawnPoint.position, spawnPoint.rotation);
            }
        }
        
        public void SpawnGameWinner(PlayerTeam winnerTeam)
        {
            if (winnerTeam == null)
                return;

            Transform spawnPoint = _levelSystem.GetGameWinnerSpawnPoint();

            foreach (var member in winnerTeam.Members)
            {
                member.Character?.Reset();
                member.SpawnInGame(spawnPoint.position, spawnPoint.rotation);

                var rb = member.Character?.Controller?.rigidbody;
                if (rb != null)
                {
                    rb.useGravity = false;
                    rb.constraints = RigidbodyConstraints.FreezeAll;
                }
            }
        }

        private Transform ResolveSpawnPointFor(PlayerTeam team, PlayerManager player, int racerIndex, Transform fallbackSpawnPoint)
        {
            Transform resolvedSpawn = _spawnResolver?.Invoke(team, player, racerIndex, fallbackSpawnPoint);

            return resolvedSpawn ? resolvedSpawn : fallbackSpawnPoint;
        }

        private Transform GetSpawnPointFor(PlayerTeam team, int spawnIndex)
        {
            if (_levelSystem.IsRaceMap())
            {
                return team.Rank == 1
                    ? _levelSystem.GetWinnerSpawnPoint()
                    : _levelSystem.GetSpawnPoint(spawnIndex);
            }

            return _levelSystem.GetSpawnPoint(spawnIndex);
        }
    }
}