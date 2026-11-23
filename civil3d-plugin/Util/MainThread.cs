using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices.Core;

namespace Civil3DMcp
{
    /// <summary>
    /// Civil 3D crashes if the drawing is changed from a background thread.
    /// Work is queued, Civil 3D is woken with a harmless Windows message,
    /// and the queue is processed in Civil 3D's Idle event on the main thread.
    /// While a modal dialog is open Civil 3D never goes idle (tested on 2025), so a job that hasn't started soon
    /// is taken back out of the queue and the caller is told what is in the way (see Busy).
    /// </summary>
    internal static class MainThread
    {
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder s, int max);
        private const uint WM_NULL = 0x0000, GW_ENABLEDPOPUP = 6;

        /// <summary>State: 0 waiting, 1 running (or done), 2 taken back (never runs).</summary>
        private sealed class Job
        {
            public Func<object> Fn;
            public readonly TaskCompletionSource<object> Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int State;
        }

        private static readonly ConcurrentQueue<Job> Queue = new();
        private static int _mainThreadId;
        private static IntPtr _hwnd;

        public static void Init()
        {
            _mainThreadId = Environment.CurrentManagedThreadId;
            try { _hwnd = Application.MainWindow.Handle; } catch { _hwnd = IntPtr.Zero; }
            Application.Idle += OnIdle;
        }

        private static void OnIdle(object sender, EventArgs e)
        {
            // Let command text we queued (UNDO, ZOOM...) run first; Run() keeps nudging until it has.
            if (UndoTracker.Busy()) return;
            while (Queue.TryDequeue(out var job))
            {
                if (Interlocked.CompareExchange(ref job.State, 1, 0) != 0) continue;   // the caller gave up on it
                try { job.Tcs.SetResult(job.Fn()); }
                catch (Exception ex) { job.Tcs.SetException(ex); }
            }
        }

        /// <summary>What keeps Civil 3D from taking requests, read with plain Windows calls (safe on any thread):
        /// a modal dialog disables the main window. Null when nothing is in the way.</summary>
        private static string Blocker()
        {
            if (_hwnd == IntPtr.Zero || IsWindowEnabled(_hwnd)) return null;
            var popup = GetWindow(_hwnd, GW_ENABLEDPOPUP);
            var title = new StringBuilder(256);
            if (popup != IntPtr.Zero) GetWindowText(popup, title, title.Capacity);
            return title.Length > 0 ? $"the '{title}' dialog box is open" : "a dialog box is open";
        }

        public static T Run<T>(Func<T> fn)
        {
            if (Environment.CurrentManagedThreadId == _mainThreadId) return fn();
            var job = new Job { Fn = () => fn() };
            Queue.Enqueue(job);
            if (_hwnd == IntPtr.Zero) try { _hwnd = Application.MainWindow.Handle; } catch { }
            if (_hwnd != IntPtr.Zero) PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            var waited = Stopwatch.StartNew();
            // Keep nudging Civil 3D until it picks the job up (it may be busy with a command).
            while (!job.Tcs.Task.Wait(250))
            {
                if (_hwnd != IntPtr.Zero) PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
                if (Volatile.Read(ref job.State) != 0) continue;                     // running: wait for it
                string why = waited.Elapsed.TotalSeconds > 1.5 ? Blocker() : null;
                if (why == null && waited.Elapsed.TotalSeconds > 60)
                    why = "it didn't take the request within a minute (a long command may be running)";
                // Take the job back so it can't run later, after Claude was told it failed.
                if (why != null && Interlocked.CompareExchange(ref job.State, 2, 0) == 0)
                    throw new InvalidOperationException(
                        $"Civil 3D is busy: {why}. Nothing was run. Close it or finish what is open in Civil 3D, then try again.");
            }
            return (T)job.Tcs.Task.GetAwaiter().GetResult();
        }
    }
}
