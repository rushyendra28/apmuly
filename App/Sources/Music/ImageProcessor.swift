import CoreGraphics
import Foundation
import ImageIO
import UniformTypeIdentifiers

/// Shrinks image bytes (album artwork, object photos) into small JPEGs for the renderer.
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

    static func dataURL(from data: Data, maxPixel: Int) -> String? {
        jpeg(from: data, maxPixel: maxPixel).map { "data:image/jpeg;base64," + $0.base64EncodedString() }
    }
}
