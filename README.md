# CFT All-in-One Connector

CFT All-in-One Connector is the central desktop application for Core Flight Tech flight simulation hardware.

The project is designed to detect connected Core Flight Tech products, identify the flight simulator currently running, and automatically use the appropriate product and simulator profile. It provides a simple operator interface while keeping configuration lines and the underlying MobiFlight interface out of sight.

## Product vision

One connector should be enough for the complete Core Flight Tech product family. The finished platform will:

- Detect supported Core Flight Tech products automatically.
- Detect the active flight simulator automatically.
- Select the correct device and simulator profile.
- Connect hardware without exposing configuration rows to the user.
- Present device, simulator, and aircraft status in one compact interface.
- Keep diagnostics available through an optional Debug panel.

## Current features

- Automatic Core Flight Tech device and COM-port detection
- Device firmware-name validation
- Simulator detection and automatic connection waiting
- Automatic unified-project selection for MSFS/PMDG 737 and X-Plane/Zibo 737
- Native MobiFlight device scanning and auto-binding behavior
- One-button Connect and Disconnect workflow
- Connect lockout until device lookup and profile initialization are complete
- Automatic Stop/Disconnect when the active device is removed
- Clean engine shutdown and serial-port release when the application closes
- Resizable application window
- Expandable live Debug panel with Pause/Resume, Clear, and Copy controls
- A separate application and engine identity that does not modify the standard MobiFlight installation

## Included unified projects

The distribution contains one embedded multi-device project per supported aircraft:

- `MSFS20_PMDG737.mfproj` — Microsoft Flight Simulator and PMDG 737
- `XP12_ZIBO737.mfproj` — X-Plane and Zibo 737

Each project embeds NAV, COMM, ADF, ATC, EFIS, MCP, and Compact Overhead configurations. The X-Plane 11 NAV workflow was validated in the earlier single-device build; the new unified projects require hardware testing across the included product families.

## How to use

1. Connect the Core Flight Tech device to a USB port.
2. Start a supported simulator and aircraft.
3. Run `CFT All-in-One Connector.exe`.
4. Wait while the application detects the simulator, scans the devices, and initializes the matching unified project.
5. Click **CONNECT** when the button becomes available.
6. Enable **Debug** in the lower-left corner when live engine diagnostics are needed.

## Debug panel

The Debug panel displays the live output of the MobiFlight-based engine.

- **PAUSE / RESUME** pauses only the visible log stream. Hardware operation continues.
- **CLEAR** clears the visible log history.
- **COPY** copies the visible log to the clipboard.
- The main window and Debug panel can be resized from any edge or corner.

## Repository structure

- `work/CFTAllInOneConnectorApp` — compact CFT Windows interface
- `work/CFTAllInOneConnectorEngine` — customized MobiFlight-based engine
- `outputs/CFT-All-in-One-Connector` — runnable development distribution
- `Profiles` — product and simulator configuration profiles
- `Engine` — hidden runtime engine and dependencies

## Architecture notes

The engine is based on MobiFlight Connector. The CFT build runs under a separate application identity and does not change the user's standard MobiFlight installation. MobiFlight's native module lookup and auto-binding flow remains in use.

## License and attribution

MobiFlight Connector is used under the MIT License. The original license text is retained in `LICENSE-MobiFlight.txt` in the distribution and in `work/CFTAllInOneConnectorEngine/LICENSE` in the source tree.

MobiFlight copyright: Copyright (c) 2016-2022 MobiFlight, Sebastian Moebius.
