import SwiftUI
import AppKit

/// The Syxx app mark — the real ribbon-6 logo, loaded from the bundled `Syxx.icns` so the sidebar
/// matches the app icon and the design canvas. Falls back to a gradient tile if the icon can't load.
///
/// The shipped `Syxx.icns` artwork is not centred inside its own canvas: the mark's alpha bounding
/// box is offset to the right (≈15.6% transparent gutter on the left vs ≈1.6% on the right) and
/// bleeds edge-to-edge top and bottom. Drawn verbatim in a square frame the mark reads as shoved to
/// the right and vertically cramped. `bundledIcon` trims the icon to its opaque bounding box once at
/// load, so every call site renders the mark centred at its true aspect ratio.
struct LogoView: View {
    var size: CGFloat = 40

    var body: some View {
        Group {
            if let icon = Self.bundledIcon {
                Image(nsImage: icon)
                    .resizable()
                    .interpolation(.high)
                    .aspectRatio(contentMode: .fit)
            } else {
                RoundedRectangle(cornerRadius: size * 0.24, style: .continuous)
                    .fill(
                        LinearGradient(
                            colors: [.ioAccentStart, .ioAccentEnd],
                            startPoint: .topLeading, endPoint: .bottomTrailing
                        )
                    )
            }
        }
        .frame(width: size, height: size)
    }

    private static let bundledIcon: NSImage? = {
        for sub in [nil, "Resources"] as [String?] {
            if let url = Bundle.module.url(forResource: "Syxx", withExtension: "icns", subdirectory: sub),
               let img = NSImage(contentsOf: url) {
                return trimmedToOpaqueBounds(img) ?? img
            }
        }
        return nil
    }()

    /// Returns a copy of `image` cropped to its opaque (alpha > threshold) bounding box, so a mark
    /// that sits off-centre in its source canvas renders centred when placed in a square frame.
    /// Returns `nil` if the bounds can't be computed, in which case the caller keeps the original.
    private static func trimmedToOpaqueBounds(_ image: NSImage) -> NSImage? {
        // Probe alpha at a modest resolution — the bounding box is a fraction of the canvas, so a
        // 256×256 sample is precise enough and keeps this one-time computation cheap.
        let probe = 256
        guard let rep = NSBitmapImageRep(
            bitmapDataPlanes: nil, pixelsWide: probe, pixelsHigh: probe,
            bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
            colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0),
              let ctx = NSGraphicsContext(bitmapImageRep: rep) else { return nil }

        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = ctx
        image.draw(in: NSRect(x: 0, y: 0, width: probe, height: probe))
        NSGraphicsContext.restoreGraphicsState()

        guard let data = rep.bitmapData else { return nil }
        let bytesPerRow = rep.bytesPerRow
        let samplesPerPixel = rep.samplesPerPixel
        let alphaThreshold: UInt8 = 5

        // Row 0 in bitmap memory is the TOP scanline of the rendered image.
        var minX = probe, minY = probe, maxX = -1, maxY = -1
        for y in 0..<probe {
            let rowStart = y * bytesPerRow
            for x in 0..<probe {
                let alpha = data[rowStart + x * samplesPerPixel + 3]
                if alpha > alphaThreshold {
                    if x < minX { minX = x }
                    if x > maxX { maxX = x }
                    if y < minY { minY = y }
                    if y > maxY { maxY = y }
                }
            }
        }
        guard maxX >= minX, maxY >= minY else { return nil }

        // Fractional bounds (measured from the top-left of the canvas).
        let fx = CGFloat(minX) / CGFloat(probe)
        let fyTop = CGFloat(minY) / CGFloat(probe)
        let fw = CGFloat(maxX - minX + 1) / CGFloat(probe)
        let fh = CGFloat(maxY - minY + 1) / CGFloat(probe)

        let source = image.size
        // Source rect is in the image's own (bottom-left origin) coordinate space, so flip the
        // top-measured Y: bottom edge = 1 - (top fraction + height fraction).
        let srcRect = NSRect(
            x: fx * source.width,
            y: (1 - fyTop - fh) * source.height,
            width: fw * source.width,
            height: fh * source.height
        )
        guard srcRect.width > 0, srcRect.height > 0 else { return nil }

        // Preserve resolution generously (largest call site is 96pt → 192px @2x).
        let outSize = NSSize(width: fw * 512, height: fh * 512)
        let out = NSImage(size: outSize)
        out.lockFocus()
        NSGraphicsContext.current?.imageInterpolation = .high
        image.draw(in: NSRect(origin: .zero, size: outSize),
                   from: srcRect, operation: .copy, fraction: 1.0)
        out.unlockFocus()
        return out
    }
}

#if DEBUG
#Preview {
    LogoView(size: 96)
        .padding(32)
        .syxxPreviewPanel()
}
#endif
