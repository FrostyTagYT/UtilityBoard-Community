using System;
using System.Collections.Generic;
using UnityEngine;

namespace UtilityBoard
{
    // The floating / wrist board: original artwork built from primitives + TextMesh (no external assets).
    // Board local space: x right, y up, -z = toward the player. Units are metres at Scale 1 (0.23 x 0.36 m).
    public sealed class Board
    {
        public const float W = .23f, H = .36f, FaceZ = -.010f;
        public sealed class Btn
        {
            public string id, label; public float x, y, w, h; public bool enabled = true, selected, isLabel; public Color? textColor;
            public GameObject go; public Material m; public TextMesh t; public float flashUntil; public bool inside; public float prevZ = -1f; public string shown;
        }
        public GameObject Root; public readonly List<Btn> Buttons = new List<Btn>();
        private TextMesh title, sub, footer; private Material shellM, panelM, headM; private Font font;
        private string layoutKey = ""; public Theme Th = Theme.Classic;
        public string ShaderNote = "";

        public struct Theme
        {
            public Color shell, panel, head, btn, btnOn, btnHover, btnOff, text, textOff, accent;
            public static Theme Classic => new Theme { shell = Hex(0x8925BE), panel = Hex(0x140F1C), head = Hex(0x0A0710), btn = Hex(0x2A1F3A), btnOn = Hex(0x225F41), btnHover = Hex(0x4A3466), btnOff = Hex(0x1C1724), text = Color.white, textOff = Hex(0x6E6680), accent = Hex(0xD9B5F6) };
            public static Theme Midnight => new Theme { shell = Hex(0x4E1673), panel = Hex(0x0B0810), head = Hex(0x050308), btn = Hex(0x1F1729), btnOn = Hex(0x225F41), btnHover = Hex(0x3A2850), btnOff = Hex(0x141019), text = Color.white, textOff = Hex(0x5E5670), accent = Hex(0xD9B5F6) };
            public static Color Hex(int h) => new Color(((h >> 16) & 255) / 255f, ((h >> 8) & 255) / 255f, (h & 255) / 255f);
        }

        private Shader unlit;
        private Material Mat(Color c)
        {
            if (unlit == null)
            {
                try { var t = GTApi.GameType("BetterDayNightManager"); var inst = t?.GetField("instance")?.GetValue(null); var s = t?.GetField("gorillaUnlit")?.GetValue(inst) as Shader; if (s != null) { unlit = s; ShaderNote = "game unlit shader '" + s.name + "'"; } } catch (Exception) { }
                if (unlit == null) foreach (var n in new[] { "Universal Render Pipeline/Unlit", "Unlit/Color" }) { var s = Shader.Find(n); if (s != null) { unlit = s; ShaderNote = "shader " + n; break; } }
                if (unlit == null) { var g = GameObject.CreatePrimitive(PrimitiveType.Cube); unlit = g.GetComponent<Renderer>().sharedMaterial.shader; UnityEngine.Object.Destroy(g); ShaderNote = "default primitive shader '" + unlit.name + "'"; }
            }
            var m = new Material(unlit); SetCol(m, c); return m;
        }
        public static void SetCol(Material m, Color c) { foreach (var p in new[] { "_BaseColor", "_Color" }) if (m.HasProperty(p)) m.SetColor(p, c); }
        private Font F() { if (font != null) return font; foreach (var n in new[] { "LegacyRuntime.ttf", "Arial.ttf" }) { try { font = Resources.GetBuiltinResource<Font>(n); } catch (Exception) { } if (font != null) break; } return font; }
        public TextMesh Text(Transform parent, string s, float size, Vector3 pos, TextAnchor a = TextAnchor.MiddleCenter)
        {
            var g = new GameObject("txt"); g.transform.SetParent(parent, false); g.transform.localPosition = pos; var t = g.AddComponent<TextMesh>(); var f = F();
            if (f != null) { t.font = f; g.GetComponent<MeshRenderer>().sharedMaterial = f.material; }
            t.text = s; t.characterSize = size; t.fontSize = 64; t.anchor = a; t.alignment = TextAlignment.Center; t.color = Color.white; return t;
        }
        private GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m)
        {
            var b = GameObject.CreatePrimitive(PrimitiveType.Cube); UnityEngine.Object.Destroy(b.GetComponent<Collider>()); b.name = name;
            b.transform.SetParent(parent, false); b.transform.localPosition = pos; b.transform.localScale = size; var r = b.GetComponent<Renderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; return b;
        }

