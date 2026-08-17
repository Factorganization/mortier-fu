using MortierFu.Shared;
using UnityEngine;

namespace MortierFu
{
    public sealed class PlayerCustomizationVisual : MonoBehaviour
    {
        [Header("Skins / Hats")]
        [SerializeField] private SkinnedMeshRenderer _customSkinMeshRenderer;
        [SerializeField] private Mesh[] _availableSkins;
        [SerializeField] private GameObject _crownInstance;
        [SerializeField] private Material _customizationMaterial;
        
        [Header("Face")]
        [SerializeField] private SkinnedMeshRenderer _bodySkinnedMeshRenderer;
        [SerializeField] private string _columnPropertyName = "_Column";
        [SerializeField] private string _rowPropertyName = "_Row";

        private Material _faceMaterialInstance;
        private Material _customMaterialInstance;

        public  Material CustomsMaterial => _customizationMaterial;
        public Transform HeadAnchor => _crownInstance != null ? _crownInstance.transform : null;

        private GameModeBase _gameModeBase;
        
        private static readonly int ShaderPropColor = Shader.PropertyToID("_PlayerColor");

        private void Awake()
        {
            UpdateVisualsAfterRound(false);
            _gameModeBase = GameService.CurrentGameMode as GameModeBase;
            EnsureInitialized();
        }

        private void OnEnable()
        {
            _gameModeBase ??= GameService.CurrentGameMode as GameModeBase;
            if (_gameModeBase == null)
            {
                Logs.LogWarning("No GameModeBase found; skipping visuals reset subscription.", this);
                return;
            }

            _gameModeBase.OnGameStarted += ResetVisualAfterRound;
        }

        private void OnDisable()
        {
            if (_gameModeBase == null)
                return;

            _gameModeBase.OnGameStarted -= ResetVisualAfterRound;
        }

        private void OnDestroy()
        {
            if (!_faceMaterialInstance) return;
            
            Destroy(_faceMaterialInstance);
            _faceMaterialInstance = null;
        }

        public void Apply(PlayerCustomizationData customization)
        {
            if (customization is null)
                return;

            Apply(customization.SkinIndex, customization.FaceColumn, customization.FaceRow);
        }

        private void ApplySkin(int skinIndex)
        {
            if (!_customSkinMeshRenderer || _availableSkins.Length == 0)
            {
                return;
            }

            if (skinIndex < 0 || skinIndex >= _availableSkins.Length)
            {
                Logs.LogWarning($"[PlayerCustomizationVisual] Skin index {skinIndex} is out of range.", this);
                return;
            }
            
            _customSkinMeshRenderer.sharedMesh = _availableSkins[skinIndex];
        }

        private void ApplyFace(int column, int row)
        {
            if (!_bodySkinnedMeshRenderer)
            {
                return;
            }

            _faceMaterialInstance ??= _bodySkinnedMeshRenderer.materials[2];

            if (!_faceMaterialInstance)
            {
                Logs.LogWarning("[PlayerCustomizationVisual] Face material is null.", this);
                return;
            }

            if (_faceMaterialInstance.HasProperty(_columnPropertyName))
                _faceMaterialInstance.SetFloat(_columnPropertyName, column);
            else
                Logs.LogWarning($"[PlayerCustomizationVisual] Material has no property '{_columnPropertyName}'.", this);

            if (_faceMaterialInstance.HasProperty(_rowPropertyName))
                _faceMaterialInstance.SetFloat(_rowPropertyName, row);
            else
                Logs.LogWarning($"[PlayerCustomizationVisual] Material has no property '{_rowPropertyName}'.", this);
        }

        public void SetCustom(int index)
        {
            EnsureInitialized();
            _customMaterialInstance.SetInt(ShaderPropColor, index);
        }

        public void Apply(int skinIndex, int faceColumn, int faceRow)
        {
            EnsureInitialized();
            ApplySkin(skinIndex);
            ApplyFace(faceColumn, faceRow);
        }
        
        private void EnsureInitialized()
        {
            if (_customMaterialInstance)
                return;

            _customMaterialInstance = new Material(_customizationMaterial);
            var mats = _customSkinMeshRenderer.materials;
            mats[0] = _customMaterialInstance;
            _customSkinMeshRenderer.materials = mats;
        }

        public void ResetVisualAfterRound() => UpdateVisualsAfterRound(false);
        
        public void UpdateVisualsAfterRound(bool isWinningGame)
        {
            // In the future, add VFX or other win feedback here.
            _crownInstance?.SetActive(isWinningGame);
        }
    }
}