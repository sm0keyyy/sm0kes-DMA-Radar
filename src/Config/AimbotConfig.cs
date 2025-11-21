/*
 * Aimbot Configuration Classes
 */

using System.ComponentModel;
using System.Text.Json.Serialization;

namespace LoneEftDmaRadar.Config
{
    /// <summary>
    /// Aimbot bone targeting options.
    /// </summary>
    public enum AimbotTargetBone
    {
        [Description("Head")]
        Head = 0,
        [Description("Neck")]
        Neck = 1,
        [Description("Upper Chest")]
        UpperChest = 2,
        [Description("Chest")]
        Chest = 3,
        [Description("Pelvis")]
        Pelvis = 4
    }

    /// <summary>
    /// Aimbot targeting mode.
    /// </summary>
    public enum AimbotTargetingMode
    {
        [Description("FOV (Closest to Crosshair)")]
        FOV = 0,
        [Description("CQB (Closest Distance)")]
        CQB = 1
    }

    /// <summary>
    /// Aimbot activation mode.
    /// </summary>
    public enum AimbotActivationMode
    {
        [Description("Hold Key")]
        HoldKey = 0,
        [Description("Toggle")]
        Toggle = 1,
        [Description("Always On")]
        AlwaysOn = 2
    }

    /// <summary>
    /// Configuration for Makcu-based Aimbot.
    /// </summary>
    public sealed class AimbotConfig
    {
        #region General Settings

        /// <summary>
        /// Enable/Disable Aimbot.
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Makcu COM port (empty for auto-detection).
        /// </summary>
        [JsonPropertyName("makcuComPort")]
        public string MakcuComPort { get; set; } = "";

        #endregion

        #region Targeting Settings

        /// <summary>
        /// Activation mode.
        /// </summary>
        [JsonPropertyName("activationMode")]
        public AimbotActivationMode ActivationMode { get; set; } = AimbotActivationMode.HoldKey;

        /// <summary>
        /// Activation key (for HoldKey/Toggle modes).
        /// Default: Mouse4 (XButton1)
        /// </summary>
        [JsonPropertyName("activationKey")]
        public string ActivationKey { get; set; } = "XButton1";

        /// <summary>
        /// Require ADS (Aim Down Sights) to activate.
        /// </summary>
        [JsonPropertyName("requireADS")]
        public bool RequireADS { get; set; } = false;

        /// <summary>
        /// Aim FOV (degrees) - only targets within this cone will be selected.
        /// </summary>
        [JsonPropertyName("aimFOV")]
        public float AimFOV { get; set; } = 20.0f;

        /// <summary>
        /// Maximum aim distance (meters).
        /// </summary>
        [JsonPropertyName("maxAimDistance")]
        public float MaxAimDistance { get; set; } = 200.0f;

        /// <summary>
        /// Preferred target bone.
        /// </summary>
        [JsonPropertyName("targetBone")]
        public AimbotTargetBone TargetBone { get; set; } = AimbotTargetBone.Head;

        /// <summary>
        /// Targeting mode (FOV vs Distance priority).
        /// </summary>
        [JsonPropertyName("targetingMode")]
        public AimbotTargetingMode TargetingMode { get; set; } = AimbotTargetingMode.FOV;

        /// <summary>
        /// Exclude teammates from targeting.
        /// </summary>
        [JsonPropertyName("excludeTeammates")]
        public bool ExcludeTeammates { get; set; } = true;

        /// <summary>
        /// Only target PMC players (ignore scavs/AI).
        /// </summary>
        [JsonPropertyName("pmcOnly")]
        public bool PMCOnly { get; set; } = false;

        /// <summary>
        /// Safe lock - disable aim if target leaves FOV.
        /// </summary>
        [JsonPropertyName("safeLock")]
        public bool SafeLock { get; set; } = true;

        /// <summary>
        /// Disable re-locking after first target kill.
        /// </summary>
        [JsonPropertyName("disableReLock")]
        public bool DisableReLock { get; set; } = false;

        #endregion

        #region Mouse Settings

        /// <summary>
        /// In-game mouse sensitivity value.
        /// </summary>
        [JsonPropertyName("mouseSensitivity")]
        public float MouseSensitivity { get; set; } = 0.5f;

        /// <summary>
        /// Smoothing strength (0.0 = instant snap, 1.0 = very slow).
        /// </summary>
        [JsonPropertyName("smoothingStrength")]
        public float SmoothingStrength { get; set; } = 0.4f;

        /// <summary>
        /// Use Makcu hardware smoothing (Bezier curves).
        /// </summary>
        [JsonPropertyName("useHardwareSmoothing")]
        public bool UseHardwareSmoothing { get; set; } = true;

        /// <summary>
        /// Number of smoothing segments for hardware smoothing (5-30).
        /// </summary>
        [JsonPropertyName("smoothSegments")]
        public int SmoothSegments { get; set; } = 15;

        /// <summary>
        /// Compensate for ADS sensitivity changes.
        /// </summary>
        [JsonPropertyName("compensateADS")]
        public bool CompensateADS { get; set; } = true;

        /// <summary>
        /// ADS sensitivity multiplier (typically 0.75).
        /// </summary>
        [JsonPropertyName("adsSensitivityMult")]
        public float ADSSensitivityMult { get; set; } = 0.75f;

        /// <summary>
        /// Calibration multiplier for angle-to-pixel conversion.
        /// Adjust this value during calibration.
        /// </summary>
        [JsonPropertyName("calibrationMultiplier")]
        public float CalibrationMultiplier { get; set; } = 10.0f;

        #endregion

        #region Ballistics Settings

        /// <summary>
        /// Enable ballistics prediction (bullet drop/travel time).
        /// </summary>
        [JsonPropertyName("enableBallistics")]
        public bool EnableBallistics { get; set; } = true;

        /// <summary>
        /// Enable lateral prediction (moving targets).
        /// </summary>
        [JsonPropertyName("enablePrediction")]
        public bool EnablePrediction { get; set; } = true;

        #endregion

        #region Visual Feedback

        /// <summary>
        /// Show aim FOV circle on ESP.
        /// </summary>
        [JsonPropertyName("showAimFOV")]
        public bool ShowAimFOV { get; set; } = true;

        /// <summary>
        /// Show locked target indicator.
        /// </summary>
        [JsonPropertyName("showTargetIndicator")]
        public bool ShowTargetIndicator { get; set; } = true;

        /// <summary>
        /// Show predicted aim point.
        /// </summary>
        [JsonPropertyName("showPredictedPoint")]
        public bool ShowPredictedPoint { get; set; } = false;

        #endregion

        #region Debug

        /// <summary>
        /// Enable debug logging.
        /// </summary>
        [JsonPropertyName("debugLogging")]
        public bool DebugLogging { get; set; } = false;

        #endregion
    }
}
