using System;
using Cysharp.Threading.Tasks;
using MortierFu.Shared;
using NaughtyAttributes;
using UnityEngine;

namespace MortierFu
{
    public class LevelReporter : MonoBehaviour
    {
        [Header("Level Design")] public Transform[] SpawnPoints;
        public Transform RoundWinnerSpawnPoint;
        public Transform GameWinnerSpawnPoint;

        [SerializeField] private CameraMapConfig _cameraMapConfig;

        public CameraMapConfig CameraConfig => _cameraMapConfig;
        
        private AudioService audioService => ServiceManager.Instance.Get<AudioService>();
        
        [Button]
        private void AutoPopulate()
        {
            SpawnPoints = null;

            var spawnPoints = transform.Find("Spawn Points");
            if (spawnPoints != null)
            {
                if (spawnPoints.childCount > 0)
                {
                    SpawnPoints = new Transform[spawnPoints.childCount];
                    for (int i = 0; i < spawnPoints.childCount; i++)
                    {
                        SpawnPoints[i] = spawnPoints.GetChild(i);
                    }

                    Logs.Log("Successfully populated the spawn points.");
                }
                else
                {
                    Logs.LogWarning("Found no spawn points in the parent holder.");
                }
            }
            else
            {
                Logs.LogWarning("Couldn't find Spawn Points in the child hierarchy.");
            }
        }

        [HorizontalLine] public bool IsRaceMap = false;
        [SerializeField] private bool Night, Rain;
        
        [ShowIf("IsRaceMap")] public Transform WinnerSpawnPoint;
        [ShowIf("IsRaceMap")] public Transform AugmentPivot;
        [ShowIf("IsRaceMap")] public float Radius = 4f;

        void Awake()
        {
            var levelSystem = SystemManager.Instance.Get<LevelSystem>();
            if (levelSystem == null)
            {
                Logs.LogError("[LevelReporter]: Couldn't fetch level system from the SystemManager !");
                return;
            }
            
            // When this LD is loaded, should bound itself to the LevelSystem

            levelSystem.BindReporter(this);
            
            audioService.StartAmbience(Night, Rain).Forget();
        }

#if UNITY_EDITOR

        [HorizontalLine] [Header("Debugging")] [SerializeField]
        private bool _enableDebug = true;

        [SerializeField] private Color _winnerSpawnColor = Color.yellow;
        [SerializeField] private Color _raceWinnerSpawnColor = Color.mediumPurple;
        [SerializeField] private Color _gameWinnerSpawnColor = Color.cyan;
        [SerializeField] private Color _spawnPointColor = Color.dodgerBlue;
        [SerializeField] private Color _augmentPointColor = Color.softRed;
        [SerializeField] private float _widgetSize = 0.1f;

        void OnDrawGizmos()
        {
            if (_enableDebug == false) return;

            if (SpawnPoints != null && SpawnPoints.Length > 0)
            {
                Gizmos.color = _spawnPointColor;
                for (var i = 0; i < SpawnPoints.Length; i++)
                {
                    var spawnPoint = SpawnPoints[i];
                    if (spawnPoint == null) continue;
                    Gizmos.DrawSphere(spawnPoint.position, _widgetSize);
                }
            }

            if (WinnerSpawnPoint)
            {
                Gizmos.color = _winnerSpawnColor;
                Gizmos.DrawSphere(WinnerSpawnPoint.position, _widgetSize);
            }

            if (RoundWinnerSpawnPoint)
            {
                Gizmos.color = _raceWinnerSpawnColor;
                Gizmos.DrawSphere(RoundWinnerSpawnPoint.position, _widgetSize);
            }
            
            if (GameWinnerSpawnPoint) 
            {
                Gizmos.color = _gameWinnerSpawnColor;
                Gizmos.DrawSphere(GameWinnerSpawnPoint.position, _widgetSize);
            }

            if (IsRaceMap)
            {
                if (AugmentPivot != null)
                {
                    Gizmos.color = _augmentPointColor;
                    for (int i = 0; i < 5; i++)
                    {
                        var pos1 = GetAugmentPoint(5, i);
                        Gizmos.DrawSphere(pos1, _widgetSize);
                        Gizmos.DrawLine(AugmentPivot.position, pos1);

                        var pos2 = GetAugmentPoint(5, (i + 1) % 5);
                        Gizmos.DrawLine(pos1, pos2);
                    }
                }
            }
        }
#endif

        public Vector3 GetAugmentPoint(int augmentCount, int index)
        {
            if (augmentCount <= 0)
                throw new ArgumentException("augmentCount must be greater than zero.");
            if (AugmentPivot == null)
                throw new InvalidOperationException("AugmentPivot is not assigned.");

            var angle = index * (2 * Mathf.PI / augmentCount);
            var x = AugmentPivot.position.x + Mathf.Cos(angle) * Radius;
            var z = AugmentPivot.position.z + Mathf.Sin(angle) * Radius;
            return new Vector3(x, AugmentPivot.position.y, z);
        }
    }
    
    [Serializable]
    public struct CameraMapConfig
    {
        [Header("Camera Placement")] public Vector3 PositionForRace;
        
        public Vector3 PositionForMap;
        
        [Header("Zoom & Bounds")] public float OrthoSize;

        //public bool OverrideDefaultSettings;
    }
}