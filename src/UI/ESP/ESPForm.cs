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

        // Object pooling to reduce GC pressure
        private readonly ConcurrentBag<SKPath> _pathPool = new();
        private readonly ConcurrentBag<SKPaint> _paintPool = new();

        // Pre-allocated buffers (zero allocation rendering)
        private readonly List<AbstractPlayer> _filteredPlayers = new(128);
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
                        _cameraManager.Update(localPlayer);
                        UpdateCameraPositionFromMatrix();

                        // Render Loot (background layer)
                        if (App.Config.Loot.Enabled && App.Config.UI.EspLoot)
                        {
                            DrawLoot(canvas, e.Info.Width, e.Info.Height);
                        }

                        // Render Exfils
                        if (Exits is not null && App.Config.UI.EspExfils)
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

                                         canvas.DrawCircle(screen, 4f, paint);
                                         canvas.DrawText(exfil.Name, screen.X + 6, screen.Y + 4, _textFont, SKPaints.TextExfil);
                                     }
                                }
                            }
                        }

                        // Render players
                        foreach (var player in allPlayers)
                        {
                            DrawPlayerESP(canvas, player, localPlayer, e.Info.Width, e.Info.Height);
                        }

                        if (App.Config.UI.EspCrosshair)
                        {
                            DrawCrosshair(canvas, e.Info.Width, e.Info.Height);
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

        private void DrawLoot(SKCanvas canvas, float screenWidth, float screenHeight)
        {
            var lootItems = Memory.Game?.Loot?.FilteredLoot;
            if (lootItems is null) return;

            foreach (var item in lootItems)
            {
                // OPTIMIZATION: Check distance FIRST before any other checks
                float distance = Vector3.Distance(_camPos, item.Position);
                if (App.Config.UI.EspLootMaxDistance > 0 && distance > App.Config.UI.EspLootMaxDistance)
                    continue;

                // Filter based on ESP settings
                bool isCorpse = item is LootCorpse;
                if (isCorpse && !App.Config.UI.EspCorpses)
                    continue;

                bool isContainer = item is LootContainer;
                if (isContainer && !App.Config.UI.EspContainers)
                    continue;

                bool isFood = item.IsFood;
                bool isMeds = item.IsMeds;
                bool isBackpack = item.IsBackpack;

                if (isFood && !App.Config.UI.EspFood)
                    continue;
                if (isMeds && !App.Config.UI.EspMeds)
                    continue;
                if (isBackpack && !App.Config.UI.EspBackpacks)
                    continue;

                if (WorldToScreen2(item.Position, out var screen, screenWidth, screenHeight))
                {
                     // Calculate cone filter
                     bool coneEnabled = App.Config.UI.EspLootConeEnabled && App.Config.UI.EspLootConeAngle > 0f;
                     bool inCone = true;

                     if (coneEnabled)
                     {
                         float centerX = screenWidth / 2f;
                         float centerY = screenHeight / 2f;
                         float dx = screen.X - centerX;
                         float dy = screen.Y - centerY;
                         float fov = App.Config.UI.FOV;
                         float screenAngleX = MathF.Abs(dx / centerX) * (fov / 2f);
                         float screenAngleY = MathF.Abs(dy / centerY) * (fov / 2f);
                         float screenAngle = MathF.Sqrt(screenAngleX * screenAngleX + screenAngleY * screenAngleY);
                         inCone = screenAngle <= App.Config.UI.EspLootConeAngle;
                     }

                     SKPaint circlePaint, textPaint;

                     if (item.Important)
                     {
                         circlePaint = SKPaints.PaintFilteredLoot;
                         textPaint = SKPaints.TextFilteredLoot;
                     }
                     else if (item.IsValuableLoot)
                     {
                         circlePaint = SKPaints.PaintImportantLoot;
                         textPaint = SKPaints.TextImportantLoot;
                     }
                     else if (isBackpack)
                     {
                         circlePaint = SKPaints.PaintBackpacks;
                         textPaint = SKPaints.TextBackpacks;
                     }
                     else if (isMeds)
                     {
                         circlePaint = SKPaints.PaintMeds;
                         textPaint = SKPaints.TextMeds;
                     }
                     else if (isFood)
                     {
                         circlePaint = SKPaints.PaintFood;
                         textPaint = SKPaints.TextFood;
                     }
                     else if (isCorpse)
                     {
                         circlePaint = SKPaints.PaintCorpse;
                         textPaint = SKPaints.TextCorpse;
                     }
                     else
                     {
                         circlePaint = _lootPaint;
                         textPaint = _lootTextPaint;
                     }

                     canvas.DrawCircle(screen, 2f, circlePaint);

                     if (item.Important || inCone)
                     {
                         var text = item.ShortName;
                         if (App.Config.UI.EspLootPrice)
                         {
                             text = item.Important ? item.ShortName : $"{item.ShortName} ({Utilities.FormatNumberKM(item.Price)})";
                         }
                         canvas.DrawText(text, screen.X + 4, screen.Y + 4, _lootTextFont, textPaint);
                     }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawPlayerESP(SKCanvas canvas, AbstractPlayer player, LocalPlayer localPlayer, float screenWidth, float screenHeight)
        {
            if (player is null || player == localPlayer || !player.IsAlive || !player.IsActive)
                return;

            bool isAI = player.Type is PlayerType.AIScav or PlayerType.AIRaider or PlayerType.AIBoss or PlayerType.PScav;
            float distance = Vector3.Distance(localPlayer.Position, player.Position);
            float maxDistance = isAI ? App.Config.UI.EspAIMaxDistance : App.Config.UI.EspPlayerMaxDistance;

            if (maxDistance > 0 && distance > maxDistance)
                return;

            if (maxDistance == 0 && distance > App.Config.UI.MaxDistance)
                return;

            var color = GetPlayerColor(player).Color;
            _skeletonPaint.Color = color;
            _boxPaint.Color = color;
            _textPaint.Color = color;

            bool drawSkeleton = isAI ? App.Config.UI.EspAISkeletons : App.Config.UI.EspPlayerSkeletons;
            bool drawBox = isAI ? App.Config.UI.EspAIBoxes : App.Config.UI.EspPlayerBoxes;
            bool drawName = isAI ? App.Config.UI.EspAINames : App.Config.UI.EspPlayerNames;

            if (drawSkeleton)
            {
                DrawSkeleton(canvas, player, screenWidth, screenHeight);
            }

            if (drawBox)
            {
                DrawBoundingBox(canvas, player, screenWidth, screenHeight);
            }

            if (drawName && TryProject(player.GetBonePos(Bones.HumanHead), screenWidth, screenHeight, out var headScreen))
            {
                DrawPlayerName(canvas, headScreen, player, distance);
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

        private void DrawBoundingBox(SKCanvas canvas, AbstractPlayer player, float w, float h)
        {
            // Use stackalloc to avoid heap allocations
            Span<SKPoint> projectedPoints = stackalloc SKPoint[32];
            int pointCount = 0;

            foreach (var boneKvp in player.PlayerBones)
            {
                if (pointCount >= 32)
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

            float padding = 2f;
            var rect = new SKRect(minX - padding, minY - padding, maxX + padding, maxY + padding);
            canvas.DrawRect(rect, _boxPaint);
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
        private void DrawPlayerName(SKCanvas canvas, SKPoint screenPos, AbstractPlayer player, float distance)
        {
            var name = player.Name ?? "Unknown";
            var text = $"{name} ({distance:F0}m)";

            var textWidth = _textFont.MeasureText(text);
            var textHeight = _textFont.Size;

            canvas.DrawText(text, screenPos.X - textWidth / 2, screenPos.Y - 20 + textHeight, _textFont, _textPaint);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrawNotShown(SKCanvas canvas, float width, float height)
        {
            var text = "ESP Hidden";
            var x = width / 2;
            var y = height / 2;

            canvas.DrawText(text, x, y, SKTextAlign.Center, _notShownFont, _notShownPaint);
        }

        private void DrawCrosshair(SKCanvas canvas, float width, float height)
        {
            float centerX = width / 2f;
            float centerY = height / 2f;
            float length = MathF.Max(2f, App.Config.UI.EspCrosshairLength);

            canvas.DrawLine(centerX - length, centerY, centerX + length, centerY, _crosshairPaint);
            canvas.DrawLine(centerX, centerY - length, centerX, centerY + length, _crosshairPaint);
        }

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

            if (w < 0.098f)
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

            const float margin = 200f;
            if (screen.X < -margin || screen.X > w + margin ||
                screen.Y < -margin || screen.Y > h + margin)
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

            // Dispose object pools
            foreach (var path in _pathPool)
                path.Dispose();
            _pathPool.Clear();

            foreach (var paint in _paintPool)
                paint.Dispose();
            _paintPool.Clear();
        }

        #endregion
    }
}
