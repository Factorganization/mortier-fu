using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using MortierFu.Shared;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace MortierFu
{
    public class LevelSystem : IGameSystem
    {
        private const string k_arenaMapsLabel = "ArenaMaps";
        private const string k_raceMapsLabel = "RaceMaps";

        private const string k_editorOverrideArenaMapAddress = "OverrideArenaMapAddress";
        private const string k_editorOverrideRaceMapAddress = "OverrideRaceMapAddress";
        
        private const string k_winGameMapAddress = "WinGameMap";

        private const int k_maxRaceModeSelectionAttempts = 8;

        private AsyncOperationHandle<SO_LevelSettings> _settingsHandle;

        private List<IResourceLocation> _arenaMapLocations;
        private List<IResourceLocation> _raceMapLocations;

        private readonly Dictionary<string, int> _arenaMapCooldowns = new();
        private readonly Dictionary<string, int> _raceMapCooldowns = new();

        private AsyncOperationHandle<SceneInstance> _mapHandle;

        // Preloaded maps stored by their load key string (PrimaryKey or ToString of the provided key)
        private readonly System.Collections.Generic.Dictionary<string, AsyncOperationHandle<SceneInstance>> _preloadedMaps = new();

        private CameraSystem _cameraSystem;
        private LevelReporter _boundReporter;
        private RaceReporter _boundRaceReporter;
        private Transform _fallbackTransform;

        private GameModeBase _gameModeBase;

        private SO_RaceModeDefinition _lastPlayedRaceModeDefinition;
        private string _currentLoadedMapKey;

        private bool _isFirstRound = true;
        private bool _isSecondRound = false;

        public bool IsInitialized { get; set; }

        public SO_LevelSettings Settings => _settingsHandle.IsValid() ? _settingsHandle.Result : null;

        public CameraMapConfig CurrentCameraMapConfig;

        public RaceReporter CurrentRaceReporter => BoundRaceReporter;

        public UniTask LoadRaceMap() => LoadRaceMapAvoidingPreviousModeAsync();

        public async UniTask LoadArenaMap()
        {
            await LoadMapAsync(_arenaMapLocations, _arenaMapCooldowns, arenaMode: true, editorOverrideKey: k_editorOverrideArenaMapAddress, debugMapTypeName: "arena");
        }
        
        public async UniTask LoadWinGameMap()
        {
            await FinishUnfinishedBusiness();
            await UnloadCurrentMap();

            SetCameraArenaMode(false);

            bool loaded = await LoadSpecificMapAsync(k_winGameMapAddress, "WinGame");

            if (!loaded)
            {
                Logs.LogError("[LevelSystem] Failed to load WinGame map.");
                return;
            }
            
            if (_cameraSystem != null && _cameraSystem.Controller != null)
                _cameraSystem.Controller.gameObject.SetActive(false);
        }

        private async UniTask LoadRaceMapAvoidingPreviousModeAsync()
        {
            if (_isFirstRound || _isSecondRound || HasEditorOverride(k_editorOverrideRaceMapAddress))
            {
                bool loaded = await LoadMapAsync(_raceMapLocations, _raceMapCooldowns, arenaMode: false, editorOverrideKey: k_editorOverrideRaceMapAddress, debugMapTypeName: "race");

                if (loaded)
                    RegisterLoadedRaceModeAsPlayed();

                return;
            }

            var rejectedMapKeys = new HashSet<string>();

            for (int attempt = 0; attempt < k_maxRaceModeSelectionAttempts; attempt++)
            {
                bool loaded = await LoadMapAsync(_raceMapLocations, _raceMapCooldowns, arenaMode: false, editorOverrideKey: k_editorOverrideRaceMapAddress, 
                    debugMapTypeName: "race", excludedMapKeys: rejectedMapKeys);

                if (!loaded)
                    return;

                SO_RaceModeDefinition loadedRaceMode = GetLoadedRaceModeDefinition();

                if (!IsSameAsLastPlayedRaceMode(loadedRaceMode))
                {
                    RegisterLoadedRaceModeAsPlayed();
                    return;
                }

                if (string.IsNullOrEmpty(_currentLoadedMapKey))
                    break;

                rejectedMapKeys.Add(_currentLoadedMapKey);

                Logs.LogWarning($"[LevelSystem] Rejected race map because it uses the same race mode as previous race: {loadedRaceMode.name}.");

                if (_raceMapLocations == null || rejectedMapKeys.Count >= _raceMapLocations.Count)
                    break;
            }

            Logs.LogWarning("[LevelSystem] Could not find a different race mode. Keeping the loaded race map.");

            RegisterLoadedRaceModeAsPlayed();
        }

        private async UniTask<bool> LoadMapAsync(List<IResourceLocation> mapLocations, Dictionary<string, int> mapCooldowns, bool arenaMode
            , string editorOverrideKey, string debugMapTypeName, HashSet<string> excludedMapKeys = null)
        {
            // Set high prio for map loading
            Application.backgroundLoadingPriority = ThreadPriority.High;
            
            // Augmente la taille du buffer d'upload GPU (ex: 64 Mo au lieu de 16 Mo par défaut)
            QualitySettings.asyncUploadBufferSize = 64; 

            // Permet d'allouer plus de millisecondes par frame à l'upload des textures
            QualitySettings.asyncUploadTimeSlice = 16;
            
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            await FinishUnfinishedBusiness();
            await UnloadCurrentMap();

            Logs.Log($"[Perf] Unload pris : {sw.ElapsedMilliseconds} ms");
            
            SetCameraArenaMode(arenaMode);

            bool editorOverrideLoaded = await TryLoadEditorOverrideMapAsync(editorOverrideKey, debugMapTypeName);

            if (editorOverrideLoaded)
                return true;
            
            if (!arenaMode && _isFirstRound)
            {
                bool firstRoundOverrideLoaded = await TryLoadFirstRoundRaceOverrideMapAsync(debugMapTypeName);

                if (firstRoundOverrideLoaded)
                    return true;

                _isFirstRound = false;
            }
            
            if (!arenaMode && _isSecondRound)
            {
                bool secondRoundOverrideLoaded = await TryLoadSecondRoundRaceOverrideMapAsync(debugMapTypeName);
                
                if (secondRoundOverrideLoaded)
                    return true;

                _isSecondRound = false;
            }

            IResourceLocation mapLocation = GetRandomMapLocation(mapLocations, mapCooldowns, debugMapTypeName, excludedMapKeys);

            if (mapLocation is null)
                return false;

            sw.Restart();
            
            bool loaded = await LoadAddressableMapAsync(mapLocation, debugMapTypeName);
            
            Logs.Log($"[Perf] Load pris : {sw.ElapsedMilliseconds} ms");

            if (!loaded)
                return false;

            ApplyLoadedMapData();

            return true;
        }
        
        private void RegisterVisitedMap(string mapKey)
        {
            if (string.IsNullOrEmpty(mapKey)) return;

            SaveService saveService = ServiceManager.Instance.Get<SaveService>();
            if (saveService?.Game?.visitedMaps == null) return;

            
            if (saveService.Game.visitedMaps.Add(mapKey))
            {
                Logs.Log($"[LevelSystem] New map visited registered: {mapKey}");
                saveService.SaveGame().Forget(); 
            }

            if (saveService.Game.visitedMaps.Count >= 24)
            {
                SteamManager.UnlockAchievement("VISIT_EVERY_MAP");
            }
        }

        private async UniTask<bool> TryLoadFirstRoundRaceOverrideMapAsync(string debugMapTypeName)
        {
            string overrideAddress = ResolveFirstRoundRaceOverrideAddress();

            if (string.IsNullOrEmpty(overrideAddress))
                return false;

            IResourceLocation mapLocationOverride = null;

            var mapOverrideHandle = Addressables.LoadResourceLocationsAsync(overrideAddress);

            try
            {
                await mapOverrideHandle;

                if (mapOverrideHandle.Status != AsyncOperationStatus.Succeeded || mapOverrideHandle.Result == null || mapOverrideHandle.Result.Count == 0)
                {
                    Logs.LogWarning(
                        $"[LevelSystem] First round override scene key not found in Addressables: {overrideAddress}"
                    );

                    return false;
                }

                for (int i = 0; i < mapOverrideHandle.Result.Count; i++)
                {
                    IResourceLocation location = mapOverrideHandle.Result[i];

                    if (!IsSceneLocation(location))
                        continue;

                    mapLocationOverride = location;
                    break;
                }

                if (mapLocationOverride is null)
                {
                    Logs.LogError(
                        $"[LevelSystem] First round override key found, but no valid scene location was found for: {overrideAddress}"
                    );

                    return false;
                }

                bool loadedOverride = await LoadAddressableMapAsync(mapLocationOverride, $"{debugMapTypeName} first round override");

                if (!loadedOverride)
                    return false;

                ApplyLoadedMapData();

                Logs.Log($"[LevelSystem] Enforced first round race override: {DescribeSceneKey(mapLocationOverride)}");

                return true;
            }
            finally
            {
                if (mapOverrideHandle.IsValid())
                    Addressables.Release(mapOverrideHandle);
                _isFirstRound = false;
                _isSecondRound = true;
            }
        }
        
        private async UniTask<bool> TryLoadSecondRoundRaceOverrideMapAsync(string debugMapTypeName)
        {
            string overrideAddress = ResolveSecondRoundRaceOverrideAddress();

            if (string.IsNullOrEmpty(overrideAddress))
                return false;

            IResourceLocation mapLocationOverride = null;

            var mapOverrideHandle = Addressables.LoadResourceLocationsAsync(overrideAddress);

            try
            {
                await mapOverrideHandle;

                if (mapOverrideHandle.Status != AsyncOperationStatus.Succeeded || mapOverrideHandle.Result == null || mapOverrideHandle.Result.Count == 0)
                {
                    Logs.LogWarning(
                        $"[LevelSystem] First round override scene key not found in Addressables: {overrideAddress}"
                    );

                    return false;
                }

                for (int i = 0; i < mapOverrideHandle.Result.Count; i++)
                {
                    IResourceLocation location = mapOverrideHandle.Result[i];

                    if (!IsSceneLocation(location))
                        continue;

                    mapLocationOverride = location;
                    break;
                }

                if (mapLocationOverride is null)
                {
                    Logs.LogError(
                        $"[LevelSystem] First round override key found, but no valid scene location was found for: {overrideAddress}"
                    );

                    return false;
                }

                bool loadedOverride = await LoadAddressableMapAsync(mapLocationOverride, $"{debugMapTypeName} first round override");

                if (!loadedOverride)
                    return false;

                ApplyLoadedMapData();

                Logs.Log($"[LevelSystem] Enforced first round race override: {DescribeSceneKey(mapLocationOverride)}");

                return true;
            }
            finally
            {
                if (mapOverrideHandle.IsValid())
                    Addressables.Release(mapOverrideHandle);
                
                _isSecondRound = false;
            }
        }

        private string ResolveFirstRoundRaceOverrideAddress()
        {
            if (_gameModeBase == null || _gameModeBase.Data == null || _gameModeBase.Data.FirstArenaRaceOverride == null)
                return string.Empty;

            return _gameModeBase.Data.FirstArenaRaceOverride.Address;
        }

        //un vieux ctrl c ctrl v mais j'ai ap le temps
        private string ResolveSecondRoundRaceOverrideAddress()
        {
            if (_gameModeBase == null || _gameModeBase.Data == null || _gameModeBase.Data.SecondArenaRaceOverride == null)
                return string.Empty;

            return _gameModeBase.Data.SecondArenaRaceOverride.Address;
        }
        
        private async UniTask<bool> LoadSpecificMapAsync(string address, string debugMapTypeName)
        {
            if (string.IsNullOrEmpty(address))
            {
                Logs.LogError($"[LevelSystem] Cannot load {debugMapTypeName} map: address is empty.");
                return false;
            }

            IResourceLocation mapLocation = null;
            var handle = Addressables.LoadResourceLocationsAsync(address);

            try
            {
                await handle;

                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null || handle.Result.Count == 0)
                {
                    Logs.LogWarning($"[LevelSystem] {debugMapTypeName} scene key not found in Addressables: {address}");
                    return false;
                }

                for (int i = 0; i < handle.Result.Count; i++)
                {
                    if (!IsSceneLocation(handle.Result[i]))
                        continue;

                    mapLocation = handle.Result[i];
                    break;
                }

                if (mapLocation is null)
                {
                    Logs.LogError($"[LevelSystem] {debugMapTypeName} key found, but no valid scene location was found for: {address}");
                    return false;
                }

                bool loaded = await LoadAddressableMapAsync(mapLocation, debugMapTypeName);

                if (!loaded)
                    return false;

                ApplyLoadedMapData();

                Logs.Log($"[LevelSystem] Loaded {debugMapTypeName} scene: {DescribeSceneKey(mapLocation)}");

                return true;
            }
            finally
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }
        }

        private async UniTask<bool> TryLoadEditorOverrideMapAsync(string editorOverrideKey, string debugMapTypeName)
        {
#if UNITY_EDITOR
            string sceneKey = EditorPrefs.GetString(editorOverrideKey, "");

            if (string.IsNullOrEmpty(sceneKey))
                return false;

            IResourceLocation sceneLocation = null;

            var locationsHandle = Addressables.LoadResourceLocationsAsync(sceneKey);

            try
            {
                await locationsHandle;

                if (locationsHandle.Status != AsyncOperationStatus.Succeeded || locationsHandle.Result == null || locationsHandle.Result.Count == 0)
                {
                    Logs.LogWarning($"[LevelSystem] Debug {debugMapTypeName} scene key not found in Addressables: {sceneKey}");
                    return false;
                }

                for (int i = 0; i < locationsHandle.Result.Count; i++)
                {
                    IResourceLocation location = locationsHandle.Result[i];

                    if (!IsSceneLocation(location))
                        continue;

                    sceneLocation = location;
                    break;
                }

                if (sceneLocation is null)
                {
                    Logs.LogError($"[LevelSystem] Debug {debugMapTypeName} key found, but no valid scene location was found for: {sceneKey}");
                    return false;
                }

                bool loaded = await LoadAddressableMapAsync(sceneLocation, $"debug {debugMapTypeName}");

                if (!loaded)
                    return false;

                ApplyLoadedMapData();

                Logs.Log($"[LevelSystem] Enforced debug {debugMapTypeName} scene: {DescribeSceneKey(sceneLocation)}");

                return true;
            }
            finally
            {
                if (locationsHandle.IsValid())
                    Addressables.Release(locationsHandle);
            }
#else
            return false;
#endif
        }

        // Preloads a map into memory/GPU without activating it. Use this during UI/end-of-round to avoid hitches.
        public async UniTask PreloadMapAsync(object sceneKey)
        {
            if (sceneKey is null)
            {
                Logs.LogError("[LevelSystem] Cannot preload map: scene key is null.");
                return;
            }

            object loadKey = sceneKey;
            if (sceneKey is IResourceLocation location)
                loadKey = location.PrimaryKey;

            string loadKeyStr = loadKey.ToString();

            if (_preloadedMaps.TryGetValue(loadKeyStr, out var existingHandle) && existingHandle.IsValid() && existingHandle.Status == AsyncOperationStatus.Succeeded)
            {
                Logs.Log($"[LevelSystem] Map already preloaded: {loadKeyStr}");
                return;
            }

            try
            {
                // Load with activateOnLoad = false so scene stays unloaded/disabled until activation
                var handle = Addressables.LoadSceneAsync(loadKey, LoadSceneMode.Additive, false, 0);
                await handle;

                if (handle.Status != AsyncOperationStatus.Succeeded)
                {
                    Logs.LogError($"[LevelSystem] Failed to preload map with loadKey '{loadKeyStr}': {handle.OperationException?.Message}");
                    if (handle.IsValid())
                        Addressables.Release(handle);
                    return;
                }

                _preloadedMaps[loadKeyStr] = handle;
                Logs.Log($"[LevelSystem] Preloaded map: {handle.Result.Scene.name}");
            }
            catch (Exception e)
            {
                Logs.LogError($"[LevelSystem] Exception while preloading map '{DescribeSceneKey(sceneKey)}' loadKey '{loadKey}': {e.Message}");
            }
        }

        // Loads (or activates if already preloaded) an addressable scene.
        private async UniTask<bool> LoadAddressableMapAsync(object sceneKey, string debugMapTypeName)
        {
            if (sceneKey is null)
            {
                Logs.LogError($"[LevelSystem] Cannot load {debugMapTypeName} map: scene key is null.");
                return false;
            }

            object loadKey = sceneKey;

            if (sceneKey is IResourceLocation location)
            {
                loadKey = location.PrimaryKey;

                Logs.Log($"[LevelSystem] Loading {debugMapTypeName} map from location. {DescribeSceneKey(location)}. LoadKey='{loadKey}'");
            }

            string loadKeyStr = loadKey.ToString();

            try
            {
                // If a preloaded handle exists for this map, activate it instead of loading again.
                if (_preloadedMaps.TryGetValue(loadKeyStr, out var preloadedHandle) && preloadedHandle.IsValid() && preloadedHandle.Status == AsyncOperationStatus.Succeeded)
                {
                    Logs.Log($"[LevelSystem] Activating preloaded map: {loadKeyStr}");
                    _mapHandle = preloadedHandle;

                    // Activate the scene that was loaded with activateOnLoad = false
                    var activateOp = _mapHandle.Result.ActivateAsync();
                }
                else
                {
                    // No preload available: load and activate immediately (priority 0)
                    _mapHandle = Addressables.LoadSceneAsync(loadKey, LoadSceneMode.Additive, true, 0);
                    await _mapHandle;
                }
            }
            catch (Exception e)
            {
                Logs.LogError($"[LevelSystem] Exception while loading {debugMapTypeName} map with key '{DescribeSceneKey(sceneKey)}' and loadKey '{loadKey}': {e.Message}");

                _mapHandle = default;
                return false;
            }

            if (!_mapHandle.IsValid() || _mapHandle.Status != AsyncOperationStatus.Succeeded)
            {
                Logs.LogError($"[LevelSystem] Failed to load {debugMapTypeName} map with key '{DescribeSceneKey(sceneKey)}' and loadKey '{loadKey}': {_mapHandle.OperationException?.Message}");

                _mapHandle = default;
                return false;
            }

            _currentLoadedMapKey = sceneKey is IResourceLocation loadedLocation ? GetMapCooldownKey(loadedLocation) : sceneKey.ToString();
            
            RegisterVisitedMap(_currentLoadedMapKey);

            Logs.Log($"[LevelSystem] Loaded {debugMapTypeName} map: {_mapHandle.Result.Scene.name}");

            return true;
        }

        public async UniTask UnloadCurrentMap()
        {
            await FinishUnfinishedBusiness();

            if (!_mapHandle.IsValid())
            {
                ClearCurrentMapData();
                return;
            }

            if (_mapHandle.Status == AsyncOperationStatus.Succeeded)
            {
                await Addressables.UnloadSceneAsync(_mapHandle);
            }
            else
            {
                Addressables.Release(_mapHandle);
            }

            // If the current loaded map was also a preloaded entry, remove that mapping to keep dict consistent.
            if (!string.IsNullOrEmpty(_currentLoadedMapKey) && _preloadedMaps.TryGetValue(_currentLoadedMapKey, out var preloadedHandle))
            {
                // If the stored preloaded handle is the same as the one we just unloaded, remove the entry.
                if (preloadedHandle.Equals(_mapHandle))
                {
                    _preloadedMaps.Remove(_currentLoadedMapKey);
                }
            }

            _mapHandle = default;

            ClearCurrentMapData();

            Logs.Log("[LevelSystem] Current map unloaded.");
        }

        public bool IsRaceMap() => BoundReporter != null && BoundReporter.IsRaceMap;

        public Transform GetWinnerSpawnPoint()
        {
            if (BoundReporter == null)
                return FallbackTransform;

            return BoundReporter.WinnerSpawnPoint ?? FallbackTransform;
        }

        public Transform GetRoundWinnerSpawnPoint()
        {
            if (BoundReporter == null)
                return FallbackTransform;

            return BoundReporter.RoundWinnerSpawnPoint ?? FallbackTransform;
        }
        
        public Transform GetGameWinnerSpawnPoint()
        {
            if (BoundReporter == null)
                return FallbackTransform;

            return BoundReporter.GameWinnerSpawnPoint ?? FallbackTransform;
        }

        public Transform GetSpawnPoint(int index)
        {
            if (BoundReporter == null)
                return FallbackTransform;

            if (index < 0)
            {
                Logs.LogWarning("[LevelSystem] Trying to get a spawn point with a negative index.");
                return FallbackTransform;
            }

            if (BoundReporter.SpawnPoints != null && BoundReporter.SpawnPoints.Length != 0)
                return BoundReporter.SpawnPoints[index % BoundReporter.SpawnPoints.Length];

            Logs.LogWarning("[LevelSystem] No spawn points available on current LevelReporter.");

            return FallbackTransform;
        }

        public void PopulateAugmentPoints(Vector3[] outPoints)
        {
            if (outPoints == null)
                return;

            if (BoundReporter == null)
            {
                for (int i = 0; i < outPoints.Length; i++)
                {
                    outPoints[i] = Vector3.zero;
                }

                return;
            }

            for (int i = 0; i < outPoints.Length; i++)
            {
                outPoints[i] = BoundReporter.GetAugmentPoint(outPoints.Length, i);
            }
        }

        public Transform GetAugmentPivot()
        {
            return BoundReporter != null ? BoundReporter.AugmentPivot ?? FallbackTransform : FallbackTransform;
        }

        public void BindReporter(LevelReporter reporter)
        {
            if (reporter == null)
            {
                Logs.LogWarning("[LevelSystem] Trying to bind a null LevelReporter.");
                return;
            }

            _boundReporter = reporter;

            Logs.Log("[LevelSystem] Successfully bound a new LevelReporter.");
        }

        public void BindRaceReporter(RaceReporter reporter)
        {
            if (reporter == null)
            {
                Logs.LogWarning("[LevelSystem] Trying to bind a null RaceReporter.");
                return;
            }

            _boundRaceReporter = reporter;

            Logs.Log("[LevelSystem] Successfully bound a new RaceReporter.");
        }

        private void ApplyLoadedMapData()
        {
            LevelReporter reporter = BoundReporter;

            if (reporter == null)
            {
                Logs.LogWarning("[LevelSystem] Loaded map has no LevelReporter.");
                CurrentCameraMapConfig = default;
                return;
            }

            CurrentCameraMapConfig = reporter.CameraConfig;
        }

        private void ClearCurrentMapData()
        {
            _boundReporter = null;
            _boundRaceReporter = null;
            _currentLoadedMapKey = null;
            CurrentCameraMapConfig = default;
        }

        private IResourceLocation GetRandomMapLocation(List<IResourceLocation> mapLocations, Dictionary<string, int> mapCooldowns, string debugMapTypeName, HashSet<string> excludedMapKeys = null)
        {
            if (mapLocations == null || mapLocations.Count == 0)
            {
                Logs.LogError($"[LevelSystem] No {debugMapTypeName} map locations available.");
                return null;
            }

            List<IResourceLocation> selectableMaps = GetMapsExcluding(mapLocations, excludedMapKeys);

            if (selectableMaps.Count == 0)
                selectableMaps = mapLocations;

            int unavailabilityRounds = GetMapUnavailabilityRounds();

            if (unavailabilityRounds <= 0)
                return selectableMaps.RandomElement();

            SynchronizeMapCooldowns(mapLocations, mapCooldowns);

            var availableMaps = new List<IResourceLocation>();

            for (int i = 0; i < selectableMaps.Count; i++)
            {
                IResourceLocation location = selectableMaps[i];
                string key = GetMapCooldownKey(location);

                bool isUnavailable =
                    mapCooldowns.TryGetValue(key, out int remainingRounds) &&
                    remainingRounds > 0;

                if (!isUnavailable)
                    availableMaps.Add(location);
            }

            if (availableMaps.Count == 0)
            {
                Logs.LogWarning($"[LevelSystem] All selectable {debugMapTypeName} maps are currently unavailable. Selecting the map with the lowest remaining cooldown.");

                availableMaps = GetMapsWithLowestCooldown(selectableMaps, mapCooldowns);
            }

            IResourceLocation selectedMap = availableMaps.RandomElement();
            string selectedKey = GetMapCooldownKey(selectedMap);

            TickMapCooldowns(mapCooldowns, selectedKey);

            mapCooldowns[selectedKey] = unavailabilityRounds;

            Logs.Log($"[LevelSystem] Selected {debugMapTypeName} map: {DescribeSceneKey(selectedMap)}. Unavailable for next {unavailabilityRounds} {debugMapTypeName} selections.");

            return selectedMap;
        }

        private int GetMapUnavailabilityRounds()
        {
            if (!_settingsHandle.IsValid() || _settingsHandle.Status != AsyncOperationStatus.Succeeded || Settings == null)
            {
                return 0;
            }

            return Mathf.Max(0, Settings.NbOfUnavailabilityRounds);
        }

        private SO_RaceModeDefinition GetLoadedRaceModeDefinition()
        {
            RaceReporter reporter = CurrentRaceReporter;
            return reporter ? reporter.RaceModeDefinition : null;
        }

        private bool IsSameAsLastPlayedRaceMode(SO_RaceModeDefinition raceModeDefinition)
        {
            return raceModeDefinition && _lastPlayedRaceModeDefinition && raceModeDefinition == _lastPlayedRaceModeDefinition;
        }

        private void RegisterLoadedRaceModeAsPlayed()
        {
            SO_RaceModeDefinition raceModeDefinition = GetLoadedRaceModeDefinition();

            if (!raceModeDefinition)
            {
                Logs.LogWarning("[LevelSystem] Cannot register last played race mode because loaded race has no RaceModeDefinition.");
                return;
            }

            _lastPlayedRaceModeDefinition = raceModeDefinition;

            Logs.Log($"[LevelSystem] Registered last played race mode: {raceModeDefinition.name}");
        }

        private static List<IResourceLocation> GetMapsExcluding(List<IResourceLocation> mapLocations, HashSet<string> excludedMapKeys)
        {
            var result = new List<IResourceLocation>();

            if (mapLocations == null)
                return result;

            for (int i = 0; i < mapLocations.Count; i++)
            {
                IResourceLocation location = mapLocations[i];

                if (location == null)
                    continue;

                string key = GetMapCooldownKey(location);

                if (excludedMapKeys != null && excludedMapKeys.Contains(key))
                    continue;

                result.Add(location);
            }

            return result;
        }

        private static bool HasEditorOverride(string editorOverrideKey)
        {
#if UNITY_EDITOR
            return !string.IsNullOrEmpty(EditorPrefs.GetString(editorOverrideKey, ""));
#else
            return false;
#endif
        }

        private static string GetMapCooldownKey(IResourceLocation location)
        {
            if (location is null)
                return string.Empty;

            if (!string.IsNullOrEmpty(location.PrimaryKey))
                return location.PrimaryKey;

            return !string.IsNullOrEmpty(location.InternalId) ? location.InternalId : location.ToString();
        }

        private static void TickMapCooldowns(Dictionary<string, int> mapCooldowns, string selectedKey)
        {
            if (mapCooldowns == null || mapCooldowns.Count == 0)
                return;

            var keys = new List<string>(mapCooldowns.Keys);

            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];

                if (key == selectedKey)
                    continue;

                mapCooldowns[key]--;

                if (mapCooldowns[key] <= 0)
                    mapCooldowns.Remove(key);
            }
        }

        private static void SynchronizeMapCooldowns(List<IResourceLocation> mapLocations, Dictionary<string, int> mapCooldowns)
        {
            if (mapCooldowns == null || mapCooldowns.Count == 0)
                return;

            var validKeys = new HashSet<string>();

            for (int i = 0; i < mapLocations.Count; i++)
            {
                validKeys.Add(GetMapCooldownKey(mapLocations[i]));
            }

            var cooldownKeys = new List<string>(mapCooldowns.Keys);

            for (int i = 0; i < cooldownKeys.Count; i++)
            {
                string key = cooldownKeys[i];

                if (!validKeys.Contains(key))
                    mapCooldowns.Remove(key);
            }
        }

        private static List<IResourceLocation> GetMapsWithLowestCooldown(List<IResourceLocation> mapLocations, Dictionary<string, int> mapCooldowns)
        {
            var result = new List<IResourceLocation>();

            int lowestCooldown = int.MaxValue;

            for (int i = 0; i < mapLocations.Count; i++)
            {
                IResourceLocation location = mapLocations[i];
                string key = GetMapCooldownKey(location);

                int cooldown = mapCooldowns.TryGetValue(key, out int remainingRounds) ? remainingRounds : 0;

                if (cooldown < lowestCooldown)
                {
                    lowestCooldown = cooldown;
                    result.Clear();
                    result.Add(location);
                }
                else if (cooldown == lowestCooldown)
                {
                    result.Add(location);
                }
            }

            return result;
        }

        private async UniTask FinishUnfinishedBusiness()
        {
            if (!_mapHandle.IsValid() || _mapHandle.IsDone)
                return;

            await _mapHandle;

            if (_mapHandle.Status != AsyncOperationStatus.Succeeded)
                Logs.LogError($"[LevelSystem] Previous map load failed: {_mapHandle.OperationException?.Message}");
        }

        private async UniTask<List<IResourceLocation>> LoadMapsByLabel(string label)
        {
            var handle = Addressables.LoadResourceLocationsAsync(label, typeof(SceneInstance));

            try
            {
                await handle;

                if (handle.Status != AsyncOperationStatus.Succeeded)
                {
                    Logs.LogError($"[LevelSystem] Failed to load map locations for label '{label}': {handle.OperationException?.Message}");
                    return new List<IResourceLocation>();
                }

                var locations = new List<IResourceLocation>();

                for (int i = 0; i < handle.Result.Count; i++)
                {
                    IResourceLocation location = handle.Result[i];

                    if (!IsSceneLocation(location))
                    {
                        Logs.LogWarning($"[LevelSystem] Ignored non-scene location for label '{label}': {DescribeSceneKey(location)}");
                        continue;
                    }

                    locations.Add(location);

                    Logs.Log($"[LevelSystem] Registered scene location for label '{label}': {DescribeSceneKey(location)}");
                }

                if (locations.Count == 0)
                    Logs.LogError($"[LevelSystem] No valid SceneInstance locations found for label '{label}'. Check Addressables setup.");

                return locations;
            }
            finally
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }
        }

        private void SetCameraArenaMode(bool arenaMode)
        {
            if (_cameraSystem == null || _cameraSystem.Controller == null)
            {
                Logs.LogWarning("[LevelSystem] CameraSystem or camera controller is missing.");
                return;
            }

            _cameraSystem.Controller.SetArenaMode(arenaMode);
        }

        private RaceReporter BoundRaceReporter
        {
            get
            {
                if (_boundRaceReporter)
                    return _boundRaceReporter;

                RaceReporter reporter = Object.FindAnyObjectByType<RaceReporter>(FindObjectsInactive.Exclude);

                if (!reporter)
                    return null;

                Logs.LogWarning("[LevelSystem] Found a RaceReporter in the scene. Falling back to it.");

                _boundRaceReporter = reporter;
                return _boundRaceReporter;
            }
        }

        private LevelReporter BoundReporter
        {
            get
            {
                if (_boundReporter != null)
                    return _boundReporter;

                Logs.LogWarning("[LevelSystem] No LevelReporter bound to the LevelSystem.");

                LevelReporter reporter = Object.FindAnyObjectByType<LevelReporter>(FindObjectsInactive.Exclude);

                if (reporter == null)
                    return null;

                Logs.LogWarning("[LevelSystem] Found a LevelReporter in the scene. Falling back to it.");

                _boundReporter = reporter;
                return _boundReporter;
            }
        }

        public Transform FallbackTransform
        {
            get
            {
                if (_fallbackTransform == null)
                    _fallbackTransform = new GameObject("FallbackSpawnPoint").transform;

                return _fallbackTransform;
            }
        }

        public async UniTask OnInitialize()
        {
            _cameraSystem = SystemManager.Instance.Get<CameraSystem>();

            if (_cameraSystem == null)
                Logs.LogWarning("[LevelSystem] No CameraSystem found.");

            _settingsHandle = await SystemManager.Config.LevelSettings.LazyLoadAssetRef();

            _arenaMapLocations = await LoadMapsByLabel(k_arenaMapsLabel);
            _raceMapLocations = await LoadMapsByLabel(k_raceMapsLabel);

            _gameModeBase = GameService.CurrentGameMode as GameModeBase;

            _isFirstRound = true;
        }

        private static bool IsSceneLocation(IResourceLocation location)
        {
            return location is not null && location.ResourceType == typeof(SceneInstance);
        }

        private static string DescribeSceneKey(object sceneKey)
        {
            if (sceneKey is null)
                return "<null>";

            if (sceneKey is IResourceLocation location)
                return $"PrimaryKey='{location.PrimaryKey}', InternalId='{location.InternalId}', ResourceType='{location.ResourceType}'";

            return sceneKey.ToString();
        }

        public void Dispose()
        {
            if (_settingsHandle.IsValid())
                Addressables.Release(_settingsHandle);

            _arenaMapLocations?.Clear();
            _raceMapLocations?.Clear();

            _arenaMapCooldowns.Clear();
            _raceMapCooldowns.Clear();

            ClearCurrentMapData();

            if (_fallbackTransform == null)
                return;

            Object.Destroy(_fallbackTransform.gameObject);
            _fallbackTransform = null;
        }
    }
}