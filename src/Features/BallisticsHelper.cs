/*
 * Simplified Ballistics Calculations for Aimbot
 */

using System;

namespace LoneEftDmaRadar.Features
{
    /// <summary>
    /// Ballistics information for a specific ammo/weapon combination.
    /// </summary>
    public class BallisticsInfo
    {
        public float BulletSpeed { get; set; } = 800f; // m/s
        public float BulletMassGrams { get; set; } = 10f;
        public float BulletDiameterMillimeters { get; set; } = 5.45f;
        public float BallisticCoefficient { get; set; } = 0.3f;

        public bool IsValid =>
            BulletSpeed > 0 && BulletSpeed < 2000 &&
            BulletMassGrams > 0 && BulletMassGrams < 100 &&
            BallisticCoefficient > 0 && BallisticCoefficient < 2;
    }

    /// <summary>
    /// Result of a ballistics simulation.
    /// </summary>
    public struct BallisticsResult
    {
        public float TravelTime { get; set; } // seconds
        public float DropCompensation { get; set; } // meters (vertical adjustment)
        public float Distance { get; set; } // meters
    }

    /// <summary>
    /// Simplified ballistics calculations for bullet prediction.
    /// </summary>
    public static class BallisticsHelper
    {
        private const float GRAVITY = 9.81f; // m/s²
        private const float AIR_DENSITY = 1.225f; // kg/m³ at sea level

        /// <summary>
        /// Run a simplified ballistics simulation.
        /// </summary>
        /// <param name="sourcePosition">Firing position.</param>
        /// <param name="targetPosition">Target position.</param>
        /// <param name="ballistics">Ballistics info.</param>
        /// <returns>Ballistics result with travel time and drop compensation.</returns>
        public static BallisticsResult Run(ref Vector3 sourcePosition, ref Vector3 targetPosition, BallisticsInfo ballistics)
        {
            if (ballistics == null || !ballistics.IsValid)
            {
                return new BallisticsResult
                {
                    TravelTime = 0,
                    DropCompensation = 0,
                    Distance = 0
                };
            }

            // Calculate distance
            Vector3 delta = targetPosition - sourcePosition;
            float distance = delta.Length();

            // Simple travel time estimate (ignoring drag for initial estimate)
            float travelTime = distance / ballistics.BulletSpeed;

            // Calculate bullet drop with simplified drag model
            float dropCompensation = CalculateDrop(distance, ballistics.BulletSpeed, travelTime, ballistics);

            return new BallisticsResult
            {
                TravelTime = travelTime,
                DropCompensation = dropCompensation,
                Distance = distance
            };
        }

        /// <summary>
        /// Calculate bullet drop over distance.
        /// Uses simplified drag model.
        /// </summary>
        private static float CalculateDrop(float distance, float velocity, float time, BallisticsInfo ballistics)
        {
            try
            {
                // Simplified drag coefficient calculation
                float crossSectionalArea = MathF.PI * MathF.Pow(ballistics.BulletDiameterMillimeters / 2000f, 2);
                float mass = ballistics.BulletMassGrams / 1000f; // Convert to kg

                // Simplified drag force calculation
                float dragCoefficient = 1.0f / ballistics.BallisticCoefficient;
                float dragFactor = (0.5f * AIR_DENSITY * crossSectionalArea * dragCoefficient) / mass;

                // Iterative calculation for more accurate drop
                float dt = 0.01f; // 10ms timesteps
                float verticalVelocity = 0f;
                float totalDrop = 0f;
                float currentTime = 0f;

                while (currentTime < time)
                {
                    // Gravity acceleration
                    verticalVelocity += GRAVITY * dt;

                    // Drag (simplified - affects vertical component)
                    float dragDeceleration = dragFactor * velocity * verticalVelocity;
                    verticalVelocity -= dragDeceleration * dt;

                    // Update drop
                    totalDrop += verticalVelocity * dt;

                    currentTime += dt;
                }

                return totalDrop;
            }
            catch
            {
                // Fallback to simple gravity-only calculation
                return 0.5f * GRAVITY * time * time;
            }
        }

        /// <summary>
        /// Simple ballistics calculation without drag (faster, less accurate).
        /// </summary>
        public static BallisticsResult RunSimple(ref Vector3 sourcePosition, ref Vector3 targetPosition, float bulletSpeed)
        {
            Vector3 delta = targetPosition - sourcePosition;
            float distance = delta.Length();
            float travelTime = distance / bulletSpeed;
            float drop = 0.5f * GRAVITY * travelTime * travelTime;

            return new BallisticsResult
            {
                TravelTime = travelTime,
                DropCompensation = drop,
                Distance = distance
            };
        }
    }
}
