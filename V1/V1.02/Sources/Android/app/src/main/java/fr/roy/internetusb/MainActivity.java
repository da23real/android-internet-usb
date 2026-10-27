package fr.roy.internetusb;
import android.app.Activity;
import android.content.Intent;
import android.net.VpnService;
import android.os.Bundle;
import android.os.Handler;
import android.graphics.Color;
import android.widget.*;
public class MainActivity extends Activity {
    private final Handler handler=new Handler();
    private TextView status, modeStatus;
    private Switch internet;
    private Button suspend, apply, test;
    private RadioGroup modes;
    private boolean refreshing;
    private final Runnable refresh=new Runnable() {
        public void run() { refreshUi(); handler.postDelayed(this,1000); }
    };
    private void updateText(TextView view,String value) {
        if(!value.contentEquals(view.getText())) view.setText(value);
    }
    private void refreshUi() {
        UsbControl.expire(this);
        boolean paused=UsbControl.suspended(this);
        boolean enabled=UsbControl.prefs(this).getBoolean("enabled",false);
        refreshing=true; internet.setChecked(enabled); internet.setEnabled(!paused); refreshing=false;
        updateText(suspend,paused ? "Reprendre l’application" : "Suspendre toute l’application");
        apply.setEnabled(!paused);
        for(int i=0;i<modes.getChildCount();i++) modes.getChildAt(i).setEnabled(!paused);
        test.setEnabled(!paused && "connected".equals(GnirehtetService.state));
        updateText(status,paused ? "Application suspendue · USB libre dans Android" :
            "connected".equals(GnirehtetService.state) ? "Internet connecté par USB" :
            enabled ? "Internet en attente du PC" : "Internet par USB désactivé");
        updateText(modeStatus,UsbControl.prefs(this).getString("modeResult","Choisis un usage puis touche Appliquer.")
            + "\nTest Internet : " + GnirehtetService.probeResult);
    }
    private TextView text(LinearLayout layout,String value,int size) {
        TextView t=new TextView(this); t.setText(value); t.setTextSize(size); layout.addView(t); return t;
    }
    private Button button(LinearLayout layout,String value) {
        Button b=new Button(this); b.setText(value); b.setAllCaps(false); layout.addView(b); return b;
    }
    @Override public void onCreate(Bundle saved) {
        super.onCreate(saved);
        LinearLayout layout=new LinearLayout(this); layout.setOrientation(LinearLayout.VERTICAL);
        int pad=(int)(20*getResources().getDisplayMetrics().density); layout.setPadding(pad,pad,pad,pad);
        layout.setBackgroundColor(Color.rgb(242,246,250));
        text(layout,"Internet USB",28); text(layout,"V1.02 · Android",14); status=text(layout,"",18);
        suspend=button(layout,"Suspendre toute l’application");
        suspend.setOnClickListener(v -> {
            if(UsbControl.suspended(this)) UsbControl.resume(this); else UsbControl.suspend(this); refreshUi();
        });
        button(layout,"Réglages USB dans Android").setOnClickListener(v -> openUsbSettings());
        internet=new Switch(this); internet.setText("Internet du PC par USB"); layout.addView(internet);
        internet.setOnCheckedChangeListener((b,checked) -> {
            if(refreshing) return;
            if(checked) {
                Intent permission=VpnService.prepare(this);
                if(permission!=null) startActivityForResult(permission,7); else enableInternet();
            } else {
                UsbControl.prefs(this).edit().putBoolean("enabled",false).apply(); GnirehtetService.stop(this);
            }
            refreshUi();
        });
        text(layout,"Usage USB à appliquer",20); modes=new RadioGroup(this);
        String[] labels={"Charge (sans fichiers ni MIDI)","Transfert de fichiers","MIDI"};
        for(int i=0;i<labels.length;i++) { RadioButton b=new RadioButton(this); b.setId(100+i); b.setText(labels[i]); modes.addView(b); }
        layout.addView(modes); modes.check(101);
        apply=button(layout,"Appliquer l’usage USB");
        apply.setOnClickListener(v -> {
            int choice=modes.getCheckedRadioButtonId();
            UsbControl.request(this,choice==100 ? "none" : choice==102 ? "midi" : "mtp"); refreshUi();
        });
        modeStatus=text(layout,"",15);
        test=button(layout,"Tester Internet via USB");
        test.setOnClickListener(v -> { if(GnirehtetService.running) GnirehtetService.command(this,"probe"); });
        text(layout,"\nPour appliquer un mode ou partager Internet, lance aussi Internet USB V1.02 sur le PC."
            + "\nInternet peut accompagner les fichiers ou le MIDI. Un changement de mode peut couper brièvement la connexion."
            + "\nLa suspension arrête Internet et les automatismes sans fermer cet écran. Utilise alors les choix USB d’Android."
            + "\nLe débogage USB reste inchangé. Les erreurs sont conservées dans errors.log.",14);
        ScrollView scroll=new ScrollView(this); scroll.addView(layout); setContentView(scroll); refreshUi();
    }
    private void openUsbSettings() {
        Toast.makeText(this,"Dans les options Android, cherche Configuration USB.",Toast.LENGTH_LONG).show();
        Intent[] options={new Intent(android.provider.Settings.ACTION_APPLICATION_DEVELOPMENT_SETTINGS),
            new Intent(android.provider.Settings.ACTION_SETTINGS)};
        for(Intent option:options) {
            try { startActivity(option); return; }
            catch(android.content.ActivityNotFoundException | SecurityException e) { UsbApplication.error("Open USB settings",e); }
        }
        Toast.makeText(this,"Impossible d’ouvrir les réglages Android. Consulte errors.log.",Toast.LENGTH_LONG).show();
    }
    private void enableInternet() {
        if(UsbControl.suspended(this)) return;
        UsbControl.prefs(this).edit().putBoolean("enabled",true).apply(); GnirehtetService.command(this,"arm");
    }
    @Override protected void onActivityResult(int request,int result,Intent data) {
        super.onActivityResult(request,result,data); if(request==7 && result==RESULT_OK) enableInternet(); refreshUi();
    }
    @Override protected void onResume() { super.onResume(); handler.post(refresh); }
    @Override protected void onPause() { handler.removeCallbacks(refresh); super.onPause(); }
}



