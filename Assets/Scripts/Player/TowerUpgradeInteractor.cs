using TMPro;
using UnityEngine;

// Lives on the player. Aim at a tower to see its range, and press the upgrade key to open its upgrade menu;
// press it again to close.
public class TowerUpgradeInteractor : MonoBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private KeyCode upgradeKey = KeyCode.F;
    [SerializeField] private float interactRange = 30f;
    [Tooltip("Towers AND anything that should block the view (terrain). Leave out the Player layer")]
    [SerializeField] private LayerMask aimMask = ~0;

    [Tooltip("Optional: shows '[F] Upgrade Harpoon' while aiming at a tower")]
    [SerializeField] private TMP_Text promptText;
    [Tooltip("Optional: ring showing the range of the tower being aimed at")]
    [SerializeField] private TowerRangeIndicator rangeIndicator;

    private Tower _aimedTower;

    private void Update()
    {
        TowerUpgradeMenu menu = TowerUpgradeMenu.Instance;
        if (menu == null) return;

        if (menu.IsOpen)
        {
            SetPrompt(null);

            // Keep showing the range while upgrading, so Range upgrades are visible straight away
            ShowRange(menu.CurrentTower);

            if (Input.GetKeyDown(upgradeKey)) menu.Close();
            return;
        }

        // Don't compete with the shop or tower placement
        bool busy = (TowerDevice.Instance != null && TowerDevice.Instance.IsActive) ||
                    (TowerBuilder.Instance != null && TowerBuilder.Instance.IsBuilding);

        _aimedTower = busy ? null : FindAimedTower();
        SetPrompt(_aimedTower);
        ShowRange(_aimedTower);

        if (_aimedTower != null && _aimedTower.CanBeUpgraded && Input.GetKeyDown(upgradeKey))
            menu.Open(_aimedTower);
    }

    private Tower FindAimedTower()
    {
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        if (!Physics.Raycast(ray, out RaycastHit hit, interactRange, aimMask, QueryTriggerInteraction.Ignore))
            return null;

        Tower tower = hit.collider.GetComponentInParent<Tower>();

        // Disabled while it's still falling from the sky
        if (tower == null || !tower.enabled)
            return null;

        return tower;
    }

    private void ShowRange(Tower tower)
    {
        if (rangeIndicator != null)
            rangeIndicator.Show(tower);
    }

    private void SetPrompt(Tower tower)
    {
        if (promptText == null) return;

        bool show = tower != null && tower.CanBeUpgraded;
        if (promptText.gameObject.activeSelf != show)
            promptText.gameObject.SetActive(show);

        if (show)
            promptText.text = $"[{upgradeKey}] Upgrade {tower.DisplayName}";
    }
}
