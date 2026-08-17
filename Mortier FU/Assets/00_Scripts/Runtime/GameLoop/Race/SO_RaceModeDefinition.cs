using UnityEngine;

namespace MortierFu
{
    public abstract class SO_RaceModeDefinition : ScriptableObject
    {
        [Header("Bully")] public bool UsePreviousRoundWinnerAsBully = true;
        public PlayerControlContext BullyContext = PlayerControlContext.AugmentRaceBullyClassic;
        public PlayerControlContext RacerContext = PlayerControlContext.AugmentRace;

        [Header("Duration")] public bool OverrideRaceDuration;
        [Min(0.1f)] public float RaceDuration = 20f;
        
        [Header("Race Explanation")] 
        public string RaceExplanationText;
        public string RaceExplanationTitle;
        public Sprite RaceExplanationImage;

        [Header("Bully Size")] [Min(0.1f)] public float BullyTargetSize = 3.5f;

        [Header("Bully Stats")] public SO_CharacterStats BullyStatsOverride;

        public abstract RaceModeRuntimeBase CreateRuntime();

        public float ResolveRaceDuration(float defaultDuration) => OverrideRaceDuration ? Mathf.Max(0.1f, RaceDuration) : Mathf.Max(0.1f, defaultDuration);

        public float ResolveBullyTargetSize() => Mathf.Max(0.1f, BullyTargetSize);
    }
}