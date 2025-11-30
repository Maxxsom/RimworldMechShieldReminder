# MechShieldReminder

**MechShieldReminder** is a RimWorld mod that provides tactical awareness by notifying the player when projectile interceptor shields (such as High-shield and Low-shield buildings) go down for recharging and when they come back online.

## 🌟 Features

* **Notifications:**
    * Receives a "Neutral Event" letter when a shield drops (Charging or Cooldown).
    * Receives a notification when a shield is fully recharged and active again.
* **Universal Compatibility:** Automatically detects any `ThingDef` that uses `CompProjectileInterceptor`. This means it works with:
    * Vanilla Shields: High and Low Shields
    * Modded shields: Ones that utilize the standard interception component.
* **Multi-Map Support:** Monitors shields across all active maps simultaneously.
* **Performance Optimized:**
    * Uses a throttled check (runs every 250 ticks) rather than every tick to ensure zero impact on TPS.
    * Includes automatic hash cleanup to prevent memory leaks from destroyed objects.

## 📥 Installation

1.  Download the latest release.
2.  Unzip the folder into your RimWorld `Mods` folder.
3.  Activate the mod in the mod menu.
4.  **Requirement:** This mod requires the **Harmony** library.

## 🏗️ Building From Source

This project targets `.NET Framework 4.8.1`.

### Dependencies
The project uses `Krafs.Rimworld.Ref` and `Lib.Harmony` for automatic reference handling. You do not need to manually locate DLLs if you have a working NuGet connection.

1.  Clone the repository.
2.  Open a terminal in `Source/`.
3.  Run `dotnet build -c Release` or `dotnet build -c Release`.
    1. Windows users might need to comment out the 4.8.1 reference assemblies package for it to build properly.
5.  Output assemblies will be placed in `../Assemblies`.

## 🤝 Compatibility

* **Save Safe:** Can be added or removed mid-game without issues.
* **Mods:** Compatible with any mod that adds shields using the vanilla `CompProjectileInterceptor` class.
