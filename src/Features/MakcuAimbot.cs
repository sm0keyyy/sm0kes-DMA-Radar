/*
 * Makcu Hardware Mouse Aimbot
 * Uses Makcu serial device for physical mouse movement
 */

using LoneEftDmaRadar.Config;
using LoneEftDmaRadar.DMA;
using LoneEftDmaRadar.Input;
using LoneEftDmaRadar.Tarkov.GameWorld;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using System;
using System.Linq;
using System.Threading;

namespace LoneEftDmaRadar.Features
{
    /// <summary>
    /// Makcu-based aimbot using hardware mouse movement.
    /// </summary>
    public sealed class MakcuAimbot
    {
        #region Singleton

        private static readonly object _lock = new object();
        private static MakcuAimbot _instance;

        public static MakcuAimbot Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new MakcuAimbot();
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Fields / Properties

        private Thread _aimbotThread;
        private bool _running = false;
        private bool _engaged = false;
        private bool _toggleState = false;

        /// <summary>
        /// Aimbot Configuration.
        /// </summary>
        public static AimbotConfig Config => App.Config.Aimbot;

        /// <summary>
        /// Currently locked target.
        /// </summary>
        public AbstractPlayer LockedTarget { get; private set; }

        /// <summary>
        /// Is aimbot currently engaged (activation key pressed/toggled).
        /// </summary>
        public bool IsEngaged => _engaged;

        /// <summary>
        /// Ballistics information for current weapon.
        /// </summary>
        private BallisticsInfo _ballistics = new BallisticsInfo();

        /// <summary>
        /// Smoothed mouse delta accumulator (for software smoothing).
        /// </summary>
        private Vector2 _smoothedDelta = Vector2.Zero;

        #endregion

        #region Lifecycle

        private MakcuAimbot()
        {
            // Private constructor for singleton
        }

