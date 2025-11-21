/*
 * Advanced Ballistics Calculations for Aimbot
 * Uses EFT's actual physics constants and drag model
 */

using System;

namespace LoneEftDmaRadar.Features
{
    /// <summary>
    /// Ballistics information for a specific ammo/weapon combination.
    /// Mirrors EFT's Shot class ballistics properties.
    /// </summary>
    public class BallisticsInfo
    {
        #region EFT Constants

        /// <summary>
        /// Air density used by EFT (kg/m³).
        /// </summary>
        public const float AIR_DENSITY = 1.2f;  // EFT uses 1.2, not 1.225!

        /// <summary>
        /// Drag constant from EFT.
        /// </summary>
        public const float DRAG_CONSTANT = 0.00033f;

        /// <summary>
        /// Aerodynamic coefficient from EFT.
        /// </summary>
        public const float AERODYNAMIC_COEFFICIENT = 0.0014223f;

        /// <summary>
        /// Gravity acceleration (m/s²).
        /// </summary>
        public const float GRAVITY = 9.81f;

        /// <summary>
        /// Trajectory simulation timestep (seconds).
        /// EFT uses 10ms timesteps.
        /// </summary>
        public const float TRAJECTORY_STEP = 0.01f;

        #endregion

        #region Ammo Properties

        /// <summary>
        /// Initial bullet speed (m/s) after all weapon modifiers applied.
        /// </summary>
        public float BulletSpeed { get; set; } = 800f;

        /// <summary>
        /// Bullet mass in grams.
        /// </summary>
        public float BulletMassGrams { get; set; } = 10f;

        /// <summary>
        /// Bullet diameter in millimeters.
        /// </summary>
        public float BulletDiameterMillimeters { get; set; } = 5.45f;

        /// <summary>
        /// G1 Ballistic Coefficient.
        /// </summary>
        public float BallisticCoefficient { get; set; } = 0.3f;

        #endregion

        #region Computed Properties

        /// <summary>
        /// Cross-sectional area in m².
        /// </summary>
        public float CrossSectionalArea =>
            MathF.PI * MathF.Pow(BulletDiameterMillimeters / 2000f, 2);

        /// <summary>
        /// Mass in kilograms.
        /// </summary>
        public float MassKg => BulletMassGrams / 1000f;

        /// <summary>
        /// Pre-computed drag factor for performance.
        /// </summary>
        public float DragFactor =>
            (AIR_DENSITY * CrossSectionalArea * DRAG_CONSTANT) /
            (BallisticCoefficient * MassKg);

        #endregion

        #region Validation

        /// <summary>
        /// Returns true if ballistics data is valid and safe to use.
        /// </summary>
        public bool IsAmmoValid =>
            BulletSpeed > 100 && BulletSpeed < 2000 &&
            BulletMassGrams > 1 && BulletMassGrams < 100 &&
            BulletDiameterMillimeters > 2 && BulletDiameterMillimeters < 30 &&
            BallisticCoefficient > 0.05f && BallisticCoefficient < 2f;

        #endregion
    }

    /// <summary>
    /// Result of a ballistics simulation.
    /// </summary>
    public struct BallisticsResult
    {
        /// <summary>
        /// Total travel time to target (seconds).
        /// </summary>
        public float TravelTime { get; set; }

        /// <summary>
        /// Vertical drop compensation needed (meters).
        /// Positive value means bullet drops below target.
        /// </summary>
        public float DropCompensation { get; set; }

        /// <summary>
        /// Horizontal distance to target (meters).
        /// </summary>
        public float Distance { get; set; }

        /// <summary>
        /// Final bullet velocity at target (m/s).
        /// </summary>
        public float FinalVelocity { get; set; }
    }

