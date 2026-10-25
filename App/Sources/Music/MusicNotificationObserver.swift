import Foundation

/// Receives Music's `playerInfo` notification immediately, even while Apmuly is in
/// the background (the block-based API lets AppKit hold notifications for inactive apps).
@MainActor
final class MusicNotificationObserver: NSObject {
    private static let name = Notification.Name("com.apple.Music.playerInfo")
    private let onChange: @MainActor () -> Void

    init(onChange: @escaping @MainActor () -> Void) {
        self.onChange = onChange
        super.init()
        DistributedNotificationCenter.default().addObserver(
            self, selector: #selector(received(_:)), name: Self.name, object: nil,
            suspensionBehavior: .deliverImmediately)
    }

    func invalidate() {
        DistributedNotificationCenter.default().removeObserver(self)
    }

    @objc private func received(_ notification: Notification) {
        onChange()
    }
}
