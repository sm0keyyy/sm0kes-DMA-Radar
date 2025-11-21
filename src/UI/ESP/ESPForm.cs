using LoneEftDmaRadar.Misc;
using LoneEftDmaRadar.Tarkov.GameWorld;
using LoneEftDmaRadar.Tarkov.GameWorld.Exits;
using LoneEftDmaRadar.Tarkov.GameWorld.Loot;
using LoneEftDmaRadar.Tarkov.GameWorld.Player;
using LoneEftDmaRadar.Tarkov.GameWorld.Player.Helpers;
using LoneEftDmaRadar.Tarkov.Unity.Structures;
using LoneEftDmaRadar.UI.Skia;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace LoneEftDmaRadar.UI.ESP
{
    public partial class ESPForm : Form
    {
        #region Fields/Properties

        // Performance constants
        private const int MAX_SKELETON_BONES = 32;
        private const float W_COMPONENT_THRESHOLD = 0.098f;
        private const float SCREEN_MARGIN = 200f;
        private const float PLAYER_NAME_OFFSET_Y = 20f;
        private const float LOOT_TEXT_OFFSET_X = 4f;
        private const float LOOT_TEXT_OFFSET_Y = 4f;
        private const float LOOT_CIRCLE_RADIUS = 2f;
        private const float EXFIL_CIRCLE_RADIUS = 4f;
        private const float BOX_PADDING = 2f;
        private const float MAX_FADE_DISTANCE = 300f;
        private const float MAX_ALPHA_REDUCTION = 0.7f;
        private const float DISTANCE_SCALING_FACTOR = 200f;
        private const float BASE_SKELETON_WIDTH = 1.5f;
        private const float BASE_BOX_WIDTH = 1.0f;

        public static bool ShowESP { get; set; } = true;

        private readonly System.Diagnostics.Stopwatch _fpsSw = new();
        private readonly PrecisionTimer _renderTimer;
        private int _fpsCounter;
        private int _fps;

        // Thread safety
        private volatile bool _espIsRendering = false;

        // GPU-accelerated control
        private SKGLControl _skglControl;

        // Cached Fonts/Paints
        private readonly SKFont _textFont;
        private readonly SKPaint _textPaint;
        private readonly SKPaint _skeletonPaint;
        private readonly SKPaint _boxPaint;
        private readonly SKPaint _lootPaint;
        private readonly SKFont _lootTextFont;
        private readonly SKPaint _lootTextPaint;
        private readonly SKPaint _crosshairPaint;
        private readonly SKFont _notShownFont;
        private readonly SKPaint _notShownPaint;
        private readonly SKFont _fpsFont;
        private readonly SKPaint _fpsPaint;

        // Pre-allocated buffers (zero allocation rendering)
        private readonly Dictionary<string, float> _textWidthCache = new(256);

        private Vector3 _camPos;
        private bool _isFullscreen;
        private readonly CameraManager _cameraManager = new();

        // View matrix cache
        private TransposedViewMatrix _transposedViewMatrix = new();

        /// <summary>
        /// LocalPlayer (who is running Radar) 'Player' object.
        /// </summary>
        private static LocalPlayer LocalPlayer => Memory.LocalPlayer;

        /// <summary>
        /// All Players in Local Game World (including dead/exfil'd) 'Player' collection.
        /// </summary>
        private static IReadOnlyCollection<AbstractPlayer> AllPlayers => Memory.Players;

        private static IReadOnlyCollection<IExitPoint> Exits => Memory.Exits;

        private static bool InRaid => Memory.InRaid;

        // Bone Connections for Skeleton
        private static readonly (Bones From, Bones To)[] _boneConnections = new[]
        {
            (Bones.HumanHead, Bones.HumanNeck),
            (Bones.HumanNeck, Bones.HumanSpine3),
            (Bones.HumanSpine3, Bones.HumanSpine2),
            (Bones.HumanSpine2, Bones.HumanSpine1),
            (Bones.HumanSpine1, Bones.HumanPelvis),

            // Left Arm
            (Bones.HumanNeck, Bones.HumanLUpperarm),
            (Bones.HumanLUpperarm, Bones.HumanLForearm1),
            (Bones.HumanLForearm1, Bones.HumanLForearm2),
            (Bones.HumanLForearm2, Bones.HumanLPalm),

            // Right Arm
            (Bones.HumanNeck, Bones.HumanRUpperarm),
            (Bones.HumanRUpperarm, Bones.HumanRForearm1),
            (Bones.HumanRForearm1, Bones.HumanRForearm2),
            (Bones.HumanRForearm2, Bones.HumanRPalm),

            // Left Leg
            (Bones.HumanPelvis, Bones.HumanLThigh1),
            (Bones.HumanLThigh1, Bones.HumanLThigh2),
            (Bones.HumanLThigh2, Bones.HumanLCalf),
            (Bones.HumanLCalf, Bones.HumanLFoot),

            // Right Leg
            (Bones.HumanPelvis, Bones.HumanRThigh1),
            (Bones.HumanRThigh1, Bones.HumanRThigh2),
            (Bones.HumanRThigh2, Bones.HumanRCalf),
            (Bones.HumanRCalf, Bones.HumanRFoot),
        };

        // Config cache struct to avoid repeated property access
        private readonly struct ESPConfigCache
        {
            public readonly bool LootEnabled;
            public readonly bool EspLoot;
            public readonly float EspLootMaxDistance;
            public readonly bool EspCorpses;
            public readonly bool EspContainers;
            public readonly bool EspFood;
            public readonly bool EspMeds;
            public readonly bool EspBackpacks;
            public readonly bool EspLootConeEnabled;
            public readonly float EspLootConeAngle;
            public readonly float EspLootConeMaxDistance;
            public readonly bool EspLootPrice;
            public readonly float FOV;
            public readonly bool EspExfils;
            public readonly bool EspCrosshair;
            public readonly float EspCrosshairLength;
            public readonly float EspPlayerMaxDistance;
            public readonly float EspAIMaxDistance;
            public readonly float MaxDistance;
            public readonly bool EspPlayerSkeletons;
            public readonly bool EspPlayerBoxes;
            public readonly bool EspPlayerNames;
            public readonly bool EspPlayerHeadCircles;
            public readonly float EspPlayerHeadCircleSize;
            // public readonly bool EspPlayerHealthBars; // TODO: Add to EftDmaConfig.cs when health data is available
            public readonly bool EspAISkeletons;
            public readonly bool EspAIBoxes;
            public readonly bool EspAINames;
            public readonly bool EspAIHeadCircles;
            public readonly float EspAIHeadCircleSize;
            // public readonly bool EspAIHealthBars; // TODO: Add to EftDmaConfig.cs when health data is available
            public readonly bool EspTextOutlines;
            public readonly bool EspCornerBoxes;
            public readonly float EspCornerLength;
            public readonly bool EspDistanceFading;
            public readonly bool EspDistanceScaling;

            public ESPConfigCache()
            {
                LootEnabled = App.Config.Loot.Enabled;
                EspLoot = App.Config.UI.EspLoot;
                EspLootMaxDistance = App.Config.UI.EspLootMaxDistance;
                EspCorpses = App.Config.UI.EspCorpses;
                EspContainers = App.Config.UI.EspContainers;
                EspFood = App.Config.UI.EspFood;
                EspMeds = App.Config.UI.EspMeds;
                EspBackpacks = App.Config.UI.EspBackpacks;
                EspLootConeEnabled = App.Config.UI.EspLootConeEnabled;
                EspLootConeAngle = App.Config.UI.EspLootConeAngle;
                EspLootConeMaxDistance = App.Config.UI.EspLootConeMaxDistance;
                EspLootPrice = App.Config.UI.EspLootPrice;
                FOV = App.Config.UI.FOV;
                EspExfils = App.Config.UI.EspExfils;
                EspCrosshair = App.Config.UI.EspCrosshair;
                EspCrosshairLength = App.Config.UI.EspCrosshairLength;
                EspPlayerMaxDistance = App.Config.UI.EspPlayerMaxDistance;
                EspAIMaxDistance = App.Config.UI.EspAIMaxDistance;
                MaxDistance = App.Config.UI.MaxDistance;
                EspPlayerSkeletons = App.Config.UI.EspPlayerSkeletons;
                EspPlayerBoxes = App.Config.UI.EspPlayerBoxes;
                EspPlayerNames = App.Config.UI.EspPlayerNames;
                EspPlayerHeadCircles = App.Config.UI.EspPlayerHeadCircles;
                EspPlayerHeadCircleSize = App.Config.UI.EspPlayerHeadCircleSize;
                // EspPlayerHealthBars = App.Config.UI.EspPlayerHealthBars; // TODO: Add to EftDmaConfig.cs
                EspAISkeletons = App.Config.UI.EspAISkeletons;
                EspAIBoxes = App.Config.UI.EspAIBoxes;
                EspAINames = App.Config.UI.EspAINames;
                EspAIHeadCircles = App.Config.UI.EspAIHeadCircles;
                EspAIHeadCircleSize = App.Config.UI.EspAIHeadCircleSize;
                // EspAIHealthBars = App.Config.UI.EspAIHealthBars; // TODO: Add to EftDmaConfig.cs
                EspTextOutlines = App.Config.UI.EspTextOutlines;
                EspCornerBoxes = App.Config.UI.EspCornerBoxes;
                EspCornerLength = App.Config.UI.EspCornerLength;
                EspDistanceFading = App.Config.UI.EspDistanceFading;
                EspDistanceScaling = App.Config.UI.EspDistanceScaling;
            }
        }

        #endregion

        #region Constructor/Initialization

        public ESPForm()
        {
            InitializeComponent();
            CameraManager.TryInitialize();

            // Initialize SKGLControl (GPU-accelerated)
            _skglControl = new SKGLControl
            {
                Name = "skglControl_ESP",
                BackColor = System.Drawing.Color.Black,
                Dock = DockStyle.Fill,
                Location = new System.Drawing.Point(0, 0),
                Size = new System.Drawing.Size(800, 600),
                TabIndex = 0
            };

            // Adaptive VSync
            int maxFPS = App.Config.UI.EspMaxFPS;
            _skglControl.VSync = maxFPS > 0 && maxFPS <= 60;

            this.Controls.Add(_skglControl);

            // Window properties
            this.Text = "ESP Overlay";
            this.ClientSize = new System.Drawing.Size(800, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.BackColor = System.Drawing.Color.Black;

            // Cache all fonts/paints
            _textFont = new SKFont
            {
                Size = 12,
                Edging = SKFontEdging.Antialias
            };

            _textPaint = new SKPaint
            {
                Color = SKColors.White,
                Style = SKPaintStyle.Fill
            };

            _skeletonPaint = new SKPaint
            {
                Color = SKColors.White,
                StrokeWidth = 1.5f,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke
            };

            _boxPaint = new SKPaint
            {
                Color = SKColors.White,
                StrokeWidth = 1.0f,
                IsAntialias = false,
                Style = SKPaintStyle.Stroke
            };

            _lootPaint = new SKPaint
            {
                Color = SKColors.LightGray,
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            _lootTextFont = new SKFont
            {
                Size = 10,
                Edging = SKFontEdging.Antialias
            };

            _lootTextPaint = new SKPaint
            {
                Color = SKColors.Silver,
                Style = SKPaintStyle.Fill
            };

            _crosshairPaint = new SKPaint
            {
                Color = SKColors.White,
                StrokeWidth = 1.5f,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke
            };

            _notShownFont = new SKFont
            {
                Size = 24,
                Edging = SKFontEdging.Antialias
            };

            _notShownPaint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true
            };

            _fpsFont = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold))
            {
                Size = 10,
                Edging = SKFontEdging.Antialias
            };

            _fpsPaint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true
            };

            _fpsSw.Start();

            // Setup precision timer
            int fps = App.Config.UI.EspMaxFPS;
            var interval = fps == 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(1000.0 / fps);
            _renderTimer = new PrecisionTimer(interval);

            // Event handlers
            this.Shown += ESPForm_Shown;
            this.MouseDoubleClick += ESPForm_MouseDoubleClick;
            _skglControl.MouseDown += ESPForm_MouseDown;
        }

        private async void ESPForm_Shown(object sender, EventArgs e)
        {
            // Wait for handle creation
            while (!this.IsHandleCreated || !_skglControl.IsHandleCreated)
                await Task.Delay(25);

            _skglControl.PaintSurface += ESPForm_PaintSurface;
            _renderTimer.Elapsed += RenderTimer_Elapsed;

            // Optimize GPU context
            OptimizeGRContext();
        }

        private void OptimizeGRContext()
        {
            try
            {
                var grContext = _skglControl.GRContext;
                if (grContext != null)
                {
                    // 256MB GPU cache for optimal performance
                    grContext.SetResourceCacheLimit(256 * 1024 * 1024);
                    grContext.Flush();
                    System.Diagnostics.Debug.WriteLine("ESP: OpenGL GRContext optimized successfully");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ESP: Failed to optimize GRContext: {ex.Message}");
            }
        }

        #endregion

        #region Render Loop

        private void RenderTimer_Elapsed(object sender, EventArgs e)
        {
            if (_espIsRendering || this.IsDisposed) return;

            try
            {
                this.BeginInvoke(new Action(() =>
                {
                    if (_espIsRendering || this.IsDisposed) return;

                    _espIsRendering = true;
                    try
                    {
                        _skglControl?.Invalidate();
                    }
                    finally
                    {
                        _espIsRendering = false;
                    }
                }));
            }
            catch
            {
                _espIsRendering = false;
            }
        }

        #endregion

        #region Rendering Methods

        /// <summary>
        /// Record the Rendering FPS.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetFPS()
        {
            if (_fpsSw.ElapsedMilliseconds >= 1000)
            {
                _fps = System.Threading.Interlocked.Exchange(ref _fpsCounter, 0);
                _fpsSw.Restart();
            }
            else
            {
                _fpsCounter++;
            }
        }

        private bool _lastInRaidState = false;

        /// <summary>
        /// Main ESP Render Event.
        /// </summary>
        private void ESPForm_PaintSurface(object sender, SKPaintGLSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            SetFPS();

            // Clear with black background
            canvas.Clear(SKColors.Black);

            try
            {
                // Detect raid state changes and reset camera/state when leaving raid
                if (_lastInRaidState && !InRaid)
                {
                    CameraManager.Reset();
                    _transposedViewMatrix = new TransposedViewMatrix();
                    _camPos = Vector3.Zero;
                    System.Diagnostics.Debug.WriteLine("ESP: Detected raid end - reset all state");
                }
                _lastInRaidState = InRaid;

                if (!InRaid)
                    return;

                var localPlayer = LocalPlayer;
                var allPlayers = AllPlayers;

                if (localPlayer is not null && allPlayers is not null)
                {
                    if (!ShowESP)
                    {
                        DrawNotShown(canvas, e.Info.Width, e.Info.Height);
                    }
                    else
                    {
                        // Cache config values once per frame for performance
                        var cfg = new ESPConfigCache();

                        _cameraManager.Update(localPlayer);
                        UpdateCameraPositionFromMatrix();

                        // Render Loot (background layer)
                        if (cfg.LootEnabled && cfg.EspLoot)
                        {
                            DrawLoot(canvas, e.Info.Width, e.Info.Height, in cfg);
                        }

                        // Render Exfils
                        if (Exits is not null && cfg.EspExfils)
                        {
                            foreach (var exit in Exits)
                            {
                                if (exit is Exfil exfil && exfil.Status != Exfil.EStatus.Closed)
                                {
                                     if (WorldToScreen2(exfil.Position, out var screen, e.Info.Width, e.Info.Height))
                                     {
                                         var paint = exfil.Status switch
                                         {
                                             Exfil.EStatus.Open => SKPaints.PaintExfilOpen,
                                             Exfil.EStatus.Pending => SKPaints.PaintExfilPending,
                                             _ => SKPaints.PaintExfilOpen
                                         };

                                         canvas.DrawCircle(screen, EXFIL_CIRCLE_RADIUS, paint);
                                         canvas.DrawText(exfil.Name, screen.X + 6, screen.Y + 4, _textFont, SKPaints.TextExfil);
                                     }
                                }
                            }
                        }

                        // Render players
                        foreach (var player in allPlayers)
                        {
                            DrawPlayerESP(canvas, player, localPlayer, e.Info.Width, e.Info.Height, in cfg);
                        }

                        if (cfg.EspCrosshair)
                        {
                            DrawCrosshair(canvas, e.Info.Width, e.Info.Height, cfg.EspCrosshairLength);
                        }

                        DrawFPS(canvas, e.Info.Width, e.Info.Height);
                    }
                }

                // Flush GPU operations
                canvas.Flush();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ESP RENDER ERROR: {ex}");
            }
        }

        private void DrawLoot(SKCanvas canvas, float screenWidth, float screenHeight, in ESPConfigCache cfg)
        {
            var lootItems = Memory.Game?.Loot?.FilteredLoot;
            if (lootItems is null) return;

            // Pre-calculate cone filter values once
            float centerX = screenWidth / 2f;
            float centerY = screenHeight / 2f;
            float halfFov = cfg.FOV / 2f;
            float invCenterX = 1f / centerX;
            float invCenterY = 1f / centerY;
            float coneAngleSq = cfg.EspLootConeAngle * cfg.EspLootConeAngle;
            bool coneEnabled = cfg.EspLootConeEnabled && cfg.EspLootConeAngle > 0f;
            float maxDistSq = cfg.EspLootMaxDistance * cfg.EspLootMaxDistance;
            float coneMaxDistSq = cfg.EspLootConeMaxDistance * cfg.EspLootConeMaxDistance;
            bool coneDistanceRestricted = cfg.EspLootConeMaxDistance > 0f;

            foreach (var item in lootItems)
            {
                // OPTIMIZATION: Check squared distance FIRST before any other checks (avoids sqrt)
                float distSq = Vector3.DistanceSquared(_camPos, item.Position);
                if (cfg.EspLootMaxDistance > 0 && distSq > maxDistSq)
                    continue;

                // Filter based on ESP settings
                bool isCorpse = item is LootCorpse;
                if (isCorpse && !cfg.EspCorpses)
                    continue;

                bool isContainer = item is LootContainer;
                if (isContainer && !cfg.EspContainers)
                    continue;

                bool isFood = item.IsFood;
                bool isMeds = item.IsMeds;
                bool isBackpack = item.IsBackpack;

                if (isFood && !cfg.EspFood)
                    continue;
                if (isMeds && !cfg.EspMeds)
                    continue;
                if (isBackpack && !cfg.EspBackpacks)
                    continue;

                if (WorldToScreen2(item.Position, out var screen, screenWidth, screenHeight))
                {
                     // Calculate cone filter with optimized math
                     bool inCone = true;

                     if (coneEnabled)
                     {
                         // Only apply cone filter if item is within cone max distance (or unlimited)
                         bool withinConeDistance = !coneDistanceRestricted || distSq <= coneMaxDistSq;

                         if (withinConeDistance)
                         {
                             float dx = screen.X - centerX;
                             float dy = screen.Y - centerY;
                             float screenAngleX = MathF.Abs(dx * invCenterX) * halfFov;
                             float screenAngleY = MathF.Abs(dy * invCenterY) * halfFov;
                             float screenAngleSq = screenAngleX * screenAngleX + screenAngleY * screenAngleY;
                             inCone = screenAngleSq <= coneAngleSq;  // Avoid sqrt
                         }
                         else
                         {
                             inCone = false; // Outside cone distance restriction
                         }
                     }

                     var (circlePaint, textPaint) = item switch
                     {
                         { Important: true } => (SKPaints.PaintFilteredLoot, SKPaints.TextFilteredLoot),
                         { IsValuableLoot: true } => (SKPaints.PaintImportantLoot, SKPaints.TextImportantLoot),
                         { IsBackpack: true } => (SKPaints.PaintBackpacks, SKPaints.TextBackpacks),
                         { IsMeds: true } => (SKPaints.PaintMeds, SKPaints.TextMeds),
                         { IsFood: true } => (SKPaints.PaintFood, SKPaints.TextFood),
                         LootCorpse => (SKPaints.PaintCorpse, SKPaints.TextCorpse),
                         _ => (_lootPaint, _lootTextPaint)
                     };

                     canvas.DrawCircle(screen, LOOT_CIRCLE_RADIUS, circlePaint);

                     if (item.Important || inCone)
                     {
                         var text = item.ShortName;
                         if (cfg.EspLootPrice)
                         {
                             text = item.Important ? item.ShortName : $"{item.ShortName} ({Utilities.FormatNumberKM(item.Price)})";
                         }

                         var textX = screen.X + LOOT_TEXT_OFFSET_X;
                         var textY = screen.Y + LOOT_TEXT_OFFSET_Y;

                         // Draw text outline if enabled
                         if (cfg.EspTextOutlines)
                         {
                             canvas.DrawText(text, textX, textY, _lootTextFont, SKPaints.TextOutline);
                         }

                         // Draw text fill
                         canvas.DrawText(text, textX, textY, _lootTextFont, textPaint);
                     }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawPlayerESP(SKCanvas canvas, AbstractPlayer player, LocalPlayer localPlayer, float screenWidth, float screenHeight, in ESPConfigCache cfg)
        {
            if (player is null || player == localPlayer || !player.IsAlive || !player.IsActive)
                return;

            bool isAI = player.Type is PlayerType.AIScav or PlayerType.AIRaider or PlayerType.AIBoss or PlayerType.PScav;

            // Use squared distance for comparison (avoids sqrt)
            float distSq = Vector3.DistanceSquared(localPlayer.Position, player.Position);
            float maxDistance = isAI ? cfg.EspAIMaxDistance : cfg.EspPlayerMaxDistance;
            float maxDistSq = maxDistance * maxDistance;

            if (maxDistance > 0 && distSq > maxDistSq)
                return;

            float maxDistGenSq = cfg.MaxDistance * cfg.MaxDistance;
            if (maxDistance == 0 && distSq > maxDistGenSq)
                return;

            // Only calculate actual distance if needed for display or effects
            float distance = MathF.Sqrt(distSq);

            var color = GetPlayerColor(player).Color;

            // Apply distance-based opacity fading if enabled
            if (cfg.EspDistanceFading)
            {
                float alpha = 1.0f - MathF.Min(distance / MAX_FADE_DISTANCE, MAX_ALPHA_REDUCTION);
                color = color.WithAlpha((byte)(alpha * 255));
            }

            _skeletonPaint.Color = color;
            _boxPaint.Color = color;
            _textPaint.Color = color;

            // Apply distance-based stroke scaling if enabled
            if (cfg.EspDistanceScaling)
            {
                float scale = MathF.Min(DISTANCE_SCALING_FACTOR / MathF.Max(distance, 1f), 2.0f);
                scale = MathF.Max(scale, 0.5f);

                _skeletonPaint.StrokeWidth = BASE_SKELETON_WIDTH * scale;
                _boxPaint.StrokeWidth = BASE_BOX_WIDTH * scale;
            }
            else
            {
                // Reset to default values
                _skeletonPaint.StrokeWidth = BASE_SKELETON_WIDTH;
                _boxPaint.StrokeWidth = BASE_BOX_WIDTH;
            }

            bool drawSkeleton = isAI ? cfg.EspAISkeletons : cfg.EspPlayerSkeletons;
            bool drawBox = isAI ? cfg.EspAIBoxes : cfg.EspPlayerBoxes;
            bool drawName = isAI ? cfg.EspAINames : cfg.EspPlayerNames;
            bool drawHeadCircle = isAI ? cfg.EspAIHeadCircles : cfg.EspPlayerHeadCircles;
            // bool drawHealthBar = isAI ? cfg.EspAIHealthBars : cfg.EspPlayerHealthBars; // TODO: Enable when config added

            if (drawSkeleton)
            {
                DrawSkeleton(canvas, player, screenWidth, screenHeight);
            }

            if (drawBox)
            {
                DrawBoundingBox(canvas, player, screenWidth, screenHeight, in cfg);
            }

            if (drawHeadCircle)
            {
                // Project head and neck to get natural screen-space head size
                var headPos3D = player.GetBonePos(Bones.HumanHead);
                var neckPos3D = player.GetBonePos(Bones.HumanNeck);

                if (TryProject(headPos3D, screenWidth, screenHeight, out var headCircleScreen) &&
                    TryProject(neckPos3D, screenWidth, screenHeight, out var neckScreen))
                {
                    // Calculate screen-space distance between head and neck
                    float headNeckDist = Vector2.Distance(
                        new Vector2(headCircleScreen.X, headCircleScreen.Y),
                        new Vector2(neckScreen.X, neckScreen.Y)
                    );

                    // Use config as multiplier for the natural head size
                    // Config value controls how much larger/smaller than actual head
                    float baseMultiplier = isAI ? cfg.EspAIHeadCircleSize : cfg.EspPlayerHeadCircleSize;
                    float circleRadius = headNeckDist * baseMultiplier;

                    canvas.DrawCircle(headCircleScreen, circleRadius, _boxPaint);
                }
            }

            if (drawName && TryProject(player.GetBonePos(Bones.HumanHead), screenWidth, screenHeight, out var headScreen))
            {
                DrawPlayerName(canvas, headScreen, player, distance, in cfg);

                // TODO: Uncomment when health bar config is added
                // Draw health bar above name if enabled
                // if (drawHealthBar)
                // {
                //     DrawHealthBar(canvas, headScreen, player);
                // }
            }
        }

        private void DrawSkeleton(SKCanvas canvas, AbstractPlayer player, float w, float h)
        {
            foreach (var (from, to) in _boneConnections)
            {
                var p1 = player.GetBonePos(from);
                var p2 = player.GetBonePos(to);

                if (TryProject(p1, w, h, out var s1) && TryProject(p2, w, h, out var s2))
                {
                    canvas.DrawLine(s1, s2, _skeletonPaint);
                }
            }
        }

        private void DrawBoundingBox(SKCanvas canvas, AbstractPlayer player, float w, float h, in ESPConfigCache cfg)
        {
            // Use stackalloc to avoid heap allocations
            Span<SKPoint> projectedPoints = stackalloc SKPoint[MAX_SKELETON_BONES];
            int pointCount = 0;

            foreach (var boneKvp in player.PlayerBones)
            {
                if (pointCount >= MAX_SKELETON_BONES)
                    break;

                if (TryProject(boneKvp.Value.Position, w, h, out var s))
                {
                    projectedPoints[pointCount++] = s;
                }
            }

            if (pointCount < 2)
                return;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            for (int i = 0; i < pointCount; i++)
            {
                var point = projectedPoints[i];
                if (point.X < minX) minX = point.X;
                if (point.X > maxX) maxX = point.X;
                if (point.Y < minY) minY = point.Y;
                if (point.Y > maxY) maxY = point.Y;
            }

            float boxWidth = maxX - minX;
            float boxHeight = maxY - minY;

            if (boxWidth < 1f || boxHeight < 1f || boxWidth > w * 2f || boxHeight > h * 2f)
                return;

            minX = Math.Clamp(minX, -50f, w + 50f);
            maxX = Math.Clamp(maxX, -50f, w + 50f);
            minY = Math.Clamp(minY, -50f, h + 50f);
            maxY = Math.Clamp(maxY, -50f, h + 50f);

            minX -= BOX_PADDING;
            minY -= BOX_PADDING;
            maxX += BOX_PADDING;
            maxY += BOX_PADDING;

            if (cfg.EspCornerBoxes)
            {
                // Draw corner-style boxes (L-shaped corners)
                float cornerLength = cfg.EspCornerLength;

                // Top-left corner
                canvas.DrawLine(minX, minY, minX + cornerLength, minY, _boxPaint);
                canvas.DrawLine(minX, minY, minX, minY + cornerLength, _boxPaint);

                // Top-right corner
                canvas.DrawLine(maxX, minY, maxX - cornerLength, minY, _boxPaint);
                canvas.DrawLine(maxX, minY, maxX, minY + cornerLength, _boxPaint);

                // Bottom-left corner
                canvas.DrawLine(minX, maxY, minX + cornerLength, maxY, _boxPaint);
                canvas.DrawLine(minX, maxY, minX, maxY - cornerLength, _boxPaint);

                // Bottom-right corner
                canvas.DrawLine(maxX, maxY, maxX - cornerLength, maxY, _boxPaint);
                canvas.DrawLine(maxX, maxY, maxX, maxY - cornerLength, _boxPaint);
            }
            else
            {
                // Draw full rectangle box
                var rect = new SKRect(minX, minY, maxX, maxY);
                canvas.DrawRect(rect, _boxPaint);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static SKPaint GetPlayerColor(AbstractPlayer player)
        {
             if (player.IsFocused)
                return SKPaints.PaintAimviewWidgetFocused;
            if (player is LocalPlayer)
                return SKPaints.PaintAimviewWidgetLocalPlayer;

            return player.Type switch
            {
                PlayerType.Teammate => SKPaints.PaintAimviewWidgetTeammate,
                PlayerType.PMC => SKPaints.PaintAimviewWidgetPMC,
                PlayerType.AIScav => SKPaints.PaintAimviewWidgetScav,
                PlayerType.AIRaider => SKPaints.PaintAimviewWidgetRaider,
                PlayerType.AIBoss => SKPaints.PaintAimviewWidgetBoss,
                PlayerType.PScav => SKPaints.PaintAimviewWidgetPScav,
                PlayerType.SpecialPlayer => SKPaints.PaintAimviewWidgetWatchlist,
                PlayerType.Streamer => SKPaints.PaintAimviewWidgetStreamer,
                _ => SKPaints.PaintAimviewWidgetPMC
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawPlayerName(SKCanvas canvas, SKPoint screenPos, AbstractPlayer player, float distance, in ESPConfigCache cfg)
        {
            var name = player.Name ?? "Unknown";
            var text = $"{name} ({distance:F0}m)";

            // Use cached text width if available
            if (!_textWidthCache.TryGetValue(text, out var textWidth))
            {
                textWidth = _textFont.MeasureText(text);
                _textWidthCache[text] = textWidth;
            }

            var textHeight = _textFont.Size;

            var x = screenPos.X - textWidth / 2;
            var y = screenPos.Y - PLAYER_NAME_OFFSET_Y + textHeight;

            // Draw text outline if enabled
            if (cfg.EspTextOutlines)
            {
                canvas.DrawText(text, x, y, _textFont, SKPaints.TextOutline);
            }

            // Draw text fill
            canvas.DrawText(text, x, y, _textFont, _textPaint);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawNotShown(SKCanvas canvas, float width, float height)
        {
            var text = "ESP Hidden";
            var x = width / 2;
            var y = height / 2;

            canvas.DrawText(text, x, y, SKTextAlign.Center, _notShownFont, _notShownPaint);
        }

        private void DrawCrosshair(SKCanvas canvas, float width, float height, float crosshairLength)
        {
            float centerX = width / 2f;
            float centerY = height / 2f;
            float length = MathF.Max(2f, crosshairLength);

            canvas.DrawLine(centerX - length, centerY, centerX + length, centerY, _crosshairPaint);
            canvas.DrawLine(centerX, centerY - length, centerX, centerY + length, _crosshairPaint);
        }

        // TODO: Uncomment when health bar config is added to EftDmaConfig.cs
        // and when HealthStatus property is available on AbstractPlayer (currently only on ObservedPlayer)
        /*
        private void DrawHealthBar(SKCanvas canvas, SKPoint screenPos, AbstractPlayer player)
        {
            const float barWidth = 50f;
            const float barHeight = 4f;
            const float barOffsetY = 30f; // Above the name

            // Get health percentage from HealthStatus
            float healthPercent = GetHealthPercent(player);

            // Calculate bar position (centered above name)
            float barX = screenPos.X - barWidth / 2;
            float barY = screenPos.Y - barOffsetY;

            // Background bar (darker)
            var bgPaint = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 180),
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRect(barX, barY, barWidth, barHeight, bgPaint);
            bgPaint.Dispose();

            // Health bar (color-coded)
            float healthBarWidth = barWidth * healthPercent;
            var healthColor = healthPercent switch
            {
                >= 0.7f => new SKColor(0, 255, 0),      // Green - Healthy
                >= 0.4f => new SKColor(255, 255, 0),    // Yellow - Injured
                >= 0.2f => new SKColor(255, 165, 0),    // Orange - Badly Injured
                _ => new SKColor(255, 0, 0)             // Red - Dying
            };

            var healthPaint = new SKPaint
            {
                Color = healthColor,
                Style = SKPaintStyle.Fill
            };
            canvas.DrawRect(barX, barY, healthBarWidth, barHeight, healthPaint);
            healthPaint.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float GetHealthPercent(AbstractPlayer player)
        {
            // Map HealthStatus enum to percentage
            // This is a rough approximation based on the enum values
            if (player is ObservedPlayer observed)
            {
                return observed.HealthStatus switch
                {
                    Enums.ETagStatus.Healthy => 1.0f,
                    Enums.ETagStatus.Injured => 0.6f,
                    Enums.ETagStatus.BadlyInjured => 0.3f,
                    Enums.ETagStatus.Dying => 0.1f,
                    _ => 1.0f // Default to healthy if unknown
                };
            }
            return 1.0f; // Default to healthy for non-observed players
        }
        */

        private void DrawFPS(SKCanvas canvas, float width, float height)
        {
            var fpsText = $"FPS: {_fps}";
            canvas.DrawText(fpsText, 10, 25, _fpsFont, _fpsPaint);
        }

        #endregion

        #region WorldToScreen Conversion

        private void UpdateCameraPositionFromMatrix()
        {
            var viewMatrix = _cameraManager.ViewMatrix;
            _camPos = new Vector3(viewMatrix.M14, viewMatrix.M24, viewMatrix.M34);
            _transposedViewMatrix.Update(ref viewMatrix);
        }

        private bool WorldToScreen2(in Vector3 world, out SKPoint scr, float screenWidth, float screenHeight)
        {
            scr = default;

            float w = Vector3.Dot(_transposedViewMatrix.Translation, world) + _transposedViewMatrix.M44;

            if (w < W_COMPONENT_THRESHOLD)
                return false;

            float x = Vector3.Dot(_transposedViewMatrix.Right, world) + _transposedViewMatrix.M14;
            float y = Vector3.Dot(_transposedViewMatrix.Up, world) + _transposedViewMatrix.M24;

            var centerX = screenWidth / 2f;
            var centerY = screenHeight / 2f;

            scr.X = centerX * (1f + x / w);
            scr.Y = centerY * (1f - y / w);

            return true;
        }

        private class TransposedViewMatrix
        {
            public float M44;
            public float M14;
            public float M24;
            public Vector3 Translation;
            public Vector3 Right;
            public Vector3 Up;
            public Vector3 Forward;

            public void Update(ref Matrix4x4 matrix)
            {
                M44 = matrix.M44;
                M14 = matrix.M41;
                M24 = matrix.M42;

                Translation.X = matrix.M14;
                Translation.Y = matrix.M24;
                Translation.Z = matrix.M34;

                Right.X = matrix.M11;
                Right.Y = matrix.M21;
                Right.Z = matrix.M31;

                Up.X = matrix.M12;
                Up.Y = matrix.M22;
                Up.Z = matrix.M32;

                Forward.X = matrix.M13;
                Forward.Y = -matrix.M23;
                Forward.Z = -matrix.M33;
            }
        }

        private bool TryProject(in Vector3 world, float w, float h, out SKPoint screen)
        {
            screen = default;
            if (world == Vector3.Zero)
                return false;
            if (!WorldToScreen2(world, out screen, w, h))
                return false;
            if (float.IsNaN(screen.X) || float.IsInfinity(screen.X) ||
                float.IsNaN(screen.Y) || float.IsInfinity(screen.Y))
                return false;

            if (screen.X < -SCREEN_MARGIN || screen.X > w + SCREEN_MARGIN ||
                screen.Y < -SCREEN_MARGIN || screen.Y > h + SCREEN_MARGIN)
                return false;

            return true;
        }

        #endregion

        #region Window Management

        private void ESPForm_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !_isFullscreen)
            {
                // Allow dragging the window using WinAPI
                const int WM_NCLBUTTONDOWN = 0xA1;
                const int HT_CAPTION = 0x2;

                [System.Runtime.InteropServices.DllImport("user32.dll")]
                static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
                [System.Runtime.InteropServices.DllImport("user32.dll")]
                static extern bool ReleaseCapture();

                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        private void ESPForm_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            ToggleFullscreen();
        }

        public void ToggleFullscreen()
        {
            if (_isFullscreen)
            {
                this.FormBorderStyle = FormBorderStyle.Sizable;
                this.WindowState = FormWindowState.Normal;
                this.ClientSize = new System.Drawing.Size(800, 600);
                this.StartPosition = FormStartPosition.CenterScreen;
                _isFullscreen = false;
            }
            else
            {
                var targetScreenIndex = App.Config.UI.EspTargetScreen;
                var allScreens = Screen.AllScreens;

                if (targetScreenIndex < allScreens.Length)
                {
                    var screen = allScreens[targetScreenIndex];
                    this.FormBorderStyle = FormBorderStyle.None;
                    this.WindowState = FormWindowState.Normal;
                    this.Location = screen.Bounds.Location;
                    this.ClientSize = screen.Bounds.Size;
                    _isFullscreen = true;
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);

            // Clean up resources
            _renderTimer?.Dispose();
            _skglControl.PaintSurface -= ESPForm_PaintSurface;
            _renderTimer.Elapsed -= RenderTimer_Elapsed;

            // Dispose fonts/paints
            _textFont?.Dispose();
            _textPaint?.Dispose();
            _skeletonPaint?.Dispose();
            _boxPaint?.Dispose();
            _lootPaint?.Dispose();
            _lootTextFont?.Dispose();
            _lootTextPaint?.Dispose();
            _crosshairPaint?.Dispose();
            _notShownFont?.Dispose();
            _notShownPaint?.Dispose();
            _fpsFont?.Dispose();
            _fpsPaint?.Dispose();
        }

        #endregion
    }
}
