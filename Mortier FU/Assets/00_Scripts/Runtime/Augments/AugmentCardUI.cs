using System;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.UI;
using UnityEngine;
using TMPro;

namespace MortierFu
{
    public class AugmentCardUI : AugmentVisual
    {
        [SerializeField] private SO_RaritySpritesFactory _raritySpritesFactory;

        //[SerializeField] private Image _titleImageBg;
        [Header("UI References"),SerializeField] private TextMeshProUGUI _nameTxt;
        [SerializeField] private RawImage _titleRarityFilter;
        [SerializeField] private TextMeshProUGUI _descTxt;
        [SerializeField] private Image _augmentBorder;
        [SerializeField] private Image _augmentIcon;
        [SerializeField] private Image _augmentCard;
        [SerializeField] private Image _augmentBack;
        [SerializeField] private CanvasGroup _canvasGroup;
        
        [Header("References")]
        [SerializeField] private RarityData[] _rarityData;
        [SerializeField] private Transform anchor;
        [SerializeField] private RectTransform _infoRoot;
        [SerializeField] private GameObject _explosionCardVFXPrefab;

        [SerializeField] private float _showExplosionDelay = 0.1f;
        [SerializeField] private float _hideInfoDelay = 0.2f;

        [Header("SymbolDescription")] 
        [SerializeField] private int _symboleSize;
        
        [Header("Values")] 
        [SerializeField] private Vector3 _sizeIcon = new (.3f, .3f,.3f);
        [SerializeField] private Vector3 _vfxCardLocalPosition = new (-0.03f, 0.85f, 0f);
        
        private FaceCamera _faceCamera;
        private GameObject _vfxInstance;
        protected GameObject _vfxCard;
        private Quaternion _initialRotation;
        private Vector3 _initialScale;
        private Vector2 _initialInfoPos;
        private float _initialCanvasAlpha;

        private bool _initialized;

        private CancellationTokenSource _cts;
        
        private ShakeService _shakeService;

        private StringBuilder _sb = new ();

        public void Initialize()
        {
            _faceCamera = GetComponent<FaceCamera>();

            if (_initialized)
                return;

            _initialRotation = transform.localRotation;
            _initialScale = transform.localScale;
            _initialInfoPos = _infoRoot.anchoredPosition;
            _initialCanvasAlpha = _canvasGroup.alpha;
        }

        private void Start() => _shakeService = ServiceManager.Instance.Get<ShakeService>();

        private void OnDisable() => _cts?.Cancel();

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }

        public void SetAugmentVisual(SO_Augment augment)
        {
            if (augment == null)
            {
                throw new ArgumentNullException(nameof(augment));
            }

            var data = GetRarityData(augment.Rarity);
            
            _augmentIcon.gameObject.SetActive(false);
            _augmentBack.gameObject.SetActive(false);
            _explosionCardVFXPrefab.SetActive(false);

            _nameTxt.SetText(augment.Name.ToUpper());
            _titleRarityFilter.texture = _raritySpritesFactory.GetTitleRarityFilter(augment.Rarity);
            _nameTxt.color = data.NameColor;
            
            _sb.Clear();
            _sb.Append(augment.ConditionText);
            
            if (augment.ConditionText != "")
                _sb.AppendLine();
            
            foreach (AugmentDescription desc in augment.Description)
            {
                int lineTextSize = desc.TextSize > 0 ? desc.TextSize : (int)augment.DescFontSize;

                _sb.Append($"<size={lineTextSize}>");
                _sb.Append(AugmentVariableDescription.Get(desc.variable));
                _sb.Append(GetValueSuffix(desc.value, desc.SymbolSize));
                _sb.Append("</size>");
                _sb.AppendLine();
            }


            _descTxt.color = data.DescriptionColor;
            _descTxt.SetText(_sb.ToString());
            
            _augmentBorder.sprite = _raritySpritesFactory.GetRarityBorderSpriteFromRarity(augment.Rarity);
            _augmentBack.sprite = _raritySpritesFactory.GetRarityCardBgSpriteFromRarity(augment.Rarity);

            GameObject rarityVfx = _raritySpritesFactory.GetRarityVfxFromRarity(augment.Rarity);
            if (rarityVfx != null)
            {
                _vfxCard = Instantiate(rarityVfx, Vector3.zero, Quaternion.identity, transform);
                
                _vfxCard.transform.localPosition = _vfxCardLocalPosition;
            }
            
            _augmentIcon.sprite = augment.SmallSprite;
            _augmentCard.sprite = augment.CardSprite;
        }

