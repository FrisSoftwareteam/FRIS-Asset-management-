package ng.firstregistrars.frislscan;

/**
 * A handheld UHF reader. Implementations wrap a vendor SDK (for example Hopeland's) and report
 * each EPC they read through the listener. Until a vendor SDK is added to app/libs,
 * {@link ReaderFactory} returns null and the app relies on the reader app's CSV export.
 */
public interface RfidReader {

    interface Listener {
        void onTag(String epc);

        void onError(String message);
    }

    /** Human-readable name shown in the app, for example "Hopeland UHF". */
    String name();

    boolean open(Listener listener);

    void startInventory();

    void stopInventory();

    void close();
}
