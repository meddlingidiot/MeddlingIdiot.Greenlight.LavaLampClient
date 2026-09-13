using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Greenlight.Sdk;
using Greenlight.Sdk.Protocol;

namespace Greenlight.LavaLampClient;

/// <summary>
/// The whole of the Greenlight integration, which is the point of the sample: attach, translate
/// the colour, and never care whether Greenlight is actually there.
/// </summary>
/// <remarks>
/// The tray icon, the overlay and edit mode are ordinary Avalonia and have nothing to do with
/// Greenlight — the integration is still the twenty-odd lines in
/// <see cref="StartWatchingGreenlight"/>.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class App : Application
{
    private GreenlightClient? _greenlight;
    private LampWindow? _window;
    private LampTray? _tray;
    private LampConfig _config = new();

    /// <summary>
    /// The last thing Greenlight said. Held here rather than only in the scene because the scene
    /// comes and goes — put away, brought back, rebuilt after a reload — and a lamp that came
    /// back green after a restart would be the toy lying.
    /// </summary>
    private LampState _state = LampState.Off;

    private bool _building;

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // The overlay is closed and reopened by the tray's put-away/bring-back, and there is
            // no other window — on the default setting, putting the lamp away would quit the
            // whole thing and take the tray icon with it.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _config = LampConfig.Load();

            // If Windows is set to start this, make sure it is still pointed at the right
            // executable. An update moves the versioned copy out from under an older
            // registration, and the symptom is the lamp silently not coming back one morning —
            // weeks after anybody touched the setting.
            WindowsStartup.Refresh();

            _tray = new LampTray(_config)
            {
                IsRunning = () => _window is not null,
                OnSetRunning = running =>
                {
                    if (running) ShowLamp();
                    else HideLamp();
                },
                IsEditing = () => _window?.IsEditing ?? false,

                // Turning the mode off from the menu keeps the arrangement, the same as the tick.
                // Somebody who has dragged the lamp and then gone back to the tray has said what
                // they meant; the cross is there for the other answer.
                OnSetEditing = editing =>
                {
                    if (editing) BeginEdit();
                    else EndEdit(keep: true);
                },
                OnConfigChanged = () => _window?.ApplyConfig(),
                OnReloadConfig = ReloadConfig,
                OnQuit = () => desktop.Shutdown(),
            };

            ShowLamp();
            StartWatchingGreenlight();

            desktop.Exit += async (_, _) =>
            {
                _tray?.Dispose();
                if (_greenlight is not null) await _greenlight.DisposeAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartWatchingGreenlight()
    {
        _greenlight = new GreenlightClient();

        // Both of these arrive on a background thread — the SDK says so, loudly, and this is what
        // it means in practice. Touching the scene from the pipe's thread would be a race against
        // the render loop reading the same wax.
        _greenlight.Changed += (_, e) => Apply(Translate(e.Snapshot.Status), e.Snapshot.IsBuilding);
        _greenlight.AvailabilityChanged += (_, e) =>
        {
            // Anything other than Connected means we have nothing to show, and switching the lamp
            // off is more honest than leaving green wax running on stale data.
            if (e.Availability != GreenlightAvailability.Connected) Apply(LampState.Off, building: false);
        };

        // Deliberately not awaited and deliberately not guarded: StartAsync returns as soon as the
        // background loop is running, and an absent Greenlight is not an error. The lamp sits
        // unlit until one turns up, then warms up on its own.
        _ = _greenlight.StartAsync();
    }

    private void ShowLamp()
    {
        if (_window is not null) return;

        _window = new LampWindow(_config, new LampScene(_config.Lamp))
        {
            Scene =
            {
                State = _state,
                IsBuilding = _building,
                ShowWhenOff = _config.ShowWhenOff,
                Liveliness = _config.Liveliness,
            },
        };

        _window.EditSaved += (_, _) => EndEdit(keep: true);
        _window.EditCancelled += (_, _) => EndEdit(keep: false);

        _window.Show();
        _tray?.ShowState(_state, _building);
    }

    private void HideLamp()
    {
        // Deliberately before the close, and keeping what was dragged: leaving edit mode is what
        // puts the click-through styles back, and a window destroyed mid-edit would take the
        // desktop's mouse with it until the lamp was brought out again.
        if (_window is not null && _window.IsEditing) EndEdit(keep: true);

        _window?.Close();
        _window = null;
        _tray?.ShowState(_state, _building);
    }

    private void BeginEdit()
    {
        // Nothing to move while it is put away, and turning the mode on would leave a closed
        // window holding an interactive overlay nobody can see.
        if (_window is null || _window.IsEditing) return;

        _window.IsEditing = true;
        _tray?.ShowState(_state, _building);
    }

    /// <summary>
    /// Leave edit mode. <paramref name="keep"/> writes the arrangement to the file; without it
    /// the scene puts the lamp back where it was when the mode started.
    /// </summary>
    private void EndEdit(bool keep)
    {
        if (_window is null || !_window.IsEditing) return;

        // The scene's snapshot is restored before the mode is turned off, so the frame that draws
        // without the chrome already has the lamp back in its old place — otherwise a cancel shows
        // one frame of the dragged position, which reads as the cancel not working.
        if (keep) _window.Scene.CommitEdit();
        else _window.Scene.CancelEdit();

        _window.IsEditing = false;

        // Written once, at the end, rather than on every frame of the drag: the file would
        // otherwise take a few hundred writes to move one lamp across the desk.
        if (keep) _config.Save();

        _tray?.ShowState(_state, _building);
    }

    /// <summary>Re-read the file, for colours changed by hand while this was running.</summary>
    private void ReloadConfig()
    {
        _config.CopyFrom(LampConfig.Load());

        // The placement object is the one thing the overlay cannot absorb: the scene holds the
        // instance it was built with, and a reload hands the config a new one.
        if (_window is null) return;

        HideLamp();
        ShowLamp();
    }

    private static LampState Translate(GreenlightStatus status) => status switch
    {
        GreenlightStatus.Green => LampState.Green,
        GreenlightStatus.Yellow => LampState.Amber,
        GreenlightStatus.Red => LampState.Red,
        _ => LampState.Off,
    };

    private void Apply(LampState state, bool building) =>
        Dispatcher.UIThread.Post(() =>
        {
            _state = state;
            _building = building;

            if (_window is not null)
            {
                _window.Scene.State = state;

                // A build under way makes it breathe rather than recolour: Greenlight's own rule
                // is that a broken pipeline stays red while it rebuilds, and a toy that went
                // yellow the moment the fix started would be contradicting it.
                _window.Scene.IsBuilding = building;
            }

            _tray?.ShowState(state, building);
        });
}
