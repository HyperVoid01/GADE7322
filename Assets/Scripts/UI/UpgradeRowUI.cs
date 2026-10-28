using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One upgrade track in the upgrade menu: name, level, what the next level gives, cost and a buy button
public class UpgradeRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text descriptionText;  // Optional
    [SerializeField] private TMP_Text levelText;        // "Lv 1/3"
    [SerializeField] private TMP_Text bonusText;        // "+5 damage  >  +12 damage"
    [SerializeField] private TMP_Text costText;
    [SerializeField] private Button buyButton;
    [SerializeField] private TMP_Text buyButtonText;    // Optional

    [Tooltip("Optional: one image per level, filled in as levels are bought")]
    [SerializeField] private Image[] levelPips;
    [SerializeField] private Color pipOnColor = new Color(0.3f, 0.85f, 1f);
    [SerializeField] private Color pipOffColor = new Color(1f, 1f, 1f, 0.2f);

    [SerializeField] private Color affordableColor = Color.white;
    [SerializeField] private Color missingColor = new Color(1f, 0.33f, 0.33f);

    private Action _onBuy;

    private void Awake()
    {
        buyButton.onClick.AddListener(() => _onBuy?.Invoke());
    }

    public void Setup(Tower tower, int trackIndex, Action onBuy)
    {
        _onBuy = onBuy;

        UpgradeTrack track = tower.UpgradeData.tracks[trackIndex];
        int level = tower.GetTrackLevel(trackIndex);
        bool maxed = tower.IsTrackMaxed(trackIndex);

        nameText.text = track.displayName;
        if (descriptionText != null) descriptionText.text = track.description;
        levelText.text = $"Lv {level}/{track.MaxLevel}";

        // Current bonus and what the next level gives
        string current = level > 0 ? track.FormatValue(track.levels[level - 1].value) : "None";
        bonusText.text = maxed ? current : $"{current}  >  {track.FormatValue(track.levels[level].value)}";

        for (int i = 0; i < levelPips.Length; i++)
            levelPips[i].color = i < level ? pipOnColor : pipOffColor;

        if (maxed)
        {
            costText.text = "";
            buyButton.interactable = false;
            if (buyButtonText != null) buyButtonText.text = "MAXED";
            return;
        }

        ResourceManager.ResourceCost[] costs = tower.GetNextCost(trackIndex);
        costText.text = FormatCosts(costs);
        buyButton.interactable = costs == null || ResourceManager.Instance.HasEnoughResources(costs);
        if (buyButtonText != null) buyButtonText.text = "UPGRADE";
    }

    // One line per cost; resources the player is short on are shown in red
    private string FormatCosts(ResourceManager.ResourceCost[] costs)
    {
        if (costs == null || costs.Length == 0) return "Free";

        StringBuilder sb = new StringBuilder();

        foreach (ResourceManager.ResourceCost cost in costs)
        {
            int owned = ResourceManager.Instance.GetResourceAmount(cost.resourceType);
            string hex = ColorUtility.ToHtmlStringRGB(owned >= cost.amount ? affordableColor : missingColor);

            sb.AppendLine($"<color=#{hex}>{cost.amount} {ResourceManager.GetResourceName(cost.resourceType)} ({owned})</color>");
        }

        return sb.ToString().TrimEnd();
    }
}