        public void Build()
        {
            Root = new GameObject("UtilityBoard_" + Edition.Id); UnityEngine.Object.DontDestroyOnLoad(Root);
            shellM = Mat(Th.shell); panelM = Mat(Th.panel); headM = Mat(Th.head);
            Box("shell", Root.transform, new Vector3(0, 0, .006f), new Vector3(W, H, .012f), shellM);
            Box("panel", Root.transform, new Vector3(0, 0, -.0005f), new Vector3(W - .012f, H - .012f, .002f), panelM);
            Box("titlebox", Root.transform, new Vector3(0, .150f, -.002f), new Vector3(W - .028f, .036f, .002f), headM);
            title = Text(Root.transform, Edition.Title, .0026f, new Vector3(0, .150f, -.0035f));
            sub = Text(Root.transform, "", .0016f, new Vector3(0, .1215f, -.0035f)); sub.color = Th.accent;
            footer = Text(Root.transform, Edition.Footer, .00115f, new Vector3(0, -.1675f, -.0035f)); footer.color = Th.accent;
        }
        public void ApplyTheme(Theme th)
        {
            Th = th; if (Root == null) return; SetCol(shellM, th.shell); SetCol(panelM, th.panel); SetCol(headM, th.head); sub.color = th.accent; footer.color = th.accent; layoutKey = "";
        }

        // ---------- layout: rows of buttons ----------
        public sealed class Row { public List<Btn> items = new List<Btn>(); }
        public static Btn B(string id, string label, bool enabled = true, bool selected = false) => new Btn { id = id, label = label, enabled = enabled, selected = selected };
        public static Btn Lbl(string label, Color? c = null) => new Btn { id = "", label = label, isLabel = true, enabled = false, textColor = c };
        public const float NavY = .094f, Row0 = .0605f, Step = .0298f, RowH = .025f, Inner = W - .028f;

