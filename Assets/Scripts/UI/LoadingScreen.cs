using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Random = UnityEngine.Random;

// Underwater loading screen that stays up from the moment Play is pressed until the procedural map is finished.
//
// The screen lives in a prefab loaded from a Resources folder, so any scene can start a load:
//     LoadingScreen.LoadScene(1);
// It survives the scene change (DontDestroyOnLoad), follows the scene load and then MarchingCubes'
// generation progress, waits a couple of frames for OnTerrainGenerated listeners (player, kelp, resources)
// to finish their spawning behind it, and then fades out to reveal the world.
//
// Setup: add this to an empty GameObject, right-click the component header > Build Default Layout,
// tweak the look, then save it as a prefab named "LoadingScreen" inside a Resources folder (e.g. Assets/Resources).
[RequireComponent(typeof(Canvas), typeof(CanvasGroup))]
public class LoadingScreen : MonoBehaviour
{
    private const string ResourcePath = "LoadingScreen";

    [Header("References")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image background;
    [SerializeField] private RectTransform progressFill; // Stretched by its anchors, so it works with any Image type
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text percentText;
    [SerializeField] private TMP_Text tipText;
    [SerializeField] private TMP_Text seedText;
    [SerializeField] private RectTransform bubbleArea;

    [Header("Background")]
    [Tooltip("Colour at 0% - the deep")]
    [SerializeField] private Color deepColor = new Color(0.01f, 0.04f, 0.10f, 1f);
    [Tooltip("Colour at 100% - like rising toward the light")]
    [SerializeField] private Color shallowColor = new Color(0.04f, 0.27f, 0.40f, 1f);

    [Header("Progress")]
    [Tooltip("Share of the bar used by Unity loading the scene. Map generation fills the rest")]
    [SerializeField, Range(0f, 0.5f)] private float sceneLoadShare = 0.15f;
    [Tooltip("How quickly the bar catches up to the real progress")]
    [SerializeField] private float barCatchUpSpeed = 4f;
    [Tooltip("The screen stays up at least this long, so fast loads don't just flicker")]
    [SerializeField] private float minimumShowTime = 1.5f;

    [Header("Fade")]
    [SerializeField] private float fadeInTime = 0.25f;
    [SerializeField] private float fadeOutTime = 0.6f;

    [Header("Tips")]
    [SerializeField, TextArea] private string[] tips =
    {
        "Pufferfish damage every tower in range when they pop.",
        "Mine ore with your pickaxe to afford new towers and upgrades.",
        "Towers change shape after enough upgrades.",
        "Jellyfish shoot from range. Keep your towers healthy.",
        "Enemies follow the glowing trails toward the main tower.",
        "If the main tower falls, the game is over.",
        "AVOID spending too much time in the caves."
    };
    [SerializeField] private float tipInterval = 4.5f;
    [SerializeField] private float tipFadeTime = 0.35f;

    [Header("Bubbles")]
    [Tooltip("Leave empty to use a bubble drawn in code")]
    [SerializeField] private Sprite bubbleSprite;
    [SerializeField] private float bubblesPerSecond = 7f;
    [SerializeField] private Vector2 bubbleSize = new Vector2(10f, 34f);
    [SerializeField] private Vector2 bubbleRiseSpeed = new Vector2(60f, 160f);
    [SerializeField] private float bubbleWobble = 18f;
    [SerializeField] private Color bubbleColor = new Color(0.75f, 0.95f, 1f, 0.35f);

    [Header("Title")]
    [SerializeField] private float titleBobAmount = 8f;
    [SerializeField] private float titleBobSpeed = 1.1f;

    private class Bubble
    {
        public RectTransform rect;
        public Image image;
        public float speed;
        public float baseX;
        public float wobbleSpeed;
        public float phase;
        public bool active;
    }

    private static LoadingScreen _instance;
    public static bool IsLoading => _instance != null;

    private readonly List<Bubble> _bubbles = new List<Bubble>();
    private float _bubbleTimer;
    private float _target;    // Real progress
    private float _displayed; // What the bar shows, which eases toward _target and never goes backwards
    private string _status;
    private Vector2 _titleBasePosition;
    private Sprite _generatedBubbleSprite;

    // ---------------- Starting a load ----------------

    public static void LoadScene(int buildIndex) => Begin(() => SceneManager.LoadSceneAsync(buildIndex));
    public static void LoadScene(string sceneName) => Begin(() => SceneManager.LoadSceneAsync(sceneName));

    private static void Begin(Func<AsyncOperation> startLoad)
    {
        if (_instance != null) return; // Already loading, ignore double clicks

        // The game over screen freezes time. Restarting or going to the menu has to undo that.
        Time.timeScale = 1f;

        LoadingScreen prefab = Resources.Load<LoadingScreen>(ResourcePath);
        if (prefab == null)
        {
            Debug.LogWarning($"No LoadingScreen prefab found at Resources/{ResourcePath}. Loading without a loading screen.");
            startLoad();
            return;
        }

        _instance = Instantiate(prefab);
        DontDestroyOnLoad(_instance.gameObject);
        _instance.StartCoroutine(_instance.LoadRoutine(startLoad));
    }

    // ---------------- Load sequence ----------------

    private IEnumerator LoadRoutine(Func<AsyncOperation> startLoad)
    {
        float startTime = Time.unscaledTime;

        SetStatus("Diving in...");
        if (seedText != null) seedText.text = "";

        // Fade in before the load starts, so the screen covers the old scene's last frame
        yield return Fade(0f, 1f, fadeInTime);

        // 1. Unity loads the scene. AsyncOperation.progress stops at 0.9 until the scene activates.
        AsyncOperation load = startLoad();
        while (!load.isDone)
        {
            _target = Mathf.Clamp01(load.progress / 0.9f) * sceneLoadShare;
            yield return null;
        }

        // One frame for the new scene's Start methods, which is where MarchingCubes picks its seed and begins
        yield return null;

        // 2. The map is generated. Scenes without procedural terrain (the main menu) skip straight past this.
        MarchingCubes terrain = MarchingCubes.Instance;
        if (terrain != null)
        {
            if (seedText != null) seedText.text = $"Seed  {terrain.Seed}";

            while (terrain != null && !terrain.IsGenerated)
            {
                _target = Mathf.Lerp(sceneLoadShare, 1f, terrain.GenerationProgress);
                SetStatus(terrain.GenerationStage);
                yield return null;
            }
        }

        _target = 1f;

        // OnTerrainGenerated listeners spawn the player, kelp, resources and caves.
        // That makes the next frame or two heavy, so let it happen behind the screen.
        yield return null;
        yield return null;

        SetStatus("Ready");

        // Let the bar visibly finish, and don't vanish after a split second on fast machines
        while (_displayed < 0.999f || Time.unscaledTime - startTime < minimumShowTime)
            yield return null;

        // The world is revealed, and clicks reach the game again
        canvasGroup.blocksRaycasts = false;
        yield return Fade(1f, 0f, fadeOutTime);

        Destroy(gameObject);
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            canvasGroup.alpha = Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration));
            yield return null;
        }

        canvasGroup.alpha = to;
    }

    // ---------------- Per-frame visuals ----------------

    private void Awake()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();

        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = true; // Nothing behind the screen can be clicked while loading
        canvasGroup.interactable = false;

        if (titleText != null) _titleBasePosition = titleText.rectTransform.anchoredPosition;

        UpdateProgressVisuals();
    }

    private void Start()
    {
        if (tipText != null && tips != null && tips.Length > 0)
            StartCoroutine(TipRoutine());
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;

        if (_generatedBubbleSprite != null)
        {
            Destroy(_generatedBubbleSprite.texture);
            Destroy(_generatedBubbleSprite);
        }
    }

    // Unscaled time throughout, so the screen animates even if timeScale is 0
    private void Update()
    {
        float dt = Time.unscaledDeltaTime;

        // Eases toward the real progress, with a minimum speed so it always arrives
        float gap = _target - _displayed;
        if (gap > 0f)
            _displayed = Mathf.MoveTowards(_displayed, _target, Mathf.Max(0.08f, gap * barCatchUpSpeed) * dt);

        UpdateProgressVisuals();
        UpdateBubbles(dt);

        if (titleText != null)
        {
            float bob = Mathf.Sin(Time.unscaledTime * titleBobSpeed * Mathf.PI * 2f) * titleBobAmount;
            titleText.rectTransform.anchoredPosition = _titleBasePosition + new Vector2(0f, bob);
        }
    }

    private void UpdateProgressVisuals()
    {
        if (progressFill != null)
        {
            progressFill.anchorMin = new Vector2(0f, 0f);
            progressFill.anchorMax = new Vector2(_displayed, 1f);
            progressFill.offsetMin = Vector2.zero;
            progressFill.offsetMax = Vector2.zero;
        }

        if (percentText != null)
            percentText.text = $"{Mathf.FloorToInt(_displayed * 100f)}%";

        // The water gets lighter as the map nears completion
        if (background != null)
            background.color = Color.Lerp(deepColor, shallowColor, _displayed);
    }

    private void SetStatus(string status)
    {
        if (status == _status || statusText == null) return;

        _status = status;
        statusText.text = status;
    }

    // ---------------- Tips ----------------

    private IEnumerator TipRoutine()
    {
        // Shuffled, so every load starts on a different tip and none repeat until all have shown
        List<string> order = new List<string>(tips);
        int index = order.Count;

        while (true)
        {
            if (index >= order.Count)
            {
                for (int i = order.Count - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (order[i], order[j]) = (order[j], order[i]);
                }

                index = 0;
            }

            tipText.text = "TIP:  " + order[index++];
            yield return FadeText(tipText, 0f, 1f, tipFadeTime);

            yield return new WaitForSecondsRealtime(tipInterval);

            yield return FadeText(tipText, 1f, 0f, tipFadeTime);
        }
    }

    private static IEnumerator FadeText(TMP_Text text, float from, float to, float duration)
    {
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            text.alpha = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }

        text.alpha = to;
    }

    // ---------------- Bubbles ----------------

    // Pooled UI images that rise and wobble up the screen behind the text
    private void UpdateBubbles(float dt)
    {
        if (bubbleArea == null) return;

        Rect area = bubbleArea.rect;

        _bubbleTimer += dt * bubblesPerSecond;
        while (_bubbleTimer >= 1f)
        {
            _bubbleTimer -= 1f;
            SpawnBubble(area);
        }

        float time = Time.unscaledTime;

        foreach (Bubble bubble in _bubbles)
        {
            if (!bubble.active) continue;

            Vector2 position = bubble.rect.anchoredPosition;
            position.y += bubble.speed * dt;
            position.x = bubble.baseX + Mathf.Sin(time * bubble.wobbleSpeed + bubble.phase) * bubbleWobble;
            bubble.rect.anchoredPosition = position;

            // Fade out over the top fifth of the screen
            float height01 = position.y / Mathf.Max(1f, area.height);
            Color color = bubbleColor;
            color.a *= Mathf.Clamp01((1f - height01) * 5f);
            bubble.image.color = color;

            if (height01 >= 1f)
            {
                bubble.active = false;
                bubble.rect.gameObject.SetActive(false);
            }
        }
    }

    private void SpawnBubble(Rect area)
    {
        Bubble bubble = GetFreeBubble();

        // Smaller bubbles rise slower, like real ones
        float size01 = Random.value;
        float size = Mathf.Lerp(bubbleSize.x, bubbleSize.y, size01 * size01);

        bubble.speed = Mathf.Lerp(bubbleRiseSpeed.x, bubbleRiseSpeed.y, size01) * Random.Range(0.85f, 1.15f);
        bubble.baseX = Random.Range(0f, area.width);
        bubble.wobbleSpeed = Random.Range(1.5f, 3.5f);
        bubble.phase = Random.Range(0f, Mathf.PI * 2f);
        bubble.active = true;

        bubble.rect.sizeDelta = new Vector2(size, size);
        bubble.rect.anchoredPosition = new Vector2(bubble.baseX, -size);
        bubble.image.color = bubbleColor;
        bubble.rect.gameObject.SetActive(true);
    }

    private Bubble GetFreeBubble()
    {
        foreach (Bubble existing in _bubbles)
        {
            if (!existing.active) return existing;
        }

        GameObject go = new GameObject("Bubble", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(bubbleArea, false);

        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = Vector2.zero; // Positions measured from the area's bottom-left
        rect.pivot = new Vector2(0.5f, 0.5f);

        Image image = go.GetComponent<Image>();
        image.sprite = bubbleSprite != null ? bubbleSprite : GetGeneratedBubbleSprite();
        image.raycastTarget = false;

        Bubble bubble = new Bubble { rect = rect, image = image };
        _bubbles.Add(bubble);
        return bubble;
    }

    // A soft ring with a bright rim and a small highlight, drawn once into a texture
    private Sprite GetGeneratedBubbleSprite()
    {
        if (_generatedBubbleSprite != null) return _generatedBubbleSprite;

        const int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Color[] pixels = new Color[size * size];
        Vector2 center = new Vector2(size - 1, size - 1) * 0.5f;
        Vector2 highlight = center + new Vector2(-size * 0.17f, size * 0.17f);
        float radius = size * 0.5f - 1f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x, y);
                float d = Vector2.Distance(p, center) / radius; // 0 at the middle, 1 at the edge

                float alpha = 0f;
                if (d <= 1f)
                {
                    float fill = 0.15f;                                         // Faint body
                    float rim = Mathf.SmoothStep(0.7f, 1f, d) * (1f - Mathf.SmoothStep(0.94f, 1f, d)); // Bright edge
                    float shine = 1f - Mathf.Clamp01(Vector2.Distance(p, highlight) / (size * 0.12f)); // Top-left glint
                    alpha = Mathf.Clamp01(fill + rim * 0.9f + shine * shine);
                }

                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(pixels);
        texture.Apply();

        _generatedBubbleSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        return _generatedBubbleSprite;
    }
}