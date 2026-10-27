package fr.roy.internetusb;

import android.app.Application;
import java.io.File;
import java.io.FileWriter;
import java.io.PrintWriter;
import java.util.Date;

public class UsbApplication extends Application {
    private static File logFile;
    @Override public void onCreate() {
        super.onCreate();
        logFile = new File(getFilesDir(), "errors.log");
        final Thread.UncaughtExceptionHandler previous = Thread.getDefaultUncaughtExceptionHandler();
        Thread.setDefaultUncaughtExceptionHandler((thread, error) -> {
            error("Uncaught " + thread.getName(), error);
            if (previous != null) previous.uncaughtException(thread, error);
        });
    }
    public static synchronized void error(String message, Throwable error) {
        android.util.Log.e("InternetUSB", message, error);
        if (logFile == null) return;
        try (PrintWriter out = new PrintWriter(new FileWriter(logFile, true))) {
            out.println(new Date() + " " + message);
            if (error != null) error.printStackTrace(out);
        } catch (Exception e) { android.util.Log.e("InternetUSB", "Cannot write errors.log", e); }
    }
}
