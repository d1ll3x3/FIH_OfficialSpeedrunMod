using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace OfficialSpeedrunMod
{
    // The Speedrun app's card icons, made the way the vanilla cards are (read from the game's
    // assets: the phone main menu items' CardMaskWithAnimator):
    //  - each card's Animator swaps the Card image's sprite: an "Activate" flipbook of 7 to 11
    //    drawings over 0.7 to 1 second, played once when the entry is selected, then held on
    //    its last drawing ("Idle");
    //  - the drawings are line icons on a 2:1 canvas (512x256 or 1024x512), centered and about
    //    three quarters of its height, lines 2.3 to 3.9% of its height, antialiased; the Card
    //    stretches the canvas to its own rect and the mask crops the empty sides.
    // So each icon here is a flipbook of drawings on a 512x256 canvas, set as the copied
    // entry's Card sprite like the Animator does (whose Stats flipbook is switched off), and
    // played once each time the entry is shown. The drawings are rendered in code from shapes
    // given as signed distances, each frame a pose of the icon's parts, because the mod ships
    // no assets.
    public static class PhoneIcons
    {
        public enum Kind { Stopwatch, Route, Chat, Trophy }

        // The canvas, and the design grid the shapes are drawn on (Size units, y up), centered
        // on it at Zoom canvas pixels per unit: the drawings go from about 6 to 92% of the
        // canvas height, like Stats'.
        private const int Width = 512, Height = 256;
        private const float Size = 128f, Zoom = 2.27f, Bottom = -20f;
        // Half the line width, in design units: 8 canvas pixels wide, 3% of the canvas height.
        private const float Stroke = 4f / Zoom;

        private sealed class Part
        {
            public string Name;
            public Vector2 Pivot;
            public Func<Vector2, float> Distance;
        }

        // A part's pose in a frame: moved, turned (degrees, counterclockwise) and scaled around
        // its pivot. The whole icon has a pose too, around its own pivot.
        private struct Pose
        {
            public Vector2 Offset;
            public float Angle;
            public Vector2 Scale;
            public bool Hidden;

            public static Pose Rest => new Pose { Scale = Vector2.one };
        }

        private sealed class Frame
        {
            public float Time;
            public Pose Icon = Pose.Rest;
            public readonly Dictionary<string, Pose> Parts = new Dictionary<string, Pose>();
        }

        private sealed class Flipbook
        {
            public Vector2 Pivot;
            public Part[] Parts;
            public Frame[] Frames;
            public Sprite[] Sprites;
        }

        private sealed class Icon
        {
            public Flipbook Book;
            public Image Card;
            public bool WasShown;
            public float ShownAt;
        }

        private static readonly List<Icon> _icons = new List<Icon>();
        private static readonly Dictionary<Kind, Flipbook> _books = new Dictionary<Kind, Flipbook>();

        // Gives a copied entry's card this icon instead of the Stats one.
        public static void Apply(GameObject entry, Kind kind)
        {
            var mask = entry.transform.Find("CardMaskWithAnimator");
            var card = mask?.Find("Card")?.GetComponent<Image>();
            if (card == null)
            {
                SpeedrunPlugin.Logger.LogWarning($"Phone icons: '{entry.name}' has no card; it keeps the Stats icon.");
                return;
            }
            // The copied Animator would put the Stats drawings back. Found by its IL2CPP type
            // name: GetComponent(string) is broken in this interop, and the Animator type would
            // need its module only for this.
            foreach (var behaviour in mask.GetComponents<Behaviour>().ToArray())
                if (behaviour.GetIl2CppType().Name == "Animator") behaviour.enabled = false;

            var book = Book(kind);
            card.sprite = book.Sprites[book.Sprites.Length - 1];
            _icons.Add(new Icon { Book = book, Card = card });
        }

        // Plays the flipbook once each time its entry comes on screen, then holds the last
        // drawing.
        public static void Update()
        {
            _icons.RemoveAll(i => i.Card == null);
            foreach (var icon in _icons)
            {
                if (!icon.Card.gameObject.activeInHierarchy)
                {
                    icon.WasShown = false;
                    continue;
                }
                if (!icon.WasShown)
                {
                    icon.WasShown = true;
                    icon.ShownAt = Time.unscaledTime;
                }
                float t = Time.unscaledTime - icon.ShownAt;
                var frames = icon.Book.Frames;
                int frame = 0;
                while (frame + 1 < frames.Length && frames[frame + 1].Time <= t) frame++;
                var sprite = icon.Book.Sprites[frame];
                if (icon.Card.sprite == null || icon.Card.sprite.Pointer != sprite.Pointer) icon.Card.sprite = sprite;
            }
        }

        // ── Drawing ─────────────────────────────────────────────────────

        private static Flipbook Book(Kind kind)
        {
            if (_books.TryGetValue(kind, out var book) && book.Sprites.All(s => s != null)) return book;
            book = Define(kind);
            book.Sprites = book.Frames.Select(f => Render(book, f)).ToArray();
            _books[kind] = book;
            return book;
        }

        private static Sprite Render(Flipbook book, Frame frame)
        {
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            var pixels = new Color32[Width * Height];
            var parts = book.Parts.Select(p => (p, Get(frame, p.Name))).Where(x => !x.Item2.Hidden).ToArray();
            float iconScale = Mathf.Min(Mathf.Abs(frame.Icon.Scale.x), Mathf.Abs(frame.Icon.Scale.y));
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    // Canvas pixel to design units, undoing the icon's pose.
                    var p = new Vector2((x + 0.5f - Width / 2f) / Zoom + Size / 2f, (y + 0.5f - Bottom) / Zoom);
                    if (p.x < -20f || p.x > Size + 20f) continue;
                    p = Undo(p, book.Pivot, frame.Icon);
                    float d = float.MaxValue;
                    foreach (var (part, pose) in parts)
                    {
                        float scale = Mathf.Min(Mathf.Abs(pose.Scale.x), Mathf.Abs(pose.Scale.y));
                        d = Mathf.Min(d, part.Distance(Undo(p, part.Pivot, pose)) * scale);
                    }
                    // Antialiased: the pixel's coverage of the ink, a canvas pixel wide.
                    float alpha = Mathf.Clamp01(0.5f - d * iconScale * Zoom);
                    if (alpha > 0f) pixels[y * Width + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0, 0, Width, Height), new Vector2(0.5f, 0.5f), 50f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Pose Get(Frame frame, string part) => frame.Parts.TryGetValue(part, out var pose) ? pose : Pose.Rest;

        // A point of the posed drawing back to where it is in the shape's own space.
        private static Vector2 Undo(Vector2 p, Vector2 pivot, Pose pose)
        {
            var q = p - pivot - pose.Offset;
            float a = -pose.Angle * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
            q = new Vector2(q.x * c - q.y * s, q.x * s + q.y * c);
            return new Vector2(q.x / pose.Scale.x, q.y / pose.Scale.y) + pivot;
        }

        // ── The icons ───────────────────────────────────────────────────

        private static Flipbook Define(Kind kind) => kind switch
        {
            Kind.Stopwatch => Stopwatch(),
            Kind.Route => Route(),
            Kind.Chat => Chat(),
            _ => Trophy(),
        };

        // Speedrun: the stopwatch lands squashed, its button clicks and the hand sweeps round
        // once, overshooting before it settles.
        private static Flipbook Stopwatch() => new Flipbook
        {
            Pivot = V(64, 14),
            Parts = new[]
            {
                P("Body", 64, 56, q => Min(
                    Ring(q, 64, 56, 40), Line(q, 64, 96, 64, 104), Line(q, 90, 86, 97, 93),
                    Line(q, 64, 88, 64, 82), Line(q, 64, 24, 64, 30), Line(q, 32, 56, 38, 56), Line(q, 90, 56, 96, 56))),
                P("Hand", 64, 56, q => Min(Line(q, 64, 56, 64, 80), Disc(q, 64, 56, 5))),
                P("Button", 64, 108, q => Box(q, 54, 104, 74, 113, 3, true)),
            },
            Frames = new[]
            {
                F(0.00f, Whole(s: V(1.14f, 0.8f))),
                F(0.10f, Whole(s: V(0.93f, 1.1f)), ("Button", Move(0, 4))),
                F(0.20f, Whole(), ("Button", Move(0, -4)), ("Hand", Turn(-25))),
                F(0.30f, Whole(a: 5), ("Hand", Turn(-115))),
                F(0.40f, Whole(a: -4), ("Hand", Turn(-205))),
                F(0.50f, Whole(a: 2), ("Hand", Turn(-295))),
                F(0.60f, Whole(), ("Hand", Turn(-400))),
                F(0.72f, Whole(), ("Hand", Turn(-382))),
                F(0.85f, Whole(), ("Hand", Turn(-390))),
            },
        };

        // Levels: a map pin drops on the level, squashes on landing and bounces, then the route
        // to it is drawn dot by dot.
        private static Flipbook Route()
        {
            var dots = new[] { V(18, 30), V(32, 22), V(48, 25), V(58, 37), V(66, 50), V(76, 60) };
            var tip = V(94, 52);
            var parts = dots.Select((d, i) => P("Dot" + i, d.x, d.y, q => Disc(q, d.x, d.y, 6f))).ToList();
            parts.Add(P("Shadow", tip.x, tip.y - 4, q => Ellipse(q, tip.x, tip.y - 4, 17, 4.5f)));
            parts.Add(P("Pin", tip.x, tip.y, q => Min(
                Ring(q, tip.x, tip.y + 42, 20), Disc(q, tip.x, tip.y + 42, 7),
                Line(q, tip.x - 19, tip.y + 35, tip.x, tip.y), Line(q, tip.x + 19, tip.y + 35, tip.x, tip.y))));

            // The dots from `count` on are not drawn yet.
            (string, Pose)[] Shown(int count, params (string, Pose)[] extra) =>
                Enumerable.Range(count, dots.Length - count).Select(i => ("Dot" + i, Hide())).Concat(extra).ToArray();

            return new Flipbook
            {
                Pivot = V(64, 14),
                Parts = parts.ToArray(),
                Frames = new[]
                {
                    F(0.00f, Whole(), Shown(0, ("Pin", At(0, 26, 0, 0.88f, 1.16f)), ("Shadow", Grow(0.5f)))),
                    F(0.10f, Whole(), Shown(0, ("Pin", At(0, 0, 0, 1.24f, 0.78f)), ("Shadow", Grow(1.15f)))),
                    F(0.20f, Whole(), Shown(0, ("Pin", At(0, 10, 0, 0.94f, 1.08f)), ("Shadow", Grow(0.8f)))),
                    F(0.30f, Whole(), Shown(1, ("Pin", At(0, 0, 0, 1.06f, 0.95f)))),
                    F(0.40f, Whole(), Shown(2, ("Dot1", Grow(1.3f)))),
                    F(0.50f, Whole(), Shown(4, ("Dot3", Grow(1.3f)))),
                    F(0.60f, Whole(), Shown(6, ("Dot5", Grow(1.3f)))),
                    F(0.72f, Whole()),
                },
            };
        }

        // NPCs: a speech bubble pops up, the three dots hop one after the other, and the
        // bubble wobbles to a stop.
        private static Flipbook Chat() => new Flipbook
        {
            Pivot = V(40, 22),
            Parts = new[]
            {
                P("Bubble", 40, 22, q => Min(
                    Box(q, 14, 40, 114, 102, 18, false), Line(q, 32, 40, 24, 22), Line(q, 24, 22, 48, 40))),
                P("Dot0", 40, 71, q => Disc(q, 40, 71, 7)),
                P("Dot1", 64, 71, q => Disc(q, 64, 71, 7)),
                P("Dot2", 88, 71, q => Disc(q, 88, 71, 7)),
            },
            Frames = new[]
            {
                F(0.00f, Whole(s: V(0.75f, 0.6f)), ("Dot0", Hide()), ("Dot1", Hide()), ("Dot2", Hide())),
                F(0.10f, Whole(s: V(1.1f, 1.12f)), ("Dot0", Hide()), ("Dot1", Hide()), ("Dot2", Hide())),
                F(0.20f, Whole(), ("Dot0", At(0, 9, 0, 1.2f, 1.2f)), ("Dot1", Hide()), ("Dot2", Hide())),
                F(0.30f, Whole(), ("Dot1", At(0, 9, 0, 1.2f, 1.2f)), ("Dot2", Hide())),
                F(0.40f, Whole(), ("Dot2", At(0, 9, 0, 1.2f, 1.2f))),
                F(0.50f, Whole(a: 4)),
                F(0.60f, Whole(a: -2)),
                F(0.70f, Whole()),
            },
        };

        // Best times: the trophy drops in squashed, springs up, rocks and settles, and a
        // sparkle flashes on its rim.
        private static Flipbook Trophy() => new Flipbook
        {
            Pivot = V(64, 16),
            Parts = new[]
            {
                P("Cup", 64, 16, q => Min(
                    Line(q, 34, 98, 94, 98), Max(Ring(q, 64, 82, 30), q.y - 98),
                    Max(Ring(q, 34, 82, 12), q.x - 34), Max(Ring(q, 94, 82, 12), 94 - q.x),
                    Line(q, 64, 52, 64, 32), Box(q, 42, 16, 86, 30, 3, false))),
                P("Sparkle", 108, 108, q => Min(Line(q, 108, 98, 108, 118), Line(q, 98, 108, 118, 108))),
            },
            Frames = new[]
            {
                F(0.00f, Whole(s: V(1.2f, 0.75f)), ("Sparkle", Hide())),
                F(0.10f, Whole(o: V(0, 6), s: V(0.9f, 1.12f)), ("Sparkle", Hide())),
                F(0.20f, Whole(a: -8), ("Sparkle", Hide())),
                F(0.30f, Whole(a: 6), ("Sparkle", Hide())),
                F(0.40f, Whole(a: -3), ("Sparkle", Grow(0.5f))),
                F(0.50f, Whole(), ("Sparkle", At(0, 0, -30, 1.35f, 1.35f))),
                F(0.60f, Whole(), ("Sparkle", At(0, 0, -45, 0.9f, 0.9f))),
                F(0.72f, Whole()),
            },
        };

        // ── Building blocks ─────────────────────────────────────────────

        private static Vector2 V(float x, float y) => new Vector2(x, y);

        private static Part P(string name, float px, float py, Func<Vector2, float> distance)
            => new Part { Name = name, Pivot = V(px, py), Distance = distance };

        private static Frame F(float time, Pose icon, params (string part, Pose pose)[] parts)
        {
            var frame = new Frame { Time = time, Icon = icon };
            foreach (var (part, pose) in parts) frame.Parts[part] = pose;
            return frame;
        }

        private static Pose Whole(Vector2? o = null, float a = 0f, Vector2? s = null)
            => new Pose { Offset = o ?? Vector2.zero, Angle = a, Scale = s ?? Vector2.one };

        private static Pose At(float x, float y, float angle, float sx, float sy)
            => new Pose { Offset = V(x, y), Angle = angle, Scale = V(sx, sy) };

        private static Pose Move(float x, float y) => At(x, y, 0f, 1f, 1f);

        private static Pose Turn(float angle) => At(0f, 0f, angle, 1f, 1f);

        private static Pose Grow(float scale) => At(0f, 0f, 0f, scale, scale);

        private static Pose Hide() => new Pose { Scale = Vector2.one, Hidden = true };

        // Signed distances (negative inside the ink), in design units. Min is called for every
        // pixel of every frame, so it has overloads instead of a params array.
        private static float Min(float a, float b) => Mathf.Min(a, b);
        private static float Min(float a, float b, float c) => Mathf.Min(Mathf.Min(a, b), c);
        private static float Min(float a, float b, float c, float d) => Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, d));
        private static float Min(float a, float b, float c, float d, float e) => Mathf.Min(Min(a, b, c, d), e);
        private static float Min(float a, float b, float c, float d, float e, float f) => Mathf.Min(Min(a, b, c, d), Mathf.Min(e, f));
        private static float Min(float a, float b, float c, float d, float e, float f, float g) => Mathf.Min(Min(a, b, c, d), Min(e, f, g));

        private static float Max(float a, float b) => Mathf.Max(a, b);

        private static float Line(Vector2 q, float ax, float ay, float bx, float by)
        {
            float dx = bx - ax, dy = by - ay;
            float k = Mathf.Clamp01(((q.x - ax) * dx + (q.y - ay) * dy) / (dx * dx + dy * dy));
            return new Vector2(ax + dx * k - q.x, ay + dy * k - q.y).magnitude - Stroke;
        }

        private static float Ring(Vector2 q, float cx, float cy, float r)
            => Mathf.Abs((q - V(cx, cy)).magnitude - r) - Stroke;

        private static float Disc(Vector2 q, float cx, float cy, float r) => (q - V(cx, cy)).magnitude - r;

        private static float Ellipse(Vector2 q, float cx, float cy, float rx, float ry)
            => (new Vector2((q.x - cx) / rx, (q.y - cy) / ry).magnitude - 1f) * Mathf.Min(rx, ry);

        // A rounded box's outline (or the whole box).
        private static float Box(Vector2 q, float x0, float y0, float x1, float y1, float r, bool filled)
        {
            float hx = (x1 - x0) / 2f - r, hy = (y1 - y0) / 2f - r;
            float qx = Mathf.Abs(q.x - (x0 + x1) / 2f) - hx, qy = Mathf.Abs(q.y - (y0 + y1) / 2f) - hy;
            float d = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
            return filled ? d : Mathf.Abs(d) - Stroke;
        }
    }
}
