using UnityEngine;

namespace Tanks.World.Chunks
{
    /// <summary>
    /// Integer identifier of a terrain chunk. Two chunks with the same coordinates always describe
    /// the same slice of world, which is what makes the streaming / determinism contract work.
    /// </summary>
    public readonly struct ChunkCoordinate : System.IEquatable<ChunkCoordinate>
    {
        public readonly int X;
        public readonly int Z;

        public ChunkCoordinate(int x, int z)
        {
            X = x;
            Z = z;
        }

        /// <summary>Converts a world position into the coordinate of the chunk containing it.</summary>
        public static ChunkCoordinate FromWorldPosition(Vector3 worldPosition, float chunkSize)
        {
            return new ChunkCoordinate(
                Mathf.FloorToInt(worldPosition.x / chunkSize),
                Mathf.FloorToInt(worldPosition.z / chunkSize));
        }

        /// <summary>Converts a world position into the coordinate of the chunk containing it.</summary>
        public static ChunkCoordinate FromWorldXZ(float worldX, float worldZ, float chunkSize)
        {
            return new ChunkCoordinate(
                Mathf.FloorToInt(worldX / chunkSize),
                Mathf.FloorToInt(worldZ / chunkSize));
        }

        /// <summary>World space origin (min X, min Z corner) of this chunk.</summary>
        public Vector3 Origin(float chunkSize)
        {
            return new Vector3(X * chunkSize, 0f, Z * chunkSize);
        }

        /// <summary>Centre of this chunk in world space (X/Z only, Y left at zero).</summary>
        public Vector3 Center(float chunkSize)
        {
            return new Vector3((X + 0.5f) * chunkSize, 0f, (Z + 0.5f) * chunkSize);
        }

        /// <summary>Stable hash suitable for dictionary lookups and deterministic per chunk RNG.</summary>
        public int StableHash(int seed)
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + X;
                h = h * 31 + Z;
                h = h * 31 + seed;
                return h;
            }
        }

        public bool Equals(ChunkCoordinate other)
        {
            return X == other.X && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is ChunkCoordinate other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 397) ^ Z;
            }
        }

        public static bool operator ==(ChunkCoordinate a, ChunkCoordinate b)
        {
            return a.X == b.X && a.Z == b.Z;
        }

        public static bool operator !=(ChunkCoordinate a, ChunkCoordinate b)
        {
            return a.X != b.X || a.Z != b.Z;
        }

        public override string ToString()
        {
            return $"({X}, {Z})";
        }
    }
}
