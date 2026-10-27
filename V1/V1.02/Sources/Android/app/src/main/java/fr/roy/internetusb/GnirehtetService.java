/* Derived from Gnirehtet, Copyright 2017 Genymobile, Apache License 2.0.
 * Roy Internet USB modifications, 2026: USB lifecycle, PC lease and HTTPS probe. */
package fr.roy.internetusb;
import android.content.*;
import android.net.*;
import android.os.*;
import java.net.URL;
import java.net.HttpURLConnection;

public class GnirehtetService extends VpnService {
    public static final boolean VERBOSE = false;
    public static volatile String state = "waiting";
    public static volatile String probeResult = "non effectué";
    public static volatile boolean running;
    private final Handler handler = new Handler();
    private final Notifier notifier = new Notifier(this);
    private ParcelFileDescriptor vpn;
    private Forwarder forwarder;
    private long lastHeartbeat;
    private boolean usbConnected;
    private boolean probing;
    private int generation;
    private final BroadcastReceiver usb = new BroadcastReceiver() {
        @Override public void onReceive(Context c, Intent i) {
            usbConnected = i.getBooleanExtra("connected", false);
            if (!usbConnected) { lastHeartbeat = 0; closeTunnel(); }
        }
    };
    private final Runnable checkLease = new Runnable() {
        public void run() {
            if (vpn != null && (!usbConnected || SystemClock.elapsedRealtime() - lastHeartbeat > 16000)) closeTunnel();
            handler.postDelayed(this, 2000);
        }
    };
    public static void command(Context c, String command) {
        Intent i = new Intent(c,GnirehtetService.class).setAction(command);
        if (Build.VERSION.SDK_INT >= 26) c.startForegroundService(i); else c.startService(i);
    }
    public static void start(Context c, VpnConfiguration unused) { command(c,"heartbeat"); }
    public static void stop(Context c) { c.stopService(new Intent(c,GnirehtetService.class)); }
    static Intent createStopIntent(Context c) { return new Intent(c,GnirehtetService.class).setAction("disable"); }
    @Override public void onCreate() {
        super.onCreate(); running = true;
        notifier.start();
        Intent sticky = registerReceiver(usb,new IntentFilter("android.hardware.usb.action.USB_STATE"));
        usbConnected = sticky != null && sticky.getBooleanExtra("connected",false);
        handler.post(checkLease);
    }
    @Override public int onStartCommand(Intent i,int flags,int startId) {
        if (UsbControl.suspended(this) || !getSharedPreferences("usb",0).getBoolean("enabled",false)) { stopSelf(); return START_NOT_STICKY; }
        String action = i == null ? "arm" : i.getAction();
        if ("disable".equals(action)) {
            UsbControl.suspend(this); return START_NOT_STICKY;
        } else if ("suspend".equals(action)) { lastHeartbeat=0; closeTunnel(); }
        else if ("heartbeat".equals(action) && usbConnected) {
            lastHeartbeat = SystemClock.elapsedRealtime();
            if (vpn == null) openTunnel();
        } else if ("probe".equals(action)) probe();
        return START_STICKY;
    }
    private void openTunnel() {
        if (VpnService.prepare(this) != null) return;
        try {
            Builder b = new Builder().setSession("Internet USB V1.02").addAddress("10.0.0.2",32)
                .addRoute("0.0.0.0",0).addDnsServer("1.1.1.1").addDnsServer("8.8.8.8")
                .setBlocking(true).setMtu(16384);
            vpn = b.establish();
            if (vpn == null) { UsbApplication.error("VPN authorization unavailable",null); return; }
            final int current = ++generation;
            state="connecting"; notifier.update();
            Handler events = new Handler(getMainLooper()) {
                @Override public void handleMessage(Message m) {
                    if (vpn == null || current != generation) return;
                    if (m.what == RelayTunnelListener.MSG_RELAY_TUNNEL_CONNECTED) {
                        state="connected"; notifier.update();
                        ConnectivityManager cm=(ConnectivityManager)getSystemService(CONNECTIVITY_SERVICE);
                        for (Network n:cm.getAllNetworks()) {
                            NetworkCapabilities caps=cm.getNetworkCapabilities(n);
                            if (caps!=null && caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN)) { setUnderlyingNetworks(new Network[]{n}); break; }
                        }
                        handler.postDelayed(() -> { if (current==generation && vpn!=null) probe(); },1200);
                    } else if (m.what == RelayTunnelListener.MSG_RELAY_TUNNEL_DISCONNECTED) {
                        state="connecting"; probeResult="connexion interrompue"; notifier.update();
                    }
                }
            };
            forwarder = new Forwarder(this,vpn.getFileDescriptor(),new RelayTunnelListener(events));
            forwarder.forward();
        } catch (Exception e) { UsbApplication.error("Cannot open VPN",e); closeTunnel(); }
    }
    private void closeTunnel() {
        ++generation;
        if (forwarder != null) {
            try { forwarder.stop(); } catch (Exception e) { UsbApplication.error("Cannot stop forwarding",e); }
            forwarder=null;
        }
        if (vpn != null) {
            try { vpn.close(); } catch (Exception e) { UsbApplication.error("Cannot close VPN",e); }
            vpn=null;
        }
        state="waiting"; probeResult="non effectué"; notifier.update();
    }
    private void probe() {
        if (probing || vpn==null || !"connected".equals(state)) return;
        probing=true; probeResult="en cours";
        final int current=generation;
        new Thread(() -> {
            String result;
            HttpURLConnection connection=null;
            try {
                ConnectivityManager cm=(ConnectivityManager)getSystemService(CONNECTIVITY_SERVICE);
                Network network=null;
                for (Network n:cm.getAllNetworks()) {
                    NetworkCapabilities caps=cm.getNetworkCapabilities(n);
                    if (caps!=null && caps.hasTransport(NetworkCapabilities.TRANSPORT_VPN)) { network=n; break; }
                }
                if (network==null) throw new java.io.IOException("No VPN network");
                connection=(HttpURLConnection)network.openConnection(new URL("https://www.google.com/generate_204"));
                connection.setConnectTimeout(8000); connection.setReadTimeout(8000); connection.setUseCaches(false);
                int code=connection.getResponseCode();
                result=code==204 ? "OK via USB" : "réponse HTTP " + code;
                if (code!=204) UsbApplication.error("Internet probe HTTP " + code,null);
            } catch (Exception e) { result="échec (voir errors.log)"; UsbApplication.error("USB Internet probe",e); }
            finally { if (connection!=null) connection.disconnect(); }
            final String value=result;
            handler.post(() -> { probing=false; if (current==generation) { probeResult=value; notifier.update(); } });
        },"USB-Internet-test").start();
    }
    @Override public void onRevoke() {
        getSharedPreferences("usb",0).edit().putBoolean("enabled",false).apply();
        closeTunnel(); stopSelf(); super.onRevoke();
    }
    @Override public void onDestroy() {
        handler.removeCallbacksAndMessages(null); unregisterReceiver(usb);
        closeTunnel(); notifier.stop(); running=false; super.onDestroy();
    }
}



