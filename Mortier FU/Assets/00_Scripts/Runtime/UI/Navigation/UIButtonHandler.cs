using MortierFu;
using UnityEngine;
using UnityEngine.EventSystems;

public class UIButtonHandler : MonoBehaviour, ISelectHandler, ISubmitHandler
{
    [SerializeField] private bool submit;
    
    public void OnSelect(BaseEventData eventData)
    {
        AudioService.PlayOneShot(AudioService.FMODEvents.SFX_UI_Navigate);
    }

    public void OnSubmit(BaseEventData eventData)
    {
        if (submit)
            AudioService.PlayOneShot(AudioService.FMODEvents.SFX_UI_Select);
    }
}
