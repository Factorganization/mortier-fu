using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

namespace MortierFu
{
    public class RacePressureUI : MonoBehaviour
    {
        [Header("Parameters")]
        [SerializeField] private float _pulseDuration = 0.6f;
        [SerializeField] private float _delayBetweenPulses = 0.5f; // Temps de pause entre 2 pulses

        [Header("References")]
        [SerializeField] private Image _vignetteImage;

        [Header("Audio")]
        [SerializeField] private AudioSource _audioSource;
        [SerializeField] private AudioClip _pulseSFX;

        private Sequence _vignetteSequence;
        private Tween _delayTween;
        private Color _baseColor;

        private void Awake()
        {
            if (_vignetteImage != null)
            {
                _baseColor = _vignetteImage.color;
                _baseColor.a = 1f;
            }
        }

        public void StartVignettePressure(float duration)
        {
            if (_vignetteImage == null) return;

            StopVignettePressure();

            _vignetteImage.color = new Color(_baseColor.r, _baseColor.g, _baseColor.b, 0f);
            _vignetteImage.gameObject.SetActive(true);

            float halfDuration = _pulseDuration * 0.5f;
            
            _vignetteSequence = Sequence.Create(cycles: -1)
                .ChainCallback(this, target => target.PlayPulseSFX())
                .Chain(Tween.Alpha(_vignetteImage, endValue: 1f, duration: halfDuration))
                .Chain(Tween.Alpha(_vignetteImage, endValue: 0f, duration: halfDuration))
                .ChainDelay(_delayBetweenPulses);

            _delayTween = Tween.Delay(duration, StopVignettePressure);
        }

        public void StopVignettePressure()
        {
            if (_vignetteSequence.isAlive)
                _vignetteSequence.Stop();

            if (_delayTween.isAlive)
                _delayTween.Stop();

            if (_vignetteImage == null) return;

            var color = _vignetteImage.color;
            color.a = 0f;
            _vignetteImage.color = color;
        }

        private void PlayPulseSFX()
        {
            AudioService.PlayOneShot(AudioService.FMODEvents.SFX_Misc_EndRaceAlert);
        }
    }
}