using System;
using System.Reflection;
using System.Drawing;
using System.Windows.Forms;
using System.IO;

// Off-screen verification of the compiled form; never operates the user's desktop.
internal static class VerifyWindows {
    [STAThread] static int Main(string[] args) {
        try {
            Assembly app=Assembly.LoadFrom(Path.GetFullPath(args[0]));
            Type type=app.GetType("MainForm",true);
            MethodInfo validate=type.GetMethod("ValidateSerial",BindingFlags.Static|BindingFlags.NonPublic);
            foreach(string serial in new string[]{"ANDROID123","abc-def_09.2"}) {
                if((string)validate.Invoke(null,new object[]{serial+"\r\n"})!=serial) throw new Exception("USB serial rejected");
            }
            foreach(string invalid in new string[]{"", "unknown", "192.168.1.2:5555", "emulator:5554", "a b", "a\" -s other", "one\ntwo"}) {
                bool rejected=false;
                try { validate.Invoke(null,new object[]{invalid}); }
                catch(TargetInvocationException e) { rejected=e.InnerException is IOException; }
                if(!rejected) throw new Exception("Unsafe or ambiguous USB identity accepted");
            }
            type.GetField("VerificationMode",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,true);
            MethodInfo parse=type.GetMethod("ExtractProbe",BindingFlags.Static|BindingFlags.NonPublic);
            string value=(string)parse.Invoke(null,new object[]{"Broadcast completed: result=0, data=\"enabled=true;permission=true;state=connected;probe=OK via USB\""});
            if(value!="OK via USB") throw new Exception("Probe parser");
            MethodInfo field=type.GetMethod("Field",BindingFlags.Static|BindingFlags.NonPublic);
            if((string)field.Invoke(null,new object[]{"Broadcast completed: result=0, data=\"protocol=101;requestId=17;mode=midi;pending=true\"","requestId"})!="17") throw new Exception("Request parser");
            MethodInfo matches=type.GetMethod("ModeMatches",BindingFlags.Static|BindingFlags.NonPublic);
            if(!(bool)matches.Invoke(null,new object[]{"mtp","hisuite,mtp,mass_storage,adb"})) throw new Exception("Huawei MTP");
            if(!(bool)matches.Invoke(null,new object[]{"midi","midi,adb"})) throw new Exception("MIDI with ADB");
            if((bool)matches.Invoke(null,new object[]{"none","mtp,adb"})) throw new Exception("Charge incorrectly confirmed");
            Application.EnableVisualStyles();
            using(Form form=(Form)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{false},null)) {
                bool blocked=false;
                try { type.GetMethod("Device",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{"get-state"}); }
                catch(TargetInvocationException e) { blocked=e.InnerException is IOException; }
                if(!blocked) throw new Exception("Command allowed without selected device");
                if(form.Controls.Count<9) throw new Exception("Missing controls");
                form.ShowInTaskbar=false; form.StartPosition=FormStartPosition.Manual; form.Location=new Point(-20000,-20000);
                form.Show(); Application.DoEvents();
                using(Bitmap bitmap=new Bitmap(form.Width,form.Height)) {
                    form.DrawToBitmap(bitmap,new Rectangle(0,0,bitmap.Width,bitmap.Height));
                    bitmap.Save(args[1],System.Drawing.Imaging.ImageFormat.Png);
                }
                ((IDisposable)type.GetField("tray",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form)).Dispose();
                ((IDisposable)type.GetField("job",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form)).Dispose();
            }
            Console.WriteLine("PASS: compiled form, status parser, controls, off-screen render."); return 0;
        } catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}

