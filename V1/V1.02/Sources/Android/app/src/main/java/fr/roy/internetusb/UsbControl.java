package fr.roy.internetusb;
import android.content.Context;
import android.content.SharedPreferences;
final class UsbControl {
    static SharedPreferences prefs(Context c) { return c.getSharedPreferences("usb",0); }
    static boolean suspended(Context c) { return prefs(c).getBoolean("suspended",false); }
    static void suspend(Context c) {
        prefs(c).edit().putBoolean("suspended",true).putBoolean("pending",false).putBoolean("applying",false)
            .putLong("requestId",prefs(c).getLong("requestId",0)+1)
            .putString("modeResult","Application suspendue · Android gère l’USB").apply();
        GnirehtetService.stop(c);
    }
    static void resume(Context c) { prefs(c).edit().putBoolean("suspended",false).apply(); }
    static void request(Context c,String mode) {
        if(suspended(c)) return;
        prefs(c).edit().putLong("requestId",prefs(c).getLong("requestId",0)+1)
            .putBoolean("applying",false).putString("requestedMode",mode).putBoolean("pending",true)
            .putLong("expires",System.currentTimeMillis()+30000)
            .putString("modeResult","Demande en attente du PC (30 secondes maximum)").apply();
    }
    static void expire(Context c) {
        if((prefs(c).getBoolean("pending",false) || prefs(c).getBoolean("applying",false)) && System.currentTimeMillis()>prefs(c).getLong("expires",0))
            prefs(c).edit().putBoolean("pending",false).putBoolean("applying",false)
                .putString("modeResult","Commande expirée ou non confirmée · vérifie les choix USB Android, puis réessaie").apply();
    }
}

