using System;

namespace GangsOfBharat.Shared.Serialization
{
    /// <summary>Mayanagri world bounds used for position quantization.</summary>
    public static class WorldBounds
    {
        /// <summary>8 km x 8 km city (8192 m so the grid divides evenly).</summary>
        public const float Size = 8192f;
        public const float MinX = 0f;
        public const float MaxX = Size;
        public const float MinZ = 0f;
        public const float MaxZ = Size;
        /// <summary>Height range: sea floor to the tallest tower plus helicopters.</summary>
        public const float MinY = -128f;
        public const float MaxY = 896f;

        /// <summary>20 bits over 8192 m = 0.78 cm steps.</summary>
        public const int HorizontalBits = 20;
        /// <summary>17 bits over 1024 m = 0.78 cm steps.</summary>
        public const int VerticalBits = 17;

        public static float ClampX(float x)
        {
            return Quantization.Clamp(x, MinX, MaxX);
        }

        public static float ClampY(float y)
        {
            return Quantization.Clamp(y, MinY, MaxY);
        }

        public static float ClampZ(float z)
        {
            return Quantization.Clamp(z, MinZ, MaxZ);
        }
    }

    public static class Quantization
    {
        public const float TwoPi = (float)(Math.PI * 2.0);

        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }

        public static uint MaxValue(int bits)
        {
            return bits >= 32 ? uint.MaxValue : (1u << bits) - 1u;
        }

        public static uint Quantize(float value, float min, float max, int bits)
        {
            if (float.IsNaN(value))
            {
                value = min;
            }
            float clamped = Clamp(value, min, max);
            double normalized = (clamped - min) / (double)(max - min);
            uint maxQ = MaxValue(bits);
            return (uint)Math.Round(normalized * maxQ);
        }

        public static float Dequantize(uint q, float min, float max, int bits)
        {
            uint maxQ = MaxValue(bits);
            if (q > maxQ)
            {
                q = maxQ;
            }
            return (float)(min + (q / (double)maxQ) * (max - min));
        }

        /// <summary>Largest round-trip error for a quantized range.</summary>
        public static float MaxError(float min, float max, int bits)
        {
            return (float)((max - min) / (double)MaxValue(bits) * 0.5);
        }

        public static float WrapAngle(float radians)
        {
            if (float.IsNaN(radians) || float.IsInfinity(radians))
            {
                return 0f;
            }
            double a = radians % (Math.PI * 2.0);
            if (a < 0)
            {
                a += Math.PI * 2.0;
            }
            return (float)a;
        }

        public static uint QuantizeAngle(float radians, int bits)
        {
            double a = WrapAngle(radians);
            uint steps = 1u << bits;
            uint q = (uint)Math.Round(a / (Math.PI * 2.0) * steps);
            return q & (steps - 1u);
        }

        public static float DequantizeAngle(uint q, int bits)
        {
            uint steps = 1u << bits;
            return (float)((q & (steps - 1u)) * (Math.PI * 2.0) / steps);
        }

        /// <summary>Smallest absolute difference between two angles in radians.</summary>
        public static float AngleDelta(float a, float b)
        {
            double d = Math.Abs(WrapAngle(a) - WrapAngle(b));
            if (d > Math.PI)
            {
                d = Math.PI * 2.0 - d;
            }
            return (float)d;
        }
    }
}
