import Foundation

/// Where Apmuly keeps its caches: ~/Library/Application Support/apmuly
public enum ApmulyPaths {
    public static var applicationSupport: URL {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
            ?? FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support")
        return base.appendingPathComponent("apmuly", isDirectory: true)
    }
}
