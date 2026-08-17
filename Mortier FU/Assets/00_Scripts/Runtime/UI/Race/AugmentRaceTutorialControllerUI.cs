using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MortierFu;
using MortierFu.Shared;
using PrimeTween;
using UnityEngine;

public class AugmentRaceTutorialControllerUI : MonoBehaviour
{
    [SerializeField] private CanvasGroup background;
    [SerializeField] private Transform cardsLayout;
    [SerializeField] private CardTuto cardPrefab;
    [SerializeField] private SO_TutorialConfig tutorialConfig;
    [SerializeField] private float backgroundFadeDuration = 0.25f;
    [SerializeField] private float cardStaggerDelay = 0.08f;

    private readonly List<CardTuto> _activeCards = new();
    private AugmentRaceTutorialController _tutorialController;
    
    private CancellationTokenSource _cts;

    
    private void OnEnable()
    {

        GameModeBase gm = GameService.CurrentGameMode as GameModeBase;

        if (gm == null)
        {
            Logs.LogError("[AugmentRaceTutorialControllerUI] No active GameModeBase found.");
            return;
        }

        gm.OnTutorialRaceControllerInit += Init;
    }

    private void Init(AugmentRaceTutorialController aTutorialController)
    {
        _tutorialController = aTutorialController;
        
        if (_tutorialController == null)
        {
            Logs.LogError("[AugmentRaceTutorialControllerUI] AugmentRaceTutorialController is null on current game mode.");
            return;
        }
        
        _tutorialController.OnTutorialStepStarted += HandleTutorialStepStarted;
        _tutorialController.OnTutorialStepCompleted += HandleTutorialStepCompleted;

        background.gameObject.SetActive(false);
        background.alpha = 0f;
    }

    private void OnDisable()
    {
        if (GameService.CurrentGameMode is GameModeBase gm) gm.OnTutorialRaceControllerInit -= Init;

        if (_tutorialController != null)
        {
            _tutorialController.OnTutorialStepStarted -= HandleTutorialStepStarted;
            _tutorialController.OnTutorialStepCompleted -= HandleTutorialStepCompleted;
        }
    }

    private void HandleTutorialStepStarted(AugmentRaceTutorialType tutorialType) =>
        ShowTutorialAsync(tutorialType).Forget();

    private void HandleTutorialStepCompleted(AugmentRaceTutorialType tutorialType) =>
        HideTutorialAsync().Forget();

    private async UniTaskVoid ShowTutorialAsync(AugmentRaceTutorialType tutorialType)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        
        SO_TutorialStepData stepData = tutorialConfig.GetStepData(tutorialType);

        if (stepData is null)
        {
            Logs.LogError($"[AugmentRaceTutorialControllerUI] No step data found for {tutorialType}.");
            return;
        }

        background.gameObject.SetActive(true);
        await Tween.Alpha(background, 1f, backgroundFadeDuration).ToUniTask(PlayerLoopTiming.Update,_cts.Token);

        foreach (TutorialCardData cardData in stepData.Cards)
        {
            CardTuto card = Instantiate(cardPrefab, cardsLayout);
            card.SetData(cardData);
            _activeCards.Add(card);

            await card.PlayAppear(_cts);

            await UniTask.Delay(System.TimeSpan.FromSeconds(cardStaggerDelay),cancellationToken: _cts.Token);
        }
    }

    private async UniTaskVoid HideTutorialAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        
        Tween lastDisappearTween = default;
        
        AudioService.PlayOneShot(AudioService.FMODEvents.SFX_Augment_Showcase);

        foreach (CardTuto card in _activeCards)
        {
            lastDisappearTween = card.PlayDisappear(_cts);
        }

        await Tween.Alpha(background, 0f, backgroundFadeDuration).ToUniTask(PlayerLoopTiming.Update, _cts.Token);

        if (lastDisappearTween.isAlive)
            await lastDisappearTween;

        foreach (CardTuto card in _activeCards)
        {
            Destroy(card.gameObject);
        }

        _activeCards.Clear();

        background.gameObject.SetActive(false);
    }
}