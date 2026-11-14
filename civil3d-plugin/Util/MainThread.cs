using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices.Core;

namespace Civil3DMcp
{
    /// <summary>
    /// Civil 3D crashes if the drawing is changed from a background thread.
    /// Work is queued, Civil 3D is woken with a harmless Windows message,
    /// and the queue is processed in Civil 3D's Idle event on the main thread.
    /// </summary>
    internal static class MainThread
    {
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
        private const uint WM_NULL = 0x0000;

        private static readonly ConcurrentQueue<(Func<object> fn, TaskCompletionSource<object> tcs)> Queue = new();
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
            while (Queue.TryDequeue(out var job))
            {
                try { job.tcs.SetResult(job.fn()); }
                catch (Exception ex) { job.tcs.SetException(ex); }
            }
        }

        public static T Run<T>(Func<T> fn)
        {
            if (Environment.CurrentManagedThreadId == _mainThreadId) return fn();
            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            Queue.Enqueue((() => fn(), tcs));
            if (_hwnd == IntPtr.Zero) try { _hwnd = Application.MainWindow.Handle; } catch { }
            if (_hwnd != IntPtr.Zero) PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            // Keep nudging Civil 3D until it picks the job up (it may be busy with a command).
            while (!tcs.Task.Wait(250))
                if (_hwnd != IntPtr.Zero) PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);
            return (T)tcs.Task.GetAwaiter().GetResult();
        }
    }
}
