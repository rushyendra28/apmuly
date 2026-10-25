import Foundation
import UniformTypeIdentifiers
import WebKit

/// Serves the bundled renderer (HTML/JS/fonts) to the web view under `apmuly://renderer/…`.
/// A custom scheme (rather than file://) gives the page a real origin, so ES modules and import maps load.
@MainActor
final class RendererSchemeHandler: NSObject, WKURLSchemeHandler {
    static let scheme = "apmuly"
    static let entryURL = URL(string: "apmuly://renderer/index.html")!

    private let root: URL

    init(root: URL) {
        self.root = root.standardizedFileURL
    }

    func webView(_ webView: WKWebView, start task: any WKURLSchemeTask) {
        guard let url = task.request.url, let file = resolve(url), let data = try? Data(contentsOf: file),
              let response = HTTPURLResponse(url: url, statusCode: 200, httpVersion: "HTTP/1.1", headerFields: [
                  "Content-Type": Self.mimeType(for: file),
                  "Content-Length": String(data.count),
              ])
        else {
            task.didFailWithError(URLError(.fileDoesNotExist))
            return
        }
        task.didReceive(response)
        task.didReceive(data)
        task.didFinish()
    }

    func webView(_ webView: WKWebView, stop task: any WKURLSchemeTask) {}

    /// Maps a request onto a file inside the renderer folder; rejects anything that escapes it.
    private func resolve(_ url: URL) -> URL? {
        let relative = url.path.drop { $0 == "/" }
        guard !relative.isEmpty else { return nil }
        let candidate = root.appendingPathComponent(String(relative)).standardizedFileURL
        guard candidate.path.hasPrefix(root.path + "/") else { return nil }
        return candidate
    }

    private static func mimeType(for file: URL) -> String {
        switch file.pathExtension {
        case "js", "mjs": "text/javascript"
        case "json": "application/json"
        case "html": "text/html; charset=utf-8"
        case "woff2": "font/woff2"
        default: UTType(filenameExtension: file.pathExtension)?.preferredMIMEType ?? "application/octet-stream"
        }
    }
}
