// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "ApmulyCore",
    platforms: [.macOS(.v15)],
    products: [
        .library(name: "ApmulyCore", targets: ["ApmulyCore"]),
    ],
    targets: [
        .target(name: "ApmulyCore"),
        .testTarget(name: "ApmulyCoreTests", dependencies: ["ApmulyCore"]),
    ]
)
