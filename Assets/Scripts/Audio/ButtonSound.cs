using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Hover and click sounds for a UI button. Clicking a disabled button (can't afford it, maxed out)
// plays the "denied" buzz instead, so the player knows why nothing happened.
// SfxPlayer adds this to every button in the scene automatically; spawned buttons add it themselves.
public class ButtonSound : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
{
    private Selectable _selectable;

    private void Awake()
    {
        _selectable = GetComponent<Selectable>();
    }

    private bool IsUsable => _selectable == null || _selectable.IsInteractable();

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (SfxPlayer.Instance != null && IsUsable)
            SfxPlayer.Instance.PlayUiHover();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (SfxPlayer.Instance == null) return;

        if (IsUsable)
            SfxPlayer.Instance.PlayUiClick();
        else
            SfxPlayer.Instance.PlayDenied();
    }
}
