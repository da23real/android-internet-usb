using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Internet USB")]
[assembly: System.Reflection.AssemblyVersion("1.2.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.2.0.0")]

internal static class Program {
    internal static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    internal static readonly string LogDirectory = Path.Combine(Root,"Logs");
    private static readonly object logLock = new object();
    [STAThread] static void Main(string[] args) {
        Directory.CreateDirectory(LogDirectory);
        bool first;
        using (Mutex mutex = new Mutex(true,"Local\\Roy.InternetUSB.V1",out first)) {
            if (!first) return;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += delegate(object s,ThreadExceptionEventArgs e) { Log(e.Exception.ToString()); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s,UnhandledExceptionEventArgs e) { Log(e.ExceptionObject.ToString()); };
            Application.Run(new MainForm(Array.IndexOf(args,"--background") >= 0));
        }
    }
    internal static void Log(string text) {
        lock(logLock) {
            try { File.AppendAllText(Path.Combine(LogDirectory,"errors.log"),DateTime.Now.ToString("s") + " " + text + Environment.NewLine); }
            catch (Exception e) { Debug.WriteLine(e); }
        }
    }
}

internal sealed class MainForm : Form {
    internal static bool VerificationMode = false;
    private const string Package="fr.roy.internetusb";
    private string serial;
    private readonly string adb=Path.Combine(Program.Root,"Tools","adb.exe");
    private readonly Label status=new Label();
    private readonly Label detail=new Label();
    private readonly Button pause=new Button();
    private readonly NotifyIcon tray=new NotifyIcon();
    private readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    private bool busy, paused, exiting, allowClose, mapped;
    private string pendingAppCommand;
    private Process relay;
    private int port;
    private readonly RelayJob job=new RelayJob();
    private string lastError="";
    private readonly bool background;

