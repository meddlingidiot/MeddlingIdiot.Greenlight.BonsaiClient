using System.Diagnostics;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Greenlight.Sdk;
using Greenlight.Sdk.Protocol;

namespace Greenlight.BonsaiClient;

/// <summary>
/// The whole of the Greenlight integration: attach, hand each snapshot to the gardener, and
/// never care whether Greenlight is actually there.
/// </summary>
/// <remarks>
/// The one thing this client does that the others do not is remember. The gardener and its
/// tree live here rather than in the window, because the tree has to keep wilting while it is
/// put away — a broken build does not stop being broken because nobody is looking at the pot.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class App : Application
{
    /// <summary>How often the tree is tended — wilted or healed — when nothing else is happening.</summary>
    private static readonly TimeSpan TendEvery = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The longest the tree goes unsaved while it is changing. Every snapshot that grows or breaks
    /// it saves on the next tend; slow wilting only needs writing down now and then.
    /// </summary>
    private static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(60);

    private GreenlightClient? _greenlight;
    private BonsaiWindow? _window;
    private BonsaiTray? _tray;
    private BonsaiConfig _config = new();
    private Gardener _gardener = null!;
    private DispatcherTimer? _tending;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastTend;
    private TimeSpan _lastSave;
    private bool _saveSoon;

    private GreenlightStatus _status = GreenlightStatus.Unknown;
    private bool _building;
    private IReadOnlyList<WaitingReview> _waiting = [];

    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _config = BonsaiConfig.Load();

            var life = Gardener.Load();
            _gardener = new Gardener(life, BonsaiTree.Grow(life.Seed));
            ApplyGardening();

            WindowsStartup.Refresh();

            _tray = new BonsaiTray(_config)
            {
                IsRunning = () => _window is not null,
                OnSetRunning = running =>
                {
                    if (running) ShowTree();
                    else HideTree();
                },
                IsEditing = () => _window?.IsEditing ?? false,
                OnSetEditing = editing =>
                {
                    if (editing) BeginEdit();
                    else EndEdit(keep: true);
                },
                OnConfigChanged = () => _window?.ApplyConfig(),
                OnReloadConfig = ReloadConfig,
                OnQuit = () => desktop.Shutdown(),
            };

            ShowTree();
            UpdateTray();

            _lastTend = _clock.Elapsed;
            _tending = new DispatcherTimer { Interval = TendEvery };
            _tending.Tick += (_, _) => Tend();
            _tending.Start();

            StartWatchingGreenlight();

            desktop.Exit += async (_, _) =>
            {
                _tending?.Stop();
                Gardener.Save(_gardener.Life);
                _tray?.Dispose();
                if (_greenlight is not null) await _greenlight.DisposeAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void StartWatchingGreenlight()
    {
        _greenlight = new GreenlightClient();

        // Both arrive on a background thread. The gardener and the scene are the UI thread's.
        _greenlight.Changed += (_, e) => Dispatcher.UIThread.Post(() => Observe(e.Snapshot));
        _greenlight.AvailabilityChanged += (_, e) =>
        {
            if (e.Availability != GreenlightAvailability.Connected)
                Dispatcher.UIThread.Post(GoDormant);
        };

        // Not awaited: an absent Greenlight is not an error. The tree sits dormant until one turns up.
        _ = _greenlight.StartAsync();
    }

    private void Observe(GreenlightSnapshot snapshot)
    {
        // Time up to now was spent in the old colour; tend it before the colour changes, or the
        // first five seconds of a fix would be counted as more wilting.
        Tend();

        var observed = _gardener.Observe(snapshot);
        if (observed.Broken > 0) _window?.Scene.MarkBroken(observed.Broken);
        if (observed.Grown > 0 || observed.Broken > 0) _saveSoon = true;

        _status = snapshot.Status;
        _building = snapshot.IsBuilding;

        var now = DateTimeOffset.Now;
        _waiting = snapshot.PullRequests
            .Where(pr => pr.IsReviewRequestedFromMe && pr.Status == GreenlightPullRequestStatus.Active)
            .Select(pr => new WaitingReview($"{pr.Provider}:{pr.Repo}:{pr.Id}", now - pr.CreatedAt))
            .ToList();

        ApplyToScene(quietly: false);
        UpdateTray();
    }

    /// <summary>
    /// Greenlight has gone. The tree goes dormant — grey and still, neither dying nor healing —
    /// and the caterpillars leave without turning into anything, because nobody reviewed them.
    /// </summary>
    private void GoDormant()
    {
        Tend();

        _status = GreenlightStatus.Unknown;
        _building = false;
        _waiting = [];

        ApplyToScene(quietly: true);
        UpdateTray();
    }

    private void Tend()
    {
        var now = _clock.Elapsed;
        var elapsed = now - _lastTend;
        _lastTend = now;

        // A machine that slept through the night comes back with one enormous tick. The tree
        // should not have wilted while the screen it was on was off — and Greenlight was not
        // watching the builds then either.
        if (elapsed > TimeSpan.FromMinutes(2)) elapsed = TendEvery;

        _gardener.Tend(elapsed, _status);

        if (_gardener.IsDirty && (_saveSoon || now - _lastSave >= SaveEvery))
        {
            Gardener.Save(_gardener.Life);
            _gardener.IsDirty = false;
            _saveSoon = false;
            _lastSave = now;
        }

        UpdateTray();
    }

    private void ApplyGardening()
    {
        _gardener.DecayHours = _config.DecayHours;
        _gardener.RecoveryHours = _config.RecoveryHours;
    }

    private void ApplyToScene(bool quietly)
    {
        if (_window is null) return;

        _window.Scene.Mood = Translate(_status);
        _window.Scene.IsBuilding = _building;
        _window.Scene.SetWaiting(_waiting, quietly);
    }

    private void UpdateTray()
    {
        _tray?.ShowState(Translate(_status), _building);
        _tray?.ShowLife(_gardener.Life, _gardener.VisibleLeaves);
    }

    private void ShowTree()
    {
        if (_window is not null) return;

        _window = new BonsaiWindow(_config, new BonsaiScene(_config.Tree, _gardener)
        {
            ShowWhenOff = _config.ShowWhenOff,
            Liveliness = _config.Liveliness,
        });

        // Already there rather than arriving: a caterpillar that has been on the tree all along
        // should not make an entrance every time the tree is brought back out.
        ApplyToScene(quietly: true);

        _window.EditSaved += (_, _) => EndEdit(keep: true);
        _window.EditCancelled += (_, _) => EndEdit(keep: false);

        _window.Show();
        UpdateTray();
    }

    private void HideTree()
    {
        if (_window is not null && _window.IsEditing) EndEdit(keep: true);

        _window?.Close();
        _window = null;
        UpdateTray();
    }

    private void BeginEdit()
    {
        if (_window is null || _window.IsEditing) return;

        _window.IsEditing = true;
        UpdateTray();
    }

    private void EndEdit(bool keep)
    {
        if (_window is null || !_window.IsEditing) return;

        if (keep) _window.Scene.CommitEdit();
        else _window.Scene.CancelEdit();

        _window.IsEditing = false;

        if (keep) _config.Save();

        UpdateTray();
    }

    private void ReloadConfig()
    {
        _config.CopyFrom(BonsaiConfig.Load());
        ApplyGardening();

        if (_window is null) return;

        HideTree();
        ShowTree();
    }

    private static TreeMood Translate(GreenlightStatus status) => status switch
    {
        GreenlightStatus.Green => TreeMood.Green,
        GreenlightStatus.Yellow => TreeMood.Amber,
        GreenlightStatus.Red => TreeMood.Red,
        _ => TreeMood.Off,
    };
}
