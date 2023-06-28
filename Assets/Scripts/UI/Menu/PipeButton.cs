using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class PipeButton : MonoBehaviour {

    public Color selectedColor = Color.white, deselectedColor = Color.gray;
    public Color selectedTextColor = new Color(240,240,0), deselectedTextColor = Color.gray;
    public bool leftAnchored;

    private Color disabledColor;
    private Button button;
    public Transform textObject;
    public TextMeshProUGUI text;
    private Image image;
    private RectTransform rect;
    private Vector2 anchor, adjustedAnchor;

    public void Start() {
        rect = GetComponent<RectTransform>();
        button = GetComponent<Button>();
        textObject = this.gameObject.transform.Find("Text (TMP)"); //DO NOT RENAME THE TEXT OBJECT PLEASE IT WILL BREAK THE CODE THANKS <3
        text = textObject.GetComponent<TextMeshProUGUI>();
        image = GetComponentInChildren<Image>();
        anchor = leftAnchored ? rect.anchorMax : rect.anchorMin;
        adjustedAnchor = anchor + Vector2.right * (leftAnchored ? -0.05f : 0.05f);
        disabledColor = new(deselectedColor.r, deselectedColor.g, deselectedColor.b, deselectedColor.a/2f);
    }

    public void Update() {
        if (!button.interactable) {
            SetAnchor(adjustedAnchor);
            image.color = disabledColor;
            text.color = deselectedTextColor;
            return;
        }
        if (EventSystem.current.currentSelectedGameObject == gameObject) {
            SetAnchor(anchor);
            image.color = selectedColor;
            text.color = selectedTextColor;
        } else {
            SetAnchor(adjustedAnchor);
            image.color = deselectedColor;
            text.color = deselectedTextColor;
        }
    }

    private void SetAnchor(Vector2 value) {
        if (leftAnchored)
            rect.anchorMax = value;
        else
            rect.anchorMin = value;
    }
}
