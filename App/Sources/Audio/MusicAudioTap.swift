import Accelerate
import AppKit
import CoreAudio
import Foundation
import os

/// Mono audio from Music, stamped with when its first sample played (systemUptime seconds).
struct AudioChunk: Sendable {
    let hostTime: TimeInterval
    let sampleRate: Double
    let samples: [Float]
}

struct AudioEnergy: Sendable {
    let sequence: Int
    /// RMS of the low-passed (≈ <150 Hz) signal — drives beat detection.
    let bass: Double
    /// Overall RMS.
    let level: Double
    /// Log-spaced spectrum bands, 0…1 (for the visualizer).
    let bands: [Double]
}

/// Listens to Music.app's audio output through a Core Audio process tap (macOS 14.2+)
/// and measures loudness and bass energy for beat-reactive visuals.
/// If the user denies audio capture, the tap delivers silence and visuals fall back to lyric-driven pulses.
final class MusicAudioTap: @unchecked Sendable {
    private static let logger = Logger(subsystem: "io.github.rushyendra28.apmuly", category: "audio-tap")
    private static let bassCutoffHz = 150.0
    private static let checkInterval: TimeInterval = 2
    private static let retryInterval: TimeInterval = 15

    private let lock = NSLock()
    private var sumSquares = 0.0
    private var bassSquares = 0.0
    private var frames = 0
    private var sequence = 0
    private var lowpass = 0.0
    private var alpha = 0.02
    private var sampleRate = 48_000.0
    /// Raw mono samples for the vocal aligner; only collected while it's listening.
    private var collecting = false
    private var pending: [Float] = []
    private var pendingHostTime: TimeInterval?
    private static let maxPendingSeconds = 10.0

    // Spectrum: a rolling window of the latest samples, analysed on demand.
    private static let fftLog2 = 11
    private static let fftSize = 1 << fftLog2
    private static let bandCount = 24
    private static let lowestHz = 40.0
    private static let highestHz = 12_000.0
    /// Display range for band levels (dB relative to full scale).
    private static let floorDB = -72.0
    private static let ceilingDB = -12.0
    private var ring = [Float](repeating: 0, count: fftSize)
    private var ringIndex = 0
    private let fft = vDSP.FFT(log2n: vDSP_Length(fftLog2), radix: .radix2, ofType: DSPSplitComplex.self)
    private let window = vDSP.window(ofType: Float.self, usingSequence: .hanningDenormalized, count: fftSize, isHalfWindow: false)

    // Main-thread state.
    private var tapID = AudioObjectID(kAudioObjectUnknown)
    private var aggregateID = AudioObjectID(kAudioObjectUnknown)
    private var procID: AudioDeviceIOProcID?
    private var runningPID: pid_t?
    private var failedPID: pid_t?
    private var lastCheck = Date.distantPast
    /// The player app whose audio is tapped (Music or Spotify); changing it re-targets the tap.
    var targetBundleID = PlayerApp.music.bundleID {
        didSet { if targetBundleID != oldValue { lastCheck = .distantPast } }
    }
    private var lastFailure = Date.distantPast

    deinit {
        stop()
    }

    /// Runs `body` holding the lock (NSLock.withLock needs macOS 13).
    private func locked<T>(_ body: () -> T) -> T {
        lock.lock()
        defer { lock.unlock() }
        return body()
    }

    /// Starts, restarts (Music relaunched) or stops the tap as needed. Cheap to call often.
    func ensureRunning() {
        // Core Audio process taps arrived in macOS 14.2; earlier systems run without Music's audio.
        guard #available(macOS 14.2, *) else { return }
        guard Date().timeIntervalSince(lastCheck) >= Self.checkInterval else { return }
        lastCheck = Date()
        let pid = NSRunningApplication.runningApplications(withBundleIdentifier: targetBundleID).first?.processIdentifier
        guard pid != runningPID else { return }
        stop()
        guard let pid else { return }
        if pid == failedPID, Date().timeIntervalSince(lastFailure) < Self.retryInterval { return }
        do {
            try start(pid: pid)
            runningPID = pid
            failedPID = nil
            Self.logger.info("Audio tap started for \(self.targetBundleID, privacy: .public) (pid \(pid))")
        } catch {
            failedPID = pid
            lastFailure = Date()
            Self.logger.error("Audio tap failed: \(String(describing: error), privacy: .public)")
            stop()
        }
    }

    /// Energy since the previous call, or nil if no audio arrived.
    func snapshot() -> AudioEnergy? {
        lock.lock()
        guard frames > 0 else {
            lock.unlock()
            return nil
        }
        let n = Double(frames)
        sequence += 1
        let sequence = sequence
        let bass = sqrt(bassSquares / n)
        let level = sqrt(sumSquares / n)
        sumSquares = 0
        bassSquares = 0
        frames = 0
        let latest = Array(ring[ringIndex...] + ring[..<ringIndex])
        let rate = sampleRate
        lock.unlock()
        return AudioEnergy(sequence: sequence, bass: bass, level: level, bands: spectrum(of: latest, sampleRate: rate))
    }

