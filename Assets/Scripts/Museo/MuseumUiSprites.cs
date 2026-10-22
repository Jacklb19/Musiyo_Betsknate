using UnityEngine;

namespace MusiyoBetsknate.Museum
{
    /// <summary>
    /// Sprites the interface draws itself at runtime, so the menus need no textures: rounded cards, soft dots for slider
    /// handles, horizontal gradients for the shade of the title screen and a radial veil for the comfort vignette. Each
    /// one is made once and shared.
    /// </summary>
    public static class MuseumUiSprites
    {
        private static readonly System.Collections.Generic.Dictionary<string, Sprite> cache = new();

        /// <summary>A white rounded rectangle sliced at its corners, so it stretches to any size without distortion.</summary>
        public static Sprite Rounded(int radius)
        {
            radius = Mathf.Clamp(radius, 1, 64);
            string key = "rounded" + radius;
            if (cache.TryGetValue(key, out var found) && found != null) return found;
            int size = radius * 2 + 2;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = key, filterMode = FilterMode.Bilinear };
            var centre = new Vector2(size / 2f, size / 2f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Distance from the pixel centre to the inner rectangle (one pixel wide at the middle); antialiased edge.
                    var p = new Vector2(x + .5f, y + .5f) - centre;
                    var q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - new Vector2(1, 1);
                    float outside = Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) - radius;
                    float alpha = Mathf.Clamp01(.5f - outside);
                    texture.SetPixel(x, y, new Color(1, 1, 1, alpha));
                }
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
            cache[key] = sprite;
            return sprite;
        }

        /// <summary>A white disc with a soft antialiased edge, for slider handles and focus marks.</summary>
        public static Sprite Dot(int size = 32)
        {
            size = Mathf.Clamp(size, 8, 128);
            string key = "dot" + size;
            if (cache.TryGetValue(key, out var found) && found != null) return found;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = key, filterMode = FilterMode.Bilinear };
            float radius = size / 2f - 1;
            var centre = new Vector2(size / 2f, size / 2f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = (new Vector2(x + .5f, y + .5f) - centre).magnitude;
                    texture.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(radius - distance + .5f)));
                }
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
            cache[key] = sprite;
            return sprite;
        }

        /// <summary>A white sprite whose alpha follows the given (position, alpha) stops from left to right.</summary>
        public static Sprite Horizontal(string name, (float at, float alpha)[] stops)
        {
            string key = "gradient:" + name;
            if (cache.TryGetValue(key, out var found) && found != null) return found;
            const int width = 256;
            var texture = new Texture2D(width, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = key };
            for (int x = 0; x < width; x++) texture.SetPixel(x, 0, new Color(1, 1, 1, Sample(stops, x / (width - 1f))));
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, width, 1), new Vector2(.5f, .5f));
            cache[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// A square veil clear at its centre and dark at its edge, for the vignette that narrows the view while the
        /// visitor moves on a headset. <paramref name="clear"/> is the share of the half-width that stays fully open.
        /// </summary>
        public static Sprite Radial(float clear = .42f, int size = 128)
        {
            string key = "radial" + size + ":" + clear.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            if (cache.TryGetValue(key, out var found) && found != null) return found;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = key, filterMode = FilterMode.Bilinear };
            var centre = new Vector2(size / 2f, size / 2f);
            float half = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = (new Vector2(x + .5f, y + .5f) - centre).magnitude / half;
                    float alpha = Mathf.SmoothStep(0, 1, Mathf.Clamp01((distance - clear) / Mathf.Max(.01f, .95f - clear)));
                    texture.SetPixel(x, y, new Color(0, 0, 0, alpha));
                }
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
            cache[key] = sprite;
            return sprite;
        }

        /// <summary>Alpha of a gradient at <paramref name="t"/> (0 to 1), eased between its stops.</summary>
        public static float Sample((float at, float alpha)[] stops, float t)
        {
            if (stops == null || stops.Length == 0) return 1;
            if (t <= stops[0].at) return stops[0].alpha;
            for (int index = 1; index < stops.Length; index++)
                if (t >= stops[index - 1].at && t <= stops[index].at)
                {
                    float k = Mathf.SmoothStep(0, 1, (t - stops[index - 1].at) / Mathf.Max(.0001f, stops[index].at - stops[index - 1].at));
                    return Mathf.Lerp(stops[index - 1].alpha, stops[index].alpha, k);
                }
            return stops[stops.Length - 1].alpha;
        }
    }
}
