package fr.roy.internetusb;
import android.content.*;
import android.net.VpnService;
/** Protected by WRITE_SECURE_SETTINGS in the manifest: trusted ADB shell only. */
public class ControlReceiver extends BroadcastReceiver {
    @Override public void onReceive(Context c,Intent i) {
        String command=i.getStringExtra("command");
        SharedPreferences p=UsbControl.prefs(c);
        UsbControl.expire(c);
        boolean accepted=false;
        if("suspendApp".equals(command)) UsbControl.suspend(c);
        if("resumeApp".equals(command)) UsbControl.resume(c);
        boolean suspended=UsbControl.suspended(c);
        long id=i.getLongExtra("requestId",-1);
        if("takeMode".equals(command) && !suspended && p.getBoolean("pending",false) && id==p.getLong("requestId",0)) {
            p.edit().putBoolean("pending",false).putBoolean("applying",true).putLong("expires",System.currentTimeMillis()+30000).putString("modeResult","Changement USB en cours…").apply(); accepted=true;
        }
        if("modeResult".equals(command) && !suspended && id==p.getLong("requestId",0)) {
            String result=i.getStringExtra("result"),mode=p.getString("requestedMode","");
            String label="mtp".equals(mode) ? "transfert de fichiers" : "midi".equals(mode) ? "MIDI" : "charge";
            p.edit().putBoolean("applying",false).putString("modeResult","ok".equals(result) ? "Mode USB confirmé : "+label :
                "Changement USB non confirmé · consulte errors.log ou les réglages Android").apply();
            if(!"ok".equals(result)) UsbApplication.error("USB mode not confirmed",null);
        }
        boolean enabled=p.getBoolean("enabled",false),authorized=VpnService.prepare(c)==null;
        if("suspend".equals(command)) GnirehtetService.stop(c);
        else if(!suspended && enabled && authorized && ("heartbeat".equals(command) || "probe".equals(command)))
            GnirehtetService.command(c,command);
        setResultCode(0);
        setResultData("protocol=101;suspended="+suspended+";enabled="+enabled+";permission="+authorized
            +";pending="+p.getBoolean("pending",false)+";requestId="+p.getLong("requestId",0)
            +";mode="+p.getString("requestedMode","none")+";accepted="+accepted
            +";state="+GnirehtetService.state+";probe="+GnirehtetService.probeResult);
    }
}

