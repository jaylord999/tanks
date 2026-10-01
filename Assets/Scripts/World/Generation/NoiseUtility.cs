using UnityEngine;

namespace Tanks.World.Generation
{
    /// <summary>
    /// Deterministic, allocation free value/gradient noise used by the procedural terrain.
    /// Everything here is a pure function of its inputs so that the same world seed and the same
    /// chunk coordinates always produce identical terrain on every device.
    ///
    /// This is intentionally self contained (no dependency on Unity.Mathematics or external noise
    /// packages) so the generator can be moved to a background thread later without changes.
    /// </summary>
    public static class NoiseUtility
    {
        private const float PerlinNorm = 1.41421356f; // 1 / 0.7071067812

        /// <summary>Hashes an integer pair plus a seed into a stable pseudo random uint.</summary>
        public static uint Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 374761393u;
                h += (uint)x * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h += (uint)y * 2246822519u;
                h = (h ^ (h >> 15)) * 3266489917u;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>Deterministic pseudo random value in [0,1) for an integer lattice point.</summary>
        public static float Hash01(int x, int y, int seed)
        {
            return Hash(x, y, seed) * (1f / 4294967296f);
        }

        /// <summary>Smooth (quintic) interpolation, keeps first and second derivatives continuous.</summary>
        public static float Smooth(float t)
        {
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        public static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        /// <summary>
        /// Perlin style gradient noise in [-1,1]. Cheap, smooth and fully deterministic.
        /// </summary>
        public static float Perlin(float x, float y, int seed)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float xf = x - xi;
            float yf = y - yi;

            float u = Smooth(xf);
            float v = Smooth(yf);

            float n00 = GradientDot(xi, yi, xf, yf, seed);
            float n10 = GradientDot(xi + 1, yi, xf - 1f, yf, seed);
            float n01 = GradientDot(xi, yi + 1, xf, yf - 1f, seed);
            float n11 = GradientDot(xi + 1, yi + 1, xf - 1f, yf - 1f, seed);

            float bottom = Lerp(n00, n10, u);
            float top = Lerp(n01, n11, u);
            return Lerp(bottom, top, v) * PerlinNorm;
        }

        private static float GradientDot(int x, int y, float dx, float dy, int seed)
        {
            // 16 evenly distributed unit-ish gradients picked deterministically per lattice point.
            uint h = Hash(x, y, seed) & 15u;
            float angle = h * (Mathf.PI * 2f / 16f);
            float gx = Mathf.Cos(angle);
            float gy = Mathf.Sin(angle);
            return gx * dx + gy * dy;
        }

        /// <summary>
        /// Fractal (fBm) noise in roughly [-1,1]. <paramref name="axisScale"/> multiplies the
        /// coordinates before sampling, so larger values mean higher frequency (smaller features).
        /// </summary>
        public static float Fbm(
            float x,
            float y,
            int octaves,
            float axisScale,
            float lacunarity,
            float persistence,
            int seed)
        {
            float sum = 0f;
            float amplitude = 1f;
            float totalAmplitude = 0f;
            float frequency = axisScale;

            for (int i = 0; i < octaves; i++)
            {
                sum += Perlin(x * frequency, y * frequency, seed + i * 1013) * amplitude;
                totalAmplitude += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return totalAmplitude > 0f ? sum / totalAmplitude : 0f;
        }

        /// <summary>
        /// Rotated, multi octave noise. Rotating each octave breaks the axis aligned grid look that
        /// plain Perlin can show on wide open terrain, which keeps the wasteland from looking tiled.
        /// Returns roughly [-1,1].
        /// </summary>
        public static float RidgedWasteland(
            float x,
            float y,
            int octaves,
            float axisScale,
            float lacunarity,
            float persistence,
            int seed)
        {
            float sum = 0f;
            float amplitude = 1f;
            float totalAmplitude = 0f;
            float frequency = axisScale;

            // 2D rotation matrices per octave (about 0.55 and 1.15 radians) - cheap and removes
            // the visible diagonal banding of single direction Perlin noise.
            const float cosA = 0.8525245f;
            const float sinA = 0.5226872f;
            const float cosB = 0.4084874f;
            const float sinB = 0.9127639f;

            for (int i = 0; i < octaves; i++)
            {
                float rx = x * cosA - y * sinA;
                float ry = x * sinA + y * cosA;
                if ((i & 1) == 1)
                {
                    float tx = x * cosB - y * sinB;
                    float ty = x * sinB + y * cosB;
                    rx = tx;
                    ry = ty;
                }

                sum += Perlin(rx * frequency, ry * frequency, seed + i * 7919) * amplitude;
                totalAmplitude += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return totalAmplitude > 0f ? sum / totalAmplitude : 0f;
        }
    }
}
