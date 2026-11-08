# FRISL Scan – Android app

A small Android app for the Auditor's handheld (Hopeland UHF reader). It opens the web app's
`/Scan` screen full-screen and adds what a browser can't do on the device:

- an app icon, no browser bars, sign-in kept between uses, screen kept on while scanning
- camera barcode scanning and the **Import file from reader** picker (CSV from the UHF app)
- the **hardware trigger key** is passed to the page (`window.frislTrigger(down, keyCode, hasReader)`)
- a slot for the vendor **RFID SDK**: tags read by the SDK are handed to the page
  (`window.frislOnTags([...epc])`) and go straight into the open Audit / Stock check / Look up

## Download

Every push to `main` that changes `android/` builds and publishes:
https://github.com/FrisSoftwareteam/FRIS-Asset-management-/releases/download/apk-latest/frisl-scan.apk

On the handheld: open that link in Chrome, download, tap the file, allow
"Install unknown apps" for Chrome when asked (or turn on *Unknown source application* in
FeatureSettings), then Install.

## Adding the Hopeland SDK (direct trigger reads)

Hopeland's Android UHF SDK isn't public; ask the supplier for the "UHF SDK / API for Android"
for this device (APPCenter shows SDK 3.41). Then:

1. Put the SDK `.jar`/`.aar` (and any `.so` files under `app/src/main/jniLibs/<abi>/`) in `app/libs`.
2. Add `HopelandReader implements RfidReader` using the SDK's connect / start inventory /
   stop inventory / tag callback calls, reporting each EPC (hex) via `listener.onTag(epc)`.
3. Return `new HopelandReader(context)` from `ReaderFactory.create`.

The page already handles `frislOnTags`, so no web change is needed.

## Signing

Builds are signed with a key cached on GitHub Actions so updates install over the previous
version. If that cache is ever cleared, the next APK has a new key and must be installed after
uninstalling the old app. For a permanent key, generate a keystore and set `FRISL_KEYSTORE`,
`FRISL_KEYSTORE_PASSWORD`, `FRISL_KEY_ALIAS`, `FRISL_KEY_PASSWORD` in the workflow from repository
secrets.