    internal MainForm(bool background) {
        this.background=background;
        Text="Internet USB · V1.02"; ClientSize=new Size(560,400);
        MinimumSize=new Size(540,430); StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Segoe UI",10); BackColor=Color.FromArgb(243,247,251);
        Icon=SystemIcons.Application;
        Label title=new Label { Text="Internet USB",Font=new Font("Segoe UI",24,FontStyle.Bold),ForeColor=Color.FromArgb(22,55,80),AutoSize=true,Location=new Point(25,20) };
        Controls.Add(title);
        Controls.Add(new Label { Text="Internet du PC → Android · par câble USB",AutoSize=true,Location=new Point(28,75) });
        status.SetBounds(28,115,500,35); status.Font=new Font("Segoe UI",14,FontStyle.Bold); Controls.Add(status);
        detail.SetBounds(28,155,500,75); Controls.Add(detail);
        pause.Text="Suspendre tout"; pause.SetBounds(28,238,185,38);
        pause.Click += async delegate { await TogglePause(); }; Controls.Add(pause);
        Button phone=new Button { Text="Ouvrir l’appli sur le téléphone",Location=new Point(225,238),Size=new Size(295,38) };
        phone.Click += async delegate {
            if (busy || exiting) return;
            busy=true;
            try { await Task.Run(() => Device("shell am start -n " + Package + "/.MainActivity")); }
            catch(Exception e) { Report(e); }
            finally { busy=false; }
        }; Controls.Add(phone);
        CheckBox startup=new CheckBox { Text="Lancer en arrière-plan à l’ouverture de Windows",AutoSize=true,Location=new Point(28,294) };
        using(RegistryKey key=Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) {
            startup.Checked=key!=null && key.GetValue("RoyInternetUSB")!=null;
        }
        startup.CheckedChanged += delegate {
            try {
                using(RegistryKey key=Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) {
                    if(startup.Checked) key.SetValue("RoyInternetUSB","\"" + Application.ExecutablePath + "\" --background");
                    else key.DeleteValue("RoyInternetUSB",false);
                }
            } catch(Exception e) { Report(e); }
        }; Controls.Add(startup);
        LinkLabel logs=new LinkLabel { Text="Ouvrir le dossier des journaux",AutoSize=true,Location=new Point(28,333) };
        logs.LinkClicked += delegate { Process.Start("explorer.exe","\"" + Program.LogDirectory + "\""); }; Controls.Add(logs);
        Controls.Add(new Label { Text="Fermer cette fenêtre conserve la connexion dans la zone de notification.",AutoSize=true,Location=new Point(28,365),Font=new Font("Segoe UI",8) });
        ContextMenuStrip menu=new ContextMenuStrip();
        menu.Items.Add("Ouvrir Internet USB",null,delegate { ShowWindow(); });
        menu.Items.Add("Pause / Reprendre",null,async delegate { await TogglePause(); });
        menu.Items.Add("Quitter et arrêter le partage",null,async delegate { await Quit(); });
        tray.Icon=SystemIcons.Application; tray.Text="Internet USB"; tray.Visible=true; tray.ContextMenuStrip=menu;
        tray.DoubleClick += delegate { ShowWindow(); };
        FormClosing += delegate(object s,FormClosingEventArgs e) {
            if (!allowClose && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; Hide(); }
            else { timer.Stop(); StopRelay(); tray.Dispose(); job.Dispose(); }
        };
        timer.Interval=4000;
        timer.Tick += async delegate { await Poll(); };
        Shown += async delegate { if(VerificationMode) return; if(this.background) Hide(); timer.Start(); await Poll(); };
        SetStatus("Recherche du téléphone…","Branche ton téléphone avec un câble USB de données.");
    }
    private void ShowWindow() { Show(); WindowState=FormWindowState.Normal; Activate(); }
    private void SetStatus(string text,string information) { status.Text=text; detail.Text=information; tray.Text=("Internet USB · " + text).Substring(0,Math.Min(63,("Internet USB · " + text).Length)); }
    private void Report(Exception e) {
        if(e.Message!=lastError) { Program.Log(e.ToString()); lastError=e.Message; }
        SetStatus("Connexion indisponible",e.Message);
    }
    private string Run(string file,string arguments,int timeout) {
        ProcessStartInfo info=new ProcessStartInfo(file,arguments) {
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
            WorkingDirectory=Program.Root,StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8
        };
        using(Process p=Process.Start(info)) {
            Task<string> stdout=p.StandardOutput.ReadToEndAsync();
            Task<string> stderr=p.StandardError.ReadToEndAsync();
            if(!p.WaitForExit(timeout)) { try { p.Kill(); } catch(Exception e) { Program.Log(e.ToString()); } throw new IOException("Délai dépassé : " + Path.GetFileName(file)); }
            string result=stdout.Result + stderr.Result;
            if(p.ExitCode!=0 || result.Contains("Exception occurred") || result.Contains("SecurityException") || result.Contains("Error: ")) throw new IOException(result.Trim());
            return result;
        }
    }
    private static string ValidateSerial(string value) {
        value=value.Trim();
        if(!Regex.IsMatch(value,@"\A[A-Za-z0-9_.-]+\z") || value=="unknown")
            throw new IOException("Aucun téléphone USB unique disponible. Branche un seul téléphone et autorise le débogage USB.");
        return value;
    }
    private string Device(string command) {
        if(string.IsNullOrEmpty(serial)) throw new IOException("Aucun téléphone USB sélectionné.");
        return Run(adb,"-s " + serial + " " + command,12000);
    }
    private void SelectUsbDevice() {
        string selected;
        try { selected=ValidateSerial(Run(adb,"-d get-serialno",12000)); }
        catch {
            ReleaseConnection(); serial=null; pendingAppCommand=null;
            throw new IOException("Branche un seul téléphone USB, déverrouille-le et autorise le débogage USB pour ce PC.");
        }
        if(selected!=serial) {
            ReleaseConnection(); pendingAppCommand=null; serial=selected;
        }
    }
    private string Command(string value) { return Device("shell am broadcast --user 0 -a " + Package + ".CONTROL -n " + Package + "/.ControlReceiver --receiver-foreground --es command " + value); }
    private void EnsureRelay() {
        if(relay!=null && !relay.HasExited) return;
        StopRelay(); mapped=false;
        TcpListener candidate=new TcpListener(IPAddress.Loopback,0); candidate.Start();
        port=((IPEndPoint)candidate.LocalEndpoint).Port; candidate.Stop();
        ProcessStartInfo info=new ProcessStartInfo(Path.Combine(Program.Root,"Tools","gnirehtet.exe"),"relay -p " + port) {
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true,WorkingDirectory=Program.Root
        };
        relay=new Process { StartInfo=info };
        DataReceivedEventHandler log=delegate(object sender,DataReceivedEventArgs e) {
            if(e.Data!=null && (e.Data.Contains("ERROR") || e.Data.Contains("WARN") || e.Data.Contains("panicked"))) Program.Log("relay: " + e.Data);
        };
        relay.OutputDataReceived+=log; relay.ErrorDataReceived+=log;
        relay.Start(); job.Assign(relay); relay.BeginOutputReadLine(); relay.BeginErrorReadLine();
        bool ready=false;
        for(int i=0;i<20;i++) {
            if(relay.HasExited) throw new IOException("Le relais USB n’a pas démarré. Consulte errors.log.");
            try { using(TcpClient check=new TcpClient()) { check.Connect(IPAddress.Loopback,port); ready=true; break; } }
            catch(SocketException) { Thread.Sleep(100); }
        }
        if(!ready) { StopRelay(); throw new IOException("Le relais USB ne répond pas."); }
    }
    private void StopRelay() {
        if(relay!=null) {
            try { if(!relay.HasExited) relay.Kill(); } catch(Exception e) { Program.Log(e.ToString()); }
            relay.Dispose(); relay=null;
        }
        mapped=false;
    }
    private async Task Poll() {
        if(busy || exiting) return;
        busy=true;
        try {
            string result=await Task.Run(() => {
                // adb -d selects physical USB only, and refuses multiple USB devices.
                SelectUsbDevice();
                string deviceState=Device("get-state").Trim();
                if(deviceState!="device") { ReleaseConnection(); return deviceState; }
                string installed=Device("shell pm path " + Package);
                if(!installed.Contains("package:")) { StopRelay(); return "missing"; }
                if(pendingAppCommand!=null) { Command(pendingAppCommand); pendingAppCommand=null; }
                string state=Command("status");
                if(!state.Contains("protocol=101;")) { ReleaseConnection(); return "update"; }
                if(state.Contains("suspended=true")) { ReleaseConnection(); return state; }
                if(state.Contains("pending=true")) { ApplyRequestedMode(state); return Command("status"); }
                if(!state.Contains("enabled=true") || !state.Contains("permission=true")) { ReleaseConnection(); return state; }
                EnsureRelay();
                string reverse=Device("reverse --list");
                if(!mapped || !reverse.Contains("localabstract:gnirehtet tcp:" + port)) {
                    Device("reverse localabstract:gnirehtet tcp:" + port); mapped=true;
                }
                return Command("heartbeat");
            });
            lastError="";
            paused=result.Contains("suspended=true"); pause.Text=paused ? "Reprendre" : "Suspendre tout";
            if(result=="absent") SetStatus("En attente du téléphone","Le partage démarrera automatiquement au branchement USB.");
            else if(result=="unauthorized") SetStatus("Autorisation USB requise","Déverrouille le téléphone et autorise le débogage USB pour ce PC.");
            else if(result=="offline") SetStatus("Téléphone hors ligne","Vérifie le câble et déverrouille le téléphone. Nouvelle tentative automatique.");
            else if(result=="missing") SetStatus("Application Android absente","Installe InternetUSB-1.02.apk sur le téléphone.");
            else if(result=="update") SetStatus("Mise à jour Android requise","Installe InternetUSB-1.02.apk sur le téléphone.");
            else if(paused) SetStatus("Application suspendue","Android gère librement l’USB. Reprends depuis le téléphone ou ce bouton.");
            else if(!result.Contains("enabled=true") || !result.Contains("permission=true")) SetStatus("À activer sur le téléphone","Ouvre l’appli sur le téléphone, active la connexion automatique et accepte l’autorisation VPN.");
            else if(result.Contains("state=connected")) SetStatus("Connecté au téléphone par USB",result.Contains("OK via USB") ? "Internet vérifié depuis le téléphone via le PC. Tu peux fermer cette fenêtre." : "Tunnel USB actif. Test Internet : " + ExtractProbe(result));
            else SetStatus("Connexion au téléphone…","Le tunnel démarre automatiquement. Garde le câble branché.");
        } catch(Exception e) { StopRelay(); pendingAppCommand=null; Report(e); }
        finally { busy=false; }
    }
    private static string ExtractProbe(string result) {
        Match m=Regex.Match(result,"probe=([^\"\r\n]+)"); return m.Success ? m.Groups[1].Value : "en attente";
    }
    private static string Field(string state,string key) {
        Match m=Regex.Match(state,@"(?:^|[;""\s])"+Regex.Escape(key)+@"=([^;""\r\n]*)");
        return m.Success ? m.Groups[1].Value : "";
    }
    private static bool ModeMatches(string mode,string actual) {
        string[] functions=actual.Trim().Split(',');
        if(mode=="none") return actual.Trim()=="none" || actual.Trim()=="adb";
        return Array.IndexOf(functions,mode)>=0;
    }
    private void ReleaseConnection() {
        try { if(mapped) Device("reverse --remove localabstract:gnirehtet"); }
        catch(Exception e) { Program.Log(e.ToString()); }
        finally { StopRelay(); }
    }
    private void ApplyRequestedMode(string state) {
        string mode=Field(state,"mode"), id=Field(state,"requestId");
        long requestId;
        if((mode!="mtp" && mode!="midi" && mode!="none") || !long.TryParse(id,out requestId))
            throw new IOException("Demande USB invalide.");
        string claimed=Command("takeMode --el requestId "+id);
        if(!claimed.Contains("accepted=true")) return;
        ReleaseConnection();
        bool ok=false;
        try {
            string latest=Command("status");
            if(latest.Contains("suspended=true") || Field(latest,"requestId")!=id) return;
            // Huawei maps empty/none back to MTP. ADB alone removes files/MIDI while preserving the authorized debugging channel.
            try { Device("shell svc usb setFunctions "+(mode=="none" ? "adb" : mode)); }
            catch(Exception e) { Program.Log("USB mode command/re-enumeration: "+e.Message); }
            DateTime until=DateTime.UtcNow.AddSeconds(18);
            do {
                Thread.Sleep(500);
                try {
                    string actual=Device("shell getprop sys.usb.state");
                    if(ModeMatches(mode,actual)) { ok=true; break; }
                } catch(Exception e) { Program.Log("USB re-enumeration: "+e.Message); }
            } while(DateTime.UtcNow<until);
            if(!ok) Program.Log("USB mode not confirmed: "+mode);
        } catch(Exception e) { Program.Log(e.ToString()); }
        finally {
            try { Command("modeResult --el requestId "+id+" --es result "+(ok ? "ok" : "error")); }
            catch(Exception e) { Program.Log(e.ToString()); }
        }
    }
    private async Task TogglePause() {
        if(exiting) return;
        while(busy && !exiting) await Task.Delay(100);
        if(exiting) return;
        if(string.IsNullOrEmpty(serial)) { SetStatus("Téléphone indisponible","Branche un seul téléphone USB avant cette commande."); return; }
        pendingAppCommand=paused ? "resumeApp" : "suspendApp";
        await Poll();
        pendingAppCommand=null;
    }
    private async Task Quit() {
        if(exiting) return;
        exiting=true; timer.Stop();
        while(busy) await Task.Delay(100);
        try { await Task.Run(() => { try { Command("suspend"); } finally { ReleaseConnection(); } }); }
        catch(Exception e) { Program.Log(e.ToString()); }
        allowClose=true; Close();
    }
}

// The relay must not survive a crashed or forcibly closed controller.
internal sealed class RelayJob : IDisposable {
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits {
        public long PerProcess,PerJob; public uint Flags; public UIntPtr Min,Max; public uint Active; public UIntPtr Affinity; public uint Priority,Scheduling;
    }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong A,B,C,D,E,F; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits {
        public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory,JobMemory,PeakProcessMemory,PeakJobMemory;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes,string name);
    [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr job,int info,ref ExtendedLimits limits,uint size);
    [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    private IntPtr handle;
    internal RelayJob() {
        handle=CreateJobObject(IntPtr.Zero,null);
        ExtendedLimits limits=new ExtendedLimits(); limits.Basic.Flags=0x2000;
        if(handle==IntPtr.Zero || !SetInformationJobObject(handle,9,ref limits,(uint)Marshal.SizeOf(limits))) throw new IOException("Impossible de préparer le relais USB.");
    }
    internal void Assign(Process process) {
        if(!AssignProcessToJobObject(handle,process.Handle)) { process.Kill(); throw new IOException("Impossible de superviser le relais USB."); }
    }
    public void Dispose() { if(handle!=IntPtr.Zero) { CloseHandle(handle); handle=IntPtr.Zero; } }
}




