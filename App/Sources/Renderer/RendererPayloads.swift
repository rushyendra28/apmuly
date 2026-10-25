import ApmulyCore
import Foundation

/// JSON shapes sent to the renderer (see App/Resources/Renderer/js/main.js).
enum RendererPayload {
    struct Lyrics: Encodable {
        struct Word: Encodable {
            let t: String
            let s: TimeInterval
            let e: TimeInterval
        }

        struct Line: Encodable {
            let s: TimeInterval
            let e: TimeInterval
            let t: String
            let w: [Word]
        }

        let trackID: String
        let timing: String
        let lines: [Line]

        init(trackID: String, document: LyricsDocument) {
            self.trackID = trackID
            timing = document.timing.rawValue
            lines = document.lines.map { line in
                Line(s: line.start, e: line.end, t: line.text,
                     w: line.words.map { Word(t: $0.text, s: $0.start, e: $0.end) })
            }
        }
    }

    struct Insights: Encodable {
        struct Object: Encodable {
            let word: String
            let emoji: String?
            let wordIndex: Int?
        }

        struct Line: Encodable {
            let index: Int
            let emoji: String?
            let burst: Bool
            let objects: [Object]
        }

        let trackID: String
        let lines: [Line]

        init(trackID: String, insights: [Int: LineInsight]) {
            self.trackID = trackID
            lines = insights.keys.sorted().compactMap { index in
                insights[index].map { insight in
                    Line(index: index, emoji: insight.emoji, burst: insight.burst,
                         objects: insight.objects.map {
                             Object(word: $0.word, emoji: $0.emoji, wordIndex: $0.wordIndex)
                         })
                }
            }
        }
    }

    struct Artwork: Encodable {
        let trackID: String
        let url: String
    }

    struct Deck: Encodable {
        let trackID: String
        let role: String
        let position: TimeInterval
        let isRunning: Bool
        let opacity: Double
    }

    struct Energy: Encodable {
        let seq: Int
        let bass: Double
        let level: Double
        let bands: [Double]
    }

    /// Per-track info the stage shows when there are no lyrics (visualizer mode).
    struct Track: Encodable {
        let trackID: String
        let title: String
        let artist: String
        /// "synced", "loading" or "none" (no timed lyrics: the stage becomes a visualizer).
        let lyrics: String
    }

    struct Frame: Encodable {
        let decks: [Deck]
        let energy: Energy?
    }
}

/// How lyrics are presented.
enum StageMode: String, CaseIterable, Identifiable {
    /// 3D themed lyrics with emoji and pictures.
    case visualizer
    /// A readable, scrolling lyrics sheet over the theme's (dimmed) 3D scene.
    case lyrics

    var id: String { rawValue }

    var title: String {
        switch self {
        case .visualizer: "Visualizer"
        case .lyrics: "Lyrics"
        }
    }

    var toggled: StageMode { self == .visualizer ? .lyrics : .visualizer }
}
