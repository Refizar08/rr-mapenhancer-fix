# Map Enhancer - Community Fix Edition

[![Version](https://img.shields.io/badge/version-1.5.4-blue.svg)](https://github.com/Refizar08/rr-mapenhancer-fix/releases)
[![Game](https://img.shields.io/badge/Railroader-2025-green.svg)](https://store.steampowered.com/app/1683150/Railroader/)

> **Note:** This is a maintained fork by me with fixes and improvements for Map Enhancer. I had pull from the main repository but provide working updates and bug fixes until Vanguard officially updates the original mod.

## 🎯 About This Fork

This version of Map Enhancer is **up-to-date and fully functional** with the latest version of Railroader. While we await official updates from the original author (Vanguard), this fork includes critical bug fixes and new features to keep the mod working smoothly.

**All feedback and contributions are appreciated!**

## 📥 Installation

1. Download the latest release from the [Releases page](https://github.com/Refizar08/rr-mapenhancer-fix/releases)
2. Install the mod in Unity Mod Manager
3. Launch Railroader and check your prefered settings

## ✨ Features

### 🆕 Community Fork Exclusive Features

#### **Cross Traffic Mod Compatibility** (v1.5.4)
- 🔧 Fixed interchange tracks appearing dark gray with LegoTrainMan's Cross Traffic mod
- 🎨 Interchanges now correctly display yellow or proper area colors
- 🛡️ Skips Cross Traffic's global industry area to prevent color conflicts
- ✅ Full compatibility with Cross Traffic functionality maintained

#### **Industry Area Colors** (v1.5.3.7)
- 🎨 Industrial tracks colored by their owning area's color (instead of uniform yellow)
- 🔧 Toggle control in mod settings (enabled by default)
- 🎯 Area registry lookup for accurate industry ownership detection
- ✅ Works correctly with both vanilla and modded industries
- 🌈 Light grey color for unreachable tracks (prevents confusion with red Sylva area)
- 🔄 Position-based fallback if registry lookup fails
- 🛡️ Inactive industry guard prevents disabled industries from affecting track colors

#### **Signal Status Display on Map** (v1.5.3.6)
- 🚦 Real-time signal aspect visualization on map
- 🔴 Red: Stop
- 🟡 Yellow: Approach/Diverging Approach  
- 🟢 Green: Clear/Diverging Clear
- 🟠 Orange: Restricting

#### **Visual-Only Track Coloring Mode** (v1.5.3.6)
- 👁️ Track colors are purely visual - doesn't modify underlying track classes
- 🔌 Prevents conflicts with other mods that depend on track classifications
- ⚙️ Toggle feature in settings (enabled by default)
- 🔄 Compatible with all existing MapEnhancer features

#### **Passenger Stop Tracking** (v1.5.3.6)
- 🟣 Highlight passenger stop track segments in purple
- 🔧 Toggle feature in settings (disabled by default)
- 🎨 Custom color picker for passenger stops

#### **Switch Reset Tools** (v1.5.3.6)
- 🔄 Reset ALL switches to Normal position (straight/mainline route)
- 🔄 Reset ALL switches to Thrown position (diverging route)
- 📍 Accessible via dropdown in map settings panel
- ⚡ Quick bulk operations for track management

#### **Critical Bug Fixes** (v1.5.2.2025-fix)
- ✅ Fixed race condition with track classification
- ✅ Resolved mainline/industrial tracks incorrectly classified as branch
- ✅ Track segments now properly classified after all mods initialize
- ✅ Industrial track coloring preserved correctly
- ✅ Improved compatibility with other map/track mods

### Core Map Enhancements (Original Features)

#### **Enhanced Junction Markers**
- 🎯 Customizable junction markers with size controls
- 🔀 Visual indicators for switch positions (green=normal, red=thrown)
- 🎚️ Adjustable marker scale (0.5 - 1.0)
- 👁️ Auto-hide non-mainline markers at zoom threshold

#### **Advanced Track Visualization**
- 🎨 Fully customizable track colors (mainline, branch, industrial, unavailable)
- 📏 Adjustable track line thickness (0.5 - 2.0)
- 🌈 RGBA color pickers for all track types
- 🎯 Track class-based automatic coloring

#### **Traincar Map Icons**
- 🚃 See ALL cars on the map, not just locomotives
- 📝 Car identification labels (reporting mark + road number)
- 🎨 Freight car color coding by destination area
- 📦 Visual representation scaled by car length
- 👆 Click to inspect car details

#### **Flare Markers**
- 🔥 Visual flare indicators on the map
- 📍 Place flares directly from map view
- 🎚️ Adjustable flare marker scale (0.1 - 1.0)
- 🗑️ Click flares to remove them

#### **Advanced Zoom & View Controls**
- 🔍 Customizable min/max zoom levels (50 - 15000 range)
- 📐 Map window resizing (200 - 800 docked size)
- 🎮 Anti-aliasing options (Off, 2x, 4x, 8x)
- 🎯 Better zoom normalization and icon scaling

#### **Location Teleport System**
- 📍 Quick teleport dropdown to major locations
- 🎨 Color-coded locations by area
- 🗺️ Auto-sorted by area color (hue-based)
- ⚡ Instant camera repositioning

#### **Locomotive Selector**
- 🚂 Dropdown list of all locomotives
- 📋 Sorted by locomotive name
- 👆 Click to center map on selected locomotive
- 🔄 Auto-updates as locomotives are added/removed

### Map Controls

#### **Keyboard Shortcuts**
- `Z` - Toggle map size (docked/fullscreen)
- `Ctrl+Z` - Toggle follow mode (keeps map focused on selected locomotive)
- `Shift+Z` - Re-center map on current camera position
- `Left Click` - Place flare (when flare tool is active)
- `Double Click` - Optional: Require double-click for interactions

#### **Mouse Controls**
- 🖱️ Drag to pan the map
- 🔍 Scroll to zoom in/out
- 👆 Click icons to interact (inspect cars, remove flares, etc.)
- 🎯 Click locomotives/cars to inspect and select

### Track Color System

#### **Default Track Colors**
- 🟢 **Mainline Tracks**: Green (RGB [0, 155, 0], customisable)
- 🔵 **Branch/Yard Tracks**: Blue (Teal, customisable)
- 🟡 **Industrial Tracks**: Yellow (or area color when enabled)
- ⚪ **Unavailable Tracks**: Light grey (or red when industry area colors disabled)
- 🟣 **Passenger Stops**: Purple (when tracking enabled, customisable)

#### **Customization**
- 🎨 Full RGBA color pickers for all track types
- 💾 Colors persist between sessions
- 🔄 Reset to defaults available

### 🆕 What's New in This Fork

This fork includes **ALL original Map Enhancer features** plus community improvements:

- 🔧 **Cross Traffic mod compatibility** - No more gray interchanges (v1.5.4)
- 🎨 **Industry area colors** - Tracks colored by owning area (v1.5.3.7)
- 🚦 **Signal status display** - Real-time signal aspects on map (v1.5.3.6)
- 🟣 **Passenger stop tracking** - Optional purple highlighting (v1.5.3.6)
- 👁️ **Visual-only track colors** - No conflicts with other mods (v1.5.3.6)
- 🔄 **Bulk switch reset tools** - Reset all switches at once (v1.5.3.6)
- ✅ **Critical bug fixes** - Track classification race conditions resolved (v1.5.2)

See detailed feature descriptions below and full changelog in [Version History](#-version-history).

## ⚙️ Configuration

All settings are available and shown when game first launches.

### Display Settings
- **Fusee (Flare) Marker Scale**: 0.1 - 1.0 (default: 0.6)
- **Junction Marker Scale**: 0.5 - 1.0 (default: 0.6)
- **Junction Non-Mainline Marker Cutoff**: 0.01 - 1.0 (default: 0.12)
  - Controls when non-mainline junction markers hide based on zoom level
- **Track Line Thickness**: 0.5 - 2.0 (default: 1.25)
- **Map Window Antialiasing**: Off, 2x, 4x, 8x (default: 4x)

### Map Settings
- **Docked Map Window Size**: 200 - 800 (default: 800)
- **Map Zoom Min**: 50 - 100 (default: 50)
  - Lower values = more zoom in capability
- **Map Zoom Max**: 5000 - 15000 (default: 10000)
  - Higher values = more zoom out capability

### Interaction Settings
- **Require Double Click**: Toggle for double-click requirement on map interactions

### Keyboard Bindings
- **Toggle Map Size**: Default `Z` (customizable)
- **Follow Mode**: Default `Ctrl+Z` (customizable)
- **Re-center Map**: Default `Shift+Z` (customizable)

### Track Colors
Customize colors for all track types with RGBA sliders:
- Mainline Track Color
- Branch/Yard Track Color
- Industry Track Color
- Unavailable Track Color
- Passenger Stop Track Color (when enabled)

### Feature Toggles
- ☑️ **Use Visual-Only Track Coloring** (default: ON)
- ☐ **Enable Passenger Stop Tracking** (default: OFF)
- ☑️ **Enable Industry Area Colors** (default: ON)

## 🐛 Known Issues

- I don't know!

## 💬 Feedback & Support

All feedback, bug reports, and feature requests are welcomed!

- **Discord Forum**: [Map Enhancer Discussion](https://discord.com/channels/795878618697433097/1212693563738034196)
- **Discord**: [Railroader Discord](https://discord.gg/r8xeTPAnhw)
- **Issues**: Report bugs on discord forum or [Create issue](https://github.com/Refizar08/rr-mapenhancer-fix/issues)
- **Maintainer**: reaper8f on Discord

## 🤝 Contributing

Contributions are welcome! If you'd like to help improve Map Enhancer:

## 📜 Version History

### v1.5.4 (2025-10-30) - Latest
- 🔧 Fixed: Interchange tracks appearing dark gray with LegoTrainMan's Cross Traffic mod
- 🛡️ Added: Check to skip Cross Traffic's "legos-global-industries" area identifier
- 🛡️ Added: Guard for inactive industry components
- ✅ Maintains full compatibility with Cross Traffic functionality

### v1.5.3.7 (2025-10-29)
- ✨ Added: Industrial tracks colored by owning area's color (toggle in settings)
- 🎯 Added: Area registry lookup for accurate industry ownership detection
- 🎨 Changed: Unreachable tracks now light grey (prevents confusion with red Sylva area)
- ✅ Fixed: Modded industries showing incorrect colors
- � Added: Position-based fallback detection

### v1.5.3.6 (2025-10-22)
- 🚦 Added: Signal status display on map with color-coded aspects
- 🟣 Added: Passenger stop tracking with toggle (purple highlighting)
- 👁️ Added: Visual-only track coloring mode to prevent mod conflicts
- 🔄 Added: Bulk switch reset tools (All Normal / All Thrown)
- 🐛 Fixed: Track classification issues and branch/passenger track detection

### v1.5.2.2025-fix (2025-10-06)
- 🐛 Fixed: Race condition with track classification
- ✅ Fixed: Mainline/industrial tracks misclassified as branch
- 🔌 Improved: Compatibility with other map/track mods

### Previous Versions
See [Releases](https://github.com/Refizar08/rr-mapenhancer-fix/releases) for complete version history.

## 📋 Credits

- **Original Author**: Vanguard - [Original Nexus Mods Page](https://www.nexusmods.com/railroader/mods/18/)
- **Community Maintainer**: Refizar08 (reaper8f)
- **Contributors**: Thanks to all community members

## 📄 License

This project maintains the same license as the original Map Enhancer mod.

---

**🎮 Happy Railroading!**

*This fork is not officially affiliated with or endorsed by the original Map Enhancer author. It's a community effort to keep the mod functional and improved while we await official updates.*
