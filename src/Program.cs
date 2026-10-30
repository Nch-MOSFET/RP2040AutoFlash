using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("RP2040 AutoFlash")]
[assembly: AssemblyProduct("RP2040 AutoFlash")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace RP2040AutoFlash
{
    static class Program
    {
        const string MutexName = "RP2040AutoFlash_SingleInstance";
        const string ShowEventName = "RP2040AutoFlash_ShowWindow";

        [STAThread]
        static void Main(string[] args)
        {
            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // 既に起動しているなら、そちらの画面を前に出して終わる
                    try
                    {
                        using (var showEvent = EventWaitHandle.OpenExisting(ShowEventName))
                            showEvent.Set();
                    }
                    catch (WaitHandleCannotBeOpenedException) { }
                    return;
                }

                using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);

                    // --tray はスタートアップ登録から起動したとき。画面を出さずに通知領域で待つ
                    var form = new MainForm(Array.IndexOf(args, "--tray") >= 0);
                    ThreadPool.RegisterWaitForSingleObject(showEvent, (state, timedOut) =>
                    {
                        try { form.BeginInvoke(new Action(form.ShowFromTray)); }
                        catch (InvalidOperationException) { }
                    }, null, Timeout.Infinite, false);

                    Application.Run(form);
                }
            }
        }
    }
}
