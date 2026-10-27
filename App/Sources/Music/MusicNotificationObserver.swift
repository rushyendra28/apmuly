import Foundation

/// Receives the player apps' change notifications (Music's `playerInfo`, Spotify's
/// `PlaybackStateChanged`) immediately, even while Apmuly is in the background
/// (the block-based API lets AppKit hold notifications for inactive apps).
@MainActor
final class MusicNotificationObserver: NSObject {
    private let onChange: @MainActor () -> Void

    init(names: [String], onChange: @escaping @MainActor () -> Void) {
        self.onChange = onChange
        super.init()
        for name in names {
            DistributedNotificationCenter.default().addObserver(
                self, selector: #selector(received(_:)), name: Notification.Name(name), object: nil,
                suspensionBehavior: .deliverImmediately)
        }
    }

    func invalidate() {
        DistributedNotificationCenter.default().removeObserver(self)
    }

    @objc private func received(_ notification: Notification) {
        onChange()
    }
}