    /// Log-spaced band levels (0…1) of the latest window.
    private func spectrum(of samples: [Float], sampleRate: Double) -> [Double] {
        let half = Self.fftSize / 2
        let windowed = vDSP.multiply(samples, window)
        var real = [Float](repeating: 0, count: half)
        var imaginary = [Float](repeating: 0, count: half)
        var magnitudes = [Float](repeating: 0, count: half)
        real.withUnsafeMutableBufferPointer { realPointer in
            imaginary.withUnsafeMutableBufferPointer { imaginaryPointer in
                var split = DSPSplitComplex(realp: realPointer.baseAddress!, imagp: imaginaryPointer.baseAddress!)
                windowed.withUnsafeBufferPointer { input in
                    input.baseAddress!.withMemoryRebound(to: DSPComplex.self, capacity: half) {
                        vDSP_ctoz($0, 2, &split, 1, vDSP_Length(half))
                    }
                }
                fft?.forward(input: split, output: &split)
                vDSP.absolute(split, result: &magnitudes)
            }
        }
        let binHz = sampleRate / Double(Self.fftSize)
        let ratio = pow(Self.highestHz / Self.lowestHz, 1 / Double(Self.bandCount))
        return (0..<Self.bandCount).map { band in
            let low = Self.lowestHz * pow(ratio, Double(band))
            let lowBin = max(1, Int(low / binHz))
            let highBin = min(half - 1, max(lowBin + 1, Int(low * ratio / binHz)))
            let peak = magnitudes[lowBin..<highBin].max() ?? 0
            let db = 20 * log10(max(1e-9, Double(peak) / Double(Self.fftSize)))
            return min(1, max(0, (db - Self.floorDB) / (Self.ceilingDB - Self.floorDB)))
        }
    }

    /// Turns raw-sample collection (for the vocal aligner) on or off.
    func setCollecting(_ on: Bool) {
        locked {
            collecting = on
            pending.removeAll(keepingCapacity: on)
            pendingHostTime = nil
        }
    }

    /// Samples collected since the previous call, or nil if none.
    func drainSamples() -> AudioChunk? {
        lock.lock()
        defer { lock.unlock() }
        guard let hostTime = pendingHostTime, !pending.isEmpty else { return nil }
        let chunk = AudioChunk(hostTime: hostTime, sampleRate: sampleRate, samples: pending)
        pending.removeAll(keepingCapacity: true)
        pendingHostTime = nil
        return chunk
    }

