using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using PrimeTween;
using MortierFu.Shared;

namespace MortierFu
{
    public class CircleTransition : MonoBehaviour
    {
        [SerializeField] private Image _image;

        private static readonly Vector2 k_defaultCenter = new Vector2(0.5f, 0.5f);

        private Material _material;

        public static CircleTransition Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Logs.LogError("[CircleOpen] Multiple instances detected! Destroying duplicate.", this);
                Destroy(gameObject);
                return;
            }

            Instance = this;

            _material = _image.material;
            
            _image.gameObject.SetActive(false);
            _material.SetFloat("_Progress", 0);
            _material.SetVector("_Center", k_defaultCenter);
        }

        public async UniTask OpenAsync(float duration, Vector2? origin = null)
        {
            _material.SetFloat("_Progress", 0);
            _material.SetVector("_Center", origin ?? k_defaultCenter);
            _image.gameObject.SetActive(true);
    
            await Tween.MaterialProperty(_material, Shader.PropertyToID("_Progress"), 1f, duration, Ease.InOutQuad);
            
            _image.gameObject.SetActive(false);
        }

        public async UniTask CloseAsync(float duration, Vector2? origin = null)
        {
            _material.SetFloat("_Progress", 1);
            _material.SetVector("_Center", origin ?? k_defaultCenter);
            _image.gameObject.SetActive(true);
            
            await Tween.MaterialProperty(_material, Shader.PropertyToID("_Progress"), 0f, duration, Ease.InOutQuad);
        }
    }
}