using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CFTAllInOneConnectorApp;

internal static class Program
{
    [STAThread] private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private string EngineExe => Path.Combine(AppContext.BaseDirectory, "Engine", "CFTAllInOneConnectorEngine.exe");
    private string EngineLog => Path.Combine(AppContext.BaseDirectory, "Engine", "log.txt");
    private string ProfilesDirectory => Path.Combine(AppContext.BaseDirectory, "Profiles");
    private readonly Button action = new();
    private readonly Label state = new();
    private readonly Label dot = new();
    private readonly Label devicesValue = StatusValue("Not found");
    private readonly Label simulatorValue = StatusValue("Not found");
    private readonly Label aircraftValue = StatusValue("No aircraft detected");
    private readonly System.Windows.Forms.Timer monitor = new() { Interval = 500 };
    private readonly CheckBox debugToggle = new();
    private readonly Panel debugPanel = new();
    private readonly RichTextBox debugOutput = new();
    private readonly Button debugPauseButton = new();
    private readonly Label debugState = new();
    private Process? mobiFlight;
    private IntPtr mobiFlightWindow;
    private bool connected;
    private bool engineReady;
    private bool engineInitializing;
    private bool waitingForDevice;
    private bool waitingForSimulator;
    private bool deviceWarningShown;
    private Form? deviceWarningDialog;
    private bool debugPaused;
    private long debugLogPosition;
    private string debugRemainder = "";

    private static readonly Size CollapsedSize = new(790, 300);
    private const int DebugPanelExtraHeight = 290;
    private bool deviceDisconnectBeingHandled;
    private string? lastConnectedModule;
    private string? trackedDevicePort;

