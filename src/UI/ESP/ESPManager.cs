using System.Windows.Forms;

namespace LoneEftDmaRadar.UI.ESP
{
    public static class ESPManager
    {
        private static ESPForm _espForm;
        private static Thread _winFormsThread;
        private static bool _isInitialized = false;
        private static readonly object _lockObject = new object();
        private static readonly ManualResetEventSlim _formCreatedEvent = new ManualResetEventSlim(false);

        public static void Initialize()
        {
            lock (_lockObject)
            {
                if (_isInitialized && _espForm != null) return;

                // Create dedicated STA thread for WinForms
                _winFormsThread = new Thread(() =>
                {
                    try
                    {
                        _espForm = new ESPForm();
                        _espForm.FormClosed += (s, e) =>
                        {
                            _espForm = null;
                            _isInitialized = false;
                            ESPForm.ShowESP = false;
                            System.Windows.Forms.Application.ExitThread(); // Exit WinForms message loop
                        };

                        // Signal that form is created
                        _formCreatedEvent.Set();

                        // Run WinForms message pump
                        System.Windows.Forms.Application.Run(_espForm);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"ESP Thread Error: {ex}");
                        _formCreatedEvent.Set(); // Unblock even on error
                    }
                })
                {
                    Name = "ESPForm Thread",
                    IsBackground = true
                };

                _winFormsThread.SetApartmentState(ApartmentState.STA);
                _winFormsThread.Start();

                // Wait for form creation (with timeout)
                if (_formCreatedEvent.Wait(TimeSpan.FromSeconds(10)))
                {
                    // Wait for handle creation
                    int attempts = 0;
                    while (_espForm != null && !_espForm.IsHandleCreated && attempts < 100)
                    {
                        Thread.Sleep(10);
                        attempts++;
                    }

                    if (_espForm != null && _espForm.IsHandleCreated)
                    {
                        _isInitialized = true;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("ESP: Failed to create form handle");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ESP: Timeout waiting for form creation");
                }
            }
        }

        public static void ToggleESP()
        {
            if (!_isInitialized || _espForm == null) Initialize();

            ESPForm.ShowESP = !ESPForm.ShowESP;

            SafeInvoke(() =>
            {
                if (ESPForm.ShowESP)
                    _espForm?.Show();
                else
                    _espForm?.Hide();
            });
        }

        public static void ShowESP()
        {
            if (!_isInitialized || _espForm == null) Initialize();

            ESPForm.ShowESP = true;

            SafeInvoke(() => _espForm?.Show());
        }

        public static void StartESP()
        {
            if (!_isInitialized || _espForm == null) Initialize();

            ESPForm.ShowESP = true;

            SafeInvoke(() =>
            {
                _espForm?.Show();

                // Force Fullscreen
                if (_espForm != null && _espForm.FormBorderStyle != FormBorderStyle.None)
                {
                    _espForm.ToggleFullscreen();
                }
            });
        }

        public static void HideESP()
        {
            ESPForm.ShowESP = false;

            SafeInvoke(() => _espForm?.Hide());
        }

        public static void ToggleFullscreen()
        {
            if (!_isInitialized) Initialize();

            SafeInvoke(() => _espForm?.ToggleFullscreen());
        }

        public static void CloseESP()
        {
            SafeInvoke(() =>
            {
                _espForm?.Close();
            });

            _espForm = null;
            _isInitialized = false;
            _formCreatedEvent.Reset();
        }

        public static void ApplyResolutionOverride()
        {
            if (!_isInitialized || _espForm is null) return;
            // Resolution override handled in ESPForm.ToggleFullscreen()
        }

        /// <summary>
        /// Resets camera state. Useful when ESP appears broken.
        /// </summary>
        public static void ResetCamera()
        {
            Tarkov.GameWorld.CameraManager.Reset();
        }

        /// <summary>
        /// Thread-safe invoke helper to execute actions on the WinForms UI thread.
        /// </summary>
        private static void SafeInvoke(Action action)
        {
            if (_espForm == null || _espForm.IsDisposed)
                return;

            try
            {
                if (_espForm.InvokeRequired)
                {
                    _espForm.Invoke(action);
                }
                else
                {
                    action();
                }
            }
            catch (ObjectDisposedException)
            {
                // Form was disposed, ignore
            }
            catch (InvalidOperationException)
            {
                // Handle creation issue, ignore
            }
        }

        public static bool IsInitialized => _isInitialized;
    }
}
