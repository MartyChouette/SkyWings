// ReticleHUD.cs
using UnityEngine;
using UnityEngine.UI;
using System.Collections;

[RequireComponent(typeof(Image))]
public class ReticleHUD : MonoBehaviour
{
    public Color normalColor = Color.white;
    public Color targetColor = new Color(1f, 0.5f, 0.2f);
    public float popScale = 1.35f;
    public float popTime = 0.12f;
    public float returnTime = 0.15f;

    Image img;
    Vector3 baseScale;

    void Awake()
    {
        img = GetComponent<Image>();
        baseScale = transform.localScale;
        img.color = normalColor;
    }

    public void SetTargeting(bool on)
    {
        img.color = on ? targetColor : normalColor;
    }

    public void Pop()
    {
        StopAllCoroutines();
        StartCoroutine(PopCR());
    }

    IEnumerator PopCR()
    {
        float t = 0f;
        Vector3 start = baseScale;
        Vector3 peak = baseScale * popScale;
        while (t < popTime)
        {
            t += Time.unscaledDeltaTime;
            transform.localScale = Vector3.Lerp(start, peak, t / popTime);
            yield return null;
        }
        t = 0f;
        while (t < returnTime)
        {
            t += Time.unscaledDeltaTime;
            transform.localScale = Vector3.Lerp(peak, baseScale, t / returnTime);
            yield return null;
        }
        transform.localScale = baseScale;
    }
}