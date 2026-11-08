package ng.firstregistrars.frislscan;

import android.content.Context;

/**
 * Picks the reader implementation for this device. Hopeland's SDK is not public, so for now there
 * is none and this returns null. When the SDK is supplied: put its .jar/.aar in app/libs, add a
 * HopelandReader implementing {@link RfidReader}, and return it here (see android/README.md).
 */
final class ReaderFactory {

    private ReaderFactory() {
    }

    static RfidReader create(Context context) {
        return null;
    }
}
