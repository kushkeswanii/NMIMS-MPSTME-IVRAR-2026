using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace TrustBreach.Analytics.Dashboard
{
    /// <summary>Colours and small uGUI factory used by the research dashboard (everything is built from code).</summary>
    public static class DashboardUI
    {
        public static readonly Color Navy = Hex("#0B1A2E");
        public static readonly Color Charcoal = Hex("#161B24");
        public static readonly Color Card = Hex("#1D2430");
        public static readonly Color CardEdge = Hex("#2A3342");
        public static readonly Color Track = Hex("#252D3A");
        public static readonly Color Ink = Hex("#E8ECF2");
        public static readonly Color InkSecondary = Hex("#A9B3C1");
        public static readonly Color InkMuted = Hex("#717C8C");
        public static readonly Color Cyan = Hex("#38E1FF");
        public static readonly Color Amber = Hex("#FFB547");
        public static readonly Color Red = Hex("#FF5A4E");

        // Data marks: validated categorical set on the charcoal surface (L 0.48-0.67, CVD-safe in this order).
        public static readonly Color Resisted = Hex("#1AA6C2");
        public static readonly Color Recovered = Hex("#C4801E");
        public static readonly Color Declined = Hex("#8F74E6");
        public static readonly Color Compromised = Hex("#E0483C");
        public static readonly Color Incomplete = Hex("#5F6B7A");
        public static readonly Color SafeMark = Resisted;
        public static readonly Color RiskyMark = Compromised;
        public static readonly Color NeutralMark = Incomplete;
        public static readonly Color PromiseMark = Recovered;

        public static Color OutcomeColor(string outcome)
        {
            switch (outcome)
            {
                case ResearchSchema.Resisted: return Resisted;
                case ResearchSchema.Recovered: return Recovered;
                case ResearchSchema.Declined: return Declined;
                case ResearchSchema.Compromised: return Compromised;
                default: return Incomplete;
            }
        }

        public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.magenta;

        /// <summary>Child rect positioned from the parent's top-left corner, in reference pixels.</summary>
        public static RectTransform Area(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static RectTransform Stretch(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        public static Image Box(Transform parent, string name, float x, float y, float w, float h, Color color, bool raycast = false)
        {
            RectTransform rt = Area(parent, name, x, y, w, h);
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        public static TextMeshProUGUI Label(Transform parent, string name, string text, float x, float y, float w, float h, float size,
            Color color, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, FontStyles style = FontStyles.Normal)
        {
            RectTransform rt = Area(parent, name, x, y, w, h);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = align;
            tmp.fontStyle = style;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            tmp.richText = true;
            return tmp;
        }

        public static Button MakeButton(Transform parent, string name, string label, float x, float y, float w, float h, Color color,
            Action onClick, float fontSize = 17f)
        {
            Image image = Box(parent, name, x, y, w, h, color, true);
            var button = image.gameObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
            colors.colorMultiplier = 1.4f;
            button.colors = colors;
            button.onClick.AddListener(() => onClick());
            TextMeshProUGUI text = Label(image.transform, "Label", label, 0f, 0f, w, h, fontSize, Ink, TextAlignmentOptions.Center, FontStyles.Bold);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return button;
        }

        public static void SetLabel(Button button, string label) => button.GetComponentInChildren<TextMeshProUGUI>(true).text = label;

        public static void Hint(GameObject target, Func<string> text, Action<string> show)
        {
            DashboardHint hint = target.GetComponent<DashboardHint>() ?? target.AddComponent<DashboardHint>();
            hint.Text = text;
            hint.Show = show;
            if (target.TryGetComponent(out Graphic graphic)) graphic.raycastTarget = true;
        }
    }

    /// <summary>Hover explanation: shows a definition (with numerator / denominator) in the dashboard's footer bar.</summary>
    public class DashboardHint : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Func<string> Text;
        public Action<string> Show;

        public void OnPointerEnter(PointerEventData eventData) => Show?.Invoke(Text?.Invoke());
        public void OnPointerExit(PointerEventData eventData) => Show?.Invoke(null);
    }
}