        public void SetPage(string subtitle, List<List<Btn>> rows)
        {
            sub.text = subtitle;
            var all = new List<List<Btn>>(); all.Add(new List<Btn> { B("nav:back", "< BACK"), B("nav:home", "HOME"), B("nav:hide", "HIDE") }); all.AddRange(rows);
            var key = ""; foreach (var r in all) { foreach (var b in r) key += b.id + (b.isLabel ? "L" : "") + "|"; key += "/"; }
            if (key != layoutKey) Relayout(all, key);
            // update labels / states in place
            int k = 0; foreach (var r in all) foreach (var b in r) { var cur = Buttons[k++]; cur.label = b.label; cur.enabled = b.enabled; cur.selected = b.selected; cur.textColor = b.textColor; }
            Refresh();
        }
        private void Relayout(List<List<Btn>> all, string key)
        {
            foreach (var b in Buttons) { if (b.go != null) UnityEngine.Object.Destroy(b.go); if (b.t != null) UnityEngine.Object.Destroy(b.t.gameObject); if (b.m != null) UnityEngine.Object.Destroy(b.m); }
            Buttons.Clear(); layoutKey = key;
            for (int ri = 0; ri < all.Count; ri++)
            {
                var r = all[ri]; float y = ri == 0 ? NavY : Row0 - (ri - 1) * Step; float gap = .005f; float w = (Inner - gap * (r.Count - 1)) / r.Count;
                for (int i = 0; i < r.Count; i++)
                {
                    var b = r[i]; b.w = w; b.h = ri == 0 ? .022f : RowH; b.x = -Inner / 2 + w / 2 + i * (w + gap); b.y = y;
                    if (!b.isLabel) { b.m = Mat(Th.btn); b.go = Box("btn_" + b.id, Root.transform, new Vector3(b.x, b.y, -.006f), new Vector3(b.w, b.h, .008f), b.m); }
                    float ts = r.Count >= 3 ? .0016f : .0019f; if (ri == 0) ts = .0015f; if (b.isLabel && r.Count == 1) ts = .0017f;
                    b.t = Text(Root.transform, "", ts, new Vector3(b.x, b.y, b.isLabel ? -.0035f : FaceZ - .0006f));
                    Buttons.Add(b);
                }
            }
        }
        private static string Fit(string s, float w, float ts) { int max = Mathf.Max(4, Mathf.FloorToInt((w - .006f) / (ts * 3.15f))); return s == null ? "" : s.Length <= max ? s : s.Substring(0, max - 1) + "\u2026"; }
        public void Refresh()
        {
            float now = Time.unscaledTime;
            foreach (var b in Buttons)
            {
                var txt = Fit(b.label, b.w, b.t.characterSize); if (b.shown != txt) { b.t.text = txt; b.shown = txt; }
                b.t.color = b.textColor ?? (b.isLabel ? Th.accent : b.enabled ? Th.text : Th.textOff);
                if (b.go == null) continue;
                bool flash = now < b.flashUntil;
                SetCol(b.m, !b.enabled ? Th.btnOff : (b.selected || flash) ? Th.btnOn : b.inside ? Th.btnHover : Th.btn);
                float z = flash || b.inside ? -.0035f : -.006f;   // visibly depressed while touched
                var lp = b.go.transform.localPosition; if (Mathf.Abs(lp.z - z) > .0001f) { b.go.transform.localPosition = new Vector3(lp.x, lp.y, z); b.t.transform.localPosition = new Vector3(b.x, b.y, z - .0046f); }
            }
        }

        // ---------- fingertip presser ----------
        // Touch volume hugs the VISIBLE front face: 4 mm sideways margin, from 8 mm in front of the face to 20 mm behind it.
        // A press fires once when the tip ENTERS the volume from the front, then the button re-arms only after the tip leaves.
        public const float SideMargin = .004f, FrontMargin = .008f, BackDepth = .020f, Lockout = .25f;
        private float lastPress;
        public string Poke(Vector3 tipWorld, bool haveTip)
        {
            if (Root == null || !Root.activeInHierarchy) return null;
            float s = Mathf.Max(.05f, Root.transform.lossyScale.x); var lp = Root.transform.InverseTransformPoint(tipWorld);
            string fired = null;
            foreach (var b in Buttons)
            {
                if (b.go == null) continue;
                bool inXY = haveTip && Mathf.Abs(lp.x - b.x) < b.w / 2 + SideMargin / s && Mathf.Abs(lp.y - b.y) < b.h / 2 + SideMargin / s;
                bool inZ = haveTip && lp.z > FaceZ - FrontMargin / s && lp.z < FaceZ + BackDepth / s;
                bool inside = inXY && inZ;
                bool fromFront = b.prevZ < FaceZ + .002f / s;   // last frame the tip was in front of (or just touching) the face
                if (inside && !b.inside && fromFront && b.enabled && Time.unscaledTime - lastPress > Lockout && fired == null) { fired = b.id; lastPress = Time.unscaledTime; b.flashUntil = Time.unscaledTime + .18f; }
                b.inside = inside; b.prevZ = haveTip ? lp.z : -1f;
            }
            return fired;
        }
        public Btn Find(string id) { foreach (var b in Buttons) if (b.id == id) return b; return null; }
        public Vector3 FaceCenterWorld(Btn b, float zOffsetMeters)
        {
            float s = Mathf.Max(.05f, Root.transform.lossyScale.x);
            return Root.transform.TransformPoint(new Vector3(b.x, b.y, FaceZ + zOffsetMeters / s));
        }
    }
}
