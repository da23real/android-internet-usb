package fr.roy.internetusb;
import android.app.*;
import android.content.*;
import android.os.Build;

public class Notifier {
    private final Service service;
    public Notifier(Service service) { this.service=service; }
    private Notification notification() {
        Notification.Builder b=Build.VERSION.SDK_INT>=26 ? new Notification.Builder(service,"usb") : new Notification.Builder(service);
        String state=GnirehtetService.state;
        String text="connected".equals(state) ? "Connecté au PC · " + GnirehtetService.probeResult :
            "connecting".equals(state) ? "Connexion USB en cours…" : "Prêt · branche le câble et lance Internet USB sur le PC";
        PendingIntent open=PendingIntent.getActivity(service,0,new Intent(service,MainActivity.class),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
        PendingIntent stop=PendingIntent.getService(service,1,GnirehtetService.createStopIntent(service),PendingIntent.FLAG_UPDATE_CURRENT|PendingIntent.FLAG_IMMUTABLE);
        return b.setContentTitle("Internet USB").setContentText(text).setSmallIcon(R.drawable.ic_usb_24dp)
            .setContentIntent(open).setOngoing(true).setOnlyAlertOnce(true)
            .addAction(R.drawable.ic_close_24dp,"Suspendre tout",stop).build();
    }
    public void start() {
        if (Build.VERSION.SDK_INT>=26) manager().createNotificationChannel(new NotificationChannel("usb","Connexion USB",NotificationManager.IMPORTANCE_LOW));
        service.startForeground(42,notification());
    }
    public void update() { manager().notify(42,notification()); }
    public void stop() { service.stopForeground(true); }
    private NotificationManager manager() { return (NotificationManager)service.getSystemService(Context.NOTIFICATION_SERVICE); }
}