    func stop() {
        if aggregateID != kAudioObjectUnknown {
            if let procID {
                AudioDeviceStop(aggregateID, procID)
                AudioDeviceDestroyIOProcID(aggregateID, procID)
            }
            AudioHardwareDestroyAggregateDevice(aggregateID)
        }
        if tapID != kAudioObjectUnknown, #available(macOS 14.2, *) {
            AudioHardwareDestroyProcessTap(tapID)
        }
        procID = nil
        aggregateID = AudioObjectID(kAudioObjectUnknown)
        tapID = AudioObjectID(kAudioObjectUnknown)
        runningPID = nil
    }

    // MARK: - Setup

    private struct TapError: Error, CustomStringConvertible {
        let step: String
        let status: OSStatus
        var description: String { "\(step) (OSStatus \(status))" }
    }

    private func check(_ status: OSStatus, _ step: String) throws {
        if status != noErr { throw TapError(step: step, status: status) }
    }

    @available(macOS 14.2, *)
    private func start(pid: pid_t) throws {
        let processObject = try Self.audioObject(for: pid)
        let outputUID = try Self.defaultOutputUID()

        let description = CATapDescription(stereoMixdownOfProcesses: [processObject])
        description.uuid = UUID()
        description.muteBehavior = .unmuted
        description.isPrivate = true
        try check(AudioHardwareCreateProcessTap(description, &tapID), "create tap")

        var format = AudioStreamBasicDescription()
        var size = UInt32(MemoryLayout<AudioStreamBasicDescription>.size)
        var address = Self.address(kAudioTapPropertyFormat)
        try check(AudioObjectGetPropertyData(tapID, &address, 0, nil, &size, &format), "read tap format")
        let sampleRate = format.mSampleRate > 0 ? format.mSampleRate : 48_000
        locked {
            alpha = 1 - exp(-2 * .pi * Self.bassCutoffHz / sampleRate)
            self.sampleRate = sampleRate
        }

        let aggregate: [String: Any] = [
            kAudioAggregateDeviceNameKey: "Apmuly Music Tap",
            kAudioAggregateDeviceUIDKey: UUID().uuidString,
            kAudioAggregateDeviceMainSubDeviceKey: outputUID,
            kAudioAggregateDeviceIsPrivateKey: true,
            kAudioAggregateDeviceIsStackedKey: false,
            kAudioAggregateDeviceTapAutoStartKey: true,
            kAudioAggregateDeviceSubDeviceListKey: [[kAudioSubDeviceUIDKey: outputUID]],
            kAudioAggregateDeviceTapListKey: [[kAudioSubTapDriftCompensationKey: true,
                                               kAudioSubTapUIDKey: description.uuid.uuidString]],
        ]
        try check(AudioHardwareCreateAggregateDevice(aggregate as CFDictionary, &aggregateID), "create aggregate device")

        let queue = DispatchQueue(label: "io.github.rushyendra28.apmuly.audio-tap", qos: .userInteractive)
        try check(AudioDeviceCreateIOProcIDWithBlock(&procID, aggregateID, queue) { [weak self] _, input, inputTime, _, _ in
            self?.accumulate(input, playedAt: Self.uptime(of: inputTime.pointee))
        }, "create IO proc")
        try check(AudioDeviceStart(aggregateID, procID), "start device")
    }

    /// Converts an audio host timestamp to `ProcessInfo.systemUptime` seconds (the app's clock).
    private static func uptime(of stamp: AudioTimeStamp) -> TimeInterval {
        let now = ProcessInfo.processInfo.systemUptime
        guard stamp.mFlags.contains(.hostTimeValid) else { return now }
        let current = AudioConvertHostTimeToNanos(AudioGetCurrentHostTime())
        let stamped = AudioConvertHostTimeToNanos(stamp.mHostTime)
        return now - (Double(current) - Double(stamped)) / 1_000_000_000
    }

    /// Runs on the audio queue.
    private func accumulate(_ input: UnsafePointer<AudioBufferList>, playedAt hostTime: TimeInterval) {
        lock.lock()
        defer { lock.unlock() }
        if collecting {
            if pending.count > Int(sampleRate * Self.maxPendingSeconds) {
                pending.removeAll(keepingCapacity: true)
                pendingHostTime = nil
            }
            if pending.isEmpty { pendingHostTime = hostTime }
        }
        for buffer in UnsafeMutableAudioBufferListPointer(UnsafeMutablePointer(mutating: input)) {
            guard let data = buffer.mData else { continue }
            let channels = max(1, Int(buffer.mNumberChannels))
            let samples = UnsafeBufferPointer(start: data.assumingMemoryBound(to: Float.self),
                                              count: Int(buffer.mDataByteSize) / MemoryLayout<Float>.size)
            var frame = 0
            while frame + channels <= samples.count {
                var mono = 0.0
                for c in 0..<channels { mono += Double(samples[frame + c]) }
                mono /= Double(channels)
                lowpass += alpha * (mono - lowpass)
                sumSquares += mono * mono
                bassSquares += lowpass * lowpass
                frames += 1
                frame += channels
                ring[ringIndex] = Float(mono)
                ringIndex = (ringIndex + 1) & (Self.fftSize - 1)
                if collecting { pending.append(Float(mono)) }
            }
        }
    }

    private static func address(_ selector: AudioObjectPropertySelector) -> AudioObjectPropertyAddress {
        AudioObjectPropertyAddress(mSelector: selector, mScope: kAudioObjectPropertyScopeGlobal,
                                   mElement: kAudioObjectPropertyElementMain)
    }

    @available(macOS 14.2, *)
    private static func audioObject(for pid: pid_t) throws -> AudioObjectID {
        var pid = pid
        var object = AudioObjectID(kAudioObjectUnknown)
        var size = UInt32(MemoryLayout<AudioObjectID>.size)
        var address = address(kAudioHardwarePropertyTranslatePIDToProcessObject)
        let status = AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address,
                                                UInt32(MemoryLayout<pid_t>.size), &pid, &size, &object)
        guard status == noErr, object != kAudioObjectUnknown else { throw TapError(step: "find Music audio object", status: status) }
        return object
    }

    private static func defaultOutputUID() throws -> String {
        var device = AudioDeviceID(kAudioObjectUnknown)
        var size = UInt32(MemoryLayout<AudioDeviceID>.size)
        var address = address(kAudioHardwarePropertyDefaultSystemOutputDevice)
        var status = AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size, &device)
        guard status == noErr else { throw TapError(step: "default output device", status: status) }
        var uid = "" as CFString
        size = UInt32(MemoryLayout<CFString>.size)
        address = Self.address(kAudioDevicePropertyDeviceUID)
        status = withUnsafeMutablePointer(to: &uid) { AudioObjectGetPropertyData(device, &address, 0, nil, &size, $0) }
        guard status == noErr else { throw TapError(step: "output device UID", status: status) }
        return uid as String
    }
}
