// Small builders for UI Toolkit trees, named after the browser game's CSS classes (Rotorcraft.uss), so a screen
// reads much like its React original: Ui.Panel(Ui.H3("Briefing"), Ui.P(text)), Ui.Btn("Fly", "primary", go).
// Placard text (buttons, chips, headings, labels) is upper-cased here, as CSS text-transform did.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rotorcraft.Presentation.UI
{
    public static class Ui
    {
        static readonly CultureInfo GB = CultureInfo.GetCultureInfo("en-GB");

        public static VisualElement E(string classes = null, params VisualElement[] kids)
        {
            var e = new VisualElement();
            AddClasses(e, classes);
            foreach (var k in kids) if (k != null) e.Add(k);
            return e;
        }

        public static T Cls<T>(this T e, string classes) where T : VisualElement { AddClasses(e, classes); return e; }
        public static T With<T>(this T e, Action<T> f) where T : VisualElement { f(e); return e; }
        public static T Kids<T>(this T e, params VisualElement[] kids) where T : VisualElement { foreach (var k in kids) if (k != null) e.Add(k); return e; }
        public static T Kids<T>(this T e, IEnumerable<VisualElement> kids) where T : VisualElement { foreach (var k in kids) if (k != null) e.Add(k); return e; }
        public static T Show<T>(this T e, bool on) where T : VisualElement { e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None; return e; }
        public static T Toggle<T>(this T e, string cls, bool on) where T : VisualElement { e.EnableInClassList(cls, on); return e; }

        static void AddClasses(VisualElement e, string classes)
        {
            if (string.IsNullOrEmpty(classes)) return;
            foreach (var c in classes.Split(' ')) if (c.Length > 0) e.AddToClassList(c);
        }

        public static Label T(string text, string classes = null) { var l = new Label(text ?? ""); AddClasses(l, classes); return l; }
        public static Label Up(string text, string classes = null) => T((text ?? "").ToUpper(GB), classes);
        public static Label P(string text, string classes = null) => T(text, "p " + classes);
        public static Label Muted(string text, string classes = null) => T(text, "muted " + classes);
        public static Label H1(string text, bool smaller = false) => Up(text, smaller ? "h1 small-h" : "h1");
        public static Label H2(string text, string classes = null) => T(text, "h2 " + classes);
        public static Label H3(string text, bool fg = false) => Up(text, fg ? "h3 fg" : "h3");
        public static Label Placard(string text) => Up(text, "placard");
        public static Label Eyebrow(string text) => Up(text, "eyebrow");

        public static VisualElement Row(params VisualElement[] kids) => E("row", kids);
        public static VisualElement RowBetween(params VisualElement[] kids) => E("row between nowrap", kids);
        public static VisualElement Stack(params VisualElement[] kids) => E("stack", kids);
        public static VisualElement Tight(params VisualElement[] kids) => E("stack tight", kids);
        public static VisualElement Panel(params VisualElement[] kids) => E("panel stack", kids);
        public static VisualElement Spacer() { var e = new VisualElement(); e.style.flexGrow = 1; return e; }

        /// <summary>A button; small is the second line used in menu buttons ("Continue / Next: ...").</summary>
        public static Button Btn(string text, string classes, Action onClick, string small = null, bool disabled = false)
        {
            var b = new Button(() => onClick?.Invoke()) { text = "" };
            AddClasses(b, "btn " + classes);
            if (small != null)
            {
                b.AddToClassList("btn-col");
                b.Add(T((text ?? "").ToUpper(GB)));
                b.Add(T(small, "btn-small"));
            }
            else b.text = (text ?? "").ToUpper(GB);
            b.SetEnabled(!disabled);
            return b;
        }

        public static Label Chip(string text, string kind = null) => Up(text, "chip " + kind);

        /// <summary>Segmented choice (one of several).</summary>
        public static VisualElement Seg<TV>(IEnumerable<(TV value, string label)> options, TV current, Action<TV> onPick)
        {
            var seg = E("seg");
            foreach (var (v, l) in options)
            {
                var vv = v;
                var b = new Button(() => onPick(vv)) { text = l.ToUpper(GB) };
                AddClasses(b, "seg-btn");
                if (EqualityComparer<TV>.Default.Equals(v, current)) b.AddToClassList("on");
                seg.Add(b);
            }
            return seg;
        }

        public static VisualElement Check(string label, bool value, Action<bool> onChange)
        {
            var row = E("check");
            if (value) row.AddToClassList("on");
            var box = E("check-box", T(value ? "✓" : "", "check-tick"));
            row.Add(box);
            row.Add(T(label, "check-l"));
            row.RegisterCallback<ClickEvent>(_ => onChange(!value));
            row.focusable = true;
            row.RegisterCallback<NavigationSubmitEvent>(_ => onChange(!value));
            return row;
        }

        public static VisualElement Field(string label, VisualElement input)
        {
            return E("field", Up(label, "field-l"), input);
        }

        /// <summary>A labelled slider: the label shows the formatted value and updates while dragging.</summary>
        public static VisualElement Slider(string label, float min, float max, float value, Func<float, string> fmt, Action<float> onCommit, float step = 0)
        {
            var l = Up($"{label} · {fmt(value)}", "field-l");
            var s = new Slider(min, max) { value = value };
            s.AddToClassList("slider");
            s.RegisterValueChangedCallback(e =>
            {
                var v = step > 0 ? Mathf.Round(e.newValue / step) * step : e.newValue;
                l.text = $"{label} · {fmt(v)}".ToUpper(GB);
            });
            s.RegisterCallback<PointerCaptureOutEvent>(_ => onCommit(step > 0 ? Mathf.Round(s.value / step) * step : s.value));
            s.RegisterCallback<NavigationMoveEvent>(_ => onCommit(step > 0 ? Mathf.Round(s.value / step) * step : s.value));
            return E("field", l, s);
        }

        public static TextField Text(string value, int maxLength, string placeholder, Action<string> onChange)
        {
            var t = new TextField { value = value ?? "", maxLength = maxLength };
            t.AddToClassList("txt");
            t.textEdition.placeholder = placeholder;
            t.textEdition.hidePlaceholderOnFocus = true;
            t.RegisterValueChangedCallback(e => onChange(e.newValue));
            return t;
        }

        /// <summary>Key/value lines (the CSS dl.kv).</summary>
        public static VisualElement Kv(params (string k, string v)[] rows) => Kv(rows.Select(r => (r.k, r.v, (string)null)).ToArray());
        public static VisualElement Kv(params (string k, string v, string cls)[] rows)
        {
            var box = E("kv");
            foreach (var (k, v, cls) in rows) if (k != null) box.Add(E("kv-row", T(k, "kv-k"), T(v, "kv-v " + cls)));
            return box;
        }

        public static VisualElement Stat(string value, string label, string vClass = null) => E("stat", T(value, "stat-v " + vClass), Up(label, "stat-l"));
        public static VisualElement Stats(params VisualElement[] stats) => E("stats", stats);

        /// <summary>A bar 0..100: green, amber (c) or red (w).</summary>
        public static VisualElement Bar(double pct, string kind = null)
        {
            var b = E("bar " + kind);
            var f = E("bar-fill");
            f.style.width = Length.Percent((float)Math.Max(0, Math.Min(100, pct)));
            b.Add(f);
            return b;
        }

        public static VisualElement ScoreRow(string label, double pct, string right, string kind, string sub = null)
        {
            var l = E("scorerow-l", T(label));
            if (sub != null) { l.style.flexDirection = FlexDirection.Row; l.Add(T(" · " + sub, "muted small")); }
            return E("scorerow", l, Bar(pct, kind).Cls("in-row"), T(right, "scorerow-v"));
        }

        /// <summary>A simple table: column widths as flex ratios; numeric columns right-aligned.</summary>
        public static VisualElement Table(string[] headers, float[] widths, IEnumerable<VisualElement[]> rows, bool[] numeric = null)
        {
            var t = E("table");
            var hr = E("tr");
            for (int i = 0; i < headers.Length; i++)
            {
                var c = Up(headers[i], "th td" + (numeric != null && numeric[i] ? " num" : ""));
                c.style.flexGrow = widths[i]; c.style.flexBasis = 0;
                hr.Add(c);
            }
            t.Add(hr);
            foreach (var r in rows)
            {
                var tr = E("tr");
                for (int i = 0; i < r.Length && i < widths.Length; i++)
                {
                    var c = r[i] ?? new VisualElement();
                    var cell = E("td" + (numeric != null && numeric[i] ? " num" : ""));
                    cell.style.flexGrow = widths[i]; cell.style.flexBasis = 0;
                    cell.Add(c);
                    tr.Add(cell);
                }
                t.Add(tr);
            }
            return t;
        }

        public static Label Cell(string text, bool num = false) => T(text, num ? "td num" : "td");

        public static ScrollView Scroll(string classes, params VisualElement[] kids)
        {
            var s = new ScrollView(ScrollViewMode.Vertical);
            AddClasses(s, classes);
            s.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            foreach (var k in kids) if (k != null) s.Add(k);
            return s;
        }

        public static VisualElement Reasons(IEnumerable<string> reasons)
        {
            var r = E("reasons");
            foreach (var x in reasons) r.Add(E("row nowrap", T("○ ", "reason reason-dot"), T(x, "reason")));
            return r;
        }

        // ---------------------------------------------------------------------------------- formatting
        public static string Money(double n) => $"{(n < 0 ? "−" : "")}£{Math.Abs(Math.Round(n)).ToString("#,0", GB)}";
        public static string Hm(double sec) { var m = (int)Math.Round(sec / 60); return $"{m / 60}:{m % 60:00}"; }
        public static string Ms(double sec) => $"{(int)Math.Floor(sec / 60)}:{(int)Math.Round(sec % 60):00}";
        public static string Date(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("d MMM", GB);
        public static string N0(double v) => Math.Round(v).ToString("#,0", GB);
        public static string F1(double v) => v.ToString("F1", CultureInfo.InvariantCulture);
        public static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s.Substring(1);
    }
}
