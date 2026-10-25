import CoreAudio
import Foundation

/// How long audio takes from Music to your ears on the current output device
/// (Bluetooth headphones are typically 150–250 ms; built-in speakers a few ms).
enum OutputLatency {
    /// Total output latency in seconds, or nil if it can't be read.
    static func current() -> TimeInterval? {
        guard let device = defaultOutputDevice(),
              let sampleRate: Float64 = property(device, kAudioDevicePropertyNominalSampleRate, scope: kAudioObjectPropertyScopeGlobal),
              sampleRate > 0
        else { return nil }
        let output = kAudioDevicePropertyScopeOutput
        let deviceLatency: UInt32 = property(device, kAudioDevicePropertyLatency, scope: output) ?? 0
        let safetyOffset: UInt32 = property(device, kAudioDevicePropertySafetyOffset, scope: output) ?? 0
        let bufferFrames: UInt32 = property(device, kAudioDevicePropertyBufferFrameSize, scope: output) ?? 0
        let frames = Double(deviceLatency) + Double(safetyOffset) + Double(bufferFrames) + Double(streamLatency(device) ?? 0)
        return frames / sampleRate
    }

    private static func defaultOutputDevice() -> AudioDeviceID? {
        let device: AudioDeviceID? = property(AudioObjectID(kAudioObjectSystemObject), kAudioHardwarePropertyDefaultOutputDevice,
                                              scope: kAudioObjectPropertyScopeGlobal)
        return device.flatMap { $0 == kAudioObjectUnknown ? nil : $0 }
    }

    private static func streamLatency(_ device: AudioDeviceID) -> UInt32? {
        var address = AudioObjectPropertyAddress(mSelector: kAudioDevicePropertyStreams, mScope: kAudioDevicePropertyScopeOutput,
                                                 mElement: kAudioObjectPropertyElementMain)
        var size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(device, &address, 0, nil, &size) == noErr, size >= UInt32(MemoryLayout<AudioStreamID>.size)
        else { return nil }
        var streams = [AudioStreamID](repeating: 0, count: Int(size) / MemoryLayout<AudioStreamID>.size)
        guard AudioObjectGetPropertyData(device, &address, 0, nil, &size, &streams) == noErr, let first = streams.first else { return nil }
        return property(first, kAudioStreamPropertyLatency, scope: kAudioObjectPropertyScopeGlobal)
    }

    private static func property<T>(_ object: AudioObjectID, _ selector: AudioObjectPropertySelector,
                                    scope: AudioObjectPropertyScope) -> T? {
        var address = AudioObjectPropertyAddress(mSelector: selector, mScope: scope, mElement: kAudioObjectPropertyElementMain)
        var size = UInt32(MemoryLayout<T>.size)
        let pointer = UnsafeMutablePointer<T>.allocate(capacity: 1)
        defer { pointer.deallocate() }
        guard AudioObjectGetPropertyData(object, &address, 0, nil, &size, pointer) == noErr else { return nil }
        return pointer.pointee
    }
}
