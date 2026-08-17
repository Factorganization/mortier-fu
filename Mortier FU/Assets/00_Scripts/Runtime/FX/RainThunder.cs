using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MortierFu.Shared;
using UnityEngine;
using Random = UnityEngine.Random;

namespace MortierFu
{
    public class RainThunder : MonoBehaviour
    {
        [SerializeField] private ParticleSystem thunderFX;
        [SerializeField] private Vector2 randomTimeRange;

        private readonly ParticleSystem.Particle[] _particles = new ParticleSystem.Particle[1];
        private CountdownTimer _timerFx;

        private void OnEnable()
        {
            float delay = Random.Range(randomTimeRange.x, randomTimeRange.y);

            _timerFx = new CountdownTimer(delay);
            _timerFx.Start();
            _timerFx.OnTimerStop += OnTimerExpired;
        }

        private void OnDisable()
        {
            if (_timerFx != null)
            {
                _timerFx.OnTimerStop -= OnTimerExpired;
            }
        }

        private void OnTimerExpired()
        {
            CallThunderAsync(this.GetCancellationTokenOnDestroy()).Forget();
        }

        private async UniTaskVoid CallThunderAsync(CancellationToken cancellationToken)
        {
            _timerFx.Stop();

            if (thunderFX == null)
            {
                Logs.LogError("[RainThunder] 'thunderFX' ParticleSystem reference is missing in the Inspector!");
                return;
            }

            thunderFX.Play();

            bool cancelled = await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken).SuppressCancellationThrow();
            
            if (cancelled) return;

            int count = thunderFX.GetParticles(_particles);

            if (count == 0)
            {
                Logs.LogWarning("[RainThunder] Particle count is 0! If the strike comes from a Sub-Emitter, reference that child ParticleSystem directly.");
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    Vector3 particleWorldPos = thunderFX.main.simulationSpace == ParticleSystemSimulationSpace.Local
                        ? thunderFX.transform.TransformPoint(_particles[i].position)
                        : _particles[i].position;

                    bool soundEventNull = AudioService.FMODEvents.SFX_Misc_ThunderStrike.IsNull;
                    Logs.Log($"[RainThunder] Playing Audio at World Pos: {particleWorldPos} | FMOD Event IsNull: {soundEventNull}");

                    AudioService.PlayOneShot(AudioService.FMODEvents.SFX_Misc_ThunderStrike, particleWorldPos);
                }
            }

            float nextDelay = Random.Range(randomTimeRange.x, randomTimeRange.y);

            _timerFx.Reset(nextDelay);
            _timerFx.Start();
        }
    }
}