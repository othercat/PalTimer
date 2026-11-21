using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace Pal98Timer
{
    // Only immutable display values cross from the timer core to the overlay's STA.
    // All form creation, painting, resizing and disposal belong to that STA.
    internal sealed class TimerOverlayHost : IDisposable
    {
        private readonly SynchronizationContext owner;
        private readonly Func<Dx9OverlaySnapshot> sample;
        private Action<Dx9OverlayLayoutSettings> persist;
        private readonly string title;
        private readonly object startup = new object();
        private readonly Queue<Action<Dx9OverlayForm>> pending = new Queue<Action<Dx9OverlayForm>>();
        private Control dispatcher;
        private Dx9OverlayForm window;
        private volatile Dx9OverlaySnapshot latest;
        private volatile Dx9OverlayLayoutSettings layout;
        private volatile bool editing;
        private volatile bool closed;
        private bool started;
        private int nextSample;

        internal event EventHandler EditModeChanged;
        internal event EventHandler Closed;
        internal event Action<Exception> Failed;
        internal bool IsDisposed => closed;
        internal bool IsInEditMode => editing;
        internal bool CaptureWindow => layout.CaptureWindow;
        internal string ConfiguredFontFamily => layout.FontFamily;
        internal float ConfiguredFontSize => layout.FontSize;
        internal FontStyle ConfiguredFontStyle => layout.FontStyle;
        internal int ConfiguredFontColorArgb => layout.FontColorArgb;
        internal Keys ConfiguredToggleHotkey => layout.ToggleHotkey;

        internal TimerOverlayHost(string title, Func<Dx9OverlaySnapshot> sample,
            Dx9OverlayLayoutSettings initial, Action<Dx9OverlayLayoutSettings> persist)
        {
            owner = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            this.title = title;
            this.sample = sample;
            this.persist = persist;
            layout = initial.Clone();
            nextSample = Environment.TickCount;
        }

        internal void Start()
        {
            lock (startup)
            {
                if (started || closed) return;
                started = true;
            }
            var thread = new Thread(Run) { IsBackground = true, Name = "PalTimer OBS" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        // Called by the existing timing worker after its timing/competition tick.
        // Losing an optional display frame must never interrupt timing.
        internal void Sample()
        {
            int now = Environment.TickCount;
            if (closed || unchecked(now - nextSample) < 0) return;
            nextSample = unchecked(now + 100);
            try { latest = sample(); }
            catch { latest = null; }
        }

        private void Run()
        {
            try
            {
                using (var pump = new Control())
                using (var form = new Dx9OverlayForm(() => latest, layout, SaveLayout))
                {
                    var handle = pump.Handle;
                    form.Text = title;
                    form.LayoutSaveFailed += Report;
                    form.EditModeChanged += (s, e) =>
                    {
                        editing = form.IsInEditMode;
                        Notify(() => EditModeChanged?.Invoke(this, EventArgs.Empty));
                    };
                    form.FormClosed += (s, e) => Application.ExitThread();
                    lock (startup)
                    {
                        if (closed) return;
                        window = form;
                        dispatcher = pump;
                        while (pending.Count > 0) EnqueueOnSta(pending.Dequeue());
                    }
                    form.Start();
                    Application.Run();
                }
            }
            catch (Exception error) { Report(error); }
            finally
            {
                bool notify;
                lock (startup)
                {
                    notify = !closed;
                    closed = true;
                    dispatcher = null;
                    window = null;
                    pending.Clear();
                }
                if (notify) Notify(() => Closed?.Invoke(this, EventArgs.Empty));
            }
        }

        private void SaveLayout(Dx9OverlayLayoutSettings value)
        {
            var copy = value.Clone();
            persist(copy);
            layout = copy;
            Notify(() => EditModeChanged?.Invoke(this, EventArgs.Empty));
        }

        private void Notify(Action action)
        {
            try { owner.Post(_ => action(), null); }
            catch (InvalidOperationException) { /* main application has exited */ }
        }

        private void Report(Exception error) => Notify(() => Failed?.Invoke(error));

        private void EnqueueOnSta(Action<Dx9OverlayForm> action)
        {
            try
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    if (closed) return;
                    try { action(window); }
                    catch (Exception error) { Report(error); }
                }));
            }
            catch (InvalidOperationException) { /* already closing */ }
        }

        private void Post(Action<Dx9OverlayForm> action)
        {
            lock (startup)
            {
                if (closed) return;
                if (dispatcher == null) pending.Enqueue(action);
                else EnqueueOnSta(action);
            }
        }

        internal void ToggleEditMode() => Post(f =>
        {
            if (f.IsInEditMode) f.EndEditMode();
            else if (!f.BeginEditMode()) throw new InvalidOperationException("请先打开游戏，或选择直播伴侣窗口采集模式。");
        });
        internal void ToggleCaptureWindow() => Post(f => f.SetCaptureWindow(!f.CaptureWindow));
        internal void SwitchLayout(Dx9OverlayLayoutSettings value, Action<Dx9OverlayLayoutSettings> save)
        {
            var copy = value.Clone();
            Post(f =>
            {
                if (f.IsInEditMode) f.EndEditMode();
                persist = save;
                layout = copy;
                f.LoadLayout(copy);
                editing = f.IsInEditMode;
                Notify(() => EditModeChanged?.Invoke(this, EventArgs.Empty));
            });
        }
        internal void ApplyFont(string family, float size, FontStyle style) => Post(f => f.ApplyFont(family, size, style));
        internal void ApplyFontColor(Color color) => Post(f => f.ApplyFontColor(color));
        internal void ApplyToggleHotkey(Keys value) => Post(f => f.ApplyToggleHotkey(value));
        internal void ResetLayout() => Post(f => f.ResetLayout());

        public void Dispose()
        {
            lock (startup)
            {
                if (closed) return;
                closed = true;
                pending.Clear();
                // The owner never waits for a paint or joins an STA from a hotkey.
                try { dispatcher?.BeginInvoke(new Action(Application.ExitThread)); }
                catch (InvalidOperationException) { /* already closing */ }
            }
        }
    }
}