        /// <summary>
        /// Initialize and start the aimbot system.
        /// </summary>
        public bool Initialize()
        {
            lock (_lock)
            {
                if (_running)
                {
                    Console.WriteLine("[MakcuAimbot] Already running.");
                    return true;
                }

                try
                {
                    // Initialize Makcu device
                    if (!MakcuManager.Initialize(Config.MakcuComPort))
                    {
                        Console.WriteLine("[MakcuAimbot] Failed to initialize Makcu device!");
                        return false;
                    }

                    // Start aimbot worker thread
                    _aimbotThread = new Thread(AimbotWorker)
                    {
                        IsBackground = true,
                        Priority = ThreadPriority.Highest,
                        Name = "MakcuAimbot"
                    };

                    _running = true;
                    _aimbotThread.Start();

                    Console.WriteLine("[MakcuAimbot] Initialized successfully.");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MakcuAimbot] Initialize error: {ex}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Stop the aimbot system.
        /// </summary>
        public void Shutdown()
        {
            lock (_lock)
            {
                _running = false;
                _engaged = false;
                LockedTarget = null;

                MakcuManager.Disconnect();

                Console.WriteLine("[MakcuAimbot] Shutdown complete.");
            }
        }

        #endregion

        #region Worker Thread

        /// <summary>
        /// Main aimbot worker thread.
        /// </summary>
        private void AimbotWorker()
        {
            Console.WriteLine("[MakcuAimbot] Worker thread starting...");

            while (_running)
            {
                try
                {
                    // Wait for game to start
                    if (!Memory.InRaid || Memory.LocalPlayer == null || Memory.Game == null)
                    {
                        ResetAimbot();
                        Thread.Sleep(200);
                        continue;
                    }

                    // Check if enabled
                    if (!Config.Enabled)
                    {
                        ResetAimbot();
                        Thread.Sleep(100);
                        continue;
                    }

                    // Check if Makcu is connected
                    if (!MakcuManager.IsConnected)
                    {
                        Console.WriteLine("[MakcuAimbot] Makcu disconnected, attempting reconnect...");
                        MakcuManager.Reconnect();
                        Thread.Sleep(1000);
                        continue;
                    }

                    // Update engagement state
                    UpdateEngagementState();

                    // Run aimbot if engaged
                    if (_engaged)
                    {
                        RunAimbot();
                    }
                    else
                    {
                        ResetTarget();
                    }

                    // Sleep based on engagement
                    Thread.Sleep(_engaged ? 8 : 50); // 8ms when active, 50ms when idle
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MakcuAimbot] Worker error: {ex}");
                    Thread.Sleep(200);
                }
            }

            Console.WriteLine("[MakcuAimbot] Worker thread stopped.");
        }

        #endregion

        #region Engagement Logic

        /// <summary>
        /// Update aimbot engagement state based on activation mode and key state.
        /// </summary>
        private void UpdateEngagementState()
        {
            try
            {
                bool keyPressed = CheckActivationKey();

                switch (Config.ActivationMode)
                {
                    case AimbotActivationMode.HoldKey:
                        _engaged = keyPressed;
                        break;

                    case AimbotActivationMode.Toggle:
                        if (keyPressed && !_toggleState)
                        {
                            _engaged = !_engaged;
                            Console.WriteLine($"[MakcuAimbot] Toggled {(_engaged ? "ON" : "OFF")}");
                        }
                        _toggleState = keyPressed;
                        break;

                    case AimbotActivationMode.AlwaysOn:
                        _engaged = true;
                        break;
                }

                // Check ADS requirement
                if (Config.RequireADS && _engaged)
                {
                    var localPlayer = Memory.LocalPlayer as ClientPlayer;
                    if (localPlayer == null || !localPlayer.IsAiming)
                    {
                        _engaged = false;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MakcuAimbot] UpdateEngagementState error: {ex}");
                _engaged = false;
            }
        }

        /// <summary>
        /// Check if activation key is pressed.
        /// </summary>
        private bool CheckActivationKey()
        {
            try
            {
                // Default to Mouse4 (XButton1)
                if (Config.ActivationKey == "XButton1" || Config.ActivationKey == "Mouse4")
                {
                    return MakcuManager.IsButtonPressed(MakcuMouseButton.mouse4);
                }
                else if (Config.ActivationKey == "XButton2" || Config.ActivationKey == "Mouse5")
                {
                    return MakcuManager.IsButtonPressed(MakcuMouseButton.mouse5);
                }
                else if (Config.ActivationKey == "MiddleButton")
                {
                    return MakcuManager.IsButtonPressed(MakcuMouseButton.Middle);
                }

                // TODO: Add keyboard key support via InputManager if needed
                return false;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Aimbot Execution

        /// <summary>
        /// Main aimbot execution logic.
        /// </summary>
        private void RunAimbot()
        {
            try
            {
                var localPlayer = Memory.LocalPlayer as ClientPlayer;
                if (localPlayer == null)
                    return;

                // Check if target is still valid
                if (LockedTarget != null && !IsTargetValid(LockedTarget))
                {
                    if (Config.DebugLogging)
                        Console.WriteLine("[MakcuAimbot] Target invalid, resetting.");
                    ResetTarget();
                }

                // Acquire new target if needed
                if (LockedTarget == null)
                {
                    if (Config.DisableReLock)
                    {
                        // Don't re-lock after first kill
                        _engaged = false;
                        return;
                    }

                    LockedTarget = GetBestTarget(localPlayer);

                    if (LockedTarget == null)
                    {
                        Thread.Sleep(10);
                        return;
                    }

                    if (Config.DebugLogging)
                        Console.WriteLine($"[MakcuAimbot] Locked onto: {LockedTarget.Name}");
                }

                // Aim at target
                AimAtTarget(localPlayer, LockedTarget);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MakcuAimbot] RunAimbot error: {ex}");
            }
        }

        /// <summary>
        /// Aim at the locked target using Makcu mouse movement.
        /// </summary>
        private void AimAtTarget(ClientPlayer localPlayer, AbstractPlayer target)
        {
            try
            {
                // Determine target bone
                Bones targetBone = GetTargetBone(target);

                // Get bone position
                if (!target.PlayerBones.TryGetValue(targetBone, out var boneTransform))
                {
                    if (Config.DebugLogging)
                        Console.WriteLine($"[MakcuAimbot] Bone {targetBone} not found!");
                    return;
                }

                Vector3 targetBonePos = boneTransform.Position;

                // Get local player info
                Vector3 localPos = localPlayer.Position;
                Vector2 currentRotation = localPlayer.Rotation;

                // Calculate predicted target position
                Vector3 predictedPos = CalculatePredictedPosition(localPlayer, target, targetBonePos);

                // Safe lock check - ensure target is still in FOV
                if (Config.SafeLock)
                {
                    float angleToTarget = CalculateAngleToPosition(localPos, currentRotation, predictedPos);
                    if (angleToTarget > Config.AimFOV)
                    {
                        if (Config.DebugLogging)
                            Console.WriteLine($"[MakcuAimbot] Target left FOV ({angleToTarget:F1}°), unlocking.");
                        ResetTarget();
                        return;
                    }
                }

                // Calculate aim angles
                Vector2 targetAngles = CalculateAimAngles(localPos, predictedPos);

                // Calculate angle delta
                Vector2 angleDelta = new Vector2(
                    NormalizeAngle(targetAngles.X - currentRotation.X),
                    targetAngles.Y - currentRotation.Y
                );

                // Convert to mouse delta
                Vector2 mouseDelta = AngleToMouseDelta(angleDelta, localPlayer.IsAiming);

                // Apply smoothing and move mouse
                ApplyMouseMovement(mouseDelta);

                if (Config.DebugLogging && Config.DebugLogging)
                {
                    Console.WriteLine($"[MakcuAimbot] Angle: {angleDelta.X:F2}°,{angleDelta.Y:F2}° | Mouse: {mouseDelta.X:F0},{mouseDelta.Y:F0}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MakcuAimbot] AimAtTarget error: {ex}");
            }
        }

        #endregion

        #region Target Selection

        /// <summary>
        /// Get the best target to aim at.
        /// </summary>
        private AbstractPlayer GetBestTarget(ClientPlayer localPlayer)
        {
            try
            {
                var game = Memory.Game as LocalGameWorld;
                if (game == null || game.Players == null)
                    return null;

                Vector3 localPos = localPlayer.Position;
                Vector2 localRot = localPlayer.Rotation;

                var validTargets = game.Players
                    .Where(p => IsValidTarget(p))
                    .Select(p => new
                    {
                        Player = p,
                        Distance = Vector3.Distance(localPos, p.Position),
                        FOV = CalculateAngleToPlayer(localPos, localRot, p)
                    })
                    .Where(t =>
                        t.Distance <= Config.MaxAimDistance &&
                        t.FOV <= Config.AimFOV)
                    .ToList();

                if (validTargets.Count == 0)
                    return null;

                // Select based on targeting mode
                return Config.TargetingMode switch
                {
                    AimbotTargetingMode.FOV => validTargets.MinBy(t => t.FOV)?.Player,
                    AimbotTargetingMode.CQB => validTargets.MinBy(t => t.Distance)?.Player,
                    _ => validTargets.MinBy(t => t.FOV)?.Player
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MakcuAimbot] GetBestTarget error: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Check if a player is a valid aimbot target.
        /// </summary>
        private bool IsValidTarget(AbstractPlayer player)
        {
            if (player == null)
                return false;

            // Not local player
            if (player is LocalPlayer)
                return false;

            // Alive check
            if (!player.IsAlive)
                return false;

            // Team check
            if (Config.ExcludeTeammates && player.IsFriendly)
                return false;

            // PMC only check
            if (Config.PMCOnly && !player.IsPmc)
                return false;

            return true;
        }

        /// <summary>
        /// Check if target is still valid (not dead).
        /// </summary>
        private bool IsTargetValid(AbstractPlayer target)
        {
            return target != null && target.IsAlive;
        }

        #endregion

        #region Calculations

        /// <summary>
        /// Calculate predicted target position with ballistics and movement.
        /// </summary>
        private Vector3 CalculatePredictedPosition(ClientPlayer localPlayer, AbstractPlayer target, Vector3 targetPos)
        {
            try
            {
                // Simple prediction without ballistics
                if (!Config.EnableBallistics)
                    return targetPos;

                Vector3 localPos = localPlayer.Position;

                // Run ballistics simulation
                var ballisticsResult = BallisticsHelper.RunSimple(
                    ref localPos,
                    ref targetPos,
                    _ballistics.BulletSpeed > 0 ? _ballistics.BulletSpeed : 800f
                );

                Vector3 predictedPos = targetPos;

                // Apply bullet drop compensation
                predictedPos.Z += ballisticsResult.DropCompensation;

                // Apply movement prediction
                if (Config.EnablePrediction && target is ObservedPlayer observedPlayer)
                {
                    // Get target velocity (if available from MovementContext)
                    // For now, use simple approach
                    // TODO: Read velocity from Memory if needed
                    Vector3 targetVelocity = Vector3.Zero;

                    if (targetVelocity.Length() < 25f) // Sanity check
                    {
                        predictedPos += targetVelocity * ballisticsResult.TravelTime;
                    }
                }

                return predictedPos;
            }
            catch
            {
                return targetPos; // Fallback to un-predicted position
            }
        }

        /// <summary>
        /// Calculate aim angles (yaw/pitch) from one position to another.
        /// </summary>
        private Vector2 CalculateAimAngles(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.Length();

            float yaw = MathF.Atan2(delta.Y, delta.X) * (180f / MathF.PI);
            float pitch = MathF.Asin(MathF.Max(-1f, MathF.Min(1f, delta.Z / distance))) * (180f / MathF.PI);

            return new Vector2(yaw, pitch);
        }

        /// <summary>
        /// Calculate angle from current view to a position.
        /// </summary>
        private float CalculateAngleToPosition(Vector3 from, Vector2 currentAngles, Vector3 to)
        {
            Vector2 targetAngles = CalculateAimAngles(from, to);

            float yawDelta = NormalizeAngle(targetAngles.X - currentAngles.X);
            float pitchDelta = targetAngles.Y - currentAngles.Y;

            return MathF.Sqrt(yawDelta * yawDelta + pitchDelta * pitchDelta);
        }

        /// <summary>
        /// Calculate angle from current view to a player (center mass).
        /// </summary>
        private float CalculateAngleToPlayer(Vector3 from, Vector2 currentAngles, AbstractPlayer player)
        {
            return CalculateAngleToPosition(from, currentAngles, player.Position);
        }

        /// <summary>
        /// Normalize angle to -180 to 180 range.
        /// </summary>
        private float NormalizeAngle(float angle)
        {
            while (angle > 180f) angle -= 360f;
            while (angle < -180f) angle += 360f;
            return angle;
        }

        /// <summary>
        /// Convert angle delta (degrees) to mouse movement (pixels).
        /// </summary>
        private Vector2 AngleToMouseDelta(Vector2 angleDelta, bool isADS)
        {
            // Base pixels per degree (will need calibration!)
            float pixelsPerDegree = Config.CalibrationMultiplier;

            // Sensitivity scaling
            float sensScale = 1.0f / (Config.MouseSensitivity * 0.5f);

            // ADS compensation
            if (isADS && Config.CompensateADS)
            {
                sensScale *= (1.0f / Config.ADSSensitivityMult);
            }

            // TODO: Add FOV scaling based on current camera FOV

            float multiplier = pixelsPerDegree * sensScale;

            return new Vector2(
                angleDelta.X * multiplier,
                -angleDelta.Y * multiplier // Invert Y (down is negative pitch, positive mouse Y)
            );
        }

        /// <summary>
        /// Apply mouse movement with smoothing.
        /// </summary>
        private void ApplyMouseMovement(Vector2 targetDelta)
        {
            try
            {
                if (Config.UseHardwareSmoothing)
                {
                    // Use Makcu hardware smoothing
                    MakcuManager.MoveMouseSmooth(
                        (int)targetDelta.X,
                        (int)targetDelta.Y,
                        Config.SmoothSegments
                    );
                }
                else
                {
                    // Software lerp smoothing
                    float smoothFactor = 1.0f - MathF.Max(0f, MathF.Min(1f, Config.SmoothingStrength));

                    Vector2 smoothedMove = new Vector2(
                        _smoothedDelta.X + (targetDelta.X - _smoothedDelta.X) * smoothFactor,
                        _smoothedDelta.Y + (targetDelta.Y - _smoothedDelta.Y) * smoothFactor
                    );

                    MakcuManager.MoveMouse((int)smoothedMove.X, (int)smoothedMove.Y);

                    _smoothedDelta = smoothedMove;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MakcuAimbot] ApplyMouseMovement error: {ex}");
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Determine which bone to target.
        /// </summary>
        private Bones GetTargetBone(AbstractPlayer target)
        {
            return Config.TargetBone switch
            {
                AimbotTargetBone.Head => Bones.HumanHead,
                AimbotTargetBone.Neck => Bones.HumanNeck,
                AimbotTargetBone.UpperChest => Bones.HumanSpine3,
                AimbotTargetBone.Chest => Bones.HumanSpine2,
                AimbotTargetBone.Pelvis => Bones.HumanPelvis,
                _ => Bones.HumanHead
            };
        }

        /// <summary>
        /// Reset aimbot state.
        /// </summary>
        private void ResetAimbot()
        {
            _engaged = false;
            ResetTarget();
        }

        /// <summary>
        /// Reset locked target.
        /// </summary>
        private void ResetTarget()
        {
            LockedTarget = null;
            _smoothedDelta = Vector2.Zero;
        }

        #endregion
    }
}
