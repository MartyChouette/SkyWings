using UnityEngine;

/// <summary>
/// Toggles one or more UI roots on/off.
/// - Press a key to show/hide everything (e.g. F1).
/// - You can also hook ToggleHUD() to a UI Button.
/// </summary>
public class UIToggleHUD : MonoBehaviour
{
    [Header("What to toggle")]
    [Tooltip("All of these GameObjects will be enabled/disabled together.")]
    public GameObject[] hudRoots;

    [Header("Input")]
    [Tooltip("Key to toggle HUD visibility.")]
    public KeyCode toggleKey = KeyCode.F1;

    [Tooltip("If true, HUD starts hidden and you press the key to show it.")]
    public bool startHidden = false;

    bool _visible = true;

    void Awake()
    {
        // Initialize visibility
        _visible = !startHidden;
        ApplyVisibility();
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleHUD();
        }
    }

    /// <summary>
    /// Toggle from code or UI button.
    /// </summary>
    public void ToggleHUD()
    {
        _visible = !_visible;
        ApplyVisibility();
    }

    /// <summary>
    /// Explicit show/hide from other scripts if you want.
    /// </summary>
    public void SetHUDVisible(bool visible)
    {
        _visible = visible;
        ApplyVisibility();
    }

    void ApplyVisibility()
    {
        if (hudRoots == null) return;

        foreach (var go in hudRoots)
        {
            if (go != null)
                go.SetActive(_visible);
        }
    }
}