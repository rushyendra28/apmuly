import ApmulyCore
import SwiftUI

/// Lyrics mode: the whole song as a readable sheet. The current line fills word by word and is
/// kept centred; past lines fade, upcoming lines wait. Emoji from the lyric analysis trail each line.
struct LyricsMachineView: View {
    let model: NowPlayingModel
    var showsNoLyricsCaption = true

    var body: some View {
        TimelineView(.animation) { _ in
            let decks = model.render(at: ProcessInfo.processInfo.systemUptime)
            if let deck = decks.current, case .loaded(.lyrics(let doc)) = deck.lyrics {
                if doc.timing == .unsynced {
                    UnsyncedView(doc: doc)
                } else {
                    LyricsSheet(doc: doc, position: deck.lyricsPosition, insights: model.insights[deck.trackID] ?? [:])
                        .id(deck.trackID)
                }
            } else {
                CurrentStatusOverlay(model: model, showsNoLyricsCaption: showsNoLyricsCaption)
            }
        }
    }
}

private struct LyricsSheet: View {
    let doc: LyricsDocument
    let position: TimeInterval
    let insights: [Int: LineInsight]

    private static let edgeFade = Gradient(stops: [
        .init(color: .clear, location: 0), .init(color: .black, location: 0.18),
        .init(color: .black, location: 0.82), .init(color: .clear, location: 1),
    ])

    var body: some View {
        let current = doc.lineIndex(at: position)
        ScrollViewReader { proxy in
            ScrollView(.vertical) {
                LazyVStack(alignment: .leading, spacing: 22) {
                    Color.clear.frame(height: 240)
                    ForEach(doc.lines.indices, id: \.self) { i in
                        LyricRow(line: doc.lines[i], relation: relation(of: i, current: current),
                                 position: i == current ? position : nil,
                                 emoji: LineDecoration.emoji(for: insights[i]))
                            .id(i)
                    }
                    Color.clear.frame(height: 280)
                }
                .padding(.horizontal, 64)
                .frame(maxWidth: 1000, alignment: .leading)
                .frame(maxWidth: .infinity)
            }
            .scrollIndicators(.hidden)
            .mask(LinearGradient(gradient: Self.edgeFade, startPoint: .top, endPoint: .bottom))
            .onAppear { proxy.scrollTo(current ?? 0, anchor: .center) }
            .onChange(of: current) { _, line in
                withAnimation(.spring(duration: 0.55, bounce: 0.12)) { proxy.scrollTo(line ?? 0, anchor: .center) }
            }
        }
    }

    private func relation(of index: Int, current: Int?) -> LyricRow.Relation {
        guard let current else { return .upcoming }
        return index == current ? .current : index < current ? .past : .upcoming
    }
}

private struct LyricRow: View {
    enum Relation: Equatable { case past, current, upcoming }

    let line: LyricLine
    let relation: Relation
    /// Playback position, only for the current line (drives the word fill).
    let position: TimeInterval?
    let emoji: String

    private static let font = Font.system(size: 36, weight: .heavy, design: .rounded)

    var body: some View {
        content
            .font(Self.font)
            .multilineTextAlignment(.leading)
            .frame(maxWidth: .infinity, alignment: .leading)
            .scaleEffect(relation == .current ? 1 : 0.86, anchor: .leading)
            .blur(radius: relation == .current ? 0 : 0.5)
            .animation(.spring(duration: 0.45), value: relation)
    }

    @ViewBuilder
    private var content: some View {
        if line.isGap {
            Text("♪").foregroundStyle(.white.opacity(relation == .current ? 0.9 : 0.25))
        } else if relation == .current, let position {
            karaoke(at: position)
        } else {
            Text(line.text + trailing).foregroundStyle(.white.opacity(relation == .past ? 0.3 : 0.5))
        }
    }

    private var trailing: String { emoji.isEmpty ? "" : "  " + emoji }

    /// Sung words light up progressively; upcoming words stay dim.
    private func karaoke(at position: TimeInterval) -> Text {
        let words = line.words.enumerated().reduce(Text("")) { text, item in
            let (i, word) = item
            let fill = word.end > word.start ? min(1, max(0, (position - word.start) / (word.end - word.start)))
                : (position >= word.start ? 1 : 0)
            let piece = Text(word.text + (i < line.words.count - 1 ? " " : ""))
                .foregroundStyle(.white.opacity(0.35 + 0.65 * fill))
            return Text("\(text)\(piece)")
        }
        return Text("\(words)\(Text(trailing))")
    }
}