    /// <summary>
    /// Advanced ballistics simulation matching EFT's physics engine.
    /// </summary>
    public static class BallisticsHelper
    {
        /// <summary>
        /// Run advanced ballistics simulation using EFT's physics model.
        /// Iteratively simulates bullet trajectory with drag and gravity.
        /// </summary>
        /// <param name="sourcePosition">Firing position (fireport).</param>
        /// <param name="targetPosition">Target position.</param>
        /// <param name="ballistics">Ballistics info for ammo/weapon.</param>
        /// <returns>Ballistics result with travel time and drop compensation.</returns>
        public static BallisticsResult Run(ref Vector3 sourcePosition, ref Vector3 targetPosition, BallisticsInfo ballistics)
        {
            if (ballistics == null || !ballistics.IsAmmoValid)
            {
                // Return zero result for invalid ballistics
                return new BallisticsResult
                {
                    TravelTime = 0,
                    DropCompensation = 0,
                    Distance = 0,
                    FinalVelocity = 0
                };
            }

            try
            {
                // Calculate initial direction and distance
                Vector3 delta = targetPosition - sourcePosition;
                Vector3 direction = Vector3.Normalize(delta);
                float targetDistance = delta.Length();

                // Calculate horizontal distance (ignore Z/vertical)
                Vector3 horizontalDelta = new Vector3(delta.X, delta.Y, 0);
                float horizontalDistance = horizontalDelta.Length();

                // Initialize physics simulation
                Vector3 position = sourcePosition;
                Vector3 velocity = direction * ballistics.BulletSpeed;
                float time = 0f;
                float maxTime = 10f; // Safety limit: 10 seconds max

                // Pre-calculate drag factor for performance
                float dragFactor = ballistics.DragFactor;

                // Iterative physics simulation with 10ms timesteps
                while (time < maxTime)
                {
                    // Check if we've reached the target's horizontal distance
                    Vector3 currentHorizontalPos = new Vector3(position.X, position.Y, sourcePosition.Z);
                    Vector3 targetHorizontalPos = new Vector3(targetPosition.X, targetPosition.Y, sourcePosition.Z);
                    float currentHorizontalDist = Vector3.Distance(currentHorizontalPos, targetHorizontalPos);

                    if (currentHorizontalDist >= horizontalDistance)
                    {
                        // We've reached target distance
                        break;
                    }

                    // Calculate drag force (velocity-dependent)
                    float velocityMagnitude = velocity.Length();

                    // Drag acceleration = -dragFactor * velocity^2 * velocity_normalized
                    Vector3 dragAcceleration = -Vector3.Normalize(velocity) * dragFactor * velocityMagnitude * velocityMagnitude;

                    // Gravity acceleration (only vertical component)
                    Vector3 gravityAcceleration = new Vector3(0, 0, -BallisticsInfo.GRAVITY);

                    // Total acceleration
                    Vector3 totalAcceleration = gravityAcceleration + dragAcceleration;

                    // Update velocity (Euler integration)
                    velocity += totalAcceleration * BallisticsInfo.TRAJECTORY_STEP;

                    // Update position
                    position += velocity * BallisticsInfo.TRAJECTORY_STEP;

                    // Increment time
                    time += BallisticsInfo.TRAJECTORY_STEP;
                }

                // Calculate drop compensation
                // Positive = bullet dropped below target (need to aim higher)
                float dropCompensation = targetPosition.Z - position.Z;

                // Final velocity
                float finalVelocity = velocity.Length();

                return new BallisticsResult
                {
                    TravelTime = time,
                    DropCompensation = dropCompensation,
                    Distance = targetDistance,
                    FinalVelocity = finalVelocity
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Ballistics] Simulation error: {ex.Message}");

                // Fallback to simple calculation
                return RunSimple(ref sourcePosition, ref targetPosition, ballistics.BulletSpeed);
            }
        }

        /// <summary>
        /// Simple ballistics calculation without drag (fallback).
        /// Uses only gravity, no air resistance.
        /// </summary>
        /// <param name="sourcePosition">Firing position.</param>
        /// <param name="targetPosition">Target position.</param>
        /// <param name="bulletSpeed">Bullet speed (m/s).</param>
        /// <returns>Simple ballistics result.</returns>
        public static BallisticsResult RunSimple(ref Vector3 sourcePosition, ref Vector3 targetPosition, float bulletSpeed)
        {
            try
            {
                Vector3 delta = targetPosition - sourcePosition;
                float distance = delta.Length();

                // Simple travel time (assumes constant velocity)
                float travelTime = distance / bulletSpeed;

                // Simple gravity drop (no drag)
                float drop = 0.5f * BallisticsInfo.GRAVITY * travelTime * travelTime;

                return new BallisticsResult
                {
                    TravelTime = travelTime,
                    DropCompensation = drop,
                    Distance = distance,
                    FinalVelocity = bulletSpeed
                };
            }
            catch
            {
                return new BallisticsResult
                {
                    TravelTime = 0,
                    DropCompensation = 0,
                    Distance = 0,
                    FinalVelocity = 0
                };
            }
        }
    }
}
