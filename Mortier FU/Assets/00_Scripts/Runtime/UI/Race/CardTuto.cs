using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using MortierFu;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CardTuto : MonoBehaviour
{
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private Image mainImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private float appearDuration = 0.3f;
    [SerializeField] private float disappearDuration = 0.2f;
    [SerializeField] private Vector3 appearScaleFrom = Vector3.zero;

    private Vector3 originScale;

    private void Awake()
    {
        originScale = transform.localScale;
    }

    public void SetData(TutorialCardData data)
    {
        if (descriptionText)
            descriptionText.text = data.DescriptionKey;

        if (mainImage)
            mainImage.sprite = data.MainImage;
        
        if (nameText)
            nameText.text = data.Name;
    }

    public UniTask PlayAppear(CancellationTokenSource cts)
    {
        canvasGroup.alpha = 0f;
        transform.localScale = appearScaleFrom;

        AudioService.PlayOneShot(AudioService.FMODEvents.SFX_UI_Navigate, transform.position);

        Tween.Alpha(canvasGroup, 1f, appearDuration).ToUniTask(cancellationToken:cts.Token).Forget();
        return Tween.Scale(transform,originScale, appearDuration, Ease.OutBack).ToUniTask(cancellationToken: cts.Token);
    }

    public Tween PlayDisappear(CancellationTokenSource cts)
    {
        Tween.Alpha(canvasGroup, 0f, disappearDuration).ToUniTask(cancellationToken:cts.Token).Forget();
        return Tween.Scale(transform, appearScaleFrom, disappearDuration, Ease.InBack);
    }
    
    
}