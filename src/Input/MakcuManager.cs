/*
 * Makcu Manager - Singleton wrapper for Makcu device
 */

using System;
using System.Numerics;

namespace LoneEftDmaRadar.Input
{
    /// <summary>
    /// Singleton manager for Makcu hardware mouse device.
    /// Handles initialization, connection management, and provides simplified API.
    /// </summary>
    public sealed class MakcuManager
    {
        private static readonly object _lock = new object();
        private static MakcuManager _instance;
        private static bool _initialized = false;
        private static string _lastComPort = "";

        /// <summary>
        /// Get the singleton instance.
        /// </summary>
        public static MakcuManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new MakcuManager();
                    }
                }
                return _instance;
            }
        }

        /// <summary>
        /// Returns true if Makcu device is connected and ready.
        /// </summary>
        public static bool IsConnected => Device.connected;

        /// <summary>
        /// Get the Makcu device version string.
        /// </summary>
        public static string Version => Device.version;

        private MakcuManager()
        {
            // Private constructor for singleton
        }

        /// <summary>
        /// Initialize Makcu device connection.
        /// </summary>
        /// <param name="comPort">Optional COM port (e.g., "COM7"). Leave empty for auto-detection.</param>
        /// <returns>True if connection successful.</returns>
        public static bool Initialize(string comPort = "")
        {
            lock (_lock)
            {
                try
                {
                    if (_initialized && IsConnected)
                    {
                        Console.WriteLine("[MakcuManager] Already initialized and connected.");
                        return true;
                    }

                    bool success;

                    if (string.IsNullOrWhiteSpace(comPort))
                    {
                        Console.WriteLine("[MakcuManager] Auto-detecting Makcu device...");
                        success = Device.AutoConnectMakcu();
                    }
                    else
                    {
                        Console.WriteLine($"[MakcuManager] Connecting to {comPort}...");
                        success = Device.MakcuConnect(comPort);
                        if (success)
                            _lastComPort = comPort;
                    }

                    if (success)
                    {
                        _initialized = true;
                        Console.WriteLine($"[MakcuManager] Initialized successfully. Version: {Version}");
                    }
                    else
                    {
                        Console.WriteLine("[MakcuManager] Failed to initialize Makcu device.");
                    }

                    return success;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MakcuManager] Initialize error: {ex}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Disconnect from Makcu device.
        /// </summary>
        public static void Disconnect()
        {
            lock (_lock)
            {
                try
                {
                    Device.Disconnect();
                    _initialized = false;
                    Console.WriteLine("[MakcuManager] Disconnected.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MakcuManager] Disconnect error: {ex}");
                }
            }
        }

        /// <summary>
        /// Attempt to reconnect to Makcu device.
        /// </summary>
        /// <returns>True if reconnection successful.</returns>
        public static bool Reconnect()
        {
            lock (_lock)
            {
                try
                {
                    Console.WriteLine("[MakcuManager] Attempting reconnection...");
                    Disconnect();
                    System.Threading.Thread.Sleep(200);
                    return Initialize(_lastComPort);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MakcuManager] Reconnect error: {ex}");
                    return false;
                }
            }
        }

        /// <summary>
        /// Move mouse by delta X/Y pixels instantly.
        /// </summary>
        /// <param name="deltaX">Horizontal movement (positive = right).</param>
        /// <param name="deltaY">Vertical movement (positive = down).</param>
        public static void MoveMouse(int deltaX, int deltaY)
        {
            if (!IsConnected)
            {
                Console.WriteLine("[MakcuManager] Cannot move mouse - not connected.");
                return;
            }

            try
            {
                Device.move(deltaX, deltaY);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MakcuManager] MoveMouse error: {ex}");
            }
        }

        /// <summary>
        /// Move mouse by delta X/Y pixels with smoothing.
        /// </summary>
        /// <param name="deltaX">Horizontal movement (positive = right).</param>
        /// <param name="deltaY">Vertical movement (positive = down).</param>
        /// <param name="segments">Number of smoothing segments (higher = smoother but slower).</param>
        public static void MoveMouseSmooth(int deltaX, int deltaY, int segments = 10)
        {
            if (!IsConnected)
            {
                Console.WriteLine("[MakcuManager] Cannot move mouse - not connected.");
                return;
            }

            try
            {
                Device.move_smooth(deltaX, deltaY, segments);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MakcuManager] MoveMouseSmooth error: {ex}");
            }
        }

        /// <summary>
        /// Move mouse with Vector2 (for convenience).
        /// </summary>
        public static void MoveMouse(Vector2 delta)
        {
            MoveMouse((int)delta.X, (int)delta.Y);
        }

        /// <summary>
        /// Move mouse with Vector2 smoothing.
        /// </summary>
        public static void MoveMouseSmooth(Vector2 delta, int segments = 10)
        {
            MoveMouseSmooth((int)delta.X, (int)delta.Y, segments);
        }

        /// <summary>
        /// Check if a specific mouse button is pressed.
        /// </summary>
        public static bool IsButtonPressed(MakcuMouseButton button)
        {
            return Device.button_pressed(button);
        }

        /// <summary>
        /// Get list of all available serial devices.
        /// </summary>
        public static System.Collections.Generic.List<Device.SerialDeviceInfo> EnumerateDevices()
        {
            return Device.EnumerateSerialDevices();
        }
    }
}