        private string GetValueSuffix(E_AugmentValue value, int symbolSize)
        {

            string symbol = value switch
            {
                E_AugmentValue.Empty => "",
                E_AugmentValue.MinusThree => " <sprite index=1> <sprite index=1> <sprite index=1>",
                E_AugmentValue.MinusTwo => " <sprite index=1> <sprite index=1>",
                E_AugmentValue.MinusOne => " <sprite index=1>",
                E_AugmentValue.MinusOneGreen => " <sprite index=2>",
                E_AugmentValue.PlusOne => " <sprite index=0>",
                E_AugmentValue.PlusTwo => " <sprite index=0> <sprite index=0>",
                E_AugmentValue.PlusThree => " <sprite index=0> <sprite index=0> <sprite index=0> ",
                E_AugmentValue.OneNumber => " <sprite index=0><sprite index=4>",
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, null)
            };
            
            return symbol == "" ? "" : $"<size={symbolSize}><b>{symbol}</b></size>";
        }

        public void SetIconCardVisual(SO_Augment augment)
        {
            _vfxInstance = SetAugmentVisualIcon(augment, Vector3.zero, anchor, _sizeIcon, true);

            var children = _vfxInstance.GetComponentsInChildren<Transform>(true);
            foreach (var child in children)
            {
                child.gameObject.layer = 3;
            }
        }

        private RarityData GetRarityData(E_AugmentRarity augmentRarity) =>
            Array.Find(_rarityData, data => data.Rarity == augmentRarity);

        public void SetFaceCameraEnabled(bool enable) => _faceCamera.enabled = enable;

        private async UniTask PlayBoonDropTransition(AugmentPickup pickupVFX)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            
            SetFaceCameraEnabled(false);

            _augmentIcon.transform.localScale = Vector3.one;

            await UniTask.Delay(TimeSpan.FromSeconds(_showExplosionDelay), cancellationToken: token); 
            
            _shakeService.ShakeControllers(ShakeService.ShakeType.MID);
            _explosionCardVFXPrefab.SetActive(true);
            
            AudioService.PlayOneShot(AudioService.FMODEvents.SFX_Augment_Pop, _explosionCardVFXPrefab.transform.position);

            await UniTask.Delay(TimeSpan.FromSeconds(_hideInfoDelay), cancellationToken: token);
            DisableObjects();
            pickupVFX.gameObject.SetActive(true);
        }

        public void DisableObjectsOnFlip()
        {
            //_titleImageBg.gameObject.SetActive(false);
            _nameTxt.gameObject.SetActive(false);
            _descTxt.gameObject.SetActive(false);
            _vfxInstance.SetActive(false);
            
            if(_vfxCard != null)
                _vfxCard.SetActive(false);
            
            _augmentBack.gameObject.SetActive(true);
            _augmentIcon.gameObject.SetActive(true);
        }

        private void DisableObjects()
        {
            //_titleImageBg.gameObject.SetActive(false);
            _nameTxt.gameObject.SetActive(false);
            _descTxt.gameObject.SetActive(false);

            _augmentBack.gameObject.SetActive(false);
            _augmentIcon.gameObject.SetActive(false);
            _augmentCard.gameObject.SetActive(false);
            _augmentBorder.gameObject.SetActive(false);
        }

        public async UniTask PlayRevealSequence(AugmentPickup pickupVFX)
        {
            await PlayBoonDropTransition(pickupVFX);
        }

        public void Show() => gameObject.SetActive(true);

        public void Hide()
        {
            _cts?.Cancel();
            gameObject.SetActive(false);
        }
        
        public void ResetUI()
        {
            transform.localRotation = _initialRotation;
            transform.localScale = _initialScale;

            _canvasGroup.alpha = _initialCanvasAlpha;

            _infoRoot.anchoredPosition = _initialInfoPos;

            //_titleImageBg.gameObject.SetActive(true);
            _nameTxt.gameObject.SetActive(true);
            _descTxt.gameObject.SetActive(true);
            _augmentCard.gameObject.SetActive(true);
            _augmentBorder.gameObject.SetActive(true);

            _augmentBack.gameObject.SetActive(false);
            _augmentIcon.gameObject.SetActive(false);

            SetFaceCameraEnabled(true);
        }

        public void Reset() => _explosionCardVFXPrefab.SetActive(false);

        [Serializable]
        private struct RarityData
        {
            public E_AugmentRarity Rarity;
            public Color NameColor;
            public Color DescriptionColor; //stoian
        }
    }
}