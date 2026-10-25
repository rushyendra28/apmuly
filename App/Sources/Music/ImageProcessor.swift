import ApmulyCore
import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers

/// Shrinks album artwork into small JPEGs for the renderer and samples its colours.
enum ImageProcessor {
    private static let jpegQuality = 0.8

    /// Downscaled JPEG (longest side ≤ `maxPixel`), or nil if the bytes aren't a decodable image.
    static func jpeg(from data: Data, maxPixel: Int) -> Data? {
        guard let source = CGImageSourceCreateWithData(data as CFData, nil),
              let image = CGImageSourceCreateThumbnailAtIndex(source, 0, [
                  kCGImageSourceCreateThumbnailFromImageAlways: true,
                  kCGImageSourceCreateThumbnailWithTransform: true,
                  kCGImageSourceThumbnailMaxPixelSize: maxPixel,
              ] as CFDictionary)
        else { return nil }
        let output = NSMutableData()
        guard let destination = CGImageDestinationCreateWithData(output, UTType.jpeg.identifier as CFString, 1, nil) else { return nil }
        CGImageDestinationAddImage(destination, image, [kCGImageDestinationLossyCompressionQuality: jpegQuality] as CFDictionary)
        return CGImageDestinationFinalize(destination) ? output as Data : nil
    }

    private static let sampleSide = 32

    /// The song's colours from its artwork, or nil if the bytes aren't a decodable image.
    static func palette(from data: Data) -> ThemePalette? {
        guard let source = CGImageSourceCreateWithData(data as CFData, nil),
              let image = CGImageSourceCreateThumbnailAtIndex(source, 0, [
                  kCGImageSourceCreateThumbnailFromImageAlways: true,
                  kCGImageSourceThumbnailMaxPixelSize: 128,
              ] as CFDictionary),
              let space = CGColorSpace(name: CGColorSpace.sRGB)
        else { return nil }
        let side = sampleSide
        var bytes = [UInt8](repeating: 0, count: side * side * 4)
        let drawn = bytes.withUnsafeMutableBytes { buffer -> Bool in
            guard let context = CGContext(data: buffer.baseAddress, width: side, height: side, bitsPerComponent: 8,
                                          bytesPerRow: side * 4, space: space,
                                          bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
            else { return false }
            context.interpolationQuality = .medium
            context.draw(image, in: CGRect(x: 0, y: 0, width: side, height: side))
            return true
        }
        guard drawn else { return nil }
        let pixels = stride(from: 0, to: bytes.count, by: 4).map { i in
            RGB(r: Double(bytes[i]) / 255, g: Double(bytes[i + 1]) / 255, b: Double(bytes[i + 2]) / 255)
        }
        return ThemePalette.from(pixels: pixels)
    }

    static func dataURL(from data: Data, maxPixel: Int) -> String? {
        jpeg(from: data, maxPixel: maxPixel).map { "data:image/jpeg;base64," + $0.base64EncodedString() }
    }
}