    public MainForm()
    {
        Text = "CFT All-in-One Connector";
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        ClientSize = CollapsedSize;
        StartPosition = FormStartPosition.Manual;
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1200, 800);
        Location = new Point(screen.Left + Math.Max(20, (screen.Width - Width) / 2),
                             screen.Top + Math.Max(20, (screen.Height - Height) / 2));
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimumSize = new Size(830, 370);
        BackColor = Color.FromArgb(15, 23, 42);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10F);

        Controls.Add(new Label { Text = "CORE FLIGHT TECH", Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold), ForeColor = Color.FromArgb(226, 232, 240), AutoSize = true, Location = new Point(28, 25) });
        dot.Text = "●"; dot.Font = new Font("Segoe UI", 13F); dot.ForeColor = Color.FromArgb(100, 116, 139); dot.AutoSize = true; dot.Location = new Point(30, 72);
        state.Text = "DISCONNECTED"; state.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold); state.ForeColor = Color.FromArgb(148, 163, 184); state.AutoSize = true; state.Location = new Point(55, 75);
        action.Text = "INITIALIZING…"; action.Enabled = false; action.Font = new Font("Segoe UI Semibold", 14F, FontStyle.Bold); action.FlatStyle = FlatStyle.Flat; action.FlatAppearance.BorderSize = 0;
        action.BackColor = Color.FromArgb(37, 99, 235); action.ForeColor = Color.White; action.Cursor = Cursors.Hand; action.Size = new Size(384, 78); action.Location = new Point(28, 155); action.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        action.Click += async (_, _) => await ToggleConnection();
        Controls.Add(dot); Controls.Add(state); Controls.Add(action);
        AddStatusCard("CONNECTED DEVICES", devicesValue, 455, 25);
        devicesValue.AutoEllipsis = false;
        devicesValue.Font = new Font("Segoe UI Semibold", 9.5F);
        devicesValue.Height = 36;
        AddStatusCard("SIM STATUS", simulatorValue, 455, 113);
        AddStatusCard("AIRCRAFT", aircraftValue, 455, 201);
        BuildDebugPanel();
        monitor.Tick += (_, _) => { CheckProcess(); ReadMobiFlightStatus(); CheckWaitingDevice(); CheckWaitingSimulator(); UpdateDebugLog(); }; monitor.Start();
        Shown += async (_, _) => await StartOrWaitForDevice();
        FormClosing += (_, _) => ShutdownEngine();
    }

    private static Label StatusValue(string text) => new()
    {
        Text = text, AutoEllipsis = true, AutoSize = false, Size = new Size(270, 28),
        Font = new Font("Segoe UI Semibold", 11F), ForeColor = Color.FromArgb(226, 232, 240)
    };

    private void AddStatusCard(string caption, Label value, int x, int y)
    {
        var card = new Panel { Location = new Point(x, y), Size = new Size(305, 73), BackColor = Color.FromArgb(30, 41, 59), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        card.Controls.Add(new Label { Text = caption, Location = new Point(15, 10), AutoSize = true, Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold), ForeColor = Color.FromArgb(96, 165, 250) });
        value.Location = new Point(15, 34); card.Controls.Add(value); Controls.Add(card);
    }

    private void BuildDebugPanel()
    {
        debugToggle.Text = "Debug";
        debugToggle.AutoSize = true;
        debugToggle.Location = new Point(24, 272);
        debugToggle.ForeColor = Color.FromArgb(148, 163, 184);
        debugToggle.CheckedChanged += (_, _) =>
        {
            if (debugToggle.Checked)
            {
                debugPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                ClientSize = new Size(ClientSize.Width, Math.Max(CollapsedSize.Height + DebugPanelExtraHeight, ClientSize.Height + DebugPanelExtraHeight));
                debugPanel.SetBounds(18, 310, ClientSize.Width - 36, ClientSize.Height - 330);
                debugPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
                debugPanel.Visible = true;
                UpdateDebugLog();
            }
            else
            {
                debugPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                debugPanel.Visible = false;
                ClientSize = new Size(ClientSize.Width, Math.Max(CollapsedSize.Height, ClientSize.Height - DebugPanelExtraHeight));
            }
        };
        Controls.Add(debugToggle);

        debugPanel.Location = new Point(18, 310);
        debugPanel.Size = new Size(754, 260);
        debugPanel.BackColor = Color.White;
        debugPanel.Visible = false;
        debugPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var title = new Label
        {
            Text = "MOBIFLIGHT ENGINE DEBUG",
            Location = new Point(14, 11),
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold),
            ForeColor = Color.Black
        };
        debugPanel.Controls.Add(title);

        debugState.Text = "LIVE";
        debugState.Location = new Point(185, 11);
        debugState.AutoSize = true;
        debugState.Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold);
        debugState.ForeColor = Color.FromArgb(74, 222, 128);
        debugPanel.Controls.Add(debugState);

        debugPauseButton.Text = "PAUSE";
        debugPauseButton.Size = new Size(78, 28);
        debugPauseButton.Location = new Point(492, 6);
        debugPauseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        debugPauseButton.FlatStyle = FlatStyle.Flat;
        debugPauseButton.ForeColor = Color.Black;
        debugPauseButton.BackColor = Color.FromArgb(241, 245, 249);
        debugPauseButton.FlatAppearance.BorderColor = Color.FromArgb(148, 163, 184);
        debugPauseButton.Click += (_, _) =>
        {
            debugPaused = !debugPaused;
            debugPauseButton.Text = debugPaused ? "RESUME" : "PAUSE";
            debugState.Text = debugPaused ? "PAUSED" : "LIVE";
            debugState.ForeColor = debugPaused ? Color.FromArgb(250, 204, 21) : Color.FromArgb(74, 222, 128);
            if (!debugPaused) UpdateDebugLog();
        };
        debugPanel.Controls.Add(debugPauseButton);

        var clear = DebugButton("CLEAR", 578);
        clear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        clear.Click += (_, _) =>
        {
            debugOutput.Clear();
            debugRemainder = "";
            try { debugLogPosition = File.Exists(EngineLog) ? new FileInfo(EngineLog).Length : 0; } catch { }
        };
        debugPanel.Controls.Add(clear);

        var copy = DebugButton("COPY", 664);
        copy.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        copy.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(debugOutput.Text)) Clipboard.SetText(debugOutput.Text);
        };
        debugPanel.Controls.Add(copy);

        debugOutput.Location = new Point(12, 42);
        debugOutput.Size = new Size(730, 205);
        debugOutput.ReadOnly = true;
        debugOutput.BackColor = Color.White;
        debugOutput.ForeColor = Color.Black;
        debugOutput.BorderStyle = BorderStyle.FixedSingle;
        debugOutput.Font = new Font("Consolas", 9F);
        debugOutput.WordWrap = false;
        debugOutput.DetectUrls = false;
        debugOutput.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        debugPanel.Controls.Add(debugOutput);
        Controls.Add(debugPanel);
    }

    private static Button DebugButton(string text, int x) => new()
    {
        Text = text,
        Size = new Size(78, 28),
        Location = new Point(x, 6),
        FlatStyle = FlatStyle.Flat,
        ForeColor = Color.Black,
        BackColor = Color.FromArgb(241, 245, 249)
    };

    private void UpdateDebugLog()
    {
        if (!debugToggle.Checked || debugPaused || !File.Exists(EngineLog)) return;
        try
        {
            using var stream = new FileStream(EngineLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length < debugLogPosition)
            {
                debugLogPosition = 0;
                debugRemainder = "";
                debugOutput.Clear();
            }
            if (debugLogPosition == 0 && stream.Length > 100_000)
                debugLogPosition = stream.Length - 100_000;
            stream.Seek(debugLogPosition, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, leaveOpen: true);
            var addition = reader.ReadToEnd();
            debugLogPosition = stream.Position;
            if (addition.Length == 0) return;

            var text = debugRemainder + addition;
            var lines = text.Split('\n');
            debugRemainder = text.EndsWith('\n') ? "" : lines[^1];
            var completeCount = text.EndsWith('\n') ? lines.Length - 1 : lines.Length - 1;
            for (var i = 0; i < completeCount; i++) AppendDebugLine(lines[i].TrimEnd('\r'));

            if (debugOutput.TextLength > 250_000)
                debugOutput.Select(0, debugOutput.TextLength - 200_000);
            if (debugOutput.SelectionLength > 0) debugOutput.SelectedText = "";
            debugOutput.SelectionStart = debugOutput.TextLength;
            debugOutput.ScrollToCaret();
        }
        catch { }
    }

    private void AppendDebugLine(string line)
    {
        debugOutput.SelectionStart = debugOutput.TextLength;
        debugOutput.SelectionColor = Color.Black;
        debugOutput.AppendText(line + Environment.NewLine);
    }

    private async Task ToggleConnection()
    {
        action.Enabled = false;
        try { if (connected) await Disconnect(); else await Connect(); }
        finally { action.Enabled = true; }
    }

    private string? ResolveProfile()
    {
        if (!File.Exists(EngineExe)) return null;
        var profileName = DetectSimulatorFamily() switch
        {
            SimulatorFamily.MicrosoftFlightSimulator => "MSFS20_PMDG737.mfproj",
            SimulatorFamily.XPlane => "XP12_ZIBO737.mfproj",
            _ => null
        };
        return profileName is null ? null : Path.Combine(ProfilesDirectory, profileName) is var path && File.Exists(path) ? path : null;
    }

    private async Task StartOrWaitForDevice()
    {
        if (GetCoreFlightTechCandidatePorts().Count == 0)
        {
            ShowDeviceRequired();
            return;
        }

        waitingForDevice = false;
        if (DetectSimulatorFamily() == SimulatorFamily.None)
        {
            ShowSimulatorRequired();
            return;
        }
        await PrepareEngine();
    }

    private void ShowSimulatorRequired()
    {
        waitingForSimulator = true;
        engineReady = false;
        action.Enabled = false;
        action.Text = "WAITING FOR SIMULATOR";
        state.Text = "WAITING FOR SIMULATOR";
        state.ForeColor = dot.ForeColor = Color.FromArgb(250, 204, 21);
        simulatorValue.Text = "Start MSFS or X-Plane";
    }

    private void CheckWaitingSimulator()
    {
        if (!waitingForSimulator || engineInitializing || DetectSimulatorFamily() == SimulatorFamily.None) return;
        waitingForSimulator = false;
        _ = PrepareEngine();
    }

    private void ShowDeviceRequired()
    {
        waitingForDevice = true;
        waitingForSimulator = false;
        engineReady = false;
        action.Enabled = false;
        action.Text = "DEVICE REQUIRED";
        state.Text = "WAITING FOR DEVICE";
        state.ForeColor = dot.ForeColor = Color.FromArgb(250, 204, 21);
        devicesValue.Text = "Not found";

        if (deviceWarningShown) return;
        deviceWarningShown = true;
    }

    private void ShowDeviceWarningDialog()
    {
        if (deviceWarningDialog is { IsDisposed: false }) return;

        var dialog = new Form
        {
            Text = "Device required",
            ClientSize = new Size(440, 145),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            TopMost = true,
            BackColor = Color.White,
            Font = new Font("Segoe UI", 10F)
        };
        dialog.Controls.Add(new Label
        {
            Text = "CoreFlightTech NAV (CPT) was not found.\r\nConnect the device to a USB port to continue.",
            AutoSize = false,
            Location = new Point(24, 24),
            Size = new Size(390, 55),
            ForeColor = Color.FromArgb(30, 41, 59)
        });
        var ok = new Button { Text = "OK", Size = new Size(90, 32), Location = new Point(324, 92), DialogResult = DialogResult.OK };
        ok.Click += (_, _) => dialog.Close();
        dialog.Controls.Add(ok);
        dialog.AcceptButton = ok;
        dialog.FormClosed += (_, _) => deviceWarningDialog = null;
        deviceWarningDialog = dialog;
        dialog.Show(this);
    }

    private void CheckWaitingDevice()
    {
        if (!waitingForDevice || engineInitializing || GetCoreFlightTechCandidatePorts().Count == 0) return;
        waitingForDevice = false;
        deviceWarningShown = false;
        deviceWarningDialog?.Close();
        deviceWarningDialog = null;
        _ = PrepareEngine();
    }

    private bool TargetModuleIsReady(string port)
    {
        if (!File.Exists(EngineLog)) return false;
        var lines = File.ReadLines(EngineLog).TakeLast(300).ToArray();
        var accepted = Array.FindLastIndex(lines, x => Regex.IsMatch(x, $@"Dedicated mode: accepted '.+?' .* on {Regex.Escape(port)}", RegexOptions.IgnoreCase));
        if (accepted < 0) return false;
        var disappeared = Array.FindLastIndex(lines, x => x.Contains($"Port disappeared: {port}", StringComparison.OrdinalIgnoreCase));
        if (disappeared > accepted) return false;
        var bindingReady = Array.FindLastIndex(lines, x =>
            x.Contains("CoreFlightTech READY:", StringComparison.OrdinalIgnoreCase) ||
            x.Contains("reloading profile for native auto-binding", StringComparison.OrdinalIgnoreCase));
        return bindingReady > accepted;
    }

    private async Task<bool> PrepareEngine()
    {
        if (engineReady && FindEngineProcess() is not null) return true;
        if (engineInitializing) return false;

        engineInitializing = true;
        action.Enabled = false;
        action.Text = "INITIALIZING…";
        SetBusy("INITIALIZING…");
        try
        {
            var targetPort = GetCoreFlightTechCandidatePorts().FirstOrDefault();
            if (targetPort is null)
            {
                ShowDeviceRequired();
                return false;
            }

            var profile = ResolveProfile();
            if (profile is null)
            {
                ShowSimulatorRequired();
                return false;
            }

            mobiFlight = FindEngineProcess();
            if (mobiFlight is null)
            {
                mobiFlight = Process.Start(new ProcessStartInfo
                {
                    FileName = EngineExe,
                    Arguments = $"/cfg \"{profile}\" /coreFlightHidden",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(EngineExe)!
                });
            }

            for (var i = 0; i < 80 && mobiFlight is { HasExited: false }; i++)
            {
                mobiFlight.Refresh();
                mobiFlightWindow = FindWindowForProcess(mobiFlight);
                if (mobiFlightWindow != IntPtr.Zero) break;
                await Task.Delay(250);
            }

            for (var i = 0; i < 80 && mobiFlight is { HasExited: false }; i++)
            {
                if (GetCoreFlightTechCandidatePorts().Count == 0)
                {
                    ShowDeviceRequired();
                    return false;
                }
                if (File.Exists(EngineLog) &&
                    File.ReadLines(EngineLog).TakeLast(100).Any(x => x.Contains("CoreFlightTech READY:", StringComparison.OrdinalIgnoreCase)) &&
                    TargetModuleIsReady(targetPort))
                {
                    engineReady = mobiFlightWindow != IntPtr.Zero;
                    break;
                }
                await Task.Delay(250);
            }

            if (!engineReady)
            {
                if (GetCoreFlightTechCandidatePorts().Count == 0)
                {
                    ShowDeviceRequired();
                    return false;
                }
                state.Text = "INITIALIZATION FAILED";
                state.ForeColor = dot.ForeColor = Color.FromArgb(248, 113, 113);
                action.Text = "NOT READY";
                return false;
            }

            Native.ShowWindow(mobiFlightWindow, Native.SW_HIDE);
            SetConnected(false);
            ReadMobiFlightStatus();
            return true;
        }
        finally
        {
            engineInitializing = false;
            action.Enabled = engineReady;
        }
    }

    private async Task Connect()
    {
        if (!await PrepareEngine()) return;

        deviceDisconnectBeingHandled = false;
        lastConnectedModule = null;
        trackedDevicePort = GetCoreFlightTechCandidatePorts().FirstOrDefault();
        if (trackedDevicePort is not null)
        lastConnectedModule = $"Core Flight Tech device ({trackedDevicePort})";

        SetBusy("CONNECTING…");
        Native.PostMessage(mobiFlightWindow, Native.WM_COREFLIGHTTECH_RUN, IntPtr.Zero, IntPtr.Zero);
        Native.ShowWindow(mobiFlightWindow, Native.SW_HIDE);
        await Task.Delay(150);
        SetConnected(true);
        ReadMobiFlightStatus();
    }

    private async Task Disconnect()
    {
        SetBusy("DISCONNECTING…");
        var process = mobiFlight is { HasExited: false } ? mobiFlight : FindEngineProcess();
        if (process is not null && !process.HasExited)
        {
            process.Refresh();
            var window = mobiFlightWindow != IntPtr.Zero && Native.IsWindow(mobiFlightWindow)
                ? mobiFlightWindow
                : FindWindowForProcess(process);
            if (window != IntPtr.Zero)
            {
                Native.PostMessage(window, Native.WM_COREFLIGHTTECH_STOP, IntPtr.Zero, IntPtr.Zero);
                Native.ShowWindow(window, Native.SW_HIDE);
                await Task.Delay(250);
            }
        }
        SetConnected(false);
    }

    private void ShutdownEngine()
    {
        monitor.Stop();
        deviceWarningDialog?.Close();
        var process = mobiFlight is { HasExited: false } ? mobiFlight : FindEngineProcess();
        if (process is null || process.HasExited) return;

        var window = mobiFlightWindow != IntPtr.Zero && Native.IsWindow(mobiFlightWindow)
            ? mobiFlightWindow
            : FindWindowForProcess(process);
        if (window != IntPtr.Zero)
            Native.PostMessage(window, Native.WM_COREFLIGHTTECH_SHUTDOWN, IntPtr.Zero, IntPtr.Zero);
    }

    private async Task HandleDisconnectedModule(string moduleName)
    {
        if (deviceDisconnectBeingHandled || !connected) return;

        deviceDisconnectBeingHandled = true;
        action.Enabled = false;
        try
        {
            await Disconnect();
        }
        finally
        {
            lastConnectedModule = null;
            trackedDevicePort = null;
            deviceDisconnectBeingHandled = false;
            deviceWarningShown = true; // The disconnect popup already informed the user.
            ShowDeviceRequired();
        }
    }

    private Process? FindEngineProcess()
    {
        var expectedPath = Path.GetFullPath(EngineExe);
        foreach (var process in Process.GetProcessesByName("CFTAllInOneConnectorEngine"))
        {
            try
            {
                if (!process.HasExited && string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? ""), expectedPath, StringComparison.OrdinalIgnoreCase))
                    return process;
            }
            catch { }
        }
        return null;
    }

    private static IntPtr FindWindowForProcess(Process process)
    {
        if (process.HasExited) return IntPtr.Zero;
        process.Refresh();
        if (process.MainWindowHandle != IntPtr.Zero) return process.MainWindowHandle;

        var result = IntPtr.Zero;
        Native.EnumWindows((window, _) =>
        {
            Native.GetWindowThreadProcessId(window, out var processId);
            if (processId != (uint)process.Id) return true;

            var title = new StringBuilder(512);
            Native.GetWindowText(window, title, title.Capacity);
            if (!title.ToString().Contains("MobiFlight Connector", StringComparison.OrdinalIgnoreCase))
                return true;

            result = window;
            return false;
        }, IntPtr.Zero);
        return result;
    }

    private void CheckProcess() { if (connected && !(mobiFlight is { HasExited: false }) && FindEngineProcess() is null) SetConnected(false); }
    private void ReadMobiFlightStatus()
    {
        var process = mobiFlight is { HasExited: false } ? mobiFlight : FindEngineProcess();
        try
        {
            if (!File.Exists(EngineLog)) return;
            var lines = File.ReadLines(EngineLog).TakeLast(500).ToArray();
            var ports = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var acceptedDevices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                var acceptedMatch = Regex.Match(line, @"Dedicated mode: accepted '(.+?)'.*? on (COM\d+)", RegexOptions.IgnoreCase);
                if (acceptedMatch.Success)
                    acceptedDevices[acceptedMatch.Groups[2].Value] = acceptedMatch.Groups[1].Value.Trim();
                var connectedMatch = Regex.Match(line, @"Connected to .+? at (COM\d+) of type (.+?) \(", RegexOptions.IgnoreCase);
                if (connectedMatch.Success)
                {
                    ports[connectedMatch.Groups[1].Value] = connectedMatch.Groups[2].Value.Trim();
                    continue;
                }
                var removedMatch = Regex.Match(line, @"Port disappeared:\s*(COM\d+)", RegexOptions.IgnoreCase);
                if (removedMatch.Success)
                {
                    ports.Remove(removedMatch.Groups[1].Value);
                    acceptedDevices.Remove(removedMatch.Groups[1].Value);
                }
            }

            var presentPorts = GetPresentSerialPorts();
            foreach (var port in ports.Keys.Where(x => !presentPorts.Contains(x)).ToArray()) ports.Remove(port);
            foreach (var port in ports.Keys.Where(x => !acceptedDevices.ContainsKey(x)).ToArray()) ports.Remove(port);

            var candidatePorts = GetCoreFlightTechCandidatePorts();
            var connectedModuleNames = ports
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .Select(x => $"{acceptedDevices.GetValueOrDefault(x.Key, FriendlyDeviceName(x.Value))} ({x.Key})")
                .ToArray();
            devicesValue.Text = connectedModuleNames.Length > 0
                ? string.Join(Environment.NewLine, connectedModuleNames)
                : candidatePorts.Count > 0
                    ? string.Join(Environment.NewLine, candidatePorts.OrderBy(x => x).Select(x => $"Core Flight Tech device ({x})"))
                    : "Not found";

            if (connectedModuleNames.Length > 0)
            {
                trackedDevicePort = ports.Keys.First();
                lastConnectedModule = connectedModuleNames[0];
            }
            else if (connected && candidatePorts.Count > 0)
            {
                trackedDevicePort ??= candidatePorts.First();
                lastConnectedModule ??= $"Core Flight Tech device ({trackedDevicePort})";
            }

            if (connected && trackedDevicePort is not null && !presentPorts.Contains(trackedDevicePort) && !deviceDisconnectBeingHandled)
            {
                var disconnectedModule = lastConnectedModule ?? $"Core Flight Tech device ({trackedDevicePort})";
                _ = HandleDisconnectedModule(disconnectedModule);
            }

            var simConnected = lines.LastOrDefault(x => Regex.IsMatch(x, @"((connected to\s+)?X.?Plane|SimConnect|FSUIPC).*(connected|connection established|opened|detected)|connected to\s+X.?Plane", RegexOptions.IgnoreCase));
            var simLost = lines.LastOrDefault(x => Regex.IsMatch(x, @"(connection lost|disconnected|connection failed|no simulator)", RegexOptions.IgnoreCase));
            if (process is not null && !process.HasExited && simConnected is not null && (simLost is null || Array.LastIndexOf(lines, simConnected) > Array.LastIndexOf(lines, simLost)))
                simulatorValue.Text = simConnected.Contains("X-Plane", StringComparison.OrdinalIgnoreCase) ? "X-Plane Detected"
                    : simConnected.Contains("SimConnect", StringComparison.OrdinalIgnoreCase) ? "Microsoft Flight Simulator Detected"
                    : "Simulator Detected";
            else simulatorValue.Text = "Waiting for simulator";

            string? aircraft = null;
            foreach (var line in lines.Reverse())
            {
                var match = Regex.Match(line, @"aircraft.*?(?:changed|detected|name).*?[:=]\s*(.+)$", RegexOptions.IgnoreCase);
                if (match.Success) { aircraft = match.Groups[1].Value.Trim(); break; }
            }
            aircraftValue.Text = aircraft ?? "No aircraft detected";
        }
        catch { }
    }

    private enum SimulatorFamily
    {
        None,
        MicrosoftFlightSimulator,
        XPlane
    }

    private static SimulatorFamily DetectSimulatorFamily()
    {
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    var name = process.ProcessName;
                    if (name.StartsWith("X-Plane", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("XPlane", StringComparison.OrdinalIgnoreCase))
                        return SimulatorFamily.XPlane;
                    if (name.Equals("FlightSimulator", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("FlightSimulator2024", StringComparison.OrdinalIgnoreCase))
                        return SimulatorFamily.MicrosoftFlightSimulator;
                }
            }
        }
        catch { }
        return SimulatorFamily.None;
    }

    private static HashSet<string> GetPresentSerialPorts()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key is null) return result;
            foreach (var name in key.GetValueNames())
                if (key.GetValue(name) is string port && !string.IsNullOrWhiteSpace(port)) result.Add(port);
        }
        catch { }
        return result;
    }

    private static HashSet<string> GetCoreFlightTechCandidatePorts()
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var presentPorts = GetPresentSerialPorts();
        try
        {
            // CoreFlightTech NAV uses the CH340 USB serial bridge (VID 1A86, PID 7523).
            // This is detection only; the engine still verifies the firmware module name.
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB\VID_1A86&PID_7523");
            if (root is null) return result;
            foreach (var instanceName in root.GetSubKeyNames())
            {
                using var parameters = root.OpenSubKey($@"{instanceName}\Device Parameters");
                if (parameters?.GetValue("PortName") is string port && !String.IsNullOrWhiteSpace(port) && presentPorts.Contains(port)) result.Add(port);
            }
        }
        catch { }
        return result;
    }

    private static string FriendlyDeviceName(string technicalName) =>
        technicalName.Equals("MobiFlight Mega", StringComparison.OrdinalIgnoreCase)
            ? "CoreFlightTech NAV (CPT)"
            : technicalName;

    private void ResetDetails()
    {
        devicesValue.Text = "Not found"; simulatorValue.Text = "Not found"; aircraftValue.Text = "No aircraft detected";
    }
    private void SetBusy(string text) { state.Text = text; state.ForeColor = dot.ForeColor = Color.FromArgb(250, 204, 21); }
    private void SetConnected(bool value)
    {
        connected = value; state.Text = value ? "CONNECTED" : "DISCONNECTED";
        state.ForeColor = value ? Color.FromArgb(74, 222, 128) : Color.FromArgb(148, 163, 184);
        dot.ForeColor = value ? Color.FromArgb(34, 197, 94) : Color.FromArgb(100, 116, 139);
        action.Text = value ? "DISCONNECT" : "CONNECT"; action.BackColor = value ? Color.FromArgb(220, 38, 38) : Color.FromArgb(37, 99, 235);
        if (!value) ResetDetails();
    }
}

internal static class Native
{
    internal delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    internal const int SW_HIDE = 0;
    internal const uint WM_COREFLIGHTTECH_RUN = 0x8000 + 737;
    internal const uint WM_COREFLIGHTTECH_STOP = 0x8000 + 738;
    internal const uint WM_COREFLIGHTTECH_SHUTDOWN = 0x8000 + 739;
    [DllImport("user32.dll")] internal static extern bool ShowWindow(IntPtr hWnd, int command);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);
}
