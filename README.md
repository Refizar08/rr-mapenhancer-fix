# Map Enhancer - Community Fix Edition

[![Version](https://img.shields.io/badge/version-1.5.3.7-blue.svg)](https://github.com/Refizar08/rr-mapenhancer-fix/releases)
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

### Core Map Enhancements
- **Enhanced Junction Markers**: Customizable junction markers with size controls
- **Improved Track Visualization**: Better track coloring and line thickness options
- **Traincar Map Icons**: See all cars on the map, not just locomotives
- **Flare Markers**: Visual flare indicators on the map
- **Advanced Zoom Controls**: Customize min/max zoom levels (50-15000 range)
- **Map Window Resizing**: Flexible map window sizing
- **Signal Visualization**: Color-coded signal icons showing current aspects
- **Location Teleport**: Quick teleport dropdown to major locations
- **Switch Reset Tools**: Quickly reset all switches to normal or thrown position

### Map Controls
- **Toggle Map**: `Z` - Quick toggle map size
- **Follow Mode**: `Ctrl+Z` - Keep map focused on selected locomotive
- **Re-center Map**: `Shift+Z` - Center map on current camera position
- **Place Flares**: Click on map with flare tool active

### Track Color Customization
- **Mainline Tracks**: Default green (RGB 0, 155, 0) - fully customizable
- **Branch/Yard Tracks**: Default Blue - fully customizable
- **Industrial Tracks**: Default yellow or togglable area-based colors - fully customizable
- **Unavailable Tracks**: Customizable color (light grey when industry area colors enabled)
- **Passenger Stop Tracks**: Optional purple highlighting (toggle feature)

### 🆕 New in This Fork

#### **Industry Area Colors** (v1.5.3.7)
- Industrial tracks are now colored by their owning area's color
- Works correctly with both default and modded industries
- Toggle feature in settings (enabled by default)
- Uses area registry lookup for accurate color assignment
- Unreachable tracks shown in light grey to avoid confusion

#### **Visual-Only Track Coloring Mode**
- Track colors are purely visual - doesn't modify underlying track classes
- Prevents conflicts with other mods that depend on track classifications
- Enabled by default

#### **Passenger Stop Tracking**
- Highlight passenger stop track segments in purple
- Toggle feature in settings (disabled by default)
- Requires map reload when toggled

#### **Bug Fixes**
- ✅ Fixed race conditions with track classification from other mods
- ✅ Improved mainline/branch track detection reliability
- ✅ Fixed track color consistency between modes

## ⚙️ Configuration

All settings are avialable at first game launch!

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

### v1.5.3.7 (Latest)
- ✨ Added industry area colors feature with toggle control
- ✨ Industrial tracks now colored by their owning area
- 🐛 Fixed modded industries showing incorrect colors
- 🎨 Changed unreachable track color to light grey when industry colors enabled

### v1.5.3.6
- 🐛 Fixed track class race conditions with other mods
- ✨ Added visual-only track coloring mode
- ✨ Added passenger stop tracking feature
- 🛠️ Improved mainline/industrial track detection

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
