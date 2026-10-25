import Foundation

/// Moves each line's words to the singer's actual pace.
///
/// Lyric files usually time whole lines, so word timings start out as a syllable-based guess.
/// Words the singer was heard on are pinned to when they were sung; the others are spread between
/// those anchors by syllable weight, and after the last anchor they continue at the singer's measured
/// pace. Lines sung again (choruses) inherit the pattern learned on another occurrence.
public enum WordRetimer {
    /// Fallback seconds per syllable after the last heard word when the pace is unknown.
    static let defaultPace: TimeInterval = 0.32
    /// Keep the last word starting a little before the line ends.
    private static let lineTail: TimeInterval = 0.1
    private static let minimumGap: TimeInterval = 0.02

    public static func retime(_ document: LyricsDocument, observations: [WordObservation]) -> LyricsDocument {
        guard !observations.isEmpty else { return document }
        let byLine = Dictionary(grouping: observations, by: \.line)

        // Learned relative patterns (seconds after line start), by line text; best-observed wins.
        var patterns: [String: [Int: TimeInterval]] = [:]
        for (index, observed) in byLine where document.lines.indices.contains(index) {
            let line = document.lines[index]
            let anchors = consistentAnchors(observed, in: line)
            let key = TitleNormalizer.key(line.text)
            if !anchors.isEmpty, anchors.count > (patterns[key]?.count ?? 0) {
                patterns[key] = anchors.mapValues { $0 - line.start }
            }
        }

        let lines = document.lines.enumerated().map { index, line -> LyricLine in
            guard !line.isGap, !line.words.isEmpty else { return line }
            var anchors = consistentAnchors(byLine[index] ?? [], in: line)
            if anchors.isEmpty, let pattern = patterns[TitleNormalizer.key(line.text)] {
                anchors = pattern.mapValues { line.start + $0 }.filter { $0.value < line.end - lineTail }
            }
            guard !anchors.isEmpty else { return line }
            return LyricLine(start: line.start, end: line.end, text: line.text, words: place(line, anchors: anchors))
        }
        return LyricsDocument(lines: lines, timing: document.timing)
    }

    /// One time per word (median of repeats), clamped into the line, strictly increasing.
    private static func consistentAnchors(_ observations: [WordObservation], in line: LyricLine) -> [Int: TimeInterval] {
        let latest = line.end - lineTail
        let perWord = Dictionary(grouping: observations.filter { line.words.indices.contains($0.word) }, by: \.word)
            .compactMapValues { obs -> TimeInterval? in
                let time = SyncCalibrator.median(obs.map(\.time))
                return time <= latest ? max(line.start, time) : nil
            }
        var anchors: [Int: TimeInterval] = [:]
        var previous = -TimeInterval.infinity
        for word in perWord.keys.sorted() {
            guard let time = perWord[word], time > previous + minimumGap else { continue }
            anchors[word] = time
            previous = time
        }
        return anchors
    }

    private static func place(_ line: LyricLine, anchors: [Int: TimeInterval]) -> [LyricWord] {
        let words = line.words
        let weights = words.map { Double(WordTimingEstimator.syllables(in: $0.text)) }
        let weight = { (from: Int, to: Int) in weights[from..<to].reduce(0, +) }
        let anchorIndices = anchors.keys.sorted()
        var starts = [TimeInterval](repeating: line.start, count: words.count)

        // Before the first anchor: from the line start.
        let first = anchorIndices[0]
        for i in 0..<first {
            starts[i] = line.start + weight(0, i) / max(weight(0, first), 1e-9) * (anchors[first]! - line.start)
        }
        // Between anchors: spread by syllable weight.
        for (a, b) in zip(anchorIndices, anchorIndices.dropFirst()) {
            for i in a..<b {
                starts[i] = anchors[a]! + weight(a, i) / max(weight(a, b), 1e-9) * (anchors[b]! - anchors[a]!)
            }
        }
        // After the last anchor: continue at the singer's measured pace, compressed to fit the line.
        let last = anchorIndices[anchorIndices.count - 1]
        starts[last] = anchors[last]!
        if last + 1 < words.count {
            let pace = anchorIndices.count >= 2 && weight(first, last) > 0
                ? (anchors[last]! - anchors[first]!) / weight(first, last)
                : defaultPace
            let room = line.end - lineTail - anchors[last]!
            let needed = weight(last, words.count - 1) * pace
            let scale = needed > room && needed > 0 ? room / needed : 1
            for i in (last + 1)..<words.count {
                starts[i] = anchors[last]! + weight(last, i) * pace * scale
            }
        }

        return words.indices.map { i in
            let end = i + 1 < words.count
                ? starts[i + 1]
                : min(line.end, starts[i] + WordTimingEstimator.maxSungDuration(of: words[i].text))
            return LyricWord(text: words[i].text, start: starts[i], end: max(end, starts[i] + minimumGap))
        }
    }
}
