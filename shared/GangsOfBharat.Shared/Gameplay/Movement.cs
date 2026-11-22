using System;
using GangsOfBharat.Shared.Serialization;

namespace GangsOfBharat.Shared.Gameplay
{
    public struct CharacterState
    {
        public float X;
        public float Y;
        public float Z;
        /// <summary>Radians; 0 faces +Z, positive turns towards +X (Unity convention).</summary>
        public float Yaw;
    }

    public struct VehicleState
    {
        public float X;
        public float Y;
        public float Z;
        public float Yaw;
        /// <summary>Signed forward speed in m/s.</summary>
        public float Speed;
    }

    /// <summary>
    /// Deterministic kinematics shared by the server simulation and client prediction. Same
    /// input + same state + same dt gives the same result on both sides; the client replays its
    /// unacked inputs on top of each server snapshot.
    /// </summary>
    public static class Movement
    {
        public const float WalkSpeed = 3.5f;
        public const float RunSpeed = 6.5f;
        public const float BrakeDeceleration = 10f;
        public const float CoastDeceleration = 1.5f;
        public const float ReverseFraction = 0.25f;
        /// <summary>Distance around a vehicle in which a player may enter it.</summary>
        public const float EnterVehicleRange = 5f;

        public static float SanitizeAxis(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v))
            {
                return 0f;
            }
            return Quantization.Clamp(v, -1f, 1f);
        }

        public static void StepCharacter(ref CharacterState s, float moveX, float moveZ, float yaw, bool sprint, float dt)
        {
            moveX = SanitizeAxis(moveX);
            moveZ = SanitizeAxis(moveZ);
            float lenSq = moveX * moveX + moveZ * moveZ;
            if (lenSq > 1f)
            {
                float inv = 1f / (float)Math.Sqrt(lenSq);
                moveX *= inv;
                moveZ *= inv;
                lenSq = 1f;
            }
            float speed = sprint ? RunSpeed : WalkSpeed;
            s.X = WorldBounds.ClampX(s.X + moveX * speed * dt);
            s.Z = WorldBounds.ClampZ(s.Z + moveZ * speed * dt);
            if (lenSq > 0.0001f)
            {
                s.Yaw = Quantization.WrapAngle((float)Math.Atan2(moveX, moveZ));
            }
            else
            {
                s.Yaw = Quantization.WrapAngle(yaw);
            }
        }

        public static void StepVehicle(ref VehicleState s, VehicleDef def, float throttle, float steer, bool brake, float dt)
        {
            throttle = SanitizeAxis(throttle);
            steer = SanitizeAxis(steer);
            float top = def.TopSpeedMetersPerSecond;
            float speed = s.Speed;

            if (brake)
            {
                speed = MoveTowardsZero(speed, BrakeDeceleration * dt);
            }
            else if (throttle > 0f)
            {
                if (speed < 0f)
                {
                    speed = MoveTowardsZero(speed, BrakeDeceleration * throttle * dt);
                }
                else
                {
                    speed += throttle * def.Acceleration * dt;
                }
            }
            else if (throttle < 0f)
            {
                if (speed > 0f)
                {
                    speed = MoveTowardsZero(speed, BrakeDeceleration * -throttle * dt);
                }
                else
                {
                    speed += throttle * def.Acceleration * 0.5f * dt;
                }
            }
            else
            {
                speed = MoveTowardsZero(speed, CoastDeceleration * dt);
            }
            speed = Quantization.Clamp(speed, -top * ReverseFraction, top);

            // Steering authority grows with speed up to 5 m/s; reversing flips the direction.
            float authority = Math.Abs(speed) / 5f;
            if (authority > 1f)
            {
                authority = 1f;
            }
            float direction = speed >= 0f ? 1f : -1f;
            s.Yaw = Quantization.WrapAngle(s.Yaw + steer * def.TurnRate * authority * direction * dt);

            float fx = (float)Math.Sin(s.Yaw);
            float fz = (float)Math.Cos(s.Yaw);
            float nx = s.X + fx * speed * dt;
            float nz = s.Z + fz * speed * dt;
            float cx = WorldBounds.ClampX(nx);
            float cz = WorldBounds.ClampZ(nz);
            if (cx != nx || cz != nz)
            {
                speed = 0f; // hit the edge of the map
            }
            s.X = cx;
            s.Z = cz;
            s.Speed = speed;
        }

        private static float MoveTowardsZero(float v, float amount)
        {
            if (v > 0f)
            {
                v -= amount;
                return v < 0f ? 0f : v;
            }
            if (v < 0f)
            {
                v += amount;
                return v > 0f ? 0f : v;
            }
            return 0f;
        }
    }

    /// <summary>Server-side checks against speed hacks and teleports.</summary>
    public static class MovementValidator
    {
        /// <summary>Allowed overshoot factor over the max speed (lag, quantization).</summary>
        public const float SpeedTolerance = 1.5f;
        /// <summary>Fixed slack in metres for jitter.</summary>
        public const float DistanceSlack = 1.0f;
        /// <summary>Any single jump longer than this is a teleport regardless of dt.</summary>
        public const float TeleportDistance = 50f;

        public static float MaxAllowedDistance(float maxSpeed, float dt)
        {
            return maxSpeed * SpeedTolerance * dt + DistanceSlack;
        }

        public static bool IsTeleport(float fromX, float fromZ, float toX, float toZ)
        {
            float dx = toX - fromX;
            float dz = toZ - fromZ;
            return dx * dx + dz * dz > TeleportDistance * TeleportDistance;
        }

        /// <summary>True if moving from -> to within dt is physically possible at maxSpeed.</summary>
        public static bool IsPlausibleMove(float fromX, float fromZ, float toX, float toZ, float dt, float maxSpeed)
        {
            if (float.IsNaN(toX) || float.IsNaN(toZ) || float.IsInfinity(toX) || float.IsInfinity(toZ))
            {
                return false;
            }
            if (IsTeleport(fromX, fromZ, toX, toZ))
            {
                return false;
            }
            float dx = toX - fromX;
            float dz = toZ - fromZ;
            float max = MaxAllowedDistance(maxSpeed, dt);
            return dx * dx + dz * dz <= max * max;
        }

        /// <summary>
        /// Compares the client's predicted position with the authoritative one. A large
        /// divergence means the client is not running the shared movement code (or is lagging).
        /// </summary>
        public static bool ClientDiverged(float serverX, float serverZ, float clientX, float clientZ, float threshold)
        {
            float dx = clientX - serverX;
            float dz = clientZ - serverZ;
            return dx * dx + dz * dz > threshold * threshold;
        }
    }
}
