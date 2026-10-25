// Draws Apmuly's app icon (1024×1024 PNG): a dark rounded square, warp streaks radiating from
// the centre, and a chrome-white music note. Used by scripts/make-icon.sh.
// Usage: swift scripts/make-icon.swift <output.png>
import AppKit
import CoreGraphics
import Foundation

let size: CGFloat = 1024
let outputPath = CommandLine.arguments.dropFirst().first ?? "icon_1024.png"

let colorSpace = CGColorSpace(name: CGColorSpace.sRGB)!
guard let ctx = CGContext(data: nil, width: Int(size), height: Int(size), bitsPerComponent: 8, bytesPerRow: 0,
                          space: colorSpace, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else {
    fatalError("Could not create drawing context")
}

func color(_ white: CGFloat, _ alpha: CGFloat = 1) -> CGColor { CGColor(gray: white, alpha: alpha) }

// macOS icon grid: 824pt rounded square centred in 1024 with ~185pt corners.
let tile = CGRect(x: 100, y: 100, width: 824, height: 824)
let tilePath = CGPath(roundedRect: tile, cornerWidth: 185, cornerHeight: 185, transform: nil)
let center = CGPoint(x: size / 2, y: size / 2)

// Soft drop shadow under the tile.
ctx.saveGState()
ctx.setShadow(offset: CGSize(width: 0, height: -12), blur: 30, color: color(0, 0.45))
ctx.addPath(tilePath)
ctx.setFillColor(color(0.05))
ctx.fillPath()
ctx.restoreGState()

// Tile background: deep radial gradient.
ctx.saveGState()
ctx.addPath(tilePath)
ctx.clip()
let background = CGGradient(colorsSpace: colorSpace, colors: [
    CGColor(srgbRed: 0.16, green: 0.17, blue: 0.24, alpha: 1),
    CGColor(srgbRed: 0.03, green: 0.03, blue: 0.06, alpha: 1),
] as CFArray, locations: [0, 1])!
ctx.drawRadialGradient(background, startCenter: center, startRadius: 0, endCenter: center, endRadius: 560, options: [])

// Warp streaks: deterministic pseudo-random rays, brighter and longer further out.
var seed: UInt64 = 0x5EED
func random() -> CGFloat {
    seed = seed &* 6364136223846793005 &+ 1442695040888963407
    return CGFloat((seed >> 33) % 10_000) / 10_000
}
ctx.setLineCap(.round)
for _ in 0..<150 {
    let angle = random() * .pi * 2
    let start = 90 + random() * 330
    let length = 30 + random() * (start * 0.55)
    let width = 2 + random() * 5
    let from = CGPoint(x: center.x + cos(angle) * start, y: center.y + sin(angle) * start)
    let to = CGPoint(x: center.x + cos(angle) * (start + length), y: center.y + sin(angle) * (start + length))
    ctx.setStrokeColor(color(1, 0.12 + 0.5 * min(1, start / 420)))
    ctx.setLineWidth(width)
    ctx.move(to: from)
    ctx.addLine(to: to)
    ctx.strokePath()
}
ctx.restoreGState()

// Music note (beamed pair of eighth notes), built from simple shapes.
let note = CGMutablePath()
func head(_ c: CGPoint) {
    let t = CGAffineTransform(translationX: c.x, y: c.y).rotated(by: -0.42)
    note.addEllipse(in: CGRect(x: -78, y: -54, width: 156, height: 108), transform: t)
}
let leftHead = CGPoint(x: 395, y: 360)
let rightHead = CGPoint(x: 655, y: 410)
head(leftHead)
head(rightHead)
let stem: CGFloat = 46
note.addRect(CGRect(x: leftHead.x + 72 - stem, y: leftHead.y + 10, width: stem, height: 330))
note.addRect(CGRect(x: rightHead.x + 72 - stem, y: rightHead.y + 10, width: stem, height: 330))
let beam = CGMutablePath()
beam.move(to: CGPoint(x: leftHead.x + 72 - stem, y: leftHead.y + 340))
beam.addLine(to: CGPoint(x: rightHead.x + 72, y: rightHead.y + 340))
beam.addLine(to: CGPoint(x: rightHead.x + 72, y: rightHead.y + 340 - 92))
beam.addLine(to: CGPoint(x: leftHead.x + 72 - stem, y: leftHead.y + 340 - 92))
beam.closeSubpath()
note.addPath(beam)

// Chrome look: vertical silver gradient clipped to the note, with a soft glow behind it.
ctx.saveGState()
ctx.setShadow(offset: .zero, blur: 40, color: color(1, 0.35))
ctx.addPath(note)
ctx.setFillColor(color(0.9))
ctx.fillPath()
ctx.restoreGState()

ctx.saveGState()
ctx.addPath(note)
ctx.clip()
let chrome = CGGradient(colorsSpace: colorSpace, colors: [
    color(1), color(0.78), color(0.97), color(0.62), color(0.92),
] as CFArray, locations: [0, 0.35, 0.5, 0.7, 1])!
ctx.drawLinearGradient(chrome, start: CGPoint(x: 0, y: 760), end: CGPoint(x: 0, y: 280), options: [])
ctx.restoreGState()

guard let image = ctx.makeImage(),
      let png = NSBitmapImageRep(cgImage: image).representation(using: .png, properties: [:]) else {
    fatalError("Could not encode PNG")
}
try png.write(to: URL(fileURLWithPath: outputPath))
print("wrote \(outputPath)")
