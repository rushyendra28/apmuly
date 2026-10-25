# Apmuly developer tasks. Run `make help` for a list.

APP_DEBUG := build/DerivedData/Build/Products/Debug/Apmuly.app

.PHONY: help setup project build run test test-core test-renderer release icon previews clean

help: ## Show available tasks
	@grep -E '^[a-z-]+:.*## ' $(MAKEFILE_LIST) | awk -F ':.*## ' '{printf "  %-14s %s\n", $$1, $$2}'

setup: ## Install build tools (XcodeGen via Homebrew)
	@command -v brew >/dev/null || { echo "Homebrew is required: https://brew.sh"; exit 1; }
	@command -v xcodegen >/dev/null || brew install xcodegen
	@xcodebuild -version | head -1

project: ## Generate Apmuly.xcodeproj from project.yml
	xcodegen generate

build: project ## Build the app (Debug)
	xcodebuild -project Apmuly.xcodeproj -scheme Apmuly -configuration Debug -derivedDataPath build/DerivedData build -quiet

run: build ## Build and launch the app
	open "$(APP_DEBUG)"

test: test-core test-renderer ## Run all tests

test-core: ## Swift tests (lyrics, sync, matching, insights)
	cd Packages/ApmulyCore && swift test

test-renderer: ## JavaScript tests for the 3D renderer logic (Node 22+)
	node --test 'App/RendererTests/*.test.mjs'

release: ## Build a universal Release app and DMG in dist/
	scripts/build-release.sh

icon: ## Regenerate the app icon
	scripts/make-icon.sh

previews: ## Render the stage with placeholder words (needs Google Chrome)
	node scripts/preview-themes.mjs theme-previews 8

clean: ## Remove build output and the generated Xcode project
	rm -rf build dist Apmuly.xcodeproj
