using System.Collections.Generic;
using UnityEngine;

// Makes a model glow by swapping its materials for temporary copies with emission turned on.
// Copies are used (not a MaterialPropertyBlock) because emission needs the _EMISSION keyword,
// which a property block can't switch on. Remove() puts the original materials back.
// It sits on the model itself, so if the model is destroyed while glowing the copies are cleaned up too.
public class TowerGlow : MonoBehaviour
{
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP Lit
    private static readonly int ColorId = Shader.PropertyToID("_Color");         // Built-in / custom shaders

    private class Slot
    {
        public Material copy;
        public bool hasEmission;
        public Color baseEmission;
        public int colorId = -1;
        public Color baseColor;
    }

    private class Entry
    {
        public Renderer renderer;
        public Material[] originals;
        public Material[] copies;
    }

    private readonly List<Entry> _entries = new List<Entry>();
    private readonly List<Slot> _slots = new List<Slot>();

    private Color _glowColor;
    private Color _tintColor;
    private float _intensity;
    private bool _removed;

    // Starts a glow on every mesh under 'model'. Starts at 0, so nothing changes until SetAmount is called.
    public static TowerGlow Apply(GameObject model, Color glowColor, float intensity)
    {
        TowerGlow glow = model.AddComponent<TowerGlow>();
        glow.Init(glowColor, intensity);
        return glow;
    }

    private void Init(Color glowColor, float intensity)
    {
        _glowColor = glowColor;
        _intensity = intensity;

        // The surface tint can't be HDR, so squash the glow colour into 0-1 for it
        float max = Mathf.Max(1f, glowColor.maxColorComponent);
        _tintColor = new Color(glowColor.r / max, glowColor.g / max, glowColor.b / max, 1f);

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            // Meshes only: skips beams (LineRenderer), trails and particle effects
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;

            Material[] originals = r.sharedMaterials;
            Material[] copies = new Material[originals.Length];

            for (int i = 0; i < originals.Length; i++)
            {
                if (originals[i] == null) continue;

                Material copy = new Material(originals[i]) { name = originals[i].name + " (Glow)" };
                Slot slot = new Slot { copy = copy };

                if (copy.HasProperty(EmissionColorId))
                {
                    slot.hasEmission = true;
                    slot.baseEmission = copy.GetColor(EmissionColorId);
                    copy.EnableKeyword("_EMISSION");
                }

                if (copy.HasProperty(BaseColorId)) slot.colorId = BaseColorId;
                else if (copy.HasProperty(ColorId)) slot.colorId = ColorId;

                if (slot.colorId >= 0)
                    slot.baseColor = copy.GetColor(slot.colorId);

                copies[i] = copy;
                _slots.Add(slot);
            }

            r.sharedMaterials = copies;
            _entries.Add(new Entry { renderer = r, originals = originals, copies = copies });
        }
    }

    // 0 = looks exactly like the original, 1 = full glow
    public void SetAmount(float amount)
    {
        if (_removed) return;
        amount = Mathf.Clamp01(amount);

        foreach (Slot slot in _slots)
        {
            // Emission does the real glowing (and catches bloom if the URP Volume has it)
            if (slot.hasEmission)
                slot.copy.SetColor(EmissionColorId, slot.baseEmission + _glowColor * (_intensity * amount));

            // The surface colour also washes toward the glow, so shaders without emission still visibly change
            if (slot.colorId >= 0)
            {
                Color tinted = Color.Lerp(slot.baseColor, _tintColor, amount * 0.5f);
                tinted.a = slot.baseColor.a;
                slot.copy.SetColor(slot.colorId, tinted);
            }
        }
    }

    // Puts the original materials back and removes this component
    public void Remove()
    {
        if (_removed) return;

        RestoreAndClean();
        Destroy(this);
    }

    private void OnDestroy()
    {
        if (!_removed)
            RestoreAndClean();
    }

    private void RestoreAndClean()
    {
        _removed = true;

        foreach (Entry entry in _entries)
        {
            if (entry.renderer != null)
                entry.renderer.sharedMaterials = entry.originals;

            foreach (Material copy in entry.copies)
            {
                if (copy != null) Destroy(copy);
            }
        }

        _entries.Clear();
        _slots.Clear();
    }
}