package ng.firstregistrars.frislscan;

import android.Manifest;
import android.annotation.SuppressLint;
import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.Bitmap;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.view.KeyEvent;
import android.view.View;
import android.view.WindowManager;
import android.webkit.CookieManager;
import android.webkit.JavascriptInterface;
import android.webkit.PermissionRequest;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Toast;

import org.json.JSONArray;

/**
 * FRISL Scan: the web app's /Scan screen in a full-screen app, plus what a browser can't do on a
 * handheld: it receives the hardware trigger key and, once a vendor RFID SDK is added, reads tags
 * directly and hands them to the page (window.frislOnTags).
 */
public class MainActivity extends Activity {

    private static final int FILE_CHOOSER_REQUEST = 1001;
    private static final int CAMERA_PERMISSION_REQUEST = 1002;

    private WebView webView;
    private ValueCallback<Uri[]> fileCallback;
    private PermissionRequest pendingPermission;
    private RfidReader reader;
    private boolean inventoryRunning;
    private final Handler main = new Handler(Looper.getMainLooper());

    @SuppressLint({"SetJavaScriptEnabled", "AddJavascriptInterface"})
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        // Auditors walk around with the screen on.
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);

        webView = new WebView(this);
        webView.setLayoutParams(new android.view.ViewGroup.LayoutParams(
                android.view.ViewGroup.LayoutParams.MATCH_PARENT, android.view.ViewGroup.LayoutParams.MATCH_PARENT));
        setContentView(webView);

        WebSettings s = webView.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);
        s.setDatabaseEnabled(true);
        s.setMediaPlaybackRequiresUserGesture(false);
        s.setAllowFileAccess(false);
        s.setUserAgentString(s.getUserAgentString() + " FrislScanApp/" + BuildConfig.VERSION_NAME);

        CookieManager cookies = CookieManager.getInstance();
        cookies.setAcceptCookie(true);
        cookies.setAcceptThirdPartyCookies(webView, false);

        webView.addJavascriptInterface(new Bridge(), "FrislNative");
        webView.setWebViewClient(new Client());
        webView.setWebChromeClient(new Chrome());

        reader = ReaderFactory.create(this);
        if (reader != null && !reader.open(new ReaderListener())) {
            reader = null;
        }

        if (savedInstanceState != null) {
            webView.restoreState(savedInstanceState);
        } else {
            webView.loadUrl(BuildConfig.START_URL);
        }
    }

    // ------------------------------------------------------------------ hardware trigger

    /**
     * Handheld scan triggers arrive as key events. Their key codes differ by maker, so any key the
     * page doesn't use for typing (scan keys, F-keys, or 280-300) is treated as the trigger.
     */
    private static boolean isTriggerKey(int code) {
        return code == KeyEvent.KEYCODE_BUTTON_L1 || code == KeyEvent.KEYCODE_BUTTON_R1
                || (code >= KeyEvent.KEYCODE_F1 && code <= KeyEvent.KEYCODE_F12)
                || (code >= 280 && code <= 300)
                || code == 139 || code == 501 || code == 520 || code == 521 || code == 522;
    }

    @Override
    public boolean dispatchKeyEvent(KeyEvent event) {
        int code = event.getKeyCode();
        if (isTriggerKey(code)) {
            if (event.getAction() == KeyEvent.ACTION_DOWN && event.getRepeatCount() == 0) {
                onTrigger(true, code);
            } else if (event.getAction() == KeyEvent.ACTION_UP) {
                onTrigger(false, code);
            }
            return true;
        }
        return super.dispatchKeyEvent(event);
    }

    private void onTrigger(boolean down, int keyCode) {
        if (reader != null) {
            if (down && !inventoryRunning) {
                inventoryRunning = true;
                reader.startInventory();
            } else if (!down && inventoryRunning) {
                inventoryRunning = false;
                reader.stopInventory();
            }
        }
        js("window.frislTrigger && window.frislTrigger(" + down + "," + keyCode + "," + (reader != null) + ")");
    }

    private final class ReaderListener implements RfidReader.Listener {
        @Override
        public void onTag(String epc) {
            JSONArray arr = new JSONArray();
            arr.put(epc);
            js("window.frislOnTags && window.frislOnTags(" + arr + ")");
        }

        @Override
        public void onError(String message) {
            main.post(() -> Toast.makeText(MainActivity.this, message, Toast.LENGTH_LONG).show());
        }
    }

    private void js(String script) {
        main.post(() -> webView.evaluateJavascript(script, null));
    }

    /** Exposed to the page as window.FrislNative. */
    private final class Bridge {
        @JavascriptInterface
        public String version() {
            return BuildConfig.VERSION_NAME;
        }

        @JavascriptInterface
        public boolean hasReader() {
            return reader != null;
        }

        @JavascriptInterface
        public String readerName() {
            return reader == null ? "" : reader.name();
        }

        @JavascriptInterface
        public void startInventory() {
            if (reader != null && !inventoryRunning) {
                inventoryRunning = true;
                reader.startInventory();
            }
        }

        @JavascriptInterface
        public void stopInventory() {
            if (reader != null && inventoryRunning) {
                inventoryRunning = false;
                reader.stopInventory();
            }
        }

        @JavascriptInterface
        public void vibrate(int ms) {
            android.os.Vibrator v = (android.os.Vibrator) getSystemService(VIBRATOR_SERVICE);
            if (v != null) v.vibrate(Math.max(10, Math.min(ms, 500)));
        }
    }

    // ------------------------------------------------------------------ web view plumbing

    private final class Client extends WebViewClient {
        @Override
        public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
            Uri uri = request.getUrl();
            if (BuildConfig.SITE_HOST.equalsIgnoreCase(uri.getHost())) {
                return false;
            }
            // Anything outside the asset site opens in the normal browser.
            try {
                startActivity(new Intent(Intent.ACTION_VIEW, uri));
            } catch (ActivityNotFoundException ignored) {
            }
            return true;
        }

        @Override
        public void onPageStarted(WebView view, String url, Bitmap favicon) {
            super.onPageStarted(view, url, favicon);
        }

        @Override
        public void onPageFinished(WebView view, String url) {
            CookieManager.getInstance().flush();
        }

        @Override
        public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
            if (request.isForMainFrame()) {
                showOffline(String.valueOf(error.getDescription()));
            }
        }
    }

    private void showOffline(String reason) {
        String html = "<html><head><meta name='viewport' content='width=device-width,initial-scale=1'>"
                + "<style>body{font-family:sans-serif;background:#f2f5f9;color:#0e182a;display:flex;align-items:center;"
                + "justify-content:center;height:100vh;margin:0;text-align:center}div{padding:24px}"
                + "button{margin-top:18px;height:48px;padding:0 28px;border:0;border-radius:10px;background:#1d3f6d;"
                + "color:#fff;font-size:16px;font-weight:bold}p{color:#5b6676}</style></head><body><div>"
                + "<h2>Can't reach FRISL</h2><p>Check Wi-Fi or mobile data, then try again.</p>"
                + "<p style='font-size:12px'>" + android.text.Html.escapeHtml(reason) + "</p>"
                + "<button onclick=\"location.href='" + BuildConfig.START_URL + "'\">Try again</button></div></body></html>";
        webView.loadDataWithBaseURL(BuildConfig.START_URL, html, "text/html", "utf-8", null);
    }

    private final class Chrome extends WebChromeClient {
        // CSV import from the reader app: open Android's file picker.
        @Override
        public boolean onShowFileChooser(WebView view, ValueCallback<Uri[]> callback, FileChooserParams params) {
            if (fileCallback != null) fileCallback.onReceiveValue(null);
            fileCallback = callback;
            Intent intent = new Intent(Intent.ACTION_GET_CONTENT);
            intent.addCategory(Intent.CATEGORY_OPENABLE);
            intent.setType("*/*");
            try {
                startActivityForResult(Intent.createChooser(intent, "Choose the reader file"), FILE_CHOOSER_REQUEST);
            } catch (ActivityNotFoundException e) {
                fileCallback = null;
                Toast.makeText(MainActivity.this, "No file picker on this device", Toast.LENGTH_LONG).show();
                return false;
            }
            return true;
        }

        // Camera for barcode scanning on the page.
        @Override
        public void onPermissionRequest(PermissionRequest request) {
            main.post(() -> {
                boolean wantsCamera = false;
                for (String r : request.getResources()) {
                    if (PermissionRequest.RESOURCE_VIDEO_CAPTURE.equals(r)) wantsCamera = true;
                }
                if (!wantsCamera) {
                    request.deny();
                    return;
                }
                if (checkSelfPermission(Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) {
                    request.grant(new String[]{PermissionRequest.RESOURCE_VIDEO_CAPTURE});
                } else {
                    pendingPermission = request;
                    requestPermissions(new String[]{Manifest.permission.CAMERA}, CAMERA_PERMISSION_REQUEST);
                }
            });
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == FILE_CHOOSER_REQUEST && fileCallback != null) {
            Uri[] result = null;
            if (resultCode == RESULT_OK && data != null) {
                if (data.getClipData() != null) {
                    int n = data.getClipData().getItemCount();
                    result = new Uri[n];
                    for (int i = 0; i < n; i++) result[i] = data.getClipData().getItemAt(i).getUri();
                } else if (data.getData() != null) {
                    result = new Uri[]{data.getData()};
                }
            }
            fileCallback.onReceiveValue(result);
            fileCallback = null;
        }
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == CAMERA_PERMISSION_REQUEST && pendingPermission != null) {
            if (grantResults.length > 0 && grantResults[0] == PackageManager.PERMISSION_GRANTED) {
                pendingPermission.grant(new String[]{PermissionRequest.RESOURCE_VIDEO_CAPTURE});
            } else {
                pendingPermission.deny();
            }
            pendingPermission = null;
        }
    }

    @Override
    public void onBackPressed() {
        if (webView.canGoBack()) {
            webView.goBack();
        } else {
            super.onBackPressed();
        }
    }

    @Override
    protected void onSaveInstanceState(Bundle outState) {
        super.onSaveInstanceState(outState);
        webView.saveState(outState);
    }

    @Override
    protected void onPause() {
        super.onPause();
        if (reader != null && inventoryRunning) {
            inventoryRunning = false;
            reader.stopInventory();
        }
        CookieManager.getInstance().flush();
    }

    @Override
    protected void onDestroy() {
        if (reader != null) reader.close();
        webView.destroy();
        super.onDestroy();
    }
}
